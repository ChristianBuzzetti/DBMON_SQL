# DBMON SQL user guide

DBMON SQL is a Windows workbench for writing and running SQL against SAP ASE, SAP IQ and SAP Replication Server (RS). This guide walks through each part of the window.

Standard builds and distributed packages exclude developer UI smoke checks and synthetic showcase data. In these builds, `--smoke` prints `smoke checks not included in this build` and exits with code 1. Developers can enable the checks with `dotnet build src/AseIsql.Desktop -c Release -p:Smoke=true`; see [Building and testing](building.md) for running them and producing a package. Driver-free core checks also cover session execution and diagnostics; the optional native IQ check prints an `INFO` skip when its Windows registry or driver is unavailable.

- [Connecting](#connecting)
- [Session tabs](#session-tabs)
- [The window](#the-window)
- [The editor](#the-editor)
- [Running SQL](#running-sql)
- [Results](#results)
- [Messages](#messages)
- [Recipes](#recipes)
- [Parameters](#parameters)
- [Run on several servers](#run-on-several-servers)
- [Repeat on a timer](#repeat-on-a-timer)
- [Activity monitor](#activity-monitor)
- [Object explorer](#object-explorer)
- [Generate DDL](#generate-ddl)
- [Replication Server panel](#replication-server-panel)
- [IQ metrics](#iq-metrics)
- [Status bar](#status-bar)
- [Personalization and Boberto](#personalization-and-boberto)
- [Files and local data](#files-and-local-data)
- [Limits](#limits)

## Connecting

The login opens when the app starts (and with `F11`, or **Connect…** when you right-click a session tab).

| Field | Notes |
|---|---|
| **Profile** | A saved login. Choosing one fills the form. |
| **Server type** | SAP ASE, SAP IQ or Replication Server. It is your choice: the app never guesses it from the alias name. |
| **Login / Password** | Your credentials. The password stays masked and is never saved unless *Remember password* is on. |
| **Database** | Required for ASE (default `master`). Optional for IQ (empty uses the server's database). Not used for Replication Server. |
| **Server** | An alias from `sql.ini`. ASE uses `query` entries. IQ and RS aliases that list only a `master` entry use it, and the summary line says so. |
| **Charset** | `ServerDefault`, `utf8`, `iso_1`, `cp1252` or any charset name. |
| **Hostname** | Client host name reported to the server (ASE and RS). |
| **Language** | ASE/RS message language; `us_english` by default. |
| **Encrypt password / Encrypt data (TLS)** | TLS is required when the `sql.ini` entry says `ssl`; select the CA file under *Advanced options*. |
| **Production (PROD)** | Marks the server as production. Its session shows a red **PROD** band and tag, and SQL that changes data or server state asks for confirmation first. See [PROD protection](#prod-protection). Saved with the profile; profiles saved before 0.4.0 are not PROD. |

**Advanced options** holds manual host and port, the TLS CA file (`TrustedFile`), the IQ server name (optional when one IQ server listens on the host and port), login timeout, query timeout (**0 = no timeout, the default**: a query runs until it ends or `Esc` cancels it; 1 to 3600 s cancels it automatically) and the maximum number of rows per execution.

For SAP IQ servers that require a client identity, enable **Encrypt data (TLS)** and select both the trusted `.crt` under **Browse CA…** and the `.id` under **Browse identity…** in Advanced options. Enter the private key password in **Client identity password**, separately from the database login password. Saving a profile retains both paths. Enable **Remember identity password** to save the key password with that profile, encrypted with Windows DPAPI for your user. This is independent of **Remember password** for the database login; both are off by default. Save the profile with **Profiles → Save current**, or connect with an existing loaded profile, to keep the selected passwords. Loading it again restores only the passwords you chose to remember. Changing the identity file or leaving SAP IQ clears the identity input and its Remember checkbox.

When connecting by IP to a server whose certificate identifies a DNS name, set **Expected server certificate name** to the verified Common Name (CN). DBMON sends `CERTIFICATE_NAME` and validates that name and the certificate trust chain. Leave it empty to use normal hostname/IP verification. This field is separate from **IQ server name**, which selects the engine (`ENG`/`ServerName`). DBMON does not silently enable `SKIP_CERTIFICATE_NAME_CHECK`; an explicit certificate name keeps server identity verification. See SAP's [certificate name checking reference](https://help.sap.com/docs/SAP_IQ/8e6989ea146b41d78f671443295cd7a0/561a2182636942cda909f529203c467c.html).

IQ TLS values containing semicolons, parentheses, braces, quotes, control characters or leading/trailing whitespace are rejected before calling the driver: these values would be ambiguous in its nested TLS option syntax. DBMON sends the native `Encryption=TLS(...)` clause without surrounding ODBC braces; those braces cause IQ error `-832` (unrecognized encryption type). Diagnostics hide both passwords. Certificate trust and server identity verification remain enabled; a TLS failure never triggers an unencrypted retry. See SAP's [connection parameters reference](https://help.sap.com/doc/1948d3f607fc4e7b9745d07669ad0f1b/16.2.0/en-US/SAP_IQ_Communication_Reference_en.pdf) for `IDENTITY`, `IDENTITY_PASSWORD` and `TRUSTED_CERTIFICATES`.

**Test connection** opens a temporary session and runs `SELECT 1` (`admin version` on Replication Server). **Connect** opens the session and closes the dialog. The *Information* tab shows the driver in use and the `sql.ini` that was read.

**Profiles** keep the form under a name. They are stored in `%LOCALAPPDATA%\DBMON_SQL\profiles.dat`, encrypted with Windows DPAPI for your user. **Forget password** removes only the database password; **Forget identity password** removes only the key password. Each action also clears that secret from the loaded form. Saving with either Remember checkbox off removes that stored password. Overwriting or forgetting passwords creates no `.bak` copies. Deleting a profile asks for a second click.

A profile whose CA certificate or IQ identity file was moved or deleted no longer blocks the others: every profile loads, and the login window names the affected one in red (for example *Profile X: The trusted certificate file does not exist: C:\certs\ca.pem*), also in the Profiles list. Load it, choose the file again under *Advanced options* and save it. Until then it cannot connect, and saving other profiles keeps it in the file unchanged.

Cancelling the login leaves the app open **offline**: a notice sits above the editor, Run is disabled, and nothing executes until you connect.

### Drivers per server type

| Server type | ODBC driver | Notes |
|---|---|---|
| SAP ASE | `Adaptive Server Enterprise` (64-bit) | Full support: session facts, database switch, explorer, object actions. |
| SAP IQ | `SAP IQ` (64-bit) | `Host=host:port`, optional server and database names. Explorer and `sp_iq*` object actions. |
| Replication Server | `Adaptive Server Enterprise` (64-bit) | RCL commands (`admin who`, `admin health`…). No database, no catalog. |

## Session tabs

Use **+ Session** to open the existing login in a new workspace in the same window. `F11` / **Connect** fills the active offline tab, or adds a tab when the active one is already connected. Cancelling login leaves that tab **Not connected**, with an offline notice and Run disabled. Each connected tab shows its alias (or host:port) and **ASE**, **IQ** or **RS**.

Switch tabs to return to that session's SQL document, undo history, caret, bookmarks, results, pins, messages and cached object explorer. ASE options and the current database belong to that connection. `F9` on a result re-runs its recorded SQL on the session that produced it; pinned results remain in place.

**Disconnect** (`F12`) affects only the active session. Right-click any session tab for **Connect…**, **Disconnect**, **Duplicate session** (a new tab right after it, connected with the same profile and credentials, in the same database) and **Close session** on that session, whether or not it is on screen; Connect brings that tab forward first. The **×** on a session tab closes that connection and confirms unsaved SQL. **Exit** is at the end of the **Query** menu. Closing the last tab leaves one offline workspace. Closing the window confirms unsaved documents and closes all its connections.

Sessions run independently. Start a long query, switch to another engine's tab and work there: each tab keeps its own query in flight, its own Run and Cancel state, its own status line and its own messages, and publishes its results into its own tab when they arrive. A tab that is working shows a **▶** before its name, and the result it produced while you were away is already selected when you return. Inside one session the rules are unchanged: one query at a time, and a repeat there blocks that session's `F9`, multi-server run and catalog until it stops.

A session that is running or repeating cannot be closed: stop it first, and the English status says so. The caution band, the Run chevron glyph and the title warning describe the session on screen, so they follow the tab you select. **Run on several servers** (`Shift+F9`) uses the active connection and its separately managed extra servers.

Appearance, Boberto, profiles, SQL history, recipes, server groups and command search are shared by the window. Session tabs are held in memory for the window's lifetime; they do not launch another process or window.

## The window

The window title names the open document and the product, with a `*` while it has unsaved changes. The server, platform and database are not repeated there: they belong to the session tab and the status bar, which are always on screen, and one title cannot speak for several sessions at once.

The toolbar keeps one row: **Run** with its chevron ▾ (run selection, current line, several servers, repeat, run without timeout, continue after errors), **Cancel**, the file glyphs (new, open, save), **Objects** and **Recipes**. Session tabs sit below the toolbar; the active session has an accent border and underline, and **+ Session** opens another workspace. In windows narrower than 1180 DIP, labels turn into glyphs (tooltips name them); below 900 DIP Boberto's band steps aside so the workspace keeps the header.

## The editor

The SQL editor is [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) with SQL highlighting in the palette's colors.

- **Find** `Ctrl+F`, **find and replace** `Ctrl+H`.
- **Bookmarks**: `Ctrl+F2` toggles, `F2` / `Shift+F2` jump to the next / previous one. Bookmarks follow inserted lines.
- **Editor menu** and right-click: undo/redo, copy/paste, comment or uncomment lines, duplicate line (`Ctrl+Shift+D`), selection to upper or lower case.
- **Word wrap** `Alt+Z`; **zoom** `Ctrl++`, `Ctrl+-`, `Ctrl+0`, or `Ctrl` with the mouse wheel over the editor.
- **Files**: `Ctrl+N` new, `Ctrl+O` open, `Ctrl+S` save, `Ctrl+Shift+S` save as. Files are UTF-8; the previous version is kept as a backup when you overwrite. Unsaved changes are confirmed before they are discarded.
- **History** `Ctrl+R`: the latest 100 queries across sessions, newest date/time first; double-click one to load its complete SQL. Stored as plaintext in `%LOCALAPPDATA%\DBMON_SQL\query-history.json` with atomic-write backups. Keep sensitive SQL out of history. Unreadable history files are preserved and a warning is shown instead of overwriting them.
- **Command search** `Ctrl+K`: every command by name.
- **SQL autocomplete** is always available: `Ctrl+Space` suggests matching tables from the current database after `FROM`, `JOIN`, `UPDATE`, `INTO` or `TABLE`, plus local keywords, Sybase system objects and known databases in other positions. Table names preserve server spelling. Use `Up/Down` to choose, then `Tab` or `Enter` to accept; `Escape` closes the list. Outside the list, `Tab` indents and `Enter` inserts a newline. See [editor completion](editor-completion.md).

Right-clicking an identifier (`orders`, `dbo.orders`, `sales..orders`, `[my table]`) checks its database, owner and type before offering **object actions**. Tables and views offer row inspection; procedures offer code inspection. ASE uses `sysobjects`; IQ uses its own catalogs. When offline, busy, ambiguous or unable to inspect safely, the menu explains why instead of guessing. See [object actions](object-actions.md).

## Running SQL

| Keys | What runs |
|---|---|
| `F9` or **Run** | The selection if there is one, otherwise the whole script. On a result tab, see [re-running a result](#re-running-a-result). The chevron next to Run lists the other ways to run. |
| `Ctrl+E` | Only the selection; with nothing selected it reports that and runs nothing. |
| `Alt+F9` | The line under the caret. |
| `Esc` or **Cancel** | Cancels the running execution (best effort in ODBC; partial results are reported as such). |

Scripts are split on `GO` lines, exactly like `isql`; `GO` inside strings, comments or brackets is not a separator. Each execution shows its elapsed time in the status bar while it runs.

### Export a query directly to a file

Choose **Export query to file…** in **Tools** or **Run ▾**, then choose a CSV, TSV or JSON Lines (`.jsonl`) file. It runs the current editor selection, or the whole script when nothing is selected, including `GO` batches and repetitions. Parameters and the usual PROD confirmation apply before the file is opened or SQL is sent. Existing files are replaced after the save dialog's overwrite confirmation.

Every result set streams to disk row by row without creating grid tabs. The grid's row, 64 MiB, cell, column and result-set limits do not apply; SQL/script limits and the profile's query timeout still apply. The status bar shows rows written. **Esc** or **Cancel** requests cancellation; the chosen file stays on disk and the status bar and Messages identify its path and row count as **partial**. Errors also retain and report partial output. ODBC cancellation is best effort: verify the session state before continuing.

Files use UTF-8 without a BOM. CSV/TSV use CRLF row endings and RFC 4180 quoting (also applied to tabs in TSV); NULL and empty strings both produce empty fields. Multiple sets share one file, with a blank line and a new header for each set, including empty sets. Numbers and dates use invariant culture, dates retain their fractional seconds, and binary values use `0x` hex.

JSON Lines writes one object per row: `{"resultSet":1,"columns":["name","amount"],"values":["José",null]}`. The one-based result-set index continues across batches; empty result sets advance the index without producing a line. Column/value arrays preserve duplicate or empty column names. Numbers and booleans remain typed, NULL is JSON `null`, and dates use ISO text. Export follows **Continue after errors**; any failed batch marks the overall file partial. Statistics IO plan tables are not added to the export.

### isql-style execution

- **`go N`** (also `GO N`, with any spaces) runs the batch above it N times in a row, as `isql` does. N is 1 to 1000; anything else is refused before anything runs. Messages ends with a summary such as *Summary: 5 batches ok / 0 with error*.
- **Continue after errors** (Run ▾, or *Query → Continue after errors*): when a batch fails, its error is recorded in Messages and the next batch runs, like `isql` told to continue. Off by default: the first failed batch stops the script. The choice is kept across restarts (`run.json`). The final summary counts the batches that succeeded and failed. A lost connection or `Esc` always stops the script.
- **Run without timeout (maintenance)** (Run ▾, or *Query → Execute without timeout*): runs the selection or script once without the profile's query timeout, for long `dbcc`, `reorg` or `dump` work. A profile can also use query timeout `0` for no timeout at all. `Esc` cancels in both cases.
- **Statistics IO** (*Tools → SAP ASE session options*) sends `set plan for show_execio_xml to message on` and reads the plan the server kept for each batch. It initializes `textsize` to 16 MiB once per connection when capture first runs, unless your SQL has already issued `set textsize`. Your own `set textsize` takes precedence for the rest of that connection, including after toggling Statistics IO. When the server returns no plan, Messages says *plan not captured* for that batch; the batch is never run a second time to obtain one.

### Plan viewer (SAP ASE)

- **Plan tree** (button on a *Statistics IO* tab, its context menu *Plan viewer…*, or *Tools → Plan viewer…*) opens the full operator tree of the executed plan: Emit, joins, aggregates, sorts and scans, each with object, index and scan type when present, and rows / logical reads / physical reads shown as *est → act*. When actual rows or logical reads differ from the estimate by 10× or more (either way) the operator gets a warning chip; by 100× or more, or when the estimate is 0 and the actual is not, an error chip. Colors come from the theme (Notice, ErrorInk). Hover a row for the exact figures; select it for details. *View XML* / *Copy XML* show the plan as ASE returned it.
- **Estimated plan (no exec)** (*Tools*, or *Tools → SAP ASE session options* next to Statistics IO) compiles the selection or script without executing it and opens its tree with estimates only (nothing is flagged). The exact sequence, each line its own batch: `set statement_cache off` (a cached plan is reused without optimization and would return no plan), `set plan for show_final_plan_xml to message on`, `set noexec on`, your SQL, `set noexec off`, `select showplan_in_xml(0)` (then `(-1)` if empty), `set plan for show_final_plan_xml off`, `set statement_cache on`. If NOEXEC was already on it is lifted first and restored at the end; if Statistics IO was on, `set plan for show_execio_xml to message on` is re-sent. Scripts that mention `noexec` are refused. If cleanup fails, Messages says so: check the session options or reconnect. If a large plan arrives cut, turn Statistics IO on once (it initializes `textsize`) or issue `set textsize`.
- **Compare plans…** (*Tools*, the Statistics IO tab menu, or *Compare with…* in the viewer) diffs two plans captured in this window (Statistics IO tabs, estimated plans; the last 32 are kept). Operators are matched by their path in the tree and listed as *same*, *different* (index, est/act rows, est/act logical reads), *only in A* or *only in B*. *Open as result tab* copies the diff to a regular result.

### PROD protection

Tick **Production (PROD)** in the login (or the saved profile) for production servers. While a PROD session is on screen, its session tab has a red frame, a top rail and a **PROD** tag, and the status bar shows a red band and **PROD** beside the platform. The red is the palette's error color, so it reads on light and dark palettes.

Before SQL that changes state runs on a PROD session, a confirmation shows the server and every statement found (line, kind and text), plus the batches that `go N` repeats and how many times. **Cancel** is the default: `Enter` and `Esc` never run it; only **Run on PROD** does. This applies to `F9`, `Ctrl+E`, `Alt+F9`, re-running a result, object actions, recipes (they are inserted into the editor and run from there), **Repeat every…** (asked once when it starts, with the interval) and **Run on several servers** (one confirmation naming every PROD server in the run). Read-only SQL runs without asking, and sessions that are not PROD behave as before.

What counts as changing state: `insert`, `update`, `delete`, `merge`, `select … into` (temporary `#tables` excluded), `create`/`alter`/`drop`, `truncate`, `grant`/`revoke`, `setuser`, `reconfigure`, `writetext`; `sp_add*`, `sp_drop*`, `sp_dboption`, `sp_rename`, `sp_modifylogin`, `sp_password` and `sp_configure` with a value; every `dbcc` except `sqltext`, `help`, `stacktrace`, `showplan`, `memusage`, `traceflags` and `traceon`/`traceoff` of 3604/3605; `checkpoint`, `online database`, `disk init/resize/mirror/remirror/unmirror/refit/reinit`, `mount`/`unmount database`, `quiesce`, `kill`, `dump`, `load`, `shutdown`, `reorg`; and **`update statistics`** (it rewrites the optimizer statistics, so plans can change, and reads whole tables or indexes). Replication Server: `sysadmin`, `ignore loss`, `rebuild queues`, `allow connections`, `suspend`, `resume`, and `create`/`alter`/`drop connection`. Words inside comments, quoted strings and `[brackets]` do not count. `exec` of a user procedure is not inspected: the guard is a warning aid, not a permission check.

## Results

Every result set opens in its own tab: **Result 1**, **Result 2**…

Choose **Text output (isql style)** beside Messages, or on a result's toolbar, to read the whole run as monospace text. Each result set has headers, dashes and `(N rows affected)` for the rows received locally. Grid filters and sorting do not change this text. Long cells wrap at 120 characters without dropping their contents; `NULL` is explicit. **Copy** copies the whole run and **Save as .txt…** saves UTF-8 text. Selecting an older result shows that result's run; Query shows the latest run. Sessions keep their own output, including runs with messages or errors and no result tables.

The current engine provides separate result and message lists: text shows result sets first, followed by server messages in their received order, plus errors and any retention notice. Exact message/result interleaving is unavailable from these lists. Row counts on partial results describe only the received rows; the error explains the limit or cancellation.

When output contains recognized `sp_sysmon` headings followed by dashes, **sp_sysmon sections** appears beside the text. Select a section (or use the arrow keys) to scroll to its heading. The recipes **sp_sysmon 1 minute** and **sp_sysmon 5 minutes** collect reports; allow enough query timeout, or use **Run without timeout (maintenance)**.

- **Accumulate**: a new execution adds its tabs after the existing ones. Up to 40 result tabs are kept; beyond that the oldest unpinned ones close and Messages says so.
- **Typed values**: numbers, dates and binary values keep their types; `NULL` is shown as blank but stays a real `NULL` for copying and exporting. Binary values are shown as hex.
- **Columns** open wide enough for their first rows (up to 520 px) and can be resized, reordered and sorted. Unnamed expressions are labelled *(no column name)*.
- **Filter**: the box above each grid filters rows locally, without querying the server.
- **Row details** shows the current row vertically. Selecting numeric cells shows SUM, AVG, MIN and MAX in the status bar.
- **Pin** keeps a result open; it is never replaced and survives new executions (up to 8 pinned results).
- **Rename** with a double-click on the tab header, or *Rename resultset…*. **Middle-click** the header to close it.

Right-click the grid for:

- **Select all** (`Ctrl+A` with the grid focused) marks all result cells in the current filtered view. **Select entire rows** expands the selected cells to their complete rows.
- **Copy as formatted table** copies a styled rich HTML table for Outlook, Teams and other rich-text editors, including headers, borders and alternating row colors. It includes a readable plain-text fallback, follows the visible column order and copies only selected cells; gaps stay empty. Select all first for the whole filtered result. HTML displays database NULL as `NULL`, distinct from empty strings. The receiving editor controls which styles it preserves; choose normal paste or keep source formatting to retain the table. This action creates no file. Escaped HTML is limited to 16 Mi characters.
- **Include executed query** is an optional checkbox in each result's context menu. It applies to every result copy format, including Ctrl+C. The formatted-table action places a styled SQL block above the output; other formats prepend readable Query and Results sections. It uses the SQL recorded for that result, including submitted parameter values, even after editing the query, switching sessions or preserving a pinned result. A multi-batch execution includes its complete submitted script. Leave this option off when copying strict CSV/TSV data without a report preamble. Query and output share the clipboard size limit; query inclusion is unavailable if the original SQL is missing.
- **Copy**, *Copy with headers*, *Copy headers only*, *Copy all*, *Copy all with headers* and **Copy delimited** (tab, comma, semicolon, pipe). Ordinary Copy/Ctrl+C and Copy all preserve literal text, including SQL line breaks and double quotes, without CSV quoting. Columns are separated by tabs and result rows by newlines. Use Copy delimited or CSV export when you need escaped, unambiguous fields for a spreadsheet. Copies follow the visible column order and source values, not clipped cell text.
- **View source SQL**, **Freeze first column**, **Bar chart…**, **Transpose result**.
- **Export** to JSON, Markdown, an HTML report, a SQL `IN` list or `INSERT` statements. CSV export is under *Tools*.

### Re-running a result

With a result tab selected, `F9` (or **Run**) runs **that result's SQL again on the session it came from** and replaces its tabs in place, keeping the names you gave them. A pinned result is never replaced: the new run is added beside it. On the Query tab, `F9` runs the editor as usual.

Results from a multi-server run are re-run with *Run on…* (`Shift+F9`); results from an earlier session cannot be re-run on a new one.

## Messages

The panel under the workspace lists the latest execution's messages: time, code, source and text. Server prints, warnings, errors, "rows affected" and client notes appear here and never as result grids. Right-click a message and choose *View original diagnostic* to see the driver's full text (with your password redacted).

Messages are shown when the driver delivers them. With the SAP ASE ODBC driver that is when the batch completes, so PRINT and informational output appear then; DBMON SQL itself shows each message as soon as the driver raises it. The panel refreshes about every 250 ms. For long output such as `sp_sysmon` or `dbcc`, the panel retains the first 250 and last 250 messages in order, with one **… N messages omitted …** row between them. Client notes also count toward the panel's retention limit. The run itself keeps up to 20,000 messages or 8 MiB of message text, so the isql text view and the sp_sysmon index cover a full report; beyond that cap the same marker shows the omitted middle.

The panel can be hidden from *View → Server messages*; it reopens by itself when an execution reports an error.

## Recipes

**Recipes** (toolbar) or *Library → Recipes…* opens the query library: 94 built-in recipes plus your own.

- Each built-in recipe is tagged **ASE**, **IQ** or **RS**. The platform box shows the connected platform's recipes by default, or all of them when offline. A recipe for another platform is still available and says so in red.
- **Insert on a new line** (default, also `Enter` and double-click) puts the recipe on its own line below the caret, never over your SQL, and selects it so `Ctrl+E` runs just that. `Ctrl+Z` removes it.
- **Replace query** replaces the whole editor with the recipe; `Ctrl+Z` brings your text back.
- **My recipes**: *Library → Save as recipe…* stores the selection or query. *Import…* and *Export…* exchange recipes as JSON. My recipes appear on every platform.

Recipes that change things (kill a SPID, truncate a log, resume a connection…) are in the *DBA operations* and *Operations* categories and describe their effect before you run them.

## Parameters

Write `:name` anywhere in SQL to make it a parameter:

```sql
select * from master..monLocks where spid = :spid
sp_spaceused ':table', 1
```

A form asks for each value when you **insert a recipe**, when you **run SQL** that contains parameters, or when you choose *Editor → Fill parameters…* (`Ctrl+Shift+P`). It shows a live preview and the reason any value is refused.

- Inside quotes, values are escaped (`o'neil` becomes `'o''neil'`).
- Outside quotes, a value must be a single name, qualified name or number (`42`, `dbo.orders`, `[my table]`), so a value can never add statements.
- `:name` inside comments, `[brackets]`, times such as `'00:00:10'` and `a::b` are not parameters.
- Known names get sensible defaults (the current database for `:database`, `00:00:10` for `:interval`…). Values are remembered for the session only, never written to disk.
- *Run as written* / *Insert as written* keeps the placeholders.

## Run on several servers

**Run ▾ → Run on several servers…** (`Shift+F9`) runs the selection or script on several sessions in parallel.

- Add servers with *+ Add server…* (each one logs in separately; passwords are not stored), or open a saved **group**.
- Each server's results appear as `SERVER · 1`, `SERVER · 2`…, plus a combined `All · n` result with a server column and a **Diff** tab that shows which keys differ between servers. Choose the key columns with *Key* and *Compare*.
- Messages are labelled with their server; one failing server does not stop the others.
- *Groups* saves the listed servers as a named group in `%LOCALAPPDATA%\DBMON_SQL\server-groups.json`.

## Repeat on a timer

**Run ▾ → Repeat every…** (`Ctrl+Shift+F9`, or *Query → Repeat every…*) runs the selection or query on the current session now and again after each run finishes. While it repeats, the Run chevron shows the repeat glyph.

**MDA deltas (cumulative counters)** — an optional checkbox in the Repeat window — computes deltas between runs for ASE monitoring queries. When enabled and the SQL reads a cumulative MDA table (`monSysWaits`, `monOpenObjectActivity`, `monProcessActivity`, `monSysStatement`, etc.), the first run establishes a baseline and subsequent runs show a delta table with what changed: counters are subtracted per row key (WaitEventID, ObjectID, SPID+KPID, etc.), new rows are marked `[new]`, rows that disappeared are listed as `[gone]` with NULL deltas, and counters that went down (a process restarted, or counters were cleared) show `[reset]` with their new values. A top-N bar chart of the largest deltas appears alongside the delta table. IQ and Replication Server ignore this option; non-MDA results pass through unchanged.

- **Every**: 5 seconds to 24 hours, in seconds, minutes or hours.
- **Runs**: empty repeats until you stop it.
- **Stop at the first failed run** is on by default.
- **Keep every run as a new result**: off by default, so each run replaces the previous run's result; on, every run adds its own tab (within the 40-tab limit).
- SQL that changes data or server state (`insert`, `update`, `delete`, `drop`, `kill`, `resume`…; temporary `#tables` excluded; the full list is under [PROD protection](#prod-protection)) needs an explicit acknowledgement before it can start. On a PROD session the PROD confirmation is asked once more when the repeat starts, never on each run.

While repeating, an amber band shows *REPEATING*, the interval, the run count, the server, the SQL and a countdown with a progress rail, plus **Run now** and **Stop repeating**. The window title starts with `⚠ REPEATING`. The repeat stops on Stop, `Esc`, the run limit, a failure (when set), disconnecting or closing the app; Messages records why. It belongs to its own session: other tabs keep running their own SQL while it ticks, and its band reappears when you return to its tab.

## Activity monitor

Open **Tools → Activity monitor…** (also in command search). The regular login opens a **dedicated connection**; confirm the server and credentials there. The monitor runs in its own window and connection, so refreshes and detail queries can run while an editor session is busy. Closing the monitor cancels its requests and closes its connection. SAP IQ and Replication Server show **not supported for this platform yet**.

Choose **2 s**, **5 s** (default), **10 s** or **30 s** refresh, or **Pause**. **Refresh now** works while paused. Requests run one at a time, with a ten-second query timeout. Right-click the grid and choose **Duplicate session** to open a new editor tab in the main window, connected like the monitor's own session. The process list reads `master..sysprocesses`: SPID, status, login, host, program, database, command, CPU, physical IO, memory, blocked-by SPID, seconds blocked and transaction name. Counters are server values, not rates. The login's maximum-row setting applies; increase it in the monitor login if the server has more processes than that limit.

The **SPID / blocking tree** column indents blocked processes below their blockers. **HEAD** marks a head blocker in the palette's notice colors. **cycle** marks circular blocking; one edge is cut for display so each SPID appears once. **blocker absent** means that blocker was not returned in the snapshot. Filters keep matching processes and their ancestors. Click any column header to toggle its sort direction within each branch, preserving the tree. Select rows and use **Copy rows**, its context-menu entry or `Ctrl+C` to copy tab-separated values with headers.

Select a SPID for its **Current SQL** and **Locks**. The monitor reads ordered `monProcessSQLText` fragments matched to `monSysStatement`; if statement monitoring is unavailable or its history has no matching statement yet, it tries batch SQL text alone. Locks come from `monLocks`. If MDA tables are disabled, missing or not permitted, the affected pane hides and the detail heading explains why. The monitor does not enable server monitoring or change permissions. After an administrator changes access, select **Retry MDA**. No current text or no lock rows can also mean the SPID is idle or has already ended.

Right-click a process and choose **Kill spid…**. The first confirmation lists its server, SPID, login, host, program and command. The second requires typing the exact SPID number. PROD profiles show an extra production warning. **Cancel** is the default in the identity review; cancelling either dialog sends nothing. System processes (`suid <= 0`), missing logins, the monitor itself and known editor sessions on the same server cannot be killed. Killing is disabled if the monitor cannot identify its own SPID. After confirmation, the process is read again; an identity change cancels the action. The KILL batch repeats the identity and system-process checks on the server. Your ASE permissions still decide whether KILL is allowed. A successful submission may start a long rollback; refresh to see the result. A process snapshot can change at any moment, including between the final check and execution.

## Object explorer

`F4` or **Objects** opens the explorer.

- **ASE**: databases, then Tables, Views, Procedures, Triggers, Functions, Defaults, Rules and System tables. Additional branches list indexes, constraints/foreign keys, user-defined types, users, groups, roles and segments per database; **master** also lists logins (excluding passwords). Expand these entries for read-only details.
- **IQ**: the server's database, then Tables, Views, Procedures and System objects (SYS and dbo).
- Groups load on demand, 100 objects at a time (*Show 100 more…*). The filter box searches names on the server (`Enter` applies, `Esc` clears).
- Double-click an object to insert its qualified name; right-click for its actions.

Expand an ASE table for **Rows, space, locks and partitions**, **Indexes and keys**, **Triggers**, **Constraints** and **Foreign-key columns**. These branches query only when opened. Row counts use the server's `row_count()` estimate; space shows reserved/used pages and table KB with the page size, and partitions show their index IDs. Index keys retain their ordinal and ASC/DESC order. Defaults/rules offer **View code** and **Describe**. User/group details include the database group; role entries are mapped through the database's local role catalog. Use Reload to discard cached details after schema changes. Permissions, version differences and local row limits can leave lists partial; their notices and Messages show that evidence.

Replication Server has no relational catalog; open its panel from Tools or use the RS recipes.

## Generate DDL

Right-click an ASE table, index, view, procedure, trigger or user-defined type and choose **Generate DDL**. Index actions are available both in the database's Indexes branch and under a table. Resolved ASE table/code identifiers in the editor also offer this action.

Generation reads catalog metadata and opens a new **DDL · name** session tab with an editable, unsaved SQL document. The tab starts offline; save/copy the script, or explicitly connect it before running. The source document stays in its own session. Scripts include `USE` and `GO` separators and are never executed by generation.

Table scripts include column types/lengths/precision, nullability, identity, defaults and bound rules, lock scheme, primary/unique constraints, indexes (clustered/nonclustered, uniqueness and key direction), check constraints and foreign keys, including referenced database/owner names. Shared bound default/rule definitions appear once before the table and are rebound afterward. Referenced tables and alias types must already exist. Alias types use ASE's `sp_addtype`; the script identifies the required owner and any existing default/rule prerequisites. Views, procedures and triggers reuse stored source in `number, colid2, colid` fragment order, as documented in [ASE syscomments](https://infocenter.sybase.com/help/topic/com.sybase.infocenter.dc36274.1570/html/tables/X19576.htm).

Review scripts before use. Grants, physical placement, partition layout and storage options are excluded. Computed/encrypted columns and function-based indexes need a server-specific script. Missing/protected source, invalid metadata, incomplete keys and partial reads stop generation. Use unchained mode with no open transaction and disable NOEXEC before requesting DDL. Catalog access and the connection's local row limit still apply.

## Replication Server panel

On an RS session, choose **Tools → Replication Server panel…**. Tabs run `admin who`, `admin who_is_down`, `admin health`, `admin disk_space`, `admin show_connections` and `admin who, sqm`. Each loads when selected; **Refresh selected tab** replaces its snapshot. DOWN/suspended thread rows use the current theme's warning colors. Partition **Usage %** is used segments divided by total segments, following [RS admin disk_space](https://infocenter.sybase.com/help/topic/com.sybase.infocenter.dc32410.1571/doc/html/san1273713904372.html). NULL means the percentage is unknown, including invalid or zero capacity. All result sets and server messages remain available.

**Suspend connection…** and **Resume connection…** ask for the registered data server and database. They accept simple RCL identifiers (letters, digits, `_`, `$`, `#`, `@`, with a letter or underscore first). Two separate confirmations show the original RS server and exact command; Cancel is the default. PROD adds its usual statement review through the existing execution path. Confirmed actions publish results in the original session; refresh the panel afterward to see the new state.

## IQ metrics

On an IQ session, choose **Tools → IQ metrics…**. Read-only tabs show `sp_iqstatus` key/value pairs, `sp_iqdbspace` with numeric **Usage %**, `sp_iqconnection`, and `sp_iqtransaction` as both raw details and counts/workspace KB grouped by state. The transaction list is the server's transaction-control-block output, including completed versions; see [IQ sp_iqtransaction](https://help.sap.com/docs/PRODUCT_ID/a898e08b84f21015969fa437e89860c8/a5bb670c84f21015b392b24dcf28be14.html). Unknown workspace totals remain NULL. Dbspace percentages use the server's [Usage column](https://infocenter.sybase.com/help/topic/com.sybase.infocenter.dc38151.1510/html/iqrefbb/CIHDAEDC.htm); sizes with units are preserved as raw fields.

Tabs load on demand and **Refresh selected tab** takes a new snapshot. RS and IQ panels stay attached to the session/connection that opened them, even when the main session tab changes. They share that session's execution lane, so stop repeats and wait for running work before refreshing. A closed/replaced connection requires reopening the panel. Partial results, permission errors and unrecognized result shapes are labeled; raw results and messages remain visible when metric parsing is unavailable. No automatic polling runs.

## Status bar

On the right: the caret position, then the session facts reported by the server.

- **Database**: click to switch databases (ASE). IQ shows its database; RS has none.
- **SPID**: click to copy it.
- **Login** and the **charset** the server negotiated.
- The platform tag (ASE, IQ, RS) and, during multi-server work, how many extra sessions are open.
- **PROD** and a red band across the status bar while the session on screen is a production one.

## Personalization and Boberto

**Personalize** opens one window with a live preview:

- **Style**: 23 DBMON palettes (17 dark, 6 light: Alba, Sandstone, Papel, Sepia, Glaciar and Alto contraste, the last one with text at 7:1 or more for accessibility) and the accent: *Auto*, 14 presets or a **custom color** with a hue, saturation and value mixer and a hex box. On light palettes a bright accent is deepened until it reads as text.
- **Text**: interface font (bundled Inter or Segoe UI Variable) and size; SQL font (Consolas, the default, or the bundled JetBrains Mono) and size. Changes keep your SQL, selection and undo history.
- **Interface**: compact rows, soft corners, server messages panel.
- **Effects**: show Boberto, and motion (idle animations, neon edges); Windows' reduce-animation setting is honored.

**Boberto** is the companion in the header band. Click him (or the pencil) to open **Boberto Studio**: gestures, costumes, accessories, skins, backgrounds and frames. The X hides him and suspends his renderer; *View → Boberto companion* brings him back.

## Server health check

Open **Tools → Server health check** (also in Search commands). Select up to eight connected servers, use **Add server** to log in with a profile, or **Groups → Open** to log in to a saved server group. The editor SQL is unrelated to this tool. All probes are fixed, read-only queries; each server runs independently in parallel using MultiRun, with probes executed serially within its session.

The matrix has one server per row and one probe per column. **OK**, **WARN** and **FAIL** use the current palette's success, notice and error colors; the overall state is the worst probe state after applying rules, excluding ignored and disabled probes. **—** means that a probe belongs to another platform. Select a cell for its SQL, values, messages and failure details. **Export CSV** and **Export HTML** save the complete matrix. **Run health check** refreshes it; **Cancel run** cancels all outstanding probes.

| Platform | Probes |
|---|---|
| ASE | `@@version`; uptime from `tempdb` creation time; database status/status2 flags; data and log allocated/free pages and usage per database; last transaction dump and age where applicable; blocked processes; user connections and configured maximum. |
| IQ | `@@version`; `sp_iqstatus` main and temporary IQ block usage, with the full status output in details. |
| Replication Server | `admin health` (including mode and loss status when present); `admin who_is_down`. |

ASE suspect/offline/for-load databases and suspect pages fail the flag probe; dbo-only/single-user databases warn. Capacity warns **above 85%** and fails **above 95%**. Blocking warns at **1** blocked process and fails at **10**. On PROD servers, last transaction dump age warns above **24 hours** and fails above **48 hours**; on non-PROD servers this probe defaults to **Ignore**. The dump probe excludes the first three system databases, names beginning `syb`, and databases with trunc-log-on-checkpoint; `dumptrdate` is a transaction-log-dump timestamp, not proof of a full database backup. There is no full database dump probe. Missing dump times or unrecognized/empty metrics warn; query errors, permission failures and timeouts fail only the affected probes, subject to each rule's severity cap. An empty RS down-thread result is OK.

Use **Rules…** in the health check toolbar to edit **PROD servers** and **Non-PROD servers** separately. The connection profile's **Production (PROD)** flag selects the rule set for each server. **Enabled** controls execution: unchecked probes issue no SQL and show **OK · disabled by rule**. **Severity** is the maximum state a probe can produce: **Fail** preserves its normal states, **Warn** caps failures (including errors and timeouts) at WARN, and **Ignore** still reads the probe and shows **OK · ignored by rule**, retaining the observed result in details. Ignored and disabled probes never raise Overall; a server with all probes ignored or disabled is OK.

Enter per-probe **Warn threshold** / **Fail threshold** overrides for ASE data usage, log usage, user connections, last log dump, blocked processes and IQ space. Blank values inherit the global thresholds below; probes without numeric thresholds have blank, non-editable cells. Percentage thresholds require **0 ≤ warn < fail ≤ 100**. Dump hours and blocked counts require whole numbers with **1 ≤ warn < fail ≤ 2147483647**. Validation compares overrides with inherited values too. **Reset to defaults** resets both rule sets; **Save** validates and saves them, while **Cancel** discards edits. After saving, rerun the health check to apply the rules to existing results, as indicated in the status line.

Rules are stored beside the global settings in `%LOCALAPPDATA%\DBMON_SQL\health-rules.json`, with `"version": 1`, `production` and `nonProduction` arrays. Each entry has `probeId`, `enabled`, `severity` (`Ignore`, `Warn`, `Fail`), and optional `warnThreshold` / `failThreshold`. Missing entries use defaults. Unknown probe ids are retained on save and ignored during execution. A corrupt or invalid rules file uses defaults and displays a warning in the health window; it is replaced only when you save rules.

Data space uses `sysusages` and `curunreservedpgs`; log space uses `lct_admin('logsegment_freepages', dbid)`. These are capacity snapshots, not table-level accounting. Databases whose log shares a fragment with data (such as master) appear only in Data usage, labeled `data+log`; Log usage and Last log dump skip them (they cannot dump the log separately). Last log dump also skips archive databases. Offline/suspect/for-load databases are omitted from capacity queries and remain visible in the database-flags probe. Catalog visibility and supported server version determine which metrics are available. ASE flag meanings follow SAP's [sysdatabases reference](https://infocenter.sybase.com/help/topic/com.sybase.infocenter.dc36274.1600/doc/html/san1393052470321.html); IQ parsing follows [sp_iqstatus](https://help.sap.com/docs/SAP_IQ/a898e08b84f21015969fa437e89860c8/a5b8569b84f210159be8f2c13b8ea3fb.html).

Defaults work without a settings file. To customize them, create `%LOCALAPPDATA%\DBMON_SQL\health-check.json`:

```json
{
  "UsageWarnPercent": 85,
  "UsageFailPercent": 95,
  "BlockedWarnCount": 1,
  "BlockedFailCount": 10,
  "DumpWarnHours": 24,
  "DumpFailHours": 48,
  "ServerTimeoutSeconds": 60
}
```

The timeout is a total deadline per server (1–3600 seconds), independent of a profile's unlimited-query setting. Warning thresholds must be below failure thresholds. Invalid settings are reported before a run starts. Cancellation asks the ODBC driver to cancel and waits for it to release the sessions; closing an active dialog requests cancellation and keeps it open until the driver returns. Tools suppress user execution options, plan/IO capture and trailing user SQL. Stop repeat execution before opening a tool.

## Object comparison

Open **Tools → Compare objects**, add/log in to profiles or open a group, and select at least two SAP ASE connections. In the comparison window choose the **Left** and **Right** connections, each side's **database**, and **Tables**, **Procedures**, **Views** or **Triggers**. Click **Compare objects**. IQ and RS are supported by health checks; this object's catalog comparison currently targets ASE.

Objects match on separate, case-sensitive **owner and name** fields. The list shows **Only in left**, **Only in right**, **Different**, **Same**, or **Unavailable**. Select an object for a unified line diff (minus lines from the left; plus lines from the right) or the two definitions side by side. These views support selection and copying. Nothing is applied to either server and no migration SQL is executed.

Code reads ordered `syscomments` text through `CatalogSql.ObjectText`; hidden, missing or inaccessible definitions are unavailable, never reported as equal empty code. **Normalize SQL whitespace** collapses formatting outside literals, quoted identifiers and comments when calculating object status. Changing it reuses the captured definitions; the displayed diff always preserves original text and line numbers, so it can still show formatting differences for a normalized **Same** object. Line endings compare consistently across Windows and Unix text.

Tables compare column order, name, datatype, length, nullability, identity, precision and scale, plus logical index names, descriptions and ordered keys from `sp_helpindex`. Physical index IDs and storage counters do not affect equality. Constraints, defaults, permissions and table data are outside this comparison. Inventories reflect what the login can see; an object absent from that inventory is reported only on the other side. A failed/partial inventory stops comparison rather than treating unread objects as missing. Individual definition errors appear as **Unavailable** while other objects continue.

Comparisons allow 2,000 objects per side, 4 MiB of definition characters per object and 32 MiB per side, in addition to the profile's result limits. Both sides read concurrently, with a 60-second command timeout and a five-minute overall deadline. A large line diff exceeding four million working LCS cells directs you to the side-by-side definitions. **Cancel run** also waits for both drivers before releasing the sessions. Metadata can change during a scan; use suitable snapshots or a quiet period when a consistent catalog view is needed.

## Files and local data

Everything the app stores lives in `%LOCALAPPDATA%\DBMON_SQL`:

| File | Content |
|---|---|
| `appearance.json` | Palette, accent, fonts, sizes, options. |
| `profiles.dat` | Saved logins, DPAPI-encrypted for your Windows user. |
| `library.json` | My recipes. |
| `server-groups.json` | Saved server groups for multi-server runs (any server type; Replication Server and IQ entries without a database are kept). |
| `health-check.json` | Optional health thresholds and per-server timeout. |
| `health-rules.json` | Versioned per-probe PROD / non-PROD execution, severity and threshold rules. |
| `run.json` | Run settings: *Continue after errors*. |
| `BobertoRuntime\` | The companion's WebView2 profile (his look). |

Builds before 16 October 2026 used `%LOCALAPPDATA%\ASE ISQL Desktop`; the first start of a newer build moves it once (or copies it when it is in use).

## Limits

| Limit | Value |
|---|---|
| SQL text per execution | 1 MiB |
| `GO` batches per execution | 256 |
| `go N` count | 1 to 1000; 10,000 batch executions per run in total |
| Query timeout | 0 (none) to 3600 s |
| Rows per execution | Profile setting (default 10,000; up to 1,000,000) |
| Values per execution | 64 MiB |
| One cell | 1 MiB (never silently truncated) |
| Result sets per execution / columns per result | 128 / 1,024 |
| Result tabs | 40 (oldest unpinned close first) |
| Pinned results | 8, 64 MiB in total |
| Messages per execution | Up to 20,000 (first 10,000 + last 10,000) or 8 MiB of text, with one counted marker for the omitted middle; the Messages panel shows the first 250 + last 250 |
| Query history | 100 queries across sessions · newest first · 32 MiB file limit |
| Repeat interval | 5 seconds to 24 hours |

When a limit is reached, the output is marked partial and Messages explains which limit applied.
