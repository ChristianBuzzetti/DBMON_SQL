# Editor object actions

Right-click a single object identifier or select it before opening the Query editor menu. DBMON SQL checks the current session's object catalog before offering actions. The heading shows the database, owner, name and catalog type returned by the lookup. Normal edit commands remain available while lookup runs or when metadata is unavailable.

SAP ASE supports `name`, `owner.name`, `database.owner.name` and `database..name`, including bracketed identifiers. Unqualified names use the database reported by the same metadata batch. SQL typed in the editor, including an unexecuted `USE`, does not change that context. An omitted owner must have exactly one visible match; qualify the owner when several matches exist. No owner or `dbo` is guessed.

SAP IQ uses `name` or `owner.name` in its connected database. Tables and views come from `SYS.SYSTAB`/`SYS.SYSUSER`; procedures and functions come from `SYS.SYSPROCEDURE`. Replication Server has no relational object lookup. Temporary names beginning with `#` are explicitly unavailable because their session-specific catalog names cannot safely be guessed.

Tables offer row selection, metadata and space/index helpers. Views offer row selection and stored code. Procedures, triggers and functions offer code and the applicable platform metadata helpers. Unknown, incomplete and ambiguous matches do not offer object commands. A lookup with no visible matches does not prove that an object does not exist: permissions may hide it.

Lookup uses the application's existing connection and the same execution lock as queries and catalog requests. It reads at most two candidate objects, requests cancellation after five seconds and also cancels when the menu closes. Driver cancellation is best effort; the session stays busy until the driver returns. It adds no result tabs and preserves the previous Messages contents. Busy, disconnected, repeating and NOEXEC sessions show a disabled explanation.

The metadata batch runs only when `@@tranchained = 0` and `@@trancount = 0`. It does not alter transaction options, commit or roll back user work. Chained mode or an active explicit transaction makes lookup unavailable, including IQ connections configured for chained mode. IQ's transaction counter alone does not detect implicitly started transactions, so the same conservative guard applies there. See SAP's [transaction documentation](https://help.sap.com/docs/SAP_IQ/a898e08b84f21015969fa437e89860c8/a61490f284f21015aa60a82fc60f9e76.html) and [global-variable reference](https://infocenter.sybase.com/help/topic/com.sybase.infocenter.dc38151.1604/doc/html/san1278452894756.html).

An action belongs to the session, connection and menu request that resolved it. Switching sessions, reconnecting, closing the menu or opening another menu expires it. Open the menu again after changing the context. A selected SQL action runs only when explicitly chosen; lookup itself never executes the editor's SQL.
