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

## Static demo / GitHub Pages

`docs/index.html`, `docs/site.css`, `docs/site.js`, `docs/images/` and `docs/fonts/` form a static, self-contained screenshot tour. It has no npm dependencies, backend, telemetry or upload endpoint. Buttons switch real screenshots; they do not pretend to parse a DB in the browser. Relative URLs work under `/HOK-BetaStudio/`.

On GitHub, choose Settings → Pages → Deploy from a branch → `main` → `/docs`. No Pages settings are changed automatically. The expected URL after a successful deployment is `https://alanshown.github.io/HOK-BetaStudio/`.

## Release gate

Full binary packages remain private/draft pending the checks listed in `docs/RELEASE-CHECKLIST.md`. A GitHub draft is not a public download. Do not set `draft: false` as part of packaging.
