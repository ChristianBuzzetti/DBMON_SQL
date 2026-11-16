namespace AseIsql.Desktop;

/// <summary>One block of a guide topic: numbered steps, bullet tips, or keyboard shortcuts.</summary>
public sealed record GuideBlock(string Heading,string Kind,IReadOnlyList<(string A,string B)> Items);

/// <summary>A guide topic. <see cref="Try"/> are (button label, action id) handled by the main window.</summary>
public sealed record GuideTopic(string Id,string Glyph,string Title,string Lead,IReadOnlyList<GuideBlock> Blocks,IReadOnlyList<(string Label,string Action)> Try);

/// <summary>
/// Everything DBMON SQL can do, written for the people who use it (the same ground as docs/user-guide.md, shorter).
/// Each topic: what it is, how to use it step by step, tips and shortcuts, and buttons that open the feature.
/// </summary>
public static class GuideContent
{
    private static GuideBlock Steps(string heading,params string[] items)=>new(heading,"steps",items.Select(i=>(i,"")).ToList());
    private static GuideBlock Tips(string heading,params string[] items)=>new(heading,"tips",items.Select(i=>(i,"")).ToList());
    private static GuideBlock Keys(string heading,params (string Key,string What)[] items)=>new(heading,"keys",items);

    public static IReadOnlyList<GuideTopic> All { get; }=
    [
        new("start","","Getting started",
            "DBMON SQL is a fast, keyboard-first workbench for SAP ASE, SAP IQ and SAP Replication Server. Write SQL, run it, read typed results and server messages apart, and keep everything you use often a keystroke away.",
            [
                Steps("YOUR FIRST QUERY",
                    "The login opens when the app starts. Pick the server type, an alias from sql.ini, your login and password, then Connect.",
                    "Write SQL in the editor, or open Recipes for a ready-made query.",
                    "Press F9. Each result set opens in its own tab, and server messages go to the Messages panel below."),
                Tips("GOOD TO KNOW",
                    "Cancelling the login keeps the app open offline: you can write, open and save SQL, and nothing runs until you connect.",
                    "Ctrl+K searches every command by name, so you never need to remember where a menu lives.",
                    "While SQL runs, the logo next to the app name keeps turning. Esc cancels."),
            ],
            [("Connect","connect"),("Search commands","commands")]),

        new("connect","","Connecting",
            "Connections are made with your sql.ini and the credentials you type. Save them as profiles so the next login is one click.",
            [
                Steps("CONNECT TO A SERVER",
                    "Choose the Server type: SAP ASE, SAP IQ or Replication Server. The app never guesses it from the alias.",
                    "Pick an alias from sql.ini, or open Advanced options to type host and port.",
                    "Enter login and password, then Test connection or Connect.",
                    "Save the form as a profile. The password is stored only if you tick Remember password, encrypted for your Windows user."),
                Tips("OPTIONS WORTH KNOWING",
                    "Production (PROD) marks a server as production: its session shows a red band and SQL that changes state asks for confirmation.",
                    "Advanced options hold the TLS CA file, the IQ server name and client identity, login and query timeouts (0 = no timeout) and the row limit.",
                    "The Information tab shows the driver in use and the sql.ini that was read."),
                Keys("SHORTCUTS",("F11","Connect"),("F12","Disconnect the active session")),
            ],
            [("Open the login","connect")]),

        new("sessions","","Session tabs",
            "Several connections live side by side in one window. Each tab keeps its own SQL, undo history, results, messages and object explorer.",
            [
                Steps("WORK WITH SESSIONS",
                    "Click + Session to open another workspace with the same login, or F11 to connect a new one.",
                    "Switch tabs freely: a long query keeps running in its own tab, marked with ▶, while you work elsewhere.",
                    "Right-click a tab for Connect…, Disconnect, Duplicate session and Close session."),
                Tips("RULES",
                    "One query at a time per session; other sessions are independent.",
                    "A session that is running or repeating cannot be closed: stop it first.",
                    "Appearance, Boberto, profiles, history, recipes and server groups are shared by the window."),
            ],
            [("New session","newsession")]),

        new("editor","","The editor",
            "An AvalonEdit SQL editor with syntax colors in your palette, bookmarks, autocomplete and object actions on right-click.",
            [
                Keys("EDITING",("Ctrl+F / Ctrl+H","Find / find and replace"),("Ctrl+Space","Autocomplete tables, keywords and system objects"),("Ctrl+Shift+D","Duplicate line"),("Ctrl+F2","Toggle bookmark"),("F2 / Shift+F2","Next / previous bookmark"),("Alt+Z","Word wrap"),("Ctrl++ Ctrl+- Ctrl+0","Editor zoom")),
                Keys("FILES AND HISTORY",("Ctrl+N / Ctrl+O","New / open"),("Ctrl+S / Ctrl+Shift+S","Save / save as"),("Ctrl+R","Query history: the latest 100 queries")),
                Tips("OBJECT ACTIONS",
                    "Right-click a table, view or procedure name in your SQL for sp_help, row inspection, code, dependencies and more.",
                    "The app checks the object's database, owner and type first, and explains why when it cannot."),
            ],
            [("Query history","history"),("Find","find")]),

        new("run","","Running SQL",
            "Run the whole script, the selection or one line. Scripts split on GO like isql, and go N repeats a batch.",
            [
                Keys("WAYS TO RUN",("F9","The selection, or the whole script"),("Ctrl+E","Only the selection"),("Alt+F9","The current line"),("Shift+F9","Run on several servers"),("Ctrl+Shift+F9","Repeat on a timer"),("Esc","Cancel the run (and stop a repeat)")),
                Tips("ISQL-STYLE",
                    "go N runs the batch above it N times (1 to 1000) and Messages ends with a summary.",
                    "Continue after errors (Run ▾) records a failed batch and goes on with the next one.",
                    "Run without timeout (maintenance) is for long dbcc, reorg or dump work; Esc still cancels.",
                    "The chevron next to Run lists every way to run."),
            ],
            [("Fill parameters","params")]),

        new("results","","Results",
            "Every result set opens in its own tab, with typed values and real NULLs. Pin, filter, transpose, chart, copy and export.",
            [
                Tips("WORKING WITH RESULTS",
                    "F9 on a result tab re-runs that result's SQL on the session it came from and replaces it in place.",
                    "Pin keeps a result (up to 8); new runs never replace it.",
                    "The box above a grid filters rows locally. Selecting numeric cells shows SUM, AVG, MIN and MAX in the status bar.",
                    "Double-click a tab header to rename it; middle-click to close it.",
                    "Text output (isql style) shows the whole run as monospace text you can copy or save."),
                Tips("RIGHT-CLICK THE GRID",
                    "Copy as formatted table for Outlook and Teams, or copy with headers, all rows or delimited.",
                    "Include executed query adds the SQL that produced the result to any copy.",
                    "Export to JSON, Markdown, an HTML report, an IN list or INSERT statements; CSV is under Tools."),
            ],
            []),

        new("messages","","Messages",
            "Server prints, warnings and errors go to their own panel with time, code and source, never into a fake result grid.",
            [
                Tips("THE MESSAGES PANEL",
                    "Right-click a message and choose View original diagnostic for the driver's full text, with your password redacted.",
                    "Long output keeps the first and last 250 rows with one marker for what was omitted; the isql text view keeps the full run.",
                    "Hide it from View → Server messages; it comes back by itself when a run reports an error."),
            ],
            []),

        new("recipes","","Recipes",
            "94 built-in recipes tagged ASE, IQ or RS (sessions, blocking, locks, space, log, statistics, sp_iq*, admin who, queues, DSI…) plus your own.",
            [
                Steps("USE A RECIPE",
                    "Click Recipes on the toolbar (or Library → Recipes…).",
                    "The list shows the connected platform's recipes; pick one.",
                    "Insert on a new line puts it below the caret and selects it, so Ctrl+E runs just that. Ctrl+Z removes it."),
                Tips("MY RECIPES",
                    "Library → Save as recipe… stores the selection or query.",
                    "Import… and Export… exchange recipes as JSON.",
                    "Recipes that change things are in DBA operations and Operations and describe their effect first."),
            ],
            [("Open recipes","recipes")]),

        new("params","","Parameters",
            "Write :name anywhere in SQL to make it a parameter. A form asks for the values with a live preview.",
            [
                Steps("FILL PARAMETERS",
                    "Write SQL such as select * from master..monLocks where spid = :spid.",
                    "Run it, insert a recipe that has parameters, or press Ctrl+Shift+P.",
                    "Type each value and check the preview; then run."),
                Tips("SAFE BY DESIGN",
                    "Values inside quotes are escaped; outside quotes a value must be one name or number, so it can never add statements.",
                    "Known names get defaults (the current database for :database) and values are remembered for the session only."),
            ],
            [("Fill parameters","params")]),

        new("multi","","Run on several servers",
            "Run the same SQL on many sessions in parallel, with per-server results, a combined view and a diff of what differs.",
            [
                Steps("RUN ON SEVERAL SERVERS",
                    "Press Shift+F9 (or Run ▾ → Run on several servers…).",
                    "Add servers with + Add server…, or open a saved group.",
                    "Run: each server gets its own results, plus All · n with a server column and a Diff tab."),
                Tips("GOOD TO KNOW",
                    "One failing server does not stop the others; messages are labelled with their server.",
                    "Groups saves the listed servers for next time."),
            ],
            [("Run on several servers","multi")]),

        new("repeat","","Repeat on a timer",
            "Re-run a query every few seconds, minutes or hours, with an amber band that shows the run count and countdown.",
            [
                Steps("START A REPEAT",
                    "Press Ctrl+Shift+F9 (or Run ▾ → Repeat every…).",
                    "Choose the interval (5 seconds to 24 hours) and, optionally, how many runs.",
                    "Start. Run now and Stop repeating are on the band; Esc stops it too."),
                Tips("GOOD TO KNOW",
                    "MDA deltas turns cumulative ASE monitoring counters into what changed between runs.",
                    "SQL that changes data or server state needs an explicit acknowledgement before it can repeat.",
                    "A repeat belongs to its session; other tabs keep working."),
            ],
            [("Repeat every…","repeat")]),

        new("prod","","PROD protection",
            "Mark production servers in the login. Their sessions show a red band and SQL that changes state asks before it runs.",
            [
                Tips("HOW IT WORKS",
                    "Tick Production (PROD) in the login or the saved profile.",
                    "Before insert, update, delete, DDL, kill, dump, dbcc and similar statements run, a confirmation lists the server and every statement found.",
                    "Cancel is the default: Enter and Esc never run it; only Run on PROD does.",
                    "It also covers re-running results, recipes, repeats and multi-server runs. It is a warning aid, not a permission check."),
            ],
            [("Open the login","connect")]),

        new("objects","","Object explorer and DDL",
            "Browse ASE databases and IQ tables, views and procedures with server-side filters and paging, and generate DDL.",
            [
                Steps("BROWSE OBJECTS",
                    "Press F4 or click Objects.",
                    "Expand a database and a group; type in the filter box and press Enter to search names on the server.",
                    "Double-click an object to insert its qualified name; right-click it for its actions."),
                Tips("GENERATE DDL (ASE)",
                    "Right-click a table, index, view, procedure, trigger or type and choose Generate DDL.",
                    "The script opens in a new offline DDL tab: review it, save it, or connect it before running."),
            ],
            [("Open the object explorer","objects")]),

        new("activity","","Activity monitor",
            "Processes, blocking tree, current SQL, plan and locks of an ASE server, refreshed on its own dedicated connection.",
            [
                Steps("OPEN THE MONITOR",
                    "Tools → Activity monitor… and confirm the server and credentials for its dedicated connection.",
                    "Choose the refresh (2 s, 5 s, 10 s, 30 s) or Pause; Only active hides idle sessions.",
                    "Select a SPID for its Current SQL, Plan (on demand) and Locks."),
                Tips("GOOD TO KNOW",
                    "The SPID / blocking tree indents blocked processes below their blockers; HEAD marks a head blocker.",
                    "Kill spid… asks twice, the second time for the exact SPID; system processes and your own sessions cannot be killed."),
            ],
            [("Open the activity monitor","activity")]),

        new("health","","Server health check",
            "Fixed, read-only probes across up to eight servers at once: version, uptime, database flags, space, log dumps, blocking and connections.",
            [
                Steps("RUN A HEALTH CHECK",
                    "Tools → Server health check…",
                    "Select connected servers, add one with Add server, or open a saved group.",
                    "Run health check: one row per server, OK, WARN or FAIL per probe. Select a cell for its SQL and details."),
                Tips("RULES AND EXPORT",
                    "Rules… sets separate PROD and non-PROD rules: enable, severity cap and thresholds per probe.",
                    "Export CSV and Export HTML save the whole matrix."),
            ],
            [("Open the health check","health")]),

        new("compare","","Object comparison",
            "Compare tables, procedures, views or triggers between two SAP ASE connections, read-only.",
            [
                Steps("COMPARE OBJECTS",
                    "Tools → Compare objects… and select at least two ASE connections.",
                    "Choose the Left and Right connections and databases, and the object type.",
                    "Compare objects: each one is Only in left, Only in right, Different, Same or Unavailable; select it for a line diff or side by side."),
                Tips("GOOD TO KNOW",
                    "Normalize SQL whitespace ignores formatting outside literals and comments.",
                    "Nothing is applied to either server."),
            ],
            [("Compare objects","compare")]),

        new("plans","","Plans (SAP ASE)",
            "See the executed or estimated plan as an operator tree, with estimate-versus-actual warnings, and compare two plans.",
            [
                Tips("THREE WAYS IN",
                    "Statistics IO (Tools → SAP ASE session options) captures the executed plan of each batch; open it with Plan viewer….",
                    "Estimated plan (no exec) compiles the selection or script without running it.",
                    "Compare plans… diffs two captured plans operator by operator."),
                Tips("READING THE TREE",
                    "Rows and reads show est → act; a chip warns when they differ by 10× or more, and flags an error at 100×.",
                    "View XML and Copy XML show the plan as ASE returned it."),
            ],
            [("Estimated plan","estimated"),("Plan viewer","plan")]),

        new("rsiq","","Replication Server and IQ",
            "Dedicated read-only panels for RS and IQ sessions.",
            [
                Tips("REPLICATION SERVER PANEL",
                    "On an RS session: Tools → Replication Server panel…",
                    "Tabs run admin who, who_is_down, health, disk_space, show_connections and who, sqm; down or suspended threads use warning colors.",
                    "Suspend connection… and Resume connection… ask twice and show the exact command."),
                Tips("IQ METRICS",
                    "On an IQ session: Tools → IQ metrics…",
                    "sp_iqstatus, sp_iqdbspace with Usage %, sp_iqconnection and sp_iqtransaction, each loaded on demand."),
            ],
            [("RS panel","rs"),("IQ metrics","iq")]),

        new("export","","Export",
            "Save a result, or stream a whole query straight to a file without opening grid tabs.",
            [
                Tips("TWO WAYS",
                    "Tools → Export result to CSV… saves the selected result.",
                    "Tools → Export query to file… runs the selection or script and streams every row to CSV, TSV or JSON Lines; the grid limits do not apply.",
                    "Esc cancels an export; the file is kept and reported as partial."),
            ],
            [("Export query to file","export")]),

        new("status","","Status bar",
            "The status bar shows what the server says about the session on screen.",
            [
                Tips("ON THE RIGHT",
                    "Database: click to switch databases (ASE).",
                    "SPID: click to copy it.",
                    "Login, the negotiated charset and the platform tag (ASE, IQ, RS).",
                    "PROD and a red band while a production session is on screen."),
            ],
            []),

        new("personalize","","Personalize and Boberto",
            "Palettes, accent, fonts, density, the logo and Boberto, all applied live and saved on this computer.",
            [
                Tips("PERSONALIZE",
                    "Style: 23 palettes (17 dark, 6 light) and any accent, including a custom colour.",
                    "Text: interface and SQL fonts and sizes. Interface: compact rows, soft corners, the messages panel.",
                    "Effects & Logo: shape, eyes, head and background of the logo, Boberto and motion."),
                Tips("BOBERTO",
                    "Boberto is the companion in the header band; click him or the pencil to open Boberto Studio for gestures, costumes, accessories and backgrounds.",
                    "The X hides him; View → Boberto companion brings him back.",
                    "Windows' reduce-animation setting is honored."),
            ],
            [("Personalize","personalize")]),

        new("keys","","Keyboard shortcuts",
            "Everything DBMON SQL does from the keyboard.",
            [
                Keys("RUN",("F9","Run the script or selection"),("Ctrl+E","Run the selection"),("Alt+F9","Run the current line"),("Shift+F9","Run on several servers"),("Ctrl+Shift+F9","Repeat on a timer"),("Esc","Cancel")),
                Keys("WORKSPACE",("Ctrl+K","Search commands"),("F4","Object explorer"),("F11 / F12","Connect / disconnect"),("Ctrl+R","Query history"),("Ctrl+Shift+P","Fill parameters"),("Ctrl+Q","Back to the editor"),("F1","This guide")),
                Keys("EDITOR",("Ctrl+N Ctrl+O Ctrl+S","New, open, save"),("Ctrl+F / Ctrl+H","Find / replace"),("Ctrl+F2 / F2","Bookmark / next"),("Ctrl+Shift+D","Duplicate line"),("Alt+Z","Word wrap"),("Ctrl++ Ctrl+- Ctrl+0","Zoom")),
            ],
            [("Search commands","commands")]),

        new("data","","Privacy, files and limits",
            "No telemetry: the app talks only to the servers you connect to. Your data stays on this computer.",
            [
                Tips("PRIVACY",
                    "Passwords are never logged or saved unless you choose Remember password; then they are encrypted with Windows DPAPI for your user.",
                    "Diagnostics shown to you have your password redacted, and every connection identifies itself as DBMON SQL.",
                    "Boberto runs from local files in an isolated profile; no SQL or credentials reach it."),
                Tips("FILES",
                    "Everything lives in %LOCALAPPDATA%\\DBMON_SQL: appearance, profiles, My recipes, server groups, health rules, history and Boberto.",
                    "Query history is plain text: keep sensitive SQL out of it."),
                Tips("LIMITS",
                    "1 MiB of SQL and 256 GO batches per run; rows per run from the profile (default 10,000).",
                    "40 result tabs (8 pinned); when a limit is reached the output is marked partial and Messages says why."),
            ],
            [("About DBMON SQL","about")]),
    ];
}
