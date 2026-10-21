# Boberto catalog checks

Run from the repository root with Node.js:

```powershell
node --test tests/Boberto/boberto-accessories.test.cjs tests/Boberto/boberto-gestures.test.cjs
```

The dependency-free suites originate in SMG_APPS `shared/tests` and load the desktop's packaged engine. They cover accessory selection and persistence, both accessory colors, corrected classic outfits, held props, materials, Natural color swatches, and gesture keyframes. Canvas calls are recorded to detect invalid coordinates and unbalanced state; these checks do not replace visual inspection in WebView2.

The English adapter check also preserves catalog identifiers while translating the expanded catalog. The engine and Studio scripts are copied verbatim from SMG_APPS `shared/js`; desktop adapters and the existing Neo Mirai CSS adjustments remain local.

The synchronized catalog contains 52 accessories, 14 materials, and 30 gestures. Background artwork was already identical. Run `./build.ps1 -Test` for desktop/core checks and `./build.ps1 -Test -Publish` for the development package and real WebView2 smoke checks. Product version remains 0.1.0.

## Verification on 2026-10-21

- Before synchronization, the 39 upstream checks reported 28 failures and 11 passes; after synchronization, all 40 checks passed, including the desktop translation check.
- `./build.ps1 -Test -Publish` passed: 82 core checks and 315 UI checks/measurements, without contacting ASE. Development output: `artifacts/windows-x64-dev`.
- Both packaged engine scripts matched the SMG_APPS source SHA-256 hashes. The real Studio capture displayed the expanded gesture catalog and corrected crown outfit.
- Additional Gentle AI review remained unavailable: preflight returned `operation_timeout`, `mutation_outcome: not_started`. No review approval was obtained. Existing unrelated session edits were preserved.
