# DBMON SQL user guide

DBMON SQL is a Windows workbench for writing and running SQL against SAP ASE, SAP IQ and SAP Replication Server (RS). This guide walks through each part of the window.

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
- [Object explorer](#object-explorer)
- [Status bar](#status-bar)
- [Personalization and Boberto](#personalization-and-boberto)
- [Files and local data](#files-and-local-data)
- [Limits](#limits)

## Connecting

The login opens when the app starts (and with `F11` or **Connection → Connect…**).

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

**Advanced options** holds manual host and port, the TLS CA file (`TrustedFile`), the IQ server name (optional when one IQ server listens on the host and port), login timeout, query timeout and the maximum number of rows per execution.

For SAP IQ servers that require a client identity, enable **Encrypt data (TLS)** and select both the trusted `.crt` under **Browse CA…** and the `.id` under **Browse identity…** in Advanced options. Enter the private key password in **Client identity password**, separately from the database login password. Saving a profile retains both paths. Enable **Remember identity password** to save the key password with that profile, encrypted with Windows DPAPI for your user. This is independent of **Remember password** for the database login; both are off by default. Save the profile with **Profiles → Save current**, or connect with an existing loaded profile, to keep the selected passwords. Loading it again restores only the passwords you chose to remember. Changing the identity file or leaving SAP IQ clears the identity input and its Remember checkbox.

When connecting by IP to a server whose certificate identifies a DNS name, set **Expected server certificate name** to the verified Common Name (CN). DBMON sends `CERTIFICATE_NAME` and validates that name and the certificate trust chain. Leave it empty to use normal hostname/IP verification. This field is separate from **IQ server name**, which selects the engine (`ENG`/`ServerName`). DBMON does not silently enable `SKIP_CERTIFICATE_NAME_CHECK`; an explicit certificate name keeps server identity verification. See SAP's [certificate name checking reference](https://help.sap.com/docs/SAP_IQ/8e6989ea146b41d78f671443295cd7a0/561a2182636942cda909f529203c467c.html).

IQ TLS values containing semicolons, parentheses, braces, quotes, control characters or leading/trailing whitespace are rejected before calling the driver: these values would be ambiguous in its nested TLS option syntax. DBMON sends the native `Encryption=TLS(...)` clause without surrounding ODBC braces; those braces cause IQ error `-832` (unrecognized encryption type). Diagnostics hide both passwords. Certificate trust and server identity verification remain enabled; a TLS failure never triggers an unencrypted retry. See SAP's [connection parameters reference](https://help.sap.com/doc/1948d3f607fc4e7b9745d07669ad0f1b/16.2.0/en-US/SAP_IQ_Communication_Reference_en.pdf) for `IDENTITY`, `IDENTITY_PASSWORD` and `TRUSTED_CERTIFICATES`.

**Test connection** opens a temporary session and runs `SELECT 1` (`admin version` on Replication Server). **Connect** opens the session and closes the dialog. The *Information* tab shows the driver in use and the `sql.ini` that was read.

**Profiles** keep the form under a name. They are stored in `%LOCALAPPDATA%\DBMON_SQL\profiles.dat`, encrypted with Windows DPAPI for your user. **Forget password** removes only the database password; **Forget identity password** removes only the key password. Each action also clears that secret from the loaded form. Saving with either Remember checkbox off removes that stored password. Overwriting or forgetting passwords creates no `.bak` copies. Deleting a profile asks for a second click.

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

**Disconnect** (`F12`) affects only the active session. The **×** on a session tab closes that connection and confirms unsaved SQL. Closing the last tab leaves one offline workspace. Closing the window confirms unsaved documents and closes all its connections.

Sessions run independently. Start a long query, switch to another engine's tab and work there: each tab keeps its own query in flight, its own Run and Cancel state, its own status line and its own messages, and publishes its results into its own tab when they arrive. A tab that is working shows a **▶** before its name, and the result it produced while you were away is already selected when you return. Inside one session the rules are unchanged: one query at a time, and a repeat there blocks that session's `F9`, multi-server run and catalog until it stops.

A session that is running or repeating cannot be closed: stop it first, and the English status says so. The caution band, the Run chevron glyph and the title warning describe the session on screen, so they follow the tab you select. **Run on several servers** (`Shift+F9`) uses the active connection and its separately managed extra servers.

Appearance, Boberto, profiles, SQL history, recipes, server groups and command search are shared by the window. Session tabs are held in memory for the window's lifetime; they do not launch another process or window.

## The window

The window title names the open document and the product, with a `*` while it has unsaved changes. The server, platform and database are not repeated there: they belong to the session tab and the status bar, which are always on screen, and one title cannot speak for several sessions at once.

The toolbar keeps one row: **Run** with its chevron ▾ (run selection, current line, several servers, repeat), **Cancel**, the file glyphs (new, open, save), **Objects** and **Recipes**. Session tabs sit below the toolbar; the active session has an accent border and underline, and **+ Session** opens another workspace. In windows narrower than 1180 DIP, labels turn into glyphs (tooltips name them); below 900 DIP Boberto's band steps aside so the workspace keeps the header.

## The editor

The SQL editor is [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) with SQL highlighting in the palette's colors.

- **Find** `Ctrl+F`, **find and replace** `Ctrl+H`.
- **Bookmarks**: `Ctrl+F2` toggles, `F2` / `Shift+F2` jump to the next / previous one. Bookmarks follow inserted lines.
- **Editor menu** and right-click: undo/redo, copy/paste, comment or uncomment lines, duplicate line (`Ctrl+Shift+D`), selection to upper or lower case.
- **Word wrap** `Alt+Z`; **zoom** `Ctrl++`, `Ctrl+-`, `Ctrl+0`, or `Ctrl` with the mouse wheel over the editor.
- **Files**: `Ctrl+N` new, `Ctrl+O` open, `Ctrl+S` save, `Ctrl+Shift+S` save as. Files are UTF-8; the previous version is kept as a backup when you overwrite. Unsaved changes are confirmed before they are discarded.
- **History** `Ctrl+R`: the latest 100 queries across sessions, newest date/time first; double-click one to load its complete SQL. Stored as plaintext in `%LOCALAPPDATA%\DBMON_SQL\query-history.json` with atomic-write backups. Keep sensitive SQL out of history. Unreadable history files are preserved and a warning is shown instead of overwriting them.
- **Command search** `Ctrl+K`: every command by name.

Right-clicking an identifier (`orders`, `dbo.orders`, `sales..orders`, `[my table]`) offers **object actions** for it: on ASE, *Select TOP 100*, `sp_help`, `sp_depends`, `sp_helpindex`, `sp_spaceused` and *View code*; on IQ, *Select TOP 100*, `sp_iqcolumn`, `sp_iqindex` and `sp_iqtablesize`.

## Running SQL

| Keys | What runs |
|---|---|
| `F9` or **Run** | The selection if there is one, otherwise the whole script. On a result tab, see [re-running a result](#re-running-a-result). The chevron next to Run lists the other ways to run. |
| `Ctrl+E` | Only the selection; with nothing selected it reports that and runs nothing. |
| `Alt+F9` | The line under the caret. |
| `Esc` or **Cancel** | Cancels the running execution (best effort in ODBC; partial results are reported as such). |

Scripts are split on `GO` lines, exactly like `isql`; `GO` inside strings, comments or brackets is not a separator. Each execution shows its elapsed time in the status bar while it runs.

## Results

Every result set opens in its own tab: **Result 1**, **Result 2**…

- **Accumulate**: a new execution adds its tabs after the existing ones. Up to 40 result tabs are kept; beyond that the oldest unpinned ones close and Messages says so.
- **Typed values**: numbers, dates and binary values keep their types; `NULL` is shown as blank but stays a real `NULL` for copying and exporting. Binary values are shown as hex.
- **Columns** open wide enough for their first rows (up to 520 px) and can be resized, reordered and sorted. Unnamed expressions are labelled *(no column name)*.
- **Filter**: the box above each grid filters rows locally, without querying the server.
- **Row details** shows the current row vertically. Selecting numeric cells shows SUM, AVG, MIN and MAX in the status bar.
- **Pin** keeps a result open; it is never replaced and survives new executions (up to 8 pinned results).
- **Rename** with a double-click on the tab header, or *Rename resultset…*. **Middle-click** the header to close it.

Right-click the grid for:

- **Copy**, *Copy with headers*, *Copy headers only*, *Copy all*, *Copy all with headers* and **Copy delimited** (tab, comma, semicolon, pipe). Ordinary Copy/Ctrl+C and Copy all preserve literal text, including SQL line breaks and double quotes, without CSV quoting. Columns are separated by tabs and result rows by newlines. Use Copy delimited or CSV export when you need escaped, unambiguous fields for a spreadsheet. Copies follow the visible column order and source values, not clipped cell text.
- **View source SQL**, **Freeze first column**, **Bar chart…**, **Transpose result**.
- **Export** to JSON, Markdown, an HTML report, a SQL `IN` list or `INSERT` statements. CSV export is under *Tools*.

### Re-running a result

With a result tab selected, `F9` (or **Run**) runs **that result's SQL again on the session it came from** and replaces its tabs in place, keeping the names you gave them. A pinned result is never replaced: the new run is added beside it. On the Query tab, `F9` runs the editor as usual.

Results from a multi-server run are re-run with *Run on…* (`Shift+F9`); results from an earlier session cannot be re-run on a new one.

## Messages

The panel under the workspace lists the latest execution's messages: time, code, source and text. Server prints, warnings, errors, "rows affected" and client notes appear here and never as result grids. Right-click a message and choose *View original diagnostic* to see the driver's full text (with your password redacted).

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

- **Every**: 5 seconds to 24 hours, in seconds, minutes or hours.
- **Runs**: empty repeats until you stop it.
- **Stop at the first failed run** is on by default.
- **Keep every run as a new result**: off by default, so each run replaces the previous run's result; on, every run adds its own tab (within the 40-tab limit).
- SQL that changes data or server state (`insert`, `update`, `delete`, `drop`, `kill`, `resume`…; temporary `#tables` excluded) needs an explicit acknowledgement before it can start.

While repeating, an amber band shows *REPEATING*, the interval, the run count, the server, the SQL and a countdown with a progress rail, plus **Run now** and **Stop repeating**. The window title starts with `⚠ REPEATING`. The repeat stops on Stop, `Esc`, the run limit, a failure (when set), disconnecting or closing the app; Messages records why. It belongs to its own session: other tabs keep running their own SQL while it ticks, and its band reappears when you return to its tab.

## Object explorer

`F4` or **Objects** opens the explorer.

- **ASE**: databases, then Tables, Views, Procedures, Triggers, Functions and System tables per database.
- **IQ**: the server's database, then Tables, Views, Procedures and System objects (SYS and dbo).
- Groups load on demand, 100 objects at a time (*Show 100 more…*). The filter box searches names on the server (`Enter` applies, `Esc` clears).
- Double-click an object to insert its qualified name; right-click for its actions.

Replication Server has no catalog to browse; use the RS recipes.

## Status bar

On the right: the caret position, then the session facts reported by the server.

- **Database**: click to switch databases (ASE). IQ shows its database; RS has none.
- **SPID**: click to copy it.
- **Login** and the **charset** the server negotiated.
- The platform tag (ASE, IQ, RS) and, during multi-server work, how many extra sessions are open.

## Personalization and Boberto

**Personalize** opens one window with a live preview:

- **Style**: 19 DBMON palettes (17 dark, 2 light) and the accent: *Auto*, 14 presets or a **custom color** with a hue, saturation and value mixer and a hex box. On light palettes a bright accent is deepened until it reads as text.
- **Text**: interface font (bundled Inter or Segoe UI Variable) and size; SQL font (Consolas, the default, or the bundled JetBrains Mono) and size. Changes keep your SQL, selection and undo history.
- **Interface**: compact rows, soft corners, server messages panel.
- **Effects**: show Boberto, and motion (idle animations, neon edges); Windows' reduce-animation setting is honored.

**Boberto** is the companion in the header band. Click him (or the pencil) to open **Boberto Studio**: gestures, costumes, accessories, skins, backgrounds and frames. The X hides him and suspends his renderer; *View → Boberto companion* brings him back.

## Files and local data

Everything the app stores lives in `%LOCALAPPDATA%\DBMON_SQL`:

| File | Content |
|---|---|
| `appearance.json` | Palette, accent, fonts, sizes, options. |
| `profiles.dat` | Saved logins, DPAPI-encrypted for your Windows user. |
| `library.json` | My recipes. |
| `server-groups.json` | Saved server groups for multi-server runs. |
| `BobertoRuntime\` | The companion's WebView2 profile (his look). |

Builds before 16 October 2026 used `%LOCALAPPDATA%\ASE ISQL Desktop`; the first start of a newer build moves it once (or copies it when it is in use).

## Limits

| Limit | Value |
|---|---|
| SQL text per execution | 1 MiB |
| `GO` batches per execution | 256 |
| Rows per execution | Profile setting (default 10,000; up to 1,000,000) |
| Values per execution | 64 MiB |
| One cell | 1 MiB (never silently truncated) |
| Result sets per execution / columns per result | 128 / 1,024 |
| Result tabs | 40 (oldest unpinned close first) |
| Pinned results | 8, 64 MiB in total |
| Messages per execution | 500 (older ones are counted as omitted) |
| Query history | 100 queries across sessions · newest first · 32 MiB file limit |
| Repeat interval | 5 seconds to 24 hours |

When a limit is reached, the output is marked partial and Messages explains which limit applied.
