using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AseIsql.Desktop;

// SQL repeated on a timer against the current session. The text and parameter values are fixed when it starts;
// the next run is scheduled Interval after the previous one finishes, so runs never overlap. It stops on demand,
// after MaxRuns, on a failed run when StopOnError is set, and whenever the session it started on goes away.
public sealed class RepeatPlan(string sql,TimeSpan interval,int? maxRuns,bool stopOnError,IQueryEngine session,string scope,bool keepEveryRun=false)
{
    public static readonly TimeSpan MinInterval=TimeSpan.FromSeconds(5),MaxInterval=TimeSpan.FromHours(24);
    public string Sql { get; }=sql;
    public TimeSpan Interval { get; }=interval;
    public int? MaxRuns { get; }=maxRuns;
    public bool StopOnError { get; }=stopOnError;
    public IQueryEngine Session { get; }=session;
    public string Scope { get; }=scope;
    // False: each run replaces the previous run's results. True: every run adds its own result tabs.
    public bool KeepEveryRun { get; }=keepEveryRun;
    public int? LastRunId { get; set; }
    public int Runs { get; set; }
    public int Failures { get; set; }
    public DateTime NextRun { get; set; }
    public DateTime Started { get; }=DateTime.Now;
    public static string Describe(TimeSpan interval)=>interval.TotalSeconds<60?interval.TotalSeconds.ToString("0",CultureInfo.InvariantCulture)+" s":
        interval.TotalMinutes<60?interval.TotalMinutes.ToString("0.#",CultureInfo.InvariantCulture)+" min":interval.TotalHours.ToString("0.#",CultureInfo.InvariantCulture)+" h";
}

public partial class MainWindow
{
    private RepeatPlan? repeat;
    private DispatcherTimer? repeatClock;
    private bool repeatBusy;
    // Set by ExecuteAsync for the run that just finished: an error, a cancellation or a display failure.
    private bool lastRunFailed;

    private void RepeatClick(object sender,RoutedEventArgs e)
    {
        if(repeat!=null){StopRepeat("Stopped from the toolbar.");return;}
        if(running!=null||changingSession){StatusText.Text="Wait for the current operation to finish.";return;}
        if(engine is DisconnectedQueryEngine){StatusText.Text="Connect before repeating SQL.";return;}
        bool selection=Editor.SelectionLength>0;string source=selection?Editor.SelectedText:Editor.Text;
        if(string.IsNullOrWhiteSpace(source)){StatusText.Text="Write or select SQL to repeat.";return;}
        // Parameters are asked once: every run repeats the same filled text.
        if(FillParameters(source,selection?"Selection":"Query","Use for every run") is not {} sql)return;
        var form=new RepeatWindow(sql,selection?"selection":"query",engine.Name) {Owner=this};
        if(form.ShowDialog()!=true||form.Plan(engine) is not {} plan)return;
        StartRepeat(plan);
    }
    private void RepeatNowClick(object sender,RoutedEventArgs e){if(repeat!=null)repeat.NextRun=DateTime.Now;RepeatTick();}
    private void RepeatStopClick(object sender,RoutedEventArgs e)=>StopRepeat("Stopped by you.");

