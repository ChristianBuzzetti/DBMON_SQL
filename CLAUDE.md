# DBMON SQL — guía para Claude Code

## Stack
WPF desktop app, .NET 10 (SDK pinned in `global.json`), C#. SQL client for SAP ASE, SAP IQ and Replication Server over 64-bit ODBC. Editor: AvalonEdit. Boberto companion: WebView2 + JS assets.

## Layout
- `src/AseIsql.Core` — UI-free logic: SQL splitting (`SqlScript`), risk detection (`SqlRisk`), profiles (`ConnectionProfile`), server groups, multi-run, diff, formats, history.
- `src/AseIsql.Odbc` — `OdbcSession` (execution, messages, plan/IO), result reading.
- `src/AseIsql.Desktop` — `MainWindow` split in partials (`MainWindow.<Area>.cs`), `ConnectionWindow`, `PersonalizationWindow`, `Themes.cs` + `Assets/dbmon-palettes.json` (every palette must define every key in `Themes.Keys`), recipes JSON in Assets. `*Smoke.cs` and `MainWindow.Showcase.cs` = opt-in UI smoke checks (`--smoke`, Windows only); excluded from default builds.
- `tests/AseIsql.Checks/Program.cs` — core checks harness (`Check("name", () => ...)`), no server needed.
- `docs/` (user-guide, architecture, building), `CHANGELOG.md`, version only in `Directory.Build.props`.

## Build / check (Linux box)
```
dotnet build src/AseIsql.Desktop -c Release -p:EnableWindowsTargeting=true
dotnet run --project tests/AseIsql.Checks -c Release -p:EnableWindowsTargeting=true
```
The core harness includes scripted OdbcSession checks with no ODBC driver or server. The optional native IQ TLS check prints an INFO skip when the Windows registry or driver is unavailable.

On Windows: `./build.ps1 -Test [-Publish]`. Publishing verifies a separate smoke executable under `artifacts/ui-smoke/win-x64`, then publishes the distribution with `Smoke=false`.

## Opt-in UI smoke checks (Windows)
```
dotnet build src/AseIsql.Desktop -c Release -p:Smoke=true
& 'src/AseIsql.Desktop/bin/Release/net10.0-windows/DBMON SQL.exe' --smoke --report out.json
```
`Smoke=true` defines `DBMON_SMOKE` and includes all UI smoke partials and the showcase. A default Release build/publish excludes those sources, fixtures and hooks; `--smoke` prints `smoke checks not included in this build` and exits with code 1 before opening the window or touching user data. Rebuild with `-p:Smoke=false` before distributing. See [docs/building.md](docs/building.md).

## ODBC seam
`IOdbcConnection` / `IOdbcCommand` are internal driver boundaries; `NativeOdbcConnection` forwards real ODBC calls and native exceptions unchanged. The checks assembly has friend access to inject `ScriptedOdbcConnection`, which scripts SQL, result sets, info diagnostics, failures and read/cancel timing while exercising the actual session and result reader. Keep fake implementations in tests.

## Restricted sandboxes
When instructed that build/run cannot work (network or MSBuild named pipes), do not attempt them. Review source signatures and finish with `git diff --check`; the parent runs build/checks.

## Conventions
- Match existing style: compact C#, partial classes, theme colors via `DynamicResource`/palette keys, never hardcoded colors.
- New behavior gets a core `Check` (or smoke check if it is UI-only).
- Keep user-visible strings in English (app UI language). Update CHANGELOG/user-guide for user-facing changes.

## Limits
- Never run git commit/push/tag, open PRs or publish releases; the maintainer does that.
- Do not touch credentials, user profiles/data (`%LOCALAPPDATA%/DBMON_SQL`), lock files or package versions.
- Do not run long self-verification loops; one build + checks run at the end is enough.
