<div align="center">
  <img src="docs/images/app-icon.png" width="112" alt="HOK BetaStudio icon">
  <h1>HOK BetaStudio</h1>
  <p>A desktop workspace for Honor of Kings assets.</p>
  <p>
    <img alt="Version 1.2" src="https://img.shields.io/badge/version-1.2-147d72?style=flat-square">
    <img alt="Windows x64" src="https://img.shields.io/badge/platform-Windows_x64-357b9b?style=flat-square">
    <img alt="C# and React" src="https://img.shields.io/badge/C%23_%2B_React-desktop-667672?style=flat-square">
    <img alt="Three interface languages" src="https://img.shields.io/badge/UI-EN_%C2%B7_%E4%B8%AD%E6%96%87_%C2%B7_VI-147d72?style=flat-square">
    <img alt="Experimental rebuilding" src="https://img.shields.io/badge/DB_rebuilding-Beta-b68a42?style=flat-square">
  </p>
</div>

<!-- README-I18N:START -->

**English** | [简体中文](./README.zh.md) | [Tiếng Việt](./README.vi.md)

<!-- README-I18N:END -->

Browse DB packages by hero and skin, inspect Unity and non-Unity entries, preview models and audio, and export selected assets in batches. A C# backend keeps parsing, previews and exports outside the interface process.

