using System.Data;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // These sessions exist only inside --smoke. No driver, connection string or credentials are used.
    private sealed class ObjectResolutionSmokeEngine(ServerPlatform platform, QueryOutput response) : IQueryEngine, ISessionFacts
    {
        public string Name => "Synthetic object metadata";
        public string CachedDatabase = "live_db";
        public SessionFacts Facts => new(CachedDatabase, null, "fixture", "utf8", platform);
        public int Calls;
        public string LastSql = "";
        public ManualResetEventSlim? Gate;
        public bool IgnoreCancellation;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public QueryOutput Execute(string sql, CancellationToken cancellation = default)
        {
            Interlocked.Increment(ref Calls); LastSql = sql; Started.TrySetResult();
            if (IgnoreCancellation) Gate?.Wait(TimeSpan.FromSeconds(3));
            else { Gate?.Wait(cancellation); cancellation.ThrowIfCancellationRequested(); }
            return response;
        }
    }
    private static QueryOutput ObjectResolutionFixture(params (int Id, string? Owner, string Name, string Type)[] rows)
    {
        var output = new QueryOutput();
        var context = new DataTable(); context.Columns.Add("context_database"); context.Rows.Add("live_db"); output.Tables.Add(context);
        var objects = new DataTable(); objects.Columns.Add("id", typeof(int)); objects.Columns.Add("owner"); objects.Columns.Add("name"); objects.Columns.Add("type");
        foreach (var row in rows) objects.Rows.Add(row.Id, row.Owner is null ? DBNull.Value : row.Owner, row.Name, row.Type);
        output.Tables.Add(objects); return output;
    }
    private async Task VerifyEditorObjectResolution(Action<bool, string> require)
    {
        var original = activeSession;
        var owner = AddSession()!;
        var menu = new ContextMenu();
        menu.Closed += (_, _) => CloseEditorObjectMenu(menu);
        ObjectResolutionSmokeEngine Prepare(string token, QueryOutput response, ServerPlatform platform = ServerPlatform.Ase)
        {
            var source = new ObjectResolutionSmokeEngine(platform, response);
            owner.Engine = source; owner.Options = AseSessionOptions.Default;
            Editor.Text = token; Editor.SelectAll(); owner.SavedText = token;
            BuildEditorMenu(menu); return source;
        }
        async Task<EditorObjectResult> Lookup() => await ReadEditorObject(editorObjectRequest!);
        ContextMenu Actions(EditorObjectResult result)
        {
            var actions = new ContextMenu();
            if (result.Object is { } item) AddResolvedEditorObjectActions(actions, editorObjectRequest!, item, result.ContextDatabase);
            return actions;
        }
        static bool Has(ContextMenu items, string header) => items.Items.OfType<MenuItem>().Any(item => Equals(item.Header, header));
        try
        {
            var tableEngine = Prepare("orders", ObjectResolutionFixture((7, "sales", "orders", "U")));
            AddMessage(owner, 0, "CLIENT", "Synthetic diagnostic retained across object lookup.");
            var diagnostics = owner.Messages.ToArray(); var tabs = owner.Tabs.ToArray(); string editorSql = Editor.Text;
            require(Has(menu, "Checking database, owner and object type…") && !Has(menu, "Select TOP 100 rows") && Has(menu, "Copy"),
                "Unresolved editor object menus preserve editing and expose no guessed object commands");
            var tableResult = await Lookup(); var tableActions = Actions(tableResult);
            foreach (var helper in new[] { ("help", "sp_help"), ("depends", "sp_depends"), ("indexes", "sp_helpindex"), ("space", "sp_spaceused") })
            {
                var target = new ObjectTarget("mon_db", "dbo", "mon_table");
                string expected = "exec mon_db.." + helper.Item2 + " 'dbo.mon_table'";
                require(ObjectHelperSql(helper.Item1, target) == expected &&
                    ResolvedEditorHelperSql(helper.Item1, new CatalogObject("mon_db", "dbo", "mon_table", "U", 7)) == expected,
                    "Editor and explorer " + helper.Item2 + " preserve ASE procedure and object boundaries without unnecessary delimiters");
            }
            require(tableActions.Items.OfType<MenuItem>().All(item => ReferenceEquals(item.Style, FindResource(typeof(MenuItem)))),
                "Resolved object commands inherit the application MenuItem style");
            require(tableResult.Object is { Database: "live_db", Owner: "sales", Type: "U" } && Has(tableActions, "Select TOP 100 rows") && Has(tableActions, "Indexes · sp_helpindex") && !Has(tableActions, "View code"),
                "ASE table resolution returns the effective database and actual owner with table-only actions");
            require(owner.Messages.SequenceEqual(diagnostics) && owner.Tabs.SequenceEqual(tabs) && Editor.Text == editorSql && owner.Running == null && owner.CatalogActions == 0,
                "Editor metadata lookup preserves SQL, result tabs and existing diagnostics and releases the execution lane");
            require(tableEngine.LastSql.Contains("if @@tranchained = 0 and @@trancount = 0 begin") && tableEngine.LastSql.Contains("select db_name() as context_database") && tableEngine.LastSql.Contains("select top 2") && !tableEngine.LastSql.Contains("use ", StringComparison.OrdinalIgnoreCase),
                "Object lookup reads its current database in a bounded batch guarded before any SELECT");

            foreach (string type in new[] { "P", "V", "TR", "SF" })
            {
                Prepare("sales.object_name", ObjectResolutionFixture((8, "sales", "object_name", type)));
                var result = await Lookup(); var actions = Actions(result);
                require(Has(actions, "View code") && Has(actions, "Select TOP 100 rows") == (type == "V") && !Has(actions, "Indexes · sp_helpindex"),
                    "ASE " + type + " menus offer stored code and only applicable relation actions");
            }
            foreach (string type in new[] { "U", "V", "P" })
            {
                Prepare("app.object_name", ObjectResolutionFixture((9, "app", "object_name", type)), ServerPlatform.Iq);
                var result = await Lookup(); var actions = Actions(result);
                require(result.Object?.Platform == ServerPlatform.Iq && Has(actions, "View code") == (type != "U") && Has(actions, "Select TOP 100 rows") == (type != "P") && Has(actions, "Size · sp_iqtablesize") == (type == "U"),
                    "IQ " + type + " lookup uses its catalog type for code, rows and size actions");
            }

            foreach (string token in new[] { "archive..orders", "archive.sales.orders" })
            {
                var source = Prepare(token, ObjectResolutionFixture((7, "sales", "orders", "U")));
                var result = await Lookup();
                require(result.Object is { Database: "archive", Owner: "sales" } && result.ContextDatabase == "live_db" && source.LastSql.Contains("[archive]..sysobjects") &&
                    (token.Contains("..", StringComparison.Ordinal) || source.LastSql.Contains("u.name = 'sales'")),
                    "Explicit ASE database and owner parts are retained independently of the connected current database");
            }
            Prepare("orders", ObjectResolutionFixture((7, "sales", "orders", "U")));
            Editor.Text = "use not_executed;\norders"; Editor.Select(Editor.Text.Length - 6, 6); BuildEditorMenu(menu);
            require((await Lookup()).Object?.Database == "live_db" && !((ObjectResolutionSmokeEngine)owner.Engine).LastSql.Contains("not_executed"),
                "An unexecuted USE in the editor never changes object lookup context");

            var staleFacts = Prepare("orders", ObjectResolutionFixture((7, "sales", "orders", "U")));
            staleFacts.CachedDatabase = "old_db"; BuildEditorMenu(menu);
            var freshResult = await Lookup(); var freshRequest = editorObjectRequest!;
            require(freshResult.Object?.Database == "live_db" && EditorObjectActionAllowed(freshRequest, freshResult.ContextDatabase),
                "Fresh catalog context remains actionable when cached database facts are stale");
            staleFacts.CachedDatabase = "other_db";
            require(!EditorObjectActionAllowed(freshRequest, freshResult.ContextDatabase),
                "Changing database facts after lookup expires the resolved action");
            staleFacts.CachedDatabase = "old_db"; owner.RunCounter++;
            require(!EditorObjectActionAllowed(freshRequest, freshResult.ContextDatabase),
                "An intervening completed query expires resolved actions even when cached facts do not change");

            Prepare("orders", ObjectResolutionFixture());
            var missing = await Lookup();
            require(missing.Object == null && missing.Notice.Contains("hidden by permissions"), "No visible catalog match is not reported as proof that the object does not exist");
            Prepare("orders", ObjectResolutionFixture((7, "sales", "orders", "U"), (8, "other", "orders", "U")));
            require((await Lookup()).Object == null, "Unqualified names with several owners expose no object actions");
            Prepare("orders", new QueryOutput { Error = "Synthetic permission denied" });
            require((await Lookup()).Object == null && !Has(menu, "Select TOP 100 rows"), "Denied catalog access cannot fall back to guessed object actions");
            Prepare("orders", new QueryOutput());
            require((await Lookup()).Notice.Contains("transaction mode"), "A guarded batch that returns no result is unavailable rather than missing");
            foreach (var row in new[] { (7, (string?)null, "orders", "U"), (7, (string?)"sales", "orders", "?"), (0, (string?)"sales", "orders", "U") })
            {
                Prepare("orders", ObjectResolutionFixture(row));
                require((await Lookup()).Object == null, "Incomplete or unsupported object metadata never produces actionable commands");
            }

            var blocked = Prepare("orders", ObjectResolutionFixture((7, "sales", "orders", "U")));
            owner.CatalogActions++;
            require((await Lookup()).Object == null && blocked.Calls == 0, "A pending catalog publication prevents a second metadata operation");
            owner.CatalogActions--;
            using (var runningProbe = new CancellationTokenSource())
            {
                owner.Running = runningProbe;
                require((await Lookup()).Object == null && blocked.Calls == 0, "A running query prevents concurrent editor metadata reads");
                owner.Running = null;
            }
            owner.Options = AseSessionOptions.Default with { NoExec = true };
            require((await Lookup()).Object == null && blocked.Calls == 0, "NOEXEC is preserved and does not trigger an automatic catalog read");
            owner.Options = AseSessionOptions.Default; owner.Changing = true;
            require((await Lookup()).Object == null && blocked.Calls == 0, "A connection change prevents editor metadata reads");
            owner.Changing = false;
            foreach (var pair in new[] { ("#temporary", ServerPlatform.Ase), ("db.app.orders", ServerPlatform.Iq), ("orders", ServerPlatform.Rs) })
            {
                var source = Prepare(pair.Item1, ObjectResolutionFixture(), pair.Item2);
                require((await Lookup()).Object == null && source.Calls == 0, "Unsupported temporary, IQ database-qualified and RS names require no server metadata request");
            }
            owner.Engine = new DisconnectedQueryEngine(); BuildEditorMenu(menu);
            require((await Lookup()).Notice.Contains("Connect"), "Disconnected editor inspection remains local and asks for a connection");

            var sourceQuoted = Prepare("[db'quote].[own'er].[a.b]", ObjectResolutionFixture((11, "own'er", "a.b", "U")));
            var quoted = await Lookup();
            require(sourceQuoted.LastSql.Contains("[db'quote]..sysobjects") && sourceQuoted.LastSql.Contains("o.name = 'a.b'") && sourceQuoted.LastSql.Contains("u.name = 'own''er'") &&
                ResolvedEditorHelperSql("top", quoted.Object!) == "SELECT TOP 100 * FROM [db'quote].[own'er].[a.b]" && ResolvedEditorHelperSql("help", quoted.Object!).Contains("'[own''er].[a.b]'"),
                "Catalog lookup and resolved helper SQL escape database, owner, object and literal boundaries");
            require(ParseObjectToken("orders; drop table x") == null && ParseObjectToken("db...orders") == null && ParseObjectToken("[unfinished") == null,
                "Malformed selections remain ordinary edit menus rather than executable object identifiers");

            var closed = Prepare("orders", ObjectResolutionFixture((7, "sales", "orders", "U")));
            using (var gate = new ManualResetEventSlim())
            {
                closed.Gate = gate; var pending = Lookup(); await closed.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                CloseEditorObjectMenu(menu); gate.Set(); var result = await pending;
                require(result.Object == null && !EditorObjectCurrent(editorObjectRequest!) && owner.Running == null && owner.CatalogActions == 0,
                    "Closing an object menu cancels an in-flight read and releases its original session without publishing actions");
            }
            var stale = Prepare("orders", ObjectResolutionFixture((7, "sales", "orders", "U")));
            using (var gate = new ManualResetEventSlim())
            {
                stale.Gate = gate; var oldRequest = editorObjectRequest!; var pending = Lookup(); await stale.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                BuildEditorMenu(menu); gate.Set(); await pending;
                require(!EditorObjectCurrent(oldRequest) && Has(menu, "Object lookup unavailable while a query is running.") && !Has(menu, "Select TOP 100 rows"),
                    "Reopening the object menu cannot accept a response from the previous menu generation");
            }

            var pinned = Prepare("orders", ObjectResolutionFixture((7, "sales", "orders", "U")));
            var pinnedResult = await Lookup(); var pinnedRequest = editorObjectRequest!; var pinnedActions = Actions(pinnedResult);
            var topAction = pinnedActions.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Select TOP 100 rows"));
            int calls = pinned.Calls;
            SelectSession(original); topAction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            require(pinned.Calls == calls && ReferenceEquals(activeSession, original), "An old object action never executes against the newly selected session");
            SelectSession(owner);
            owner.Engine = new ObjectResolutionSmokeEngine(ServerPlatform.Ase, ObjectResolutionFixture());
            topAction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            require(pinned.Calls == calls && ((ObjectResolutionSmokeEngine)owner.Engine).Calls == 0 && !EditorObjectActionAllowed(pinnedRequest, "live_db"),
                "Reconnecting expires resolved commands instead of executing them on a replacement connection");

            var live = Prepare("orders", ObjectResolutionFixture((7, "sales", "orders", "U")));
            var liveActions = Actions(await Lookup());
            var liveAction = liveActions.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Select TOP 100 rows"));
            liveActions.Items.Remove(liveAction); menu.Items.Add(liveAction); menu.PlacementTarget = Editor; menu.IsOpen = true;
            // Popup opening and MenuBase's input mode are asynchronous. Invoke only once the real visual
            // container has loaded; invoking earlier can execute Click without exercising popup dismissal.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            bool closedBeforeClick = false;
            liveAction.Click += (_, _) => closedBeforeClick = !menu.IsOpen;
            ((IInvokeProvider)new MenuItemAutomationPeer(liveAction).GetPattern(PatternInterface.Invoke)).Invoke();
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while ((live.Calls < 2 || owner.Running != null) && DateTime.UtcNow < deadline) await Task.Delay(10);
            require(live.Calls == 2 && live.LastSql == "SELECT TOP 100 * FROM [live_db].[sales].[orders]", "A chosen object action executes only its resolved database and owner on the captured session");
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            require(!menu.IsOpen && closedBeforeClick, "Deferred WPF object clicks remain valid after logical popup dismissal");
        }
        finally
        {
            menu.IsOpen = false; CloseEditorObjectMenu(menu);
            owner.Options = AseSessionOptions.Default; owner.Changing = false; owner.SavedText = owner.Document.Text;
            if (owner.Running == null && owner.CatalogActions == 0) await CloseSessionAsync(owner);
            SelectSession(original);
        }
    }
}
