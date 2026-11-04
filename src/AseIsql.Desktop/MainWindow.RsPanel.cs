using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private void RsPanelClick(object sender, RoutedEventArgs e) => OpenMonitor(ServerPlatform.Rs, "Replication Server panel", RsMonitor.Commands,
        (index, table) => new[] { (RsMonitor.Commands[index].Label, index switch {
            0 or 1 => RsMonitor.Threads(table), 3 => RsMonitor.DiskSpace(table), _ => table.Copy() }) }, AddRsActions);

    private void AddRsActions(Window window, StackPanel toolbar, SessionWorkspace owner, IQueryEngine source)
    {
        foreach (bool suspend in new[] { true, false }) {
            var button = new Button { Content = suspend ? "Suspend connection…" : "Resume connection…", Margin = new Thickness(8, 0, 0, 0) };
            button.Click += async (_, _) => {
                bool Allowed() => sessions.Contains(owner) && MonitorConnectionCurrent(owner, source, ServerPlatform.Rs) && !owner.Busy;
                if (!Allowed()) { ShowMessage("Connection action unavailable", "The original RS session is closed, changed or busy. Wait or reopen the panel."); return; }
                string? server = AskText("RS connection", "Data server name (as registered in Replication Server):", owner:window);
                if (server == null) return;
                string? database = AskText("RS connection", "Database name on " + server + ":", owner:window);
                if (database == null) return;
                string sql;
                try { sql = RsMonitor.ConnectionCommand(suspend, server, database); }
                catch (ArgumentException ex) { ShowMessage("Invalid RS connection", ex.Message); return; }
                string prod = IsProduction(owner) ? "PRODUCTION · " : "";
                string verb = suspend ? "Suspend" : "Resume";
                if (!Confirm(window, prod + verb + " RS connection", prod + source.Name + "\n\n" + sql + "\n\nThis changes replication state for the specified connection.", "Review action")) return;
                if (!Allowed()) return;
                if (!Confirm(window, "Confirm RS action again", prod + source.Name + "\n\nSend this exact command?\n" + sql, verb + " connection")) return;
                if (!Allowed()) return;
                // The normal execution lane also asks MainWindow.Production's PROD confirmation, records the exact
                // command/results in the original session, and never retries a state-changing RCL command.
                await ExecuteAsync(owner, sqlOverride:sql, label:verb + " RS connection");
            };
            toolbar.Children.Add(button);
        }
    }
}
