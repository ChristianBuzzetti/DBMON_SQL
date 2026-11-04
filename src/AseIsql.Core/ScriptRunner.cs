namespace AseIsql;

// How one batch ended: Ok; Failed (the server rejected it, the session is usable); Fatal (the connection or the run
// itself is gone, so nothing after it may run, whatever "continue after errors" says).
public enum BatchOutcome { Ok, Failed, Fatal }

// What a script run needs from a session. The runner owns the order of calls; the channel owns the driver.
// Execute is the only member that receives user SQL, and the runner calls it exactly once per batch execution:
// ReadPlan reads the plan buffer the server already filled and can never run the batch again.
public interface IBatchChannel
{
    string SourceTag { get; }
    // Statistics IO: arm the per-batch plan capture before a batch and read it back after.
    bool CapturesPlans { get; }
    void ArmPlanCapture(CancellationToken cancellation);
    // showplan_in_xml(index) as text; "" when the buffer is empty, null when the read itself failed (the channel has
    // already written the reason to output).
    string? ReadPlan(int index, QueryOutput output, CancellationToken cancellation);
    // Runs the batch once, reading its results and messages into output. A failure writes its diagnostics to output
    // and sets output.Error; cancellation and local result limits throw.
    BatchOutcome Execute(string batch, QueryOutput output, CancellationToken cancellation);
}

public sealed record ScriptRunSummary(int Executions, int Succeeded, int Failed, bool Stopped);

public static class ScriptRunner
{
    public const int MaxBatches = 256;
    // Every "go N" repetition counts as one execution.
    public const int MaxExecutions = 10000;
    public const string PlanNotCaptured = "Statistics IO: plan not captured for this batch (showplan_in_xml(0) and (-1) returned an empty buffer). The batch was not run again to obtain one.";

    // Runs each batch Repeat times, in order. A failed batch stops the run unless continueOnError is set, as isql
    // does without and with its -c style option; a Fatal outcome always stops it. With continueOnError, or with any
    // repetition, the summary line counts the executions that succeeded and failed.
    public static ScriptRunSummary Run(IReadOnlyList<SqlBatch> batches, IBatchChannel channel, QueryOutput output, bool continueOnError, CancellationToken cancellation)
    {
        if (batches.Count > MaxBatches) throw new FormatException($"Local limit: {MaxBatches} GO batches per execution.");
        int total = SqlScript.Executions(batches);
        if (total > MaxExecutions) throw new FormatException($"Local limit: {MaxExecutions:N0} batch executions per run, counting go N repetitions ({total:N0} requested).");
        // Plain scripts keep their usual messages; the per-batch notes and the summary appear when they add information.
        bool report = continueOnError || batches.Any(batch => batch.Repeat > 1);
        var plans = new List<ExecutionIoPlan>();
        int executed = 0, succeeded = 0, failed = 0; bool stopped = false;
        string? firstError = null, firstRaw = null, firstSource = null;
        for (int b = 0; b < batches.Count && !stopped; b++)
        {
            var batch = batches[b];
            for (int run = 1; run <= batch.Repeat; run++)
            {
                cancellation.ThrowIfCancellationRequested();
                bool capture = channel.CapturesPlans;
                if (capture) channel.ArmPlanCapture(cancellation);
                // Each execution starts without an error of its own; the first failure is what the run reports.
                output.Error = null;
                var outcome = channel.Execute(batch.Text, output, cancellation);
                executed++;
                if (outcome == BatchOutcome.Ok)
                {
                    succeeded++;
                    if (capture && !cancellation.IsCancellationRequested) CollectPlans(channel, output, plans, cancellation);
                    continue;
                }
                failed++;
                firstError ??= output.Error; firstRaw ??= output.ErrorRawText; firstSource ??= output.ErrorSource;
                string where = Where(batches, b, run);
                if (outcome == BatchOutcome.Fatal || !continueOnError)
                {
                    stopped = true;
                    if (report) output.Messages.Add(new QueryMessage(1, "CLIENT", $"Stopped at {where}: {output.Error}"));
                    break;
                }
                output.Messages.Add(new QueryMessage(1, "CLIENT", $"Error in {where} · continuing with the next batch: {output.Error}"));
            }
        }
        if (plans.Any(p => p.Scans.Count > 0)) output.Tables.Add(ExecutionIo.ToTable(plans));
        if (report)
            output.Messages.Add(new QueryMessage(failed > 0 ? 1 : 0, "CLIENT", $"Summary: {succeeded} {(succeeded == 1 ? "batch" : "batches")} ok / {failed} with error" +
                (stopped && executed < total ? $" · {total - executed} not run" : "") + "."));
        if (failed == 0) output.Error = null;
        else
        {
            output.Error = continueOnError && !stopped ? $"{failed} of {executed} {(executed == 1 ? "batch" : "batches")} failed; the run continued after each error. First error: {firstError}" : firstError;
            output.ErrorRawText = firstRaw; output.ErrorSource = firstSource ?? output.ErrorSource;
        }
        return new ScriptRunSummary(executed, succeeded, failed, stopped);
    }

    private static string Where(IReadOnlyList<SqlBatch> batches, int index, int run) =>
        "batch " + (index + 1) + " of " + batches.Count + (batches[index].Repeat > 1 ? $" (run {run} of {batches[index].Repeat})" : "");

    // Reads the plans the server kept for the batch that just ran. Never executes the batch again: when the buffer is
    // empty the run says so and moves on (a second run doubles exec, dbcc or checkpoint effects).
    public static void CollectPlans(IBatchChannel channel, QueryOutput output, List<ExecutionIoPlan> plans, CancellationToken cancellation)
    {
        string? rawXml = channel.ReadPlan(0, output, cancellation);
        if (rawXml == null) return;
        if (rawXml.Length == 0) rawXml = channel.ReadPlan(-1, output, cancellation);
        if (rawXml == null) return;
        if (rawXml.Length == 0) { output.Messages.Add(new QueryMessage(0, "CLIENT", PlanNotCaptured)); return; }
        var report = ExecutionIo.Parse(rawXml);
        if (report.Plans.Count == 0)
        {
            output.Messages.Add(new QueryMessage(0, "CLIENT", $"Statistics IO: plan not captured · no user execution plan found in {rawXml.Length} chars ({report.Unreadable} unreadable).", rawXml));
            return;
        }
        foreach (var plan in report.Plans)
        {
            plans.Add(plan);
            foreach (string line in ExecutionIo.Summary(plan))
                output.Messages.Add(new QueryMessage(3615, channel.SourceTag, line, plan.Xml));
            if (plan.MissingStatistics.Count > 0)
                output.Messages.Add(new QueryMessage(0, channel.SourceTag, "Missing statistics · " + string.Join(", ", plan.MissingStatistics), plan.Xml));
        }
        if (report.Unreadable > 0)
            output.Messages.Add(new QueryMessage(0, "CLIENT", $"Statistics IO: {report.Unreadable} plan block(s) could not be parsed.", rawXml));
    }
}