    private void StartRepeat(RepeatPlan plan)
    {
        repeat=plan;plan.NextRun=DateTime.Now;
        RecordHistory(plan.Sql);
        repeatClock??=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};
        repeatClock.Tick-=RepeatClockTick;repeatClock.Tick+=RepeatClockTick;repeatClock.Start();
        RepeatBanner.Visibility=Visibility.Visible;RunMenuGlyph.Text="\uE895";RepeatMenu.Header="Stop repeating";
        RepeatPulse.BeginAnimation(OpacityProperty,shellMotion?new DoubleAnimation(1,.25,TimeSpan.FromSeconds(.8)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever}:null);
        UpdateRepeatBanner();UpdateTitle();
        RepeatTick();
    }
    private void RepeatClockTick(object? sender,EventArgs e)=>RepeatTick();
    // Runs when due and idle; a user run in progress simply delays the next repeat.
    private async void RepeatTick()
    {
        var plan=repeat;
        if(plan==null)return;
        if(!ReferenceEquals(engine,plan.Session)||engine is AseIsql.Odbc.OdbcSession {IsOpen:false}){StopRepeat("Stopped: the session it was running on is no longer active.");return;}
        UpdateRepeatBanner();
        if(repeatBusy||running!=null||changingSession||DateTime.Now<plan.NextRun)return;
        repeatBusy=true;
        try{
            plan.Runs++;UpdateRepeatBanner();
            int before=lastRunId;
            await ExecuteAsync(sqlOverride:plan.Sql,label:"repeat run "+plan.Runs+(plan.MaxRuns is int max?" of "+max:"")+" · every "+RepeatPlan.Describe(plan.Interval),repeatRun:true,replaceRun:plan.KeepEveryRun?null:plan.LastRunId);
            if(lastRunId!=before)plan.LastRunId=lastRunId;
            if(!ReferenceEquals(repeat,plan))return;
            if(lastRunFailed)plan.Failures++;
            plan.NextRun=DateTime.Now+plan.Interval;
            if(lastRunFailed&&plan.StopOnError){StopRepeat("Stopped: run "+plan.Runs+" failed. Review Messages.");return;}
            if(plan.MaxRuns is int limit&&plan.Runs>=limit){StopRepeat("Completed "+Counts.Of(plan.Runs,"run")+".");return;}
            UpdateRepeatBanner();
        }finally{repeatBusy=false;}
    }
    private void StopRepeat(string reason)
    {
        if(repeat is not {} plan)return;
        repeat=null;repeatClock?.Stop();
        RepeatBanner.Visibility=Visibility.Collapsed;RunMenuGlyph.Text="\uE70D";RepeatMenu.Header="Repeat every…";
        RepeatPulse.BeginAnimation(OpacityProperty,null);
        AddMessage(plan.Failures>0?1:0,"CLIENT","Repeat ended · "+reason+" "+Counts.Of(plan.Runs,"run")+(plan.Failures>0?", "+Counts.Of(plan.Failures,"failed run"):"")+" every "+RepeatPlan.Describe(plan.Interval)+".");
        StatusText.Text="Repeat ended · "+reason;UpdateTitle();
    }
    private void UpdateRepeatBanner()
    {
        if(repeat is not {} plan)return;
        double left=Math.Max(0,(plan.NextRun-DateTime.Now).TotalSeconds);bool now=running!=null&&repeatBusy;
        RepeatTitle.Text="every "+RepeatPlan.Describe(plan.Interval);
        string firstLine=plan.Sql.Split('\n').Select(l=>l.Trim()).FirstOrDefault(l=>l.Length>0)??"";
        RepeatDetail.Text="run "+plan.Runs+(plan.MaxRuns is int max?" of "+max:" · until stopped")+" · "+plan.Session.Name+(plan.Failures>0?" · "+Counts.Of(plan.Failures,"failure"):"")+(plan.StopOnError?"":" · keeps going after errors")+(plan.KeepEveryRun?" · a new result per run":"")+" · "+plan.Scope+": "+firstLine;
        RepeatCountdown.Text=now?"RUNNING":left<0.5?"STARTING":"NEXT IN "+Math.Ceiling(left).ToString("0",CultureInfo.InvariantCulture)+" S";
        // The rail fills from the end of one run to the start of the next.
        double fraction=now?1:Math.Clamp(1-left/plan.Interval.TotalSeconds,0,1);
        RepeatProgress.Width=Math.Max(0,(RepeatBanner.ActualWidth-6)*fraction);
        RepeatBanner.ToolTip="Repeating since "+plan.Started.ToString("HH:mm:ss",CultureInfo.InvariantCulture)+"\n\n"+(plan.Sql.Length>2000?plan.Sql[..2000]+"…":plan.Sql);
    }
}

// Interval, number of runs and error policy for a repeat. SQL that changes data, objects or the server needs an
// explicit acknowledgement before Start is enabled.
public sealed class RepeatWindow:Window
{
    private readonly string sql,scope;
    public TextBox IntervalBox { get; }=new() {Width=90,Text="30"};
    public ComboBox UnitBox { get; }=new() {Width=110,ItemsSource=new[]{"seconds","minutes","hours"},SelectedIndex=0,Margin=new Thickness(8,0,0,0)};
    public TextBox RunsBox { get; }=new() {Width=90,Text="",ToolTip="Empty repeats until you stop it"};
    public CheckBox StopOnErrorBox { get; }=new() {Content="Stop at the first failed run",IsChecked=true};
    public CheckBox KeepEveryRunBox { get; }=new() {Content="Keep every run as a new result (otherwise each run replaces the previous one)",IsChecked=false,ToolTip="Up to 40 result tabs; the oldest unpinned ones close beyond that."};
    public CheckBox Acknowledge { get; }=new() {Visibility=Visibility.Collapsed};
    public Button Start { get; }
    private readonly TextBlock problem=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    public IReadOnlyList<string> StateChanges { get; }

