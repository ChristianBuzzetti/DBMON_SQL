using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AseIsql.Odbc;

namespace AseIsql.Desktop;

// PROD protection and isql-style run settings. A session is PROD when its login profile says so; it then shows a red
// band and PROD tag on its tab and in the status bar, and SQL that changes state (SqlRisk) asks for confirmation before
// it runs from F9, Ctrl+E, Alt+F9, a result re-run, the object actions, Repeat or Run on several servers. Recipes are
// inserted into the editor and run through the same path. Sessions that are not PROD behave exactly as before.
public partial class MainWindow
{
    // Template trigger for the session tab: a PROD session gets an ErrorInk frame and top rail.
    public static readonly DependencyProperty IsProductionProperty = DependencyProperty.RegisterAttached("IsProduction", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static bool GetIsProduction(DependencyObject target) => (bool)target.GetValue(IsProductionProperty);
    public static void SetIsProduction(DependencyObject target, bool value) => target.SetValue(IsProductionProperty, value);

    private RunSettings runSettings = new();
    private string? runSettingsWarning;
    private string RunSettingsPath => isSmoke ? Path.Combine(AppContext.BaseDirectory, "UiSmoke", "run.json") : RunSettingsStore.PathName;
    // Smoke hook: answers the PROD confirmation instead of the dialog and records what it was shown.
    private Func<ProductionPrompt, bool>? productionAnswer;
    // The Run dropdown last opened, for the smoke checks of its entries.
    private ContextMenu? lastRunMenu;

    internal sealed record ProductionPrompt(string Server, string Action, SqlRiskReview Review, string? Note);

    private static bool IsProduction(SessionWorkspace owner) => owner.Engine is not DisconnectedQueryEngine && owner.Profile?.Production == true;

    private void LoadRunSettings()
    {
        try { runSettings = RunSettingsStore.Load(RunSettingsPath); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { runSettings = new(); runSettingsWarning = "Run settings could not be read and start at their defaults: " + ex.Message; }
        ContinueOnErrorMenu.IsChecked = runSettings.ContinueOnError;
    }

    private void SetContinueOnError(bool on)
    {
        runSettings = runSettings with { ContinueOnError = on };
        ContinueOnErrorMenu.IsChecked = on;
        try { RunSettingsStore.Save(RunSettingsPath, runSettings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AddMessage(1, "CLIENT", "Run settings could not be saved: " + ex.Message); }
        StatusText.Text = on ? "Continue after errors · on: a failed batch is recorded and the next one runs" : "Continue after errors · off: the first failed batch stops the script";
    }
    // The Query menu entry is checkable and has already flipped itself; the Run dropdown entry toggles.
    private void ContinueOnErrorMenuClick(object sender, RoutedEventArgs e) => SetContinueOnError(ContinueOnErrorMenu.IsChecked);
    private void NoTimeoutClick(object sender, RoutedEventArgs e) => _ = ExecuteAsync(noTimeout: true);

    // Applied right before a run starts, so every session, including the extra multi-server ones, runs under the
    // setting in force now.
    private void ApplyRunSettings(IQueryEngine target)
    {
        if (target is OdbcSession session) session.ContinueOnError = runSettings.ContinueOnError;
    }

    // Status bar band and tag for the session on screen.
    private void RefreshProductionIndicator()
    {
        bool prod = IsProduction(activeSession);
        ProdStripe.Visibility = ProdBadge.Visibility = prod ? Visibility.Visible : Visibility.Collapsed;
        ProdBadge.ToolTip = prod ? "Production session · " + activeSession.Engine.Name + " · SQL that changes data or server state asks for confirmation" : null;
    }

    // True when the SQL may run: not PROD, nothing that changes state, or the user confirmed.
    private bool ConfirmProduction(SessionWorkspace owner, string sql, string action, string? note = null) =>
        !IsProduction(owner) || ConfirmProduction(owner.Profile is { } profile ? ProfileLabel(profile) + " · " + owner.Engine.Name : owner.Engine.Name, sql, action, note);

    private bool ConfirmProduction(string server, string sql, string action, string? note)
    {
        var review = SqlRisk.Review(sql);
        if (!review.ChangesState) return true;
        var prompt = new ProductionPrompt(server, action, review, note);
        return productionAnswer?.Invoke(prompt) ?? new ProductionConfirmWindow(prompt) { Owner = this }.ShowDialog() == true;
    }

    // The PROD targets of a multi-server run: the current session when its profile is PROD, and every added session
    // whose login profile is.
    private IReadOnlyList<RunTarget> ProductionTargets(IReadOnlyList<RunTarget> chosen) => chosen.Where(target =>
        ReferenceEquals(target.Engine, engine) ? IsProduction(activeSession) : targetProfiles.TryGetValue(target, out var profile) && profile.Production).ToList();

    // Confirmation shown before SQL that changes state runs on a PROD session. Cancel is the default and has focus:
    // Enter or Esc never runs it; only an explicit click on the red button does.
    internal sealed class ProductionConfirmWindow : Window
    {
        public Button RunButton { get; }
        public Button CancelButton { get; }

        public ProductionConfirmWindow(ProductionPrompt prompt)
        {
            SetResourceReference(StyleProperty, typeof(Window)); TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Title = "Confirm on PROD · " + prompt.Server; Width = 680; SizeToContent = SizeToContent.Height; MaxHeight = 720;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            var root = new DockPanel { Margin = new Thickness(20, 18, 20, 16) };
            var band = new Border { BorderThickness = new Thickness(4, 1, 1, 1), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 12) };
            band.SetResourceReference(Border.BorderBrushProperty, "ErrorInk"); band.SetResourceReference(Border.BackgroundProperty, "Surface");
            var head = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold };
            head.SetResourceReference(TextBlock.ForegroundProperty, "ErrorInk"); head.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            head.Inlines.Add(new Run("⚠ PROD · " + prompt.Server));
            var lead = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
                Text = prompt.Action + " changes data or server state on a production server. Review the statements below before running it." };
            lead.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            var bandText = new StackPanel(); bandText.Children.Add(head); bandText.Children.Add(lead); band.Child = bandText;
            DockPanel.SetDock(band, Dock.Top); root.Children.Add(band);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            CancelButton = new Button { Content = "Cancel", IsCancel = true, IsDefault = true, MinWidth = 110 };
            RunButton = new Button { Content = "Run on PROD", MinWidth = 140, Margin = new Thickness(8, 0, 0, 0), FontWeight = FontWeights.SemiBold };
            RunButton.SetResourceReference(BackgroundProperty, "ErrorInk"); RunButton.SetResourceReference(BorderBrushProperty, "ErrorInk"); RunButton.SetResourceReference(ForegroundProperty, "Surface");
            CancelButton.Click += (_, _) => DialogResult = false;
            RunButton.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(CancelButton); buttons.Children.Add(RunButton);
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);

            var details = new System.Text.StringBuilder();
            foreach (var finding in prompt.Review.Findings.Take(20)) details.AppendLine($"line {finding.Line,4} · {finding.Kind,-18} · {finding.Statement}");
            if (prompt.Review.Findings.Count > 20) details.AppendLine($"… and {prompt.Review.Findings.Count - 20} more");
            if (prompt.Review.Repetitions.Count > 0) { details.AppendLine(); foreach (string line in prompt.Review.Repetitions) details.AppendLine("↻ " + line); }
            if (prompt.Note != null) { details.AppendLine(); details.AppendLine(prompt.Note); }
            var list = new TextBox { Text = details.ToString().TrimEnd(), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            list.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
            root.Children.Add(list);
            Content = root;
            Loaded += (_, _) => CancelButton.Focus();
        }
    }
}
