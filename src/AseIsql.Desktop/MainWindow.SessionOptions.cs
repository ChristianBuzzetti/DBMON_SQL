using System.Windows;
using System.Windows.Controls;
using AseIsql.Odbc;

namespace AseIsql.Desktop;

// SAP ASE session options: noexec, statistics io, statistics time and compatibility_mode, sent as SET statements on the
// open session. One builder feeds both the Tools submenu and the status-bar chip, so there is a single description of
// what each option does and a single place that decides the next state.
public partial class MainWindow
{
    // What this window has sent on the current session. A new session starts at the default, because the SET options
    // of the previous one went with it.
    private AseSessionOptions aseOptions = AseSessionOptions.Default;

    private bool AseOptionsAvailable => engine is OdbcSession { IsOpen: true, Platform: ServerPlatform.Ase } && running == null && !changingSession;

    private List<object> AseOptionItems()
    {
        var items = new List<object>();
        void Toggle(string header, bool isChecked, AseSessionOptions next, string tip)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked, ToolTip = tip, IsEnabled = AseOptionsAvailable };
            item.Click += (_, _) => ApplyAseOptions(next);
            items.Add(item);
        }
        Toggle("No exec · compile without running", aseOptions.NoExec, aseOptions with { NoExec = !aseOptions.NoExec },
            "set noexec on/off · the server parses, compiles and optimizes every statement and executes none. Results stop arriving until it is off again.");
        items.Add(new Separator());
        Toggle("Statistics IO", aseOptions.StatisticsIo, aseOptions with { StatisticsIo = !aseOptions.StatisticsIo },
            "set statistics io on/off + set plan for show_execio_xml to message on · per-table logical and physical reads from the executed plan, in Messages and in a Statistics IO tab.");
        Toggle("Statistics time", aseOptions.StatisticsTime, aseOptions with { StatisticsTime = !aseOptions.StatisticsTime },
            "set statistics time on/off · parse, compile and execution time, as server messages.");
        items.Add(new Separator());
        var compatibility = new MenuItem { Header = "Compatibility mode", IsEnabled = AseOptionsAvailable, ToolTip = "set compatibility_mode on/off · the session starts at the server's own setting, which DBMON SQL does not read and cannot restore once you choose here." };
        void Compatibility(string header, CompatibilityMode mode)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = aseOptions.Compatibility == mode, IsEnabled = AseOptionsAvailable };
            item.Click += (_, _) => ApplyAseOptions(aseOptions with { Compatibility = mode });
            compatibility.Items.Add(item);
        }
        Compatibility("On", CompatibilityMode.On);
        Compatibility("Off", CompatibilityMode.Off);
        items.Add(compatibility);
        items.Add(new Separator());
        // Compatibility mode is left where it is: the default is the server's, and no SET statement brings it back.
        var reset = new MenuItem { Header = "Turn all off", IsEnabled = AseOptionsAvailable && !aseOptions.IsDefault, ToolTip = "Returns noexec and both statistics to off. Compatibility mode stays where you set it." };
        reset.Click += (_, _) => ApplyAseOptions(AseSessionOptions.Default with { Compatibility = aseOptions.Compatibility });
        items.Add(reset);
        return items;
    }

    // SubmenuOpened bubbles, so the compatibility submenu opening would re-enter here and rebuild — that is, destroy —
    // the very item the mouse is on, closing the whole menu. Only the top item's own opening rebuilds it.
    private void AseOptionsOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, AseOptionsMenu)) return;
        AseOptionsMenu.Items.Clear();
        foreach (var item in AseOptionItems()) AseOptionsMenu.Items.Add(item);
    }

    private void SetOptionsClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SetOptionsButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        foreach (var item in AseOptionItems()) menu.Items.Add(item);
        menu.IsOpen = true;
    }

    // The menu item has already flipped its own check mark by the time this runs, so every exit path refreshes the
    // menu and the chip from aseOptions: what the window shows is what the server acknowledged, never what was clicked.
    private void ApplyAseOptions(AseSessionOptions next) => RunCatalogAction(async () => {
        if (engine is not OdbcSession { IsOpen: true, Platform: ServerPlatform.Ase })
        { RefreshAseOptions(); StatusText.Text = "Session options apply to a connected SAP ASE session."; return; }
        var script = AseSessionOptions.Script(aseOptions, next);
        if (script.Count == 0) { RefreshAseOptions(); return; }
        string sql = string.Join(Environment.NewLine + "go" + Environment.NewLine, script);
        var output = await ReadCatalog(sql, "Session options");
        if (output == null || output.Error != null) { RefreshAseOptions(); return; }
        aseOptions = next;
        AddMessage(0, "CLIENT", "Session options · " + string.Join(" · ", script));
        if (next.NoExec) AddMessage(0, "CLIENT", "NOEXEC is on: until you turn it off, the server compiles every script and runs none.");
        if (next.StatisticsSilencedByNoExec) AddMessage(0, "CLIENT", "Statistics IO and statistics time measure execution, so they report nothing while NOEXEC is on.");
        RefreshAseOptions();
        StatusText.Text = next.Tags.Count == 0 ? "Session options · all off" : "Session options · " + string.Join(" · ", next.Tags);
    }, "Session options");

    private void RefreshAseOptions()
    {
        bool ase = engine is OdbcSession { IsOpen: true, Platform: ServerPlatform.Ase };
        // Only the options that make ASE talk after the last row pay for the extra call; everyone else gets none.
        if (engine is OdbcSession session)
        {
            session.TrailingFlush = ase ? aseOptions.TrailingFlushStatement : null;
            session.CaptureExecutionIo = ase && aseOptions.CapturesExecutionIo;
        }
        AseOptionsMenu.IsEnabled = AseOptionsAvailable;
        AseOptionsMenu.ToolTip = ase
            ? "SET options on this session only · they are gone when it closes, and multi-server and repeat runs open their own sessions"
            : "Connect to SAP ASE to set session options";
        var tags = aseOptions.Tags;
        SetOptionsButton.Visibility = ase && tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetOptionsText.Text = string.Join(" · ", tags);
        SetOptionsText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, aseOptions.NoExec ? "ErrorInk" : "Accent");
        SetOptionsButton.ToolTip = (aseOptions.NoExec ? "NOEXEC is on: scripts are compiled, not executed. " : "")
            + "SAP ASE session options in force · click to change";
    }
}
