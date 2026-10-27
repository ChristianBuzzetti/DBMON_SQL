# SQL completion

Completion is always available with **Ctrl+Space**. There is no preference to enable or disable it; previously saved completion preferences have no effect.

- Press **Ctrl+Space** in SQL code to suggest keywords, Sybase system objects and database names matching the current prefix, ignoring case. An empty prefix shows the available vocabulary.
- Use **Up/Down** or **Page Up/Page Down** to choose a suggestion, then **Tab** or **Enter** to replace the prefix. A double-click also accepts it.
- **Escape** closes the list without editing SQL. **Enter** inserts a newline when the list is closed. A plain **Tab** outside the list retains AvalonEdit indentation; **Shift+Tab** retains unindentation.
- Keywords and system objects insert in lowercase. Database names retain their cached spelling and are delimited safely when necessary. Undo reverses one accepted completion.

After `FROM`, `JOIN`, `UPDATE`, `INTO` or `TABLE`, **Ctrl+Space** requests matching tables from the connected session's effective database. For example, `select * from mon` lists visible tables beginning with `mon`. It reads the catalog, not the SQL buffer: an unexecuted `USE` does not change the database. Owner and database qualifiers restrict the lookup; IQ supports owner qualification only. Table names retain server spelling and are delimited when needed. When several owners share a name, insertion includes the owner.

Each explicit request reads at most 101 catalog rows and shows up to 100 suggestions; type a longer prefix to narrow the list. Lookup shares the query execution lane, preserves Messages and results, and requests cancellation after five seconds. Busy, disconnected, repeating, NOEXEC, chained-mode and active-transaction sessions do not run table lookups. Permission errors and malformed catalog responses produce no table suggestions. Editing, moving the caret, pressing Escape, switching sessions or changing the connection cancels a pending request; a late response cannot open a list in the new context. Driver cancellation is best effort, so the session stays busy until it returns.

Other suggestions are local. Database names come from the active session's existing database cache and current database facts. `master` is a database suggestion when present in that cache, rather than a keyword. An offline session offers keywords and the system-object vocabulary. Columns and variables are outside this vocabulary.

System objects include the 79 system table/view identifiers and 111 monitoring identifiers listed across the [SAP ASE 16.0 SP04 and 16.1 Reference Manual: Tables](https://help.sap.com/doc/a612d2d0bc2b1014a731f1be020d1d58/16.1.0.2/en-US/SAP_ASE_Reference_Manual_Tables_en.pdf), plus the requested compatibility name `mon_dbs`. The latter is not documented there as an ASE built-in. Suggestions do not establish that an object exists or is accessible on a particular server. Monitoring identifiers are inserted in lowercase as requested; on a case-sensitive ASE server, use the canonical mixed-case name from SAP's manual (for example, `monOpenDatabases`).

For a qualified table such as `FROM master..sys`, `FROM master..mon`, `FROM master.dbo.sys` or `FROM dbo.sys`, Ctrl+Space uses that catalog context. Outside table positions, or offline, qualified prefixes retain the local system-object vocabulary. Bracketed qualifiers such as `[my database]..sys` are supported. After a qualifying dot, keywords and database names are excluded.

Quoted strings, an identifier currently inside delimiters, comments (including nested block comments), variables and parameters suppress completion. The caret must be at the end of its word. Typing punctuation dismisses the list without accepting anything. Switching documents or sessions, changing a connection, applying appearance settings or leaving the window closes the list.

Implementation uses AvalonEdit's [CompletionWindow and ICompletionData](https://github.com/icsharpcode/AvalonEdit/blob/v6.3.1/ICSharpCode.AvalonEdit.Sample/article.html), preserving document replacement, caret tracking and undo ownership in AvalonEdit.
