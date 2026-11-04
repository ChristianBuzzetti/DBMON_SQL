using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // PROD indicator and confirmation, the Run dropdown's isql-style entries, and saved profiles with a missing CA.
    // Synthetic engines and isolated files only: nothing authenticates or touches the user's profiles.
    private async Task VerifyProductionGuard(Action<bool, string> require)
    {
        var original = activeSession;
        var prod = AddSession()!;
        var prodEngine = new WorkspaceSmokeEngine("prod-a", ServerPlatform.Ase);
        var prodProfile = new ConnectionProfile("prod-host", 5000, "master", "fixture", false, "", ServerAlias: "PRODA", Production: true);
        engine = prodEngine; lastProfile = prodProfile; RefreshConnection();
        require(ProdBadge.Visibility == Visibility.Visible && ProdStripe.Visibility == Visibility.Visible && prod.ProdBadge.Visibility == Visibility.Visible && GetIsProduction(prod.Header),
            "A PROD session shows the PROD tag and red band on its session tab and in the status bar");
        require(ReferenceEquals(ProdBadge.Background, Application.Current.Resources["ErrorInk"]) && ReferenceEquals(ProdStripe.Background, Application.Current.Resources["ErrorInk"]),
            "The PROD indicator uses the theme's ErrorInk, so every palette colors it");
        var prompts = new List<ProductionPrompt>();
        productionAnswer = prompt => { prompts.Add(prompt); return false; };
        try
        {
            Editor.Text = "select 1 as reading"; savedText = Editor.Text;
            await ExecuteAsync();
            require(prompts.Count == 0 && prodEngine.Executions == 1, "Read-only SQL on a PROD session runs without asking");
            Editor.Text = "delete from fixture_t where id = 1\ngo 3"; savedText = Editor.Text;
            await ExecuteAsync();
            require(prompts.Count == 1 && prodEngine.Executions == 1 && prompts[0].Server.Contains("PRODA") && prompts[0].Review.Findings.Single().Kind == "delete" &&
                prompts[0].Review.Repetitions.Single().Contains("runs 3 times") && log.Any(m => m.Text.StartsWith("Not run: cancelled at the PROD confirmation", StringComparison.Ordinal)),
                "State-changing SQL on PROD asks first with the server, the statements and the go N repetitions; Cancel sends nothing");
            productionAnswer = prompt => { prompts.Add(prompt); return true; };
            Editor.Select(0, 0); Editor.CaretOffset = 0;
            await ExecuteAsync(lineOnly: true);
            require(prompts.Count == 2 && prodEngine.Executions == 2 && prodEngine.LastSql == "delete from fixture_t where id = 1", "Alt+F9 on PROD asks too, and a confirmed run executes once");
            require(ProductionTargets([new RunTarget("PRODA", engine)]).Count == 1, "A multi-server run counts the current PROD session among its PROD targets");
            lastProfile = prodProfile with { Production = false }; RefreshConnection();
            Editor.Text = "delete from fixture_t"; savedText = Editor.Text;
            await ExecuteAsync();
            require(prompts.Count == 2 && prodEngine.Executions == 3 && ProdBadge.Visibility == Visibility.Collapsed && prod.ProdBadge.Visibility == Visibility.Collapsed && !GetIsProduction(prod.Header),
                "Outside PROD nothing asks and no PROD indicator is shown");
        }
        finally { productionAnswer = null; }
        var dialog = new ProductionConfirmWindow(new ProductionPrompt("PRODA", "This script", SqlRisk.Review("kill 42\ngo\ndbcc checkdb(sales)"), null));
        require(dialog.CancelButton.IsDefault && dialog.CancelButton.IsCancel && !dialog.RunButton.IsDefault && dialog.Title.Contains("PRODA"),
            "The PROD confirmation defaults to Cancel: Enter and Esc never run the SQL");
        dialog.Close();

        RunMenuClick(this, new RoutedEventArgs());
        var items = lastRunMenu!.Items.OfType<MenuItem>().ToList(); lastRunMenu.IsOpen = false;
        require(items.Any(i => Equals(i.Header, "Run without timeout (maintenance)") && i.IsEnabled) && items.Single(i => Equals(i.Header, "Continue after errors")).IsCheckable,
            "The Run dropdown offers Run without timeout and a checkable Continue after errors");
        bool before = runSettings.ContinueOnError;
        SetContinueOnError(!before);
        require(RunSettingsStore.Load(RunSettingsPath).ContinueOnError == !before && ContinueOnErrorMenu.IsChecked == !before, "Continue after errors is saved and reflected in the Query menu");
        SetContinueOnError(before);

        string store = Path.Combine(AppContext.BaseDirectory, "UiSmoke", "profiles-missing-ca.dat"), ca = Path.GetTempFileName();
        ProfileStore.OverridePath = store;
        try
        {
            ProfileStore.Save(store, [new SavedLogin("good", new ConnectionProfile("good-host", 5000, "master", "fixture", false, "")),
                new SavedLogin("tls", new ConnectionProfile("tls-host", 5000, "master", "fixture", true, ca, Production: true))]);
            File.Delete(ca);
            var loaded = ProfileStore.Load(store);
            require(loaded.Count == 2 && loaded[0].Problem == null && loaded[1].Problem!.Contains(ca), "A profile whose CA file was deleted no longer prevents the others from loading");
            var window = new ConnectionWindow();
            try
            {
                require(window.SavedProfiles.Items.Count == 2 && window.ProfileProblems.Single().StartsWith("Profile tls: ", StringComparison.Ordinal) && window.Feedback.Text.Contains("Profile tls: "),
                    "The login window lists every profile and names the one whose CA certificate is missing");
            }
            finally { window.Close(); }
            ProfileStore.Save(store, ProfileStore.Upsert(loaded, loaded[0] with { Password = null }));
            require(ProfileStore.Load(store).Count == 2 && ProfileStore.Load(store)[1].Profile.Production, "Saving the store keeps the profile with the missing file, PROD flag included");
        }
        finally { ProfileStore.OverridePath = null; foreach (string file in Directory.GetFiles(Path.GetDirectoryName(store)!, "profiles-missing-ca.dat*")) File.Delete(file); File.Delete(ca); }

        savedText = Editor.Text;
        await CloseSessionAsync(prod);
        SelectSession(original);
    }
}
