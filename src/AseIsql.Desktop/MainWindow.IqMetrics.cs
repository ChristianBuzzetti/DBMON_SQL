using System.Data;
using System.Windows;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private void IqMetricsClick(object sender, RoutedEventArgs e) => OpenMonitor(ServerPlatform.Iq, "IQ metrics", IqMetrics.Commands, ParseIqMonitorTables);
    private static IReadOnlyList<(string Label, DataTable Table)> ParseIqMonitorTables(int index, DataTable table) => index switch {
        0 => new[] { ("Key / value", IqMetrics.Status(table)) },
        1 => new[] { ("Dbspace usage", IqMetrics.Dbspaces(table)) },
        2 => new[] { ("Connections", IqMetrics.Connections(table)) },
        3 => new[] { ("Summary by state", IqMetrics.TransactionSummary(table)), ("Transaction details", table.Copy()) },
        _ => throw new ArgumentException("Unknown IQ metric.")
    };
}
