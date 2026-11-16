# Microsoft Store listing (DBMON SQL, Store ID 9P5K31P104WG)

Text for Partner Center → Submission → Store listings. Package: `./build.ps1 -Test -Publish -Msix` →
`artifacts/store/DBMON_SQL_<version>.0_x64.msix`. Screenshots: `docs/media` (workspace, parameters, repeat, palettes, Studio).

## Properties

- **Category:** Developer tools. Subcategory: Database.
- **Privacy policy URL:** https://github.com/ChristianBuzzetti/DBMON_SQL/blob/main/PRIVACY.md
- **Website:** https://github.com/ChristianBuzzetti/DBMON_SQL
- **Support contact:** https://github.com/ChristianBuzzetti/DBMON_SQL/issues
- **Product declarations:** no personal data collected; works with mouse and keyboard. Depends on non-Microsoft software: the SAP ASE / SAP IQ 64-bit ODBC drivers (not included).
- **System requirements:** Windows 10 version 1809 or later, x64; 4 GB RAM recommended. SAP ASE ODBC driver 64-bit (and SAP IQ ODBC driver for SAP IQ).
- **Restricted capability `runFullTrust` justification:** DBMON SQL is a desktop (WPF) SQL client. It needs full trust to load the SAP ODBC drivers installed on the PC (64-bit native DLLs registered in HKLM\SOFTWARE\ODBC), read the user's sql.ini from %SYBASE%, and connect to the database servers the user chooses.
- **Age rating questionnaire:** developer utility; no user-generated content shared with other users, no communication between users, no purchases, no location, no AI.
- **Pricing:** free. **Markets:** all.
- **Notes for certification:** DBMON SQL is a client for SAP ASE, SAP IQ and SAP Replication Server databases. Connecting needs the SAP ASE (or SAP IQ) 64-bit ODBC driver, which SAP distributes with its client software, plus a server and a login; neither is included or required to open the app. Without a server you can still try: the SQL editor (syntax colors, find/replace, bookmarks), the recipe library (Recipes button, 94 SQL recipes with parameter forms and live preview), the Guide, Personalize (palettes, fonts, logo) and Boberto Studio (the animated companion). The connection window opens at start; close it or press Cancel to reach the editor. The app makes no network connections other than to the servers the user connects to.

## English (en-US)

**Product name:** DBMON SQL

**Short description (≤ 100 chars):**
A fast, keyboard-first SQL workbench for SAP ASE, SAP IQ and SAP Replication Server.

**Description:**
DBMON SQL keeps the dense, keyboard-driven workflow of the classic isql tools that Sybase DBAs know, and adds what a modern workbench should have.

Connect to SAP ASE and SAP Replication Server through the SAP ASE ODBC driver, or to SAP IQ through the SAP IQ ODBC driver. Server aliases come straight from your sql.ini.

Write SQL in a real editor with syntax colors, find and replace, bookmarks and zoom. F9 runs the script or the selection. Results keep their types and NULLs, accumulate in tabs and can be pinned, filtered, transposed, charted and exported to CSV, JSON, Markdown, HTML, IN lists or INSERT statements. Server messages, warnings and errors stay in their own panel.

Use 94 built-in recipes for ASE, IQ and Replication Server (sessions, blocking, locks, space, log, statistics, queues and more), with parameter forms that never let a value add statements. Run the same SQL on several servers at once and see what differs. Repeat a query on a timer with a clear caution band.

Browse databases and objects, check the session at a glance in the status bar, and make it yours with 23 palettes, fonts and Boberto, an animated companion.

No account, no telemetry: DBMON SQL talks only to the servers you connect to. Passwords are saved only if you choose, encrypted for your Windows user. Open source (MIT).

Requires the SAP ASE ODBC driver (64-bit) for SAP ASE and Replication Server, or the SAP IQ ODBC driver (64-bit) for SAP IQ, available from SAP. DBMON SQL is an independent product, not affiliated with or endorsed by SAP.

**Features (one per line, ≤ 200 chars each):**
- SQL client for SAP ASE, SAP IQ and SAP Replication Server over 64-bit ODBC
- Server aliases read from your sql.ini, or host and port by hand
- Editor with syntax colors, find and replace, bookmarks, comment toggle and zoom
- Typed results that keep NULLs, in tabs you can pin, filter, transpose and chart
- Export results to CSV, JSON, Markdown, HTML, IN lists or INSERT statements
- Server messages and errors in their own panel, never mixed with results
- 94 recipes for ASE, IQ and Replication Server with safe parameters
- Run SQL on several servers at once and compare the results
- Repeat a query on a timer with a visible caution band
- 23 palettes, fonts and Boberto, an animated companion; no telemetry

**Keywords:** SQL, SAP ASE, Sybase, SAP IQ, Replication Server, isql, database, DBA

**What's new in this version:** First Microsoft Store release.
