# Build inputs and local packaging

The repository includes application source and the required upstream C# source snapshot. It intentionally excludes npm/NuGet caches, game DB fixtures, private keys, game portrait libraries and compiled third-party runtimes.

## Compile source

Use Windows x64, .NET 10 SDK and Node.js/npm. Run the commands in the root README. `npm ci` and `dotnet build` restore dependencies from their manifests; no dependency archive belongs in Git.

`vendor/Studio-HoK/Keys.example.json` is an empty list. If a supported encrypted format needs keys, place your authorized key configuration at `vendor/Studio-HoK/Keys.json` locally. This file is ignored by Git. Do not publish your keys.

## Inputs for the full desktop package

| Local path | Contents | Required for |
|---|---|---|
| `.tools/media/runtime/` | `ffmpeg.exe`, `vgmstream-cli.exe` and vgmstream runtime DLLs | audio decoding and MP3 conversion |
| `.tools/native/x64/` | compatible original FBX, FMOD and codec helper DLLs | native export/decoder features |
| `assets/catalog/remote-index.seed.json` and `corrections.default.json` | validated initial HTTPS image index and exact identity corrections | 1.3 resource catalog; static game portraits are no longer packaged |
| WebView2 Runtime | installed on the target computer | desktop web interface |
| NSIS | `makensis.exe` on PATH, or its standard Windows installation | installer creation |

Media versions and source URLs are in `assets/licenses/media/NOTICE.txt`. Native helper binaries must match the P/Invoke contracts in `vendor/Studio-HoK`; substituting unrelated DLL versions is not supported. Native dependency acquisition is manual, not an automatic download script. A missing helper must not be interpreted as a successful preview/export.

`tooling/build-desktop.ps1` creates a self-contained .NET desktop package by default. Use `-NativeDirectory` if your local helper directory differs. It copies UI assets, the worker, media tools and notices. `-FrameworkDependent` creates a smaller build requiring a compatible .NET Desktop Runtime; it is not the default portable package.

`tooling/package-desktop.ps1` takes an already-built directory and produces a portable ZIP, a current-user NSIS installer and SHA-256 checksums in `deliverables/`. The EXE is not a single-file application. Both outputs are unsigned and must keep third-party notices. The installer does not download WebView2 or alter system-wide settings.

## Test scope

The 1.3.0.3 source update adds installer upgrade detection, HOK dense animation decoding, and reference-linked animation previews. See [asset coverage and known limitations](ASSET-COVERAGE-1.3.0.3.json) for fixture counts and reproducible commands. Original-object/raw export and semantic conversion are different guarantees: missing external image streams and unsupported particle simulation are reported, not fabricated. Build number 1.3.0.3 does not imply that the existing 1.3 Release attachments have been replaced.

`Hok.Contracts.Tests` runs without game DBs and uses `planning/identity-test-vectors.json`. `Hok.Catalog.Tests` validates the 1.3 index normalization, staging, atomic application, offline behavior and recovery without network access by default. Add `--live` after the project-root argument to check the official endpoints. The local fixture tests for media and rebuilding need the corresponding DB directories and, for some scripts, reports produced by earlier fixture tests. Absolute-path reports are not uploaded. Review each script's input requirements before running it.

```powershell
dotnet run --project backend/Hok.Catalog.Tests -c Release -- .
dotnet run --project backend/Hok.Catalog.Tests -c Release -- . --live
```

See [1.3 resource synchronization](RESOURCE-SYNC-1.3.md) for cache boundaries and the isolated desktop integration test. Build/package scripts now default to **1.3**. Existing published 1.2 downloads and build directories are not overwritten.

Do not use an empty public fixture directory to infer game compatibility. The source snapshot can compile without game data, but native previews and fixture integration tests require separately supplied inputs.

## Documentation

The repository contains the three README variants, build documentation and real desktop screenshots. The separately maintained demonstration page and its code are intentionally excluded; this repository is not a GitHub Pages deployment source.

## Local source checks

The automatic `Source checks` workflow was removed at the maintainer's request. The frontend build, C# builds, `Hok.Contracts.Tests`, `Hok.Catalog.Tests` and documentation checks remain available locally. These source-only checks require no private game DBs or native media/export binaries. A passing source build does not verify native export, game compatibility or installer behavior. No workflow publishes packages automatically.

## Release status and review

Version 1.2 was published by the maintainer on 2026-10-03. See [installation and checksums](INSTALL.md). Publication does not close the unresolved distribution and clean-machine checks in [RELEASE-CHECKLIST.md](RELEASE-CHECKLIST.md). Future full packages must be reviewed before publication; packaging scripts do not publish automatically.
