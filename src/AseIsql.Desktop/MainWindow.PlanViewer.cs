using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AseIsql.Odbc;

namespace AseIsql.Desktop;

// SAP ASE plan viewer: the full operator tree of a captured plan (Statistics IO tabs, estimated plans), estimate-vs-
// actual highlights from PlanTree and a two-plan diff from PlanCompare. Highlights use palette keys only: a warning is
// the Notice/NoticeInk pair, an error ErrorInk, so every light and dark theme keeps its contrast.
public partial class MainWindow
{
    public sealed record CapturedPlan(string Label, ExecutionIoPlan Plan, bool Estimated) { public override string ToString() => Label; }

    // Plans seen in this window, oldest first, bounded. Statistics IO tabs join the list when a viewer or compare
    // dialog opens, so closed tabs stay comparable until they age out.
    private readonly List<CapturedPlan> capturedPlans = [];
    private const int MaxCapturedPlans = 32;

    private CapturedPlan Capture(string source, ExecutionIoPlan plan, bool? estimated = null)
    {
        if (capturedPlans.FirstOrDefault(c => ReferenceEquals(c.Plan, plan)) is { } known) return known;
        bool isEstimate = estimated ?? PlanTree.Flatten(plan.Tree).All(n => n.Actual.IsEmpty);
        string text = plan.Text.Length > 60 ? plan.Text[..60] + "…" : plan.Text;
        var captured = new CapturedPlan(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " · " + (isEstimate ? "estimated" : "executed") + " · " + source
            + " · stmt " + plan.Statement.ToString(CultureInfo.InvariantCulture) + " · " + text, plan, isEstimate);
        capturedPlans.Add(captured);
        if (capturedPlans.Count > MaxCapturedPlans) capturedPlans.RemoveAt(0);
        return captured;
    }

    private void CaptureOpenTabs()
    {
        foreach (var session in sessions)
            foreach (var tab in session.Tabs)
                foreach (var plan in ExecutionIo.PlansOf(tab.Tag as DataTable)) Capture(ResultName(tab), plan);
    }

    // Statistics IO tabs: a button beside the grid tools and the same entries in the grid's context menu.
    private void AddPlanResultTools(TabItem tab, DockPanel panel, ContextMenu menu)
    {
        if (ExecutionIo.PlansOf(tab.Tag as DataTable).Count == 0) return;
        void Open() => OpenPlanViewer(ExecutionIo.PlansOf(tab.Tag as DataTable).Select(p => Capture(ResultName(tab), p)).ToList());
        var button = new Button { Content = "Plan tree", Padding = new Thickness(8,4,8,4), ToolTip = "Open the full operator tree of these plans, with estimate-vs-actual highlights" };
        button.Click += (_, _) => Open(); panel.Children.Add(button);
        var view = new MenuItem { Header = "Plan viewer…" }; view.Click += (_, _) => Open(); menu.Items.Add(view);
        var compare = new MenuItem { Header = "Compare plans…" };
        compare.Click += (_, _) => OpenPlanCompare(ExecutionIo.PlansOf(tab.Tag as DataTable).Select(p => Capture(ResultName(tab), p)).LastOrDefault());
        menu.Items.Add(compare);
    }

    private void PlanViewerClick(object sender, RoutedEventArgs e)
    {
        CaptureOpenTabs();
        if (Workspace.SelectedItem is TabItem { Tag: DataTable table } tab && ExecutionIo.PlansOf(table) is { Count: > 0 } plans)
            OpenPlanViewer(plans.Select(p => Capture(ResultName(tab), p)).ToList());
        else OpenPlanViewer(capturedPlans.ToList());
    }
    private void ComparePlansClick(object sender, RoutedEventArgs e) => OpenPlanCompare();
    private void EstimatedPlanClick(object sender, RoutedEventArgs e) => EstimatePlan();

    // The editor selection, or the whole script, compiled under noexec: see EstimatedPlan for the exact commands.
    private void EstimatePlan()
    {
        var owner = activeSession;
        if (owner.Engine is not OdbcSession { IsOpen: true, Platform: ServerPlatform.Ase }) { SetStatus(owner, "Estimated plans need a connected SAP ASE session."); return; }
        bool selection = Editor.SelectionLength > 0;
        string sql = selection ? Editor.SelectedText : Editor.Text;
        if (EstimatedPlan.Refusal(sql) is { } refusal) { SetStatus(owner, refusal); if (!string.IsNullOrWhiteSpace(sql)) ShowError(refusal); return; }
        if (FillParameters(sql, selection ? "Selection" : "Query", "Estimated plan") is not { } filled) { SetStatus(owner, "Cancelled · estimated plan parameters"); return; }
        if (EstimatedPlan.Refusal(filled) is { } filledRefusal) { ShowError(filledRefusal); return; }
        RunCatalogAction(target => EstimatedPlanAsync(target, filled, selection ? "selection" : "script"), "Estimated plan");
    }

