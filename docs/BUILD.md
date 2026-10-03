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
| `assets/portraits/` | your authorized hero and skin portraits, following catalog relative paths | full catalog imagery |
| WebView2 Runtime | installed on the target computer | desktop web interface |
| NSIS | `makensis.exe` on PATH, or its standard Windows installation | installer creation |

Media versions and source URLs are in `assets/licenses/media/NOTICE.txt`. Native helper binaries must match the P/Invoke contracts in `vendor/Studio-HoK`; substituting unrelated DLL versions is not supported. Native dependency acquisition is manual, not an automatic download script. A missing helper must not be interpreted as a successful preview/export.

`tooling/build-desktop.ps1` creates a self-contained .NET desktop package by default. Use `-NativeDirectory` if your local helper directory differs. It copies UI assets, the worker, media tools and notices. `-FrameworkDependent` creates a smaller build requiring a compatible .NET Desktop Runtime; it is not the default portable package.

`tooling/package-desktop.ps1` takes an already-built directory and produces a portable ZIP, a current-user NSIS installer and SHA-256 checksums in `deliverables/`. The EXE is not a single-file application. Both outputs are unsigned and must keep third-party notices. The installer does not download WebView2 or alter system-wide settings.

## Test scope

`Hok.Contracts.Tests` runs without game DBs and uses `planning/identity-test-vectors.json`. The local fixture tests for media and rebuilding need the corresponding DB directories and, for some scripts, reports produced by earlier fixture tests. Absolute-path reports are not uploaded. Review each script's input requirements before running it.

Do not use an empty public fixture directory to infer game compatibility. The source snapshot can compile without game data, but native previews and fixture integration tests require separately supplied inputs.

## Documentation

The repository contains the three README variants, build documentation and real desktop screenshots. The separately maintained demonstration page and its code are intentionally excluded; this repository is not a GitHub Pages deployment source.

## Source-only CI

The `Source checks` workflow builds the React frontend, C# desktop host and worker on Windows, then runs `Hok.Contracts.Tests`. It uses only public repository inputs; game DBs, artwork libraries, media executables and private native helpers are not required. It does not package, upload artifacts or publish releases. A passing run does not verify native export, game compatibility or installer behavior.

## Release status and review

Version 1.2 was published by the maintainer on 2026-10-03. See [installation and checksums](INSTALL.md). Publication does not close the unresolved distribution and clean-machine checks in [RELEASE-CHECKLIST.md](RELEASE-CHECKLIST.md). Future full packages must be reviewed before publication; packaging scripts do not publish automatically.
