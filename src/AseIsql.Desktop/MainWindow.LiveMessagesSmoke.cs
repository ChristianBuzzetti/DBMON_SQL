namespace AseIsql.Desktop;

public partial class MainWindow
{
    private sealed class LiveMessagesSmokeEngine : IQueryEngine, IDisposable
    {
        public string Name => "Live messages fixture";
        public readonly ManualResetEventSlim Finish = new();
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public QueryOutput Execute(string sql, CancellationToken cancellation = default) => Execute(sql, cancellation, null);
        public QueryOutput Execute(string sql, CancellationToken cancellation, IProgress<QueryMessage>? messages)
        {
            var output = new QueryOutput(messages);
            for (int i = 0; i < 1000; i++) output.Messages.Add(new QueryMessage(i, "ASE", "live " + i, "raw " + i));
            Started.TrySetResult();
            Finish.Wait(cancellation);
            output.Messages.Add(new QueryMessage(1000, "ASE", "live 1000", "raw 1000"));
            return output;
        }
        public void Dispose() => Finish.Dispose();
    }

    private async Task VerifyLiveMessages(Action<bool, string> require)
    {
        var owner = activeSession;
        var previous = owner.Engine;
        using var fixture = new LiveMessagesSmokeEngine();
        Task? execution = null;
        try
        {
            owner.Engine = fixture;
            execution = ExecuteAsync(owner, sqlOverride: "select 1");
            await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!owner.Messages.Any(row => row.Text == "live 999") && DateTime.UtcNow < deadline) await Task.Delay(25);
            require(!execution.IsCompleted && owner.Messages.Any(row => row.Text == "live 999" && row.RawText == "raw 999"),
                "Server messages appear on the dispatcher while execution is still blocked, with raw evidence intact");
            require(owner.Messages.Count == 501 && owner.Messages[1].Text == "live 0" && owner.Messages[249].Text == "live 248" &&
                owner.Messages[250].Text == "… 501 messages omitted …" && owner.Messages[251].Text == "live 750",
                "Live output retains the first 250 and last 250 panel rows around one accurate omission marker");
            fixture.Finish.Set(); await execution;
            require(owner.Messages.Count == 501 && owner.Messages.Count(row => row.Text.Contains("messages omitted")) == 1 &&
                owner.Messages[250].Text == "… 503 messages omitted …" && owner.Messages[^2].Text == "live 1000" && owner.Messages[^1].Text.StartsWith("Done ·"),
                "Completion flushes the final live message once and counts the Done note without replaying output");
        }
        finally
        {
            fixture.Finish.Set();
            if (execution != null) await execution;
            owner.Engine = previous;
            RefreshFor(owner);
        }
    }
}