    private async Task EstimatedPlanAsync(SessionWorkspace owner, string sql, string scope)
    {
        if (owner.Running != null || owner.Changing) { SetStatus(owner, "Wait for this session's current operation to finish, then request the estimated plan."); return; }
        if (owner.Engine is not OdbcSession { IsOpen: true, Platform: ServerPlatform.Ase } session) { SetStatus(owner, "Estimated plans need a connected SAP ASE session."); return; }
        var options = owner.Options;
        using var cancellation = new CancellationTokenSource(); owner.Running = cancellation; RefreshFor(owner);
        ClearMessages(owner);
        AddMessage(owner, 0, "CLIENT", "Estimated plan · " + scope + " · compiled under set noexec on, nothing executed · " + string.Join(" · ", EstimatedPlan.Prepare(options)));
        SetStatus(owner, "Estimated plan · compiling · Esc cancels");
        try
        {
            var run = await Task.Run(() => {
                var outputs = new List<QueryOutput>(); string xml = ""; bool restored = false, compiled = false;
                QueryOutput Send(string text, CancellationToken token) { var output = session.Execute(text, token, userRun: false); outputs.Add(output); return output; }
                try
                {
                    if (Send(EstimatedPlan.Batches(EstimatedPlan.Prepare(options)), cancellation.Token).Error == null) { compiled = true; Send(sql, cancellation.Token); }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    // Cleanup always runs, cancelled or not: the session must not be left in noexec with the plan option on.
                    try
                    {
                        if (Send(EstimatedPlan.Lift, CancellationToken.None).Error == null && compiled)
                        {
                            xml = EstimatedPlan.Xml(Send(EstimatedPlan.Read(0), CancellationToken.None).Tables);
                            if (xml.Length == 0) xml = EstimatedPlan.Xml(Send(EstimatedPlan.Read(-1), CancellationToken.None).Tables);
                        }
                        restored = Send(EstimatedPlan.Batches(EstimatedPlan.Restore(options)), CancellationToken.None).Error == null;
                    }
                    catch (OperationCanceledException) { }
                }
                return (Outputs: outputs, Xml: xml, Restored: restored);
            });
            foreach (var output in run.Outputs)
            {
                AddOutputMessages(owner, output);
                if (output.Error != null) AddMessage(owner, 1, output.ErrorSource, output.Error, output.ErrorRawText);
            }
            if (!run.Restored)
            {
                AddMessage(owner, 1, "CLIENT", "Estimated plan: the session options could not be restored. The session may still have NOEXEC or show_final_plan_xml on; check Tools › SAP ASE session options or reconnect.");
                ShowMessages(owner);
            }
            var plans = EstimatedPlan.Plans(run.Xml);
            if (plans.Count == 0)
            {
                AddMessage(owner, 1, "CLIENT", "Estimated plan: no plan returned" + (run.Xml.Length == 0 ? " (empty showplan_in_xml buffer)." : $" · {run.Xml.Length} chars could not be read as a user plan."), run.Xml.Length == 0 ? null : run.Xml);
                ShowMessages(owner); SetStatus(owner, cancellation.IsCancellationRequested ? "Estimated plan cancelled" : "Estimated plan · no plan returned · see Messages");
                return;
            }
            AddMessage(owner, 0, "CLIENT", "Estimated plan · " + Counts.Of(plans.Count, "statement") + " · nothing was executed", run.Xml);
            SetStatus(owner, "Estimated plan · " + Counts.Of(plans.Count, "statement"));
            OpenPlanViewer(plans.Select(p => Capture(SourceLabelOf(owner), p, estimated: true)).ToList());
        }
        finally { owner.Running = null; RefreshFor(owner); }
    }

    private static string PlanCount(long? value) => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "–";

