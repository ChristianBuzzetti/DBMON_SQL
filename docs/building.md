# Building and testing

## Toolchain

- Windows 10/11 x64.
- .NET SDK **10.0.402** or a later 10.0 patch (pinned in [`global.json`](../global.json)). Install it system-wide, or put a private copy in `.tools/dotnet` (ignored by git); `build.ps1` prefers `.tools/dotnet` when it exists.
- PowerShell 5.1 or later.
- For the UI smoke checks and real connections: the 64-bit SAP ASE ODBC driver (`Adaptive Server Enterprise`); the SAP IQ ODBC driver is optional.
- For the README recording only: [ffmpeg](https://ffmpeg.org/) on `PATH`.

NuGet restores use lock files (`packages.lock.json`), so builds are repeatable.

## Build script

```powershell
./build.ps1              # Release build
./build.ps1 -Test        # + core checks (no server, no ODBC driver needed)
./build.ps1 -Test -Publish   # + self-contained win-x64 package and UI smoke checks
```

`-Publish` first publishes an opted-in smoke executable to `artifacts/ui-smoke/win-x64`, runs it with `--smoke`, and stops if any UI check fails. It then publishes a self-contained development package with `Smoke=false` to `C:\DBMON_SQL`. This is the stable local execution directory selected by the maintainer. Close DBMON SQL before rebuilding: the script refuses to overwrite a package that is running. `build-info.json` in the package records the version, build time, check counts from the separate smoke build, and assembly hashes of the final distribution.

Default builds and publishes exclude UI smoke sources, fixtures, hooks and the showcase. Passing `--smoke` to such a build prints `smoke checks not included in this build` and exits with code 1 before opening the window or accessing settings. To include them explicitly on Windows:

```powershell
dotnet build src/AseIsql.Desktop -c Release -p:Smoke=true
& 'src/AseIsql.Desktop/bin/Release/net10.0-windows/DBMON SQL.exe' --smoke --report out.json
# A standalone smoke package can also be produced in a separate directory:
dotnet publish src/AseIsql.Desktop -c Release -r win-x64 --self-contained true -p:Smoke=true -o artifacts/ui-smoke/win-x64
```

The `Smoke=true` build property defines `DBMON_SMOKE` and includes `MainWindow.*Smoke.cs` and `MainWindow.Showcase.cs`. Rebuild with `-p:Smoke=false` before sharing a directly built executable. The publish script does this automatically for its final package.

## Live test (Smoke build, non-prod only)

Use the opted-in Smoke executable on the maintainer's Windows laptop with the SAP ASE x64 ODBC driver and access to NON-PROD ASE 16. Save the login and its password in DBMON SQL first, under the same Windows account that will run the test (profiles use DPAPI). No isql, external SQL runner or scripts are needed.

```powershell
& 'artifacts/ui-smoke/win-x64/DBMON SQL.exe' --smoke --live-test `
  --profile 'chbuzzetti @ ASEDBDESA_SEG' `
  --allow ASEDBDESA_SEG,ASEDBDESA_SMMP,ASEDBQA_SEG,ASEDBQA_SMMP `
  --report C:\temp\live.json
$LASTEXITCODE
```

Live mode dispatches before constructing the main window and does not run the synthetic smoke suite. It reads the real `%LOCALAPPDATA%\DBMON_SQL\profiles.dat`, `health-check.json` and `health-rules.json` without saving, migrating or modifying them. Settings, history and session restore are untouched. A profile name must match exactly one saved name (case-sensitive); its alias, or server when the alias is empty, must occur in the case-insensitive `--allow` list. Non-ASE profiles, profiles marked Production and profiles without a saved password are refused with exit code **2**, a console message and a JSON report when the destination is writable. The report must be outside both current and legacy app-data directories. Passwords are redacted from all reported detail; credentials and connection strings are never serialized.

The runner records PASS/FAIL/SKIP, detail and elapsed milliseconds for each check, checkpointing JSON after every result. Exit code **0** means every check passed or skipped; **1** means a failed check, deadline or report write. The report's `phase` is `finished` and `exitCode` is set only after cleanup; an interrupted run can leave a `running` report with a null exit code. Console lines contain the same redacted results. Live mode preserves redirected stdout or attaches the launching console when one exists; JSON is the durable record when launched without a console.

Checks cover connection identity, activity parsing, an A/B/C blocking pair and tree, SQL text and locks, duplicate-session database/SPID, every health cell under the user's non-PROD rules (defaults when absent/invalid), estimated and actual operator trees, MDA deltas, live PRINT timing, streaming export cancellation and reuse of the same session, explorer catalog SQL, generated test-table DDL, and the pure PROD SQL-risk guard. Health WARN cells count as PASS with their observed severity in the detail; ignored/disabled cells are SKIP. A separate pure check verifies that non-PROD defaults ignore Last log dump. MDA probes are SKIP when monitoring is unavailable or permissions are denied; other query failures are FAIL.

Only the runner's own `tempdb..dbmon_lt_<A spid>` table is created, inserted, updated and dropped. A pre-existing table with that name is refused and left untouched. DDL is generated before the transaction begins. The runner never executes KILL; it only reports `ActivityMonitor.KillDenied` for B. Transactions are rolled back and the owned table is dropped in `finally`, with cleanup failures recorded. The export uses `OdbcSession.Export` and `QueryExportWriter`, SQL `TOP 10000` to cap rows, and a trailing five-second WAITFOR to preserve a cancellation window on small catalogs. It cancels after about one second, probes `select 1` on that same connection, and removes its temporary partial CSV.

Optional controls:

| Switch | Default | Behavior |
|---|---|---|
| `--block-seconds N` | `75` | Seconds B must still be waiting, measured from entry into its background query; range 1–3600. Short runs do not cover the former 60-second regression. |
| `--max-minutes N` | `10` | Global cancellation deadline; range 1–60. Remaining checks are skipped and the run fails on timeout. |
| `--include-sysmon` | Off | Run the one-minute `sp_sysmon` sample and require indexed sections. Requires `sa_role`; otherwise this check is SKIP by default. |

B receives the saved/migrated profile timeout with **no override**. A separate check expects `QueryTimeout = 0`, the 1.0.1 default; an explicitly saved nonzero timeout causes that check to fail and is never changed on disk. Most diagnostic queries have a ten-second statement timeout. Health uses its saved server timeout. Global cancellation uses the app's cooperative ODBC command cancellation; native login has no cancellation overload and remains bounded by the saved login timeout. Cleanup receives separate ten-second cancellation budgets. As in the UI, sessions stay owned until native commands return; a driver that does not honor cancellation can delay shutdown beyond the requested deadline.

### Share the app

Use **`C:\DBMON_SQL`** as the single development distribution folder. After successful publication, the build script removes generated `UiSmoke/` and `BobertoSmoke/` output, root debug symbols (`*.pdb`) and WebView2 API documentation (`Microsoft.Web.WebView2.*.xml`). Application DLLs, runtime files, language folders, assets and font licenses stay in place. `README.txt` explains recipient requirements and `LICENSE.txt` includes the MIT license.

Zip that entire folder. Recipients extract it and run `DBMON SQL.exe`; sending only the executable is insufficient. They need Windows x64, their own SAP ODBC x64 driver, server access and credentials. WebView2 is required for Boberto; .NET is included. Do not share `%LOCALAPPDATA%/DBMON_SQL`, which contains personal settings and profiles. See the [requirements](../README.md#requirements).

To clean an existing verified package without rebuilding:

```powershell
./scripts/prepare-distribution.ps1
./tests/verify-distribution.ps1   # Isolated packaging regression checks
./tests/verify-build-path.ps1     # Default paths, overrides and running-package guard
```

The preparation script checks recorded assembly hashes before making changes and refuses a running package. It does not publish a second copy or change the product version. The optional `-PackageDirectory PATH` accepts an existing verified package, including a formal release directory. Unknown files are preserved; this is targeted cleanup, not a full audit of arbitrary files added manually.

The product version lives only in [`Directory.Build.props`](../Directory.Build.props). Formal releases use `./build.ps1 -Test -Publish -Release`, which writes `artifacts/windows-x64-v<version>-final` and keeps the current and previous release.

## Checks

| Suite | Command | What it covers |
|---|---|---|
| Core checks | `./build.ps1 -Test` (runs `tests/AseIsql.Checks`) | SQL splitting, parameters, risk detection, result reading and limits, formats and exports, `sql.ini` parsing, connection strings per platform, profiles, recipes, multi-server combining and diffs. Scripted ODBC checks exercise the real `OdbcSession` without a driver: facts, result sets, bounded/login/error diagnostics, live progress, textsize per session, continue-on-error, `go N`, cancellation and limits. No server is contacted. |
| UI smoke | `"DBMON SQL.exe" --smoke --report out.json` (requires `Smoke=true`; run separately by `-Publish`) | About 250 checks on the real window: keyboard selection, palettes, personalization, Boberto dock and Studio, results, copies, repeat, parameters, login form per platform, explorer SQL builders. Uses a synthetic in-memory engine; it never opens a server connection. Needs the SAP ASE ODBC driver installed (it checks detection) and WebView2. |

Smoke runs write screenshots next to the opted-in executable in `UiSmoke/` and Boberto fixtures in `BobertoSmoke/`. With `-Publish`, these stay under the separate `artifacts/ui-smoke/win-x64` directory; the JSON report remains at `artifacts/smoke-latest.json`. The final distribution excludes smoke code. The optional native IQ TLS parser check prints an `INFO` skip when the Windows registry is unavailable (including non-Windows hosts) or the IQ driver is absent.

In a restricted sandbox where .NET restore/build or MSBuild named pipes are unavailable, do not attempt build/run. Review source signatures and use `git diff --check`; run the build and checks in the parent environment.

### Workspace regression checks (2026-10-20)

`./build.ps1 -Test -Publish` passed 82 core checks and 281 UI checks for the 0.1.0 development package.
The new checks cover literal database underscores and constrained popup scrolling, four-DIP tab-plate gaps,
numbering after the final result closes (including sole-run replacements), timestamped history reload and
malformed-file preservation, and raw multiline SQL copying while explicit delimited formats keep escaping.
History fixtures use unique system-temporary directories, never the user's saved history.
Window captures include Query/Result selection and the dated history dialog. No ASE query was executed.
Native clipboard transfer is not asserted by these checks; they verify the text producer used by Copy/Ctrl+C.

## Command-line switches

| Switch | Purpose |
|---|---|
| `--smoke --report PATH` | Requires `Smoke=true`. Run the UI checks with synthetic data and write a JSON report. |
| `--smoke --live-test --profile NAME --allow ALIASES --report PATH` | Requires `Smoke=true`. Run live checks using a saved non-PROD ASE profile; see the live-test section above. |
| `--smoke --showcase DIR` | Requires `Smoke=true`. Play the scripted README tour with a fictional `DEMO_ASE` server and record frames of the app's own windows to `DIR`. |
| `--ui-preview` | Open the window without the login, for visual inspection. |
| `--startup-report PATH` | Measure time to first render and exit. |

Synthetic UI data exists only in opted-in builds behind `--smoke`; normal starts always begin with the login.

## Recording the README tour

```powershell
dotnet build src/AseIsql.Desktop -c Release -p:Smoke=true
./scripts/record-showcase.ps1
```

The showcase types a query, runs it, inserts a recipe with a parameter, repeats a query on a timer and cycles palettes, moving the pointer as it goes (about 45 seconds). Frames are composed only from DBMON SQL's own windows with `PrintWindow`, so other applications on screen can never appear in the output. The script writes `docs/media/dbmon-sql-tour.gif`, `dbmon-sql-tour.mp4` and one still per scene.

## Continuous integration

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) builds the solution and runs the core checks on `windows-latest`. The UI smoke checks need the SAP ODBC driver and a desktop session, so they run locally in a separate opted-in build through `-Publish`.

## Type fidelity against the server

[`docs/type-fidelity-sweep.sql`](type-fidelity-sweep.sql) asks the server for the boundary value of every ASE
type: numerics wider than `decimal`, unsigned integers above the signed maximum, fractions of a second, padded
characters, binary with leading zero bytes and NULL in each type. Run it in DBMON SQL and in ASE ISQL and compare
the two outputs cell by cell; anything rounded, blank, shortened or shown as a repeated value on one side is a
finding. The sweep reads constants and the system catalog only, and writes nothing.

The core checks cover the client half of this with fixtures (`ResultReader`, `ResultFormats`), but only the sweep
shows what the SAP driver actually hands over for a given server version.

### Characters, and where they are lost

Character data is converted to the session's charset on its way back from the server, and a console applies its
own codepage after that, so a row of question marks does not say who lost the characters. The sweep's
`unicode as bytes` batch settles it: binary is converted by nobody, so its hex is what the server holds.

Measured on 30/10/2026 against ASEDB1, with the same batch in DBMON SQL and in ASE ISQL started with `-J utf8`.
Neither client is at fault, and the loss is not on the way back: `convert(varbinary(8), convert(univarchar(4),
'漢字'))` returns `0x001A001A` through the ODBC driver and `0x003f003f` through ISQL, which are the substitution
characters of each path. The accented Latin characters of the same string came through intact in both. So the
ideographs were already replaced before the server stored them, and the counts that looked reassuring —
`char_length` 2, `datalength` 4 — were counting two substitution characters.

The cause is upstream of the client: a character literal in a query is ordinary character data, which ASE
converts to the server's default charset before anything becomes `univarchar`. Where that default cannot
represent a character, the server has nothing left to store. A `univarchar` column can hold the character; a
literal typed into a query cannot carry it there. So this is a property of the server's charset, and no Charset
choice at login changes it: `-J utf8` did not.

Reading a character already stored as Unicode is a different path, and the one that matters for real data. The
sweep's `unicode from bytes` batch isolates it by building the value from its code points as binary, which
carries no charset, so it reaches the column intact. Measured the same day: `convert(univarchar(2), 0x6F225B57)`
reads as 漢字 in DBMON SQL, and `0x6F225B57` comes back unchanged, so the server keeps UTF-16 in that order and
the trip back loses nothing. The same batch in ASE ISQL shows `??`.

So stored Unicode is safe here, and a `univarchar` column reads in DBMON SQL what ISQL cannot show. What no
client can do is carry a character through a query literal that the server's default charset cannot represent.

### A known limit: the fraction of a second of `time`

Measured on 30/10/2026 against ASEDB1 with the SAP ASE ODBC driver: `datepart(ms, convert(time, '16:30:45.123'))`
returns 123, so the server holds the fraction, while the column itself arrives as `16:30:45`. The reader asks the
driver for that column's own text before anything converts the cell, and the text has no fraction either, so the
fraction is already gone when the driver answers. `bigtime` keeps its microseconds through the same path, which
points at the described type rather than at the driver's conversion: ODBC's `SQL_TYPE_TIME` has no
fractional-seconds field, so neither its typed value nor its character conversion can carry one.

That reading is not proven. Running the same batch in ASE ISQL, which reaches the server through CT-Lib instead of
ODBC, settles it: a fraction there confirms the loss belongs to the ODBC layer, and no fraction moves it into the
server's own `time` handling. Nothing in the client can recover a fraction the driver never delivers; when it
matters, read the column as `bigtime` or have the server render it, as the sweep's `datepart` columns do.
