# Third-party notices

DBMON SQL is licensed under the [MIT License](LICENSE). It includes or depends on the components below, each under its own license.

| Component | Version | License | Use |
|---|---|---|---|
| [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) | 6.3.1.120 | MIT | SQL editor control |
| [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) | 1.0.4258.31 | Microsoft WebView2 SDK license (BSD-style, see the package's `LICENSE.txt`) | Hosts the Boberto companion |
| [System.Data.Odbc](https://www.nuget.org/packages/System.Data.Odbc) | 10.0.13 | MIT | ODBC access |
| [.NET runtime](https://github.com/dotnet/runtime) and WPF | 10.0 | MIT | Included in self-contained packages |
| [Inter](https://github.com/rsms/inter) font | 4.x | SIL Open Font License 1.1 | Interface type, bundled as `Assets/Fonts/Inter-*.ttf` with `Inter-LICENSE.txt` |
| [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) font | 2.x | SIL Open Font License 1.1 | SQL and results type, bundled as `Assets/Fonts/JetBrainsMono-Regular.ttf` with `JetBrainsMono-OFL.txt` |

The font license texts ship next to the fonts in every build.

**Boberto** (the companion engine, Studio, artwork and backgrounds in `src/AseIsql.Desktop/Assets/Boberto`) and the DBMON palettes are original work by Christian Buzzetti, part of this repository under the MIT License.

**Not included:** the SAP ASE ODBC driver, the SAP IQ ODBC driver and the Microsoft Edge WebView2 Runtime are installed separately by the user under their own licenses. DBMON SQL does not redistribute them. SAP, SAP ASE, SAP IQ, SAP Replication Server and Sybase are trademarks of SAP SE; this project is not affiliated with SAP.
