using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using AseIsql.Odbc;

namespace AseIsql.Desktop;

public partial class MainWindow : Window
{
    private IQueryEngine engine { get => activeSession.Engine; set => activeSession.Engine = value; }
    private ObservableCollection<LogMessage> log => activeSession.Messages;
    private CancellationTokenSource? running { get => activeSession.Running; set => activeSession.Running = value; }
    private string? documentPath { get => activeSession.Path; set => activeSession.Path = value; }
    private string savedText { get => activeSession.SavedText; set => activeSession.SavedText = value; }
    private bool ready;
    private bool logVisible = true;
    private GridLength savedLogHeight = new(190);
    private readonly SearchPanel search;
    private double firstRenderMs;

    public MainWindow()
    {
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        Messages.ItemsSource = log;
        var messageMenu=new ContextMenu(); var originalMessage=new MenuItem {Header="View original diagnostic"};
        originalMessage.Click+=(_,_)=> { if(Messages.SelectedItem is LogMessage message) TextWindow(message.Time+" · "+message.Source,message.RawText??message.Text); };
        messageMenu.Items.Add(originalMessage); Messages.ContextMenu=messageMenu;
        Messages.PreviewMouseRightButtonDown+=(_,args)=> { var row=FindAncestor<DataGridRow>(args.OriginalSource as DependencyObject); if(row!=null)Messages.SelectedItem=row.Item; };
        search = SearchPanel.Install(Editor.TextArea);
        Editor.PreviewMouseWheel += EditorWheel;
        AttachEditorMenu();
        AttachEditorCompletion();
#if DBMON_SMOKE
        if(isSmoke) engine=ShowcaseDirectory!=null?new ShowcaseEngine():new DemoQueryEngine();
        Editor.Text = isSmoke && ShowcaseDirectory==null ? DemoSql : "";
#else
        Editor.Text = "";
#endif
        savedText = Editor.Text;
        InitializeSessions();
        LoadRunSettings();
        ready = true;
        InitializeAppearance();
        SourceInitialized += (_, _) => Themes.ApplyChrome(this, Themes.Current);
        Editor.TextArea.Caret.PositionChanged += (_, _) => PositionText.Text = $"Ln {Editor.TextArea.Caret.Line}, Col {Editor.TextArea.Caret.Column}";
        PreviewKeyDown += HandleKey;
        QueryTab.GotKeyboardFocus += (_,args)=> {if(args.OriginalSource==QueryTab)Dispatcher.BeginInvoke(new Action(()=>Editor.TextArea.Focus()),System.Windows.Threading.DispatcherPriority.Input);};
        ContentRendered += (_, _) => {
            if (firstRenderMs != 0) return;
            using var process = Process.GetCurrentProcess();
            firstRenderMs = (DateTime.Now - process.StartTime).TotalMilliseconds;
            string[] args = Environment.GetCommandLineArgs();
            int reportIndex = Array.IndexOf(args, "--startup-report");
            if (reportIndex >= 0 && reportIndex + 1 < args.Length)
            {
                File.WriteAllText(args[reportIndex + 1], System.Text.Json.JsonSerializer.Serialize(new { firstRenderMs, cpuMs = process.TotalProcessorTime.TotalMilliseconds, peakWorkingSetBytes = process.PeakWorkingSet64 }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                Application.Current.Shutdown();
            }
        };
#if DBMON_SMOKE
        Loaded += async (_, _) => {
#else
        Loaded += (_, _) => {
#endif
            Editor.Focus(); RefreshConnection(); LoadHistory();
#if DBMON_SMOKE
            if (IsShowcase) await RunShowcase();
            else if (isSmoke) await VerifySmoke();
            else
#endif
            if(!Environment.GetCommandLineArgs().Any(arg=>arg is "--startup-report" or "--ui-preview")) { ConnectClick(this,new RoutedEventArgs()); if(engine is not OdbcSession) { AddMessage(0,"CLIENT","Working offline. SQL can be edited and saved; connect with F11 to execute."); StatusText.Text="Disconnected · connect with F11 to execute SQL"; } }
            // After the login (which starts Messages afresh), so the one-time move stays visible.
            if (
#if DBMON_SMOKE
                !isSmoke &&
#endif
                DataFolder.MigrationNote is { } moved) AddMessage(0, "CLIENT", moved);
            if (historyWarning != null) AddMessage(0, "CLIENT", historyWarning);
            if (runSettingsWarning != null) AddMessage(0, "CLIENT", runSettingsWarning);
        };
    }

    private void HandleKey(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (HandleEditorCompletionKey(key, modifiers, e)) return;
        if (key == Key.F9 && modifiers == ModifierKeys.None) { e.Handled = true; _ = RunPressedAsync(); }
        else if (key == Key.F9 && modifiers == ModifierKeys.Alt) { e.Handled = true; _ = ExecuteAsync(true); }
        else if (key == Key.F9 && modifiers == ModifierKeys.Shift) { e.Handled = true; MultiRunClick(this, e); }
        else if (key == Key.F9 && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { e.Handled = true; RepeatClick(this, e); }
        else if (key == Key.F4 && modifiers == ModifierKeys.None) { BrowserClick(this, e); e.Handled = true; }
        else if (key == Key.F11 && modifiers == ModifierKeys.None) { ConnectClick(this, e); e.Handled = true; }
        else if (key == Key.F12 && modifiers == ModifierKeys.None) { DisconnectClick(this, e); e.Handled = true; }
        else if (key == Key.F2 && modifiers == ModifierKeys.Control) { ToggleBookmark(); e.Handled=true; }
        else if (key == Key.F2 && modifiers is ModifierKeys.None or ModifierKeys.Shift) { GoBookmark(modifiers==ModifierKeys.Shift); e.Handled=true; }
        // Esc ends a repeat as well as the run in progress: nothing keeps firing after the user asked it to stop.
        else if (key == Key.Escape && (running != null || repeat != null)) { if (repeat != null) StopRepeat("Stopped with Esc."); if (running != null) _ = running.CancelAsync(); e.Handled = true; }
        else if (key == Key.Up && modifiers == ModifierKeys.None && Editor.TextArea.IsKeyboardFocusWithin && MoveCaretToStartOfFirstLine()) e.Handled = true;
        else if (key == Key.Down && modifiers == ModifierKeys.None && Editor.TextArea.IsKeyboardFocusWithin && MoveCaretToEndOfLastLine()) e.Handled = true;
        else if (key is Key.Up or Key.Down && modifiers == ModifierKeys.Shift && Editor.TextArea.IsKeyboardFocusWithin && ExtendSelectionToDocumentEdge(key == Key.Up)) e.Handled = true;
        else if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.N: NewClick(this, e); break;
                case Key.O: OpenClick(this, e); break;
                case Key.S: SaveClick(this, e); break;
                case Key.R: HistoryClick(this, e); break;
                case Key.Q: QueryClick(this, e); break;
                case Key.E: _ = ExecuteAsync(selectionOnly: true); break;
                case Key.K: PaletteClick(this, e); break;
                case Key.H: ReplaceClick(this, e); break;
                case Key.Add: case Key.OemPlus: Zoom(1); break;
                case Key.Subtract: case Key.OemMinus: Zoom(-1); break;
                case Key.D0: ZoomResetClick(this,e); break;
                default: return;
            }
            e.Handled = true;
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.S) { SaveAsClick(this, e); e.Handled = true; }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.D) { DuplicateLineClick(this,e); e.Handled=true; }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.P) { FillParametersClick(this,e); e.Handled=true; }
        else if (modifiers == ModifierKeys.Alt && key == Key.Z) { Editor.WordWrap = !Editor.WordWrap; e.Handled=true; }
    }

