using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using AseIsql.Odbc;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // IQ names objects as owner.name inside its single database, so an IQ object never carries a database part.
    private sealed record CatalogObject(string Database, string? Owner, string Name, string Type, int Id, ServerPlatform Platform = ServerPlatform.Ase)
    {
        public string QualifiedName => Owner == null ? throw new ArgumentException("The catalog did not return an owner; the object cannot be resolved unambiguously.") :
            string.Join(".", (Platform == ServerPlatform.Iq ? new[]{Owner,Name} : new[]{Database,Owner,Name}).Select(ResultFormats.Identifier));
        public ObjectTarget Target => new(Platform == ServerPlatform.Iq ? null : Database, Owner, Name);
    }
    // An object named in the editor or chosen in the explorer. Database and owner stay unknown when not given,
    // so the server resolves them in the session's context instead of the client guessing a schema.
    public sealed record ObjectTarget(string? Database, string? Owner, string Name)
    {
        public string Display => Database != null ? Database + "." + (Owner ?? "") + "." + Name : Owner != null ? Owner + "." + Name : Name;
        public string ProcedurePrefix => Database != null ? UseTarget(Database) + ".." : "";
        public string ProcedureArgument => ResultFormats.Literal(Owner != null ? UseTarget(Owner) + "." + UseTarget(Name) : UseTarget(Name));
        public string ObjectIdArgument => ResultFormats.Literal(Display);
        public string From => Database != null ? ResultFormats.Identifier(Database) + "." + (Owner != null ? ResultFormats.Identifier(Owner) : "") + "." + ResultFormats.Identifier(Name) :
            Owner != null ? ResultFormats.Identifier(Owner) + "." + ResultFormats.Identifier(Name) : ResultFormats.Identifier(Name);
    }
    private const int CatalogPage = 100;
    private string browserFilter { get => activeSession.Filter; set => activeSession.Filter = value; }
    private static readonly (string Type, string Label)[] CatalogGroups = [("U","Tables"),("V","Views"),("P","Procedures"),("TR","Triggers"),("SF","Functions"),("S","System tables")];
    // IQ: base and global temporary tables, views, procedures (functions included) and the SYS/dbo system objects.
    private static readonly (string Type, string Label)[] IqCatalogGroups = [("U","Tables"),("V","Views"),("P","Procedures"),("S","System objects")];
    private static IReadOnlyList<(string Type, string Label)> CatalogGroupsFor(ServerPlatform platform) => platform == ServerPlatform.Iq ? IqCatalogGroups : CatalogGroups;

    // Catalog actions start from tree and menu callbacks. Each one has an owner here: unexpected server shapes and
    // driver failures become a visible message instead of an unobserved task or an async-void crash.
    // A catalog request belongs to the session that asked for it, and the user may select another tab while the round
    // trip is in flight: the owner is handed to the action so nothing it publishes can land on the session on screen.
    private async void RunCatalogAction(Func<SessionWorkspace,Task> action,string context)
    {
        var owner=activeSession;
        if (owner.Repeat != null) { SetStatus(owner,"Stop repeating before browsing the catalog. The repeat is still active."); return; }
        owner.CatalogActions++;
        try { await action(owner); }
        catch(Exception ex) when(ex is FormatException or InvalidCastException or OverflowException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or OdbcException)
        { AddMessage(owner,1,"CLIENT",context+" failed: "+ex.Message); ShowMessages(owner); SetStatus(owner,context+" failed · see Messages"); }
        finally { owner.CatalogActions--; RefreshFor(owner); }
    }
    private async Task<QueryOutput?> ReadCatalog(SessionWorkspace owner,string sql,string label="Catalog")
    {
        if(owner.Running!=null||owner.Changing) { SetStatus(owner,"Wait for this session's current operation to finish, then reload the catalog."); return null; }
        if(owner.Repeat!=null) { SetStatus(owner,"Stop repeating before browsing the catalog. The repeat is still active."); return null; }
        if(owner.Engine is not OdbcSession { IsOpen:true } session) { SetStatus(owner,"The catalog requires a connected session."); return null; }
        // Same publication rules as an execution: Messages shows this catalog request only, with omissions and raw evidence.
        using var cancellation=new CancellationTokenSource(); owner.Running=cancellation; RefreshFor(owner);
        ClearMessages(owner); AddMessage(owner,0,"CLIENT",label+" · "+owner.Engine.Name);
        try {
            string tagged = label == "Session options" ? sql : "/* dbmon_execio */\n" + sql;
            var output=await Task.Run(()=>session.Execute(tagged,cancellation.Token,userRun:false));
            foreach(var message in output.Messages) AddMessage(owner,message.Number,message.Source,message.Text,message.RawText);
            if(output.OmittedMessages>0) AddMessage(owner,0,"CLIENT",$"{output.OmittedMessages} earlier messages were omitted by the retention limit.");
            if(output.Error!=null) { AddMessage(owner,1,output.ErrorSource,output.Error,output.ErrorRawText); ShowMessages(owner); SetStatus(owner,"Catalog partial/unavailable: "+output.Error); if(output.Tables.Count==0)return null; }
            return output;
        } catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OperationCanceledException or OdbcException) { AddMessage(owner,1,"CLIENT","Catalog request failed: "+ex.Message); SetStatus(owner,ex.Message); return null; }
        finally { owner.Running=null; RefreshFor(owner); }
    }
    private void ReloadBrowserClick(object sender,RoutedEventArgs e)=>RunCatalogAction(LoadBrowser,"Catalog reload");
    private void BrowserSearchKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(e.Key==System.Windows.Input.Key.Escape){BrowserSearch.Text="";}
        else if(e.Key!=System.Windows.Input.Key.Enter)return;
        e.Handled=true;browserFilter=BrowserSearch.Text.Trim();
        // Expanded databases reload their groups with the new filter; collapsed ones apply it when opened.
        foreach(var node in BrowserTree.Items.OfType<TreeViewItem>().Where(n=>n.Tag is string)){
            bool wasExpanded=node.IsExpanded;ResetDatabaseNode(node);
            if(wasExpanded){node.IsExpanded=true;if(browserFilter.Length>0)foreach(var group in node.Items.OfType<TreeViewItem>())group.IsExpanded=true;}
        }
        StatusText.Text=browserFilter.Length==0?"Object filter cleared":"Object filter · names containing \""+browserFilter+"\"";
    }
    private async Task LoadBrowser(SessionWorkspace owner)
    {
        if(owner.Engine is not OdbcSession { IsOpen:true }) { PublishCatalog(owner,[new TreeViewItem { Header="Connect to browse databases." }]); return; }
        var platform=PlatformOf(owner);
        if(platform==ServerPlatform.Rs) { PublishCatalog(owner,[new TreeViewItem { Header="Replication Server has no catalog to browse. Use the RS recipes: admin who, admin health." }]); return; }
        // IQ serves one database: the tree starts at it without a query, and its groups page on demand.
        if(platform==ServerPlatform.Iq) {
            string iqDatabase=(owner.Engine as ISessionFacts)?.Facts.Database is {Length:>0} name?name:"IQ database";
            var root=new TreeViewItem { Header=iqDatabase, Tag=iqDatabase }; ResetDatabaseNode(root,owner);
            PublishCatalog(owner,[root]); root.IsExpanded=true;
            return;
        }
        var output=await ReadCatalog(owner,DatabasesSql);
        if(output==null) return;
        RememberDatabases(owner,output);
        if(output.Tables.Count==0||output.Tables[0].Columns.Count<1) { PublishCatalog(owner,[new TreeViewItem {Header="No catalog data"}]); return; }
        var nodes=new List<TreeViewItem>();
        foreach(DataRow row in output.Tables[0].Rows) {
            string name=Convert.ToString(row[0],CultureInfo.InvariantCulture)??""; if(name.Length==0) continue;
            var node=new TreeViewItem { Header=name, Tag=name }; ResetDatabaseNode(node,owner);
            nodes.Add(node);
        }
        if(output.Error!=null)nodes.Add(new TreeViewItem {Header="Partial list · "+output.Error});
        PublishCatalog(owner,nodes);
    }
    // The explorer tree is the session's own: it is kept on the workspace and mirrored into the shell only while that
    // tab is on screen, so a catalog read that lands after a tab switch never overwrites another session's tree.
    private void PublishCatalog(SessionWorkspace owner,IReadOnlyList<TreeViewItem> nodes)
    {
        owner.Catalog.Clear(); owner.Catalog.AddRange(nodes);
        if(!IsOnScreen(owner))return;
        BrowserTree.Items.Clear();
        foreach(var node in nodes)BrowserTree.Items.Add(node);
    }
    // Opening a database costs no query: it lists object groups, and each group pages its own objects on demand.
    private void ResetDatabaseNode(TreeViewItem node)=>ResetDatabaseNode(node,activeSession);
    private void ResetDatabaseNode(TreeViewItem node,SessionWorkspace owner)
    {
        string database=(string)node.Tag;node.Items.Clear();node.IsExpanded=false;
        var platform=PlatformOf(owner);
        foreach(var (type,label) in CatalogGroupsFor(platform)){
            var group=new TreeViewItem {Header=label+(owner.Filter.Length>0?" · \""+owner.Filter+"\"":"")};group.Items.Add(new TreeViewItem {Header="Expand to load"});
            bool loading=false,loaded=false;
            group.Expanded+=(_,args)=> {
                if(args.OriginalSource!=group||loaded||loading||activeSession!=owner||owner.Repeat!=null)return; loading=true;
                RunCatalogAction(async _=> {try{loaded=await LoadObjectPage(owner,database,type,label,group,null,platform);}finally{loading=false;}},label+" of "+database);
            };
            node.Items.Add(group);
        }
    }
    // Keyset paging: the next page starts after the last (name, id) shown, so pages stay stable without OFFSET.
    private static string ObjectsPageSql(string database,string type,string filter,CatalogObject? after)
    {
        string db=ResultFormats.Identifier(database);
        string where="o.type = "+ResultFormats.Literal(type);
        if(filter.Length>0) where+=" and o.name like "+ResultFormats.Literal("%"+LikeEscape(filter)+"%")+" escape '\\'";
        if(after!=null) where+=$" and (o.name > {ResultFormats.Literal(after.Name)} or (o.name = {ResultFormats.Literal(after.Name)} and o.id > {after.Id.ToString(CultureInfo.InvariantCulture)}))";
        return $"select top {CatalogPage+1} o.id, u.name as owner, o.name, o.type from {db}..sysobjects o left join {db}..sysusers u on u.uid=o.uid where {where} order by o.name, o.id";
    }
    // SQL Anywhere catalog of the IQ database: SYSTAB/SYSUSER for tables and views, SYSPROCEDURE for procedures.
    // The same keyset continuation and escaped LIKE filter as the ASE explorer.
    private static string IqObjectsPageSql(string type,string filter,CatalogObject? after)
    {
        bool procedures=type=="P";
        string id=procedures?"p.proc_id":"t.table_id",name=procedures?"p.proc_name":"t.table_name";
        string where=type switch {
            "U"=>"t.table_type_str in ('BASE','GBL TEMP') and u.user_name not in ('SYS','dbo')",
            "V"=>"t.table_type_str in ('VIEW','MAT VIEW') and u.user_name not in ('SYS','dbo')",
            "P"=>"u.user_name not in ('SYS','dbo')",
            "S"=>"u.user_name in ('SYS','dbo')",
            _=>throw new ArgumentException("Unknown IQ object group.")};
        if(filter.Length>0) where+=" and "+name+" like "+ResultFormats.Literal("%"+LikeEscape(filter)+"%")+" escape '\\'";
        if(after!=null) where+=$" and ({name} > {ResultFormats.Literal(after.Name)} or ({name} = {ResultFormats.Literal(after.Name)} and {id} > {after.Id.ToString(CultureInfo.InvariantCulture)}))";
        string kind=type=="S"?"case when t.table_type_str in ('VIEW','MAT VIEW') then 'V' else 'S' end":ResultFormats.Literal(type);
        return procedures
            ? $"select top {CatalogPage+1} p.proc_id as id, u.user_name as owner, p.proc_name as name, 'P' as type from SYS.SYSPROCEDURE p join SYS.SYSUSER u on u.user_id = p.creator where {where} order by p.proc_name, p.proc_id"
            : $"select top {CatalogPage+1} t.table_id as id, u.user_name as owner, t.table_name as name, {kind} as type from SYS.SYSTAB t join SYS.SYSUSER u on u.user_id = t.creator where {where} order by t.table_name, t.table_id";
    }
    private static string LikeEscape(string text)=>text.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_").Replace("[","\\[");
    private static string ObjectsSql(string database)=>ObjectsPageSql(database,"U","",null);
    private static (IReadOnlyList<CatalogObject> Page,bool More) PageObjects(List<CatalogObject> rows)=>(rows.Take(CatalogPage).ToArray(),rows.Count>CatalogPage);
    // The whole result is validated before any node is built, so a malformed shape never leaves a half-filled tree.
    private static List<CatalogObject> ParseCatalogObjects(DataTable table,string database,ServerPlatform platform=ServerPlatform.Ase)
    {
        if(table.Columns.Count<4) throw new FormatException($"The object catalog returned {table.Columns.Count} columns; expected id, owner, name and type.");
        var objects=new List<CatalogObject>(table.Rows.Count);
        foreach(DataRow row in table.Rows) {
            if(row[0] is DBNull||row[2] is DBNull||row[3] is DBNull) throw new FormatException("The object catalog returned a row without id, name or type.");
            int id=Convert.ToInt32(row[0],CultureInfo.InvariantCulture);
            string name=Convert.ToString(row[2],CultureInfo.InvariantCulture)??"",type=Convert.ToString(row[3],CultureInfo.InvariantCulture)?.Trim()??"";
            string? owner=row[1] is DBNull?null:Convert.ToString(row[1],CultureInfo.InvariantCulture)?.Trim();
            objects.Add(new CatalogObject(database,string.IsNullOrEmpty(owner)?null:owner,name,type,id,platform));
        }
        return objects;
    }
    private async Task<bool> LoadObjectPage(SessionWorkspace owner,string database,string type,string label,TreeViewItem group,CatalogObject? after,ServerPlatform platform=ServerPlatform.Ase)
    {
        string filter=owner.Filter;
        var output=await ReadCatalog(owner,platform==ServerPlatform.Iq?IqObjectsPageSql(type,filter,after):ObjectsPageSql(database,type,filter,after));
        if(output==null)return false;
        var (page,more)=PageObjects(output.Tables.Count==0?[]:ParseCatalogObjects(output.Tables[0],database,platform));
        if(after==null)group.Items.Clear();
        else foreach(var stale in group.Items.OfType<TreeViewItem>().Where(item=>item.Tag is "more").ToArray())group.Items.Remove(stale);
        foreach(var item in page)group.Items.Add(ObjectNode(item));
        int shown=group.Items.OfType<TreeViewItem>().Count(item=>item.Tag is CatalogObject);
        group.Header=label+" · "+shown+(more?"+":"")+(filter.Length>0?" · \""+filter+"\"":"");
        if(shown==0)group.Items.Add(new TreeViewItem {Header=filter.Length>0?"No names contain \""+filter+"\"":"No objects"});
        if(more){
            var button=new Button {Content=$"Show {CatalogPage} more…",Style=(Style)FindResource("QuietButton"),Margin=new Thickness(0),Padding=new Thickness(4,2,4,2)};
            var last=page[^1];button.Click+=(_,_)=> {RunCatalogAction(async target=> {if(!ReferenceEquals(target,owner))return;button.IsEnabled=false;if(!await LoadObjectPage(owner,database,type,label,group,last,platform))button.IsEnabled=true;},"More "+label);};
            group.Items.Add(new TreeViewItem {Header=button,Tag="more",Focusable=false});
        }
        if(output.Error!=null)group.Items.Add(new TreeViewItem {Header="Partial list · "+output.Error});
        return true;
    }
    private TreeViewItem ObjectNode(CatalogObject item)
    {
        var node=new TreeViewItem { Header=(item.Owner??"?")+"."+item.Name,Tag=item,ToolTip=item.Database+"."+(item.Owner??"?")+"."+item.Name };
        var menu=new ContextMenu();
        var insert=new MenuItem {Header="Insert object name"};insert.Click+=(_,_)=> {try{QueryClick(this,new RoutedEventArgs());Editor.Document.Replace(Editor.SelectionStart,Editor.SelectionLength,item.QualifiedName);}catch(ArgumentException ex){ShowError(ex.Message);}};menu.Items.Add(insert);
        menu.Items.Add(new Separator());
        if(item.Platform==ServerPlatform.Iq)AddIqObjectActions(menu,item.Target,item.Type,item);else AddObjectActions(menu,item.Target,item.Type);
        node.ContextMenu=menu;
        node.MouseDoubleClick+=(_,args)=> {if(args.Source!=node)return;args.Handled=true;try{QueryClick(this,new RoutedEventArgs());Editor.Document.Replace(Editor.SelectionStart,Editor.SelectionLength,item.QualifiedName);}catch(ArgumentException ex){ShowError(ex.Message);}};
        return node;
    }
    // One action set for explorer nodes and identifiers selected in the editor. Read-only helpers run as their own
    // execution (results and Messages as usual); the editor text is never replaced unless the action says so.
    private void AddObjectActions(ItemsControl menu,ObjectTarget target,string? type=null)
    {
        bool connected=engine is OdbcSession { IsOpen:true };
        void Add(string header,string? gesture,Action action,bool needsSession=true)
        {
            var item=new MenuItem {Header=header,InputGestureText=gesture??"",IsEnabled=!needsSession||connected};
            item.Click+=(_,_)=> {try{action();}catch(ArgumentException ex){ShowError(ex.Message);}};menu.Items.Add(item);
        }
        bool relation=type is null or "U" or "S" or "V",code=type is null or "P" or "V" or "TR" or "SF";
        if(relation)Add("Select TOP 100 rows",null,()=>RunHelper(ObjectHelperSql("top",target),"TOP 100 · "+target.Display));
        if(code)Add("View code",null,()=>RunCatalogAction(owner=>ObjectCode(owner,target),"Code of "+target.Display));
        Add("Describe · sp_help",null,()=>RunHelper(ObjectHelperSql("help",target),"sp_help · "+target.Display));
        Add("Dependencies · sp_depends",null,()=>RunHelper(ObjectHelperSql("depends",target),"sp_depends · "+target.Display));
        if(relation){Add("Indexes · sp_helpindex",null,()=>RunHelper(ObjectHelperSql("indexes",target),"sp_helpindex · "+target.Display));Add("Rows and space · sp_spaceused",null,()=>RunHelper(ObjectHelperSql("space",target),"sp_spaceused · "+target.Display));}
        if(relation)Add("Write SELECT TOP 100 in the editor",null,()=> {if(!ConfirmDiscard())return;SetDocument("SELECT TOP 100 *"+Environment.NewLine+"FROM "+target.From+";"+Environment.NewLine,null);},needsSession:false);
        if(!connected)menu.Items.Add(new MenuItem {Header="Connect to inspect objects on the server",IsEnabled=false});
    }
    // IQ helpers: sp_iq* procedures instead of sp_help/sp_depends; code comes from SYSVIEW/SYSPROCEDURE by catalog id,
    // so it is offered only for explorer nodes, which carry that id.
    private void AddIqObjectActions(ItemsControl menu,ObjectTarget target,string? type,CatalogObject? item=null)
    {
        bool connected=engine is OdbcSession { IsOpen:true };
        void Add(string header,Action action,bool needsSession=true)
        {
            var entry=new MenuItem {Header=header,IsEnabled=!needsSession||connected};
            entry.Click+=(_,_)=> {try{action();}catch(ArgumentException ex){ShowError(ex.Message);}};menu.Items.Add(entry);
        }
        bool relation=type is null or "U" or "S" or "V",table=type is null or "U" or "S";
        if(relation)Add("Select TOP 100 rows",()=>RunHelper(IqObjectHelperSql("top",target),"TOP 100 · "+target.Display));
        if(item!=null&&type is "V" or "P")Add("View code",()=>RunCatalogAction(owner=>ObjectCode(owner,target,IqCodeSql(item)),"Code of "+target.Display));
        if(relation)Add("Columns · sp_iqcolumn",()=>RunHelper(IqObjectHelperSql("columns",target),"sp_iqcolumn · "+target.Display));
        if(table){Add("Indexes · sp_iqindex",()=>RunHelper(IqObjectHelperSql("indexes",target),"sp_iqindex · "+target.Display));Add("Size · sp_iqtablesize",()=>RunHelper(IqObjectHelperSql("size",target),"sp_iqtablesize · "+target.Display));}
        if(relation)Add("Write SELECT TOP 100 in the editor",()=> {if(!ConfirmDiscard())return;SetDocument("SELECT TOP 100 *"+Environment.NewLine+"FROM "+target.From+";"+Environment.NewLine,null);},needsSession:false);
        if(!connected)menu.Items.Add(new MenuItem {Header="Connect to inspect objects on the server",IsEnabled=false});
    }
    private static string IqObjectHelperSql(string action,ObjectTarget target)
    {
        if(target.Database!=null)throw new ArgumentException("SAP IQ names objects as owner.name; remove the database part.");
        string name=ResultFormats.Literal(target.Name),owner=target.Owner==null?"null":ResultFormats.Literal(target.Owner);
        return action switch {
            "top"=>"SELECT TOP 100 * FROM "+target.From,
            "columns"=>"sp_iqcolumn "+name+", "+owner,
            "indexes"=>"sp_iqindex "+name+(target.Owner==null?"":", null, "+owner),
            "size"=>"sp_iqtablesize "+ResultFormats.Literal(target.Owner==null?target.Name:target.Owner+"."+target.Name),
            _=>throw new ArgumentException("Unknown object action.")};
    }
    private static string IqCodeSql(CatalogObject item)=>item.Type=="P"
        ? "select p.proc_defn from SYS.SYSPROCEDURE p where p.proc_id = "+item.Id.ToString(CultureInfo.InvariantCulture)
        : "select v.view_def from SYS.SYSVIEW v join SYS.SYSTAB t on t.object_id = v.view_object_id where t.table_id = "+item.Id.ToString(CultureInfo.InvariantCulture);
    private static string ObjectHelperSql(string action,ObjectTarget target)=>action switch {
        "top"=>"SELECT TOP 100 * FROM "+target.From,
        "help"=>"exec "+target.ProcedurePrefix+"sp_help "+target.ProcedureArgument,
        "depends"=>"exec "+target.ProcedurePrefix+"sp_depends "+target.ProcedureArgument,
        "indexes"=>"exec "+target.ProcedurePrefix+"sp_helpindex "+target.ProcedureArgument,
        "space"=>"exec "+target.ProcedurePrefix+"sp_spaceused "+target.ProcedureArgument,
        "code"=>"select c.text from "+target.ProcedurePrefix+"syscomments c where c.id = object_id("+target.ObjectIdArgument+") order by c.number, c.colid",
        _=>throw new ArgumentException("Unknown object action.")};
    private void RunHelper(string sql,string label)
    {
        if(running!=null||changingSession){StatusText.Text="Wait for this session's current operation to finish.";return;}
        _=ExecuteAsync(sqlOverride:sql,label:label);
    }
    private async Task ObjectCode(SessionWorkspace owner,ObjectTarget target,string? sql=null)
    {
        var result=await ReadCatalog(owner,sql??ObjectHelperSql("code",target));
        if(result?.Tables.Count>0) {
            if(result.Tables[0].Columns.Count<1) throw new FormatException("The definition catalog returned no text column.");
            var text=string.Concat(result.Tables[0].Rows.Cast<DataRow>().Select(row=>Convert.ToString(row[0],CultureInfo.InvariantCulture)));
            TextWindow("Code · "+target.Display,(result.Error==null?"":"PARTIAL TEXT: "+result.Error+"\n\n")+(text.Length==0?"No text available: the object does not exist here, has no stored text, is protected or is not visible to this login.":text));
        }
    }
    // Resolves the token under the selection or caret: name, owner.name, db.owner.name, db..name or [quoted] parts.
    private static readonly Regex ObjectToken=new(@"^(?:\[[^\]]+\]|""[^""]+""|[A-Za-z_#@$][\w#@$]*)(?:\.{1,2}(?:\[[^\]]+\]|""[^""]+""|[A-Za-z_#@$][\w#@$]*)){0,2}$",RegexOptions.CultureInvariant);
    public static ObjectTarget? ParseObjectToken(string? text)
    {
        text=text?.Trim();
        if(string.IsNullOrEmpty(text)||text.Length>300||!ObjectToken.IsMatch(text))return null;
        if(text.StartsWith('@'))return null;
        int doubled=text.IndexOf("..",StringComparison.Ordinal);
        try{
            if(doubled>0){var db=ResultFormats.QualifiedName(text[..doubled]);var name=ResultFormats.QualifiedName(text[(doubled+2)..]);return db.Length==1&&name.Length==1?new ObjectTarget(db[0],null,name[0]):null;}
            var parts=ResultFormats.QualifiedName(text);
            return parts.Length switch {1=>new ObjectTarget(null,null,parts[0]),2=>new ObjectTarget(null,parts[0],parts[1]),3=>new ObjectTarget(parts[0],parts[1],parts[2]),_=>null};
        }catch(ArgumentException){return null;}
    }
}