    // "est → act" per metric when the plan has actual counts, estimates alone otherwise.
    private static string PlanMetricsText(PlanNode node)
    {
        PlanMetrics est = node.Estimated, act = node.Actual;
        if (act.IsEmpty) return est.IsEmpty ? "" : $"est rows {PlanCount(est.Rows)} · lio {PlanCount(est.LogicalReads)} · pio {PlanCount(est.PhysicalReads)}";
        return $"rows {PlanCount(est.Rows)} → {PlanCount(act.Rows)} · lio {PlanCount(est.LogicalReads)} → {PlanCount(act.LogicalReads)} · pio {PlanCount(est.PhysicalReads)} → {PlanCount(act.PhysicalReads)}";
    }

    private static Border SeverityChip(PlanSeverity severity)
    {
        var text = new TextBlock { Text = severity == PlanSeverity.Error ? "✖ est/act" : "⚠ est/act", FontWeight = FontWeights.SemiBold };
        var chip = new Border { Child = text, CornerRadius = new CornerRadius(3), Padding = new Thickness(5,0,5,0), Margin = new Thickness(0,0,6,0), VerticalAlignment = VerticalAlignment.Center };
        if (severity == PlanSeverity.Warning) { chip.SetResourceReference(Border.BackgroundProperty, "Notice"); text.SetResourceReference(TextBlock.ForegroundProperty, "NoticeInk"); }
        else { chip.BorderThickness = new Thickness(1); chip.SetResourceReference(Border.BorderBrushProperty, "ErrorInk"); text.SetResourceReference(TextBlock.ForegroundProperty, "ErrorInk"); }
        return chip;
    }

