using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AseIsql.Odbc;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private sealed class EditorObjectRequest(ContextMenu menu, SessionWorkspace owner, ObjectTarget? target, long generation)
    {
        public readonly ContextMenu Menu = menu;
        public readonly SessionWorkspace Owner = owner;
        public readonly IQueryEngine Engine = owner.Engine;
        public readonly string? CachedDatabase = (owner.Engine as ISessionFacts)?.Facts.Database;
        public readonly long RunCounter = owner.RunCounter;
        public readonly ObjectTarget? Target = target;
        public readonly long Generation = generation;
        public readonly CancellationTokenSource Cancellation = new();
        public MenuItem? Placeholder;
        public bool Closed;
    }
    private sealed record EditorObjectResult(CatalogObject? Object, string ContextDatabase, string Notice);
    // WPF closes a menu before raising its deferred public Click event. Capture only a genuine activation
    // while the request is live, then recheck connection ownership when the deferred callback arrives.
    private sealed class ResolvedObjectMenuItem(Func<bool> canActivate) : MenuItem
    {
        private bool activation;
        protected override void OnClick() { activation = canActivate(); base.OnClick(); }
        public bool TakeActivation() { bool result = activation; activation = false; return result; }
    }
    private EditorObjectRequest? editorObjectRequest;
    private long editorObjectGeneration;

    private void BeginEditorObjectMenu(ContextMenu menu)
    {
        if (editorObjectRequest is { } previous) { previous.Closed = true; previous.Cancellation.Cancel(); }
        var target = ParseObjectToken(Editor.SelectionLength > 0 ? Editor.SelectedText : IdentifierAtCaret());
        editorObjectRequest = new EditorObjectRequest(menu, activeSession, target, ++editorObjectGeneration);
    }
    private void CloseEditorObjectMenu(ContextMenu menu)
    {
        if (editorObjectRequest is not { } request || request.Menu != menu) return;
        request.Closed = true;
        request.Cancellation.Cancel();
    }
    private void AddEditorObjectPlaceholder(ContextMenu menu, ObjectTarget? target)
    {
        if (target == null || editorObjectRequest is not { } request) return;
        var header = new MenuItem { Header = "Object · " + target.Display, IsEnabled = false, FontWeight = FontWeights.SemiBold };
        menu.Items.Add(header);
        request.Placeholder = new MenuItem { Header = EditorObjectUnavailable(request) ?? "Checking database, owner and object type…", IsEnabled = false };
        menu.Items.Add(request.Placeholder);
        menu.Items.Add(new Separator());
    }
    private static string? EditorObjectUnavailable(EditorObjectRequest request)
    {
        if (request.Target == null) return "Select one object identifier to inspect it.";
        if (request.Engine is DisconnectedQueryEngine || request.Engine is OdbcSession { IsOpen: false }) return "Connect to inspect this object.";
        if (request.Owner.Busy) return "Object lookup unavailable while " + request.Owner.BusyReason + ".";
        if (request.Owner.Options.NoExec) return "Object lookup unavailable while NOEXEC is enabled.";
        if (PlatformOf(request.Owner) == ServerPlatform.Rs) return "Replication Server has no relational object catalog.";
        if (PlatformOf(request.Owner) == ServerPlatform.Iq && request.Target.Database != null) return "SAP IQ requires owner.name, without a database part.";
        if (request.Target.Name.StartsWith('#')) return "Temporary object lookup is unavailable; use its session's catalog explicitly.";
        return null;
    }
    private bool EditorObjectOwned(EditorObjectRequest request) =>
        ReferenceEquals(editorObjectRequest, request) && request.Generation == editorObjectGeneration && !request.Closed &&
        ReferenceEquals(activeSession, request.Owner) &&
        ReferenceEquals(request.Owner.Engine, request.Engine) && sessions.Contains(request.Owner);
    private bool EditorObjectCurrent(EditorObjectRequest request) => EditorObjectOwned(request) && !request.Cancellation.IsCancellationRequested;

    private async Task ResolveEditorObjectMenu(ContextMenu menu)
    {
        var request = editorObjectRequest;
        if (request == null || request.Menu != menu || request.Placeholder == null || EditorObjectUnavailable(request) != null) return;
        var result = await ReadEditorObject(request);
        if (!EditorObjectOwned(request) || !menu.IsOpen) return;
        var placeholder = request.Placeholder;
        if (result.Object == null) { placeholder.Header = result.Notice; return; }
        int index = menu.Items.IndexOf(placeholder);
        if (index < 0) return;
        menu.Items.RemoveAt(index);
        var actions = new ContextMenu();
        AddResolvedEditorObjectActions(actions, request, result.Object, result.ContextDatabase);
        foreach (var action in actions.Items.Cast<object>().ToArray()) { actions.Items.Remove(action); menu.Items.Insert(index++, action); }
        ((MenuItem)menu.Items[0]).Header = "Object · " + result.Object.Database + "." + result.Object.Owner + "." + result.Object.Name + " · " + result.Object.Type;
    }

    // A metadata read shares the user's execution lane and never clears Messages or publishes result tabs.
    // Unlike ReadCatalog, this path must leave the user's last execution diagnostics untouched.
    private async Task<EditorObjectResult> ReadEditorObject(EditorObjectRequest request)
    {
        if (!EditorObjectCurrent(request)) return new(null, "", "Object lookup cancelled.");
        if (EditorObjectUnavailable(request) is { } reason) return new(null, "", reason);
        try
        {
            var output = await ReadEditorMetadata(request.Owner, request.Engine,
                EditorObjectLookupSql(PlatformOf(request.Owner), request.Target!), request.Cancellation);
            request.Cancellation.Token.ThrowIfCancellationRequested();
            return ParseEditorObjectResult(output, request.Target!, PlatformOf(request.Owner));
        }
        catch (Exception ex) when (ex is OperationCanceledException or OdbcException or InvalidOperationException or ArgumentException or FormatException or InvalidCastException or OverflowException)
        { return new(null, "", request.Cancellation.IsCancellationRequested ? "Object lookup cancelled or timed out." : "Object metadata unavailable; check catalog access and session state."); }
    }
    private async Task<QueryOutput> ReadEditorMetadata(SessionWorkspace owner, IQueryEngine source, string sql, CancellationTokenSource cancellation, bool preserveCompletion = false)
    {
        if (owner.Busy || !ReferenceEquals(owner.Engine, source)) throw new InvalidOperationException("The session is busy or changed.");
        owner.CatalogActions++;
        owner.Running = cancellation;
        cancellation.CancelAfter(TimeSpan.FromSeconds(5));
        RefreshFor(owner, preserveCompletion);
        try
        {
            return await Task.Run(() => source is OdbcSession session
                ? session.Execute(sql, cancellation.Token, userRun: false) : source.Execute(sql, cancellation.Token));
        }
        finally
        {
            cancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            if (ReferenceEquals(owner.Running, cancellation)) owner.Running = null;
            owner.CatalogActions--;
            RefreshFor(owner, preserveCompletion);
        }
    }
    private static string EditorObjectLookupSql(ServerPlatform platform, ObjectTarget target)
    {
        string name = ResultFormats.Literal(target.Name);
        string owner = target.Owner == null ? "" : " and u." + (platform == ServerPlatform.Iq ? "user_name" : "name") + " = " + ResultFormats.Literal(target.Owner);
        string candidates;
        if (platform == ServerPlatform.Ase)
        {
            string prefix = target.Database == null ? "" : ResultFormats.Identifier(target.Database) + "..";
            candidates = "select top 2 o.id, u.name as owner, o.name, o.type from " + prefix + "sysobjects o join " + prefix + "sysusers u on u.uid = o.uid where o.name = " + name + owner;
        }
        else if (platform == ServerPlatform.Iq && target.Database == null)
            candidates = "select top 2 id, owner, name, type from (select t.table_id as id, u.user_name as owner, t.table_name as name, case when t.table_type_str in ('VIEW','MAT VIEW') then 'V' when t.table_type_str in ('BASE','GBL TEMP') then 'U' else '?' end as type from SYS.SYSTAB t join SYS.SYSUSER u on u.user_id = t.creator where t.table_name = " + name + owner +
                " union all select p.proc_id, u.user_name, p.proc_name, 'P' from SYS.SYSPROCEDURE p join SYS.SYSUSER u on u.user_id = p.creator where p.proc_name = " + name + owner + ") as candidates";
        else throw new ArgumentException("This platform or object qualification has no supported catalog lookup.");
        // IF is evaluated before SELECT, which could otherwise start a transaction in chained mode.
        // IQ's @@trancount alone does not detect implicit transactions: require unchained mode too.
        return GuardedEditorMetadataSql(candidates);
    }
    private static string GuardedEditorMetadataSql(string candidates) =>
        "/* dbmon_execio: object metadata only */\nif @@tranchained = 0 and @@trancount = 0 begin\nselect db_name() as context_database;\n" + candidates + ";\nend";
    private static EditorObjectResult ParseEditorObjectResult(QueryOutput output, ObjectTarget target, ServerPlatform platform)
    {
        if (output.Error != null || output.OmittedMessages != 0 || output.Tables.Count != 2)
            return new(null, "", "Object metadata unavailable: transaction mode, permissions or catalog access may prevent lookup.");
        var context = output.Tables[0];
        if (context.Columns.Count != 1 || context.Rows.Count != 1 || context.Rows[0][0] is not string { Length: > 0 } database)
            return new(null, "", "The server did not return the effective database.");
        var rows = ParseCatalogObjects(output.Tables[1], target.Database ?? database, platform);
        if (rows.Count == 0) return new(null, database, "No visible matching object; it may be absent or hidden by permissions.");
        if (rows.Count != 1) return new(null, database, "More than one owner or object matches; qualify the name explicitly.");
        var item = rows[0];
        if (item.Owner == null || item.Id <= 0 || string.IsNullOrEmpty(item.Name) || item.Type is not ("U" or "S" or "V" or "P" or "TR" or "SF") ||
            platform == ServerPlatform.Iq && item.Type is not ("U" or "V" or "P"))
            return new(null, database, "The catalog returned an unsupported or incomplete object type.");
        return new(item, database, "");
    }

    private bool EditorObjectActionAllowed(EditorObjectRequest request, string contextDatabase, bool activatedBeforeClose = false)
    {
        bool ownsConnection = ReferenceEquals(editorObjectRequest, request) && request.Generation == editorObjectGeneration &&
            ReferenceEquals(activeSession, request.Owner) && ReferenceEquals(request.Owner.Engine, request.Engine) && sessions.Contains(request.Owner);
        bool current = ownsConnection && (EditorObjectCurrent(request) || activatedBeforeClose) && !request.Owner.Busy &&
            (request.Engine is not OdbcSession session || session.IsOpen);
        // The catalog result is authoritative; cached facts may predate an executed USE.
        // Expire the action only when session state changes after this request was captured.
        current = current && request.Owner.RunCounter == request.RunCounter;
        if (current && request.Engine is ISessionFacts facts)
            current = string.Equals(facts.Facts.Database, request.CachedDatabase, StringComparison.Ordinal);
        if (!current) SetStatus(request.Owner, "Object action expired; open the menu again in the intended session.");
        return current;
    }
    private void AddResolvedEditorObjectActions(ItemsControl menu, EditorObjectRequest request, CatalogObject item, string contextDatabase)
    {
        var target = item.Target;
        void Add(string header, Action action)
        {
            var entry = new ResolvedObjectMenuItem(() => EditorObjectActionAllowed(request, contextDatabase)) { Header = header };
            entry.SetResourceReference(StyleProperty, typeof(MenuItem));
            entry.Click += (_, _) => { if (EditorObjectActionAllowed(request, contextDatabase, entry.TakeActivation())) action(); };
            menu.Items.Add(entry);
        }
        void Run(string header, string action) => Add(header, () => _ = ExecuteAsync(request.Owner, sqlOverride:
            ResolvedEditorHelperSql(action, item), label: header + " · " + target.Display));
        bool relation = item.Type is "U" or "S" or "V", table = item.Type is "U" or "S";
        if (relation) Run("Select TOP 100 rows", "top");
        if (item.Type is "P" or "V" or "TR" or "SF")
            Add("View code", () => RunCatalogAction(owner => ReferenceEquals(owner, request.Owner) && ReferenceEquals(owner.Engine, request.Engine)
                ? ObjectCode(owner, target, item.Platform == ServerPlatform.Iq ? IqCodeSql(item) : ResolvedEditorHelperSql("code", item)) : Task.CompletedTask, "Code of " + target.Display));
        if (item.Platform == ServerPlatform.Iq)
        {
            if (relation) Run("Columns · sp_iqcolumn", "columns");
            if (table) { Run("Indexes · sp_iqindex", "indexes"); Run("Size · sp_iqtablesize", "size"); }
        }
        else
        {
            Run("Describe · sp_help", "help"); Run("Dependencies · sp_depends", "depends");
            if (table) { Run("Indexes · sp_helpindex", "indexes"); Run("Rows and space · sp_spaceused", "space"); }
        }
        if (relation) Add("Write SELECT TOP 100 in the editor", () =>
        {
            if (!ConfirmDiscard() || !EditorObjectActionAllowed(request, contextDatabase, activatedBeforeClose: true)) return;
            SetDocument("SELECT TOP 100 *" + Environment.NewLine + "FROM " + target.From + ";" + Environment.NewLine, null);
        });
    }
    private static string ResolvedEditorHelperSql(string action, CatalogObject item)
    {
        var target = item.Target;
        if (item.Platform == ServerPlatform.Iq)
            return action == "size" ? "sp_iqtablesize " + ResultFormats.Literal(target.From) : IqObjectHelperSql(action, target);
        if (action == "code") return "select c.text from " + target.ProcedurePrefix + "syscomments c where c.id = " + item.Id.ToString(CultureInfo.InvariantCulture) + " order by c.number, c.colid";
        if (action == "top") return ObjectHelperSql(action, target);
        return ObjectHelperSql(action, target);
    }
}