    // AvalonEdit ignores Shift+Up on the first line and Shift+Down on the last; editors extend the selection to the
    // document edge there. Every other line keeps AvalonEdit's native column-based movement.
    private bool ExtendSelectionToDocumentEdge(bool up)
    {
        var area = Editor.TextArea;
        if (up ? area.Caret.Line != 1 : area.Caret.Line != Editor.Document.LineCount) return false;
        int caret = area.Caret.Offset, target = up ? 0 : Editor.Document.TextLength;
        if (caret == target) return true;
        var segment = area.Selection.SurroundingSegment;
        int anchor = area.Selection.IsEmpty || segment == null ? caret : segment.Offset == caret ? segment.EndOffset : segment.Offset;
        area.Caret.Offset = target;
        area.Selection = ICSharpCode.AvalonEdit.Editing.Selection.Create(area, anchor, target);
        return true;
    }

    // AvalonEdit leaves the caret where it is when Up is pressed on the first line. Move it to the start of that line.
    // A wrapped first line still steps through its visual rows; only the first row jumps to the line start.
    private bool MoveCaretToStartOfFirstLine()
    {
        var area = Editor.TextArea;
        var document = Editor.Document;
        if (area.Caret.Line != 1) return false;
        if (Editor.WordWrap)
        {
            var visual = area.TextView.GetOrConstructVisualLine(document.GetLineByNumber(1));
            if (visual.TextLines.Count > 1 && visual.GetTextLine(area.Caret.VisualColumn) != visual.TextLines[0]) return false;
        }
        int target = document.GetLineByNumber(1).Offset;
        if (area.Caret.Offset == target && area.Selection.IsEmpty) return true;
        area.ClearSelection();
        area.Caret.Offset = target;
        return true;
    }

