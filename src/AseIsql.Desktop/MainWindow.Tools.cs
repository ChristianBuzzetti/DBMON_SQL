using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private MessageBoxResult ShowMessage(string title,string text,bool unsaved=false)
    {
        var window=new Window {Owner=this,Title=title,Width=580,SizeToContent=SizeToContent.Height,MaxHeight=640,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new DockPanel {Margin=new Thickness(20)};
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)}; DockPanel.SetDock(buttons,Dock.Bottom);layout.Children.Add(buttons);
        MessageBoxResult result=MessageBoxResult.Cancel;
        void Add(string caption,MessageBoxResult value,bool cancel=false,bool primary=false){var button=new Button {Content=caption,IsCancel=cancel,IsDefault=primary};button.Click+=(_,_)=>{result=value;window.Close();};buttons.Children.Add(button);}
        if(unsaved){Add("Discard",MessageBoxResult.No);Add("Cancel",MessageBoxResult.Cancel,true);Add("Save",MessageBoxResult.Yes,primary:true);}else Add("Close",MessageBoxResult.OK,true,true);
        layout.Children.Add(new ScrollViewer {MaxHeight=500,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap}});window.Content=layout;window.ShowDialog();return result;
    }
    // Engine is the platform tag (ASE, IQ, RS) a recipe is written for; null means any platform, as for My recipes.
    private sealed record LibraryItem(string Name, string Sql, string Category, string Description = "", string? Engine = null)
    {
        public override string ToString() => Engine == null ? Name : Name + "  ·  " + Engine;
        public bool Fits(ServerPlatform platform) => Engine == null || Engine.Equals(platform.Tag(), StringComparison.OrdinalIgnoreCase);
    }
    private readonly List<LibraryItem> favorites = [];
    private readonly HashSet<DataTable> pinnedResults = [];
    private readonly Dictionary<DataTable, (string Sql, string Source)> resultOrigins = [];
    private string executingSql = "";
    private readonly List<ICSharpCode.AvalonEdit.Document.TextAnchor> bookmarks=[];
    private void ToggleBookmark()
    {
        QueryClick(this,new RoutedEventArgs()); bookmarks.RemoveAll(anchor=>anchor.IsDeleted);
        var line=Editor.Document.GetLineByNumber(Editor.TextArea.Caret.Line);
        var current=bookmarks.FirstOrDefault(anchor=>Editor.Document.GetLineByOffset(anchor.Offset).LineNumber==line.LineNumber);
        if(current!=null) bookmarks.Remove(current); else bookmarks.Add(Editor.Document.CreateAnchor(line.Offset));
        StatusText.Text=bookmarks.Count+" bookmarks · F2 next / Shift+F2 previous";
    }
    private void GoBookmark(bool previous)
    {
        bookmarks.RemoveAll(anchor=>anchor.IsDeleted); if(bookmarks.Count==0) return;
        int position=Editor.TextArea.Caret.Offset;
        var ordered=bookmarks.OrderBy(anchor=>anchor.Offset).ToArray();
        var target=previous ? ordered.LastOrDefault(anchor=>anchor.Offset<position)??ordered[^1] : ordered.FirstOrDefault(anchor=>anchor.Offset>position)??ordered[0];
        QueryClick(this,new RoutedEventArgs()); Editor.Select(target.Offset,0); Editor.TextArea.Caret.Offset=target.Offset; Editor.ScrollToLine(Editor.TextArea.Caret.Line);
    }
    private void BookmarkClick(object sender,RoutedEventArgs e)=>ToggleBookmark();
    private void NextBookmarkClick(object sender,RoutedEventArgs e)=>GoBookmark(false);
    private string LibraryPath => isSmoke?Path.Combine(AppContext.BaseDirectory,"UiSmoke","library.json"):DataFolder.File("library.json");
    private const string UserRecipeCategory = "My recipes";
    private const int MaxUserRecipes = 1000;
    private static List<LibraryItem> ReadUserRecipes(string path)
    {
        if(!File.Exists(path)) return [];
        if(new FileInfo(path).Length>4*1024*1024)throw new IOException("The library exceeds 4 MiB.");
        var items=JsonSerializer.Deserialize<List<LibraryItem>>(File.ReadAllText(path),new JsonSerializerOptions {UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow})??throw new JsonException("Empty library.");
        if(items.Count>MaxUserRecipes||items.Any(item=>item==null||string.IsNullOrWhiteSpace(item.Name)||item.Sql==null||item.Category==null))throw new JsonException("Invalid library format.");
        // Entries saved before the rename used the "Favorites" category.
        return items.Select(item=>item.Category=="Favorites"?item with {Category=UserRecipeCategory}:item).ToList();
    }
    private void SaveUserRecipes(List<LibraryItem> next)
    {
        if(next.Count>MaxUserRecipes)throw new IOException("The library supports up to 1,000 recipes.");
        string json = JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true });
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) throw new IOException("The library exceeds 4 MiB.");
        Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath)!); AtomicFiles.SaveUtf8(LibraryPath, json);
        favorites.Clear(); favorites.AddRange(next);
    }
    // Import accepts the export format, the local library file and the packaged catalog: an array of objects with
    // name and sql, plus optional category and description. Property names are case-insensitive; other fields are ignored.
    private static List<LibraryItem> ParseRecipeImport(string json)
    {
        using var source = JsonDocument.Parse(json);
        if(source.RootElement.ValueKind!=JsonValueKind.Array)throw new JsonException("The file must contain a JSON array of recipes.");
        if(source.RootElement.GetArrayLength()>MaxUserRecipes)throw new JsonException("The file contains more than 1,000 recipes.");
        var items=new List<LibraryItem>(); int index=0;
        foreach(var entry in source.RootElement.EnumerateArray())
        {
            index++;
            if(entry.ValueKind!=JsonValueKind.Object)throw new JsonException("Recipe "+index+" is not an object.");
            string? Field(string name,bool required)
            {
                foreach(var property in entry.EnumerateObject())
                    if(property.Name.Equals(name,StringComparison.OrdinalIgnoreCase))
                        return property.Value.ValueKind==JsonValueKind.String ? property.Value.GetString() : property.Value.ValueKind==JsonValueKind.Null&&!required ? null : throw new JsonException("Recipe "+index+": "+name+" must be text.");
                return required ? throw new JsonException("Recipe "+index+" has no "+name+".") : null;
            }
            string name=Field("name",true)!, sql=Field("sql",true)!;
            if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(sql))throw new JsonException("Recipe "+index+" has an empty name or SQL.");
            string category=Field("category",false) is {Length:>0} value && value!="Favorites" ? value : UserRecipeCategory;
            // An unknown or missing engine keeps the recipe available on every platform.
            string? engine=Platforms.TryParseTag(Field("engine",false),out var platform)?platform.Tag():null;
            items.Add(new LibraryItem(name.Trim(),sql,category,Field("description",false)??"",engine));
        }
        return items;
    }
    // Identical recipes (same name and SQL) are skipped; a different recipe with an existing name gets a numbered name.
    private static (List<LibraryItem> Recipes,int Added,int Skipped) MergeRecipes(IReadOnlyList<LibraryItem> existing,IEnumerable<LibraryItem> incoming)
    {
        var result=existing.ToList(); int added=0, skipped=0;
        foreach(var item in incoming)
        {
            if(result.Any(r=>r.Name==item.Name&&r.Sql==item.Sql)){skipped++;continue;}
            string name=item.Name; for(int n=2;result.Any(r=>r.Name.Equals(name,StringComparison.OrdinalIgnoreCase));n++)name=item.Name+" ("+n+")";
            result.Add(item with {Name=name}); added++;
        }
        return (result,added,skipped);
    }
    private static bool Confirm(Window owner,string title,string text,string action)
    {
        var window=new Window {Owner=owner,Title=title,Width=480,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new StackPanel {Margin=new Thickness(20)}; layout.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap});
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};
        var cancel=new Button {Content="Cancel",IsCancel=true,IsDefault=true}; var ok=new Button {Content=action}; ok.Click+=(_,_)=>window.DialogResult=true;
        buttons.Children.Add(ok); buttons.Children.Add(cancel); layout.Children.Add(buttons); window.Content=layout; return window.ShowDialog()==true;
    }

    private string? AskText(string title, string label, string value = "", Window? owner = null)
    {
        var window = new Window { Owner = owner ?? this, Title = title, Width = 520, Height = 185, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,8) });
        var input = new TextBox { Text = value }; panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        var ok = new Button { Content = "OK", IsDefault = true }; ok.Click += (_, _) => window.DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true }; buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        window.Content = panel; window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text : null;
    }
    private void TextWindow(string title, string text)
    {
        var window = new Window { Owner = this, Title = title, Width = 850, Height = 580, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Content = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(14) };
        ((TextBox)window.Content).SetResourceReference(Control.FontFamilyProperty,"MonoFont");window.Show();
    }
    private void Zoom(double delta) => ApplyAppearance(appearance with {EditorSize=Math.Clamp(Editor.FontSize+delta,9,32)});
    private void WrapClick(object sender, RoutedEventArgs e) => Editor.WordWrap = !Editor.WordWrap;
    private void ZoomInClick(object sender, RoutedEventArgs e) => Zoom(1);
    private void ZoomOutClick(object sender, RoutedEventArgs e) => Zoom(-1);
    private void ZoomResetClick(object sender, RoutedEventArgs e) => ApplyAppearance(appearance with {EditorSize=15});
    private void TransformSelection(Func<string, string> transform)
    {
        QueryClick(this, new RoutedEventArgs());
        if (Editor.SelectionLength == 0) { StatusText.Text = "Select text to transform."; return; }
        int start = Editor.SelectionStart; string replacement = transform(Editor.SelectedText);
        Editor.Document.Replace(start, Editor.SelectionLength, replacement); Editor.Select(start, replacement.Length);
    }
    private void UpperClick(object sender, RoutedEventArgs e) => TransformSelection(s => s.ToUpperInvariant());
    private void LowerClick(object sender, RoutedEventArgs e) => TransformSelection(s => s.ToLowerInvariant());
    private void CommentClick(object sender, RoutedEventArgs e)
    {
        QueryClick(this, e);
        var first = Editor.Document.GetLineByOffset(Editor.SelectionStart);
        int endOffset = Editor.SelectionLength > 0 ? Editor.SelectionStart + Editor.SelectionLength - 1 : first.EndOffset;
        var last = Editor.Document.GetLineByOffset(endOffset);
        var lines = Enumerable.Range(first.LineNumber, last.LineNumber - first.LineNumber + 1).Select(Editor.Document.GetLineByNumber).ToArray();
        bool uncomment = lines.All(line => Editor.Document.GetText(line).TrimStart().StartsWith("--", StringComparison.Ordinal));
        using (Editor.Document.RunUpdate())
            foreach (var line in lines.Reverse()) {
                string value = Editor.Document.GetText(line); int indent = value.Length - value.TrimStart().Length;
                if (uncomment) Editor.Document.Remove(line.Offset + indent, 2); else Editor.Document.Insert(line.Offset + indent, "--");
            }
    }
    private void DuplicateLineClick(object sender, RoutedEventArgs e)
    {
        QueryClick(this, e); var line = Editor.Document.GetLineByNumber(Editor.TextArea.Caret.Line);
        string text = Editor.Document.GetText(line.Offset, line.TotalLength);
        if (line.DelimiterLength == 0) text = Environment.NewLine + text;
        Editor.Document.Insert(line.Offset + line.TotalLength, text);
    }
    private void ReplaceClick(object sender, RoutedEventArgs e)
    {
        string? find = AskText("Find and replace", "Find text (literal):"); if (string.IsNullOrEmpty(find)) return;
        string? replacement = AskText("Find and replace", "Replace with:"); if (replacement == null) return;
        string original = Editor.SelectionLength > 0 ? Editor.SelectedText : Editor.Text;
        string result = original.Replace(find, replacement, StringComparison.Ordinal);
        if (Editor.SelectionLength > 0) TransformSelection(_ => result); else { Editor.Document.Replace(0, Editor.Text.Length, result); QueryClick(this, e); }
    }
    private void LibraryClick(object sender, RoutedEventArgs e)
    {
        var builtIn = new List<LibraryItem>();
        try {
            using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "ase-recipes.json")));
            if(source.RootElement.ValueKind!=JsonValueKind.Array)throw new JsonException("Invalid recipe catalog.");
            string Field(JsonElement entry,string name) => entry.ValueKind==JsonValueKind.Object && entry.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.String ? value.GetString()! : throw new JsonException("Invalid recipe field: "+name);
            foreach (var entry in source.RootElement.EnumerateArray()) builtIn.Add(new(Field(entry,"name"),Field(entry,"sql"),Field(entry,"category"),Field(entry,"description"),Platforms.TryParseTag(Field(entry,"engine"),out var platform)?platform.Tag():throw new JsonException("Unknown recipe engine: "+Field(entry,"engine"))));
            favorites.Clear(); favorites.AddRange(ReadUserRecipes(LibraryPath));
        } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { ShowError(ex.Message); return; }
        // User recipes are identified by reference so a built-in recipe with the same content is never deletable.
        bool IsUserRecipe(LibraryItem item) => favorites.Any(r => ReferenceEquals(r, item));
        var window = new Window { Owner = this, Title = "Library · SAP ASE, IQ and Replication Server recipes", Width = 950, Height = 640, MinWidth=760, MinHeight=480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var layout = new DockPanel { Margin = new Thickness(16) }; var filter = new TextBox { Margin = new Thickness(0,0,0,12), ToolTip="Search by name, category or SQL" };
        var heading=new TextBlock {Text="Query library",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,16)};heading.SetResourceReference(TextBlock.FontSizeProperty,"SectionFontSize");DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var label=new TextBlock {Text="Search by name, category or SQL",Margin=new Thickness(0,0,0,6)}; DockPanel.SetDock(label,Dock.Top); layout.Children.Add(label);
        bool connected = engine is AseIsql.Odbc.OdbcSession { IsOpen: true };
        var platformFilter = new ComboBox { Width = 230, Margin = new Thickness(10,0,0,12), ToolTip = "Recipes are written for one platform; My recipes appear on every platform." };
        platformFilter.ItemsSource = new[] { "All platforms" }.Concat(Platforms.All.Select(p => p.Title())).ToList();
        platformFilter.SelectedIndex = connected ? Platforms.All.ToList().IndexOf(CurrentPlatform) + 1 : 0;
        var searchRow = new DockPanel(); DockPanel.SetDock(platformFilter, Dock.Right); searchRow.Children.Add(platformFilter); searchRow.Children.Add(filter);
        DockPanel.SetDock(searchRow, Dock.Top); layout.Children.Add(searchRow);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var list = new ListBox(); grid.Children.Add(list);
        var detail=new DockPanel {Margin=new Thickness(12,0,0,0)};
        var summary=new StackPanel {Margin=new Thickness(0,0,0,12)};
        var recipeName=new TextBlock {FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};recipeName.SetResourceReference(TextBlock.FontSizeProperty,"SectionFontSize");summary.Children.Add(recipeName);
        var description=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};description.SetResourceReference(TextBlock.ForegroundProperty,"Muted");summary.Children.Add(description);DockPanel.SetDock(summary,Dock.Top);detail.Children.Add(summary);
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        preview.SetResourceReference(Control.FontFamilyProperty,"MonoFont");preview.SetResourceReference(Control.FontSizeProperty,"EditorFontSize");
        detail.Children.Add(preview);Grid.SetColumn(detail, 1); grid.Children.Add(detail);
        var insert = new Button { Content = "Insert on a new line", Margin = new Thickness(0,12,0,0), IsDefault = true, FontWeight = FontWeights.SemiBold, ToolTip = "Adds the recipe on its own line below the caret and selects it. Existing SQL is kept. Double-click a recipe does the same." };
        insert.SetResourceReference(BackgroundProperty, "Accent"); insert.SetResourceReference(ForegroundProperty, "OnAccent"); insert.SetResourceReference(BorderBrushProperty, "Accent");
        var replaceQuery = new Button { Content = "Replace query", Margin = new Thickness(0,12,8,0), ToolTip = "Replaces the whole query with the recipe. Ctrl+Z restores the previous text." };
        var delete = new Button { Content = "Delete", Margin = new Thickness(0,12,8,0), IsEnabled = false, ToolTip = "Delete the selected recipe from My recipes. Built-in recipes cannot be deleted." };
        var export = new Button { Content = "Export…", Margin = new Thickness(0,12,8,0), ToolTip = "Save My recipes to a JSON file" };
        var import = new Button { Content = "Import…", Margin = new Thickness(0,12,8,0), ToolTip = "Add recipes from a JSON file to My recipes" };
        void ShowList()
        {
            var selected = list.SelectedItem;
            ServerPlatform? shown = platformFilter.SelectedIndex > 0 ? Platforms.All[platformFilter.SelectedIndex - 1] : null;
            list.ItemsSource = favorites.Concat(builtIn).Where(item => (shown == null || item.Fits(shown.Value)) && (item.Name + " " + item.Category + " " + item.Sql).Contains(filter.Text, StringComparison.OrdinalIgnoreCase)).ToList();
            list.SelectedItem = selected; if (list.SelectedIndex < 0) list.SelectedIndex = 0;
            export.IsEnabled = favorites.Count > 0;
        }
        list.SelectionChanged += (_, _) => {
            if (list.SelectedItem is LibraryItem item){
                recipeName.Text=item.Name;preview.Text=item.Sql;
                string target=item.Engine!=null&&Platforms.TryParseTag(item.Engine,out var recipePlatform)?recipePlatform.Title():"Any platform";
                // A recipe for another platform is still shown when asked for, with the mismatch spelled out.
                string mismatch=connected&&!item.Fits(CurrentPlatform)?"For "+target+"; the current session is "+CurrentPlatform.Title()+". ":"";
                description.Text=mismatch+target+" · "+item.Category+(item.Description.Length>0?" · "+item.Description:"");
                description.SetResourceReference(TextBlock.ForegroundProperty,mismatch.Length>0?"ErrorInk":"Muted");
            }else{recipeName.Text="No recipe selected";description.Text="Try a different search.";preview.Text="";}
            delete.IsEnabled = list.SelectedItem is LibraryItem current && IsUserRecipe(current);
        };
        filter.TextChanged += (_, _) => ShowList(); platformFilter.SelectionChanged += (_, _) => ShowList();
        void UseRecipe(bool replace)
        {
            if (list.SelectedItem is not LibraryItem item) return;
            // Recipes with :name parameters ask for their values; cancelling keeps the library open.
            string? sql = FillParameters(item.Sql, item.Name, replace ? "Replace" : "Insert"); if (sql == null) return;
            QueryClick(this, e);
            if (replace) ReplaceQueryWithRecipe(sql); else InsertRecipeOnNewLine(sql);
            StatusText.Text = (replace ? "Query replaced by " : "Recipe inserted · ") + item.Name + " · Ctrl+Z undoes it";
            window.Close();
        }
        insert.Click += (_, _) => UseRecipe(false);
        replaceQuery.Click += (_, _) => UseRecipe(true);
        list.MouseDoubleClick += (_, args) => { if (args.OriginalSource is DependencyObject source && FindAncestor<ListBoxItem>(source) != null) UseRecipe(false); };
        delete.Click += (_, _) => {
            if (list.SelectedItem is not LibraryItem item || !IsUserRecipe(item)) return;
            if (!Confirm(window, "Delete recipe", "Delete \"" + item.Name + "\" from My recipes? A backup of the previous library file is kept.", "Delete")) return;
            try { SaveUserRecipes(favorites.Where(r => !ReferenceEquals(r, item)).ToList()); list.SelectedItem = null; ShowList(); StatusText.Text = "Recipe deleted · " + item.Name; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex.Message); }
        };
        export.Click += (_, _) => {
            if (favorites.Count == 0) return;
            var picker = new SaveFileDialog { Title = "Export My recipes", Filter = "JSON (*.json)|*.json", DefaultExt = ".json", FileName = "dbmon-recipes.json", OverwritePrompt = true };
            if (picker.ShowDialog(window) != true) return;
            try {
                var payload = favorites.Select(r => new { name = r.Name, category = r.Category, description = r.Description, sql = r.Sql });
                AtomicFiles.SaveUtf8(picker.FileName, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                StatusText.Text = favorites.Count + (favorites.Count == 1 ? " recipe" : " recipes") + " exported · " + Path.GetFileName(picker.FileName);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex.Message); }
        };
        import.Click += (_, _) => {
            var picker = new OpenFileDialog { Title = "Import recipes", Filter = "JSON (*.json)|*.json|All files (*.*)|*.*" };
            if (picker.ShowDialog(window) != true) return;
            try {
                if (new FileInfo(picker.FileName).Length > 4 * 1024 * 1024) throw new IOException("The file exceeds 4 MiB.");
                var (next, added, skipped) = MergeRecipes(ReadUserRecipes(LibraryPath), ParseRecipeImport(File.ReadAllText(picker.FileName)));
                if (added > 0) SaveUserRecipes(next); else { favorites.Clear(); favorites.AddRange(next); }
                ShowList();
                StatusText.Text = added + (added == 1 ? " recipe" : " recipes") + " imported" + (skipped > 0 ? " · " + skipped + " already present" : "");
            } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { ShowError("Import failed: " + ex.Message); }
        };
        var footer=new DockPanel();
        foreach (var button in new[] { import, export, delete }) { DockPanel.SetDock(button, Dock.Left); footer.Children.Add(button); }
        DockPanel.SetDock(insert,Dock.Right);footer.Children.Add(insert);DockPanel.SetDock(replaceQuery,Dock.Right);footer.Children.Add(replaceQuery);
        var hint=new TextBlock {Text="Recipes are inserted for review before you run them; :name parameters ask for their values first.",TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,12,12,0),ToolTip="My recipes are stored in "+LibraryPath};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");footer.Children.Add(hint);
        ShowList();
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); layout.Children.Add(grid); window.Content = layout; window.Loaded += (_, _) => { filter.Focus(); list.SelectedIndex=0; }; window.PreviewKeyDown+=(_,key)=> {if(key.Key==Key.Escape)window.Close();}; window.ShowDialog();
    }
    // Recipes never overwrite SQL: they go on a line of their own below the caret's line (or fill that line when it is
    // blank), and come back selected so Ctrl+E runs just them. One document change, so one Ctrl+Z removes them.
    private void InsertRecipeOnNewLine(string sql)
    {
        var document = Editor.Document;
        string newline = document.Text.Contains("\r\n", StringComparison.Ordinal) || document.TextLength == 0 ? Environment.NewLine : "\n";
        var line = document.GetLineByOffset(Editor.TextArea.Caret.Offset);
        int start;
        if (string.IsNullOrWhiteSpace(document.GetText(line))) { document.Replace(line.Offset, line.Length, sql); start = line.Offset; }
        else { document.Insert(line.EndOffset, newline + sql); start = line.EndOffset + newline.Length; }
        Editor.Select(start, sql.Length); Editor.TextArea.Caret.Offset = start + sql.Length; Editor.ScrollTo(document.GetLineByOffset(start).LineNumber, 1);
    }
    private void ReplaceQueryWithRecipe(string sql)
    {
        Editor.Document.Replace(0, Editor.Document.TextLength, sql);
        Editor.Select(0, 0); Editor.TextArea.Caret.Offset = 0; Editor.ScrollToHome();
    }
    private void SaveRecipeClick(object sender, RoutedEventArgs e)
    {
        string sql = Editor.SelectionLength > 0 ? Editor.SelectedText : Editor.Text; if (string.IsNullOrWhiteSpace(sql)) return;
        string? name = AskText("Save as recipe", "Recipe name:"); if (string.IsNullOrWhiteSpace(name)) return;
        try {
            var current = ReadUserRecipes(LibraryPath);
            if(current.Count>=MaxUserRecipes)throw new IOException("The library supports up to 1,000 recipes.");
            SaveUserRecipes(current.Append(new LibraryItem(name.Trim(), sql, UserRecipeCategory)).ToList()); StatusText.Text = "Recipe saved to My recipes · " + name.Trim();
        } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { ShowError(ex.Message); }
    }
    private void PaletteClick(object sender, RoutedEventArgs e)
    {
        var actions = new (string Name, Action Run)[] {
            ("Execute selection · Ctrl+E", () => _ = ExecuteAsync(selectionOnly: true)), ("Execute script · F9", () => _ = ExecuteAsync()), ("Repeat every… / stop repeating · Ctrl+Shift+F9", () => RepeatClick(this, new RoutedEventArgs())), ("Run on multiple servers · Shift+F9", () => MultiRunClick(this,e)),
            ("Editor · Ctrl+Q", () => QueryClick(this,e)), ("Recipes", () => LibraryClick(this,e)), ("Save as recipe", () => SaveRecipeClick(this,e)),
            ("Find and replace · Ctrl+H", () => ReplaceClick(this,e)), ("Comment / uncomment", () => CommentClick(this,e)), ("Duplicate line", () => DuplicateLineClick(this,e)),
            ("Selection to UPPERCASE", () => UpperClick(this,e)), ("Selection to lowercase", () => LowerClick(this,e)),
            ("Word wrap", () => WrapClick(this,e)), ("Increase font size", () => Zoom(1)), ("Decrease font size", () => Zoom(-1)),
            ("Object explorer · F4", () => BrowserClick(this,e)), ("History · Ctrl+R", () => HistoryClick(this,e)),
            ("Dark theme", () => SetPalette("dark")), ("Light theme", () => SetPalette("light"))
        };
        var window = new Window { Owner = this, Title = "Command palette · Ctrl+K", Width = 560, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(16) }; var filter = new TextBox { Margin = new Thickness(0,0,0,10) }; DockPanel.SetDock(filter,Dock.Top); panel.Children.Add(filter);
        var list = new ListBox { DisplayMemberPath = "Key", ItemsSource = actions.Select((a,i) => new KeyValuePair<string,int>(a.Name,i)).ToList() };
        filter.TextChanged += (_,_) => { list.ItemsSource = actions.Select((a,i) => new KeyValuePair<string,int>(a.Name,i)).Where(a => a.Key.Contains(filter.Text,StringComparison.OrdinalIgnoreCase)).ToList(); list.SelectedIndex=0; };
        Action? selected = null;
        void Choose() { if (list.SelectedItem is KeyValuePair<string,int> item) { selected=actions[item.Value].Run; window.Close(); } }
        list.MouseDoubleClick += (_,_) => Choose();
        window.PreviewKeyDown += (_,key) => { if(key.Key==Key.Enter) { Choose(); key.Handled=true; } else if(key.Key==Key.Down) { list.SelectedIndex=Math.Min(list.Items.Count-1,list.SelectedIndex+1); key.Handled=true; } else if(key.Key==Key.Up) { list.SelectedIndex=Math.Max(0,list.SelectedIndex-1); key.Handled=true; } else if(key.Key==Key.Escape) window.Close(); };
        panel.Children.Add(list); window.Content=panel; window.Loaded += (_,_) => { filter.Focus(); list.SelectedIndex=0; }; window.ShowDialog(); selected?.Invoke();
    }
}
