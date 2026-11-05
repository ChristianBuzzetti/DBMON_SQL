# Changelog

All notable changes to DBMON SQL are listed here. The project follows [Semantic Versioning](https://semver.org/).

## [1.0.8] - 2026-11-05

### Added
- Activity monitor: **Only active** toggle hides idle sessions (sleeping and AWAITING COMMAND); blockers and blocked sessions stay visible. The choice is remembered. The footer now reads "N shown of M processes".
- Access keys on the session-tab menu (Connect `C`, Disconnect `I`, Duplicate `D`, Close `L`) and the monitor grid menu (Copy `C`, Duplicate `D`).
- Activity monitor: on-demand `dbcc sqltext` fallback when MDA SQL text is empty or unavailable, with a source label, **Refresh SQL**, and cached results reused during timer refreshes. Permission denials are remembered until **Retry MDA**.
- Activity monitor: **Plan** tab with **Get plan (sp_showplan)** on the dedicated monitor connection; plans are fetched only on request, with clear idle-process and permission messages.
- Live test: `dbcc sqltext (own spid)` validates SQL text from server messages and skips logins without `sa_role` or `sybase_ts_role`.

### Fixed
- "1 process" pluralized as "2 processs"; nouns ending in s, x, z, ch and sh now take "es".

## [1.0.7] - 2026-11-04

### Fixed
- Estimated plan returned an empty plan for any statement already in the ASE statement cache (the cached plan is reused without optimization, so `show_final_plan_xml` writes nothing); plan capture now runs with `set statement_cache off` and restores it. The 1.0.4-1.0.6 live-test failures came from this (the live test always compiles the same text).

### Changed
- The live-test estimated-plan check records every command, read-back cell and server message, and on failure checks the statement cache size and a cache-busting compile.

## [1.0.6] - 2026-11-04

### Fixed
- Estimated plan regression since 1.0.4 (empty `showplan_in_xml` buffer): `set flushmessage on` at login (1.0.4) and the 1.0.5 bracket are removed; the plan path is again exactly the 1.0.3 command sequence, guarded by a core check with a scripted engine.

### Changed
- Server messages: the SAP ASE ODBC driver delivers PRINT/info messages when the batch completes, so they appear then (DBMON SQL publishes each one as soon as the driver raises it). Live-test message checks report SKIP instead of FAIL.

### Note
- Health Data usage now covers all databases (1.0.5 bigint fix); a high used % is a real finding.

## [1.0.5] - 2026-11-04

### Fixed
- Estimated plan returned an empty `showplan_in_xml` buffer after 1.0.4: with `set flushmessage on`, ASE flushes the plan message immediately. `flushmessage` is now bracketed around plan capture: the estimated plan starts with `set flushmessage off` and restores `set flushmessage on` once noexec is off, and enabling Statistics IO sends `set flushmessage off` (disabling it sends `set flushmessage on`). Login still runs `set flushmessage on` on ASE.
- Health check *Data usage* failed on large ASE servers with "Truncation error occurred. Command has been aborted.": the `numeric(20,0)` page sums divided inside `100.0 * (1.0 - a/b)` exceeded ASE numeric precision. Data usage, Log usage and User connections now sum pages as `bigint`, compute the percentage in `float` with a `nullif` divisor and return it as `numeric(9,2)`; a negative `lct_admin('logsegment_freepages')` result is treated as unknown. Table details size uses `bigint`, and the space/log recipes compute percentages in `float` with guarded divisors. New core checks forbid `numeric(20,0)` in health probes and require the bounded percentage cast.

### Changed
- Live messages: the SAP ASE ODBC driver delivers PRINT only when `SQLExecute`/`SQLMoreResults` returns, so messages arrive live between batches and result sets, while a PRINT inside a single batch with no result set arrives when the batch completes. The user guide now says so. The live test splits the check: "live messages" verifies delivery between result sets and batches, and "live messages (PRINT within one batch, no result set)" is a SKIP when the driver buffers.

## [1.0.4] - 2026-11-04

### Fixed
- Explorer table DDL failed on ASE with `[156] Incorrect syntax near the keyword 'clustered'`: the index metadata query aliased a column with the reserved word `clustered`. It is now `is_clustered`, and a check keeps all generated ASE catalog and tool SQL free of reserved-word aliases.
- MDA delta (live test) stopped at "Local limit of 10000 rows reached" on busy servers. Tool queries (not user runs) now read up to 200,000 rows regardless of the profile row limit, and the live test reads a bounded `monOpenObjectActivity` snapshot of the current database with only the columns it needs. User runs still honor the profile limit.
- PRINT and other informational messages reached the Messages pane only when the batch ended. ASE sessions now run `set flushmessage on` at login (never on IQ or Replication Server; a failure there does not block the login), so messages arrive while the batch runs.

## [1.0.3] - 2026-11-04

### Fixed
- `--live-test` crashed at startup with `ArgumentNullException` (WPF rejects `StartupUri = null`). `StartupUri` is no longer declared in App.xaml; only the normal startup path (including Smoke UI checks) sets it before opening the main window. The live-test path never calls `base.OnStartup` or creates the main window.
- Live test: any exception that escapes the runner, reaches the dispatcher or the AppDomain is recorded in the `--report` JSON (`phase: crashed`, `crash.type/message/detail`, checks already written are kept, secrets redacted) and the process exits with code 1 instead of crashing.

## [1.0.2] - 2026-11-04

### Changed
- `--live-test` now ships in the normal release build, so it runs from `C:\DBMON_SQL`. `--smoke` is optional with it. `--smoke` alone still reports that smoke checks are not included in release builds. All live-test safety guards are unchanged; see docs/building.md.
- The UI smoke suite (synthetic engine, screenshots and fixtures written next to the executable) stays in `Smoke=true` builds only.

## [1.0.1] - 2026-11-04

### Changed
- **Query timeout now defaults to 0 (no limit).** The old default of 60 s made the driver cancel any slow or lock-blocked statement. A positive value (1–3600 s) still cancels automatically, and `Esc` always cancels. A run that hits the limit reports "Query timeout (N s) reached; set 0 in the connection window for no limit."
- Saved logins and server groups now carry a format marker. Entries saved by 1.0.0 or earlier with a query timeout of 60 move to 0 once on load; an explicit 60 chosen in 1.0.0 cannot be told apart from the old default and also becomes 0 (set it again if you want it). Tool queries (Activity monitor, health check, probes) keep their own explicit short timeouts.

### Added
- **Health check rules**: configure enabled probes, maximum severity and per-probe thresholds separately for PROD and non-PROD servers through **Rules…**. Disabled probes do not execute; ignored probes never raise Overall. Last log dump is ignored by default on non-PROD. Rules are saved in versioned `health-rules.json`; corrupt files fall back to defaults with a visible warning.
- **Duplicate session**: right-click a session tab, or a row in the Activity monitor, to open a new editor tab next to the current one, already connected with the same profile and credentials (held in memory only) and in the same database. The PROD flag carries over.
- Smoke builds only (`-p:Smoke=true`): `--smoke --live-test` runs the checks against one saved non-PROD ASE profile named on the command line and present in `--allow`; see docs/building.md. Release packages do not include it.
- Saved logins written by 1.0.1 carry a format marker that 1.0.0 cannot read; keep a copy of `profiles.dat` if you may need to go back.

### Fixed
- Activity monitor resolves live ASE and MDA column aliases, selects the process result after status results, and reads SQL text and monitor identity by their server names. Missing SPID errors list the received headings.
- MDA repeat deltas match keys and counters by server aliases across result column order changes, exclude ID/gauge fields, and preserve server headings in results and charts.
- Catalog comparison, DDL, health, IQ and Replication Server parsers share column lookup across binding names, captions and source names, including trimmed and case-insensitive headings. IQ and RS derived metrics detect existing server headings correctly.
- **ASE 16 review fixes:**
  - Health check: Log usage and Last log dump skip databases whose log shares a device fragment with data (e.g. master), so Data and Log no longer report the same number; Data usage labels those rows `data+log`. Last log dump also skips archive databases.
  - Object compare no longer marks every indexed table *Unavailable*: it reads the index list among the result sets `sp_helpindex` returns on ASE 15/16.
  - Activity monitor SQL text orders `monProcessSQLText` by `BatchID, LineNumber, SequenceInLine`, and the current statement uses `monProcessStatement` (SPID, KPID, BatchID) instead of the `monSysStatement` history pipe.
  - Plan viewer and Statistics IO ignore the `<VA>` leaf of each operator, also find an `opTree` nested under `<plan>`, and round fractional estimated rows / logical reads instead of dropping them.
  - A run keeps up to 20,000 messages or 8 MiB of message text (was 250 first + 250 last), so the isql text view and the sp_sysmon section index cover full reports; the "N messages omitted" marker remains when the cap is reached. The live Messages pane keeps its 250 + 250 rows.
  - The sp_sysmon index recognizes Housekeeper Task Activity, Monitor Access to Executing SQL, Replication Agent, Tempdb Activity, Thread Utilization and Run Queue.
  - PROD/Repeat confirmation now also covers `sp_bind*`/`sp_unbind*`, login/role/ownership procedures (`sp_locklogin`, `sp_role`, `sp_changedbowner`, `sp_changegroup`), replication procedures (`sp_setreptable`, `sp_setrepproc`, `sp_setrepdbmode`, `sp_reptostandby`, `sp_config_rep_agent`, `sp_start/stop_rep_agent`), space and settings procedures (`sp_extendsegment`, `sp_placeobject`, `sp_logdevice`, `sp_modifythreshold`, `sp_chgattribute`, `sp_recompile`, `sp_hidetext`, `sp_tempdb`, `sp_audit`, `sp_logintrigger`, `sp_cacheconfig`/`sp_poolconfig` with a value), `lock table`, `sp_sysmon` without `noclear`, and RS `switch active`, `configure replication server` and `admin quiesce_force_rsi`.
  - MDA deltas no longer treat `monSysStatement`, `monSysLoad` or `monLicense` as cumulative counters.
  - Recipes: MDA columns use their exact catalog case (works on case-sensitive servers); *Unallocated space by device* joins on `vdevno` and counts device size in 2 KB virtual pages; *Top tables* uses `@@maxpagesize`; *Space usage by database* reports mixed data+log fragments separately.

## [1.0.0] — 2026-11-04

First full feature release after 0.4.0. Groups every tanda since that version (live messages and export, plan viewer and MDA deltas, activity monitor, health check and object compare, richer explorer / DDL / RS / IQ, and packaging tech debt). Intermediate tags `v0.5.0`–`v0.7.0` remain as pre-release markers; this is the single Latest package.

### Added
- **Plan viewer** (SAP ASE): full operator tree of Statistics IO plans with est → act rows/logical/physical reads and theme-colored warning/error chips when actual deviates from the estimate (≥10× / ≥100×, or estimate 0). Opens from a *Plan tree* button on Statistics IO tabs, their context menu, or *Tools → Plan viewer…*.
- **Estimated plan (no exec)** in Tools and the ASE session options: compiles the selection or script under `set noexec on` with `set plan for show_final_plan_xml to message on`, reads `showplan_in_xml`, then restores the tracked session options. Nothing is executed; scripts mentioning `noexec` are refused.
- **Compare plans…**: diff two captured plans by operator path (only in A / only in B / different index, est/act rows or logical reads), openable as a result tab.
- **Repeat MDA deltas**: optional "MDA deltas (cumulative counters)" checkbox when repeating ASE monitoring queries. Computes deltas between runs for cumulative MDA tables (`monSysWaits`, `monOpenObjectActivity`, `monProcessActivity`, …), with new/gone/reset markers and a top-N bar chart. Off by default; IQ and RS ignore it.
- **Text output (isql style)** beside Messages, with fixed-width result tables, row counts, server messages, Copy and UTF-8 `.txt` saving. Navigable **sp_sysmon sections** and recipes for 1- and 5-minute samples.
- **Export query to file…** (Tools / Run ▾) streams the selection/script to UTF-8 CSV, TSV or JSON Lines, bypassing grid limits, with PROD confirmation, Esc cancel and partial-file reporting.
- **Activity monitor…** with blocking tree, theme-colored head blockers, MDA SQL/locks panes, and **Kill spid…** behind double confirmation (PROD extra warning; own/system SPIDs protected).
- **Server health check** and **Compare objects** under Tools: parallel ASE/IQ/RS probes with a theme-colored matrix and CSV/HTML export; two-connection object compare with column/index diffs and unified LCS line diffs.
- **Richer ASE explorer**, **Generate DDL**, **Replication Server panel** and **IQ metrics** panels (read-only snapshots, manual refresh; RS suspend/resume behind double confirmation and the PROD lane).
- Live server messages while a batch runs (~250 ms refresh) with head/tail retention; complete `syscomments` fragment order for large object source.

### Changed
- UI smoke checks, fixtures and the synthetic showcase compile only with `-p:Smoke=true` (`DBMON_SMOKE`). Standard builds and packages exclude them; `--smoke` prints "smoke checks not included in this build" and exits. Publishing verifies a separate smoke build before producing the distribution with smoke disabled.
- An internal ODBC transport seam and scripted driver-free checks exercise `OdbcSession` without a driver. Real connections keep native ODBC calls and exceptions.

### Fixed
- Statistics IO initializes `textsize` once per connection and never re-runs the user batch to obtain a plan (*plan not captured* when the buffer is empty).
- Disconnected execution asks users to connect, covering ASE, IQ and Replication Server.
- The optional native IQ TLS registry check skips with an `INFO` line when the registry is unavailable (non-Windows hosts and missing hives).

## [0.4.0] — 2026-11-04

### Fixed
- **Statistics IO no longer runs SQL twice.** With Statistics IO and Statistics time on, an empty `showplan_in_xml` buffer made the app run the user's batch a second time to get a plan, which doubled the effects of `exec`, `dbcc` or `checkpoint`. Now each batch runs exactly once; when no plan comes back, Messages says *plan not captured*. A core check runs batches through a counting test double to prove it.
- **Server groups with Replication Server or IQ profiles save again.** Group validation required a database for every server; it now applies the same per-platform rules as the login (RS has none, IQ may leave it empty).
- **A saved profile with a missing CA or identity file no longer hides all the others.** Every profile loads; the login window names the affected one (*Profile X: The trusted certificate file does not exist: …*) and the Profiles list marks it. Saving keeps it in the file. It cannot connect until the path is fixed.

### Added
- **PROD protection.** *Production (PROD)* in the login and in saved profiles (older profiles are not PROD). A PROD session shows a red frame, rail and **PROD** tag on its session tab and a red band with **PROD** in the status bar, in the palette's error color. Before SQL that changes state runs on it (`F9`, `Ctrl+E`, `Alt+F9`, result re-run, object actions, recipes, Repeat, Run on several servers), a confirmation lists the server, each statement and its line, and any `go N` repetitions; **Cancel** is the default. Sessions that are not PROD are unchanged.
- **Wider state-change detection** (used by PROD and Repeat): `sp_add*`, `sp_drop*`, `sp_dboption`, `sp_rename`, `sp_modifylogin`, `sp_password`, `sp_configure` with a value, every `dbcc` except the read-only ones, `checkpoint`, `online database`, `disk init/resize/mirror…`, `mount`/`unmount database`, `quiesce`, `update statistics` (counts: it rewrites optimizer statistics), and Replication Server `sysadmin`, `ignore loss`, `rebuild queues`, `allow connections` and `create`/`alter`/`drop connection`. Comments, strings and `[names]` are still ignored.
- **`go N`** runs a batch N times, as in `isql` (1 to 1000).
- **Continue after errors** (Run ▾ and the Query menu, kept across restarts): a failed batch is recorded and the next one runs; Messages ends with *N batches ok / M with error*. Off by default.
- **No query timeout**: a profile's query timeout can be `0`, and **Run without timeout (maintenance)** in Run ▾ runs once without it. `Esc` still cancels.

## [0.3.0] — 2026-11-04

### Added
- Four light palettes in **Personalize → Style**, next to Alba and Sandstone (23 palettes, 6 light):
  - **Papel**: neutral paper with soft warm greys and an ink-blue accent.
  - **Sepia**: cream and amber with a terracotta-brown accent and a deeper header band.
  - **Glaciar**: cool blue-white surfaces with a steel-blue accent and teal strings.
  - **Alto contraste**: pure white, black text, marked borders and a dark-blue accent, for accessibility (text at 7:1 or more).
- A core check measures WCAG contrast for the new light palettes (text, header, badges, notices, accent, gutter, every SQL color and the selection) and fails below 4.5:1, or 7:1 for Alto contraste.

### Changed
- Boberto's dock uses its light-floor reflection and background tint on the new light palettes too.

## [0.2.0] — 2026-11-04

### Changed
- The **Connection** menu left the menu bar: right-click a session tab for Connect… (`F11`), Disconnect (`F12`) and Close session, applied to that tab's session. **Exit** moved to the end of the **Query** menu. `F11` and `F12` work as before on the active session.

## [0.1.0] — first public version

### Connections
- SAP ASE and SAP Replication Server through the 64-bit SAP ASE ODBC driver; SAP IQ through the 64-bit SAP IQ ODBC driver.
- Login with server type, `sql.ini` aliases (query entries; master entries for IQ and RS aliases without one), manual host and port, TLS with a CA file, charset, language, timeouts and row limit.
- Saved profiles encrypted with Windows DPAPI; passwords only with *Remember password*.
- Offline mode when the login is cancelled: SQL can be edited and saved, nothing runs.
- Status bar session facts: database (switchable on ASE), SPID, login and negotiated charset.

### Editor and execution
- AvalonEdit SQL editor with highlighting, find/replace, bookmarks, comments, case transforms, duplicate line, word wrap and zoom.
- `F9` script or selection, `Ctrl+E` selection, `Alt+F9` current line, `Esc` cancel; `GO` batches like `isql`.
- `:name` parameters with a form, live preview, escaping inside quotes and single-token values outside them.
- Persistent, timestamped query history (latest 100 entries, newest first) and command search (`Ctrl+K`).

### Results
- Typed values and `NULL` preserved; messages kept in their own panel with original diagnostics.
- Results accumulate in tabs; `F9` on a result tab re-runs that SQL in place and keeps its name; pins, renames, up to 40 result tabs.
- Local filter, row details, numeric aggregates, transpose, bar chart, frozen first column, copies in several formats, exports to CSV, JSON, Markdown, HTML, `IN` lists and `INSERT` statements.
- Column widths fitted to their first rows.

### Productivity
- Recipe library with 94 generic recipes tagged ASE, IQ or RS, filtered by the connected platform; insert on a new line or replace the query; import, export and My recipes.
- Run on several servers in parallel with per-server results, combined results, key-based diffs and saved server groups.
- Repeat on a timer (5 s to 24 h) with a caution band, countdown, run limit, stop on error, a new result per run on request, and acknowledgement for state-changing SQL.
- Object explorer for ASE databases and IQ objects with server-side filters and paging; editor object actions for ASE and IQ.

### Interface
- 19 DBMON palettes, custom accent color mixer, bundled Inter and JetBrains Mono, density, corners and motion options in one Personalize window with a live preview.
- Boberto companion and Boberto Studio, running from local files in an isolated WebView2 profile.
- Local data in `%LOCALAPPDATA%\DBMON_SQL`, moved once from the previous `ASE ISQL Desktop` folder.
