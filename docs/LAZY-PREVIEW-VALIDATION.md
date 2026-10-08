# On-demand previews and DB recovery validation

Validated locally on Windows x64 / WebView2, 9 October 2026 (Asia/Shanghai).

## Runtime policy

- Import indexes object identities, names, class IDs, formats, container records and essential references. It does not render thumbnails, decode playable audio or build a model preview for every asset.
- Mesh, AnimationClip, Material, Shader, Font, AudioClip, VideoClip and MovieTexture payloads use deferred readers, including in small DBs. Image pixels are converted only for a requested preview/export. A linked animation can load its required scene dependencies; unrelated assets are not preloaded.
- BNK/PCK indexing retains bounded offset views, not a copy of every embedded WEM. Opening a bank displays its tree without automatically selecting or decoding its first sound.
- Preview components are separate frontend modules. Opening an image does not load the Three.js model renderer. Fonts register one temporary FontFace only after selection.
- Closing releases image pixel buffers, WebGL contexts, geometry/materials/textures, audio sources, font faces and preview artifacts. Stale frontend requests cannot replace the currently selected asset. Pending model/curve/font fetches are aborted on unmount.
- Small workspaces use separate preview/export workers. A workspace over 100,000 visible rows or 512 MiB of retained managed index memory reuses the indexed worker. This avoids multiple full copies of a very large DB. The desktop remains asynchronous; operations on that shared index are serialized.
- Shared-worker cancellation stops at safe operation boundaries and preserves the loaded index. Audio subprocesses also check cancellation. An indivisible native conversion may finish its current operation before cancellation is observed.
- Native audio conversion uses short relative filenames. Validated output is moved to the chosen destination with .NET, avoiding the decoder's long-path limitation. Desktop-owned temporary audio directories are cleaned when their worker stops.

Import still needs to read/decompress container metadata and maintain a searchable index. “On demand” does not mean zero import-time disk I/O or zero index memory. The previews are not a full Unity runtime.

## Verified checks

| Check | Result |
| --- | --- |
| Fixed corpus, one DB per process/invocation | 20 DBs in 10 fixed directories |
| Visible asset rows | 704,337; unchanged from the prior recovery baseline |
| Container/index entries | 1,318,295; all paginated raw-list entries retained |
| Recovered payload bytes | 3,524,080,507; 100% of the validated expected payload sizes |
| Payload SHA-256 comparison | All 1,318,295 entries equal the prior baseline |
| Original DB SHA-256 | All unchanged |
| Import diagnostics | 0 read errors in this corpus |
| Explicit media materialization during import | 0 bytes reported by ResourceAsset instrumentation |
| Synthetic coverage / discovery / media checks | 24 / 15 / 26 passing checks |
| Real desktop BNK preview | No audio preloaded; clicked sound decodes and plays; closing cleans up |
| Real desktop texture and mesh | Click-only loading; image canvas cleared; WebGL context lost on close |
| Real desktop animation | Click-only loading; real curve keys play; updated parse state survives filtering |
| Real desktop font | One temporary FontFace after click; none after closing |
| Large DB frontend regression | Pagination, raw export, six-face Cubemap preview, six ZIP exports, animation playback and workspace reset passed with one indexed worker |
| Cubemap ZIP validation | All six archives pass archive integrity checks and contain six PNG faces each |
| BNK/WEM/MP3 regression | One BNK, six WEMs, WEM/MP3 ZIP archives; WEM bytes match DIDX ranges, MP3s decode without errors |
| Cooperative export cancellation | Export stops; the original worker still returns the complete indexed row count |
| Beta unchanged-content rebuild | Whole Wwise package can be staged/re-staged; preview release preserves the staged item; all rebuilt DB/companion hashes equal the originals and the rebuilt DB reopens with the same row count |

Real tests use `3200010504_0.db` (audio, image, mesh), `3200050702_0.db` (animation), and `8_0.db` (large-workspace UI and font). Game DBs, exported media, machine-specific paths, dependencies and build products are not committed.

## Scope and remaining limits

Byte recovery, asset discovery, semantic parsing and runtime reproduction are separate outcomes. Deferred rows are explicitly labelled; a successful name/header read is not advertised as a complete parse. The initial metadata-only corpus contains 3,791 still-unclassified raw streams, including ownership links deferred with mesh decoding. These records remain accessible in QtsVFS Raw. This number must not be presented as zero or as proof that every proprietary payload has a complete semantic decoder.

HOK action event payloads, some custom MonoBehaviour data and ShaderVariantCollection platform caches still have explicit partial interpretations. Exact game particle/shader behavior is not reproduced by the browser renderer. Cubemap PNG export represents mip 0 in eight-bit images; the archive also preserves original mip bytes, rather than claiming lossless HDR conversion.

The build reports pre-existing ImageSharp dependency advisories and a size warning for the optional 3D JavaScript chunk. They are not hidden by the functional test results. No automatic GitHub Actions workflow is added.

## Reproduce

Copy `tests/hok_db_corpus.example.txt` to the ignored `tests/hok_db_corpus.txt` and set the paths to your authorized local test data. Keep the same ten directories for comparisons. Run one exact DB per invocation; do not run large corpus tests concurrently with another DB test or a desktop test.

```powershell
dotnet run --project backend/Hok.AssetCoverage.Tests -c Release
dotnet run --project backend/Hok.Discovery.Tests -c Release -- .
dotnet run --project backend/Hok.Media.Tests -c Release
npm --prefix frontend run build

node tooling/test-db-corpus.cjs <build-directory> <report-directory> --only <single-db> --load-only --no-export --raw-audit --verify-raw-list
node tooling/compare-corpus-recovery.cjs <baseline-report-directory> <new-report-directory>
node tooling/test-lazy-desktop.cjs <build-directory> <single-db> <report-directory> bank
node tooling/test-lazy-desktop.cjs <build-directory> <single-db> <report-directory> assets
node tooling/test-lazy-desktop.cjs <build-directory> <single-db> <report-directory> animation
node tooling/test-lazy-desktop.cjs <build-directory> <single-db> <report-directory> font
node tooling/test-qts-desktop.cjs <build-directory> <8_0.db-path> <report-directory>
node tooling/test-audio-validity.cjs <build-directory> <single-db>
node tooling/test-one-db-rebuild.cjs <build-directory> <single-db-with-wwise-package> <report-directory>
```

Desktop tests launch their own hidden smoke-test application/profile and use its CDP port 9224. They do not automate the user's browser profile. Install the test dependencies with `npm ci --prefix tooling`; media validation additionally needs the configured native codecs and 7-Zip. See [BUILD.md](BUILD.md) for the local build requirements.
