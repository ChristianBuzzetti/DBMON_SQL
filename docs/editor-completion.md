# SQL completion

Completion is always available with **Ctrl+Space**. There is no preference to enable or disable it; previously saved completion preferences have no effect.

- Press **Ctrl+Space** in SQL code to suggest keywords, Sybase system objects and database names matching the current prefix, ignoring case. An empty prefix shows the available vocabulary.
- Use **Up/Down** or **Page Up/Page Down** to choose a suggestion, then **Tab** or **Enter** to replace the prefix. A double-click also accepts it.
- **Escape** closes the list without editing SQL. **Enter** inserts a newline when the list is closed. A plain **Tab** outside the list retains AvalonEdit indentation; **Shift+Tab** retains unindentation.
- Keywords and system objects insert in lowercase. Database names retain their cached spelling and are delimited safely when necessary. Undo reverses one accepted completion.

Suggestions are local. Database names come from the active session's existing database cache and current database facts; completion never queries a server. `master` is a database suggestion when present in that cache, rather than a keyword. An offline session offers keywords and the system-object vocabulary. User tables, columns and variables are outside this vocabulary.

System objects include the 79 system table/view identifiers and 111 monitoring identifiers listed across the [SAP ASE 16.0 SP04 and 16.1 Reference Manual: Tables](https://help.sap.com/doc/a612d2d0bc2b1014a731f1be020d1d58/16.1.0.2/en-US/SAP_ASE_Reference_Manual_Tables_en.pdf), plus the requested compatibility name `mon_dbs`. The latter is not documented there as an ASE built-in. Suggestions do not establish that an object exists or is accessible on a particular server. Monitoring identifiers are inserted in lowercase as requested; on a case-sensitive ASE server, use the canonical mixed-case name from SAP's manual (for example, `monOpenDatabases`).

For a qualified object such as `master..sys`, `master..mon`, `master.dbo.sys` or `dbo.sys`, Ctrl+Space suggests matching system objects and replaces only the final identifier, preserving the database and owner. Bracketed qualifiers such as `[my database]..sys` are supported. After a qualifying dot, keywords and database names are excluded.

Quoted strings, an identifier currently inside delimiters, comments (including nested block comments), variables and parameters suppress completion. The caret must be at the end of its word. Typing punctuation dismisses the list without accepting anything. Switching documents or sessions, changing a connection, applying appearance settings or leaving the window closes the list.

Implementation uses AvalonEdit's [CompletionWindow and ICompletionData](https://github.com/icsharpcode/AvalonEdit/blob/v6.3.1/ICSharpCode.AvalonEdit.Sample/article.html), preserving document replacement, caret tracking and undo ownership in AvalonEdit.
