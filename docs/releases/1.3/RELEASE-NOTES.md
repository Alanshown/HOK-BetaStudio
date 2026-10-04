# HOK BetaStudio 1.3 — Silent Resource Sync

## What's new

- **URL-based hero and skin portraits:** artwork now loads from validated HTTPS image links. Static game portrait libraries are no longer bundled. Unknown IDs and failed image requests retain local fallback portraits.
- **Silent startup checks:** the C# backend reads the applied index, fetches the official catalogs, and validates changes without showing progress, notifications, or a temporary sync button.
- **Sync only when needed:** an animated **Resource sync** button appears only after a validated difference is found. Chinese, English, and Vietnamese labels are supported.
- **Domino refresh:** applying an index update returns to the hero catalog, reloads portrait URLs, and replays the diagonal domino transition. Skin cards retain the same entrance animation.
- **Safer index updates:** staged candidates, semantic comparisons, conditional HTTP requests, atomic replacement, a previous-index backup, and offline fallback protect the applied catalog.

## Before clicking Resource sync

Sync clears the current imported DB cache, loaded asset list, selection, and **all staged replacements**, and cancels active DB workers. Rebuild/export anything you need before syncing. Original DB files and already-written exports are not deleted. Ordinary DB import/broom cleanup does not delete the resource index.

## Downloads

- **`HOK-BetaStudio-1.3-win-x64-setup.exe`** — per-user Windows x64 installer.
- **`HOK-BetaStudio-1.3-win-x64-portable.zip`** — portable application; extract the entire archive, then run `HOK BetaStudio.exe` inside its folder.
- **`SHA256SUMS.txt`** — checksums for both packages.

Microsoft Edge WebView2 Runtime is required. The packages are unsigned. The installer requires an empty destination folder; for an existing installation, use a separate empty folder or uninstall the old application first. Do not separate the executable from `worker`, `ui`, and `assets`. The supplied .NET runtime is self-contained; Python is not required.

## Verification and limitations

- 41 resource-index checks, 22 desktop synchronization checks, and 27 identity/scanner checks passed locally.
- The real `3200010500` fixture still loaded 841 assets; synchronization removed session/editing caches without changing the source DB hashes or completed-export test file.
- DB replacement/rebuilding remains **Beta**, is not recommended for normal use, and has not been verified in-game. Existing equal-length/raw-binary restrictions remain.
- Synchronization detects index/link changes, not modified image bytes behind an unchanged URL. Offline first-use images may fall back to placeholders.
- Native-component distribution and FFmpeg corresponding-source checks remain subject to the repository's release checklist. This update does not grant rights to game artwork, assets, or third-party components.

Full changelog: https://github.com/Alanshown/HOK-BetaStudio/compare/1.2...main