    public RepeatWindow(string source,string scopeName,string target)
    {
        sql=source;scope=scopeName;StateChanges=SqlRisk.StateChanges(source);
        SetResourceReference(StyleProperty,typeof(Window));TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        Title="Repeat · "+target;Width=620;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var root=new StackPanel {Margin=new Thickness(20,18,20,16)};
        var title=new TextBlock {FontWeight=FontWeights.SemiBold};var mark=new Run("◤ ");mark.SetResourceReference(TextElement.ForegroundProperty,"Accent");title.Inlines.Add(mark);title.Inlines.Add(new Run("Repeat on a timer"));title.SetResourceReference(TextBlock.FontSizeProperty,"SectionFontSize");root.Children.Add(title);
        root.Children.Add(Muted("Runs the "+scope+" on "+target+" now and again after each run finishes. The text is fixed now: later edits in the editor do not change it. A caution band stays on screen until it stops.",new Thickness(0,4,0,14)));
        var preview=new TextBox {Text=sql,IsReadOnly=true,AcceptsReturn=true,MaxHeight=120,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,0,0,14)};preview.SetResourceReference(Control.FontFamilyProperty,"MonoFont");root.Children.Add(preview);
        var every=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};every.Children.Add(Label("Every"));every.Children.Add(IntervalBox);every.Children.Add(UnitBox);root.Children.Add(every);
        var runs=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,6)};runs.Children.Add(Label("Runs"));runs.Children.Add(RunsBox);runs.Children.Add(Muted("  empty = until you stop it (Esc or Stop repeating)"));root.Children.Add(runs);
        root.Children.Add(StopOnErrorBox);root.Children.Add(KeepEveryRunBox);
        if(StateChanges.Count>0){
            // A hazard callout in the same amber as the band, so the risk reads before the timer exists.
            var warning=new Border {Background=new SolidColorBrush(Color.FromRgb(0xF5,0xA5,0x24)),BorderBrush=new SolidColorBrush(Color.FromRgb(0x1A,0x14,0x06)),BorderThickness=new Thickness(1),Padding=new Thickness(12,9,12,9),Margin=new Thickness(0,12,0,0)};
            var lines=new StackPanel();var head=new TextBlock {Text="⚠ CAUTION · this SQL changes data or server state",FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(0x1A,0x14,0x06))};head.SetResourceReference(TextBlock.FontFamilyProperty,"MonoFont");lines.Children.Add(head);
            lines.Children.Add(new TextBlock {Text="Found: "+string.Join(", ",StateChanges)+". Every run applies it again.",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(0x2B,0x21,0x08)),Margin=new Thickness(0,3,0,8)});
            Acknowledge.Content=new TextBlock {Text="I understand it will run again on every interval",Foreground=new SolidColorBrush(Color.FromRgb(0x1A,0x14,0x06)),FontWeight=FontWeights.SemiBold};Acknowledge.Visibility=Visibility.Visible;lines.Children.Add(Acknowledge);
            warning.Child=lines;root.Children.Add(warning);
        }
        problem.SetResourceReference(TextBlock.ForegroundProperty,"ErrorInk");problem.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");root.Children.Add(problem);
        var footer=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};
        var cancel=new Button {Content="Cancel",IsCancel=true};
        Start=new Button {Content="Start repeating",IsDefault=true,MinWidth=120,Margin=new Thickness(8,0,0,0),FontWeight=FontWeights.SemiBold};Start.SetResourceReference(BackgroundProperty,"Accent");Start.SetResourceReference(ForegroundProperty,"OnAccent");Start.SetResourceReference(BorderBrushProperty,"Accent");
        footer.Children.Add(cancel);footer.Children.Add(Start);root.Children.Add(footer);Content=root;
        foreach(var box in new[]{IntervalBox,RunsBox})box.TextChanged+=(_,_)=>Validate();
        UnitBox.SelectionChanged+=(_,_)=>Validate();Acknowledge.Checked+=(_,_)=>Validate();Acknowledge.Unchecked+=(_,_)=>Validate();
        Start.Click+=(_,_)=> {if(Validate())DialogResult=true;};
        PreviewKeyDown+=(_,key)=> {if(key.Key==Key.Escape)Close();};
        Loaded+=(_,_)=> {IntervalBox.Focus();IntervalBox.SelectAll();};
        Validate();
    }
    private static TextBlock Label(string text)=>new() {Text=text,Width=60,VerticalAlignment=VerticalAlignment.Center};
    private static TextBlock Muted(string text,Thickness? margin=null){var block=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=margin??new Thickness(0)};block.SetResourceReference(TextBlock.ForegroundProperty,"Muted");block.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");return block;}
    public TimeSpan? Interval=>double.TryParse(IntervalBox.Text.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out double amount)&&double.IsFinite(amount)&&amount>0?
        UnitBox.SelectedIndex switch {1=>TimeSpan.FromMinutes(amount),2=>TimeSpan.FromHours(amount),_=>TimeSpan.FromSeconds(amount)}:null;
    // The reason Start is disabled, or null when the form is complete.
    public string? Problem()
    {
        if(Interval is not {} interval)return "Enter the interval as a positive number.";
        if(interval<RepeatPlan.MinInterval||interval>RepeatPlan.MaxInterval)return "The interval must be between 5 seconds and 24 hours.";
        if(RunsBox.Text.Trim().Length>0&&!(int.TryParse(RunsBox.Text.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out int runs)&&runs is >=1 and <=100000))return "Runs: a whole number from 1 to 100,000, or empty to repeat until stopped.";
        if(StateChanges.Count>0&&Acknowledge.IsChecked!=true)return "Confirm the caution above to repeat SQL that changes data or server state.";
        return null;
    }
    private bool Validate(){string? reason=Problem();problem.Text=reason??"";Start.IsEnabled=reason==null;return reason==null;}
    public RepeatPlan? Plan(IQueryEngine session)=>Problem()!=null?null:new RepeatPlan(sql,Interval!.Value,RunsBox.Text.Trim().Length==0?null:int.Parse(RunsBox.Text.Trim(),CultureInfo.InvariantCulture),StopOnErrorBox.IsChecked==true,session,scope,KeepEveryRunBox.IsChecked==true);
}
