using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Ellipse=System.Windows.Shapes.Ellipse;
using Rectangle=System.Windows.Shapes.Rectangle;
using Shape=System.Windows.Shapes.Shape;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AseIsql.Desktop;

// Live console companion: the original DBMON engine in its sidebar dock (idle, gestures, background, border,
// reflection and neon floor). Without WebView2 Runtime it degrades to the last Studio frame and stays clickable.
public sealed class BobertoDockControl:Grid,IDisposable
{
    private WebView2 view=new() {Visibility=Visibility.Hidden};
    private readonly Border fallback=new() {Visibility=Visibility.Collapsed,Cursor=Cursors.Hand,Padding=new Thickness(12)};
    private readonly Image fallbackImage=new() {Width=96,Height=96,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,0,12)};
    private readonly TextBlock fallbackText=new() {TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center};
    private readonly TaskCompletionSource<bool> ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim runtimeOperations=new(1,1);
    private readonly CancellationTokenSource lifetime=new();
    private readonly string profilePath,snapshotPath;
    private readonly bool cacheSnapshot,suspendRenderer;
    private readonly Grid placeholder=new() {ClipToBounds=true};
    private AppearanceSettings appearance;
    private bool started,disposed,paused;
    private int visibilityRevision,misses,heals;
    private bool healing;
    private DateTime lastHeal=DateTime.MinValue;
    private readonly System.Windows.Threading.DispatcherTimer heartbeat=new() {Interval=TimeSpan.FromSeconds(4)};
    public int Heals {get;private set;}
    public event Action? StudioRequested;
    public int BlockedRequests {get;private set;}
    // Bounded lifecycle trace (navigation, ready, reveal, pause, renderer failures) for smoke diagnostics.
    private readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
    private readonly Queue<string> trace=new();
    public string Trace{get{lock(trace)return string.Join(" | ",trace);}}
    private void Note(string text){lock(trace){trace.Enqueue(clock.ElapsedMilliseconds+"ms "+text);while(trace.Count>60)trace.Dequeue();}}
    public string? Failure {get;private set;}
    public bool IsPaused=>paused;
    public bool IsLive=>ready.Task.IsCompletedSuccessfully&&!disposed&&Failure==null;
    public Task Ready=>ready.Task;
    public bool PlaceholderVisible=>placeholder.Visibility==Visibility.Visible;
    public bool ShowsSnapshot {get;private set;}
    public string SnapshotPath=>snapshotPath;
    // Only the console dock suspends its renderer when hidden; the Personalize preview shares that renderer and just hides.
    public BobertoDockControl(AppearanceSettings settings,string? profile=null,bool cacheSnapshot=true,bool suspendRenderer=true)
    {
        appearance=settings;profilePath=profile??BobertoRuntime.DefaultProfile;this.cacheSnapshot=cacheSnapshot;this.suspendRenderer=suspendRenderer;
        snapshotPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profilePath))!,"boberto-dock.png");
        BuildPlaceholder();
        var stack=new StackPanel {VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Center};stack.Children.Add(fallbackImage);stack.Children.Add(fallbackText);fallback.Child=stack;
        fallbackText.SetResourceReference(TextBlock.ForegroundProperty,"Muted");fallbackText.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");
        fallback.MouseLeftButtonUp+=(_,_)=>StudioRequested?.Invoke();
        Children.Add(view);Children.Add(placeholder);Children.Add(fallback);
        Loaded+=async(_,_)=>await InitializeAsync();
        heartbeat.Tick+=async(_,_)=>await HeartbeatAsync();
    }
    private async Task InitializeAsync()
    {
        if(started||disposed)return;started=true;
        try {
            var owner=view;
            var environment=await BobertoRuntime.EnvironmentAsync(profilePath);
            if(disposed)return;
            owner.DefaultBackgroundColor=BobertoRuntime.SurfaceColor();
            await owner.EnsureCoreWebView2Async(environment);
            if(disposed||!ReferenceEquals(owner,view))return;
            // Events from a WebView replaced by HealAsync are ignored.
            bool Stale()=>disposed||!ReferenceEquals(owner,view);
            var core=owner.CoreWebView2;BobertoRuntime.Harden(core,environment,()=>BlockedRequests++);
            core.WebMessageReceived+=(_,args)=> {
                if(Stale()||!BobertoRuntime.LocalUri(args.Source)||args.WebMessageAsJson.Length>64*1024)return;
                try {
                    using var message=JsonDocument.Parse(args.WebMessageAsJson);string? type=message.RootElement.GetProperty("type").GetString();
                    Note("message "+type);
                    // A page that reports ready after a transient failure (a cancelled reload, a recovered renderer) is live
                    // again: the failure is cleared so the reveal shows it instead of leaving the band empty.
                    if(type=="ready"){bool first=ready.TrySetResult(true);if(Failure!=null&&ready.Task.IsCompletedSuccessfully)Failure=null;fallback.Visibility=Visibility.Collapsed;misses=0;_=RevealAsync(first);}
                    else if(type=="studio")StudioRequested?.Invoke();
                    // After the dock is live, a script error is logged rather than replacing a working scene with the fallback.
                    else if(type=="error"){string text=message.RootElement.GetProperty("message").GetString()??"Boberto script error.";if(IsLive)System.Diagnostics.Debug.WriteLine("Boberto dock: "+text);else Fail(text);}
                }catch(Exception ex)when(ex is JsonException or KeyNotFoundException or InvalidOperationException){Fail("Boberto dock message rejected: "+ex.Message);}
            };
            core.NavigationStarting+=(_,args)=>Note("navigate "+(args.IsRedirected?"redirect":"start"));
            // Studio changes reload the dock (dock.js); a reload that starts before the previous one finishes aborts it
            // (ConnectionAborted / OperationCanceled). That is not a failure: marking it failed used to leave the band
            // empty for good. Other load failures after the dock has been live are healed; the watchdog covers stalls.
            core.NavigationCompleted+=(_,args)=> {
                Note("navigated "+args.IsSuccess+" "+args.WebErrorStatus);
                if(args.IsSuccess||Stale()||args.WebErrorStatus is CoreWebView2WebErrorStatus.OperationCanceled or CoreWebView2WebErrorStatus.ConnectionAborted)return;
                if(ready.Task.IsCompletedSuccessfully)_=HealAsync("load failed: "+args.WebErrorStatus);else Fail("Boberto dock failed to load: "+args.WebErrorStatus);
            };
            // A renderer crash must not take the SQL console with it; the companion heals itself (bounded in HealAsync).
            core.ProcessFailed+=(_,args)=> {
                if(Stale())return;Note("process failed "+args.ProcessFailedKind+" "+args.Reason);
                if(args.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive or CoreWebView2ProcessFailedKind.FrameRenderProcessExited)_=HealAsync("renderer "+args.ProcessFailedKind);
                else if(args.ProcessFailedKind!=CoreWebView2ProcessFailedKind.GpuProcessExited)Fail("Boberto runtime stopped: "+args.ProcessFailedKind);
            };
            core.Navigate(BobertoRuntime.Origin+"dock.html");
            heartbeat.Start();
        }catch(Exception ex)when(ex is WebView2RuntimeNotFoundException or IOException or UnauthorizedAccessException or COMException or InvalidOperationException or ArgumentException){Fail("Boberto needs Microsoft Edge WebView2 Runtime. The SQL editor and connection remain available.");}
    }
    private void Fail(string message)
    {
        if(disposed)return;Failure=message;ready.TrySetException(new InvalidOperationException(message));
        view.Visibility=Visibility.Collapsed;HidePlaceholder();fallback.Visibility=Visibility.Visible;fallbackText.Text=message+"\nClick to open Studio.";
        try{fallbackImage.Source=File.Exists(BobertoStudioControl.AvatarPath)&&new FileInfo(BobertoStudioControl.AvatarPath).Length<=1024*1024?BobertoStudioControl.DecodeFrame(File.ReadAllBytes(BobertoStudioControl.AvatarPath)):new BitmapImage(new Uri("pack://application:,,,/Assets/boberto.png"));}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){fallbackImage.Source=new BitmapImage(new Uri("pack://application:,,,/Assets/boberto.png"));}
    }
    // The live scene appears only after theme and fitted scale are applied, so the swap from the placeholder never
    // shows the default palette or a scale jump. Each new look (and the first one) refreshes the startup snapshot.
    private async Task RevealAsync(bool first)
    {
        Note("reveal "+first);await ApplyThemeAsync(appearance);
        await Task.Delay(first?320:120);
        if(disposed||Failure!=null)return;
        if(!paused)view.Visibility=Visibility.Visible;
        HidePlaceholder();Note("revealed visible="+view.Visibility+" paused="+paused);
        if(first&&paused)_=PauseAsync(true);
        if(cacheSnapshot)_=SaveSnapshotAsync();
    }
    private async Task SaveSnapshotAsync()
    {
        await Task.Delay(1500);
        if(disposed||paused||!IsLive)return;
        try {
            byte[] png=await CaptureAsync();
            if(png.Length is < 24 or > 2*1024*1024)return;
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            string staging=snapshotPath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{await File.WriteAllBytesAsync(staging,png);File.Move(staging,snapshotPath,true);}finally{if(File.Exists(staging))File.Delete(staging);}
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or COMException or ObjectDisposedException or TimeoutException or TaskCanceledException){System.Diagnostics.Debug.WriteLine("Boberto snapshot: "+ex.Message);}
    }
    private BitmapSource? LoadSnapshot()
    {
        try {
            if(!File.Exists(snapshotPath)||new FileInfo(snapshotPath).Length is < 24 or > 2*1024*1024)return null;
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.CreateOptions=BitmapCreateOptions.IgnoreImageCache;image.UriSource=new Uri(snapshotPath);image.EndInit();image.Freeze();return image;
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException){return null;}
    }
    // Startup placeholder. With a snapshot: the last scene, a light sweep across it and a pulsing LOADING chip.
    // Without one (first run): Boberto floating over an accent halo and the neon floor with its sweep.
    private void BuildPlaceholder()
    {
        bool motion=appearance.BobertoMotion&&SystemParameters.ClientAreaAnimation;
        var surface=new Border();surface.SetResourceReference(Border.BackgroundProperty,"Header");placeholder.Children.Add(surface);
        var snapshot=LoadSnapshot();ShowsSnapshot=snapshot!=null;
        if(snapshot!=null){
            placeholder.Children.Add(new Image {Source=snapshot,Stretch=Stretch.UniformToFill,Opacity=.92});
            var shine=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(1,.35)};
            foreach(var (offset,alpha) in new[]{(0d,0),(.42,0),(.5,46),(.58,0),(1d,0)})shine.GradientStops.Add(new GradientStop(Color.FromArgb((byte)alpha,255,255,255),offset));
            var travel=new TranslateTransform(-1,0);shine.RelativeTransform=travel;placeholder.Children.Add(new Border {Background=shine,IsHitTestVisible=false});
            if(motion)travel.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-1,1,TimeSpan.FromSeconds(1.6)){RepeatBehavior=RepeatBehavior.Forever});
        } else {
            var halo=new Ellipse {Width=150,Height=90,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,18),Opacity=.55};
            halo.Fill=new RadialGradientBrush((Color)Application.Current.Resources["AccentGlowColor"],Colors.Transparent){Opacity=.35};placeholder.Children.Add(halo);
            var lift=new TranslateTransform();var mascot=new Image {Source=new BitmapImage(new Uri("pack://application:,,,/Assets/dbmon-sql.png")),Width=62,Height=62,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,34),RenderTransform=lift};
            RenderOptions.SetBitmapScalingMode(mascot,BitmapScalingMode.HighQuality);placeholder.Children.Add(mascot);
            if(motion)lift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,-6,TimeSpan.FromSeconds(.9)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase {EasingMode=EasingMode.EaseInOut}});
            var floor=new Grid {Height=2,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(10,0,10,30),ClipToBounds=true};
            var edge=new Rectangle {Opacity=.75};edge.SetResourceReference(Shape.FillProperty,"EdgeBar");floor.Children.Add(edge);
            var sweepColor=(Color)Application.Current.Resources["EdgeSweepColor"];var sweep=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(1,0)};
            foreach(var (offset,alpha) in new[]{(0d,0),(.37,0),(.5,sweepColor.A),(.63,0),(1d,0)})sweep.GradientStops.Add(new GradientStop(Color.FromArgb((byte)alpha,sweepColor.R,sweepColor.G,sweepColor.B),offset));
            var run=new TranslateTransform(-.65,0);sweep.RelativeTransform=run;floor.Children.Add(new Rectangle {Fill=sweep});placeholder.Children.Add(floor);
            if(motion)run.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-.65,.65,TimeSpan.FromSeconds(1.8)){RepeatBehavior=RepeatBehavior.Forever});
        }
        var chip=new Border {HorizontalAlignment=snapshot!=null?HorizontalAlignment.Right:HorizontalAlignment.Center,VerticalAlignment=snapshot!=null?VerticalAlignment.Top:VerticalAlignment.Bottom,Margin=snapshot!=null?new Thickness(0,8,8,0):new Thickness(0,0,0,9),Padding=new Thickness(7,2,8,2),BorderThickness=new Thickness(1)};
        chip.SetResourceReference(Border.BackgroundProperty,"Header");chip.SetResourceReference(Border.BorderBrushProperty,"AccentCardRing");
        var row=new StackPanel {Orientation=Orientation.Horizontal};var dot=new Ellipse {Width=5,Height=5,Margin=new Thickness(0,0,6,0),VerticalAlignment=VerticalAlignment.Center};dot.SetResourceReference(Shape.FillProperty,"Accent");
        var label=new TextBlock {Text=snapshot!=null?"LOADING":"WAKING BOBERTO",FontSize=9,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};label.SetResourceReference(TextBlock.FontFamilyProperty,"MonoFont");label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        row.Children.Add(dot);row.Children.Add(label);chip.Child=row;placeholder.Children.Add(chip);
        if(motion)dot.BeginAnimation(OpacityProperty,new DoubleAnimation(1,.25,TimeSpan.FromSeconds(.7)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
    }
    private void HidePlaceholder()
    {
        if(placeholder.Visibility!=Visibility.Visible)return;
        placeholder.Visibility=Visibility.Collapsed;placeholder.Children.Clear();
    }    public async Task ApplyThemeAsync(AppearanceSettings settings)
    {
        appearance=settings;if(!IsLive||paused)return;
        string tokens=JsonSerializer.Serialize(BobertoRuntime.ThemeTokens(settings)),theme=JsonSerializer.Serialize(BobertoRuntime.ThemeName(settings)),scale=BobertoRuntime.DockScale(settings).ToString(System.Globalization.CultureInfo.InvariantCulture);
        view.DefaultBackgroundColor=BobertoRuntime.SurfaceColor();
        await RunRuntimeAsync(async core=> {
            await core.ExecuteScriptAsync($"window.desktopDock.applyTheme({tokens},{theme},{scale},{(settings.BobertoMotion?"true":"false")})");
            if(disposed||paused)return;
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia",BobertoRuntime.MediaFeatures(settings.BobertoMotion));
        });
    }
    // Minimized windows and a hidden rail suspend the renderer; the idle loop stops with it.
    public async Task PauseAsync(bool pause)
    {
        paused=pause;int revision=++visibilityRevision;Note("pause "+pause);
        if(!IsLive)return;
        await RunRuntimeAsync(async core=> {
            if(revision!=visibilityRevision)return;
            if(pause){view.Visibility=Visibility.Collapsed;if(suspendRenderer)await core.TrySuspendAsync();}
            else{if(core.IsSuspended)core.Resume();view.Visibility=Visibility.Visible;}
        });
        if(!pause&&revision==visibilityRevision&&!disposed)await ApplyThemeAsync(appearance);
    }
    // Studio and the Personalize preview run in this profile's renderer process. After they close, the console scene
    // must be running and painted: a suspended renderer is resumed, and a page that does not answer is reloaded.
    public async Task EnsureLiveAsync()
    {
        if(!IsLive||paused)return;
        await RunRuntimeAsync(async core=> {
            if(core.IsSuspended)core.Resume();
            if(!paused)view.Visibility=Visibility.Visible;
            string state;
            try{state=await core.ExecuteScriptAsync("window.desktopDock&&window.BOBERTO&&window.BOBERTO.dock()&&document.querySelector('#plt-wrap canvas')?'live':'down'").WaitAsync(TimeSpan.FromSeconds(2));}
            catch(TimeoutException){state="timeout";}
            Note("ensure "+state);
            if(state!="\"live\""){Recoveries++;core.Reload();}
        });
    }
    public int Recoveries {get;private set;}
    // Watchdog: while shown, the dock must answer and own its canvas. Two missed beats in a row (8 s; a normal reload
    // takes well under a second) heal it, so a stalled page never leaves the band empty until the app restarts.
    private async Task HeartbeatAsync()
    {
        if(disposed||paused||healing||Failure!=null||!ready.Task.IsCompletedSuccessfully||view.CoreWebView2 is not {} core)return;
        bool alive;
        try{alive=await core.ExecuteScriptAsync("window.desktopDock&&window.BOBERTO&&window.BOBERTO.dock()&&document.querySelector('#plt-wrap canvas')?1:0").WaitAsync(TimeSpan.FromSeconds(3))=="1";}
        catch(Exception ex)when(ex is TimeoutException or InvalidOperationException or COMException or ObjectDisposedException){alive=false;}
        if(disposed||paused)return;
        if(alive&&view.Visibility!=Visibility.Visible&&!PlaceholderVisible){Note("heartbeat reveal");view.Visibility=Visibility.Visible;}
        if(alive){misses=0;return;}
        Note("heartbeat miss "+(misses+1));
        if(++misses>=2)await HealAsync("unresponsive");
    }
    // Recovery escalates: reload the page, then replace the WebView itself (same environment, so Studio and the
    // Personalize preview keep the browser process). The last snapshot covers the band meanwhile. Bounded so a
    // runtime that keeps failing ends in the clickable fallback instead of a reload loop.
    private async Task HealAsync(string reason)
    {
        if(disposed||healing)return;
        healing=true;
        try {
            if(DateTime.UtcNow-lastHeal>TimeSpan.FromMinutes(2))heals=0;
            lastHeal=DateTime.UtcNow;heals++;Heals++;misses=0;Note("heal "+heals+" "+reason);
            if(heals>6){Fail("Boberto stopped responding ("+reason+"). The SQL editor and connection remain available.");return;}
            if(heals%2==1&&view.CoreWebView2 is {} core){
                try{core.Reload();return;}catch(Exception ex)when(ex is InvalidOperationException or COMException){Note("reload failed "+ex.Message);}
            }
            await RebuildViewAsync();
        } finally {healing=false;}
    }
    private async Task RebuildViewAsync()
    {
        var old=view;
        if(placeholder.Visibility!=Visibility.Visible){BuildPlaceholder();placeholder.Visibility=Visibility.Visible;}
        view=new WebView2 {Visibility=Visibility.Hidden};
        Children.Remove(old);Children.Insert(0,view);
        try{old.Dispose();}catch(Exception ex)when(ex is InvalidOperationException or COMException){Note("dispose failed "+ex.Message);}
        started=false;await InitializeAsync();
    }
    // Smoke: exercises the rebuild path without waiting for a real stall.
    internal Task HealForTestAsync()=>RebuildViewAsync();
    public async Task<string> EvaluateAsync(string script)
    {
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));if(disposed)throw new ObjectDisposedException(nameof(BobertoDockControl));
        try{return await view.CoreWebView2.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(10));}
        catch(TimeoutException){Note("evaluate timeout");throw new TimeoutException("Boberto dock did not answer a script in 10 s. Trace: "+Trace);}
    }
    public async Task<byte[]> CaptureAsync()
    {
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));if(disposed)throw new ObjectDisposedException(nameof(BobertoDockControl));
        using var image=new MemoryStream();await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);return image.ToArray();
    }
    private async Task RunRuntimeAsync(Func<CoreWebView2,Task> operation)
    {
        await runtimeOperations.WaitAsync();
        try {
            if(disposed)return;
            var core=view.CoreWebView2;if(core==null)return;
            await operation(core).WaitAsync(TimeSpan.FromSeconds(5),lifetime.Token);
        }catch(OperationCanceledException)when(disposed){
        }catch(TimeoutException){
            Note("runtime timeout");System.Diagnostics.Debug.WriteLine("Boberto dock did not respond in time.");
        }catch(Exception ex)when(ex is InvalidOperationException or COMException){
            if(!disposed)Fail("Boberto dock unavailable: "+ex.Message);
        }finally{runtimeOperations.Release();}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;heartbeat.Stop();lifetime.Cancel();ready.TrySetCanceled();StudioRequested=null;view.Dispose();
    }
}
