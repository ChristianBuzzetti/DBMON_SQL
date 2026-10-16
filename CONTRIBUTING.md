# Contributing to DBMON SQL

Thanks for your interest! Issues and pull requests are welcome.

## Reporting a problem

Open an [issue](https://github.com/ChristianBuzzetti/DBMON_SQL/issues) with:

- What you did, what you expected and what happened.
- Server type and version (SAP ASE, SAP IQ or Replication Server) and ODBC driver version.
- The text from Messages and, if relevant, *View original diagnostic*.

**Never paste passwords, host names, IP addresses or business data** into an issue. Replace them with placeholders. Security problems go through [SECURITY.md](SECURITY.md), not public issues.

## Making a change

1. Fork the repository and create a branch.
2. Build and run the checks:
   ```powershell
   ./build.ps1 -Test
   ```
   If you have the SAP ASE ODBC driver installed, also run the UI checks with `./build.ps1 -Test -Publish`.
3. Add or update checks for the behavior you change: core logic in `tests/AseIsql.Checks`, UI behavior in the smoke checks (`VerifySmoke` in `MainWindow.xaml.cs`).
4. Keep the existing style: compact code that matches the surrounding file, comments that explain *why*.
5. Open a pull request describing the change and how you verified it.

## Ground rules

- Keep values typed and `NULL` intact; never show server messages as result grids.
- No synthetic data outside `--smoke`; nothing executes without a connected session.
- Never log or persist passwords unless the user asks (and then only with DPAPI).
- Product UI text is English; SQL, object names and server diagnostics are shown unchanged.
- Visual changes use the palette tokens so every palette and accent keeps working.
- Don't commit build output, `.tools`, `artifacts` or local notes.

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
