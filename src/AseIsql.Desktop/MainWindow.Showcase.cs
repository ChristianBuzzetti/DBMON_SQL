using System.Data;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AseIsql.Desktop;

// --smoke --showcase DIR: a scripted tour for the README animation. Synthetic data only, from a fictional DEMO_ASE
// server; no sql.ini, saved profile, appearance file or real connection is read or written. The window stays topmost
// at a fixed size, records its own windows into frames and stills, and exits when the tour ends.
public partial class MainWindow
{
    private static readonly string? ShowcaseDirectory = Environment.GetCommandLineArgs() is var args && Array.IndexOf(args, "--showcase") is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    private bool IsShowcase => isSmoke && ShowcaseDirectory != null;

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }

    private async Task RunShowcase()
    {
        Topmost = true; WindowState = WindowState.Normal; Left = 40; Top = 30; Width = 1400; Height = 860; UpdateLayout();
        await Task.Delay(1200);
        Directory.CreateDirectory(ShowcaseDirectory!);
        // Frames come from this process's own windows only, so nothing else on the screen can enter the recording.
        using var recorder = new ShowcaseRecorder(new System.Windows.Interop.WindowInteropHelper(this).Handle, Path.Combine(ShowcaseDirectory!, "frames"));
        // A fixed starting look in the showcase's own profile, so the tour opens the same way every time.
        if (bobertoDock != null)
        {
            try { await bobertoDock.Ready.WaitAsync(TimeSpan.FromSeconds(20)); await bobertoDock.EvaluateAsync("(()=>{const B=window.BOBERTO;B.setCharacter(null);B.setSkin('auto');B.setHatColor(null);B.setHat('auriculares');B.setFondo('mirai-noche');B.setBorde('neon');return true;})()"); await Task.Delay(800); }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or ObjectDisposedException) { }
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // Scene marks save the current frame as a still for the README.
        void Mark(string name) { recorder.Still(Path.Combine(ShowcaseDirectory!, name + ".png")); File.AppendAllText(Path.Combine(ShowcaseDirectory!, "marks.txt"), name + " " + clock.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + Environment.NewLine); }
        await Task.Delay(900);

        // 1 · Write and run a query.
        QueryClick(this, new RoutedEventArgs());
        await Glide(Editor, 0.3, 0.2);
        await TypeInto(text => Editor.Document.Insert(Editor.Document.TextLength, text),
            "-- Who is working on the server right now?\nselect spid, login = suser_name(suid), db = db_name(dbid),\n       cmd, status, blocked\nfrom master..sysprocesses\nwhere spid <> @@spid\norder by spid", 18);
        await Task.Delay(500);
        await Glide(ExecuteButton); await RunPressedAsync();
        await Task.Delay(1300); Mark("workspace"); await Task.Delay(1300);

        // 2 · A recipe from the library, with its :spid parameter.
        QueryClick(this, new RoutedEventArgs()); Editor.TextArea.Caret.Offset = Editor.Document.TextLength;
        await Task.Delay(400);
        var libraryButton = VisualChildren<Button>(CommandToolbar).First(b => VisualChildren<TextBlock>(b).Any(t => t.Text == "Recipes"));
        await Glide(libraryButton);
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(700);
            var library = Application.Current.Windows.OfType<Window>().First(w => w.Title.StartsWith("Library ·", StringComparison.Ordinal));
            var filter = VisualChildren<TextBox>(library).First(box => !box.IsReadOnly);
            await Glide(filter);
            await TypeInto(text => { filter.Text += text; filter.CaretIndex = filter.Text.Length; }, "locks", 90);
            await Task.Delay(500);
            var list = VisualChildren<ListBox>(library).Single();
            var target = list.Items.Cast<object>().FirstOrDefault(i => i.ToString()!.StartsWith("SPID lock details", StringComparison.Ordinal));
            if (target != null) { list.SelectedItem = target; list.ScrollIntoView(target); }
            await Task.Delay(700); Mark("recipes"); await Task.Delay(600);
            var insert = VisualChildren<Button>(library).First(b => Equals(b.Content, "Insert on a new line"));
            await Glide(insert);
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(700);
                var form = Application.Current.Windows.OfType<ParametersWindow>().First();
                var spid = form.Fields["spid"];
                await Glide(spid); spid.Focus();
                await TypeInto(text => { spid.Text += text; spid.CaretIndex = spid.Text.Length; }, "42", 160);
                await Task.Delay(600); Mark("parameters"); await Task.Delay(600);
                await Glide(form.Primary); form.Primary.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            });
            insert.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });
        LibraryClick(this, new RoutedEventArgs());
        await Task.Delay(900);
        await Glide(ExecuteButton); await ExecuteAsync(selectionOnly: true);
        await Task.Delay(2400);

        // 3 · Repeat on a timer, with its caution band.
        QueryClick(this, new RoutedEventArgs());
        var plan = new RepeatPlan("select spid, cmd, cpu, physical_io from master..sysprocesses where spid <> @@spid", TimeSpan.FromSeconds(5), 2, true, engine, "selection");
        await Glide(RunMenuButton); StartRepeat(plan);
        await Task.Delay(2600); Mark("repeat"); await Task.Delay(1600);
        await Glide(RepeatNowButton); RepeatNowClick(this, new RoutedEventArgs());
        for (int i = 0; i < 60 && repeat != null; i++) await Task.Delay(100);
        await Task.Delay(1500);

        // 4 · Boberto Studio: accessory, colors, background and frame, clicked in the Studio itself.
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(600);
            var window = Application.Current.Windows.OfType<PersonalizationWindow>().First();
            var studio = window.Studio!;
            for (int i = 0; i < 100; i++) { try { if (await studio.EvaluateAsync("!!(window.desktopStudio&&window.desktopStudio.facts().ready)") == "true") break; } catch (InvalidOperationException) { } await Task.Delay(100); }
            await studio.EvaluateAsync(StudioPicker);
            await Task.Delay(900);
            await StudioStep(studio, "Accessory", null, 500);
            await StudioStep(studio, "Crown", "window.BOBERTO.setHat('corona')", 1100); Mark("studio-crown");
            await StudioStep(studio, "Samurai", "window.BOBERTO.setHat('samurai')", 1100);
            await StudioStep(studio, "Accessory color", null, 400);
            await studio.EvaluateAsync("window.BOBERTO.setHatColor('#c0392b')"); await Task.Delay(900);
            await StudioStep(studio, "Body", null, 400);
            await studio.EvaluateAsync("window.BOBERTO.setSkinColor('#2b2f3a')"); await Task.Delay(900);
            await StudioStep(studio, "Background", null, 500);
            await StudioStep(studio, "Cyberpunk", "window.BOBERTO.setFondo('cyberpunk')", 1100);
            await StudioStep(studio, "Torii", "window.BOBERTO.setFondo('jp-torii')", 1100); Mark("studio-background");
            await StudioStep(studio, "Borders", null, 500);
            await StudioStep(studio, "Spark", "window.BOBERTO.setBorde('chispa')", 1200); Mark("studio-border");
            await StudioStep(studio, "Gestures", null, 400);
            await studio.EvaluateAsync("window.BOBERTO.playGesture('saluda')"); await Task.Delay(2200);
            window.Close();
        });
        await Glide(BobertoButton); OpenPersonalization(true);
        await Task.Delay(2200); // the header dock reloads with the look chosen in Studio

        // 5 · Personalize: a few of the 19 palettes, applied live.
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(900);
            var personalize = Application.Current.Windows.OfType<PersonalizationWindow>().First();
            foreach (string id in new[] { "dark", "synthwave", "forest", "neomirai", "pergamino" })
            {
                var tile = personalize.PaletteButtons[id]; tile.BringIntoView();
                await Glide(tile); tile.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await Task.Delay(700); Mark("palette-" + id); await Task.Delay(400);
            }
            await Task.Delay(500);
            personalize.Close();
        });
        await Glide(PersonalizeButton); OpenPersonalization();
        await Task.Delay(2200);
        recorder.Dispose();
        File.WriteAllText(Path.Combine(ShowcaseDirectory!, "done.txt"), recorder.Frames.ToString(CultureInfo.InvariantCulture));
        await Task.Delay(400);
        savedText = Editor.Text;
        Application.Current.Shutdown(0);
    }

    // Moves the real pointer to an element over a short ease, so the recording shows where each action happens.
    private async Task Glide(FrameworkElement target, double fx = 0.5, double fy = 0.5)
    {
        if (!target.IsVisible) return;
        await GlideTo(target.PointToScreen(new Point(target.ActualWidth * fx, target.ActualHeight * fy)));
    }
    private static async Task GlideTo(Point point)
    {
        GetCursorPos(out var start);
        const int steps = 18;
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps, ease = t < .5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            SetCursorPos((int)(start.X + (point.X - start.X) * ease), (int)(start.Y + (point.Y - start.Y) * ease));
            await Task.Delay(16);
        }
        await Task.Delay(180);
    }

    // Finds a visible Studio control by its label (tab or option), remembers it and returns its center in CSS pixels.
    private const string StudioPicker = """
        window.__pick = (text) => {
          const norm = s => (s || '').replace(/\s+/g, ' ').trim().toLowerCase(), want = norm(text);
          const all = [...document.querySelectorAll('button,[role=tab],[role=button],[title],[aria-label],li,label')].filter(e => e.offsetParent !== null);
          const label = e => [e.getAttribute('aria-label'), e.title, e.textContent].map(norm);
          const el = all.find(e => label(e).some(v => v === want)) || all.find(e => label(e).some(v => v.startsWith(want + ' ') || v.startsWith(want + '\n')));
          if (!el) return null;
          el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
          const r = el.getBoundingClientRect(); window.__picked = el;
          return JSON.stringify({ x: r.left + r.width / 2, y: r.top + r.height / 2 });
        };
        true
        """;

    // Glides the pointer to a Studio control and clicks it there; when the label is not found, the engine call shows
    // the same change so the recording still tells the story.
    private async Task StudioStep(BobertoStudioControl studio, string label, string? fallback, int holdMs)
    {
        string raw = await studio.EvaluateAsync("window.__pick(" + System.Text.Json.JsonSerializer.Serialize(label) + ")");
        string? json = raw == "null" ? null : System.Text.Json.JsonSerializer.Deserialize<string>(raw);
        if (json != null)
        {
            using var point = System.Text.Json.JsonDocument.Parse(json);
            var screen = studio.PointToScreen(new Point(point.RootElement.GetProperty("x").GetDouble(), point.RootElement.GetProperty("y").GetDouble()));
            await GlideTo(screen);
            await studio.EvaluateAsync("window.__picked && window.__picked.click()");
        }
        else if (fallback != null) await studio.EvaluateAsync(fallback);
        await Task.Delay(holdMs);
    }

    private static async Task TypeInto(Action<string> append, string text, int delayMs)
    {
        foreach (char ch in text) { append(ch.ToString()); await Task.Delay(ch == '\n' ? delayMs * 4 : delayMs); }
    }

    // Fictional server for the showcase: plausible ASE shapes, invented values, never a real connection.
    private sealed class ShowcaseEngine : IQueryEngine, ISessionFacts
    {
        public string Name => "ASE · DEMO_ASE:5000 · master";
        public SessionFacts Facts => new("master", 42, "dba_demo", "utf8", ServerPlatform.Ase);
        public QueryOutput Execute(string sql, CancellationToken cancellation = default)
        {
            Thread.Sleep(420); cancellation.ThrowIfCancellationRequested();
            var output = new QueryOutput();
            if (sql.Contains("monLocks", StringComparison.OrdinalIgnoreCase))
                output.Tables.Add(Table(["spid", "db", "obj_name", "locktype", "lockLevel", "PageNumber", "RowNumber"],
                    [[42, "sales", "orders", "Ex_row", "ROW", 10422, 7], [42, "sales", "order_items", "Sh_page", "PAGE", 88310, DBNull.Value], [42, "sales", "customers", "Sh_intent", "TABLE", DBNull.Value, DBNull.Value]]));
            else if (sql.Contains("sysprocesses", StringComparison.OrdinalIgnoreCase) && sql.Contains("cpu", StringComparison.OrdinalIgnoreCase))
                output.Tables.Add(Table(["spid", "cmd", "cpu", "physical_io"],
                    [[17, "SELECT", Random.Shared.Next(900, 1400), Random.Shared.Next(200, 600)], [23, "UPDATE", Random.Shared.Next(3000, 5200), Random.Shared.Next(900, 2400)], [31, "AWAITING COMMAND", 12, 0], [42, "INSERT", Random.Shared.Next(400, 700), Random.Shared.Next(60, 140)]]));
            else if (sql.Contains("sysprocesses", StringComparison.OrdinalIgnoreCase))
                output.Tables.Add(Table(["spid", "login", "db", "cmd", "status", "blocked"],
                    [[14, "etl_batch", "warehouse", "INSERT", "running", 0], [17, "report_app", "sales", "SELECT", "runnable", 0], [23, "billing_svc", "billing", "UPDATE", "lock sleep", 42],
                     [31, "dba_demo", "master", "AWAITING COMMAND", "recv sleep", 0], [42, "api_orders", "sales", "INSERT", "running", 0], [58, "backup_svc", "sybsystemdb", "DUMP DATABASE", "running", 0]]));
            else output.Messages.Add(new QueryMessage(0, "ASE", "Showcase: no synthetic result for this statement."));
            return output;
        }
        private static DataTable Table(string[] columns, object[][] rows)
        {
            var table = new DataTable();
            for (int c = 0; c < columns.Length; c++) { var type = rows.Select(r => r[c]).FirstOrDefault(v => v is not DBNull)?.GetType() ?? typeof(string); var column = table.Columns.Add("column_" + c, type); column.Caption = columns[c]; }
            foreach (var row in rows) table.Rows.Add(row);
            return table;
        }
    }
}
