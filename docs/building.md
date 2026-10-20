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

`-Publish` writes a self-contained package to `artifacts/windows-x64-dev`, then runs the packaged app with `--smoke` and stops if any UI check fails. It refuses to overwrite a package that is running. `build-info.json` in the package records the version, build time, check counts and assembly hashes.

The product version lives only in [`Directory.Build.props`](../Directory.Build.props). Formal releases use `./build.ps1 -Test -Publish -Release`, which writes `artifacts/windows-x64-v<version>-final` and keeps the current and previous release.

## Checks

| Suite | Command | What it covers |
|---|---|---|
| Core checks | `./build.ps1 -Test` (runs `tests/AseIsql.Checks`) | SQL splitting, parameters, risk detection, result reading and limits, formats and exports, `sql.ini` parsing, connection strings per platform, profiles, recipes, multi-server combining and diffs. No server is contacted. |
| UI smoke | `"DBMON SQL.exe" --smoke --report out.json` (run by `-Publish`) | About 250 checks on the real window: keyboard selection, palettes, personalization, Boberto dock and Studio, results, copies, repeat, parameters, login form per platform, explorer SQL builders. Uses a synthetic in-memory engine; it never opens a server connection. Needs the SAP ASE ODBC driver installed (it checks detection) and WebView2. |

Smoke screenshots are written next to the executable in `UiSmoke/`.

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
| `--smoke --report PATH` | Run the UI checks with synthetic data and write a JSON report. |
| `--smoke --showcase DIR` | Play the scripted README tour with a fictional `DEMO_ASE` server and record frames of the app's own windows to `DIR`. |
| `--ui-preview` | Open the window without the login, for visual inspection. |
| `--startup-report PATH` | Measure time to first render and exit. |

Synthetic data exists only behind `--smoke`; normal starts always begin with the login.

## Recording the README tour

```powershell
./build.ps1
./scripts/record-showcase.ps1
```

The showcase types a query, runs it, inserts a recipe with a parameter, repeats a query on a timer and cycles palettes, moving the pointer as it goes (about 45 seconds). Frames are composed only from DBMON SQL's own windows with `PrintWindow`, so other applications on screen can never appear in the output. The script writes `docs/media/dbmon-sql-tour.gif`, `dbmon-sql-tour.mp4` and one still per scene.

## Continuous integration

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) builds the solution and runs the core checks on `windows-latest`. The UI smoke checks need the SAP ODBC driver and a desktop session, so they run locally through `-Publish`.
