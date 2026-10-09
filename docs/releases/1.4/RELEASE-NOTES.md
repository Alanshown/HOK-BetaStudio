# HOK BetaStudio 1.4 — On-demand Previews & Reliable Model Export

[简体中文 / maintainer publishing guide](RELEASE-NOTES.zh.md)

- Version / Git tag: **1.4**
- Windows file version: **1.4.0.0**
- Platform: Windows x64
- Release title: **HOK BetaStudio 1.4 — On-demand Previews & Reliable Model Export**
- Package names: `HOK-BetaStudio-1.4-win-x64-portable.zip` and `HOK-BetaStudio-1.4-win-x64-setup.exe`

This release rolls up the asset-recovery and preview work since the 1.3 application packages, including the latest on-demand memory improvements and the Mesh/FBX fixes for [issue #7](https://github.com/Alanshown/HOK-BetaStudio/issues/7). Some fixes previously existed in source-only 1.3 hotfix builds; they are included together in 1.4.

## Model and animation export

- Mesh assets now offer **FBX as well as OBJ**. FBX follows actual renderer, hierarchy and controller references to include the available skeleton, skin weights, morph targets, materials and linked animation clips.
- Mesh-only data can export a static FBX without an invented rig. Selecting an AnimationClip exports that clip with its referenced model/rig when those dependencies exist. Unrelated clips are not attached by filename guesswork.
- Fixed default legacy clip collection, root-transform curves, explicit clip selection and AnimatorOverrideController replacement clips. Disabling animation export now also excludes legacy clips.
- Correctly separate geometry from an FBX joint when one Unity transform acts as both. Keep joint motion and morph curves on their respective nodes.
- Validate mesh geometry and generated FBX output. Handle long output paths and Unicode installation paths; a missing native output is no longer reported as success.
- Missing textures can produce a geometry/rig/animation FBX with an explicit warning. Each FBX gets a `.fbx.export.json` report describing exported geometry, bones, animation takes and warnings.
- The export panel explains that **OBJ is static geometry**, while FBX animation depends on available references. Successful exports can show warnings rather than silently hiding limitations.

## Click-only previews and memory

- Heavy mesh, animation, material, shader, font, audio and video payloads are deferred. Image pixels are decoded only when a preview or export requests them.
- Opening a BNK/PCK lists embedded media without decoding every sound or automatically playing the first entry.
- Close a preview to release image buffers, model GPU resources, audio sources and temporary font faces. Stale requests are cancelled or ignored when switching assets.
- Small workspaces keep separate preview/export workers. Very large workspaces reuse the indexed worker to avoid several full copies of a DB; operations on that index are serialized while the desktop remains responsive.
- Fixed a short-lived cache-file lock race that could make consecutive desktop exports report failure after writing their output.

## Discovery, audio and other assets

- Preserve nested SerializedFiles, container/index records, original object identities and source-qualified references. Arbitrary DB filenames remain accessible instead of being excluded by hero-ID matching.
- Preserve class ID, PathID, raw bytes and diagnostics when semantic decoding is partial or fails. Bounded object reads prevent one damaged object from consuming the next object's bytes.
- Improved supported HOK animation layouts and curve conversion, mesh color decoding, structured metadata and full package/index exports.
- Hardened Wwise bank/media extraction and decoder output checks. Original BNK/WEM bytes remain available; supported embedded media can be decoded to WAV/MP3 and exported as ZIP collections.
- Native audio conversion also handles long destinations through a short working path.
- Cubemap supports a six-face preview and PNG ZIP; font preview is loaded and released on demand. Linked animation preview uses available scene dependencies and reports unsupported effects.

## Installation and languages

- The installer detects a valid existing per-user installation and defaults to an in-place upgrade. A different writable folder can still be chosen.
- Preflight checks reject locked application files before extraction. Upgrade/uninstall operations preserve unrelated user files.
- Application, worker, installer, English/Chinese/Vietnamese README badges and package names are aligned to **1.4**.
- URL-based portraits, silent index checks, explicit resource synchronization and the existing three-language interface remain available.

## Validation

- **8 real meshes** from two DB samples exported as **16 OBJ/FBX files**, independently re-imported with matching positions, winding and triangle counts; FBX skinning retained. Desktop FBX-then-OBJ exports passed for both samples.
- **6 synthetic native FBX outputs** independently re-imported, including standalone geometry, skinned models, selected/disabled clips, joint-mounted geometry and override controllers. Animation sampling verified actual root motion.
- The preceding sequential **20-DB regression** retained **704,337 asset rows**, **1,318,295 container entries** and **3,524,080,507 recovered payload bytes**. Payload hashes matched the baseline and source DB hashes were unchanged. See [scope and reproduction](../../LAZY-PREVIEW-VALIDATION.md).
- Local tests cover cache ownership/locking, asset coverage, discovery, media, installation paths and upgrade behavior. No automatic workflow or package publication is enabled.

The two real model samples used for this release contain no AnimationClip objects. Their skeleton/geometry exports are validated separately from synthetic animated-FBX tests; this is not a claim that every game animation has been round-tripped.

## Download and upgrade

Choose the Setup EXE or portable ZIP and verify it against the accompanying `SHA256SUMS.txt`. Close the existing application before upgrading. Extract the **entire** portable ZIP, not only the EXE. Keep the worker, UI, assets and native libraries together.

Windows x64 and Microsoft Edge WebView2 Runtime are required. The packages include the .NET runtime, but Setup does not install WebView2. Packages are currently unsigned.

## Known limitations

- Recovered bytes, identified types, fully decoded assets and faithful game playback are different guarantees. Some proprietary streams and custom script/shader payloads still have explicit raw-only or partial interpretations; they are not silently counted as fully decoded.
- Missing external resources, unsupported audio codecs and unbound animation tracks cannot be reconstructed from absent data. The preview renderer is not a full Unity particle/shader runtime.
- Cubemap PNGs represent eight-bit mip 0; the ZIP also preserves original mip bytes, rather than claiming lossless HDR conversion.
- **DB replacement/rebuilding remains Beta**, limited to compatible equal-length replacements. In-game compatibility is unverified; original DBs are not overwritten.
- Existing dependency advisories and third-party redistribution/source obligations remain documented in the [release checklist](../../RELEASE-CHECKLIST.md). Functional regression results do not constitute a security or licensing clearance.

See [installation instructions](../../INSTALL.md) and [model-export validation](../../MODEL-EXPORT-VALIDATION-1.4.md).
