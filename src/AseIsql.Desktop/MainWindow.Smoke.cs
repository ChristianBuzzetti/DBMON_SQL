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

public partial class MainWindow
{
    private const string DemoSql = "-- Synthetic fixture. No ASE connection.\n-- F9 executes the script or selected statements.\n\nSELECT 42 AS numero, 'Hello, ASE' AS saludo, NULL AS vacio;\nGO\nSELECT 2026 AS anio, 10.13 AS version, 'Independent query' AS descripcion;";
    private readonly bool isSmoke = Environment.GetCommandLineArgs().Contains("--smoke");

    private async Task VerifySmoke()
    {
        int exitCode = 0;
        var timer = Stopwatch.StartNew();
        var checks = new List<string>();
        void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        try
        {
            await ExecuteAsync();
            Require(Workspace.Items.Count == 3, "Sample produces two result tabs");
            Require(Workspace.SelectedIndex == 1, "First result is selected");
            Require(Editor.Text == savedText, "Execution preserves SQL buffer");
            Require(((TabItem)Workspace.Items[1]).Tag is DataTable table && table.Rows[0][2] is DBNull, "NULL preserved as DBNull");
            Require(log.Any(m => m.Text.Contains("Done")), "Execution messages separate from tables");
            var htmlGrid = ((DockPanel)((TabItem)Workspace.Items[1]).Content).Children.OfType<DataGrid>().Single();
            htmlGrid.SelectAllCells();
            Require(!htmlGrid.ContextMenu.Items.OfType<MenuItem>().Any(item => item.Header.Equals("Copy as HTML")) && htmlGrid.ContextMenu.Items.OfType<MenuItem>().Count(item => item.Header.Equals("Copy as formatted table")) == 1, "Results offer one formatted-table action without a duplicate Copy as HTML");
            htmlGrid.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Header.Equals("Copy as formatted table")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Require(Clipboard.ContainsData(DataFormats.Html) && !Clipboard.GetText().Contains("<table"), "Formatted-table copy supplies rich HTML for Outlook, with readable text instead of markup as fallback");
            htmlGrid.UnselectAllCells();
            await VerifyWorkspacePolish(Require);
            int tabsBefore = Workspace.Items.Count, historyBefore = history.Count;
            Editor.Select(0, 0); await ExecuteAsync(selectionOnly: true);
            Require(Workspace.Items.Count == tabsBefore && history.Count == historyBefore && log[^1].Text.StartsWith("Ctrl+E"), "Empty Ctrl+E selection does not execute the script");
            Editor.Text = "select 101 as keyboard_selection;\nselect 202 as second_line;";
            Editor.TextArea.Caret.Offset = 0;
            QueryClick(this, new RoutedEventArgs());
            await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            EditingCommands.SelectDownByLine.Execute(null, Editor.TextArea);
            Require(Editor.SelectedText.Contains("101") && !Editor.SelectedText.Contains("202"), "Shift Down command selects the current SQL line");
            Editor.Select(0, 0); Editor.TextArea.Caret.Offset = Editor.Document.GetLineByNumber(2).Offset;
            EditingCommands.SelectUpByLine.Execute(null, Editor.TextArea);
            Require(Editor.SelectedText == "select 101 as keyboard_selection;\n", "Shift Up selects to the same column on the preceding line");
            Editor.Select(0, 0); Editor.TextArea.Caret.Offset = Editor.Document.GetLineByNumber(2).Offset + 7;
            EditingCommands.SelectUpByLine.Execute(null, Editor.TextArea);
            Require(Editor.SelectedText.StartsWith("101") && Editor.SelectedText.EndsWith("select "), "Shift Up preserves the habitual column-based text selection");
            Editor.Select(0, 0); Editor.TextArea.Caret.Offset = 7;
            Require(ExtendSelectionToDocumentEdge(true) && Editor.SelectionStart == 0 && Editor.SelectionLength == 7 && Editor.TextArea.Caret.Offset == 0, "Shift Up on the first line extends the selection to the start of the document");
            Require(ExtendSelectionToDocumentEdge(true) && Editor.SelectionLength == 7, "Repeated Shift Up at the document start keeps the selection");
            int lastLineStart = Editor.Document.GetLineByNumber(2).Offset; Editor.Select(0, 0); Editor.TextArea.Caret.Offset = lastLineStart + 3;
            Require(!ExtendSelectionToDocumentEdge(true) && ExtendSelectionToDocumentEdge(false) && Editor.SelectedText == Editor.Text[(lastLineStart + 3)..], "Shift Down on the last line extends to the end while inner lines keep native movement");
            Editor.Select(0, 0); Editor.TextArea.Caret.Offset = lastLineStart + 3;
            Require(MoveCaretToEndOfLastLine() && Editor.SelectionLength == 0 && Editor.TextArea.Caret.Offset == Editor.Document.TextLength, "Down on the last line moves the caret to the end of that line");
            Require(MoveCaretToEndOfLastLine() && Editor.TextArea.Caret.Offset == Editor.Document.TextLength, "Down at the end of the last line stays there");
            Editor.Select(0, 0); Editor.TextArea.Caret.Offset = 3;
            Require(!MoveCaretToEndOfLastLine() && Editor.TextArea.Caret.Offset == 3, "Down on an earlier line keeps native movement");
            Require(MoveCaretToStartOfFirstLine() && Editor.SelectionLength == 0 && Editor.TextArea.Caret.Offset == 0, "Up on the first line moves the caret to the start of that line");
            Require(MoveCaretToStartOfFirstLine() && Editor.TextArea.Caret.Offset == 0, "Up at the start of the first line stays there");
            Editor.Select(0, 0); Editor.TextArea.Caret.Offset = lastLineStart + 3;
            Require(!MoveCaretToStartOfFirstLine() && Editor.TextArea.Caret.Offset == lastLineStart + 3, "Up on a later line keeps native movement");
            string twoLineSql = Editor.Text;
            Editor.Text = "select top 100 * from sample";
            Editor.Select(0, 0); Editor.TextArea.Caret.Offset = Editor.Document.TextLength;
            Require(MoveCaretToStartOfFirstLine() && Editor.SelectionLength == 0 && Editor.TextArea.Caret.Offset == 0, "Up on a single-line query moves the caret to the start of the line");
            Editor.Text = twoLineSql;
            Editor.Select(0, 0); Editor.TextArea.Caret.Offset = 0; EditingCommands.SelectDownByLine.Execute(null, Editor.TextArea);
            int selectionStart = Editor.SelectionStart, selectionLength = Editor.SelectionLength;
            string selected = Editor.SelectedText;
            SetPalette("dark");
            await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            Require(((SolidColorBrush)Application.Current.Resources["Surface"]).Color == (Color)ColorConverter.ConvertFromString(Themes.All[1].Colors["Surface"]) && Editor.SelectionStart == selectionStart && Editor.SelectionLength == selectionLength && Editor.SelectedText == selected, "Dark theme applies without changing keyboard selection");
            var personalization = new PersonalizationWindow(appearance,ApplyAppearance,testProfile:BobertoProfile) {Owner=this};
            personalization.Show(); personalization.UpdateLayout(); Require(personalization.SectionSwitch.Content.Equals("Boberto Studio")&&!VisualChildren<Slider>(personalization).Any(slider=>slider.Maximum==120),"Workspace sections lead to Boberto Studio and the companion size slider is gone");
            Require(new[]{"papel","sepia","glaciar","contraste"}.All(id=>personalization.PaletteButtons.ContainsKey(id)&&!Themes.All.Single(p=>p.Id==id).DarkChrome),"Personalize shows the Papel, Sepia, Glaciar and Alto contraste tiles among the light palettes");
            personalization.PaletteButtons["glaciar"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Require(appearance.ThemeId=="glaciar"&&appearance.AccentHex==null&&((SolidColorBrush)Application.Current.Resources["Surface"]).Color==(Color)ColorConverter.ConvertFromString(Themes.All.Single(p=>p.Id=="glaciar").Colors["Surface"]),"Clicking a new light palette tile applies it live and stores its Id");
            personalization.PaletteButtons["dark"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Require(personalization.PaletteButtons.Count==23 &&personalization.Sections.Items.OfType<TabItem>().Count(t=>t.Visibility==Visibility.Visible)==4&&((TabItem)personalization.Sections.Items[4]).Visibility==Visibility.Collapsed&&personalization.Sections.Style==Application.Current.Resources["SegmentedTabs"]&&personalization.Sections.Items.OfType<TabItem>().Single(t=>t.Header is StackPanel h&&h.Children.OfType<TextBlock>().Any(x=>x.Text=="Effects")).Content is ScrollViewer {Content:StackPanel}&&personalization.Toggles.TryGetValue("Effects/Show Boberto beside the editor",out var companionToggle)&&companionToggle.IsChecked==appearance.BobertoVisible, "DBMON personalization exposes 23 palette tiles in four segmented sections; Studio opens from the header and the companion toggle lives in Effects");
            // The logo customizer: the cards write one choice into the settings, the live marks and the window icon.
            personalization.Sections.SelectedIndex=3;personalization.UpdateLayout();
            Require(MarkStyle.Shapes.All(shape=>personalization.LogoButtons.ContainsKey("shape/"+shape))&&MarkStyle.EyeStyles.All(eyes=>personalization.LogoButtons.ContainsKey("eyes/"+eyes))&&MarkStyle.HeadColors.All(head=>personalization.LogoButtons.ContainsKey("head/"+head))&&MarkStyle.TileColors.All(tile=>personalization.LogoButtons.ContainsKey("tile/"+tile)),"Personalize → Effects → Logo offers every shape, eye, head and background of the mark"+$" ({personalization.LogoButtons.Count} cards)");
            Require(MarkStyle.Shapes is ["hex","circle","squircle","diamond","ghost"]&&MarkStyle.EyeStyles is ["oval","round","happy","line","pixel"]&&MarkStyle.HeadColors is ["#0E1015","#F4F4F5","#2684FF","#10B981","#F59E0B","#8B5CF6","#F43F5E"]&&MarkStyle.TileColors is ["accent","#F43F5E","#0E1015","#F4F4F5","#2684FF","#10B981","#F59E0B","#8B5CF6","none"]&&new MarkStyle().Tile=="accent","The logo catalogue is BuzzPrint's, in the same order and colours, and the background follows the palette accent by default");
            Require(AppearanceStore.MigrateMarkTile(new AppearanceSettings() with {MarkTile="#CB6A3E"}).MarkTile=="accent"&&AppearanceStore.MigrateMarkTile(new AppearanceSettings() with {MarkTile="#0E1015"}).MarkTile=="#0E1015","A logo stored with 1.0.10's fixed copper background takes the palette accent, and a chosen colour is kept");
            var markBefore=MarkStyle.From(appearance);
            personalization.LogoButtons["shape/ghost"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            personalization.LogoButtons["eyes/happy"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            personalization.LogoButtons["tile/accent"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            personalization.UpdateLayout();UpdateLayout();
            Require(markBefore.Head==appearance.MarkHead&&appearance.MarkShape=="ghost"&&appearance.MarkEyes=="happy"&&appearance.MarkTile=="accent"&&appearance.MarkHead=="#0E1015"&&MarkStyle.From(appearance)==BobertoMark.Current&&BrandMark.ActualWidth==26,"Picking a logo option stores that one choice, leaves the others alone and restyles the topbar mark"+$" (before {markBefore}, now {MarkStyle.From(appearance)}, current {BobertoMark.Current}, brand {BrandMark.ActualWidth:F0})");
            CaptureSmokeVisual(personalization,"personalize-logo.png");
            Require(Application.Current.Resources["AppIcon"] is System.Windows.Media.Imaging.BitmapSource {PixelWidth:64,PixelHeight:64}&&Icon is System.Windows.Media.Imaging.BitmapSource {PixelWidth:64},"The windows of the running app take their icon from the chosen logo, at 64 px");
            personalization.LogoButtons["tile/none"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Require(appearance.MarkTile=="none"&&BobertoMark.Current.Tile=="none","A mark with no tile is a valid logo: the head fills the square");
            VisualChildren<Button>(personalization).First(button=>Equals(button.Content,"Reset logo")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            personalization.UpdateLayout();
            Require(MarkStyle.From(appearance)==new MarkStyle()&&BobertoMark.Current==new MarkStyle(),"Reset logo returns the shipped mark: round head, round eyes, copper tile");
            personalization.Sections.SelectedIndex=1;personalization.UpdateLayout();
            var interfaceSlider=VisualChildren<Slider>(personalization).Single(slider=>slider.Minimum==11&&slider.Maximum==17);
            string typographySql=Editor.Text;object? typographyUndo=Editor.Document.UndoStack.LastGroupDescriptor;
            interfaceSlider.Value=17;personalization.UpdateLayout();UpdateLayout();
            Require(appearance.UiSize==17 && FontSize==17 && ToolsMenu.FontSize==17 && QueryTab.FontSize==17 && ExecuteButton.FontSize==17 && Messages.FontSize==17 && personalization.FontSize==17 && personalization.Sections.FontSize==17,"Text slider updates actual window, menu, tab, button, messages and personalization typography");
            Require(ToolsMenu.FontFamily.Equals(AppTypography.Inter)&&ExecuteButton.FontFamily.Equals(AppTypography.Inter)&&personalization.Sections.FontFamily.Equals(AppTypography.Inter),"Task menus and dialog tabs use bundled Inter rather than WPF legacy defaults");
            Require(Editor.Text==typographySql&&ReferenceEquals(Editor.Document.UndoStack.LastGroupDescriptor,typographyUndo)&&(Editor.SelectionStart,Editor.SelectionLength)==(selectionStart,selectionLength),"Interface text size preserves SQL, selection and undo history");
            CaptureSmokeVisual(personalization,"personalize-text-large.png");await personalization.PreviewDock!.Ready.WaitAsync(TimeSpan.FromSeconds(20));var previewFacts=await DockUntil(personalization.PreviewDock,f=>f.GetProperty("adopted").GetBoolean());Require(previewFacts.GetProperty("adopted").GetBoolean()&&previewFacts.GetProperty("canvasTop").GetDouble()>=0,"Personalize previews the configured Boberto live, whole and animated, instead of a static picture");Require(VisualChildren<TextBlock>(personalization).First(text=>text.Text=="Interface font").Foreground is SolidColorBrush {Color:var labelInk}&&labelInk==((SolidColorBrush)Application.Current.Resources["Ink"]).Color,"Selected tab accent colors its header only, not the content of the tab");
            interfaceSlider.Value=11;personalization.UpdateLayout();Require(ToolsMenu.FontSize==11&&Messages.FontSize==11,"Interface size can shrink again without stale control values");
            interfaceSlider.Value=13;
            personalization.Close();
            var dockTimer=Stopwatch.StartNew();
            companionAllowed=true;ApplyAppearance(appearance with {BobertoSize=120,BobertoVisible=true});UpdateLayout();
            var dock=bobertoDock??throw new InvalidOperationException("Boberto dock was not created");
            bool placeholderAtStart=dock.PlaceholderVisible;
            await dock.Ready.WaitAsync(TimeSpan.FromSeconds(20));double dockReadyMs=dockTimer.Elapsed.TotalMilliseconds;
            // The dock reloads itself when Studio changes the look; during that reload facts() does not exist yet.
            async Task<System.Text.Json.JsonElement> DockFacts(BobertoDockControl target){for(int attempt=0;;attempt++){string raw=await target.EvaluateAsync("window.desktopDock?window.desktopDock.facts():null");if(raw!="null"||attempt>=50){using var facts=System.Text.Json.JsonDocument.Parse(raw);return facts.RootElement.Clone();}await Task.Delay(100);}}
            async Task<System.Text.Json.JsonElement> DockUntil(BobertoDockControl target,Func<System.Text.Json.JsonElement,bool> condition){var facts=await DockFacts(target);for(int i=0;i<60&&!condition(facts);i++){await Task.Delay(100);facts=await DockFacts(target);}return facts;}
            var dockState=await DockUntil(dock,f=>f.GetProperty("scale").GetDouble()>0&&f.GetProperty("requested").GetDouble()==6);
            Require(dockState.GetProperty("ready").GetBoolean()&&dockState.GetProperty("adopted").GetBoolean()&&dockState.GetProperty("estado").GetString()=="visible"&&dockState.GetProperty("label").GetString()=="Customize Boberto","Original engine adopts the console dock with its live canvas, floor and English reading");
            Require(dockState.GetProperty("requested").GetDouble()==6&&dockState.GetProperty("scale").GetDouble() is >=3 and <=6&&dockState.GetProperty("slotHeight").GetDouble()<=dockState.GetProperty("viewportHeight").GetDouble()&&BobertoPanel.ActualWidth>=220&&BobertoHost.ActualHeight is >=90 and <=220,"Boberto lives in the header band: companion size is a ceiling and the scene fits the band height without clipping");var fitted=await DockUntil(dock,f=>f.GetProperty("canvasTop").GetDouble()>=4);Require(fitted.GetProperty("floorGap").GetDouble() is >=0 and <=30,"Boberto stands low in the header scene: the neon floor sits near the band foot, as on the Studio stage"+$" (floor gap {fitted.GetProperty("floorGap")})");Require(fitted.GetProperty("canvasTop").GetDouble()>=4,"Boberto fits whole in the header scene: the measured canvas top stays inside the band"+$" (top {fitted.GetProperty("canvasTop")}, scale {fitted.GetProperty("scale")})");
            Require(Math.Abs(BobertoHost.ActualWidth/BobertoHost.ActualHeight-16.0/9)<0.04,"Boberto's scene keeps the 16:9 proportion of its background art"+$" ({BobertoHost.ActualWidth:F0}x{BobertoHost.ActualHeight:F0})");
            var onlineEngine=engine;engine=new DisconnectedQueryEngine();RefreshConnection();int offlineTabs=Workspace.Items.Count;await ExecuteAsync();
            Require(!ExecuteButton.IsEnabled&&OfflineBanner.Visibility==Visibility.Visible&&Workspace.Items.Count==offlineTabs&&log.Count==1&&log[0].Text.StartsWith("Not connected"),"Offline workspace shows a disconnected notice, disables Run and never executes SQL");
            engine=onlineEngine;RefreshConnection();Require(ExecuteButton.IsEnabled&&OfflineBanner.Visibility==Visibility.Collapsed,"A session clears the offline notice and re-enables Run");
            Require(SessionFactsPanel.Visibility==Visibility.Collapsed,"Engines without server session facts show no database or SPID");
            try {
                engine=new FactsSmokeEngine();RefreshConnection();
                Require(SessionFactsPanel.Visibility==Visibility.Visible&&DatabaseText.Text=="sales db"&&SpidText.Text=="1969"&&SpidButton.Visibility==Visibility.Visible&&LoginText.Text=="fixture_login"&&CharsetText.Text=="utf8","Status bar shows the session's current database, SPID, login and charset reported by the server");
                string factsSql=Editor.Text;await ExecuteAsync();await Dispatcher.InvokeAsync(()=>UpdateLayout(),System.Windows.Threading.DispatcherPriority.ContextIdle);
                CaptureSmokeVisual(this,"status-session.png");
                var unnamedGrid=((DockPanel)((TabItem)Workspace.SelectedItem).Content).Children.OfType<DataGrid>().Single();
                string unnamedCopy=BuildResultCopy(unnamedGrid,ResultCopy.AllWithHeaders,',', delimited: true);
                Require(unnamedGrid.Columns[0].Header.Equals(UnnamedColumn)&&!unnamedCopy.Contains(UnnamedColumn)&&unnamedCopy.Contains("1969")&&Editor.Text==factsSql,"An unnamed column shows a placeholder header while copies keep the empty source caption");
                Require(log.Any(m=>m.Text.Contains("· 1 result · 1 row ·"))&&StatusText.Text.StartsWith("Ready · 1 result · 1 row · "),"Counts read as 1 result and 1 row, with the same elapsed time in Messages and status"+$" ({StatusText.Text})");
                Require(UseTarget("master")=="master"&&UseTarget("sales db")=="[sales db]"&&UseTarget("a]b; drop")=="[a]]b; drop]","Database switching sends regular names as typed and delimits anything else");
            } finally {engine=onlineEngine;RefreshConnection();ClearResults(true);}
            string sqlBeforeMulti=Editor.Text;var selectionBeforeMulti=(Editor.SelectionStart,Editor.SelectionLength);
            extraTargets.Add(new RunTarget("beta",new DemoQueryEngine()));extraTargets.Add(new RunTarget("broken",new DiagnosticSmokeEngine()));
            try {
                RefreshConnection();
                var runWindow=new MultiRunWindow(this,"select 5 as n, 'x' as s\nselect 6","script");runWindow.Show();runWindow.UpdateLayout();
                Require(runWindow.RowCount==3&&runWindow.RunCaption=="Run on 3 servers"&&ConnectionStatus.Text.EndsWith("+2 servers"),"Multi-server dialog lists the current and added sessions, all selected, and the status bar counts the extra sessions"+$" ({runWindow.RunCaption}, {ConnectionStatus.Text})");
                CaptureSmokeVisual(runWindow,"multi-run.png");runWindow.Close();
                excludedTargets.Add("broken");var narrowed=new MultiRunWindow(this,"select 5","selection");Require(narrowed.RunCaption=="Run on 2 servers","A server unchecked in the dialog stays unchecked for the next run");narrowed.Close();excludedTargets.Clear();
                string multiSql="select 5 as n, 'x' as s";Editor.Text=multiSql;
                await ExecuteOnTargetsAsync(AvailableTargets(),multiSql,"script");
                var multiTabs=Workspace.Items.OfType<TabItem>().Where(tab=>tab.Tag is DataTable).ToList();
                Require(multiTabs.Count==4&&multiTabs[0].Header.Equals("All · 1")&&multiTabs[1].Header.Equals("Diff · 1")&&multiTabs.Any(tab=>tab.Header.Equals("beta · 1"))&&((DataTable)multiTabs[0].Tag).Rows.Count==2&&((DataTable)multiTabs[0].Tag).Columns[0].Caption=="server"&&Workspace.SelectedItem==multiTabs[0],"Parallel run publishes a combined result with a server column first, then each server's own results"+$" ({string.Join(" | ",multiTabs.Select(tab=>tab.Header))})");
                Require(log.Any(m=>m.Source=="broken"&&m.Number==1)&&log.Any(m=>m.Source=="beta"&&m.Text.StartsWith("Done · 1 result · 1 row"))&&StatusText.Text.Contains("3 servers · 2 ok · 1 failed")&&Editor.Text==multiSql&&history[0].Sql==multiSql,"A failing server reports in Messages under its own label without hiding the others"+$" ({StatusText.Text})");
                CaptureSmokeVisual(this,"multi-run-results.png");
                var betaTab=multiTabs.Single(tab=>tab.Header.Equals("beta · 1"));
                var betaFooter=((DockPanel)betaTab.Content).Children.OfType<Grid>().Single();
                Require(betaFooter.Children.OfType<Border>().Single().Child is TextBlock {Text:"◆ beta"}&&betaTab.ToolTip.ToString()!.StartsWith("Server beta · Source: Test fixture")&&resultOrigins[(DataTable)betaTab.Tag].Source.StartsWith("Test fixture · no ASE connection · ")
                    &&resultOrigins[(DataTable)multiTabs[0].Tag].Source=="All servers · current, beta"&&resultOrigins[(DataTable)multiTabs[1].Tag].Source.EndsWith("· current, beta")&&((DockPanel)multiTabs[0].Content).Children.OfType<Grid>().Single().Children.OfType<Border>().Any()==false,
                    "Every multi-server result names its server: a badge and tooltip on each server's tab, the server list on All and Diff, frozen provenance with time");
                Workspace.SelectedItem=betaTab;await Dispatcher.InvokeAsync(()=>UpdateLayout(),System.Windows.Threading.DispatcherPriority.ContextIdle);CaptureSmokeVisual(this,"multi-run-server-tab.png");
                Require(RenameResult(betaTab,"  Saldos\n QA  ")&&betaTab.Header.Equals("Saldos QA")&&resultOrigins[(DataTable)betaTab.Tag].Source.StartsWith("Test fixture")&&!RenameResult(betaTab,"   "),"Renaming a result collapses whitespace, keeps its data and provenance, and ignores blank names");
                TogglePin(betaTab);Require(RenameResult(betaTab,new string('x',120))&&betaTab.Header.Equals("📌 "+new string('x',MaxResultName))&&ResultName(betaTab)==new string('x',MaxResultName),"A pinned result keeps its pin when renamed and names are bounded");TogglePin(betaTab);
                Require(FileStem("Saldos: QA/PROD?")=="Saldos_ QA_PROD_"&&FileStem(" . ")=="Result"&&betaTab.ContextMenu.Items.OfType<MenuItem>().First().Header.Equals("Rename…"),"Exports suggest the result's name as a safe file name, and the tab menu offers Rename");
                var diffTab=multiTabs[1];
                Require(((DataTable)diffTab.Tag).Rows.Count==0&&log.Any(m=>m.Text.StartsWith("Result 1 is identical on every server · 1 key by n"))&&((DockPanel)diffTab.Content).Children.OfType<DockPanel>().First().Children.OfType<Button>().Any(b=>b.Content.Equals("Key: n  ▾")),"Identical servers produce an empty Diff tab keyed by the first column, with a key chooser");
                Require(RekeyDiff(diffTab,[0,1])&&!Workspace.Items.Contains(diffTab)&&Workspace.Items.IndexOf(Workspace.SelectedItem)==2&&((TabItem)Workspace.SelectedItem).Header.Equals("Diff · 1")&&resultOrigins[(DataTable)((TabItem)Workspace.SelectedItem).Tag].Source.StartsWith("Differences by n, s"),"Changing the diff key replaces the Diff tab in place");
                var groupWindow=new MultiRunWindow(this,"select 1","script");
                try {
                    var alphaProfile=new ConnectionProfile("alpha-host",5000,"master","fixture",false,"",ServerAlias:"alpha");var gammaProfile=alphaProfile with {Server="gamma-host",ServerAlias="gamma"};
                    AdoptTarget(new DemoQueryEngine(),alphaProfile);
                    Require(groupWindow.ListedProfiles().SequenceEqual([alphaProfile]),"Only sessions opened with a login profile can be saved in a group");
                    Require(SaveServerGroups(ServerGroupStore.Upsert(LoadServerGroups(),new ServerGroup("qa",[alphaProfile,gammaProfile])))&&!File.ReadAllText(ServerGroupsPath).Contains("\"Password\"",StringComparison.OrdinalIgnoreCase),"Server groups are saved locally without passwords");
                    var asked=new List<string>();
                    int opened=groupWindow.OpenGroup(LoadServerGroups().Single(),(profile,caption)=>{asked.Add(caption);AdoptTarget(new DemoQueryEngine(),profile);return true;});
                    Require(opened==1&&asked.SequenceEqual(["group qa · 2/2"])&&AvailableTargets().Any(t=>t.Label=="gamma")&&AvailableTargets().Count(t=>t.Label.StartsWith("alpha"))==1,"Opening a group logs in only to servers not already listed, one prompt per server");
                    int stopped=groupWindow.OpenGroup(new ServerGroup("more",[gammaProfile with {Server="delta-host",ServerAlias="delta"},gammaProfile with {Server="omega-host",ServerAlias="omega"}]),(_,_)=>false);
                    Require(stopped==0&&!AvailableTargets().Any(t=>t.Label is "delta" or "omega"),"Cancelling a login stops opening the rest of the group");
                    groupWindow.Show();groupWindow.UpdateLayout();CaptureSmokeVisual(groupWindow,"multi-run-groups.png");
                } finally {groupWindow.Close();if(File.Exists(ServerGroupsPath))File.Delete(ServerGroupsPath);foreach(var target in extraTargets.Where(t=>t.Label is "alpha" or "gamma").ToList()){extraTargets.Remove(target);targetProfiles.Remove(target);}}
                engine=new DisconnectedQueryEngine();RefreshConnection();Require(AvailableTargets().Count==2&&RunMenuButton.IsEnabled,"Multi-server runs remain available offline with added sessions only");
            } finally {engine=onlineEngine;extraTargets.Clear();targetProfiles.Clear();excludedTargets.Clear();RefreshConnection();ClearResults(true);Editor.Text=sqlBeforeMulti;Editor.Select(selectionBeforeMulti.SelectionStart,selectionBeforeMulti.SelectionLength);}
            for(int wait=0;wait<20&&dock.PlaceholderVisible;wait++)await Task.Delay(100);
            Require(placeholderAtStart&&!dock.PlaceholderVisible,"A loading placeholder covers the band until the live, themed and fitted Boberto is revealed");
            for(int wait=0;wait<40&&!File.Exists(dock.SnapshotPath);wait++)await Task.Delay(100);
            var nextStart=new BobertoDockControl(appearance,BobertoProfile);try{Require(File.Exists(dock.SnapshotPath)&&nextStart.ShowsSnapshot&&nextStart.PlaceholderVisible,"The scene snapshot is cached so the next start shows the configured Boberto instantly while the engine loads");}finally{nextStart.Dispose();}
            checks.Add("PERF Boberto dock: ready "+dockReadyMs.ToString("F1")+" ms after creation with a prewarmed environment (isolated profile); the placeholder covers that time");
            var studioAsked=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);dock.StudioRequested+=()=>studioAsked.TrySetResult(true);
            personalizing=true;await dock.EvaluateAsync("document.getElementById('dock').click()");
            Require(await studioAsked.Task.WaitAsync(TimeSpan.FromSeconds(5)),"Clicking Boberto in the dock asks the native window for Studio instead of an in-rail modal");
            await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);personalizing=false;
            await dock.PauseAsync(true);await dock.PauseAsync(false);
            Require((await DockFacts(dock)).GetProperty("adopted").GetBoolean(),"Boberto dock survives renderer suspension and resumption");
            await dock.EvaluateAsync("window.BOBERTO.setFondo('jp-torii')");await DockUntil(dock,f=>f.GetProperty("fondo").GetString()=="jp-torii");
            BobertoHideClick(this,new RoutedEventArgs());await Task.Delay(200);
            Require(!appearance.BobertoVisible&&ReferenceEquals(bobertoDock,dock)&&dock.IsPaused&&BobertoPanel.Visibility==Visibility.Collapsed&&BobertoColumn.Width.Value==0&&!ShowBobertoMenu.IsChecked,"Hiding Boberto suspends the same WebView2 (no teardown race) and returns the band to the workspace");
            ShowBobertoMenu.IsChecked=true;BobertoToggleClick(ShowBobertoMenu,new RoutedEventArgs());UpdateLayout();
            var reshown=await DockUntil(dock,f=>f.GetProperty("adopted").GetBoolean()&&f.GetProperty("ambient").GetBoolean());
            Require(appearance.BobertoVisible&&!dock.IsPaused&&reshown.GetProperty("fondo").GetString()=="jp-torii"&&reshown.GetProperty("ambient").GetBoolean(),"Showing Boberto again resumes the dock with its background and ambient light intact");
            double normalWidth=Width,normalHeight=Height;
            Width=1000;Height=560;ApplyAppearance(appearance with {UiSize=17});UpdateLayout();
            Require(CommandToolbar.ActualWidth>0&&BobertoPanel.ActualWidth>=220&&Editor.ActualHeight>=80&&Messages.ActualHeight>=(double)Application.Current.Resources["GridHeaderHeight"]+(double)Application.Current.Resources["GridRowHeight"]+SystemParameters.HorizontalScrollBarHeight,"Small desktop layout retains usable SQL height, a complete message row, a wrapped toolbar and Boberto controls at maximum interface size"+$" (editor {Editor.ActualHeight:F0}, messages {Messages.ActualHeight:F0}, toolbar {CommandToolbar.ActualWidth:F0}, boberto {BobertoPanel.ActualWidth:F0}x{BobertoPanel.ActualHeight:F0}, header {HeaderLeft.ActualHeight:F0}, bar {HeaderBar.ActualHeight:F0}, menu {MainMenu.ActualHeight:F0}, toolbar h {CommandToolbar.ActualHeight:F0}, work {WorkArea.ActualHeight:F0}, window {ActualHeight:F0})");
            CaptureSmokeVisual(this,"workspace-small-large-text.png");
            double rowHeight=ExecuteButton.ActualHeight+ExecuteButton.Margin.Top+ExecuteButton.Margin.Bottom;
            Require(CommandToolbar.ActualHeight<=rowHeight+2&&BobertoPanel.ActualWidth<=Math.Round(ActualWidth*BobertoMaxShare)+1&&ObjectsText.Visibility==Visibility.Collapsed&&RunKeyHint.Visibility==Visibility.Collapsed,"A narrow window keeps one toolbar row with glyph buttons, and Boberto's band never exceeds 30% of the window"+$" (toolbar h {CommandToolbar.ActualHeight:F0}, row {rowHeight:F0}, band {BobertoPanel.ActualWidth:F0} of {ActualWidth:F0})");
            double previousWidth=Width;Width=860;UpdateLayout();AdaptHeader();UpdateLayout();
            Require(BobertoColumn.Width.Value==0&&BobertoPanel.Width==0&&appearance.BobertoVisible,"Below 900 DIP Boberto's band steps aside without changing the preference"+$" (window {ActualWidth:F0}, column {BobertoColumn.Width.Value:F0})");
            var narrowRepeat=new RepeatPlan("select spid, cmd, cpu, physical_io, blocked, hostname, program_name from master..sysprocesses where spid <> @@spid",TimeSpan.FromSeconds(30),null,true,engine,"query");
            repeat=narrowRepeat;RepeatBanner.Visibility=Visibility.Visible;UpdateLayout();UpdateRepeatBanner();UpdateLayout();
            var detailEnd=RepeatDetail.TranslatePoint(new Point(RepeatDetail.ActualWidth,0),RepeatBanner).X;var countdownStart=RepeatCountdown.TranslatePoint(new Point(0,0),RepeatBanner).X;
            Require(detailEnd<=countdownStart&&RepeatStopButton.ActualWidth>0&&RepeatStopText.Text=="Stop","The repeat band trims its detail before the countdown and buttons in a narrow window"+$" (detail ends {detailEnd:F0}, countdown {countdownStart:F0})");
            CaptureSmokeVisual(this,"workspace-narrow-repeat.png");
            repeat=null;RepeatBanner.Visibility=Visibility.Collapsed;Width=previousWidth;UpdateLayout();
            await Task.Delay(300);Require(bobertoDock is {Failure:null,IsLive:true},"Boberto survives window resizing and layout passes without falling back"+(bobertoDock?.Failure is {} why?": "+why:""));
            Width=normalWidth;Height=normalHeight;ApplyAppearance(appearance with {UiSize=13});UpdateLayout();UpdateLayout();Require(HeaderLeft.ActualHeight<=170&&Math.Abs(BobertoPanel.ActualHeight-HeaderLeft.ActualHeight)<=1,"Header returns to its compact height after a small window, with Boberto matching it"+$" (header {HeaderLeft.ActualHeight:F0}, band {BobertoPanel.ActualHeight:F0})");CaptureSmokeVisual(this,"workspace-dark.png");
            var studioTimer=Stopwatch.StartNew();
            string studioFixture=Path.Combine(AppContext.BaseDirectory,"BobertoSmoke","profile");
            var avatarReceived=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var realStudio=new PersonalizationWindow(appearance,ApplyAppearance,true,onFrame:image=>avatarReceived.TrySetResult(image.PixelWidth>0&&image.PixelWidth<=512),testProfile:studioFixture){Owner=this};
            try {
                realStudio.Show();realStudio.UpdateLayout();Require(realStudio.SectionSwitch.Content.Equals("Workspace personalization"),"In Boberto Studio the header action leads back to workspace personalization");
                string studioFacts=await realStudio.Studio!.EvaluateAsync("window.desktopStudio.facts()");
                using(var facts=System.Text.Json.JsonDocument.Parse(studioFacts))Require(facts.RootElement.GetProperty("ready").GetBoolean()&&facts.RootElement.GetProperty("gestures").GetInt32()>=25&&facts.RootElement.GetProperty("accessories").GetInt32()>=37&&facts.RootElement.GetProperty("skins").GetInt32()>=9&&facts.RootElement.GetProperty("backgrounds").GetInt32()>=44&&facts.RootElement.GetProperty("borders").GetInt32()>=19,"Original DBMON Boberto engine loads its real gesture, accessory, skin, background and border catalogs in WebView2");
                string changedHat=await realStudio.Studio.EvaluateAsync("window.desktopTestHat=window.BOBERTO.getHat()==='corona'?'auriculares':'corona';window.BOBERTO.setHat(window.desktopTestHat); window.desktopStudio.facts()");
                using(var facts=System.Text.Json.JsonDocument.Parse(changedHat))Require(facts.RootElement.GetProperty("hat").GetString()==System.Text.Json.JsonSerializer.Deserialize<string>(await realStudio.Studio.EvaluateAsync("window.desktopTestHat")),"Original Boberto API changes accessory and persists the caller-selected id");
                Require(await avatarReceived.Task.WaitAsync(TimeSpan.FromSeconds(10)),"Original Boberto rendering produces a bounded PNG for the console mascot");
                string stageOwned=await realStudio.Studio.EvaluateAsync("window.BOBERTO.isStaged() && document.querySelector('#stage canvas').width>0");Require(stageOwned=="true","Real Boberto canvas belongs to the studio stage rather than the static PNG");
                string localOnly=await realStudio.Studio.EvaluateAsync("performance.getEntriesByType('resource').every(r=>r.name.startsWith(location.origin+'/')||r.name.startsWith('data:'))");Require(localOnly=="true","Boberto Studio loads only packaged local resources without DBMON backend or remote services");
                string studioHat=System.Text.Json.JsonSerializer.Deserialize<string>(await realStudio.Studio.EvaluateAsync("window.desktopTestHat"))!;
                await realStudio.Studio.EvaluateAsync("window.BOBERTO.setFondo('cyberpunk');window.BOBERTO.setBorde(window.BOBERTO.listBordes()[1].id)");
                var liveDock=bobertoDock!;var synced=await DockUntil(liveDock,f=>f.GetProperty("hat").GetString()==studioHat&&f.GetProperty("fondo").GetString()=="cyberpunk");
                Require(synced.GetProperty("hat").GetString()==studioHat&&synced.GetProperty("fondo").GetString()=="cyberpunk"&&synced.GetProperty("fondoImage").GetString()!.Contains("assets/fondo-cyberpunk.jpg")&&synced.GetProperty("borde").GetString()!="ninguno"&&synced.GetProperty("ambient").GetBoolean(),"Studio accessory, background and border reach the live console dock through the shared engine profile, with matching ambient light in the rail");
                await liveDock.EvaluateAsync("window.desktopImage=null;(()=>{const i=new Image();i.onload=()=>window.desktopImage=i.naturalWidth;i.onerror=()=>window.desktopImage=0;i.src=document.querySelector('.plt-dock-fondo image').getAttribute('href');})()");
                string loaded="null";for(int i=0;i<50&&loaded=="null";i++){await Task.Delay(100);loaded=await liveDock.EvaluateAsync("window.desktopImage");}
                Require(int.TryParse(loaded,out int imageWidth)&&imageWidth>=240,"Packaged DBMON background artwork loads from the local virtual host");
                string fills=await liveDock.EvaluateAsync("(()=>{const f=document.querySelector('.plt-dock-fondo').getBoundingClientRect();return f.width>=innerWidth*0.95&&f.top<=1&&f.height>=innerHeight*0.95;})()");Require(fills=="true","The background art is shown whole: it covers the full 16:9 header scene, with floor and reading over its lower edge");
                await Task.Delay(400);File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory,"BobertoSmoke","dock-preview.png"),await liveDock.CaptureAsync());
                string dockLocal=await liveDock.EvaluateAsync("performance.getEntriesByType('resource').every(r=>r.name.startsWith(location.origin+'/')||r.name.startsWith('data:'))");Require(dockLocal=="true"&&liveDock.BlockedRequests==0,"Boberto dock loads only packaged local resources");
                // Changing the background many times in a row reloads the dock once per storage burst; it must keep painting.
                string lastFondo="";var stressTimer=Stopwatch.StartNew();
                for(int i=0;i<12;i++){lastFondo=System.Text.Json.JsonSerializer.Deserialize<string>(await realStudio.Studio.EvaluateAsync($"(()=>{{const f=window.BOBERTO.listFondos().map(x=>x.id||x).filter(x=>x!=='ninguno');const id=f[{i}%f.length];window.BOBERTO.setFondo(id);return id;}})()"))!;await Task.Delay(i%3==0?120:420);}
                var stressed=await DockUntil(liveDock,f=>f.GetProperty("adopted").GetBoolean()&&f.GetProperty("fondo").GetString()==lastFondo);
                await Task.Delay(1500);
                double darkShare=DarkShare(await liveDock.CaptureAsync());
                Require(liveDock is {Failure:null,IsLive:true}&&stressed.GetProperty("fondo").GetString()==lastFondo&&stressed.GetProperty("adopted").GetBoolean()&&darkShare<0.9&&liveDock.IsVisible,"Boberto dock keeps painting after 12 quick background changes in Studio"+$" (fondo {stressed.GetProperty("fondo")}, dark share {darkShare:P0}, recoveries {liveDock.Recoveries}, failure {liveDock.Failure}, trace {liveDock.Trace})");
                // A reload that cancels the previous one used to mark the dock failed and leave the band empty for good.
                checks.Add("PERF Boberto background stress: 12 Studio changes with dock reloads in "+stressTimer.Elapsed.TotalMilliseconds.ToString("F0")+" ms");
                for(int i=0;i<2;i++){try{await liveDock.EvaluateAsync("setTimeout(()=>location.reload(),0);setTimeout(()=>location.reload(),20);setTimeout(()=>location.reload(),45)");}catch(TimeoutException){}await Task.Delay(60);}
                var afterCancel=await DockUntil(liveDock,f=>f.GetProperty("adopted").GetBoolean());await Task.Delay(600);
                Require(liveDock is {Failure:null,IsLive:true,PlaceholderVisible:false,Heals:0}&&afterCancel.GetProperty("adopted").GetBoolean()&&DarkShare(await liveDock.CaptureAsync())<0.9,"Overlapping dock reloads (cancelled navigations) never leave the Boberto band empty"+$" (failure {liveDock.Failure}, trace {liveDock.Trace})");
                await liveDock.HealForTestAsync();
                var rebuilt=await DockUntil(liveDock,f=>f.GetProperty("adopted").GetBoolean()&&f.GetProperty("fondo").GetString()==lastFondo);
                for(int wait=0;wait<30&&liveDock.PlaceholderVisible;wait++)await Task.Delay(100);
                Require(liveDock is {Failure:null,IsLive:true,PlaceholderVisible:false}&&rebuilt.GetProperty("fondo").GetString()==lastFondo&&ReferenceEquals(bobertoDock,liveDock),"A stalled Boberto dock can replace its WebView in place and come back with the same look"+$" (trace {liveDock.Trace})");
                // Choice cooldown: a real click applies at once, a second click right after is ignored, and choices reopen
                // once the cooldown ends.
                string cooldown=await realStudio.Studio.EvaluateAsync("(()=>{const chips=[...document.querySelectorAll('#panels button.bes-chip--tarjeta[aria-pressed=\"false\"]')];const B=window.BOBERTO;chips[0].click();const first=B.getFondo(),dimmed=document.documentElement.hasAttribute('data-cooldown');chips[1].click();const blocked=B.getFondo()===first;window.desktopCoolChip=chips[2];return JSON.stringify({first,dimmed,blocked,ms:window.desktopStudio.cooldownMs});})()");
                using(var cool=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Deserialize<string>(cooldown)!)){
                    var coolFacts=cool.RootElement;
                    await Task.Delay(coolFacts.GetProperty("ms").GetInt32()+150);
                    string reopened=System.Text.Json.JsonSerializer.Deserialize<string>(await realStudio.Studio.EvaluateAsync("(()=>{const dimmed=document.documentElement.hasAttribute('data-cooldown');window.desktopCoolChip.click();return JSON.stringify({fondo:window.BOBERTO.getFondo(),dimmed});})()"))!;
                    using var after=System.Text.Json.JsonDocument.Parse(reopened);
                    string coolFondo=after.RootElement.GetProperty("fondo").GetString()!;
                    var coolDock=await DockUntil(liveDock,f=>f.GetProperty("fondo").GetString()==coolFondo&&f.GetProperty("adopted").GetBoolean());
                    Require(coolFacts.GetProperty("dimmed").GetBoolean()&&coolFacts.GetProperty("blocked").GetBoolean()&&!after.RootElement.GetProperty("dimmed").GetBoolean()&&coolFondo!=coolFacts.GetProperty("first").GetString()&&coolDock.GetProperty("fondo").GetString()==coolFondo&&liveDock is {Failure:null,IsLive:true},"Studio applies a background at once, ignores another choice during the cooldown and accepts it afterwards, and the dock follows"+$" ({cooldown} then {reopened})");
                    lastFondo=coolFondo;
                }
                await realStudio.Studio.EvaluateAsync("window.BOBERTO.setBorde('neon')");await Task.Delay(400);string borderMoves=await realStudio.Studio.EvaluateAsync("(()=>{const f=document.getElementById('frame');return document.documentElement.getAttribute('data-animations')==='on'&&f.getAttribute('data-modelo')==='neon'&&getComputedStyle(f,'::before').animationName!=='none';})()");string tabsFit=await realStudio.Studio.EvaluateAsync("[...document.querySelectorAll('#tabs .bes-tab-txt')].every(t=>t.scrollWidth<=t.clientWidth+1)");Require(tabsFit=="true","Every Boberto Studio section title is shown complete at the default Personalize size");Require(borderMoves=="true","Animated Boberto borders run in Studio (data-animations is set as SMG_APPS tweaks do)");ApplyAppearance(appearance with {UiSize=17});await realStudio.Studio.ApplyThemeAsync(appearance);
                Require(System.Text.Json.JsonSerializer.Deserialize<string>(await realStudio.Studio.EvaluateAsync("getComputedStyle(document.body).fontSize"))=="17px","Boberto Studio honors the shared interface size");
                ApplyAppearance(appearance with {UiSize=13});await realStudio.Studio.ApplyThemeAsync(appearance);
                File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory,"BobertoSmoke","studio-preview.png"),await realStudio.Studio.CaptureAsync());
                string gesture=await realStudio.Studio.EvaluateAsync("window.BOBERTO.playGesture('saluda') && window.BOBERTO.isBusy()");Require(gesture=="true","The original greeting starts a real Boberto animation in the owned studio canvas");
                checks.Add("PERF Boberto runtime: "+System.Text.Json.JsonSerializer.Serialize(realStudio.Studio.RuntimeMetrics())+"; initialization and catalog checks "+studioTimer.Elapsed.TotalMilliseconds.ToString("F1")+" ms; process peak sum is an estimate, not simultaneous family peak");
                for(int i=0;i<3;i++){
                    realStudio.Sections.SelectedIndex=0;await realStudio.Studio.PauseAsync(true);
                    realStudio.Sections.SelectedIndex=4;await realStudio.Studio.PauseAsync(false);
                }
                Require(await realStudio.Studio.EvaluateAsync("window.desktopStudio.facts().ready")=="true","Boberto survives repeated native tab suspension and resumption");
                Task pendingPause=realStudio.Studio.PauseAsync(true);realStudio.Close();await pendingPause;
                await realStudio.Studio.PauseAsync(false);await realStudio.Studio.ApplyThemeAsync(appearance);
                realStudio.Sections.SelectedIndex=0;
                Require(IsVisible,"Closing Boberto during suspension leaves the editor alive and ignores closed-window callbacks");
            } finally {realStudio.Close();}
            var loadingStudio=new PersonalizationWindow(appearance,ApplyAppearance,true,testProfile:studioFixture){Owner=this};
            loadingStudio.Show();loadingStudio.Close();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
            var reopenedStudio=new PersonalizationWindow(appearance,ApplyAppearance,true,testProfile:studioFixture){Owner=this};
            try{reopenedStudio.Show();Require(await reopenedStudio.Studio!.EvaluateAsync("window.desktopStudio.facts().ready")=="true","Boberto can reopen after closing a studio during initialization");}finally{reopenedStudio.Close();}
            var consoleDock=bobertoDock!;await consoleDock.EnsureLiveAsync();var afterStudio=await DockUntil(consoleDock,f=>f.GetProperty("adopted").GetBoolean());
            Require(afterStudio.GetProperty("adopted").GetBoolean()&&!consoleDock.IsPaused&&consoleDock.IsLive,"The console Boberto keeps running after Studio and the preview close: they share its renderer and only hide, never suspend it"+$" (recoveries {consoleDock.Recoveries})");
            Require(Editor.FontWeight==FontWeights.Normal, "Selected Query tab does not make the SQL document inherit a bold header weight");
            Require(AppTypography.Inter.GetTypefaces().Any(face=>face.TryGetGlyphTypeface(out var glyph)&&glyph.FontUri.ToString().Contains("Assets/Fonts",StringComparison.OrdinalIgnoreCase)) && AppTypography.Mono.GetTypefaces().Any(face=>face.TryGetGlyphTypeface(out var glyph)&&glyph.FontUri.ToString().Contains("Assets/Fonts",StringComparison.OrdinalIgnoreCase)), "Inter and JetBrains Mono resolve from bundled font resources instead of system fallbacks");
            var surfaceColor=(Color)ColorConverter.ConvertFromString(Themes.Current.Colors["Surface"]);var selectionColor=((SolidColorBrush)Editor.TextArea.SelectionBrush).Color;
            Require(Math.Abs(selectionColor.R-surfaceColor.R)+Math.Abs(selectionColor.G-surfaceColor.G)+Math.Abs(selectionColor.B-surfaceColor.B)>80, "SQL keyboard selection is visibly separated from the dark editor surface");
            string appearanceFixture=Path.Combine(Path.GetTempPath(),"ase-appearance-"+Guid.NewGuid().ToString("N")+".json");
            try {
                var preferences=new AppearanceSettings(ThemeId:"dark",AccentHex:"#5B8CFF",Compact:true,BobertoVisible:false,BobertoBackground:"aurora",BobertoSize:120);
                AppearanceStore.Save(appearanceFixture,preferences);Require(AppearanceStore.Load(appearanceFixture)==preferences,"Local appearance settings round trip palette, fonts, density and Boberto without connection credentials");
                AppearanceStore.Save(appearanceFixture,preferences);Require(Directory.GetFiles(Path.GetDirectoryName(appearanceFixture)!,Path.GetFileName(appearanceFixture)+".bak*").Length==0,"Saving unchanged appearance settings is idempotent");
                AtomicFiles.SaveUtf8(appearanceFixture,"{\"ThemeId\":\"light\",\"UiSize\":15,\"Motion\":false,\"BobertoBackground\":\"ocean\"}");Require(AppearanceStore.Load(appearanceFixture) is {BobertoMotion:true,BobertoBackground:"ocean"},"Earlier appearance files keep loading and receive an animated companion by default");
                AtomicFiles.SaveUtf8(appearanceFixture,"{\"BobertoBackground\":\"unknown\"}");try{AppearanceStore.Load(appearanceFixture);throw new InvalidOperationException("Accepted unknown backdrop");}catch(ArgumentException){Require(true,"Appearance settings reject unsupported legacy background ids");}
                AtomicFiles.SaveUtf8(appearanceFixture,"{\"ThemeId\":\"unknown\"}");try{AppearanceStore.Load(appearanceFixture);throw new InvalidOperationException("Accepted unknown palette");}catch(ArgumentException){Require(true,"Saved appearance rejects unknown palettes before applying resources");}
                AtomicFiles.SaveUtf8(appearanceFixture,"{\"ThemeId\":\"light\",\"Unexpected\":true}");try{AppearanceStore.Load(appearanceFixture);throw new InvalidOperationException("Accepted unexpected appearance field");}catch(System.Text.Json.JsonException){Require(true,"Appearance files reject unknown fields rather than silently importing them");}
            } finally {foreach(var fixturePath in Directory.GetFiles(Path.GetDirectoryName(appearanceFixture)!,Path.GetFileName(appearanceFixture)+"*"))File.Delete(fixturePath);}
            var menu = VisualChildren<Menu>(this).Single();
            var tools = ToolsMenu;
            tools.IsSubmenuOpen = true;
            await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            var popup = (System.Windows.Controls.Primitives.Popup)tools.Template.FindName("PART_Popup", tools);
            var popupSurface = (Border)popup.Child;
            Require(((SolidColorBrush)popupSurface.Background).Color == (Color)ColorConverter.ConvertFromString(Themes.All[1].Colors["SurfaceRaised"]), "Actual dark menu popup surface uses theme background");
            Require(tools.Items.OfType<MenuItem>().All(item => ((SolidColorBrush)item.Foreground).Color == ((SolidColorBrush)Application.Current.Resources[item.IsEnabled ? "Ink" : "Muted"]).Color), "Menu items expose readable enabled and disabled theme text");
            SetPalette("light");
            Require(((SolidColorBrush)popupSurface.Background).Color == (Color)ColorConverter.ConvertFromString(Themes.All[0].Colors["SurfaceRaised"]), "Open popup updates when switching to light theme");
            tools.IsSubmenuOpen = false;
            SetPalette("dark");
            ClearResults(true); await ExecuteAsync(selectionOnly: true);
            Require(Workspace.Items.Count == 2 && (decimal)((DataTable)((TabItem)Workspace.Items[1]).Tag).Rows[0][0] == 101m, "Selection-only execution uses keyboard selection");
            SetPalette("light");
            Require(((SolidColorBrush)Application.Current.Resources["Surface"]).Color == (Color)ColorConverter.ConvertFromString(Themes.All[0].Colors["Surface"]), "Light theme restores surface colors");
            var themeBuffer=Editor.Text; var themeSelection=(Editor.SelectionStart,Editor.SelectionLength);
            Require(Themes.All.Count==23 && Themes.All.Count(p=>!p.DarkChrome)==6 && Themes.All.Single(p=>p.Id=="pergamino").Colors["Canvas"]=="#0f1513" && Themes.All.Single(p=>p.Id=="neomirai").Colors["Accent"]=="#cb6a3e", "DBMON catalog contains 23 palettes (6 light) including Pergamino and Neo Mirai source colors");
            foreach(string lightId in new[]{"papel","sepia","glaciar","contraste"})
            {
                // Each new light palette applies with a readable derived accent ink and selection, and persists by Id.
                SetPalette(lightId);var applied=Application.Current.Resources;
                Require(CustomAccentPicker.Contrast(((SolidColorBrush)applied["OnAccent"]).Color,((SolidColorBrush)applied["Accent"]).Color)>=4.5&&CustomAccentPicker.Contrast(((SolidColorBrush)applied["Ink"]).Color,((SolidColorBrush)applied["Selection"]).Color)>=4.5,"Light palette "+lightId+" applies a legible accent and selection");
                string lightFixture=Path.Combine(Path.GetTempPath(),"ase-light-"+Guid.NewGuid().ToString("N")+".json");
                try{AppearanceStore.Save(lightFixture,appearance);Require(File.ReadAllText(lightFixture).Contains("\"ThemeId\": \""+lightId+"\"",StringComparison.Ordinal)&&AppearanceStore.Load(lightFixture).ThemeId==lightId,"Light palette "+lightId+" persists by its Id in appearance.json");}
                finally{foreach(var fixturePath in Directory.GetFiles(Path.GetDirectoryName(lightFixture)!,Path.GetFileName(lightFixture)+"*"))File.Delete(fixturePath);}
            }
            SetPalette("light");
            foreach(var palette in Themes.All) { SetPalette(palette.Id); Require(((SolidColorBrush)Application.Current.Resources["Surface"]).Color==(Color)ColorConverter.ConvertFromString(palette.Colors["Surface"]) && Editor.Text==themeBuffer && (Editor.SelectionStart,Editor.SelectionLength)==themeSelection, "Palette applies without altering SQL or selection: "+palette.Label); }
            SetPalette("dark"); ApplyAppearance(appearance with {AccentHex="#5B8CFF"});
            Require(((SolidColorBrush)Application.Current.Resources["Accent"]).Color==(Color)ColorConverter.ConvertFromString("#5B8CFF") && Themes.All[1].Colors["Accent"]=="#00e5ff", "Independent accent updates resources without mutating the DBMON palette");
            ApplyAppearance(appearance with {AccentHex="#12AB34"});Require(((SolidColorBrush)Application.Current.Resources["Accent"]).Color==Color.FromRgb(0x12,0xAB,0x34),"A custom accent outside the presets applies as the shell accent");
            foreach(string bad in new[]{"#12ab34","#12AB3","12AB34","#12AB34FF","red"}){try{(appearance with {AccentHex=bad}).Validate();throw new InvalidOperationException("Accepted accent "+bad);}catch(ArgumentException){}}
            Require(true,"Custom accents accept only upper-case #RRGGBB; short, alpha, named and lower-case spellings are rejected before applying");
            Require(CustomAccentPicker.ColorFromHsv(210,1,1).ToHex()=="#0080FF"&&CustomAccentPicker.ToHsv(Color.FromRgb(0,0x80,0xFF)) is {H:var mixedHue}&&Math.Abs(mixedHue-209.9)<.2&&CustomAccentPicker.TryParse("3ba7ff",out var typed)&&typed.ToHex()=="#3BA7FF"&&!CustomAccentPicker.TryParse("#xyz123",out _),"Accent mixer converts between HSV and hex and normalizes typed values");
            var mixerWindow=new PersonalizationWindow(appearance,ApplyAppearance,testProfile:BobertoProfile){Owner=this};
            try{mixerWindow.Show();mixerWindow.AccentPicker.Load("#7A3BFF");mixerWindow.AccentPicker.Hex.Text="#7a3bff";mixerWindow.AccentPicker.Hex.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(mixerWindow)!,0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
                Require(appearance.AccentHex=="#7A3BFF"&&((SolidColorBrush)Application.Current.Resources["Accent"]).Color==Color.FromRgb(0x7A,0x3B,0xFF),"Typing a hex in the accent mixer and pressing Enter commits a normalized custom accent live");
                mixerWindow.AccentPicker.Visibility=Visibility.Visible;mixerWindow.UpdateLayout();mixerWindow.AccentPicker.BringIntoView();mixerWindow.UpdateLayout();CaptureSmokeVisual(mixerWindow,"personalize-custom-accent.png");
                string accentFixture=Path.Combine(Path.GetTempPath(),"ase-accent-"+Guid.NewGuid().ToString("N")+".json");
                try{AppearanceStore.Save(accentFixture,appearance);Require(AppearanceStore.Load(accentFixture).AccentHex=="#7A3BFF","A custom accent survives the appearance store round trip");}finally{foreach(var fixturePath in Directory.GetFiles(Path.GetDirectoryName(accentFixture)!,Path.GetFileName(accentFixture)+"*"))File.Delete(fixturePath);}
            }finally{mixerWindow.Close();}
            Require(new AppearanceSettings().EditorFont=="consolas"&&Application.Current.Resources["MonoFont"] is FontFamily defaultMono&&defaultMono.Source.Contains("Consolas",StringComparison.Ordinal),
                "A fresh install writes SQL in Consolas"+$" (default {new AppearanceSettings().EditorFont}, applied {Application.Current.Resources["MonoFont"]})");
            Require(WheelZoomStep(ModifierKeys.Control,120)==1&&WheelZoomStep(ModifierKeys.Control,-120)==-1&&WheelZoomStep(ModifierKeys.None,120)==0&&WheelZoomStep(ModifierKeys.Control|ModifierKeys.Shift,120)==0,
                "Only Ctrl and the wheel resize the query text, up or down; the wheel alone keeps scrolling");
            double sizeBeforeWheel=appearance.EditorSize;
            Zoom(WheelZoomStep(ModifierKeys.Control,120));
            Require(appearance.EditorSize==sizeBeforeWheel+1&&Editor.FontSize==sizeBeforeWheel+1,"A wheel step up grows the query text and is stored in appearance"+$" ({sizeBeforeWheel} to {appearance.EditorSize})");
            Zoom(WheelZoomStep(ModifierKeys.Control,-120));
            Require(appearance.EditorSize==sizeBeforeWheel&&Editor.FontSize==sizeBeforeWheel,"A wheel step down returns the query text to its previous size");
            ApplyAppearance(appearance with {ThemeId="light",AccentHex=null});
            var dialog = new ConnectionWindow();
            string themeBeforeLogin=appearance.ThemeId;
            Require(!VisualChildren<TextBlock>(dialog).Any(t=>t.Text=="Color:")&&dialog.SavedProfiles!=null&&dialog.Fields.Style==Application.Current.Resources["SegmentedTabs"],"Login has no palette selector: it offers saved profiles in segmented tabs");
            dialog.Show();dialog.UpdateLayout();CaptureSmokeVisual(dialog,"connection.png");
            Require(dialog.Charset.Text=="ServerDefault" && dialog.LoginLanguage.Text=="us_english", "Connection form exposes explicit charset and language defaults");
            Require(dialog.DriverAvailable, "Installed SAP x64 ODBC driver detected without authentication");
            var prewarm=await DriverPrewarm.Running.WaitAsync(TimeSpan.FromSeconds(30));
            Require(prewarm!=null,"ODBC driver and SAP crypto libraries are preloaded in the background at startup, without connecting");
            checks.Add("PERF ODBC driver preload (slcryptokernel, sapcrypto, sybdrvodb64): "+prewarm!.Value.TotalMilliseconds.ToString("F0")+" ms off the UI thread; no server contacted");
            Require(dialog.IniPath.Text.Length > 0 && dialog.ServerAliases.Items.Count > 0 && dialog.ServerAliases.SelectedIndex == -1, "Local sql.ini discovered and read without choosing a server");
            try { dialog.ReadProfile(); throw new InvalidOperationException("Accepted empty connection profile"); }
            catch (ArgumentException) { Require(true, "Empty connection form rejected before networking"); }
            dialog.ManualMode.IsChecked = true; dialog.Server.Text = "test-host"; dialog.Database.Text = "test-db"; dialog.UserName.Text = "fixture-user"; dialog.Password.Password = "fixture-password-only";

            ApplyAppearance(appearance with {ThemeId="light",AccentHex=null});
            var selection=dialog.ReadSelection();


            var beforeActivation=engine;var beforeProfile=lastProfile;string beforeActivationSql=Editor.Text;
            try {
                var acceptedEngine=new DemoQueryEngine();ActivateConnection(acceptedEngine,selection,[new QueryMessage(5701,"ODBC 01000","Synthetic login message.","Synthetic raw login evidence.")]);
                Require(ReferenceEquals(engine,acceptedEngine)&&lastProfile==selection.Profile&&appearance.ThemeId==themeBeforeLogin&&Workspace.SelectedItem==QueryTab&&Editor.Text==beforeActivationSql&&log[^1].RawText=="Synthetic raw login evidence.","Successful login UI activation consumes captured identity and diagnostics and leaves the chosen palette alone");
            }finally{engine=beforeActivation;lastProfile=beforeProfile;RefreshConnection();}
            string profileStore=Path.Combine(AppContext.BaseDirectory,"UiSmoke","profiles.dat");
            ProfileStore.OverridePath=profileStore;
            try {
                foreach(string stale in Directory.Exists(Path.GetDirectoryName(profileStore)!)?Directory.GetFiles(Path.GetDirectoryName(profileStore)!,"profiles.dat*"):[])File.Delete(stale);
                dialog.RememberPassword.IsChecked=true;var stored=dialog.StoreCurrent("fixture profile");
                byte[] sealedBytes=File.ReadAllBytes(profileStore);string raw=Encoding.UTF8.GetString(sealedBytes)+Encoding.Unicode.GetString(sealedBytes);
                Require(stored.HasPassword&&!raw.Contains("fixture-password-only")&&!raw.Contains("test-host")&&!raw.Contains("fixture-user"),"Saved profiles are encrypted for the Windows user: neither password nor host appear in the file");
                var reopened=new ConnectionWindow();
                try {
                    var listed=reopened.SavedProfiles.Items.OfType<SavedLogin>().Single();reopened.ApplySavedLogin(listed);
                    Require(listed.Name=="fixture profile"&&reopened.Password.Password=="fixture-password-only"&&reopened.RememberPassword.IsChecked==true&&reopened.UserName.Text=="fixture-user"&&reopened.ManualMode.IsChecked==true&&reopened.Server.Text=="test-host","A new login window lists the saved profile and restores its settings and remembered password");
                    reopened.RememberPassword.IsChecked=false;var forgotten=reopened.StoreCurrent("Fixture Profile");
                    var reloaded=ProfileStore.Load(profileStore);
                    Require(!forgotten.HasPassword&&reloaded.Count==1&&!reloaded[0].HasPassword&&reloaded[0].Name=="Fixture Profile","Saving with Remember password off replaces the profile by name and drops its password");
                    Require(Directory.GetFiles(Path.GetDirectoryName(profileStore)!,"profiles.dat*").Length==1,"Profile store keeps no backup copies that could retain a forgotten password");
                    ProfileStore.Save(profileStore,ProfileStore.Remove(reloaded,"fixture profile"));Require(ProfileStore.Load(profileStore).Count==0,"Deleting a profile removes it from the store");
                    File.WriteAllBytes(profileStore,"DBMONP1\nnot a protected blob"u8.ToArray());
                    Require(Throws<IOException>(()=>ProfileStore.Load(profileStore))&&Throws<IOException>(()=>{File.WriteAllText(profileStore,"[]");ProfileStore.Load(profileStore);}),"Tampered or plain-text profile files are rejected");
                    var broken=new ConnectionWindow();try{Require(broken.SavedProfiles.Items.Count==0&&!broken.SavedProfiles.IsEnabled&&broken.Feedback.Text.StartsWith("Saved profiles could not be read"),"An unreadable profile store leaves login usable and says why");}finally{broken.Close();}
                } finally {reopened.Close();}
            } finally {foreach(string file in Directory.GetFiles(Path.GetDirectoryName(profileStore)!,"profiles.dat*"))File.Delete(file);ProfileStore.OverridePath=null;dialog.RememberPassword.IsChecked=false;}
            dialog.EncryptData.IsChecked = true;
            ConnectionProfile captured = dialog.ReadProfile();
            Require(captured.Server == "test-host" && captured.Database == "test-db" && captured.UseTls && !captured.ToString().Contains("fixture-password-only"), "Connection form captures caller inputs without password in profile");
            dialog.ManualMode.IsChecked = false;
            dialog.SetCatalog(SqlIniParser.Parse("[fixture]\nquery=NLWNSCK,fixture-host,5010,ssl\n[listener]\nmaster=TCP,listener-host,5500\n[plain]\nquery=TCP,plain-host,5005"));
            dialog.ServerAliases.SelectedIndex = 0; dialog.Charset.Text = "utf8"; dialog.Hostname.Text = "fixture-client"; dialog.EncryptPassword.IsChecked = false;
            captured = dialog.ReadProfile();
            Require(captured.ServerAlias == "fixture" && captured.Server == "fixture-host" && captured.Port == 5010 && captured.UseTls && !captured.EncryptPassword && captured.Charset == "utf8" && captured.ClientHostname == "fixture-client", "sql.ini selection resolves owned endpoint and forwards caller connection options");
            Require(!dialog.EncryptData.IsEnabled, "SSL endpoint requires encrypted transport");
            dialog.ServerAliases.SelectedIndex = 1;
            try { dialog.ReadProfile(); throw new InvalidOperationException("Accepted master-only alias"); }
            catch (ArgumentException) { Require(true, "Master-only alias rejected without networking"); }
            dialog.EncryptPassword.IsChecked = true; dialog.ServerAliases.SelectedIndex = 2;
            captured = dialog.ReadProfile();
            Require(!captured.UseTls && captured.EncryptPassword && dialog.EncryptData.IsEnabled, "Plain TCP alias starts without TLS and keeps password encryption independent");
            Require(new System.Data.Odbc.OdbcConnectionStringBuilder(AseIsql.Odbc.OdbcSession.BuildConnectionString(captured, "fixture-password-only"))["Encryption"].ToString() == "none", "Plain TCP form produces non-TLS ODBC transport without a CA file");
            dialog.ServerAliases.SelectedIndex = 0;
            Require(dialog.EncryptData.IsChecked == true && !dialog.EncryptData.IsEnabled, "Switching back to SSL restores required TLS");
            dialog.SetCatalog(SqlIniParser.Parse("[repserver]\nmaster=TCP,rs-host,5510\n[iqserver]\nmaster=TCP,iq-host,2638\nquery=TCP,iq-host,2638"));
            dialog.EncryptData.IsChecked=false;dialog.ServerAliases.SelectedIndex=0;
            try{dialog.ReadProfile();throw new InvalidOperationException("ASE accepted a master-only alias");}catch(ArgumentException){Require(true,"SAP ASE still refuses an alias that lists only its master entry");}
            dialog.SelectPlatform(ServerPlatform.Rs);dialog.UpdateLayout();
            captured=dialog.ReadProfile();
            Require(captured is {Platform:ServerPlatform.Rs,Server:"rs-host",Port:5510,Database:""}&&!dialog.Database.IsEnabled&&dialog.LanguageRow.Visibility==Visibility.Visible&&dialog.IqServerRow.Visibility==Visibility.Collapsed,"Replication Server login uses the alias's master entry, has no database and keeps the TDS login options");
            dialog.SelectPlatform(ServerPlatform.Iq);dialog.ServerAliases.SelectedIndex=1;dialog.Database.Text="";dialog.IqServerName.Text="iq_dev";dialog.UpdateLayout();
            captured=dialog.ReadProfile();
            Require(captured is {Platform:ServerPlatform.Iq,Server:"iq-host",Port:2638,Database:"",IqServerName:"iq_dev"}&&dialog.Database.IsEnabled&&dialog.IqServerRow.Visibility==Visibility.Visible&&dialog.LanguageRow.Visibility==Visibility.Collapsed&&dialog.DriverStatus.Text.Contains(OdbcSession.DriverFor(ServerPlatform.Iq)),"SAP IQ login names the IQ driver, offers the optional IQ server name and database, and hides ASE-only options");
            CaptureSmokeVisual(dialog,"connection-iq.png");
            string iqCa=Path.GetTempFileName(),iqId=Path.GetTempFileName(),iqStore=Path.Combine(Path.GetTempPath(),"dbmon-iq-profiles-"+Guid.NewGuid().ToString("N")+".dat");
            try {
                ProfileStore.OverridePath=iqStore;
                dialog.EncryptData.IsChecked=true;dialog.TrustedFile.Text=iqCa;dialog.IqIdentityFile.Text=iqId;dialog.IqCertificateName.Text="IQDBQA";
                dialog.Password.Password="fixture-db-password";dialog.IqIdentityPassword.Password="fixture-key-password";dialog.RememberPassword.IsChecked=true;
                var iqSaved=dialog.StoreCurrent("IQ TLS fixture");
                var iqLoaded=ProfileStore.Load(iqStore).Single();
                Require(iqLoaded.Profile.IqIdentityFile==iqId&&iqLoaded.Profile.TrustedFile==iqCa&&iqLoaded.Profile.IqCertificateName=="IQDBQA"&&iqLoaded.Password=="fixture-db-password"&&!System.Text.Json.JsonSerializer.Serialize(iqLoaded).Contains("fixture-key-password"),"IQ profiles preserve both certificate paths, expected certificate name and remember only the database password");
                var iqRestored=new ConnectionWindow(iqLoaded.Profile);
                try {
                    iqRestored.ApplySavedLogin(iqLoaded);
                    Require(iqRestored.IqIdentityFile.Text==iqId&&iqRestored.TrustedFile.Text==iqCa&&iqRestored.IqCertificateName.Text=="IQDBQA"&&iqRestored.IqIdentityPassword.Password.Length==0&&iqRestored.IqIdentityRow.Visibility==Visibility.Visible,"Restoring an IQ profile restores the verified certificate name and requires fresh identity password input");
                    iqRestored.IqIdentityPassword.Password="fixture-key-password";
                }finally{iqRestored.Close();}
                Require(iqRestored.IqIdentityPassword.Password.Length==0,"Closing login clears the identity password");
                dialog.AdvancedOptions.IsExpanded=true;dialog.UpdateLayout();dialog.IqIdentityRow.BringIntoView();dialog.UpdateLayout();
                Require(dialog.IqIdentityPassword.Focus()&&dialog.IqIdentityPassword.IsKeyboardFocused,"IQ identity password accepts keyboard focus in the expanded login options");
                CaptureSmokeVisual(dialog,"connection-iq-identity.png");
                dialog.RememberIdentityPassword.IsChecked=true;var bothSaved=dialog.StoreCurrent("IQ TLS fixture");
                var bothLoaded=ProfileStore.Load(iqStore).Single();
                Require(bothLoaded.HasIdentityPassword&&bothLoaded.IdentityPassword=="fixture-key-password"&&bothLoaded.Password=="fixture-db-password","Remember identity password stores it independently alongside the database password");
                dialog.UpdateLayout();CaptureSmokeVisual(dialog,"connection-iq-password-remembered.png");
                dialog.Fields.SelectedIndex=1;dialog.UpdateLayout();CaptureSmokeVisual(dialog,"connection-iq-profile-passwords.png");dialog.Fields.SelectedIndex=0;
                string encryptedIdentity=Encoding.UTF8.GetString(File.ReadAllBytes(iqStore))+Encoding.Unicode.GetString(File.ReadAllBytes(iqStore));
                Require(!encryptedIdentity.Contains("fixture-key-password")&&!encryptedIdentity.Contains("fixture-db-password"),"Neither remembered password appears in the DPAPI profile bytes");
                var identityReopened=new ConnectionWindow();
                try{
                    identityReopened.ApplySavedLogin(bothLoaded);
                    Require(identityReopened.IqIdentityPassword.Password=="fixture-key-password"&&identityReopened.RememberIdentityPassword.IsChecked==true&&identityReopened.Password.Password=="fixture-db-password","A new login restores both saved passwords with independent remember controls");
                }finally{identityReopened.Close();}
                var iqProfileSelector=dialog.SavedProfiles??throw new InvalidOperationException("Saved profile selector is unavailable");
                iqProfileSelector.SelectedIndex=0;dialog.ProfileList.SelectedIndex=0;
                Require(dialog.ForgetIdentityPasswordButton.IsEnabled,"Profiles enables forgetting a saved identity password");
                dialog.ForgetIdentityPasswordButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var onlyDatabase=ProfileStore.Load(iqStore).Single();
                Require(!onlyDatabase.HasIdentityPassword&&onlyDatabase.Password=="fixture-db-password"&&dialog.IqIdentityPassword.Password.Length==0&&dialog.RememberIdentityPassword.IsChecked==false,"Forget identity password clears only its stored and loaded secret");
                dialog.IqIdentityPassword.Password="fixture-key-password";dialog.RememberIdentityPassword.IsChecked=true;dialog.StoreCurrent("IQ TLS fixture");
                iqProfileSelector.SelectedIndex=0;dialog.ProfileList.SelectedIndex=0;dialog.ForgetPasswordButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var onlyIdentity=ProfileStore.Load(iqStore).Single();
                Require(onlyIdentity.HasIdentityPassword&&!onlyIdentity.HasPassword&&dialog.Password.Password.Length==0&&dialog.RememberPassword.IsChecked==false,"Forget login password preserves the remembered identity password");
                dialog.RememberIdentityPassword.IsChecked=false;dialog.StoreCurrent("IQ TLS fixture");
                Require(!ProfileStore.Load(iqStore).Single().HasIdentityPassword,"Saving with Remember identity password off removes its stored value");
                var oldLogin=System.Text.Json.JsonSerializer.Deserialize<SavedLogin>("{\"Name\":\"Legacy\",\"Profile\":"+System.Text.Json.JsonSerializer.Serialize(iqSaved.Profile)+",\"Password\":\"fixture-db-password\"}")!;
                Require(!oldLogin.HasIdentityPassword&&oldLogin.Password=="fixture-db-password","Legacy profiles without an identity password remain readable");
                Require(!File.Exists(iqStore+".bak"),"Forgetting either password leaves no profile backup");
                dialog.ApplySavedLogin(iqSaved);
                Require(dialog.IqIdentityPassword.Password.Length==0,"Loading another profile clears the transient identity password");
                dialog.IqIdentityPassword.Password="fixture-key-password";dialog.IqIdentityFile.Text=iqId+".changed";
                Require(dialog.IqIdentityPassword.Password.Length==0,"Changing the identity path clears the prior key password");
                dialog.IqIdentityFile.Text=iqId;dialog.IqIdentityPassword.Password="fixture-key-password";
                dialog.SelectPlatform(ServerPlatform.Ase);
                Require(dialog.IqIdentityRow.Visibility==Visibility.Collapsed&&dialog.IqIdentityPassword.Password.Length==0,"ASE hides the IQ identity and clears its password");
                dialog.SelectPlatform(ServerPlatform.Iq);
            }finally{ProfileStore.OverridePath=null;File.Delete(iqCa);File.Delete(iqId);File.Delete(iqStore);}
            var restored=new ConnectionWindow(captured);try{Require(restored.SelectedPlatform==ServerPlatform.Iq&&restored.IqServerName.Text=="iq_dev","A saved IQ profile reopens with its server type and IQ server name");}finally{restored.Close();}
            dialog.SelectPlatform(ServerPlatform.Ase);
            dialog.Close();
            Editor.Text = DemoSql; savedText = Editor.Text;
            QueryClick(this, new RoutedEventArgs());
            Editor.Select(Editor.Text.IndexOf("SELECT", StringComparison.Ordinal), "SELECT 42 AS numero, 'Hello, ASE' AS saludo, NULL AS vacio;".Length);
            ClearResults(true); await ExecuteAsync();
            Require(Workspace.Items.Count == 2, "Selection executes only one statement");
            Editor.Text = "select 7 as current_line;\nselect 8 as next_line;";
            Editor.TextArea.Caret.Line = 1;
            ClearResults(true); await ExecuteAsync(true);
            Require(Workspace.Items.Count == 2 && (decimal)((DataTable)((TabItem)Workspace.Items[1]).Tag).Rows[0][0] == 7m, "Current line executes independently");
            Editor.Text = "dbcc sqltext(1)";
            AddMessage(0,"CLIENT","earlier execution fixture");
            ClearResults(true); await ExecuteAsync();
            Require(Workspace.Items.Count == 1 && log[^1].Number == 1, "Unsupported DBCC becomes message, no simulated result");
            Require(log.All(message=>!message.Text.Contains("earlier execution fixture"))&&log.Any(message=>message.Number==1),"New executions replace earlier messages and preserve current failure diagnostics");
            Editor.Text="select 91 as latest_run";await ExecuteAsync();Require(log.All(message=>message.Number==0),"A successful execution removes preceding failure messages");
            var priorEngine=engine;
            try {
                engine=new DiagnosticSmokeEngine();await ExecuteAsync();
                Require(log.Any(message=>message.Text=="Fixture server error.")&&log.All(message=>!message.Text.Contains("[SAP]"))&&log.Last(message=>message.Number==1).RawText==DiagnosticSmokeEngine.Raw,"Messages keeps normalized records and an unframed failure summary with original error evidence accessible");
            }finally{engine=priorEngine;}
            await VerifyLiveMessages(Require);
            Editor.Select(0,0);await ExecuteAsync(selectionOnly:true);Require(log.Count==1&&log[0].Number==1,"Empty selection reports only the current execution attempt");
            SetLogVisible(false); SetLogVisible(true);
            Require(LogPanel.Visibility == Visibility.Visible && ShowLogMenu.IsChecked, "Log toggle restores visible state");
            BrowserClick(this, new RoutedEventArgs());
            Require(BrowserPanel.Visibility == Visibility.Visible, "Object browser opens disconnected state");
            BrowserClick(this, new RoutedEventArgs());
            var catalogRows=new DataTable();foreach(var column in new[]{"id","owner","name","type"})catalogRows.Columns.Add(column,typeof(object));catalogRows.Rows.Add(42,"app_owner","orders","U ");catalogRows.Rows.Add(43,DBNull.Value,"orphan","V");
            var parsedObjects=ParseCatalogObjects(catalogRows,"sales db");
            Require(parsedObjects.Count==2&&parsedObjects[0] is {Id:42,Owner:"app_owner",Type:"U"}&&parsedObjects[0].QualifiedName=="[sales db].[app_owner].[orders]"&&parsedObjects[1].Owner==null,"Object catalog rows project into typed nodes and keep unknown owners unknown");
            try{_=parsedObjects[1].QualifiedName;throw new InvalidOperationException("Resolved an unknown owner");}catch(ArgumentException){Require(true,"An object without a resolved owner is never qualified with a guessed schema");}
            var narrowCatalog=new DataTable();narrowCatalog.Columns.Add("id");narrowCatalog.Rows.Add("1");
            try{ParseCatalogObjects(narrowCatalog,"master");throw new InvalidOperationException("Accepted a narrow catalog");}catch(FormatException){Require(true,"Unexpected catalog shapes are rejected before any tree node is built");}
            var badId=catalogRows.Clone();badId.Rows.Add("not-a-number","dbo","x","U");
            try{ParseCatalogObjects(badId,"master");throw new InvalidOperationException("Accepted a non-numeric object id");}catch(FormatException){Require(true,"Non-numeric object ids become a catalog error rather than a UI crash");}
            Require(ObjectsSql("sales db").Contains("[sales db]..sysusers u on u.uid=o.uid")&&!ObjectsSql("sales db").Contains("user_name("),"Object owners resolve through sysusers of the same database");
            string objectPage=ObjectsPageSql("sales db","P","a%_[b",new CatalogObject("sales db","dbo","x'y","P",7));
            Require(objectPage.Contains("select top 101")&&objectPage.Contains("like '%a\\%\\_\\[b%' escape '\\'")&&objectPage.Contains("o.name > 'x''y'")&&objectPage.Contains("o.id > 7")&&objectPage.EndsWith("order by o.name, o.id"),"Object pages fetch 100 at a time with escaped server-side filters and keyset continuation");
            string iqPage=IqObjectsPageSql("U","a%_[b",new CatalogObject("iqdb","dbo","x'y","U",7,ServerPlatform.Iq));
            Require(iqPage.StartsWith("select top 101 ",StringComparison.Ordinal)&&iqPage.Contains("from SYS.SYSTAB t join SYS.SYSUSER u on u.user_id = t.creator")&&iqPage.Contains("t.table_type_str in ('BASE','GBL TEMP')")&&iqPage.Contains("like '%a\\%\\_\\[b%' escape '\\'")&&iqPage.Contains("t.table_name > 'x''y'")&&iqPage.Contains("t.table_id > 7")&&iqPage.EndsWith("order by t.table_name, t.table_id")&&!iqPage.Contains(".."),"IQ explorer pages tables from the SQL Anywhere catalog with the escaped filter and keyset continuation");
            Require(IqObjectsPageSql("P","",null).Contains("from SYS.SYSPROCEDURE p")&&IqObjectsPageSql("V","",null).Contains("('VIEW','MAT VIEW')")&&IqObjectsPageSql("S","",null).Contains("u.user_name in ('SYS','dbo')")&&CatalogGroupsFor(ServerPlatform.Iq).Select(g=>g.Label).SequenceEqual(new[]{"Tables","Views","Procedures","System objects"}),"IQ explorer groups tables, views, procedures and SYS/dbo system objects");
            var iqObjects=ParseCatalogObjects(catalogRows,"iqdb",ServerPlatform.Iq);
            Require(iqObjects[0].QualifiedName=="[app_owner].[orders]"&&iqObjects[0].Target.Database==null&&iqObjects[0].Target.Owner=="app_owner","IQ objects are named owner.name, without an ASE database part");
            var iqTarget=new ObjectTarget(null,"app","orders");
            Require(IqObjectHelperSql("columns",iqTarget)=="sp_iqcolumn 'orders', 'app'"&&IqObjectHelperSql("indexes",iqTarget)=="sp_iqindex 'orders', null, 'app'"&&IqObjectHelperSql("size",iqTarget)=="sp_iqtablesize 'app.orders'"&&IqObjectHelperSql("top",iqTarget)=="SELECT TOP 100 * FROM [app].[orders]"&&IqObjectHelperSql("columns",new ObjectTarget(null,null,"orders"))=="sp_iqcolumn 'orders', null","IQ object actions use sp_iqcolumn, sp_iqindex and sp_iqtablesize with quoted owner and name");
            try{IqObjectHelperSql("columns",new ObjectTarget("db","app","orders"));throw new InvalidOperationException("IQ accepted a database part");}catch(ArgumentException){Require(true,"IQ object actions refuse a database-qualified name");}
            Require(IqCodeSql(new CatalogObject("iqdb","app","p1","P",42,ServerPlatform.Iq))=="select p.proc_defn from SYS.SYSPROCEDURE p where p.proc_id = 42"&&IqCodeSql(new CatalogObject("iqdb","app","v1","V",43,ServerPlatform.Iq)).Contains("SYS.SYSVIEW v join SYS.SYSTAB t on t.object_id = v.view_object_id where t.table_id = 43"),"IQ view and procedure code is read by catalog id");
            var manyObjects=Enumerable.Range(0,101).Select(i=>new CatalogObject("db","dbo","n"+i,"U",i)).ToList();var pagedObjects=PageObjects(manyObjects);
            Require(pagedObjects.Page.Count==100&&pagedObjects.More&&!PageObjects(manyObjects.Take(100).ToList()).More,"Explorer shows 100 objects and offers more only when the server returned a 101st");
            Require(ParseObjectToken("afiliados") is {Database:null,Owner:null,Name:"afiliados"}&&ParseObjectToken("dbo.afiliados") is {Owner:"dbo",Name:"afiliados"}&&ParseObjectToken("afilmed..afiliados") is {Database:"afilmed",Owner:null,Name:"afiliados"}&&ParseObjectToken("[my db].dbo.[a.b]") is {Database:"my db",Name:"a.b"}&&ParseObjectToken("select *")==null&&ParseObjectToken("@var")==null&&ParseObjectToken("42")==null,"Editor identifiers resolve name, owner, database and bracketed parts; expressions and variables are not objects");
            var helperTarget=ParseObjectToken("afilmed..afiliados")!;
            Require(ObjectHelperSql("help",helperTarget)=="exec afilmed..sp_help 'afiliados'"&&ObjectHelperSql("top",helperTarget)=="SELECT TOP 100 * FROM [afilmed]..[afiliados]"&&ObjectHelperSql("code",ParseObjectToken("dbo.sp_x")!).Contains("object_id('dbo.sp_x')"),"Object actions build read-only helper SQL in the object's database context");
            string editorBeforeMenu=Editor.Text;Editor.Text="select * from afiliados where 1=1";Editor.Select(0,0);Editor.TextArea.Caret.Offset=18;
            var editorMenu=(ContextMenu)Editor.TextArea.ContextMenu;BuildEditorMenu(editorMenu);var editorItems=editorMenu.Items.OfType<MenuItem>().ToList();
            Require(editorItems[0].Header.Equals("Object · afiliados")&&editorItems.Any(i=>i.Header.Equals("Checking database, owner and object type…")&&!i.IsEnabled)&&!editorItems.Any(i=>i.Header.Equals("View code"))&&editorItems.Any(i=>i.Header.Equals("Execute selection")&&!i.IsEnabled)&&editorItems.Any(i=>i.Header.Equals("Paste")),"Right-click preserves editing and execution while withholding object actions until catalog resolution");
            Editor.Text=editorBeforeMenu;
            Require(string.Concat(Wordmark.Children.OfType<TextBlock>().Select(letter=>letter.Text))=="DBMON SQL"&&Title.Contains("DBMON SQL")&&Icon!=null&&BrandMark!=null&&typeof(MainWindow).Assembly.GetName().Name=="DBMON SQL","The product is DBMON SQL: Boberto mark in the topbar and as the window icon, DBMON-style letter wordmark and window title");
            Require(QueryTab.Template.FindName("Frame",QueryTab) is Border tabFrame&&tabFrame.CornerRadius==(CornerRadius)Application.Current.Resources["ControlRadius"]&&!appearance.Rounded==(tabFrame.CornerRadius.TopLeft==0)&&shellMotion==(appearance.BobertoMotion&&SystemParameters.ClientAreaAnimation)&&Application.Current.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] is Style&&Application.Current.Resources.Contains("TabIcon"),"Tabs use the DBMON segmented format (square Neo Mirai by default), scrollbars the thin shared template and the topbar edge follows the motion setting"+$" (frame {(QueryTab.Template.FindName("Frame",QueryTab) as Border)?.CornerRadius}, resource {Application.Current.Resources["ControlRadius"]}, rounded {appearance.Rounded}, motion {shellMotion}/{appearance.BobertoMotion}/{SystemParameters.ClientAreaAnimation})");
            Require(NullDisplayConverter.Instance.Convert(DBNull.Value, typeof(string), null!, CultureInfo.InvariantCulture).Equals(""), "NULL display remains empty");
            Editor.Text = "select 'source preserved' as [a,b]";
            await ExecuteAsync();
            await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            var aliasGrid = ((DockPanel)((TabItem)Workspace.SelectedItem).Content).Children.OfType<DataGrid>().Single();
            Require(aliasGrid.Columns[0].Header.Equals("a,b") && aliasGrid.Columns[0].GetCellContent(aliasGrid.Items[0]) is TextBlock { Text: "source preserved" }, "Column display names do not become binding syntax");
            var copyTable = new DataTable();
            copyTable.Columns.Add("first", typeof(string)); copyTable.Columns.Add("second", typeof(string));
            copyTable.Rows.Add("z", "not-selected"); copyTable.Rows.Add("a", "line,one\nline two"); copyTable.DefaultView.Sort = "first ASC";
            AddResult(copyTable, 2); var copyTab = (TabItem)Workspace.Items[Workspace.Items.Count - 1]; Workspace.SelectedItem = copyTab;
            await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            var copyGrid = ((DockPanel)copyTab.Content).Children.OfType<DataGrid>().Single();
            copyGrid.Items.SortDescriptions.Clear();
            copyGrid.Items.SortDescriptions.Add(new SortDescription(copyGrid.Columns[0].SortMemberPath, ListSortDirection.Ascending));
            Require(((DataRowView)copyGrid.Items[0]).Row[0].Equals("a"), "Grid sorting binds to owning source column identity");
            copyGrid.Columns[1].DisplayIndex = 0;
            copyGrid.SelectedCells.Add(new DataGridCellInfo(copyGrid.Items[0], copyGrid.Columns[0]));
            copyGrid.SelectedCells.Add(new DataGridCellInfo(copyGrid.Items[1], copyGrid.Columns[1]));
            Require(BuildResultCopy(copyGrid, ResultCopy.Selection, '\t') == "\ta\r\nnot-selected\t\r\n", "Sparse clipboard selection follows visual order and leaves unselected cells empty");
            Require(BuildResultCopy(copyGrid, ResultCopy.WithHeaders, '\t').StartsWith("second\tfirst\r\n"), "Copy with headers follows reordered source captions");
            Require(BuildResultCopy(copyGrid, ResultCopy.HeadersOnly, '\t') == "second\tfirst\r\n", "Copy headers only excludes result rows");
            Require(BuildResultCopy(copyGrid, ResultCopy.AllWithHeaders, ',', delimited: true) == "second,first\r\n\"line,one\nline two\",a\r\nnot-selected,z\r\n", "Copy all CSV includes complete sorted rows and escaped multiline cells");
            SelectContextCell(copyGrid, copyGrid.Items[0], copyGrid.Columns[0]);
            Require(copyGrid.SelectedCells.Count == 2, "Right click inside selected cells preserves multi-cell selection");
            SelectContextCell(copyGrid, copyGrid.Items[0], copyGrid.Columns[1]);
            Require(copyGrid.SelectedCells.Count == 1 && copyGrid.SelectedCells[0].Column == copyGrid.Columns[1], "Right click on unselected cell targets that cell");
            Require(copyGrid.ContextMenu.Items.OfType<MenuItem>().Any(item => item.Header.Equals("Copy delimited")), "Result context menu offers delimiter choices");
            VerifyResultSelectionAndClipboard(copyGrid, Require);
            var filterPanel=((DockPanel)copyTab.Content).Children.OfType<DockPanel>().Single();
            var localFilter=filterPanel.Children.OfType<TextBox>().Single(); localFilter.Text="line,one";
            Require(copyGrid.Items.Count==1 && BuildResultCopy(copyGrid,ResultCopy.All,',', delimited: true).Contains("line two"), "Local filter and full-view copy share visible rows");
            copyGrid.SelectAllCells();
            Require(copyGrid.SelectedCells.Count == 2, "Select all respects the filtered result view");
            localFilter.Text="["; Require(copyGrid.Items.Count==0, "Local filter escapes bracket wildcards as literal values"); localFilter.Text="";
            TogglePin(copyTab); var preservedOrigin=resultOrigins[copyTable];
            Editor.Text="select 12 as recent"; await ExecuteAsync();
            Require(Workspace.Items.Contains(copyTab) && resultOrigins[copyTable]==preservedOrigin, "Pinned result survives later execution with its original SQL and source");
            TogglePin(copyTab);
            SetPalette("dark"); copyGrid.ContextMenu.IsOpen = true;
            await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            Require(((SolidColorBrush)copyGrid.ContextMenu.Background).Color == (Color)ColorConverter.ConvertFromString(Themes.All[1].Colors["SurfaceRaised"]), "Result context menu uses readable dark palette");
            copyGrid.ContextMenu.IsOpen = false; SetPalette("light");
            copyTab.ContextMenu.Items.OfType<MenuItem>().Single(item=>item.Header.Equals("Close resultset")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Require(!Workspace.Items.Contains(copyTab) && Workspace.Items.Contains(QueryTab), "Close resultset menu removes only its owning result tab");
            while (Workspace.Items.Count > 2) CloseResult((TabItem)Workspace.Items[^1]);
            var wheelTab = (TabItem)Workspace.Items[1]; Workspace.SelectedItem = wheelTab;
            await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            var wheelHeader = (Border)wheelTab.Template.FindName("Frame", wheelTab);
            wheelHeader.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Require(!Workspace.Items.Contains(wheelTab) && Workspace.Items.Count == 1 && Workspace.SelectedItem == QueryTab, "Middle-click header closes final result and returns to Query");
            Editor.Text="select 1;\nselect 2;"; Editor.TextArea.Caret.Offset=0;
            ToggleBookmark(); Editor.Document.Insert(0,"-- inserted\n"); GoBookmark(false);
            Require(Editor.TextArea.Caret.Line==2, "Bookmark anchor follows inserted source lines");
            bookmarks.Clear();
            Editor.Select(0,Editor.Text.Length); CommentClick(this,new RoutedEventArgs());
            Require(Editor.Text.Contains("--select 1;"), "Comment action prefixes selected SQL lines");
            CommentClick(this,new RoutedEventArgs());
            Editor.Text="abc"; Editor.Select(0,3); UpperClick(this,new RoutedEventArgs()); Require(Editor.Text=="ABC", "Selection transform preserves an editable SQL document");
            Editor.FontSize=14; Zoom(1); Require(Editor.FontSize==15, "Editor zoom changes only font size"); Editor.FontSize=14;
            using(var recipes=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Assets","ase-recipes.json"))))
                Require(recipes.RootElement.GetArrayLength()==96 && recipes.RootElement.EnumerateArray().All(item=>item.GetProperty("engine").GetString() is "ASE" or "IQ" or "RS"), "Packaged library holds 96 generic recipes, each tagged SAP ASE, SAP IQ or Replication Server");
            var parameterForm=new ParametersWindow("use :database\ngo\nsp_spaceused ':table', 1\nkill :spid","Recipe fixture","Insert",name=>name=="database"?"master":""){Owner=this};
            try{parameterForm.Show();parameterForm.UpdateLayout();
                Require(parameterForm.Fields.Keys.SequenceEqual(new[]{"database","table","spid"})&&parameterForm.Fields["database"].Text=="master"&&!parameterForm.Primary.IsEnabled,"Parameter form lists each placeholder once, pre-fills known defaults and waits for missing values");
                parameterForm.Fields["table"].Text="o'neil";parameterForm.Fields["spid"].Text="42; shutdown";
                Require(!parameterForm.Primary.IsEnabled&&parameterForm.Build()==null,"A value that would add statements outside quotes is refused before insertion");
                parameterForm.Fields["spid"].Text="42";
                Require(parameterForm.Primary.IsEnabled&&parameterForm.Build()=="use master\ngo\nsp_spaceused 'o''neil', 1\nkill 42","Filled recipe escapes quoted values and inserts bare tokens verbatim");
                CaptureSmokeVisual(parameterForm,"parameters.png");
            }finally{parameterForm.Close();}
            var repeatForm=new RepeatWindow("select 1","query","fixture"){Owner=this};
            try{repeatForm.Show();
                Require(repeatForm.Start.IsEnabled&&repeatForm.Interval==TimeSpan.FromSeconds(30)&&repeatForm.Acknowledge.Visibility==Visibility.Collapsed,"Repeat form starts at 30 seconds for read-only SQL without an extra confirmation");
                repeatForm.IntervalBox.Text="2";Require(!repeatForm.Start.IsEnabled,"Repeat refuses intervals under 5 seconds");
                repeatForm.IntervalBox.Text="1";repeatForm.UnitBox.SelectedIndex=1;repeatForm.RunsBox.Text="0";Require(!repeatForm.Start.IsEnabled&&repeatForm.Interval==TimeSpan.FromMinutes(1),"Repeat refuses a zero run count and converts minutes");
            }finally{repeatForm.Close();}
            var riskyForm=new RepeatWindow("update t set x = 1","selection","fixture"){Owner=this};
            try{riskyForm.Show();riskyForm.UpdateLayout();
                Require(riskyForm.StateChanges.SequenceEqual(new[]{"update"})&&riskyForm.Acknowledge.Visibility==Visibility.Visible&&!riskyForm.Start.IsEnabled,"Repeating SQL that changes data needs an explicit caution acknowledgement");
                riskyForm.Acknowledge.IsChecked=true;Require(riskyForm.Start.IsEnabled,"The acknowledgement enables Start");
                CaptureSmokeVisual(riskyForm,"repeat-caution.png");
            }finally{riskyForm.Close();}
            async Task UntilRepeat(Func<bool> condition){for(int i=0;i<200&&!condition();i++)await Task.Delay(20);}
            string repeatBuffer=Editor.Text;int historyBeforeRepeat=history.Count;
            var twice=new RepeatPlan("select 5 as tick",TimeSpan.FromSeconds(5),2,true,engine,"query");StartRepeat(twice);
            await UntilRepeat(()=>twice.Runs==1&&!repeatBusy);
            Require(RepeatBanner.Visibility==Visibility.Visible&&RepeatTitle.Text=="every 5 s"&&RepeatDetail.Text.StartsWith("run 1 of 2",StringComparison.Ordinal)&&Title.StartsWith("⚠ REPEATING EVERY 5 S",StringComparison.Ordinal)&&RunMenuGlyph.Text=="\uE895"&&Workspace.Items.OfType<TabItem>().Any(t=>t.Tag is System.Data.DataTable),"A repeat runs at once and shows the caution band, run count, title warning and Stop control");
            UpdateLayout();CaptureSmokeVisual(this,"repeat-banner.png");
            RepeatNowClick(this,new RoutedEventArgs());await UntilRepeat(()=>repeat==null);
            Require(repeat==null&&twice.Runs==2&&RepeatBanner.Visibility==Visibility.Collapsed&&log.Any(m=>m.Text.StartsWith("Repeat ended · Completed 2 runs",StringComparison.Ordinal))&&history.Count==historyBeforeRepeat+1&&Editor.Text==repeatBuffer,"A repeat stops itself after its run limit, records its SQL once in history and leaves the editor untouched");
            var failing=new RepeatPlan("dbcc sqltext(1)",TimeSpan.FromSeconds(5),null,true,engine,"query");StartRepeat(failing);await UntilRepeat(()=>repeat==null);
            Require(repeat==null&&failing.Runs==1&&failing.Failures==1&&log.Any(m=>m.Number==1&&m.Text.Contains("run 1 failed")),"A failed run stops the repeat when Stop on error is set");
            var endless=new RepeatPlan("select 6 as tick",TimeSpan.FromSeconds(5),null,true,engine,"query");StartRepeat(endless);await UntilRepeat(()=>endless.Runs==1&&!repeatBusy);
            RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            Require(repeat==null&&RepeatBanner.Visibility==Visibility.Collapsed&&!Title.StartsWith("⚠",StringComparison.Ordinal),"Esc stops a repeat and removes every warning");
            var orphan=new RepeatPlan("select 7 as tick",TimeSpan.FromSeconds(5),null,true,engine,"query");StartRepeat(orphan);await UntilRepeat(()=>orphan.Runs==1&&!repeatBusy);
            var repeatEngine=engine;engine=new DemoQueryEngine();RepeatTick(activeSession);engine=repeatEngine;
            Require(repeat==null&&orphan.Runs==1,"A repeat stops when the session it started on is replaced");
            string accumulateBuffer=Editor.Text;ClearResults(true);QueryClick(this,new RoutedEventArgs());
            Editor.Text="select 1 as first_run";await ExecuteAsync();Editor.Text="select 2 as second_run";await ExecuteAsync();
            var accumulated=ResultTabs.ToList();
            Require(accumulated.Count==2&&((DataTable)accumulated[0].Tag).Columns[0].Caption=="first_run"&&((DataTable)accumulated[1].Tag).Columns[0].Caption=="second_run"&&Workspace.SelectedItem==accumulated[1],"Results accumulate: a new run adds its tabs after the earlier ones and shows its own");
            RenameResult(accumulated[0],"First kept");Workspace.SelectedItem=accumulated[0];Editor.Text="select 3 as editor_text";
            await RunPressedAsync();var rerun=ResultTabs.ToList();
            Require(rerun.Count==2&&rerun[0].Header.Equals("First kept")&&((DataTable)rerun[0].Tag).Columns[0].Caption=="first_run"&&!ReferenceEquals(rerun[0],accumulated[0])&&Workspace.SelectedItem==rerun[0]&&ReferenceEquals(rerun[1],accumulated[1]),"F9 on a result tab runs that result's SQL again, replaces it in place and keeps its name");
            TogglePin(rerun[0]);Workspace.SelectedItem=rerun[0];await RunPressedAsync();
            Require(ResultTabs.Count()==3&&Workspace.Items.Contains(rerun[0]),"F9 on a pinned result keeps it and adds the new run beside the others");
            TogglePin(rerun[0]);QueryClick(this,new RoutedEventArgs());await RunPressedAsync();
            Require(ResultTabs.Count()==4&&((DataTable)ResultTabs.Last().Tag).Columns[0].Caption=="editor_text","F9 on the Query tab runs the editor and adds a result");
            ClearResults(true);for(int i=0;i<MaxResultTabs+3;i++){var t=new DataTable();t.Columns.Add("n");t.Rows.Add(i);PublishResults([t],NewRun("select "+i,engine),null);}
            Require(ResultTabs.Count()==MaxResultTabs&&((DataTable)ResultTabs.First().Tag).Rows[0][0].Equals("3")&&log.Any(m=>m.Text.Contains("oldest result")),"Accumulated results stay within 40 tabs: the oldest unpinned close and Messages says so");
            ClearResults(true);
            var everyRun=new RepeatPlan("select 9 as tick",TimeSpan.FromSeconds(5),2,true,engine,"query",keepEveryRun:true);StartRepeat(everyRun);await UntilRepeat(()=>everyRun.Runs==1&&!repeatBusy);RepeatNowClick(this,new RoutedEventArgs());await UntilRepeat(()=>repeat==null);
            Require(ResultTabs.Count()==2,"A repeat set to keep every run adds one result per run");
            ClearResults(true);
            var replacing=new RepeatPlan("select 8 as tick",TimeSpan.FromSeconds(5),2,true,engine,"query");StartRepeat(replacing);await UntilRepeat(()=>replacing.Runs==1&&!repeatBusy);RepeatNowClick(this,new RoutedEventArgs());await UntilRepeat(()=>repeat==null);
            Require(ResultTabs.Count()==1,"A default repeat replaces its previous run's result instead of piling up tabs");
            ClearResults(true);Editor.Text=accumulateBuffer;
            string folderFixture=Path.Combine(Path.GetTempPath(),"dbmon-folder-"+Guid.NewGuid().ToString("N"));
            try{
                string legacy=Path.Combine(folderFixture,"ASE ISQL Desktop"),target=Path.Combine(folderFixture,"DBMON_SQL");
                Directory.CreateDirectory(Path.Combine(legacy,"BobertoRuntime"));File.WriteAllText(Path.Combine(legacy,"appearance.json"),"{}");File.WriteAllText(Path.Combine(legacy,"BobertoRuntime","x.txt"),"x");
                string? note=DataFolder.Migrate(target,legacy);
                Require(note!=null&&!Directory.Exists(legacy)&&File.Exists(Path.Combine(target,"appearance.json"))&&File.Exists(Path.Combine(target,"BobertoRuntime","x.txt"))&&DataFolder.Migrate(target,legacy)==null,"The old data folder moves once to DBMON_SQL with its files and Boberto profile");
                Directory.CreateDirectory(legacy);File.WriteAllText(Path.Combine(legacy,"appearance.json"),"{\"old\":1}");
                Require(DataFolder.Migrate(target,legacy)==null&&File.ReadAllText(Path.Combine(target,"appearance.json"))=="{}","An existing DBMON_SQL folder is never overwritten by an old one");
                Require(DataFolder.Root.EndsWith(Path.DirectorySeparatorChar+"DBMON_SQL",StringComparison.Ordinal)&&AppearanceStore.PathName.StartsWith(DataFolder.Root,StringComparison.Ordinal),"Appearance, profiles, recipes and the Boberto profile live under DBMON_SQL");
            }finally{if(Directory.Exists(folderFixture))Directory.Delete(folderFixture,true);}
            // Keyboard focus is a FocusVisualStyle again, which WPF shows for keyboard navigation only; it is inset so
            // it never covers the frame's own border, which is what the dotted default used to do.
            var focusRing=(QueryTab.FocusVisualStyle?.Setters.OfType<Setter>().FirstOrDefault(setter=>setter.Property==Control.TemplateProperty)?.Value as ControlTemplate)?.LoadContent() as Border;
            Require(focusRing is {BorderThickness.Left:>0} && focusRing.Margin.Left>=1 && focusRing.Margin.Left==focusRing.Margin.Top,"Tabs draw keyboard focus with a ring inset inside the frame, never over its border");
            Workspace.SelectedItem=QueryTab;Workspace.UpdateLayout();
            // The plate's shadow lives on its own layer: the header text is never rendered through an effect, which
            // would swap its antialiasing and make the selected tab read heavier than its neighbours.
            Require(QueryTab.Template.FindName("Frame",QueryTab) is Border {Effect:null} && QueryTab.Template.FindName("Lift",QueryTab) is Border {Effect:not null,Visibility:Visibility.Visible} && QueryTab.UseLayoutRounding,"The selected tab lifts on a layer behind its plate and rounds its layout, so the header keeps its antialiasing and the ring keeps all four sides");
            AseOptionsOpened(AseOptionsMenu,new RoutedEventArgs(MenuItem.SubmenuOpenedEvent,AseOptionsMenu));
            Require(AseOptionsMenu.Items.Count>=7 && !AseOptionsMenu.IsEnabled && AseOptionsMenu.Items.OfType<MenuItem>().All(item=>!item.IsEnabled) && AseOptionsMenu.Items.OfType<MenuItem>().Any(item=>item.Header as string=="Statistics IO"),"ASE session options build their menu with Statistics IO and every entry stays disabled without a connected ASE session");
            // SubmenuOpened bubbles from the nested compatibility item: handling it there would rebuild the menu the
            // mouse is on and close the whole dropdown.
            var compatibilityItem=AseOptionsMenu.Items.OfType<MenuItem>().First(item=>item.Items.Count>0);
            AseOptionsOpened(AseOptionsMenu,new RoutedEventArgs(MenuItem.SubmenuOpenedEvent,compatibilityItem));
            Require(ReferenceEquals(AseOptionsMenu.Items.OfType<MenuItem>().First(item=>item.Items.Count>0),compatibilityItem),"Opening the compatibility submenu does not rebuild the menu it hangs from");
            var setBeforeEngine=engine;
            try{
                aseOptions=AseSessionOptions.Default with {NoExec=true,StatisticsIo=true};RefreshAseOptions();
                Require(SetOptionsText.Text=="NOEXEC · STATS IO" && SetOptionsButton.Visibility==Visibility.Collapsed,"The SET chip stays hidden while no ASE session owns the options");
                engine=new FactsSmokeEngine();RefreshConnection();
                Require(SessionFactsPanel.Visibility==Visibility.Visible && SetOptionsButton.Visibility==Visibility.Visible && SetOptionsText.Visibility==Visibility.Visible && SetOptionsText.Text=="NOEXEC · STATS IO","An ASE session shows the SET chip right after the character set and names the options in force");
                aseOptions=AseSessionOptions.Default;RefreshAseOptions();
                Require(SetOptionsButton.Visibility==Visibility.Visible && SetOptionsText.Visibility==Visibility.Collapsed && SetOptionsText.Text.Length==0,"Clearing the ASE session options keeps the SET launcher visible right after the character set while hiding the active-options label");
            }finally{aseOptions=AseSessionOptions.Default;engine=setBeforeEngine;RefreshConnection();}
            var widthTable=new DataTable();widthTable.Columns.Add("state");widthTable.Columns.Add("n");widthTable.Columns.Add("xml");widthTable.Rows.Add("Awaiting Command for the replication DSI thread","1","<q>\n  <line>Subordinate SQL Text: select count(*) from master..sysobjects</line>\n</q>");
            Require(FitColumnWidth(widthTable,0,"state")>FitColumnWidth(widthTable,1,"n")+120&&FitColumnWidth(widthTable,1,"n")<=110&&FitColumnWidth(widthTable,0,"state")<=MaxFittedColumn&&FitColumnWidth(widthTable,2,"xml")>280,"Result columns open wide enough for their first rows' text including multiline payloads, compact for short values and capped for long ones");
            var multilineTab=AddResult(widthTable,99);Workspace.SelectedItem=multilineTab;Workspace.UpdateLayout();
            var multilineGrid=(DataGrid)((DockPanel)multilineTab.Content).Children.OfType<DataGrid>().Single();
            var renderedRow=VisualChildren<DataGridRow>(multilineGrid).FirstOrDefault();
            Require(double.IsNaN(multilineGrid.RowHeight)&&VirtualizingPanel.GetScrollUnit(multilineGrid)==ScrollUnit.Pixel&&renderedRow!=null&&renderedRow.ActualHeight>(double)Application.Current.Resources["GridRowHeight"]*1.5,"Multiline result cells grow their row vertically instead of clipping at one line and scroll by pixel");
            CloseResult(multilineTab);
            string insertBefore=Editor.Text;
            Editor.Text="select 1\nselect 2";Editor.Document.UndoStack.ClearAll();Editor.Select(2,4);Editor.TextArea.Caret.Offset=6;
            InsertRecipeOnNewLine("kill 42");
            Require(Editor.Text=="select 1\nkill 42\nselect 2"&&Editor.SelectedText=="kill 42","A recipe goes on its own line below the caret, never over the selected SQL, and comes back selected");
            Editor.Undo();Require(Editor.Text=="select 1\nselect 2","One Ctrl+Z removes an inserted recipe");
            Editor.Text="select 1\n\nselect 2";Editor.Select(0,0);Editor.TextArea.Caret.Offset=9;InsertRecipeOnNewLine("kill 42");
            Require(Editor.Text=="select 1\nkill 42\nselect 2","A recipe fills the caret's line when that line is blank");
            Editor.Text="";InsertRecipeOnNewLine("kill 42");Require(Editor.Text=="kill 42","A recipe fills an empty query without extra lines");
            Editor.Text="select 1";Editor.Document.UndoStack.ClearAll();ReplaceQueryWithRecipe("admin health");
            Require(Editor.Text=="admin health","Replace query swaps the whole text for the recipe");
            Editor.Undo();Require(Editor.Text=="select 1","One Ctrl+Z restores the query a recipe replaced");
            Require(new LibraryItem("Active locks","exec sp_lock","Diagnostics","",Engine:"ASE").ToString()=="Active locks  ·  ASE"&&new LibraryItem("Mine","select 1","My recipes").ToString()=="Mine","Built-in recipes name their platform, ASE included; My recipes stay untagged");
            Editor.Text=insertBefore;
            Require(FillParameters("select 1",  "fixture","Run")=="select 1","SQL without placeholders runs without a parameter form");
            var platformEngine=engine;
            try{
                engine=new PlatformSmokeEngine(ServerPlatform.Rs);RefreshConnection();
                string rsMenuBefore=Editor.Text;Editor.Text="select * from afiliados";Editor.TextArea.Caret.Offset=16;var rsMenu=(ContextMenu)Editor.TextArea.ContextMenu;BuildEditorMenu(rsMenu);
                Require(DatabaseButton.Visibility==Visibility.Collapsed&&!rsMenu.Items.OfType<MenuItem>().Any(i=>i.Header.ToString()!.StartsWith("Object",StringComparison.Ordinal)),"Replication Server sessions hide the database switch and ASE object actions");
                Editor.Text=rsMenuBefore;
                engine=new PlatformSmokeEngine(ServerPlatform.Iq);RefreshConnection();
                Require(DatabaseButton.Visibility==Visibility.Visible&&!DatabaseButton.IsEnabled&&DatabaseText.Text=="iqdb","SAP IQ sessions show their database without offering an ASE USE switch");
                string iqMenuBefore=Editor.Text;Editor.Text="select * from app.orders";Editor.Select(0,0);Editor.TextArea.Caret.Offset=18;var iqMenu=(ContextMenu)Editor.TextArea.ContextMenu;BuildEditorMenu(iqMenu);var iqItems=iqMenu.Items.OfType<MenuItem>().Select(i=>i.Header.ToString()).ToList();
                Require(iqItems[0]=="Object · app.orders"&&iqItems.Contains("Checking database, owner and object type…")&&!iqItems.Contains("Columns · sp_iqcolumn")&&!iqItems.Contains("Size · sp_iqtablesize")&&!iqItems.Contains("Describe · sp_help"),"IQ editor actions also wait for catalog resolution instead of guessing the object type");
                Editor.Text=iqMenuBefore;
            }finally{engine=platformEngine;RefreshConnection();}
            Exception? libraryFailure=null;
            var libraryInspection=Dispatcher.BeginInvoke(new Action(()=> {
                var library=Application.Current.Windows.OfType<Window>().Single(window=>window.Title.StartsWith("Library ·",StringComparison.Ordinal));
                try {
                    library.UpdateLayout();var recipePreview=VisualChildren<TextBox>(library).Single(box=>box.IsReadOnly);var recipeList=VisualChildren<ListBox>(library).Single();
                    Require(recipeList.Items.Count==96&&recipeList.FontFamily.Equals(AppTypography.Inter)&&recipePreview.FontFamily.Equals(Application.Current.Resources["MonoFont"])&&recipePreview.FontSize==appearance.EditorSize,"Library renders recipe metadata in interface type and SQL in the selected editor type"+$" (recipes {recipeList.Items.Count}, list {recipeList.FontFamily}, preview {recipePreview.FontFamily} at {recipePreview.FontSize}, editor {appearance.EditorSize})");
                    CaptureSmokeVisual(library,"library.png");
                }catch(Exception ex){libraryFailure=ex;}finally{library.Close();}
            }),System.Windows.Threading.DispatcherPriority.ContextIdle);
            LibraryClick(this,new RoutedEventArgs());await libraryInspection;if(libraryFailure!=null)throw libraryFailure;
            string favoriteFixture=Path.GetTempFileName();
            try {
                AtomicFiles.SaveUtf8(favoriteFixture,System.Text.Json.JsonSerializer.Serialize(new[]{new LibraryItem("fixture","select 1","Favorites")}));
                var stored=ReadUserRecipes(favoriteFixture).Single();
                Require(stored.Sql=="select 1"&&stored.Category==UserRecipeCategory, "Recipe storage round trips SQL without credentials and migrates the old Favorites category");
                var imported=ParseRecipeImport("[{\"NAME\":\"fixture\",\"sql\":\"select 2\",\"engine\":\"ASE\",\"id\":\"x\"},{\"name\":\"other\",\"sql\":\"select 3\",\"category\":\"Ops\",\"description\":null}]");
                var merged=MergeRecipes([stored],imported.Append(stored));
                Require(merged.Added==2&&merged.Skipped==1&&merged.Recipes.Select(r=>r.Name).SequenceEqual(["fixture","fixture (2)","other"])&&merged.Recipes[2].Category=="Ops", "Recipe import ignores extra fields, skips identical recipes and renames name conflicts");
                foreach(string bad in new[]{"{}","[1]","[{\"name\":\"x\"}]","[{\"name\":\" \",\"sql\":\"select 1\"}]","[{\"name\":\"x\",\"sql\":5}]","not json"})
                    Require(Throws<System.Text.Json.JsonException>(()=>ParseRecipeImport(bad)), "Recipe import rejects malformed file: "+bad);
            } finally { File.Delete(favoriteFixture); foreach(string backup in Directory.GetFiles(Path.GetDirectoryName(favoriteFixture)!,Path.GetFileName(favoriteFixture)+".*.bak")) File.Delete(backup); }
            ClearResults(true);
            var large = new DataTable();
            large.Columns.Add("id", typeof(int)); large.Columns.Add("description", typeof(string));
            var largeTimer = Stopwatch.StartNew();
            for (int row = 0; row < 100000; row++) large.Rows.Add(row, "Test row " + row);
            AddResult(large, 1); Workspace.SelectedIndex = 1;
            await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
            var grid = ((DockPanel)((TabItem)Workspace.Items[1]).Content).Children.OfType<DataGrid>().Single();
            Require(grid.Items.Count == 100000, "100k source rows available");
            Require(CountVisualRows(grid) is > 0 and < 200, "100k grid creates only visible row controls");
            checks.Add($"PERF 100k rows: {largeTimer.Elapsed.TotalMilliseconds:F1} ms including data creation and first layout; {CountVisualRows(grid)} realized row controls");
            var copyTimer = Stopwatch.StartNew();
            string completeCopy = BuildResultCopy(grid, ResultCopy.AllWithHeaders, ',', delimited: true);
            Require(completeCopy.StartsWith("id,description\r\n") && completeCopy.EndsWith("99999,Test row 99999\r\n") && completeCopy.Count(c => c == '\n') == 100001, "Copy all includes all 100k rows beyond the viewport");
            checks.Add($"PERF copy all 100k rows: {copyTimer.Elapsed.TotalMilliseconds:F1} ms; {completeCopy.Length} characters; serialization only, OS clipboard not measured");
            // Keep routine selection stress within the publication smoke budget; full-view copy above still covers 100k rows.
            large.DefaultView.RowFilter = "id < 10000";
            copyTimer.Restart(); grid.SelectAllCells();
            Require(grid.SelectedCells.Count == 20000, "Select all includes all 10k filtered rows beyond the viewport");
            checks.Add($"PERF select all 10k rows: {copyTimer.Elapsed.TotalMilliseconds:F1} ms; 20000 selected cells including aggregate calculation");
            copyTimer.Restart(); SelectEntireResultRows(grid);
            Require(grid.SelectedCells.Count == 20000, "Expand rows preserves every cell of a fully selected 10k-row result");
            checks.Add($"PERF expand selected rows 10k: {copyTimer.Elapsed.TotalMilliseconds:F1} ms including aggregate calculation");
            grid.UnselectAllCells();
            grid.SetValue(BulkResultSelectionProperty, true);
            try {
                foreach (object row in grid.Items) grid.SelectedCells.Add(new DataGridCellInfo(row, grid.Columns[0]));
            } finally { grid.SetValue(BulkResultSelectionProperty, false); }
            Require(grid.SelectedCells.Count == 10000, "Partial-row fixture selects one visible column of 10k rows");
            copyTimer.Restart(); SelectEntireResultRows(grid);
            Require(grid.SelectedCells.Count == 20000 && StatusText.Text.Contains("Numeric values: 10000"), "Expand 10k partial rows selects every cell and computes aggregates once");
            checks.Add($"PERF expand partial rows 10k: {copyTimer.Elapsed.TotalMilliseconds:F1} ms including aggregate calculation");
            Require(copyTimer.Elapsed < TimeSpan.FromSeconds(5), "Expanding 10k partial rows avoids per-row selection transactions");
            grid.UnselectAllCells(); large.DefaultView.RowFilter = "";
            // Keep initial-render Boberto timing checks ahead of the longer session lifecycle fixtures.
            await VerifyEditorCompletion(Require);
            await VerifyTableCompletion(Require);
            await VerifyEditorObjectResolution(Require);
            await VerifySessionWorkspaces(Require);
            await VerifyProductionGuard(Require);
        }
        catch (Exception ex) { checks.Add("FAIL: " + ex); exitCode = 1; }
        string[] args = Environment.GetCommandLineArgs();
        int reportIndex = Array.IndexOf(args, "--report");
        if (reportIndex >= 0 && reportIndex + 1 < args.Length)
        {
            var process = Process.GetCurrentProcess();
            File.WriteAllText(args[reportIndex + 1], System.Text.Json.JsonSerializer.Serialize(new { exitCode, checks, firstRenderMs, elapsedMs = timer.Elapsed.TotalMilliseconds, cpuMs = process.TotalProcessorTime.TotalMilliseconds, peakWorkingSetBytes = process.PeakWorkingSet64 }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        savedText = Editor.Text;
        Application.Current.Shutdown(exitCode);
    }

    private static int CountVisualRows(DependencyObject parent)
    {
        int count = parent is DataGridRow ? 1 : 0;
        for (int child = 0; child < VisualTreeHelper.GetChildrenCount(parent); child++) count += CountVisualRows(VisualTreeHelper.GetChild(parent, child));
        return count;
    }

    private static void CaptureSmokeVisual(Window window,string name)
    {
        var content=(FrameworkElement)window.Content;content.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){var bounds=new Rect(0,0,content.ActualWidth,content.ActualHeight);drawing.DrawRectangle(window.Background,null,bounds);drawing.DrawRectangle(new VisualBrush(content),null,bounds);}bitmap.Render(visual);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        string directory=Path.Combine(AppContext.BaseDirectory,"UiSmoke");Directory.CreateDirectory(directory);using var stream=File.Create(Path.Combine(directory,name));encoder.Save(stream);
    }

    private sealed class FactsSmokeEngine:IQueryEngine,ISessionFacts
    {
        public string Name=>"Synthetic session fixture";
        public SessionFacts Facts=>new("sales db",1969,"fixture_login","utf8");
        public QueryOutput Execute(string sql,CancellationToken cancellation=default)
        {
            var table=new DataTable();table.Columns.Add("column_0",typeof(int)).Caption="";table.Rows.Add(1969);
            var output=new QueryOutput();output.Tables.Add(table);return output;
        }
    }

    private sealed class PlatformSmokeEngine(ServerPlatform platform):IQueryEngine,ISessionFacts
    {
        public string Name=>platform.Tag()+" · synthetic fixture";
        public SessionFacts Facts=>new(platform==ServerPlatform.Rs?"":"iqdb",null,null,null,platform);
        public QueryOutput Execute(string sql,CancellationToken cancellation=default)=>new() {Error="Synthetic platform fixture."};
    }

    private sealed class DiagnosticSmokeEngine:IQueryEngine
    {
        public const string Raw="[SAP][ASE ODBC Driver][Adaptive Server Enterprise] Fixture server error.";
        public string Name=>"Synthetic diagnostic fixture";
        public QueryOutput Execute(string sql,CancellationToken cancellation=default)
        {
            var output=new QueryOutput {Error="Fixture query failed.",ErrorRawText=Raw,ErrorSource="ODBC"};
            output.Messages.Add(new DiagnosticMessages().Read(208,"42S02",Raw,""));return output;
        }
    }

    // Share of near-black pixels in a WebView2 capture: a renderer that stopped painting returns an almost uniform black frame.
    private static bool Throws<T>(Action action) where T:Exception { try { action(); return false; } catch (T) { return true; } }
    private static double DarkShare(byte[] png)
    {
        using var stream=new MemoryStream(png);
        var frame=new System.Windows.Media.Imaging.FormatConvertedBitmap(System.Windows.Media.Imaging.BitmapFrame.Create(stream,System.Windows.Media.Imaging.BitmapCreateOptions.None,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad),PixelFormats.Bgra32,null,0);
        int stride=frame.PixelWidth*4;var pixels=new byte[stride*frame.PixelHeight];frame.CopyPixels(pixels,stride,0);
        int dark=0;for(int i=0;i<pixels.Length;i+=4)if(pixels[i]<12&&pixels[i+1]<12&&pixels[i+2]<12)dark++;
        return pixels.Length==0?1:dark/(pixels.Length/4.0);
    }
}
