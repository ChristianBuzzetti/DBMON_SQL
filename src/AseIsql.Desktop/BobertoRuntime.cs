using System.Globalization;
using System.IO;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace AseIsql.Desktop;

// One WebView2 environment per profile: the console dock and Studio share the browser process and the
// https://boberto.desktop.invalid origin, so the original engine's smg_* localStorage keys are one look.
public static class BobertoRuntime
{
    public const string Host="boberto.desktop.invalid";
    public const string Origin="https://"+Host+"/";
    private static readonly Dictionary<string,Task<CoreWebView2Environment>> environments=new(StringComparer.OrdinalIgnoreCase);
    public static string DefaultProfile=>Path.Combine(Path.GetDirectoryName(AppearanceStore.PathName)!,"BobertoRuntime");
    public static Task<CoreWebView2Environment> EnvironmentAsync(string profilePath)
    {
        string key=Path.GetFullPath(profilePath);
        lock(environments){
            if(environments.TryGetValue(key,out var existing)&&!existing.IsFaulted&&!existing.IsCanceled)return existing;
            var created=CoreWebView2Environment.CreateAsync(userDataFolder:key,options:new CoreWebView2EnvironmentOptions(language:"en-US"));
            environments[key]=created;
            // When the last WebView of the profile closes, the browser process exits; the next companion must start a
            // fresh environment instead of attaching to the one that is gone.
            _=created.ContinueWith(task=>task.Result.BrowserProcessExited+=(_,_)=> {lock(environments){if(environments.TryGetValue(key,out var current)&&current==created)environments.Remove(key);}},CancellationToken.None,TaskContinuationOptions.OnlyOnRanToCompletion,TaskScheduler.FromCurrentSynchronizationContext());
            return created;
        }
    }
    // Starts the browser process while the window builds, so the dock created after the first render attaches at once.
    public static void Prewarm(string profilePath)=>_=EnvironmentAsync(profilePath).ContinueWith(task=>_=task.Exception,TaskContinuationOptions.OnlyOnFaulted);
    public static bool LocalUri(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host==Host&&uri.IsDefaultPort;
    // Packaged files only: navigation, popups, permissions and non-local requests are refused.
    public static void Harden(CoreWebView2 core,CoreWebView2Environment environment,Action blocked)
    {
        core.Settings.AreDevToolsEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreBrowserAcceleratorKeysEnabled=false;core.Settings.IsStatusBarEnabled=false;core.Settings.IsZoomControlEnabled=false;
        core.SetVirtualHostNameToFolderMapping(Host,Path.Combine(AppContext.BaseDirectory,"Assets","Boberto"),CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting+=(_,args)=> {if(!LocalUri(args.Uri))args.Cancel=true;};
        core.NewWindowRequested+=(_,args)=>args.Handled=true;
        core.PermissionRequested+=(_,args)=>args.State=CoreWebView2PermissionState.Deny;
        core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
        core.WebResourceRequested+=(_,args)=> {if(!LocalUri(args.Request.Uri)&&!args.Request.Uri.StartsWith("data:",StringComparison.Ordinal)){blocked();args.Response=environment.CreateWebResourceResponse(null,403,"Local content only","");}};
    }
    private static string Css(string name){var c=(Color)ColorConverter.ConvertFromString(Themes.Current.Colors[name]);return $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";}
    public static Dictionary<string,string> ThemeTokens(AppearanceSettings settings)
    {
        var tokens=new Dictionary<string,string>{{"--bg",Css("Canvas")},{"--bg-elev-0",Css("Header")},{"--bg-elev-1",Css("Surface")},{"--bg-elev-2",Css("SurfaceRaised")},{"--bg-hover",Css("AlternateRow")},{"--text",Css("Ink")},{"--text-muted",Css("Muted")},{"--text-dim",Css("LineNumber")},{"--border",Css("Rule")},{"--border-strong",Css("Rule")},{"--border-subtle",Css("RowRule")},{"--accent",Css("Accent")}};
        tokens["--ui-size"]=settings.UiSize.ToString(CultureInfo.InvariantCulture)+"px";
        tokens["--smg-text"]=(settings.UiSize/13).ToString(CultureInfo.InvariantCulture);
        tokens["--font-ui"]=settings.UiFont=="inter"?"Inter, Segoe UI, sans-serif":"Segoe UI, sans-serif";
        return tokens;
    }
    // DBMON names its light/dark palettes alba/midnight; the other catalog ids are shared.
    public static string ThemeName(AppearanceSettings settings)=>settings.ThemeId=="light"?"alba":settings.ThemeId=="dark"?"midnight":settings.ThemeId;
    public static System.Drawing.Color SurfaceColor(){var c=(Color)ColorConverter.ConvertFromString(Themes.Current.Colors["Surface"]);return System.Drawing.Color.FromArgb(255,c.R,c.G,c.B);}
    // The engine's ceiling (DOCK_SC_TOPE). The band height decides the scale that fits; the legacy BobertoSize
    // preference no longer has a control and is ignored.
    public static double DockScale(AppearanceSettings settings)=>6;
    public static string MediaFeatures(bool motion)=>System.Text.Json.JsonSerializer.Serialize(new{features=new[]{new{name="prefers-reduced-motion",value=motion?"no-preference":"reduce"}}});
}
