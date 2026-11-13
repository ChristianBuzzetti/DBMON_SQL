using System.Windows;
namespace AseIsql.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
        // Live checks read the real profiles without constructing the shell or touching any user stores. Available in every build.
        // "--icon DIR" regenerates the app icon from BobertoMark; it draws and writes files, and opens no window.
        if (e.Args.Contains("--icon"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var args = e.Args.ToList();
            string dir = args.IndexOf("--icon") is var at && at >= 0 && at + 1 < args.Count ? args[at + 1] : "Assets";
            IconMaker.Run(dir);
            Shutdown(0);
            return;
        }
        if (e.Args.Contains("--live-test"))
        {
            // No StartupUri on this path: MainWindow is never created, and base.OnStartup is not called.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            StartLiveTest(e.Args);
            return;
        }
#if !DBMON_SMOKE
        if (e.Args.Contains("--smoke"))
        {
            Console.WriteLine("smoke checks not included in this build");
            Shutdown(1);
            return;
        }
#endif
        // Before anything reads settings: the data folder moves from "ASE ISQL Desktop" to DBMON_SQL once.
        // Smoke checks use their own fixture paths and never move user data.
#if DBMON_SMOKE
        if (!e.Args.Contains("--smoke"))
#endif
            DataFolder.MigrateDefault();
        DriverPrewarm.Start();
        AppTypography.Apply(new AppearanceSettings());
        Themes.Apply(Themes.WithAccent(Themes.All[0],null));
        // Every window takes its icon from AppIcon: the default mark until the stored logo is read with the settings.
        Resources["AppIcon"]=BobertoMark.Render(64,(System.Windows.Media.Brush)Resources["Accent"]);
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>Themes.ApplyChrome((Window)sender,Themes.Current)));
        // Only the normal path (including Smoke UI checks) opens the main window.
        StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
        base.OnStartup(e);
    }
}
