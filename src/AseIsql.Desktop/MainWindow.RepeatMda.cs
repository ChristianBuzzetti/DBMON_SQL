using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AseIsql.Desktop;

// MDA deltas in Repeat: when the user enables "MDA deltas (cumulative counters)" and the SQL reads a cumulative MDA
// table (monSysWaits, monOpenObjectActivity, etc.), this replaces the raw snapshot with a delta table showing what
// changed since the last run, plus a bar chart of top deltas.
public partial class MainWindow
{
    // Process results from a repeat run with MDA deltas enabled. Returns the tables to publish (original or delta).
    private IReadOnlyList<DataTable> ProcessRepeatMdaDeltas(SessionWorkspace owner, IReadOnlyList<DataTable> tables)
    {
        var plan = owner.Repeat;
        if (plan == null || !plan.MdaDeltas) return tables;
        // Only ASE supports MDA deltas. IQ and RS: pass through unchanged.
        if (owner.Engine is not ISessionFacts { Facts.Platform: ServerPlatform.Ase }) return tables;
        // Only process the first result (typical MDA queries return one result set).
        if (tables.Count == 0) return tables;
        var current = tables[0];
        var shape = MdaDelta.Detect(current, plan.Sql);
        if (shape == null)
        {
            // Not an MDA snapshot: clear previous and pass through.
            owner.RepeatMdaPrevious = null;
            return tables;
        }
        // First run: save and show raw snapshot.
        if (owner.RepeatMdaPrevious == null)
        {
            owner.RepeatMdaPrevious = current.Copy();
            return tables;
        }
        // Compute deltas: previous → current.
        DataTable delta;
        try { delta = MdaDelta.Compute(owner.RepeatMdaPrevious, current, shape); }
        catch (ArgumentException ex)
        {
            // Schema mismatch or other error: reset and show raw snapshot.
            AddMessage(owner, 1, "CLIENT", "MDA deltas: " + ex.Message + " Showing raw snapshot; deltas restart next run.");
            owner.RepeatMdaPrevious = current.Copy();
            return tables;
        }
        owner.RepeatMdaPrevious = current.Copy();
        // Build result list: delta table, then chart if there are deltas, then remaining original tables (if any).
        var results = new List<DataTable> { delta };
        // Top-N bar chart of deltas (reusing ChartResult pattern, but as a DataTable artifact).
        var chartColumn = MdaDelta.ChartColumn(delta);
        if (chartColumn != null && delta.Rows.Count > 0)
        {
            var chart = BuildMdaDeltaChart(delta, chartColumn, shape.Table);
            if (chart != null) results.Add(chart);
        }
        // Include remaining original tables beyond the first (rare, but don't drop them).
        for (int i = 1; i < tables.Count; i++) results.Add(tables[i]);
        return results;
    }

    // Builds a top-N bar chart of MDA deltas as a DataTable for display. Returns null if no deltas worth charting.
    private DataTable? BuildMdaDeltaChart(DataTable delta, string counterColumn, string? tableName)
    {
        int statusIndex = delta.Columns.IndexOf(MdaDelta.StatusColumn);
        int counterIndex = delta.Columns.IndexOf(counterColumn);
        if (statusIndex < 0 || counterIndex < 0) return null;
        // Collect rows with non-zero deltas: (label, value).
        var rows = new List<(string Label, decimal Value)>();
        foreach (DataRow row in delta.Rows)
        {
            if (row[counterIndex] is DBNull) continue;
            decimal value = Convert.ToDecimal(row[counterIndex], CultureInfo.InvariantCulture);
            if (value == 0) continue;
            // Label: concatenate key columns (everything before delta_status).
            var keyParts = new List<string>();
            for (int i = 0; i < statusIndex; i++)
            {
                var cell = row[i];
                if (cell is not DBNull) keyParts.Add(Convert.ToString(cell, CultureInfo.InvariantCulture) ?? "");
            }
            string label = string.Join(" · ", keyParts.Where(p => p.Length > 0));
            if (label.Length == 0) label = "(no key)";
            // Append status if not normal (new/gone/reset).
            string status = Convert.ToString(row[statusIndex], CultureInfo.InvariantCulture) ?? "";
            if (status.Length > 0) label += $" [{status}]";
            rows.Add((label, value));
        }
        if (rows.Count == 0) return null;
        // Top 15 by absolute value.
        var top = rows.OrderByDescending(r => Math.Abs(r.Value)).Take(15).ToList();
        var chart = new DataTable("MDA deltas · " + counterColumn);
        chart.ExtendedProperties["ResultHeader"] = "MDA deltas · " + counterColumn + (tableName != null ? " · " + tableName : "");
        chart.Columns.Add("Row", typeof(string));
        chart.Columns.Add(counterColumn, typeof(decimal));
        foreach (var (label, value) in top) chart.Rows.Add(label, value);
        return chart;
    }
}
