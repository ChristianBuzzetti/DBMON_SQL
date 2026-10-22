using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // Synthetic engines only: these checks never authenticate or reuse an external connection.
    private sealed class WorkspaceSmokeEngine(string name, ServerPlatform platform) : IQueryEngine, ISessionFacts, IDisposable
    {
        public string Name => name + " · synthetic session";
        public SessionFacts Facts => new(platform == ServerPlatform.Rs ? "" : name + "_db", null, "fixture", "utf8", platform);
        public bool Disposed { get; private set; }
        public int Executions { get; private set; }
        public string? LastSql { get; private set; }
        public ManualResetEventSlim? Gate;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public QueryOutput Execute(string sql, CancellationToken cancellation = default)
        {
            if (Disposed) throw new InvalidOperationException("Synthetic session was disposed.");
            Started.TrySetResult(); Gate?.Wait(cancellation); cancellation.ThrowIfCancellationRequested();
            Executions++; LastSql = sql;
            var table = new DataTable(); table.Columns.Add("session"); table.Columns.Add("sql");
            table.Rows.Add(name, sql);
            var output = new QueryOutput(); output.Tables.Add(table); return output;
        }
        public void Dispose() => Disposed = true;
    }

    private async Task VerifySessionWorkspaces(Action<bool, string> require)
    {
        var original = activeSession;
        var first = AddSession()!;
        var firstEngine = new WorkspaceSmokeEngine("session-a", ServerPlatform.Ase);
        engine = firstEngine; RefreshConnection();
        Editor.Text = "select 'workspace A'"; savedText = Editor.Text;
        Editor.AppendText(" -- edit");
        ToggleBookmark();
        Editor.Select(2, 4); Editor.CaretOffset = 6;
        var documentA = Editor.Document;
        var anchorA = bookmarks.Single();
        aseOptions = AseSessionOptions.Default with { NoExec = true };
        databaseNames = ["session-a_db", "master"];
        parameterValues["value"] = "A";
        browserFilter = "filter A"; BrowserSearch.Text = "filter A";
        var nodeA = new TreeViewItem { Header = "Synthetic catalog A", IsExpanded = true };
        BrowserTree.Items.Add(nodeA);
        await ExecuteAsync(sqlOverride: Editor.Text);
        var resultA = ResultTabs.Single(); var tableA = (DataTable)resultA.Tag;
        resultA.Header = "A result"; TogglePin(resultA);
        var messagesA = log.ToArray();
        int firstRuns = firstEngine.Executions;

        var second = AddSession()!;
        require(sessions.Count == 3 && engine is DisconnectedQueryEngine && Editor.Text == "" && Workspace.Items.Count == 1,
            "A new session is an independent offline workspace with an empty SQL document");
        require(OfflineBanner.Visibility == Visibility.Visible && !ExecuteButton.IsEnabled && second.Label.Text == "Not connected",
            "An offline session has a visible notice, disabled Run and a Not connected title");
        await ExecuteAsync();
        require(firstEngine.Executions == firstRuns && !firstEngine.Disposed, "Executing offline never reaches another session");

        // Exercise cancellation through the actual existing modal, without submitting credentials.
        Exception? loginFailure = null;
        _ = Dispatcher.BeginInvoke(new Action(() => {
            try { OwnedWindows.OfType<ConnectionWindow>().Single().Close(); }
            catch (Exception ex) { loginFailure = ex; }
        }));
        ConnectClick(this, new RoutedEventArgs());
        require(loginFailure == null && engine is DisconnectedQueryEngine && !ExecuteButton.IsEnabled && sessions.Count == 3,
            "Cancelling the existing login keeps its tab offline without replacing another session");

        var secondEngine = new WorkspaceSmokeEngine("session-b", ServerPlatform.Iq);
        engine = secondEngine; RefreshConnection();
        Editor.Text = "select 'workspace B'"; savedText = Editor.Text;
        await ExecuteAsync();
        var resultB = ResultTabs.Single();
        var nodeB = new TreeViewItem { Header = "Synthetic catalog B" }; BrowserTree.Items.Add(nodeB);
        parameterValues["value"] = "B";
        require(second.Label.Text.Contains("IQ") && first.Label.Text.Contains("ASE"), "Session titles identify distinct engines and platforms");
        require(SelectSession(first) && ReferenceEquals(engine, firstEngine) && ReferenceEquals(Editor.Document, documentA),
            "Switching sessions restores the original engine and AvalonEdit document");
        require(Editor.Text == "select 'workspace A' -- edit" && Editor.SelectionStart == 2 && Editor.SelectionLength == 4 && Editor.CaretOffset == 6,
            "Switching sessions preserves each SQL buffer, caret and selection");
        require(Editor.CanUndo && bookmarks.Single() == anchorA && !anchorA.IsDeleted,
            "Session switching retains undo history and live bookmark anchors");
        require(ReferenceEquals(ResultTabs.Single(), resultA) && pinnedResults.Contains(tableA) && resultA.Header.ToString() == "📌 A result" && log.SequenceEqual(messagesA),
            "Session switching preserves result objects, names, pins and messages");
        require(BrowserTree.Items.Contains(nodeA) && nodeA.IsExpanded && BrowserSearch.Text == "filter A" && browserFilter == "filter A" &&
            databaseNames!.SequenceEqual(new[] { "session-a_db", "master" }) && aseOptions.NoExec && parameterValues["value"] == "A",
            "Each workspace retains its expanded catalog, filter, database cache, ASE options and parameter values");
        await RunPressedAsync();
        require(firstEngine.Executions == firstRuns + 1 && secondEngine.Executions == 1 && firstEngine.LastSql == "select 'workspace A' -- edit" && ResultTabs.Contains(resultA),
            "F9 on a restored pinned result re-runs its recorded SQL on the originating session");
        Editor.Undo();
        require(Editor.Text == "select 'workspace A'", "Undo after switching reverses only that workspace's edit");
        savedText = Editor.Text;

        SelectSession(second);
        require(Editor.Text == "select 'workspace B'" && ReferenceEquals(ResultTabs.Single(), resultB) && BrowserTree.Items.Contains(nodeB) && !aseOptions.NoExec && parameterValues["value"] == "B",
            "Returning to the second session restores its SQL, results and catalog without ASE option leakage");
        // A result whose captured engine no longer matches still cannot run on a replacement connection.
        var replacement = new WorkspaceSmokeEngine("replacement", ServerPlatform.Rs);
        engine = replacement;
        await RunPressedAsync();
        require(replacement.Executions == 0 && secondEngine.Executions == 1 && StatusText.Text.Contains("earlier session"),
            "A result from an earlier connection never re-runs on a replacement engine");
        engine = secondEngine;

        using (var gateB = new ManualResetEventSlim())
        using (var gateA = new ManualResetEventSlim())
        {
            secondEngine.Gate = gateB;
            var runB = ExecuteAsync(sqlOverride: "select 'held B'");
            try
            {
                // Task.Run owns each execution until its fixture gate is released.
                await Task.Yield();
                require(second.Running != null && SelectSession(first) && activeSession == first,
                    "A running query no longer holds the session strip: another engine's tab opens while it works");
                firstEngine.Gate = gateA;
                var runA = ExecuteAsync(sqlOverride: "select 'held A'");
                try
                {
                    await Task.Yield();
                    require(first.Running != null && second.Running != null && second.Label.Text.StartsWith("▶", StringComparison.Ordinal) &&
                        !ReferenceEquals(first.Running, second.Running),
                        "Two sessions execute at the same time, each with its own cancellation, and the tab in the background says it is working");
                    await CloseSessionAsync(second);
                    require(!secondEngine.Disposed && sessions.Contains(second) && activeSession == first,
                        "A session that is running cannot be closed and closing it never drags the user into that tab");
                }
                finally { gateA.Set(); await runA; firstEngine.Gate = null; }
            }
            finally { gateB.Set(); await runB; secondEngine.Gate = null; }
            require(first.Running == null && second.Running == null && firstEngine.LastSql == "select 'held A'" && secondEngine.LastSql == "select 'held B'",
                "Each session finished its own SQL on its own connection");
            var backgroundResult = second.Tabs[^1];
            require(activeSession == first && !Workspace.Items.Contains(backgroundResult) && Equals(((DataTable)backgroundResult.Tag).Rows[0][1], "select 'held B'") &&
                second.Messages[^1].Text.StartsWith("Done", StringComparison.Ordinal) && log[^1].Text.StartsWith("Done", StringComparison.Ordinal) &&
                !ReferenceEquals(second.Messages, log),
                "A background session publishes its result and messages into its own workspace, never into the one on screen");
            require(SelectSession(second) && Workspace.Items.Contains(backgroundResult) && Workspace.SelectedItem == backgroundResult,
                "Returning to that session mounts the result it produced while it was in the background");
            CloseResult(backgroundResult);
        }

        // Catalog work resumes after the user may have switched tabs. The synthetic engines are not OdbcSessions, so
        // ReadCatalog itself cannot run here; these checks exercise the publication points its callers now go through.
        var catalogOfFirst = first.Catalog.ToArray(); var optionsOfFirst = first.Options; var databasesOfFirst = first.Databases;
        var strayNode = new TreeViewItem { Header = "Catalog landed late" };
        var mountedCatalog = BrowserTree.Items.OfType<TreeViewItem>().ToArray();
        PublishCatalog(first, [strayNode]);
        require(activeSession == second && !BrowserTree.Items.Contains(strayNode) && BrowserTree.Items.OfType<TreeViewItem>().SequenceEqual(mountedCatalog) &&
            first.Catalog.Count == 1 && ReferenceEquals(first.Catalog[0], strayNode),
            "A catalog read that lands after a tab switch fills its own session's tree, never the one on screen");
        var databaseOutput = new QueryOutput();
        var databaseTable = new DataTable(); databaseTable.Columns.Add("name"); databaseTable.Rows.Add("other_server_db");
        databaseOutput.Tables.Add(databaseTable);
        RememberDatabases(first, databaseOutput);
        require(first.Databases!.SequenceEqual(new[] { "other_server_db" }) && second.Databases == null,
            "A database list read in the background is stored on its own session, so no USE can reach another server");
        string chipBeforeBackgroundOptions = SetOptionsText.Text;
        first.Options = AseSessionOptions.Default with { NoExec = true, StatisticsIo = true };
        RefreshAseOptions(first);
        require(SetOptionsText.Text == chipBeforeBackgroundOptions && !second.Options.NoExec && first.Options.NoExec,
            "SET options acknowledged by a background session stay on that session and never touch the chip on screen");
        var filterNode = new TreeViewItem { Tag = "session-a_db" };
        ResetDatabaseNode(filterNode, first);
        require(first.Filter == "filter A" && second.Filter != first.Filter &&
            filterNode.Items.OfType<TreeViewItem>().All(group => group.Header.ToString()!.Contains("filter A", StringComparison.Ordinal)),
            "Catalog group headers quote the filter of the session that built them, not the filter of the active tab");
        first.Catalog.Clear(); first.Catalog.AddRange(catalogOfFirst); first.Databases = databasesOfFirst; first.Options = optionsOfFirst;

        var plan = new RepeatPlan("select 'repeat B'", TimeSpan.FromHours(24), null, true, secondEngine, "synthetic query");
        StartRepeat(plan);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while ((plan.Runs == 0 || second.RepeatBusy) && timeout.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        require(plan.Runs == 1 && !repeatBusy && repeat == plan, "The repeat fixture reaches its idle countdown between runs");
        int beforeRepeatRefusal = secondEngine.Executions;
        SessionTabs.SelectedItem = first.Header;
        require(activeSession == first && second.Repeat == plan && RepeatBanner.Visibility == Visibility.Collapsed && repeat == null,
            "A repeat keeps running in its own session while another tab is on screen, and the caution band follows the tab");
        require(SelectSession(second) && RepeatBanner.Visibility == Visibility.Visible && repeat == plan,
            "Returning to the repeating session shows its caution band again");
        await RunPressedAsync(); await ExecuteOnTargetsAsync([new RunTarget("fixture", secondEngine)], "select 'other'", "synthetic");
        require(secondEngine.Executions == beforeRepeatRefusal && repeat == plan,
            "A repeat still blocks F9 and multi-server execution inside its own session without stopping");
        RepeatStopClick(this, new RoutedEventArgs());
        require(repeat == null && ExecuteButton.IsEnabled && ConnectMenu.IsEnabled && RepeatBanner.Visibility == Visibility.Collapsed,
            "Stopping an idle repeat immediately restores Run and Connect and removes the band");
        var completed = new RepeatPlan("select 'one repeat'", TimeSpan.FromHours(24), 1, true, secondEngine, "synthetic query");
        StartRepeat(completed); timeout.Restart();
        while ((repeat != null || repeatBusy) && timeout.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        require(completed.Runs == 1 && repeat == null && !repeatBusy && ExecuteButton.IsEnabled && ConnectMenu.IsEnabled,
            "Automatic repeat completion restores idle controls without switching session tabs");

        var catalogRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool catalogPublished = false;
        RunCatalogAction(async _ => { await catalogRelease.Task; AddMessage(0, "CLIENT", "Synthetic catalog publication"); catalogPublished = true; }, "Synthetic catalog");
        int beforeCatalogRefusal = secondEngine.Executions;
        require(second.CatalogActions == 1 && second.Running == null && SelectSession(first) && activeSession == first && SelectSession(second),
            "Catalog publication no longer holds the session strip after its query finishes");
        await ExecuteAsync(); MultiRunClick(this, new RoutedEventArgs()); RepeatClick(this, new RoutedEventArgs());
        require(secondEngine.Executions == beforeCatalogRefusal && repeat == null && activeSession == second,
            "Pending catalog publication still blocks query, multi-server and repeat starts in its own session");
        catalogRelease.SetResult();
        while (!catalogPublished || catalogActions != 0) await Task.Delay(20);

        Editor.AppendText(" -- unsaved");
        SelectSession(first);
        _ = Dispatcher.BeginInvoke(new Action(() => {
            var dialog = OwnedWindows.Cast<Window>().Single(w => w.Title == "Unsaved query");
            VisualChildren<Button>(dialog).Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        await CloseSessionAsync(second);
        require(sessions.Contains(second) && !secondEngine.Disposed && Editor.Text.EndsWith(" -- unsaved"),
            "Cancelling close of an inactive dirty session retains its document and connection");
        savedText = Editor.Text;
        await DisconnectSessionAsync();
        require(secondEngine.Disposed && engine is DisconnectedQueryEngine && !firstEngine.Disposed && first.Engine == firstEngine,
            "Disconnect disposes only the active engine and leaves the other session connected");
        require(SelectSession(first) && ResultTabs.Contains(resultA) && pinnedResults.Contains(tableA) && BrowserTree.Items.Contains(nodeA),
            "Disconnecting another session preserves this workspace's results, pins and catalog");
        CaptureSmokeVisual(this, "session-workspaces.png");
        first.SavedText = first.Document.Text; second.SavedText = second.Document.Text;
        await CloseSessionAsync(second);
        await CloseSessionAsync(first);
        SelectSession(original);
        require(sessions.Count == 1 && firstEngine.Disposed && !ReferenceEquals(engine, firstEngine) && !ReferenceEquals(engine, secondEngine),
            "Closing session tabs disposes their own resources and restores the initial workspace");

        // Last-close behavior uses a temporary inventory, retaining the original fixture for the rest of smoke.
        sessions.Remove(original); SessionTabs.Items.Remove(original.Header);
        var last = AddSession()!;
        await CloseSessionAsync(last);
        var offline = activeSession;
        require(sessions.Count == 1 && engine is DisconnectedQueryEngine && OfflineBanner.Visibility == Visibility.Visible && !ExecuteButton.IsEnabled,
            "Closing the last session leaves exactly one disconnected workspace");
        sessions.Add(original); SessionTabs.Items.Add(original.Header); SelectSession(original);
        await CloseSessionAsync(offline);
        SelectSession(original);
    }
}
