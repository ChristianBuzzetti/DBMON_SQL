using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AseIsql.Desktop;

// A repeat belongs to its own session: other tabs keep running their own SQL while it ticks. One window clock drives
// every session's plan, and the caution band on screen is always the active session's.
public partial class MainWindow
{
    private void StartRepeat(RepeatPlan plan) => StartRepeat(activeSession, plan);
    private void StartRepeat(SessionWorkspace owner, RepeatPlan plan)
    {
        owner.Repeat = plan; plan.NextRun = DateTime.Now;
        RecordHistory(plan.Sql);
        repeatClock ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        repeatClock.Tick -= RepeatClockTick; repeatClock.Tick += RepeatClockTick; repeatClock.Start();
        SyncRepeatBanner(); RefreshSessionHeader(owner); UpdateTitle();
        RepeatTick(owner);
    }

    private void RepeatClockTick(object? sender, EventArgs e)
    {
        foreach (var owner in sessions.ToArray()) if (owner.Repeat != null) RepeatTick(owner);
        if (sessions.All(session => session.Repeat == null)) repeatClock?.Stop();
    }

    // Runs when due and idle for its own session; another session's query never delays it.
    private async void RepeatTick(SessionWorkspace owner)
    {
        var plan = owner.Repeat;
        if (plan == null) return;
        if (!ReferenceEquals(owner.Engine, plan.Session) || owner.Engine is AseIsql.Odbc.OdbcSession { IsOpen: false })
        { StopRepeat(owner, "Stopped: the session it was running on is no longer active."); return; }
        if (IsOnScreen(owner)) UpdateRepeatBanner();
        if (owner.RepeatBusy || owner.Running != null || owner.Changing || owner.CatalogActions != 0 || DateTime.Now < plan.NextRun) return;
        owner.RepeatBusy = true;
        try
        {
            plan.Runs++; if (IsOnScreen(owner)) UpdateRepeatBanner();
            int before = owner.LastRunId;
            await ExecuteAsync(owner, sqlOverride: plan.Sql, label: "repeat run " + plan.Runs + (plan.MaxRuns is int max ? " of " + max : "") + " · every " + RepeatPlan.Describe(plan.Interval),
                repeatRun: true, replaceRun: plan.KeepEveryRun ? null : plan.LastRunId);
            if (owner.LastRunId != before) plan.LastRunId = owner.LastRunId;
            if (!ReferenceEquals(owner.Repeat, plan)) return;
            if (owner.LastRunFailed) plan.Failures++;
            plan.NextRun = DateTime.Now + plan.Interval;
            if (owner.LastRunFailed && plan.StopOnError) { StopRepeat(owner, "Stopped: run " + plan.Runs + " failed. Review Messages."); return; }
            if (plan.MaxRuns is int limit && plan.Runs >= limit) { StopRepeat(owner, "Completed " + Counts.Of(plan.Runs, "run") + "."); return; }
            if (IsOnScreen(owner)) UpdateRepeatBanner();
        }
        finally { owner.RepeatBusy = false; RefreshFor(owner); }
    }

    private void StopRepeat(string reason) => StopRepeat(activeSession, reason);
    private void StopRepeat(SessionWorkspace owner, string reason)
    {
        if (owner.Repeat is not { } plan) return;
        owner.Repeat = null;
        if (sessions.All(session => session.Repeat == null)) repeatClock?.Stop();
        SyncRepeatBanner();
        AddMessage(owner, plan.Failures > 0 ? 1 : 0, "CLIENT", "Repeat ended · " + reason + " " + Counts.Of(plan.Runs, "run") +
            (plan.Failures > 0 ? ", " + Counts.Of(plan.Failures, "failed run") : "") + " every " + RepeatPlan.Describe(plan.Interval) + ".");
        SetStatus(owner, "Repeat ended · " + reason);
        RefreshFor(owner);
    }

    // The band, the Run glyph and the menu entry describe the session on screen, so switching tabs switches them.
    private void SyncRepeatBanner()
    {
        bool on = activeSession.Repeat != null;
        RepeatBanner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        RunMenuGlyph.Text = on ? "" : "";
        RepeatMenu.Header = on ? "Stop repeating" : "Repeat every…";
        RepeatPulse.BeginAnimation(OpacityProperty, on && shellMotion
            ? new DoubleAnimation(1, .25, TimeSpan.FromSeconds(.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        if (on) UpdateRepeatBanner();
        UpdateTitle();
    }
}
