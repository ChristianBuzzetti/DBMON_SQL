# Security policy

## Supported versions

Security fixes go into the latest version on the `main` branch.

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Use GitHub's [private vulnerability reporting](https://github.com/ChristianBuzzetti/DBMON_SQL/security/advisories/new) for this repository and include:

- A description of the issue and its impact.
- Steps to reproduce, with any credentials, hosts and data replaced by placeholders.
- The DBMON SQL version (*Help → About*) and Windows version.

You will get an acknowledgement as soon as possible, and a fix or mitigation plan once the report is confirmed.

## How DBMON SQL handles sensitive data

- Passwords are kept in memory only for the session, redacted from diagnostics and never logged. They are written to disk only when *Remember password* is chosen, encrypted with Windows DPAPI for the current user.
- Connection profiles (`profiles.dat`) are DPAPI-encrypted as a whole.
- The app sends no telemetry and contacts only the servers you connect to.
- The Boberto companion runs from packaged local files in an isolated WebView2 profile; navigation away from it, popups, permission requests and non-local requests are blocked, and no SQL or credentials are passed to it.
- `:name` parameter values outside quotes must be single tokens, so a parameter cannot inject extra statements.
