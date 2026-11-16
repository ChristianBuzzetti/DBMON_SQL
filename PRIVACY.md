# DBMON SQL privacy policy

*Last updated: November 16, 2026*

DBMON SQL is a SQL workbench for SAP ASE, SAP IQ and SAP Replication Server on Windows. It works entirely on your PC.

**What DBMON SQL does not do**

- It does not collect, send or sell personal data.
- It has no accounts, analytics, telemetry, ads or tracking.
- Your SQL, results, history and settings never leave your PC, except what you send to the database servers you connect to.

**Network use**

- DBMON SQL connects only to the database servers you choose, through the SAP ODBC drivers installed on your PC. What you run there is governed by that server's owner.
- Every connection identifies itself to the server as `DBMON SQL`.
- The app makes no other network connections. Links in the app (for example to SAP documentation) open in your browser only when you click them.

**What stays on your PC**

- Connection profiles, appearance settings, query history, your own recipes and server groups, in `%LOCALAPPDATA%\DBMON_SQL` for your Windows user.
- Passwords are never written to disk unless you choose **Remember password**. Then they are encrypted with your Windows user (DPAPI) and can only be read by that user on that PC.
- Passwords are redacted from the diagnostics the app shows you.
- DBMON SQL reads your `sql.ini` and the ODBC driver list to offer your server aliases. It never changes them.

**Boberto**

Boberto, the animated companion, runs from files inside the app in an isolated Microsoft Edge WebView2 profile. No SQL, results or credentials are passed to it, and it loads nothing from the internet. WebView2 is a Windows component governed by Microsoft's privacy statement.

**Children**

DBMON SQL does not knowingly collect any information from anyone, including children.

**Contact**

Questions about this policy: open an issue at https://github.com/ChristianBuzzetti/DBMON_SQL/issues
