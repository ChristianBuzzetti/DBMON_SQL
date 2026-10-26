using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private sealed record CompletionSmokeSegment(int Offset, int Length) : ISegment
    {
        public int EndOffset => Offset + Length;
    }

    private async Task VerifyEditorCompletion(Action<bool, string> require)
    {
        var originalDocument = Editor.Document;
        int originalCaret = Editor.CaretOffset, originalSelectionStart = Editor.SelectionStart, originalSelectionLength = Editor.SelectionLength;
        double originalVertical = Editor.VerticalOffset, originalHorizontal = Editor.HorizontalOffset;
        var originalSavedText = savedText;
        var originalTab = Workspace.SelectedItem;
        var originalEngine = engine;
        var originalDatabases = activeSession.Databases;
        var originalAppearance = appearance;
        var fixture = new WorkspaceSmokeEngine("completion", ServerPlatform.Ase);
        KeyEventArgs KeyArgs(Key key) => new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, key) {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        void Buffer(string sql)
        {
            CloseEditorCompletion(); Editor.Document = new TextDocument(sql); Editor.CaretOffset = sql.Length;
            Editor.TextArea.Focus();
        }
        bool Invoke() => HandleEditorCompletionKey(Key.Space, ModifierKeys.Control, KeyArgs(Key.Space));
        void Press(Key key)
        {
            var args = KeyArgs(key); Editor.TextArea.RaiseEvent(args);
            if (!args.Handled) { args.RoutedEvent = Keyboard.KeyDownEvent; Editor.TextArea.RaiseEvent(args); }
        }
        try
        {
            Workspace.SelectedItem = QueryTab;
            engine = fixture; activeSession.Databases = ["master", "Sales Warehouse", "MASTER", "a]b"];
            ApplyAppearance(appearance with { SqlCompletion = false });
            Buffer("sel");
            require(Invoke() && editorCompletion != null,
                "Ctrl+Space always offers completion even with a previously disabled preference");
            var enabled = appearance with { SqlCompletion = true };
            require(JsonSerializer.Deserialize<AppearanceSettings>(JsonSerializer.Serialize(enabled)) == enabled,
                "The SQL completion preference round-trips through the existing appearance settings");
            CloseEditorCompletion();
            Press(Key.Tab);
            require(Editor.Text.StartsWith("sel") && Editor.Text.Length > 3 && Editor.Text[3..].All(char.IsWhiteSpace),
                "Tab keeps normal indentation without an active popup");

            ApplyAppearance(enabled);
            Buffer("SELECT se");
            require(Invoke() && editorCompletion?.CompletionList.CompletionData.Any(item => item.Text == "select") == true,
                "Ctrl+Space opens local SQL keyword suggestions for a case-insensitive prefix");
            require(editorCompletion!.CompletionList.CompletionData.All(item => item.Text.StartsWith("se", StringComparison.OrdinalIgnoreCase)),
                "Completion suggests prefix matches only");
            Press(Key.Tab);
            require(Editor.Text == "SELECT select" && Editor.CaretOffset == Editor.Text.Length && editorCompletion == null,
                "Tab accepts an active suggestion by replacing only the current prefix and advances the caret");
            Editor.Undo();
            require(Editor.Text == "SELECT se", "One undo restores the prefix before completion");
            Press(Key.Tab);
            require(Editor.Text.StartsWith("SELECT se") && Editor.Text.Length > 9 && Editor.Text[9..].All(char.IsWhiteSpace),
                "Tab still indents when completion is enabled but the popup is absent");

            Buffer("SELECT se"); Invoke(); Press(Key.Escape);
            require(editorCompletion == null && Editor.Text == "SELECT se", "Escape dismisses completion without changing SQL");
            Invoke(); Press(Key.Enter);
            require(editorCompletion == null && Editor.Text == "SELECT select" && fixture.Executions == 0,
                "Enter accepts a suggestion without inserting a newline or executing SQL");
            Press(Key.Enter);
            require(Editor.Text.Contains('\n'), "Enter preserves newline behavior when no completion popup is open");

            foreach (string name in new[] { "sysobjects", "sysalternates", "sysprocedures", "sysprocesses", "mon_dbs" })
            {
                Buffer(name[..^1]); Invoke();
                require(editorCompletion?.CompletionList.CompletionData.Any(item => item.Text == name) == true,
                    "System catalog vocabulary includes " + name);
                editorCompletion!.CompletionList.SelectedItem = editorCompletion.CompletionList.CompletionData.Single(item => item.Text == name);
                Press(Key.Enter);
                require(Editor.Text == name, "System object completion inserts " + name);
            }
            foreach (var sample in new[] {
                ("select * from master..sys", "sysobjects", Key.Tab),
                ("select * from master..mon", "mon_dbs", Key.Enter),
                ("select * from master.dbo.sys", "sysprocesses", Key.Enter),
                ("select * from dbo.sys", "sysalternates", Key.Tab),
                ("select * from [my database]..sys", "sysprocedures", Key.Enter),
                ("select * from master..", "sysobjects", Key.Tab) })
            {
                Buffer(sample.Item1); Invoke();
                require(editorCompletion?.CompletionList.CompletionData.Any(item => item.Text == sample.Item2) == true,
                    "Ctrl+Space offers system objects after a database or owner qualifier: " + sample.Item1);
                require(editorCompletion!.CompletionList.CompletionData.All(item => SqlCompletionSystemObjects.Contains(item.Text)),
                    "Qualified object completion offers system objects instead of keywords or database names");
                editorCompletion.CompletionList.SelectedItem = editorCompletion.CompletionList.CompletionData.Single(item => item.Text == sample.Item2);
                Press(sample.Item3);
                int suffix = sample.Item1.LastIndexOf('.') + 1;
                require(Editor.Text == sample.Item1[..suffix] + sample.Item2,
                    "Qualified completion replaces only the last identifier and preserves its database and owner");
                Editor.Undo();
                require(Editor.Text == sample.Item1, "Undo restores the original qualified object prefix");
            }
            Buffer("sys"); Invoke();
            var firstItem = editorCompletion!.CompletionList.SelectedItem;
            Press(Key.Down); var nextItem = editorCompletion.CompletionList.SelectedItem;
            require(!ReferenceEquals(firstItem, nextItem), "Down selects the next matching system object");
            Press(Key.Up);
            require(ReferenceEquals(firstItem, editorCompletion.CompletionList.SelectedItem), "Up returns to the previous matching system object");
            Press(Key.Down); Press(Key.Tab);
            require(Editor.Text == nextItem.Text, "Tab inserts the system object selected with arrow keys");

            foreach (string sql in new[] { "'se", "'it''s se", "\"se", "[se", "[a]]se", "-- se", "/* se",
                "/* outer /* inner */ se", "@se", ":se", "1se", "dbo.se", "#se", "[name]se", ".sys", "master...sys" })
            {
                Buffer(sql); Invoke();
                require(editorCompletion == null, "Completion is suppressed in non-code or invalid token context: " + sql);
            }
            foreach (string sql in new[] { "-- done\nse", "/* outer /* inner */ done */ se", "'done' + se", "(se" })
            {
                Buffer(sql); Invoke();
                require(editorCompletion != null, "Completion resumes in SQL code after a closed lexical region: " + sql);
            }
            Buffer("select"); Editor.CaretOffset = 2; Invoke();
            require(editorCompletion == null, "Completion never overwrites the leading half of an existing token");
            foreach (string sql in new[] { "-- comment", "/* comment */" })
            {
                Buffer(sql); Editor.CaretOffset = 1; Invoke();
                require(editorCompletion == null, "Completion is suppressed inside an opening comment delimiter");
            }

            Buffer("USE ma"); Invoke();
            require(editorCompletion?.CompletionList.CompletionData.Count(item => item.Text.Equals("master", StringComparison.OrdinalIgnoreCase)) == 1,
                "Database suggestions use the active cache and deduplicate names without a server query");
            Press(Key.Tab);
            require(Editor.Text == "USE MASTER", "Database insertion preserves the deterministically selected cached spelling");
            Buffer("USE Sa"); Invoke(); Press(Key.Tab);
            require(Editor.Text == "USE [Sales Warehouse]", "Database names requiring delimiters are safely bracketed");
            Buffer("USE a"); Invoke();
            editorCompletion!.CompletionList.SelectedItem = editorCompletion.CompletionList.CompletionData.Single(item => item.Text == "a]b");
            Press(Key.Tab);
            require(Editor.Text == "USE [a]]b]", "Database completion escapes closing brackets in cached names");
            Buffer("completion_"); Invoke();
            require(editorCompletion?.CompletionList.CompletionData.Any(item => item.Text == "completion_db") == true,
                "The current database is available from cached session facts");
            Editor.Document = new TextDocument("se"); Editor.CaretOffset = 2;
            require(editorCompletion == null, "Replacing the editor document closes its completion popup");

            Invoke();
            var stale = editorCompletion!.CompletionList.CompletionData.First();
            engine = new DisconnectedQueryEngine(); CloseEditorCompletion();
            stale.Complete(Editor.TextArea, new CompletionSmokeSegment(0, 2), EventArgs.Empty);
            require(Editor.Text == "se", "A completion captured for an earlier engine cannot edit the current session");
            Buffer("ma"); Invoke();
            require(editorCompletion == null && fixture.Executions == 0,
                "An offline session never offers stale database names or runs catalog SQL for completion");

            engine = fixture; Buffer("se"); Invoke();
            ApplyAppearance(enabled with { SqlCompletion = false });
            require(editorCompletion == null && Invoke(),
                "Applying old appearance settings cannot disable completion");
            ApplyAppearance(enabled); Buffer("se"); Invoke();
            var next = new SessionWorkspace { Engine = new WorkspaceSmokeEngine("other", ServerPlatform.Ase), Databases = ["other_db"] };
            var previous = activeSession;
            try
            {
                activeSession = next; Editor.Document = next.Document; Editor.Text = "completion_"; Editor.CaretOffset = Editor.Text.Length;
                require(editorCompletion == null, "A session document switch dismisses the previous completion popup");
                Invoke(); require(editorCompletion == null, "Another session cannot suggest the previous session's database");
                Buffer("other_"); Invoke();
                require(editorCompletion?.CompletionList.CompletionData.Any(item => item.Text == "other_db") == true,
                    "A session switch immediately uses the selected session's cached database names");
            }
            finally { activeSession = previous; }
            CloseEditorCompletion();
            var dialog = new PersonalizationWindow(enabled with { BobertoVisible = false }, ApplyAppearance, testProfile: BobertoProfile) { Owner = this };
            try
            {
                require(!dialog.Toggles.ContainsKey("Text/SQL completion"),
                    "Personalize no longer exposes a SQL completion toggle");
            }
            finally { dialog.Close(); }
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        finally
        {
            CloseEditorCompletion(); engine = originalEngine; activeSession.Databases = originalDatabases;
            Editor.Document = originalDocument; savedText = originalSavedText;
            Editor.Select(originalSelectionStart, originalSelectionLength); Editor.CaretOffset = originalCaret;
            Editor.ScrollToVerticalOffset(originalVertical); Editor.ScrollToHorizontalOffset(originalHorizontal);
            Workspace.SelectedItem = originalTab; ApplyAppearance(originalAppearance);
        }
    }
}
