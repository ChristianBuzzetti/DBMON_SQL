using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private AppearanceSettings appearance=new();
    private BobertoDockControl? bobertoDock;
    private bool companionAllowed,personalizing;
    // Smoke keeps Studio and dock in one isolated profile so their shared look can be verified without user data.
    private string? BobertoProfile=>isSmoke?Path.Combine(AppContext.BaseDirectory,"BobertoSmoke","profile"):null;
    private void InitializeAppearance()
    {
        if(isSmoke&&!IsShowcase) appearance=appearance with {ThemeId="light"};
        else try{appearance=AppearanceStore.Load(AppearanceStore.PathName);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException){AddMessage(1,"CLIENT","Unable to load appearance settings: "+ex.Message);}
        ApplyAppearance(appearance);
        if(appearance.BobertoVisible&&!Environment.GetCommandLineArgs().Contains("--startup-report"))BobertoRuntime.Prewarm(BobertoProfile??BobertoRuntime.DefaultProfile);
        // The companion's WebView2 starts after the editor's first render; --startup-report measures the editor alone.
        ContentRendered+=(_,_)=> {if(companionAllowed||Environment.GetCommandLineArgs().Contains("--startup-report"))return;companionAllowed=true;SyncBobertoDock(appearance);};
        StateChanged+=(_,_)=>_=bobertoDock?.PauseAsync(WindowState==WindowState.Minimized||!appearance.BobertoVisible);
        Closed+=(_,_)=>ReleaseBobertoDock();
        SizeChanged+=(_,_)=>AdaptHeader();
        HeaderLeft.SizeChanged+=(_,_)=>SizeBobertoBand();
    }
    private void ApplyAppearance(AppearanceSettings settings)
    {
        settings.Validate();appearance=settings;var palette=Themes.WithAccent(Themes.All.Single(p=>p.Id==settings.ThemeId),settings.AccentHex);Themes.Apply(palette,Editor);AppTypography.Apply(settings);Editor.FontSize=settings.EditorSize;
        ShowBobertoMenu.IsChecked=settings.BobertoVisible;
        BobertoPanel.Visibility=settings.BobertoVisible?Visibility.Visible:Visibility.Collapsed;
        AdaptHeader();
        SyncBobertoDock(settings);
        ApplyShellMotion(settings);
        SetLogVisible(settings.ShowMessages);
        foreach(Window window in Application.Current.Windows)Themes.ApplyChrome(window,palette);
    }
    // Hiding Boberto suspends its renderer instead of disposing it: recreating a WebView2 on the same profile while the
    // previous browser process was still shutting down froze Personalization and could read an empty look back.
    private void SyncBobertoDock(AppearanceSettings settings)
    {
        bool pause=!settings.BobertoVisible||WindowState==WindowState.Minimized;
        if(bobertoDock!=null){_=bobertoDock.ApplyThemeAsync(settings);if(bobertoDock.IsPaused!=pause)_=bobertoDock.PauseAsync(pause);return;}
        if(!companionAllowed||!settings.BobertoVisible)return;
        bobertoDock=new BobertoDockControl(settings,BobertoProfile);
        // Deferred: a modal dialog must not run inside the WebView2 message callback.
        bobertoDock.StudioRequested+=()=>Dispatcher.BeginInvoke(()=>OpenPersonalization(true));
        BobertoHost.Content=bobertoDock;
        if(pause)_=bobertoDock.PauseAsync(true);
    }    // Short windows drop the subtitle and tighten the header so SQL keeps its height.
    private void AdaptHeader()
    {
        bool shortWindow=ActualHeight is > 0 and < 720;
        Resources["TopMenuGlyph"]=ActualWidth is > 0 and < 1180?Visibility.Collapsed:Visibility.Visible;
        // Narrow windows keep every action but show glyphs only, so the header keeps one toolbar row.
        bool narrow=ActualWidth is > 0 and < 1180;var labels=narrow?Visibility.Collapsed:Visibility.Visible;
        CancelText.Visibility=ObjectsText.Visibility=RecipesText.Visibility=PersonalizeText.Visibility=RunKeyHint.Visibility=RepeatNowText.Visibility=labels;
        CommandsText.Visibility=ActualWidth is > 0 and < 1000?Visibility.Collapsed:Visibility.Visible;CommandsButton.MinWidth=narrow?0:250;
        RepeatStopText.Text=narrow?"Stop":"Stop repeating";
        HeaderBar.Padding=shortWindow?new Thickness(20,6,16,6):new Thickness(20,10,16,10);
        MainMenu.Padding=shortWindow?new Thickness(16,4,16,0):new Thickness(16,8,16,0);
        SizeBobertoBand();
    }
    // Boberto's scene is 16:9 like the background art: its width follows the header's real height, plus the icon
    // strip and the left rule. Small differences are ignored so a wrapping toolbar cannot make the band oscillate.
    private const double BobertoStrip=35;
    public const double BobertoMinWindow=900,BobertoMaxShare=0.30;
    private void SizeBobertoBand()
    {
        double width=BobertoBandWidth();BobertoColumn.Width=new GridLength(width);
        BobertoPanel.Width=width;BobertoPanel.Height=Math.Max(0,HeaderLeft.ActualHeight);
    }
    private double BobertoBandWidth()
    {
        // Below 900 DIP the band steps aside so the workspace keeps the header; the preference itself is unchanged.
        if(!appearance.BobertoVisible||ActualWidth is > 0 and < BobertoMinWindow)return 0;
        double height=HeaderLeft.ActualHeight>0?HeaderLeft.ActualHeight:150;
        // Never more than 30% of the window: a taller header must not widen the band and squeeze the toolbar further.
        double width=Math.Min(Math.Round(height*16/9)+BobertoStrip,ActualWidth>0?Math.Round(ActualWidth*BobertoMaxShare):double.MaxValue);
        return Math.Abs(BobertoColumn.Width.Value-width)<=2?BobertoColumn.Width.Value:width;
    }
    private void ReleaseBobertoDock()
    {
        if(bobertoDock==null)return;
        BobertoHost.Content=null;bobertoDock.Dispose();bobertoDock=null;
    }
    // DBMON's living details: the topbar edge sweeps every 7 s (smg-edge-sweep) and the brand dot pulses (brandPulse
    // 2.4 s). Both stay still when animation is off in Personalize or in Windows.
    private void ApplyShellMotion(AppearanceSettings settings)
    {
        var sweep=(Color)Application.Current.Resources["EdgeSweepColor"];
        var brush=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(1,0)};
        foreach(var (offset,alpha) in new[]{(0d,0),(.37,0),(.5,sweep.A),(.63,0),(1d,0)})brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)alpha,sweep.R,sweep.G,sweep.B),offset));
        var travel=new TranslateTransform(-.65,0);brush.RelativeTransform=travel;
        bool motion=settings.BobertoMotion&&SystemParameters.ClientAreaAnimation;
        EdgeSweep.Fill=motion?brush:null;
        if(motion)travel.BeginAnimation(TranslateTransform.XProperty,new System.Windows.Media.Animation.DoubleAnimation(-.65,.65,TimeSpan.FromSeconds(7)){RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever});
        BrandDot.BeginAnimation(OpacityProperty,motion?new System.Windows.Media.Animation.DoubleAnimation(1,.45,TimeSpan.FromSeconds(1.2)){AutoReverse=true,RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever}:null);
        BuildWordmark(motion);
        shellMotion=motion;
    }
    // DBMON's .logo-wave: mono bold letters in dim ink; each one lights up with the accent at 30% of a 3 s cycle,
    // 0.18 s after the previous letter.
    private const string WordmarkText="DBMON SQL";
    private void BuildWordmark(bool motion)
    {
        Wordmark.Children.Clear();
        var dim=((SolidColorBrush)Application.Current.Resources["Muted"]).Color;var accent=((SolidColorBrush)Application.Current.Resources["Accent"]).Color;
        for(int i=0;i<WordmarkText.Length;i++){
            var ink=new SolidColorBrush(dim);
            var letter=new System.Windows.Controls.TextBlock {Text=WordmarkText[i].ToString(),FontWeight=FontWeights.Bold,FontSize=18,Foreground=ink,Margin=new Thickness(0,0,1,0)};
            letter.SetResourceReference(System.Windows.Controls.TextBlock.FontFamilyProperty,"MonoFont");Wordmark.Children.Add(letter);
            if(!motion||WordmarkText[i]==' ')continue;
            var wave=new System.Windows.Media.Animation.ColorAnimationUsingKeyFrames {Duration=TimeSpan.FromSeconds(3),RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever,BeginTime=TimeSpan.FromSeconds(i*.18)};
            var ease=new System.Windows.Media.Animation.KeySpline(.42,0,.58,1);
            wave.KeyFrames.Add(new System.Windows.Media.Animation.LinearColorKeyFrame(dim,System.Windows.Media.Animation.KeyTime.FromPercent(0)));
            wave.KeyFrames.Add(new System.Windows.Media.Animation.SplineColorKeyFrame(accent,System.Windows.Media.Animation.KeyTime.FromPercent(.3),ease));
            wave.KeyFrames.Add(new System.Windows.Media.Animation.SplineColorKeyFrame(dim,System.Windows.Media.Animation.KeyTime.FromPercent(1),ease));
            ink.BeginAnimation(SolidColorBrush.ColorProperty,wave);
        }
    }
    // Clicking the DBMON wordmark glitches it briefly (dbmon-glitch); still when motion is off.
    private void WordmarkClick(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        if(!shellMotion)return;
        var shift=Wordmark.RenderTransform as TranslateTransform??new TranslateTransform();Wordmark.RenderTransform=shift;
        var glitch=new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames {Duration=TimeSpan.FromSeconds(.5)};
        foreach(var (x,at) in new[]{(0d,0d),(-2d,.1),(2d,.2),(-1d,.3),(1d,.4),(0d,.5)})glitch.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteDoubleKeyFrame(x,System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at))));
        shift.BeginAnimation(TranslateTransform.XProperty,glitch);
    }
    private bool shellMotion;
    private void SetPalette(string id)=>ApplyAppearance(appearance with {ThemeId=id});
    private void OpenPersonalization(bool studio=false)
    {
        if(personalizing||!IsLoaded)return;personalizing=true;
        try{var window=new PersonalizationWindow(appearance,ApplyAppearance,studio,testProfile:BobertoProfile){Owner=this};window.ShowDialog();}
        finally{personalizing=false;}
        _=bobertoDock?.EnsureLiveAsync();
        SaveAppearance();
        if(Workspace.SelectedItem==QueryTab)Editor.TextArea.Focus();
    }
    private void PersonalizeClick(object sender,RoutedEventArgs e)=>OpenPersonalization();
    private void BobertoClick(object sender,RoutedEventArgs e)=>OpenPersonalization(true);
    private void BobertoHideClick(object sender,RoutedEventArgs e){ApplyAppearance(appearance with {BobertoVisible=false});SaveAppearance();Editor.TextArea.Focus();}
    private void BobertoToggleClick(object sender,RoutedEventArgs e)
    {
        ApplyAppearance(appearance with {BobertoVisible=ShowBobertoMenu.IsChecked});SaveAppearance();
    }
    private void SaveAppearance()
    {
        if(!isSmoke)try{AppearanceStore.Save(AppearanceStore.PathName,appearance);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){ShowError("Unable to save appearance settings: "+ex.Message);}
    }
}
