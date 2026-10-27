using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private async Task VerifyTableCompletion(Action<bool, string> require)
    {
        var original = activeSession;
        var owner = AddSession()!;
        ObjectResolutionSmokeEngine Prepare(string sql, QueryOutput response, ServerPlatform platform = ServerPlatform.Ase)
        {
            CloseEditorCompletion();
            var source = new ObjectResolutionSmokeEngine(platform, response);
            owner.Engine = source; owner.Options = AseSessionOptions.Default;
            Editor.Text = sql; Editor.CaretOffset = sql.Length; Editor.TextArea.Focus();
            return source;
        }
        async Task Invoke()
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            require(HandleEditorCompletionKey(Key.Space, ModifierKeys.Control, args), "Ctrl+Space is handled for a table prefix");
            await tableCompletionLoad;
        }
        ICompletionData[] Entries() => editorCompletion?.CompletionList.CompletionData.ToArray() ?? [];
        void Accept(string name)
        {
            editorCompletion!.CompletionList.SelectedItem = Entries().First(item => item.Text == name);
            editorCompletion.CompletionList.RequestInsertion(EventArgs.Empty);
        }
        try
        {
            var source = Prepare("select * from mon",
                ObjectResolutionFixture((7, "dbo", "mon_table", "U"), (8, "sales", "mon_sales", "U")));
            AddMessage(owner, 0, "CLIENT", "Synthetic diagnostic retained across completion.");
            var messages = owner.Messages.ToArray(); var tabs = owner.Tabs.ToArray();
            await Invoke();
            require(source.Calls == 1 && Entries().Select(item => item.Text).Order().SequenceEqual(new[] { "mon_sales", "mon_table" }),
                "Ctrl+Space offers actual matching tables from the current database catalog without local vocabulary");
            require(source.LastSql.Contains("select db_name() as context_database") && source.LastSql.Contains("from sysobjects") &&
                source.LastSql.Contains("if @@tranchained = 0 and @@trancount = 0 begin") && source.LastSql.Contains("select top 101") &&
                source.LastSql.Contains("o.type in ('U','S')") && !source.LastSql.Contains("use ", StringComparison.OrdinalIgnoreCase),
                "Table completion is bounded, uses the effective database and guards transactions before reading");
            require(owner.Messages.SequenceEqual(messages) && owner.Tabs.SequenceEqual(tabs) && Editor.Text == "select * from mon" &&
                DatabaseText.Text == "live_db" && !owner.Busy, "Table lookup preserves SQL, database status, results and Messages");
            CaptureSmokeVisual(editorCompletion!, "completion-live-tables.png");
            CaptureSmokeVisual(this, "completion-live-context.png");
            Editor.TextArea.PerformTextInput("_t"); Accept("mon_table");
            require(Editor.Text == "select * from mon_table", "A table suggestion can be narrowed by typing and accepted without executing SQL");
            Editor.Undo(); require(Editor.Text == "select * from mon_t", "Undo restores the table prefix in one step");

            foreach (string sql in new[] { "select * from mon", "select * from mon_db..mon", "select * from mon_db.dbo.mon", "select * from dbo.mon" })
            {
                bool qualifiedOwner = sql.Contains("dbo.");
                Prepare(sql, qualifiedOwner ? ObjectResolutionFixture((7, "dbo", "mon_table", "U")) :
                    ObjectResolutionFixture((7, "dbo", "mon_table", "U"), (8, "sales", "mon_table", "U")));
                await Invoke(); Accept("mon_table");
                require(Editor.Text == (qualifiedOwner ? sql[..^3] + "mon_table" : sql.Contains("..") ? "select * from mon_db.dbo.mon_table" : "select * from dbo.mon_table"),
                    "Table insertion preserves qualifiers and resolves duplicate owners: " + sql);
            }
            var mixedCase = Prepare("select * from mon", ObjectResolutionFixture((7, "dbo", "monLedger", "U")));
            await Invoke(); Accept("monLedger");
            require(Editor.Text == "select * from monLedger", "Catalog table completion preserves the server's identifier spelling");
            var escaped = Prepare("select * from [db'quote].[own'er].mon_", ObjectResolutionFixture((7, "own'er", "mon_table", "U")));
            await Invoke();
            require(escaped.LastSql.Contains("[db'quote]..sysobjects") && escaped.LastSql.Contains("u.name = 'own''er'") && escaped.LastSql.Contains("like 'mon\\_%'"),
                "Table lookup escapes qualified names, literals and LIKE wildcards");
            var iq = Prepare("select * from app.mon", ObjectResolutionFixture((7, "app", "mon_iq", "U")), ServerPlatform.Iq);
            await Invoke(); require(iq.LastSql.Contains("SYS.SYSTAB") && Entries().Any(item => item.Text == "mon_iq"), "IQ completion uses its connected table catalog");

            foreach (var response in new[] { new QueryOutput { Error = "Synthetic permission denied" }, new QueryOutput(),
                ObjectResolutionFixture((7, "dbo", "mon_view", "V")), ObjectResolutionFixture((7, null, "mon_table", "U")) })
            {
                Prepare("select * from mon", response); await Invoke();
                require(editorCompletion == null && !owner.Busy, "Denied, malformed or non-table catalog data never produces table suggestions");
            }
            var limit = Prepare("select * from mon", ObjectResolutionFixture(Enumerable.Range(1, 101).Select(i => (i, (string?)"dbo", "mon" + i, "U")).ToArray()));
            await Invoke(); require(Entries().Length == 100, "Table completion displays at most 100 catalog matches");

            foreach (string block in new[] { "busy", "noexec", "rs" })
            {
                var blocked = Prepare("select * from mon", ObjectResolutionFixture(), block == "rs" ? ServerPlatform.Rs : ServerPlatform.Ase);
                owner.Changing = block == "busy"; owner.Options = AseSessionOptions.Default with { NoExec = block == "noexec" };
                await Invoke(); require(blocked.Calls == 0 && editorCompletion == null, "Table completion does not probe a " + block + " session");
                owner.Changing = false;
            }
            foreach (string change in new[] { "typing", "caret", "escape", "session", "engine", "document", "run" })
            {
                var delayed = Prepare("select * from mon", ObjectResolutionFixture((7, "dbo", "mon_table", "U")));
                using var gate = new ManualResetEventSlim();
                delayed.Gate = gate; delayed.IgnoreCancellation = true;
                var pending = Invoke(); await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (change == "typing") Editor.AppendText("x");
                if (change == "caret") Editor.CaretOffset--;
                if (change == "escape") HandleEditorCompletionKey(Key.Escape, ModifierKeys.None,
                    new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                if (change == "session") SelectSession(original);
                if (change == "engine") owner.Engine = new DisconnectedQueryEngine();
                if (change == "document") Editor.Document = new ICSharpCode.AvalonEdit.Document.TextDocument("select * from mon");
                if (change == "run") owner.RunCounter++;
                require(owner.Running != null, "An unresponsive metadata driver retains its session lane after " + change);
                gate.Set(); await pending;
                require(editorCompletion == null && !owner.Busy, "A late table response is discarded after " + change);
                SelectSession(owner);
            }
        }
        finally
        {
            CloseEditorCompletion(); SelectSession(original); sessions.Remove(owner); SessionTabs.Items.Remove(owner.Header);
        }
    }
}
