using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AseIsql.Desktop;

public sealed class BobertoStudioControl:Grid,IDisposable
{
    private readonly WebView2 view=new();
    private readonly TextBlock feedback=new() {Text="Loading Boberto Studio…",Margin=new Thickness(24),TextWrapping=TextWrapping.Wrap};
    private readonly TaskCompletionSource<bool> ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string profilePath;
    private AppearanceSettings appearance;
    private readonly bool cacheAvatar;
    private CoreWebView2Environment? environment;
    private bool started,disposed,paused;
    private readonly SemaphoreSlim runtimeOperations=new(1,1);
    private readonly CancellationTokenSource lifetime=new();
    private int visibilityRevision;
    private byte[]? lastFrame;
    public event Action<BitmapSource>? FrameReady;
    public int BlockedRequests {get;private set;}
    public static string AvatarPath=>Path.Combine(Path.GetDirectoryName(AppearanceStore.PathName)!,"boberto-avatar.png");
    public BobertoStudioControl(AppearanceSettings settings,string? isolatedProfile=null)
    {
        appearance=settings;cacheAvatar=isolatedProfile==null;profilePath=isolatedProfile??BobertoRuntime.DefaultProfile;Children.Add(view);Children.Add(feedback);Loaded+=async(_,_)=>await InitializeAsync();
    }
    private static bool LocalUri(string value)=>BobertoRuntime.LocalUri(value);
    private async Task InitializeAsync()
    {
        if(started||disposed)return;started=true;
        try {
            environment=await BobertoRuntime.EnvironmentAsync(profilePath);
            if(disposed)return;
            await view.EnsureCoreWebView2Async(environment);
            if(disposed)return;
            var core=view.CoreWebView2;BobertoRuntime.Harden(core,environment,()=>BlockedRequests++);
            core.WebMessageReceived+=(_,args)=> {
                if(!LocalUri(args.Source)||args.WebMessageAsJson.Length>2*1024*1024)return;
                if(disposed)return;
                try {using var message=JsonDocument.Parse(args.WebMessageAsJson);string? type=message.RootElement.GetProperty("type").GetString();if(type=="ready") {feedback.Visibility=Visibility.Collapsed;ready.TrySetResult(true);_=PauseAsync(paused);}else if(type=="avatar")ReceiveFrame(message.RootElement.GetProperty("png").GetString());else if(type=="error"){var error=new InvalidOperationException(message.RootElement.GetProperty("message").GetString());feedback.Visibility=Visibility.Visible;feedback.Text=error.Message;ready.TrySetException(error);}}
                catch(Exception ex)when(ex is JsonException or KeyNotFoundException or FormatException or ArgumentException or NotSupportedException){feedback.Text="Boberto preview unavailable: "+ex.Message;}
            };
            core.NavigationCompleted+=(_,args)=> {if(!args.IsSuccess){var error=new InvalidOperationException("Boberto Studio failed to load: "+args.WebErrorStatus);feedback.Text=error.Message;ready.TrySetException(error);}};
            core.Navigate(BobertoRuntime.Origin+"studio.html");
        }catch(Exception ex)when(ex is WebView2RuntimeNotFoundException or IOException or UnauthorizedAccessException or COMException or InvalidOperationException){if(!disposed){feedback.Text="Boberto Studio requires Microsoft Edge WebView2 Runtime. The SQL editor and connection remain available.\n\n"+ex.Message;ready.TrySetException(ex);}}
    }
    public async Task<string> EvaluateAsync(string script)
    {
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));if(disposed)throw new ObjectDisposedException(nameof(BobertoStudioControl));return await view.CoreWebView2.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(10));
    }
    public async Task<byte[]> CaptureAsync()
    {
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));if(disposed)throw new ObjectDisposedException(nameof(BobertoStudioControl));
        using var image=new MemoryStream();await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);return image.ToArray();
    }
    public async Task ApplyThemeAsync(AppearanceSettings settings)
    {
        appearance=settings;if(!ready.Task.IsCompletedSuccessfully||disposed||paused)return;
        var tokens=BobertoRuntime.ThemeTokens(settings);string theme=BobertoRuntime.ThemeName(settings);
        await RunRuntimeAsync(async core=> {
            if(paused)return;
            // Animated borders in boberto-estudio.css require html[data-animations="on"], which SMG_APPS sets from its tweaks panel.
            await core.ExecuteScriptAsync("window.desktopStudio.applyTheme("+JsonSerializer.Serialize(tokens)+","+JsonSerializer.Serialize(theme)+","+(settings.BobertoMotion?"true":"false")+")");
            if(disposed||paused)return;
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia",BobertoRuntime.MediaFeatures(settings.BobertoMotion));
        });
    }
    private void ReceiveFrame(string? data)
    {
        if(data==null||!data.StartsWith("data:image/png;base64,",StringComparison.Ordinal)||data.Length>1024*1024)throw new ArgumentException("Invalid Boberto PNG preview.");
        var bytes=Convert.FromBase64String(data[22..]);var image=DecodeFrame(bytes);lastFrame=bytes;FrameReady?.Invoke(image);
    }
    public static BitmapSource DecodeFrame(byte[] bytes)
    {
        if(bytes.Length>1024*1024||bytes.Length<24||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new ArgumentException("Invalid Boberto PNG.");
        int width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)),height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
        if(width is < 1 or > 512||height is < 1 or > 512)throw new ArgumentException("Boberto preview exceeds 512 pixels.");
        using var stream=new MemoryStream(bytes);var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var frame=decoder.Frames[0];frame.Freeze();return frame;
    }
    public object RuntimeMetrics()
    {
        var processes=environment?.GetProcessInfos();double cpu=0;long peakEstimate=0;int count=0;
        if(processes!=null)foreach(var info in processes)try{using var process=System.Diagnostics.Process.GetProcessById(info.ProcessId);cpu+=process.TotalProcessorTime.TotalMilliseconds;peakEstimate+=process.PeakWorkingSet64;count++;}catch(ArgumentException){}
        return new{processes=count,cpuMs=cpu,sumProcessPeakWorkingSetEstimateBytes=peakEstimate,blockedRequests=BlockedRequests};
    }
    public async Task PauseAsync(bool pause)
    {
        paused=pause;int revision=++visibilityRevision;
        if(!ready.Task.IsCompletedSuccessfully||disposed)return;
        await RunRuntimeAsync(async core=> {
            if(revision!=visibilityRevision)return;
            // Hidden only: Studio shares its profile's renderer process with the console dock, and suspending it
            // froze that shared renderer, leaving the console dock blank mid-reload. A hidden WebView2 stops painting.
            if(pause)view.Visibility=Visibility.Collapsed;
            else{if(core.IsSuspended)core.Resume();view.Visibility=Visibility.Visible;}
            await Task.CompletedTask;
        });
        if(!pause&&revision==visibilityRevision&&!disposed)await ApplyThemeAsync(appearance);
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
            if(!disposed){feedback.Visibility=Visibility.Visible;feedback.Text="Boberto preview did not respond. Close and reopen Personalization to retry.";}
        }catch(Exception ex)when(ex is InvalidOperationException or COMException){
            if(!disposed){feedback.Visibility=Visibility.Visible;feedback.Text="Boberto preview unavailable. Close and reopen Personalization to retry.\n\n"+ex.Message;}
        }finally{runtimeOperations.Release();}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;lifetime.Cancel();ready.TrySetCanceled();view.Dispose();environment=null;FrameReady=null;
        if(lastFrame!=null&&cacheAvatar)try{Directory.CreateDirectory(Path.GetDirectoryName(AvatarPath)!);if(!File.Exists(AvatarPath)||!File.ReadAllBytes(AvatarPath).AsSpan().SequenceEqual(lastFrame)){string staging=AvatarPath+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(staging,lastFrame);File.Move(staging,AvatarPath,true);}finally{if(File.Exists(staging))File.Delete(staging);}}}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){System.Diagnostics.Debug.WriteLine("Boberto avatar cache: "+ex.Message);}
    }
}
