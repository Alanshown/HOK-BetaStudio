# Model export validation — 1.4

Validated on Windows x64, 9 October 2026. This addresses the missing Mesh FBX export path in issue #7; the reporter did not supply their own failing DB.

## Real DB exports

Each DB was tested separately with the packaged worker, then its OBJ/FBX output was parsed with independent Three.js readers.

| DB | Meshes | Triangle counts | FBX bone counts |
| --- | ---: | --- | --- |
| `3200010504_0.db` | 4 | 23,722; 3,966; 68; 1,906 | 47; 4; 2; 5 |
| `3200010500_0.db` | 4 | 8,696; 3,260; 2,650; 2,122 | 69; 2; 6; 5 |

All 16 files re-imported. Positions/winding match between OBJ and FBX at five decimal places; triangle and vertex counts match export metadata. Geometry channels are finite and weighted skin indices address existing bones. Original DB SHA-256 values are unchanged. Long output paths and Chinese/Vietnamese names are covered.

Both samples contain no AnimationClip objects. FBX output correctly reports no animation takes rather than inventing them. Native animation correctness is tested separately below.

## Synthetic native FBX and desktop

- Six native outputs cover standalone mesh, rigged mesh, animations disabled, a selected clip, geometry attached to a joint, and AnimatorOverrideController replacement.
- Independent FBX re-import checks geometry, skinning and animation-take counts. AnimationMixer samples the actual root position at 0.5 seconds.
- C# checks also cover default legacy clip discovery, root path binding, exclusion of unrelated clips, invalid geometry rejection, and preservation of joint motion when separating a combined morph track.
- Real WebView2 desktop tests open one DB, select a Mesh, export FBX then OBJ through the application bridge, verify files and visible warnings, and check for frontend errors. No preview worker is created just to display the asset list.
- Cache tests distinguish a transient file lock from a persistent lock and preserve the ownership boundary.

## Reproduce

Provide compatible native helpers and authorized local samples; do not commit game DBs, exported models or machine-specific reports.

```powershell
dotnet publish backend/Hok.Model.Tests/Hok.Model.Tests.csproj -c Release -o .cache/model-tests --self-contained true
# Copy the compatible native x64 directory into .cache/model-tests/x64.
.cache/model-tests/Hok.Model.Tests.exe reports/model-fixtures
node tooling/test-model-fixtures.cjs reports/model-fixtures
dotnet run --project backend/Hok.Cache.Tests -c Release -- .cache/cache-regression

node tooling/test-model-exports.cjs build/HOK-BetaStudio-1.4-win-x64 <one-db-file> reports/model-export
node tooling/test-model-desktop.cjs build/HOK-BetaStudio-1.4-win-x64 <one-db-file> reports/model-desktop
pwsh -File tooling/test-installer-paths.ps1
pwsh -File tooling/test-installer-upgrade.ps1 -BuildDirectory build/HOK-BetaStudio-1.4-win-x64
```

Tests use PowerShell 7, separate report directories and their own desktop smoke-test profile. Run DB and desktop checks one at a time. Real model fixtures do not prove compatibility with every HOK animation format, every DCC importer or exact Unity rendering.
