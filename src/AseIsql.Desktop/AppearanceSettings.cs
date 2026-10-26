using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace AseIsql.Desktop;

public sealed record AppearanceSettings(string ThemeId="pergamino",string? AccentHex=null,string UiFont="inter",string EditorFont="consolas",double UiSize=13,double EditorSize=15,bool Compact=false,bool Rounded=false,bool ShowMessages=true,bool BobertoVisible=true,double BobertoSize=84,bool Motion=false,string BobertoBackground="theme",bool BobertoMotion=true,bool SqlCompletion=true)
{
    // SqlCompletion is retained only to read older files under strict JSON validation. Completion is always available.
    // Motion, BobertoBackground and BobertoSize predate the live dock (the band height now decides Boberto's scale): Studio's engine now owns the scene background and BobertoMotion
    // owns animation. They stay readable so earlier files load under UnmappedMemberHandling.Disallow; nothing consumes them.
    private static readonly string[] LegacyBobertoBackdrops=["theme","aurora","ocean","dusk","paper"];
    // A custom accent is any opaque #RRGGBB in upper case (the presets' spelling), so equal colors compare equal.
    public static bool IsHex(string? text)=>text is {Length:7}&&text[0]=='#'&&text.Skip(1).All(char.IsAsciiHexDigit);
    public void Validate()
    {
        if(!Themes.All.Any(p=>p.Id==ThemeId) || !(AccentHex==null||Themes.Accents.Any(a=>a.Hex==AccentHex)||IsHex(AccentHex)&&AccentHex==AccentHex.ToUpperInvariant()) || UiFont is not ("inter" or "system") || EditorFont is not ("jetbrains" or "consolas") || !double.IsFinite(UiSize) || UiSize is < 11 or > 17 || !double.IsFinite(EditorSize) || EditorSize is < 9 or > 32 || !double.IsFinite(BobertoSize) || BobertoSize is < 60 or > 120 || !LegacyBobertoBackdrops.Contains(BobertoBackground)) throw new ArgumentException("Invalid appearance settings.");
    }
}

public static class AppearanceStore
{
    private static readonly JsonSerializerOptions Options=new() {WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public static string PathName=>DataFolder.File("appearance.json");
    public static AppearanceSettings Load(string path)
    {
        if(!File.Exists(path))return new();
        if(new FileInfo(path).Length>65536)throw new IOException("Appearance settings exceed 64 KiB.");
        var settings=JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path),Options)??throw new JsonException("Empty appearance settings.");
        settings.Validate();return settings;
    }
    public static void Save(string path,AppearanceSettings settings)
    {
        settings.Validate();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string text=JsonSerializer.Serialize(settings,Options);
        if(File.Exists(path)&&File.ReadAllText(path)==text)return;
        AtomicFiles.SaveUtf8(path,text);
    }
}

public static class AppTypography
{
    public static FontFamily Inter { get; }=new(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Inter");
    public static FontFamily Mono { get; }=new(new Uri("pack://application:,,,/"),"./Assets/Fonts/#JetBrains Mono");
    public static void Apply(AppearanceSettings settings)
    {
        var resources=Application.Current.Resources;
        resources["UiFont"]=settings.UiFont=="inter"?Inter:new FontFamily("Segoe UI Variable, Segoe UI");
        resources["MonoFont"]=settings.EditorFont=="jetbrains"?Mono:new FontFamily("Consolas");
        resources["UiFontSize"]=settings.UiSize;
        resources["SmallFontSize"]=Math.Max(11,settings.UiSize-1);
        resources["SectionFontSize"]=settings.UiSize+4;
        resources["TitleFontSize"]=settings.UiSize+8;
        resources["EditorFontSize"]=settings.EditorSize;
        resources["GridRowHeight"]=Math.Max(settings.Compact?26d:32d,settings.UiSize+16);
        resources["GridHeaderHeight"]=settings.UiSize+20;
        // Square by default: DBMON's Neo Mirai skin. "Soft control corners" switches to DBMON's standard radii
        // (r-sm 6 controls, r-md 8 strips, pills) and drops the Neo Mirai corner brackets.
        resources["ControlRadius"]=new CornerRadius(settings.Rounded?6:0);
        resources["StripRadius"]=new CornerRadius(settings.Rounded?8:0);
        resources["PillRadius"]=new CornerRadius(settings.Rounded?16:0);

        resources["ToolbarPadding"]=settings.Compact?new Thickness(16,7,16,7):new Thickness(16,10,16,10);
    }
}
