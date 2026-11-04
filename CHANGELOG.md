# Changelog

All notable changes to DBMON SQL are listed here. The project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.7.0] — 2026-11-04

### Added
- **Server health check** under Tools: choose connected profiles or open a server group, then run fixed read-only ASE, IQ and RS probes in parallel through MultiRun. A theme-colored server × probe matrix shows OK/WARN/FAIL, per-cell SQL/results/messages, independent server deadlines, cancellation, and CSV/HTML export. Optional `health-check.json` configures capacity, blocking, log-dump-age thresholds and the deadline.
- **Compare objects** under Tools: compare two ASE connections and their databases for tables, procedures, views or triggers. Lists objects only in either side, different, same or unavailable; compares columns and logical index definitions, and shows unified LCS line diffs plus side-by-side definitions. Optional SQL whitespace normalization preserves quoted contents. Reads use qualified catalogs and never change the session database.
- Core fixtures cover health thresholds, platform result parsing, matrix aggregation, parallel failure isolation/deadlines, object sets, line diffs, column/index changes, and incomplete metadata.

## [0.6.0] — 2026-11-04

### Added
- **Activity monitor…** in Tools and command search: a dedicated ASE connection with 2/5/10/30-second refresh, pause, a blocking tree, theme-colored head blockers, cycle markers, filtering, sorting within branches and copying selected rows. The editor session remains independent.
- Selected-SPID details show current SQL from `monSysStatement` / `monProcessSQLText` and locks from `monLocks` when accessible. Unavailable MDA panes hide with an explanation; **Retry MDA** checks access again. IQ and Replication Server display an explicit unsupported message.
- **Kill spid…** requires a process-identity review and typing the SPID, with extra PROD warnings. Monitor/editor SPIDs, system processes (`suid <= 0`) and missing logins are protected. Process identity and guards are checked again before sending KILL; server permissions still apply.
- Core checks cover process parsing, blocking chains, multiple heads, cycles, filtering/sorting and kill guards.

## [0.5.0] — 2026-11-04

### Added
- **Text output (isql style)** beside Messages, with fixed-width result tables, row counts, server messages, Copy and UTF-8 `.txt` saving. Each result's toolbar opens its whole run; sessions retain their own text output. Long cells wrap without losing values. When the engine supplies separate result/message lists, text shows results first, then messages in their received order.
- A navigable **sp_sysmon sections** list in the text view recognizes underlined report headings. Recipes **sp_sysmon 1 minute** and **sp_sysmon 5 minutes** sample ASE performance.
- **Export query to file…** in Tools and Run ▾ runs the selection/script directly into UTF-8 CSV, TSV or JSON Lines, bypassing grid result limits. CSV/TSV repeat headers after a blank line for each result set; JSON Lines includes a one-based `resultSet` index and column/value arrays. The usual PROD confirmation applies. The status bar counts rows, Esc cancels, and cancellation or errors preserve the file with its path clearly reported as partial.

### Changed
- Server messages (including `print`, `sp_sysmon` and `dbcc` output) appear while a batch runs, refreshed about every 250 ms. Message retention keeps the first 250 and last 250 rows, with one counted omission marker between them; error diagnostics use the same limit.

### Fixed
- ASE object source uses the complete `syscomments` fragment order (`number, colid2, colid`) in both the browser and editor object actions, preserving large definitions.
- Statistics IO initializes `textsize` once per connection and preserves user `set textsize` statements, including when capture is enabled later or toggled off and on. Its tooltip now names the plan capture command actually sent.
- Disconnected query execution asks users to connect to a server, covering ASE, IQ and Replication Server.

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
