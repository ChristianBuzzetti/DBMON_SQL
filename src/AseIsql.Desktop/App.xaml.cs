using System.Windows;
namespace AseIsql.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
        // Before anything reads settings: the data folder moves from "ASE ISQL Desktop" to DBMON_SQL once.
        // Smoke checks use their own fixture paths and never move user data.
        if (!e.Args.Contains("--smoke")) DataFolder.MigrateDefault();
        DriverPrewarm.Start();
        AppTypography.Apply(new AppearanceSettings());
        Themes.Apply(Themes.WithAccent(Themes.All[0],null));
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>Themes.ApplyChrome((Window)sender,Themes.Current)));
        base.OnStartup(e);
    }
}