    // AvalonEdit leaves the caret where it is when Down is pressed on the last line. Move it to the end of that line.
    // A wrapped last line still steps through its visual rows; only the final row jumps to the line end.
    private bool MoveCaretToEndOfLastLine()
    {
        var area = Editor.TextArea;
        var document = Editor.Document;
        if (area.Caret.Line != document.LineCount) return false;
        if (Editor.WordWrap)
        {
            var visual = area.TextView.GetOrConstructVisualLine(document.GetLineByNumber(area.Caret.Line));
            if (visual.TextLines.Count > 1 && visual.GetTextLine(area.Caret.VisualColumn) != visual.TextLines[^1]) return false;
        }
        int target = document.GetLineByNumber(document.LineCount).EndOffset;
        if (area.Caret.Offset == target && area.Selection.IsEmpty) return true;
        area.ClearSelection();
        area.Caret.Offset = target;
        return true;
    }
    private Task ExecuteAsync(bool lineOnly = false, bool selectionOnly = false, string? sqlOverride = null, string? label = null, bool repeatRun = false, int? replaceRun = null, bool noTimeout = false)
        => ExecuteAsync(activeSession, lineOnly, selectionOnly, sqlOverride, label, repeatRun, replaceRun, noTimeout);

    // One session's execution. The editor is read while that session is on screen; everything after the await is
    // published into the owning workspace, so another server's tab can be used, and run, while this one works.
    // noTimeout runs this once without the profile's query timeout (maintenance); Esc still cancels it.
    private async Task ExecuteAsync(SessionWorkspace owner, bool lineOnly = false, bool selectionOnly = false, string? sqlOverride = null, string? label = null, bool repeatRun = false, int? replaceRun = null, bool noTimeout = false)
    {
        if (owner.Running != null || owner.Changing || owner.CatalogActions != 0) return;
        if (!repeatRun && owner.Repeat != null) { SetStatus(owner, "Stop repeating before starting another execution in this session. The repeat is still active."); return; }
        bool mounted = IsOnScreen(owner);
        // The editor is only readable while this session is mounted; a background run always carries its own SQL.
        if (sqlOverride == null && !mounted) { SetStatus(owner, "Open this session's tab to run its editor."); return; }
        owner.LastRunFailed = true;
        // Offline sessions never execute: the attempt is reported, and connecting stays an explicit action.
        if (owner.Engine is DisconnectedQueryEngine)
        {
            ClearMessages(owner);
            AddMessage(owner, 1, "CLIENT", "Not connected. SQL was not executed; connect with F11 or the session button, then run it again.");
            SetStatus(owner, "Disconnected · SQL was not executed"); ShowMessages(owner); return;
        }
        ClearMessages(owner);
        if (sqlOverride == null && selectionOnly && Editor.SelectionLength == 0)
        {
            AddMessage(owner, 1, "CLIENT", "Ctrl+E: select SQL with Shift+arrows before executing.");
            SetStatus(owner, "No selection · SQL was not executed"); Workspace.SelectedItem = QueryTab; Editor.Focus(); ShowMessages(owner); return;
        }
        string sql = sqlOverride ?? (lineOnly ? Editor.Document.GetText(Editor.Document.GetLineByNumber(Editor.TextArea.Caret.Line)) :
            Editor.SelectionLength > 0 ? Editor.SelectedText : Editor.Text);
        // Named before the await, while the editor still belongs to this session.
        string scope = label ?? (lineOnly ? "current line" : Editor.SelectionLength > 0 ? "selection" : "script");
        if (sqlOverride == null)
        {
            // :name placeholders ask for values; the editor keeps them, only this run uses the filled text.
            if (FillParameters(sql, lineOnly ? "Current line" : Editor.SelectionLength > 0 ? "Selection" : "Query", "Run") is not { } filled)
            { AddMessage(owner, 0, "CLIENT", "Run cancelled while filling parameters. SQL was not executed."); SetStatus(owner, "Cancelled · parameters"); return; }
            sql = filled;
        }
        // A repeat was confirmed once when it started; every other run on a PROD session that changes state asks now.
        if (!repeatRun && !ConfirmProduction(owner, sql, "This " + scope))
        {
            AddMessage(owner, 0, "CLIENT", "Not run: cancelled at the PROD confirmation. Nothing was sent to the server.");
            SetStatus(owner, "Cancelled · PROD confirmation"); ShowMessages(owner); return;
        }
        using var cancellation = new CancellationTokenSource();
        owner.ExecutingSql = sql;
        owner.Running = cancellation;
        RefreshFor(owner);
        var executingEngine = owner.Engine;
        string runningName = executingEngine.Name;
        SetStatus(owner, "Executing · " + runningName + " · Esc cancels");
        AddMessage(owner, 0, SourceLabelOf(owner), "Execute · " + scope + (noTimeout ? " · no timeout" : "") + (runSettings.ContinueOnError ? " · continue after errors" : ""));
        if (noTimeout) AddMessage(owner, 0, "CLIENT", owner.Profile?.QueryTimeout == 0 ? "This profile has no query timeout: the run lasts until it ends or Esc cancels it." : "No query timeout for this run (maintenance): it runs until it ends or Esc cancels it.");
        ApplyRunSettings(executingEngine);
        var messages = new SessionMessageProgress(owner);
        var timer = Stopwatch.StartNew();
        // One dispatcher refresh per tick, even when the driver produces thousands of messages between ticks.
        var ticker = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        ticker.Tick += (_, _) => {
            RefreshMessages(owner);
            SetStatus(owner, $"Executing · {timer.Elapsed.TotalSeconds:F1} s · {runningName} · Esc cancels");
        };
        ticker.Start();
        try
        {
            var output = await Task.Run(() => noTimeout && executingEngine is OdbcSession odbc ? odbc.Execute(sql, cancellation.Token, userRun: true, timeoutSeconds: 0, messages: messages) : executingEngine.Execute(sql, cancellation.Token, messages), cancellation.Token);
            double elapsedMs = timer.Elapsed.TotalMilliseconds; ticker.Stop();
            if (!messages.Reported) AddOutputMessages(owner, output);
            else RefreshMessages(owner);
            int rowCount = output.Tables.Sum(t => t.Rows.Count);
#if DBMON_SMOKE
            if (owner.Engine is DemoQueryEngine) cancellation.Token.ThrowIfCancellationRequested();
#endif
            // Results accumulate; a re-run replaces only its own earlier tabs.
            var run = NewRun(owner, sql, owner.Engine); owner.LastRunId = run.Id;
            CaptureRunText(owner, run, output);
            var tablesToPublish = repeatRun ? ProcessRepeatMdaDeltas(owner, output.Tables) : output.Tables;
            PublishResults(owner, tablesToPublish, run, replaceRun);
            // A repeat records its SQL once when it starts, not on every run.
            if (!repeatRun) RecordHistory(sql);
            owner.LastRunFailed = output.Error != null;
            if (output.Error != null) { AddMessage(owner, 1, output.ErrorSource, output.Error, output.ErrorRawText); ShowMessages(owner); }
            else AddMessage(owner, 0, SourceLabelOf(owner), $"Done · {Counts.Of(tablesToPublish.Count, "result")} · {Counts.Of(tablesToPublish.Sum(t => t.Rows.Count), "row")} · {Elapsed(elapsedMs)}");
            // An empty run under NOEXEC looks like a broken server; say which option produced it, every time.
            if (owner.Options.NoExec && owner.Engine is AseIsql.Odbc.OdbcSession { Platform: ServerPlatform.Ase }) AddMessage(owner, 0, "CLIENT", "NOEXEC is on: the server compiled this script and executed none of it. Tools › SAP ASE session options turns it off."
                + (owner.Options.StatisticsSilencedByNoExec ? " Statistics IO and statistics time measure execution, so they report nothing until it is off." : ""));
            // The new result becomes this session's selection, whether or not its tab is the one on screen.
            if (tablesToPublish.Count > 0 && owner.Tabs.FirstOrDefault(tab => ReferenceEquals(tab.Tag, tablesToPublish[0])) is { } firstTab)
            {
                if (IsOnScreen(owner)) Workspace.SelectedItem = firstTab; else owner.SelectedTab = firstTab;
            }
            SetStatus(owner, output.Error == null ? $"Ready · {Counts.Of(tablesToPublish.Count, "result")} · {Counts.Of(tablesToPublish.Sum(t => t.Rows.Count), "row")} · {Elapsed(elapsedMs)} · {SourceLabelOf(owner)}" : "Error / partial output · see Messages");
        }
        catch (OperationCanceledException) { AddMessage(owner, 0, SourceLabelOf(owner), "Execution cancelled. No new results were published."); SetStatus(owner, "Cancelled · " + SourceLabelOf(owner)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { AddMessage(owner, 1, "CLIENT", "Unable to display the result: " + ex.Message); SetStatus(owner, "Error · see Messages"); ShowMessages(owner); }
        finally { ticker.Stop(); RefreshMessages(owner); owner.Running = null; RefreshFor(owner); }
    }

    private static string Elapsed(double milliseconds) => milliseconds < 1000
        ? milliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms"
        : (milliseconds / 1000).ToString("F2", CultureInfo.InvariantCulture) + " s";
    public const string UnnamedColumn = "(no column name)";

    // Where a result came from, frozen when it is published: the session's database can change afterwards.
    private static string OriginOf(IQueryEngine source) => source.Name +
        (source is ISessionFacts { Facts.Spid: int spid } ? " · SPID " + spid.ToString(CultureInfo.InvariantCulture) : "") +
        " · " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public const int MaxResultName = 80;
    private static string ResultName(TabItem tab) => (tab.Header?.ToString() ?? "").Replace("📌 ", "", StringComparison.Ordinal);
    // A tab name made safe as a file name stem for exports; the tab keeps its own text.
    public static string FileStem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string stem = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return stem.Length == 0 ? "Result" : stem;
    }

    private TabItem AddResult(DataTable table, int number, string? source = null, string? sql = null, string? header = null, string? server = null, int? insertAt = null, SessionWorkspace? owner = null)
    {
        var target = owner ?? activeSession;
        string origin = source ?? OriginOf(target.Engine);
        target.Origins[table] = (sql ?? target.ExecutingSql, origin);
        // Result rows keep the compact minimum height for single-line cells, grow for multiline payloads (such as
        // XML plans or stored procedure text), scroll smoothly by pixel, and can be dragged taller or shorter.
        var grid = new DataGrid { ItemsSource = table.DefaultView, AutoGenerateColumns = false, RowHeight = double.NaN };
        grid.SetResourceReference(DataGrid.MinRowHeightProperty, "GridRowHeight");
        VirtualizingPanel.SetScrollUnit(grid, ScrollUnit.Pixel);
        grid.SetResourceReference(Control.FontFamilyProperty,"MonoFont");
        foreach (DataColumn column in table.Columns)
            // Expressions without an alias arrive unnamed; the header says so while copies keep the source caption.
            grid.Columns.Add(new DataGridTextColumn { Header = string.IsNullOrWhiteSpace(column.Caption) ? UnnamedColumn : column.Caption, SortMemberPath = column.ColumnName, Binding = new Binding($"[{column.Ordinal}]") { Converter = NullDisplayConverter.Instance }, MinWidth = 60, Width = new DataGridLength(FitColumnWidth(table, column.Ordinal, string.IsNullOrWhiteSpace(column.Caption) ? UnnamedColumn : column.Caption)) });
        var body = new DockPanel();
        var label = new TextBlock { Text = $"{origin}  ·  {Counts.Of(table.Rows.Count, "row")}  ·  {Counts.Of(table.Columns.Count, "column")}  ·  NULL is displayed as blank", Margin = new Thickness(14, 10, 14, 10),TextTrimming=TextTrimming.CharacterEllipsis };
        label.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");
        label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        // Multi-server results carry their server as a badge at the foot of the grid, beside the full provenance.
        var footer = new Grid(); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition());
        if (server != null)
        {
            var badge = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Server of this result" };
            badge.SetResourceReference(Border.BackgroundProperty, "AccentTint"); badge.SetResourceReference(Border.BorderBrushProperty, "AccentCardRing"); badge.SetResourceReference(Border.CornerRadiusProperty, "ControlRadius");
            var badgeText = new TextBlock { Text = "◆ " + server, FontWeight = FontWeights.SemiBold };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); badgeText.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"); badgeText.SetResourceReference(TextBlock.FontSizeProperty, "SmallFontSize");
            badge.Child = badgeText; footer.Children.Add(badge);
        }
        Grid.SetColumn(label, 1); footer.Children.Add(label);
        DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer); body.Children.Add(grid);
        var tab = new TabItem { Header = header ?? $"Result {number}", Content = body, Tag = table,
            ToolTip = (server != null ? "Server " + server + " · " : "") + "Source: " + origin + " · double-click to rename · middle-click to close" };
        AttachResultActions(tab, grid);
        AddResultTools(tab, grid, body, label);
        MountResultTab(target, tab, insertAt ?? target.Tabs.Count);
        return tab;
    }

    // Opening width from the header and the first rows' displayed text, so values such as "Awaiting Command" show
    // whole. Capped so one long cell cannot push the other columns away; the header gripper still resizes freely.
    public const double MaxFittedColumn = 520;
    private double FitColumnWidth(DataTable table, int ordinal, string header)
    {
        var family = Application.Current.Resources["MonoFont"] as FontFamily ?? new FontFamily("Consolas");
        double size = Application.Current.Resources["UiFontSize"] is double ui ? ui : 13, dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var regular = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bold = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        double Measure(string text, Typeface face) => new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, Brushes.Black, dip).WidthIncludingTrailingWhitespace;
        double widest = Measure(header.ToUpperInvariant(), bold);
        int rows = Math.Min(table.Rows.Count, 200);
        for (int row = 0; row < rows && widest < MaxFittedColumn; row++)
        {
            string text = NullDisplayConverter.Instance.Convert(table.Rows[row][ordinal], typeof(string), null!, CultureInfo.InvariantCulture) as string ?? "";
            foreach (string rawLine in text.Split('\n').Take(40))
            {
                string line = rawLine.TrimEnd('\r').Replace("\t", "    ", StringComparison.Ordinal);
                if (line.Length > 120) line = line[..120];
                widest = Math.Max(widest, Measure(line, regular));
            }
        }
        return Math.Clamp(Math.Ceiling(widest) + 28, 72, MaxFittedColumn);
    }

    private void ClearMessages() => ClearMessages(activeSession);

    // A short work area (a small window, a wrapped toolbar or the largest interface size) cannot hold both cards
    // whole, so the run output goes compact: the RUN OUTPUT title row folds away and the row keeps only what the
    // editor does not need for its minimum. A share of the height left the grid a sliver with no complete row.
    private const double MinimumEditorRow=136;   // card borders + the result tab strip + 80 DIP of SQL
    private const double MinimumLogRow=128;
    private void WorkAreaSizeChanged(object sender,SizeChangedEventArgs e)
    {
        bool shortArea=e.NewSize.Height<380;
        LogTitle.Visibility=shortArea?Visibility.Collapsed:Visibility.Visible;
        LogRow.MaxHeight=shortArea?Math.Max(MinimumLogRow,e.NewSize.Height-MinimumEditorRow):double.PositiveInfinity;
    }

    private void AddMessage(int number, string source, string text, string? rawText = null) => AddMessage(activeSession, number, source, text, rawText);

    private bool ConfirmDiscard()
    {
        if (Editor.Text == savedText) return true;
        var answer = ShowMessage("Unsaved query", "The query has unsaved changes. Save them before continuing?", true);
        return answer == MessageBoxResult.No || answer == MessageBoxResult.Yes && Save(false);
    }

    private void SetDocument(string text, string? path)
    {
        documentPath = path;
        bookmarks.Clear();
        Editor.Text = text;
        Editor.Document.UndoStack.ClearAll();
        savedText = text;
        UpdateTitle();
        Workspace.SelectedItem = QueryTab;
        Editor.Focus();
    }

    // The title names the document and the product, nothing else: the server, platform and database live in the
    // session tab and the status bar, and one title cannot speak for several sessions at once.
    private void UpdateTitle() => Title = $"{(repeat != null ? "⚠ REPEATING EVERY " + RepeatPlan.Describe(repeat.Interval).ToUpperInvariant() + " · " : "")}{(Editor.Text != savedText ? "* " : "")}{(documentPath == null ? "Untitled.sql" : Path.GetFileName(documentPath))} — DBMON SQL";
    private void EditorChanged(object sender, EventArgs e) { if (ready) UpdateTitle(); }
    private void NewClick(object sender, RoutedEventArgs e) { if (ConfirmDiscard()) SetDocument("", null); }
    private void OpenClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var picker = new OpenFileDialog { Filter = "SQL scripts (*.sql)|*.sql|Text files (*.txt)|*.txt|All files (*.*)|*.*" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(picker.FileName).Length > 4 * 1024 * 1024) { ShowError("Scripts can be up to 4 MiB."); return; }
            SetDocument(File.ReadAllText(picker.FileName, new UTF8Encoding(false, true)), picker.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException) { ShowError("Unable to open the file as UTF-8: " + ex.Message); }
    }

    private bool Save(bool choosePath)
    {
        string? path = documentPath;
        if (choosePath || path == null)
        {
            var picker = new SaveFileDialog { Filter = "SQL scripts (*.sql)|*.sql", DefaultExt = ".sql", FileName = path == null ? "Query.sql" : Path.GetFileName(path), OverwritePrompt = true };
            if (picker.ShowDialog(this) != true) return false;
            path = picker.FileName;
        }
        try { AtomicFiles.SaveUtf8(path, Editor.Text); documentPath = path; savedText = Editor.Text; UpdateTitle(); StatusText.Text = "File saved · UTF-8"; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError("Unable to save: " + ex.Message); return false; }
    }
    private void SaveClick(object sender, RoutedEventArgs e) => Save(false);
    private void SaveAsClick(object sender, RoutedEventArgs e) => Save(true);
    private void ExitClick(object sender, RoutedEventArgs e) => Close();
    private void ExecuteClick(object sender, RoutedEventArgs e) => _ = RunPressedAsync();
    // The Run chevron: every other way to run, with its shortcut, enabled for the current state.
    private void RunMenuClick(object sender, RoutedEventArgs e)
    {
        bool idle = running == null && !changingSession && repeat == null && catalogActions == 0, online = engine is not DisconnectedQueryEngine;
        var menu = new ContextMenu { PlacementTarget = ExecuteButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
#if DBMON_SMOKE
        lastRunMenu = menu;
#endif
        void Item(string header, string gesture, string glyph, Action action, bool enabled)
        {
            var icon = new TextBlock { Text = glyph }; icon.SetResourceReference(StyleProperty, "GlyphText");
            var item = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled, Icon = icon };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Item("Run selection", "Ctrl+E", "", () => _ = ExecuteAsync(selectionOnly: true), idle && online && Editor.SelectionLength > 0);
        Item("Run current line", "Alt+F9", "", () => _ = ExecuteAsync(lineOnly: true), idle && online);
        Item("Export query to file…", "", "", () => ExportQueryClick(this, new RoutedEventArgs()), idle && online);
        menu.Items.Add(new Separator());
        Item("Run on several servers…", "Shift+F9", "", () => MultiRunClick(this, new RoutedEventArgs()), idle);
        Item(repeat != null ? "Stop repeating" : "Repeat every…", "Ctrl+Shift+F9", "", () => RepeatClick(this, new RoutedEventArgs()), repeat != null || idle && online);
        menu.Items.Add(new Separator());
        // isql-style execution: maintenance without the profile's timeout, and whether a failed batch stops the script.
        Item("Run without timeout (maintenance)", "", "", () => _ = ExecuteAsync(noTimeout: true), idle && online);
        var continueItem = new MenuItem { Header = "Continue after errors", IsCheckable = true, IsChecked = runSettings.ContinueOnError,
            ToolTip = "Like isql: a failed batch is recorded in Messages and the next batch runs. Off stops at the first error. Kept across restarts." };
        continueItem.Click += (_, _) => SetContinueOnError(continueItem.IsChecked);
        menu.Items.Add(continueItem);
        menu.IsOpen = true;
    }
    // Run / F9: on a result tab, that result's SQL runs again on its session and replaces it; elsewhere the editor runs.
    private Task RunPressedAsync()
    {
        if (running != null || changingSession) return Task.CompletedTask;
        if (RerunOfSelectedResult(out string? refusal) is { } rerun) return ExecuteAsync(sqlOverride: rerun.Sql, label: rerun.Label, replaceRun: rerun.Replace);
        if (refusal != null) { ClearMessages(); AddMessage(1, "CLIENT", refusal); StatusText.Text = refusal; SetLogVisible(true); return Task.CompletedTask; }
        return ExecuteAsync();
    }
    private void LineClick(object sender, RoutedEventArgs e) => _ = ExecuteAsync(true);
    private void SelectionClick(object sender, RoutedEventArgs e) => _ = ExecuteAsync(selectionOnly: true);
    private void CancelClick(object sender, RoutedEventArgs e) { if (running != null) _ = running.CancelAsync(); }
    private void QueryClick(object sender, RoutedEventArgs e) { Workspace.SelectedItem = QueryTab; Editor.Focus(); }
    private void FindClick(object sender, RoutedEventArgs e) { Workspace.SelectedItem = QueryTab; Editor.Focus(); search.Open(); }
    private void BrowserClick(object sender, RoutedEventArgs e) { bool show = BrowserPanel.Visibility != Visibility.Visible; BrowserColumn.Width = new GridLength(show ? 300 : 0); BrowserPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed; if (show && BrowserTree.Items.Count == 0) RunCatalogAction(LoadBrowser,"Catalog"); }
    private void LogClick(object sender, RoutedEventArgs e) => ApplyAppearance(appearance with {ShowMessages=((MenuItem)sender).IsChecked});
    private void SetLogVisible(bool show)
    {
        if (show == logVisible) return;
        if (!show) savedLogHeight = LogRow.Height;
        LogRow.Height = show ? savedLogHeight : new GridLength(0);
        LogPanel.Visibility = LogSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        logVisible = show;
        ShowLogMenu.IsChecked = show;
    }
    private void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || e.Source != Workspace) return;
        if (!mountingSession) SelectRunText();
        // The tab strip is one scrolling row, so a tab selected from the keyboard or from code can sit off-screen.
        if (Workspace.SelectedItem is TabItem selected) selected.BringIntoView();
        if (Workspace.SelectedItem is TabItem { Tag: DataTable table } tab) StatusText.Text = $"{ResultName(tab)} · {resultOrigins.GetValueOrDefault(table).Source} · {Counts.Of(table.Rows.Count, "row")} · {Counts.Of(table.Columns.Count, "column")}";
    }
    private void ExportClick(object sender, RoutedEventArgs e)
    {
        if (Workspace.SelectedItem is not TabItem { Tag: DataTable table }) { ShowError("Select a Result tab to export."); return; }
        var picker = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", DefaultExt = ".csv", FileName = FileStem(ResultName((TabItem)Workspace.SelectedItem)) + ".csv", OverwritePrompt = true };
        if (picker.ShowDialog(this) != true) return;
        try { var tab=(TabItem)Workspace.SelectedItem; var grid=((DockPanel)tab.Content).Children.OfType<DataGrid>().Single(); AtomicFiles.SaveUtf8(picker.FileName, CsvExport.Serialize(VisibleResult(grid))); StatusText.Text = "Current view exported · CSV"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError("Unable to export: " + ex.Message); }
    }
    private void AboutClick(object sender, RoutedEventArgs e) => ShowMessage("About DBMON SQL", $"DBMON SQL · {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}\nDeveloped by Christian Buzzetti · MIT License\ngithub.com/ChristianBuzzetti/DBMON_SQL\n.NET 10 · WPF · AvalonEdit · SAP ODBC\n\nTheme palettes and accents. Ctrl+E executes the selection.\nASE connection using sql.ini and credentials entered by the user.\nServer compatibility is verified in your non-production environment.");
    private void ShowError(string text) => ShowMessage("DBMON SQL",text);

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int child = 0; child < VisualTreeHelper.GetChildrenCount(parent); child++)
        {
            DependencyObject item = VisualTreeHelper.GetChild(parent, child);
            if (item is T typed) yield return typed;
            foreach (T nested in VisualChildren<T>(item)) yield return nested;
        }
    }
}

public sealed record LogMessage(string Time, int Number, string Source, string Text, string? RawText = null);
public sealed class NullDisplayConverter : IValueConverter
{
    public static readonly NullDisplayConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch { byte[] bytes => "0x" + System.Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 128))) + (bytes.Length > 128 ? $" … ({bytes.Length} bytes)" : ""), _ => ResultFormats.Cell(value) };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