**[Screenshot tour](docs/index.html) · [Releases](https://github.com/Alanshown/HOK-BetaStudio/releases) · [Issues](https://github.com/Alanshown/HOK-BetaStudio/issues)**

> [!NOTE]
> Version 1.2 is in development. Full portable and installer packages are being prepared privately; public binary distribution is pending artwork and third-party component review. The tour is a real-screenshot presentation, not an online DB parser.

![Hero catalog in the desktop application](docs/images/01-catalog.png)

## Contents

- [Workspace and discovery](#workspace)
- [Previews and export](#export)
- [Replacement and rebuilding — Beta](#beta)
- [Project structure](#structure)
- [Build and package](#build)
- [Download and use](#download)
- [Verification](#verification)
- [Attribution and distribution](#attribution)

<a id="workspace"></a>
## Workspace and discovery

- **Browse before importing.** The local catalog shows heroes and skins on startup. Hero and skin cards use a diagonal domino entrance, with reduced-motion accessibility support.
- **Open files, open a folder, or drag them in.** Ordinary subdirectories are scanned recursively without a hard-coded depth limit. Junctions and symbolic links are skipped to avoid loops; inaccessible paths are reported.
- **Match by filename.** `3200010504.db` → skin `10504` → hero `105`. Numeric shard suffixes such as `_0` are handled separately. Recognized extensionless DBs are accepted after signature checking.
- **Default skins.** IDs ending in `00`, such as `10500`, use the hero portrait and the label “Default skin”. Unknown hero and skin IDs remain visible with stable placeholders.
- **Inspect the imported scope.** Only matching heroes and skins are shown after import, with their source DB filenames. Search by name or ID, filter asset types, page through results and batch-select assets.
- **Replace the workspace.** A new import discards the previous imported cache and pending replacements. The animated broom clears it and returns to the initial catalog; original files are not deleted.
- **Three interface languages:** English, Simplified Chinese and Vietnamese. Original asset names and catalog names are preserved.

![Two skin packages associated with hero 105](docs/images/02-skins.png)

<a id="export"></a>
## Previews and export

| Module | Preview / output |
|---|---|
| Textures and sprites | Image preview, channel toggles and zoom; PNG, TGA, BMP, JPG, raw data |
| Meshes | Interactive 3D orbit/zoom, auto-rotation and wireframe; OBJ, JSON, raw data |
| GameObject / Animator | FBX export when native helpers and references are available |
| AnimationClip | Unity YAML `.anim`, JSON, raw data; not universal standalone FBX animation conversion |
| AudioClip / Wwise audio | Local playback; original media, WAV and MP3 where decoding is supported |
| WwiseBank | Embedded-media tree and player; original BNK, ZIP of original WEM files, ZIP of converted MP3 files |
| Text, shader, font and video | Type-appropriate original content or text output, JSON and raw data as supported |
| Other package entries | Non-Unity entries are listed too; unknown or undecoded content can be retained as raw data |

Preview dialogs blur the workspace and include previous/next navigation within the same asset category. BNK navigation stays inside the selected bank. User interaction interrupts model auto-rotation.

`WwiseAudio` and `WwiseBank` identify media/container types, **not guaranteed “chat” versus “skill” categories**. A bank can contain embedded media, references to external media, or both. Only media actually present can be extracted. WEM export preserves original WEM bytes; arbitrary audio-to-WEM encoding is not implemented.

Batch export uses per-item results and collision-safe output locations. Parsing, preview and export use separate managed worker processes. Codec errors and unsupported entries are surfaced rather than reported as successful conversions.

![Filtered asset workspace](docs/images/03-assets.png)
![Model preview](docs/images/05-model.png)
![Actual BNK with six embedded audio entries](docs/images/06-audio-bank.png)
![Bank export options](docs/images/07-export.png)

<a id="beta"></a>
## Replacement and rebuilding — Beta

> [!WARNING]
> **Experimental; not recommended for normal use. Game compatibility is not verified.** Hover or focus the in-app Beta label for the warning. Passing a local parser check does not prove a rebuilt package will run in the game.

1. Select exactly one supported asset to reveal **Replace**.
2. Supply a compatible original-format binary. Successful replacements are marked; replace more entries or replace the same one again.
3. After at least one staged replacement, **Rebuild** becomes available.
4. Choose a separate output directory. The rebuilt package includes replacements and all remaining original files; the source package is not overwritten.

Current constraints: equal-length binary replacements only, within existing compressed slots; no general PNG/OBJ/MP3 import, arbitrary new assets or resized objects. Opaque objects and nested BNK media are not universally replaceable. Ordinary previews/exports show the original data until the rebuilt package is imported again.

Changed blocks may use Zstd with skippable-frame padding. Unknown QTS checksum fields and `record.bytes` mappings are **not recomputed**. GUID-only external references are not fully resolved. Zero-change copying, untouched-byte preservation and reopened-entry checks are test baselines, not an in-game guarantee.

<a id="structure"></a>
## Project structure

```text
HOK-BetaStudio/
├── backend/
│   ├── Hok.Desktop/          # C# WPF + WebView2 host
│   ├── Hok.Worker/           # parsing, preview, export
│   ├── Hok.Contracts/        # filename identity and recursive scanner
│   ├── Hok.Legacy/           # adapters to Studio-HoK readers
│   ├── Hok.Rebuild/          # experimental replacement and rebuild
│   └── *.Tests/             # executable regression suites
├── frontend/                # React + TypeScript + Three.js
├── vendor/Studio-HoK/        # upstream C# source and attribution
├── assets/                  # UI assets, catalog and license notices
├── docs/                    # GitHub Pages tour and real screenshots
├── tooling/                 # build, packaging and verification scripts
├── README.md
├── README.zh.md
└── README.vi.md
```

Dependency caches, native runtime packages, game DBs, exported assets and build products are excluded from source control. Game portrait libraries are not redistributed as a source asset pack. Provide your own authorized artwork for the full catalog presentation.

<a id="build"></a>
## Build and package

Windows x64 is required for the desktop host. Install the .NET 10 SDK, Node.js with npm, and Microsoft Edge WebView2 Runtime. Installer packaging additionally needs NSIS. Versioned JavaScript dependencies are recorded in lockfiles.

```powershell
git clone https://github.com/Alanshown/HOK-BetaStudio.git
cd HOK-BetaStudio
npm ci --prefix frontend
npm ci --prefix tooling
dotnet build backend/Hok.Desktop/Hok.Desktop.csproj -c Release
dotnet build backend/Hok.Worker/Hok.Worker.csproj -c Release
dotnet run --project backend/Hok.Contracts.Tests -c Release -- .
```

Source compilation and a complete desktop distribution are separate steps. See [build inputs](docs/BUILD.md) for native FBX/FMOD/codec helpers, media tools, artwork and the current packaging layout. Do not copy third-party binaries into the Git repository.

```powershell
powershell -ExecutionPolicy Bypass -File tooling/build-desktop.ps1 -OutputDirectory build/HOK-BetaStudio-1.2-win-x64
powershell -ExecutionPolicy Bypass -File tooling/package-desktop.ps1 -BuildDirectory build/HOK-BetaStudio-1.2-win-x64
```

The static tour needs no package installation. For GitHub Pages, select **Deploy from a branch → main → /docs**. Enable Pages yourself when ready; until then the repository HTML link is a source view, not a deployed site.

<a id="download"></a>
## Download and use

Public downloads will appear on the [Releases page](https://github.com/Alanshown/HOK-BetaStudio/releases) after distribution review. A private draft is not available to ordinary visitors.

When published, extract the **whole portable ZIP** and run `HOK BetaStudio.exe`, or use the Windows installer. Do not move the EXE away from its `worker`, `ui` and `assets` folders. WebView2 Runtime is still required even with the self-contained .NET package. Packages are not currently code-signed.

Open a folder containing the complete main DB, shards and related files. Choose a hero, a skin and an asset. Use the preview or export selection actions. Keep original packages backed up, especially when trying Beta rebuilding.

<a id="verification"></a>
## Verification

The screenshots are captured from the packaged desktop application using local `3200010500` and `3200010504` fixtures, not fabricated UI data. Fixtures and extracted audio/models are not published.

| Check | Observed result |
|---|---|
| Identity and recursive scanner | 27 checks; 14 DB/shard files found through 12 nested levels |
| Zero-change rebuild baseline | 16 checks across both packages; byte-identical baseline output |
| Replacement worker | 15 checks, including repeat replacement and untouched-byte preservation |
| Replacement desktop UI | 11 checks, including single-selection actions and localized Beta warnings |
| BNK worker / desktop | 8 / 9 checks; six embedded media entries verified in the sample bank |

These are scoped regression results, not a claim of support for every game version or codec. Some integration scripts require private fixture directories and locally generated reports. See [build inputs](docs/BUILD.md) before running them.

<a id="attribution"></a>
## Attribution and distribution

Based on Studio-HoK / AssetStudio readers. Preserve the [upstream MIT notice](vendor/Studio-HoK/LICENSE). Third-party components and UI assets retain their own terms; see [asset notices](assets/licenses/SOURCES.txt) and [media notices](assets/licenses/media/NOTICE.txt).

The HOK crest-plus-text app icon is a generated project concept, not an official game logo. Honor of Kings names, characters, artwork and trademarks belong to their respective owners; this is an independent project. Screenshots illustrate the tool and do not grant rights to redistribute the depicted game content.

Only inspect and export material you are authorized to use. Public binary release remains gated on game-art permissions, native FBX/FMOD/codec redistribution checks, and the complete corresponding-source obligations of bundled FFmpeg builds. No blanket license for those components is implied by this repository.

