using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private CompletionWindow? editorCompletion;
    private static readonly string[] SqlCompletionKeywords = (
        "ADD ALL ALTER AND AS ASC BEGIN BETWEEN BY CASE CAST CHECK COMMIT CONVERT CREATE CROSS " +
        "DATABASE DECLARE DEFAULT DELETE DESC DISTINCT DROP ELSE END EXEC EXECUTE EXISTS FROM FULL " +
        "GO GROUP HAVING IF IN INDEX INNER INSERT INTO IS JOIN LEFT LIKE NOT NULL ON OR ORDER OUTER " +
        "PRINT PROCEDURE RETURN RIGHT ROLLBACK SELECT SET TABLE THEN TOP TRAN TRANSACTION TRUNCATE " +
        "UNION UNIQUE UPDATE USE VALUES VIEW WHEN WHERE WHILE").ToLowerInvariant().Split(' ');

    private void AttachEditorCompletion()
    {
        Editor.DocumentChanged += (_, _) => CloseEditorCompletion();
        Editor.TextChanged += (_, _) => CancelTableCompletion();
        Editor.TextArea.Caret.PositionChanged += (_, _) => CancelTableCompletion();
        Editor.TextArea.TextEntering += (_, args) => {
            // Text input stays literal; Tab, Enter or a double-click explicitly accepts an item.
            if (editorCompletion != null && args.Text.Any(c => !CompletionWord(c))) CloseEditorCompletion();
        };
        Editor.TextArea.TextEntered += (_, _) => {
            if (editorCompletion is { } popup &&
                (!TryCompletionPrefix(Editor.Text, Editor.CaretOffset, out _, out var prefix) ||
                 !popup.CompletionList.CompletionData.Any(item => item.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
                CloseEditorCompletion();
        };
        Closing += (_, _) => CloseEditorCompletion();
        Deactivated += (_, _) => CloseEditorCompletion();
    }

    // Routed at the shell before its SQL navigation shortcuts; AvalonEdit still owns ordinary Tab indentation.
    private bool HandleEditorCompletionKey(Key key, ModifierKeys modifiers, KeyEventArgs args)
    {
        if (!Editor.TextArea.IsKeyboardFocusWithin) return false;
        if (key == Key.Space && modifiers == ModifierKeys.Control)
        {
            OpenEditorCompletion(); args.Handled = true; return true;
        }
        if (key == Key.Escape && tableCompletionCancellation != null)
        {
            CloseEditorCompletion(); args.Handled = true; return true;
        }
        if (editorCompletion is not { } popup) return false;
        if (key == Key.Escape)
        {
            CloseEditorCompletion(); args.Handled = true; return true;
        }
        if (key is Key.Tab or Key.Enter && modifiers == ModifierKeys.None)
        {
            popup.CompletionList.RequestInsertion(args); args.Handled = true; return true;
        }
        if (modifiers == ModifierKeys.None && key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            popup.CompletionList.HandleKey(args); return args.Handled;
        }
        // Modified Enter, Shift+Tab and command shortcuts keep their usual meaning.
        if (key is Key.Enter or Key.Tab or Key.F9 || (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
            CloseEditorCompletion();
        return false;
    }

    private void OpenEditorCompletion()
    {
        CloseEditorCompletion();
        if (Editor.SelectionLength != 0 ||
            !TryCompletionPrefix(Editor.Text, Editor.CaretOffset, out int start, out string prefix)) return;
        var owner = activeSession;
        var source = engine;
        var document = Editor.Document;
        bool live = source is not DisconnectedQueryEngine && source is not AseIsql.Odbc.OdbcSession { IsOpen: false };
        if (live && TryTableCompletionTarget(Editor.Text, Editor.CaretOffset, out var target))
        {
            tableCompletionLoad = LoadTableCompletion(owner, source, document, start, prefix, target!);
            return;
        }
        // Keywords and database names still use only facts already owned by this session.
        IEnumerable<string> databases = live ? owner.Databases ?? [] : [];
        if (live && source is ISessionFacts { Facts.Database: { Length: > 0 } current }) databases = databases.Prepend(current);
        bool qualifiedObject = start > 0 && Editor.Text[start - 1] == '.';
        var items = databases.Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (Text: name, IsDatabase: true))
            .Concat(SqlCompletionKeywords.Select(word => (Text: word, IsDatabase: false)))
            .Concat(SqlCompletionSystemObjects.Select(word => (Text: word, IsDatabase: false)))
            .Where(item => !qualifiedObject || !item.IsDatabase && SqlCompletionSystemObjects.Contains(item.Text, StringComparer.Ordinal))
            .DistinctBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .Where(item => item.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Text, StringComparer.OrdinalIgnoreCase).ToArray();
        ShowEditorCompletion(start, prefix, items.Select(item => (ICompletionData)new SqlCompletionItem(item.Text, item.IsDatabase,
            () => ReferenceEquals(activeSession, owner) && ReferenceEquals(engine, source) && ReferenceEquals(Editor.Document, document))));
    }

    private void ShowEditorCompletion(int start, string prefix, IEnumerable<ICompletionData> items)
    {
        var entries = items.ToArray();
        if (entries.Length == 0) return;
        var popup = new CompletionWindow(Editor.TextArea) {
            StartOffset = start, EndOffset = Editor.CaretOffset, Width = 310, MaxHeight = 260,
            CloseWhenCaretAtBeginning = false, FontFamily = Editor.FontFamily, FontSize = Editor.FontSize,
            FontWeight = FontWeights.Normal, ResizeMode = ResizeMode.NoResize
        };
        popup.SetResourceReference(BackgroundProperty, "SurfaceRaised");
        popup.SetResourceReference(ForegroundProperty, "Ink");
        popup.SetResourceReference(BorderBrushProperty, "Rule");
        popup.CompletionList.SetResourceReference(Control.BackgroundProperty, "SurfaceRaised");
        popup.CompletionList.SetResourceReference(Control.ForegroundProperty, "Ink");
        foreach (var item in entries) popup.CompletionList.CompletionData.Add(item);
        editorCompletion = popup;
        popup.Closed += (_, _) => { if (ReferenceEquals(editorCompletion, popup)) editorCompletion = null; };
        popup.Show();
        popup.CompletionList.SelectItem(prefix);
    }

    private void CloseEditorCompletion()
    {
        completionGeneration++;
        CancelTableCompletion();
        var popup = editorCompletion; editorCompletion = null; popup?.Close();
    }

    private static bool CompletionWord(char c) => char.IsLetterOrDigit(c) || c is '_' or '@' or '#' or '$';

    // Scan SQL context only on explicit Ctrl+Space. Nested block comments and escaped delimiters stay opaque.
    private static bool TryCompletionPrefix(string sql, int caret, out int start, out string prefix)
        => TryCompletionPrefix(sql, caret, out start, out prefix, out _);

    private static bool TryCompletionPrefix(string sql, int caret, out int start, out string prefix, out int tokenStart)
    {
        start = caret; prefix = ""; tokenStart = 0;
        if (caret < 0 || caret > sql.Length) return false;
        if (caret > 0 && caret < sql.Length &&
            (sql[caret - 1] == '/' && sql[caret] == '*' || sql[caret - 1] == '-' && sql[caret] == '-')) return false;
        char quote = '\0'; int depth = 0; bool line = false;
        for (int i = 0; i < caret; i++)
        {
            char c = sql[i], next = i + 1 < caret ? sql[i + 1] : '\0';
            if (line) { if (c is '\r' or '\n') { line = false; tokenStart = i + 1; } continue; }
            if (depth > 0)
            {
                if (c == '/' && next == '*') { depth++; i++; }
                else if (c == '*' && next == '/') { depth--; i++; if (depth == 0) tokenStart = i + 1; }
                continue;
            }
            if (quote != '\0')
            {
                if (c == quote) { if (next == quote) i++; else quote = '\0'; }
                continue;
            }
            if (c == '-' && next == '-') { line = true; i++; }
            else if (c == '/' && next == '*') { depth = 1; i++; }
            else if (c is '\'' or '"' or '[') quote = c == '[' ? ']' : c;
            else if (!CompletionWord(c) && c != '.') tokenStart = i + 1;
        }
        if (line || depth > 0 || quote != '\0') return false;
        // Replace only the final identifier; qualifiers must parse using the existing object-name rules.
        if (caret < sql.Length && CompletionWord(sql[caret])) return false;
        while (start > 0 && CompletionWord(sql[start - 1])) start--;
        prefix = sql[start..caret];
        if (prefix.Length > 0 && !(char.IsLetter(prefix[0]) || prefix[0] == '_')) return false;
        if (start > 0 && sql[start - 1] == '.')
            return ParseObjectToken(sql[tokenStart..caret] + (prefix.Length == 0 ? "_" : "")) != null;
        return start == 0 || char.IsWhiteSpace(sql[start - 1]) || "(,;=<>!+-*/%|&^".Contains(sql[start - 1]);
    }

    private sealed class SqlCompletionItem(string text, bool isDatabase, Func<bool> valid,
        string? insertion = null, string? description = null, int replaceBack = 0) : ICompletionData
    {
        public ImageSource? Image => null;
        public string Text => text;
        public object Content => description == null ? text : text + " · " + description;
        public object Description => description ?? (isDatabase ? "Database · cached for this session" :
            SqlCompletionSystemObjects.Contains(text, StringComparer.Ordinal) ? "Sybase system object · local vocabulary; availability depends on server" : "SQL keyword");
        public double Priority => 0;
        public void Complete(TextArea textArea, ISegment segment, EventArgs insertionRequestEventArgs)
        {
            if (!valid() || !TryCompletionPrefix(textArea.Document.Text, textArea.Caret.Offset, out int start, out string prefix) ||
                start != segment.Offset || textArea.Caret.Offset != segment.EndOffset ||
                !text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
            string value = insertion ?? (isDatabase && (!char.IsLetter(text[0]) && text[0] != '_' || text.Any(c => !CompletionWord(c)))
                ? "[" + text.Replace("]", "]]") + "]" : text);
            textArea.Document.Replace(start - replaceBack, segment.Length + replaceBack, value);
            textArea.Caret.Offset = start - replaceBack + value.Length;
        }
    }
}