    private static TreeViewItem PlanTreeItem(PlanNode node)
    {
        var severity = PlanTree.Severity(node);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (severity != PlanSeverity.None) line.Children.Add(SeverityChip(severity));
        line.Children.Add(new TextBlock { Text = node.Operator, FontWeight = FontWeights.SemiBold });
        if (node.Object != null)
        {
            var target = new TextBlock { Text = "  " + node.Object + (node.Alias == null ? "" : " " + node.Alias) + (node.Index == null ? "" : " · " + node.Index) + (node.ScanType == null ? "" : " · " + node.ScanType) };
            target.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); line.Children.Add(target);
        }
        var metrics = new TextBlock { Text = "   " + PlanMetricsText(node) };
        metrics.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        metrics.SetResourceReference(TextBlock.ForegroundProperty, severity == PlanSeverity.Error ? "ErrorInk" : "Muted");
        line.Children.Add(metrics);
        var issues = PlanTree.Assess(node);
        if (issues.Count > 0) line.ToolTip = string.Join(Environment.NewLine, issues.Select(i => i.Message));
        var item = new TreeViewItem { Header = line, IsExpanded = true, Tag = node };
        foreach (var child in node.Children) item.Items.Add(PlanTreeItem(child));
        return item;
    }

    private static string PlanNodeDetails(PlanNode node)
    {
        var lines = new List<string> { node.Label, "Path: " + node.Key };
        if (node.ScanType != null) lines.Add("Scan type: " + node.ScanType);
        lines.Add($"Estimated: rows {PlanCount(node.Estimated.Rows)} · logical reads {PlanCount(node.Estimated.LogicalReads)} · physical reads {PlanCount(node.Estimated.PhysicalReads)}");
        lines.Add(node.Actual.IsEmpty ? "Actual: not in this plan (estimated)" : $"Actual: rows {PlanCount(node.Actual.Rows)} · logical reads {PlanCount(node.Actual.LogicalReads)} · physical reads {PlanCount(node.Actual.PhysicalReads)}");
        lines.AddRange(PlanTree.Assess(node).Select(i => (i.Severity == PlanSeverity.Error ? "Error · " : "Warning · ") + i.Message));
        return string.Join(Environment.NewLine, lines);
    }

    private static T Docked<T>(DockPanel panel, T element, Dock dock) where T : UIElement { DockPanel.SetDock(element, dock); panel.Children.Add(element); return element; }

    private void OpenPlanViewer(IReadOnlyList<CapturedPlan> plans)
    {
        if (plans.Count == 0) { ShowError("No plan captured yet. Turn on Tools › SAP ASE session options › Statistics IO and run a query, or use Tools › Estimated plan (no exec)."); return; }
        var window = new Window { Owner = this, Title = "Plan viewer", Width = 1050, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(16) };
        var picker = Docked(root, new ComboBox { ItemsSource = plans, SelectedIndex = plans.Count - 1, Margin = new Thickness(0,0,0,8), Visibility = plans.Count > 1 ? Visibility.Visible : Visibility.Collapsed }, Dock.Top);
        var summary = Docked(root, new TextBlock { Margin = new Thickness(0,0,0,6), TextWrapping = TextWrapping.Wrap }, Dock.Top);
        var sqlText = Docked(root, new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 84, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0,0,0,6) }, Dock.Top);
        sqlText.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        var missing = Docked(root, new TextBlock { Margin = new Thickness(0,0,0,6), TextWrapping = TextWrapping.Wrap }, Dock.Top);
        missing.SetResourceReference(TextBlock.ForegroundProperty, "NoticeInk");
        var legend = Docked(root, new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,8) }, Dock.Top);
        legend.Children.Add(SeverityChip(PlanSeverity.Warning));
        legend.Children.Add(new TextBlock { Text = string.Create(CultureInfo.InvariantCulture, $"actual/estimate ≥ {PlanTree.WarningRatio:0}× or ≤ 1/{PlanTree.WarningRatio:0}     "), VerticalAlignment = VerticalAlignment.Center });
        legend.Children.Add(SeverityChip(PlanSeverity.Error));
        legend.Children.Add(new TextBlock { Text = string.Create(CultureInfo.InvariantCulture, $"≥ {PlanTree.ErrorRatio:0}× or ≤ 1/{PlanTree.ErrorRatio:0}, or estimate 0 with actual > 0     metrics read est → act"), VerticalAlignment = VerticalAlignment.Center });
        var buttons = Docked(root, new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,8,0,0) }, Dock.Bottom);
        var detail = Docked(root, new TextBox { IsReadOnly = true, Height = 112, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0,8,0,0) }, Dock.Bottom);
        detail.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        var tree = new TreeView(); root.Children.Add(tree);
        CapturedPlan? Current() => picker.SelectedItem as CapturedPlan;
        void AddButton(string text, string tip, Action click) { var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(10,4,10,4), Margin = new Thickness(0,0,8,0) }; b.Click += (_, _) => click(); buttons.Children.Add(b); }
        AddButton("Compare with…", "Diff this plan against another captured plan", () => OpenPlanCompare(Current()));
        AddButton("View XML", "The plan document as ASE returned it", () => { if (Current() is { } c) TextWindow("Plan XML · " + c.Label, c.Plan.Xml); });
        AddButton("Copy XML", "Copy the plan document to the clipboard", () => {
            if (Current() is not { } c) return;
            try { Clipboard.SetText(c.Plan.Xml); StatusText.Text = "Copied plan XML"; }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException) { ShowError("Unable to copy: " + ex.Message); }
        });
        void Render()
        {
            tree.Items.Clear(); detail.Text = "";
            if (Current() is not { } captured) return;
            var plan = captured.Plan; var nodes = PlanTree.Flatten(plan.Tree).ToList();
            int warnings = nodes.Count(n => PlanTree.Severity(n) == PlanSeverity.Warning), errors = nodes.Count(n => PlanTree.Severity(n) == PlanSeverity.Error);
            window.Title = "Plan viewer · " + (captured.Estimated ? "estimated" : "executed") + " · statement " + plan.Statement.ToString(CultureInfo.InvariantCulture);
            summary.Text = captured.Label + Environment.NewLine + Counts.Of(nodes.Count, "operator") + " · line " + plan.Line.ToString(CultureInfo.InvariantCulture)
                + (captured.Estimated ? " · estimated plan: no actual counts, nothing is flagged" : " · " + Counts.Of(warnings, "warning") + " · " + Counts.Of(errors, "error"));
            sqlText.Text = plan.Text;
            missing.Text = plan.MissingStatistics.Count == 0 ? "" : "Missing statistics · " + string.Join(", ", plan.MissingStatistics);
            missing.Visibility = missing.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var node in plan.Tree) tree.Items.Add(PlanTreeItem(node));
            if (nodes.Count == 0) tree.Items.Add(new TreeViewItem { Header = "This plan has no operator tree." });
        }
        tree.SelectedItemChanged += (_, _) => detail.Text = tree.SelectedItem is TreeViewItem { Tag: PlanNode node } ? PlanNodeDetails(node) : "";
        picker.SelectionChanged += (_, _) => Render();
        window.Content = root; Render(); window.Show();
    }

    private void OpenPlanCompare(CapturedPlan? first = null)
    {
        CaptureOpenTabs();
        var plans = capturedPlans.ToList();
        if (plans.Count < 2) { ShowError("Plan compare needs two captured plans: run with Statistics IO on, or request estimated plans, at least twice."); return; }
        var a = first != null && plans.Contains(first) ? first : plans[^2];
        var b = ReferenceEquals(a, plans[^1]) ? plans[^2] : plans[^1];
        var window = new Window { Owner = this, Title = "Compare plans", Width = 1150, Height = 700, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(16) };
        var pickers = Docked(root, new Grid { Margin = new Thickness(0,0,0,8) }, Dock.Top);
        pickers.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); pickers.ColumnDefinitions.Add(new ColumnDefinition());
        pickers.RowDefinitions.Add(new RowDefinition()); pickers.RowDefinitions.Add(new RowDefinition());
        ComboBox Picker(string label, int row, CapturedPlan selected)
        {
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,4), FontWeight = FontWeights.SemiBold };
            var combo = new ComboBox { ItemsSource = plans, SelectedItem = selected, Margin = new Thickness(0,0,0,4) };
            Grid.SetRow(text, row); Grid.SetRow(combo, row); Grid.SetColumn(combo, 1); pickers.Children.Add(text); pickers.Children.Add(combo);
            return combo;
        }
        var left = Picker("A", 0, a); var right = Picker("B", 1, b);
        var summary = Docked(root, new TextBlock { Margin = new Thickness(0,0,0,8), TextWrapping = TextWrapping.Wrap }, Dock.Top);
        var buttons = Docked(root, new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,8,0,0) }, Dock.Bottom);
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
        grid.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        root.Children.Add(grid);
        // Only-in rows carry the accent, changed rows the notice pair; equal rows keep the grid's own colors.
        grid.LoadingRow += (_, e) => {
            e.Row.ClearValue(Control.BackgroundProperty); e.Row.ClearValue(Control.ForegroundProperty);
            switch ((e.Row.Item as DataRowView)?["status"] as string)
            {
                case "only in A" or "only in B": e.Row.SetResourceReference(Control.ForegroundProperty, "Accent"); break;
                case "different": e.Row.SetResourceReference(Control.BackgroundProperty, "Notice"); e.Row.SetResourceReference(Control.ForegroundProperty, "NoticeInk"); break;
            }
        };
        IReadOnlyList<PlanDiffEntry> entries = [];
        void Render()
        {
            if (left.SelectedItem is not CapturedPlan pa || right.SelectedItem is not CapturedPlan pb) return;
            entries = PlanCompare.Compare(pa.Plan, pb.Plan);
            grid.ItemsSource = PlanCompare.ToTable(entries).DefaultView;
            string Kinds(PlanDiffKind kind) => entries.Count(x => x.Kind == kind).ToString(CultureInfo.InvariantCulture);
            summary.Text = (ReferenceEquals(pa, pb) ? "A and B are the same plan · " : "")
                + Counts.Of(entries.Count, "operator") + " · " + Kinds(PlanDiffKind.Same) + " same · " + Kinds(PlanDiffKind.Different) + " different · "
                + Kinds(PlanDiffKind.OnlyA) + " only in A · " + Kinds(PlanDiffKind.OnlyB) + " only in B"
                + (pa.Estimated != pb.Estimated ? " · one plan is estimated, so its act_* columns stay blank" : "");
        }
        void AddButton(string text, string tip, Action click) { var x = new Button { Content = text, ToolTip = tip, Padding = new Thickness(10,4,10,4), Margin = new Thickness(0,0,8,0) }; x.Click += (_, _) => click(); buttons.Children.Add(x); }
        AddButton("Swap A/B", "Exchange the two plans", () => { var held = left.SelectedItem; left.SelectedItem = right.SelectedItem; right.SelectedItem = held; });
        AddButton("Open as result tab", "Copy this diff to a result tab: sort, filter and export it like any result", () => {
            if (left.SelectedItem is not CapturedPlan pa || right.SelectedItem is not CapturedPlan pb) return;
            AddResult(PlanCompare.ToTable(entries), 0, source: "Plan compare", sql: "-- A: " + pa.Label + Environment.NewLine + "-- B: " + pb.Label, header: PlanCompare.ResultHeader);
            Workspace.SelectedIndex = Workspace.Items.Count - 1;
        });
        AddButton("View A", "Open plan A in the plan viewer", () => { if (left.SelectedItem is CapturedPlan pa) OpenPlanViewer([pa]); });
        AddButton("View B", "Open plan B in the plan viewer", () => { if (right.SelectedItem is CapturedPlan pb) OpenPlanViewer([pb]); });
        left.SelectionChanged += (_, _) => Render(); right.SelectionChanged += (_, _) => Render();
        window.Content = root; Render(); window.Show();
    }
}
