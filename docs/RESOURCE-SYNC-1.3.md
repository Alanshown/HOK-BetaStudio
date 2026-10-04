# Resource synchronization — 1.3

## User-visible behavior

- Startup renders the applied local index; hero and skin images use HTTPS URLs, not packaged game portraits.
- The C# host checks the two official catalogs in the background on each launch. Reading, fetching, validation and unchanged/failed checks have **no frontend status, progress bar, notification or button**.
- Only a validated semantic difference reveals the animated **资源同步 / Resource sync / Đồng bộ tài nguyên** button beside the File controls.
- Clicking it cancels active DB workers, removes the current session's imported-package and editing caches, clears assets/selection/staged replacements, and returns to the hero home page. Original DBs and completed exports are untouched. An export interrupted by synchronization may leave its already-written output, consistent with existing cancellation behavior.
- The validated candidate becomes the applied index only after the click. All portrait URLs receive the new revision; the hero grid remounts and replays the diagonal domino transition. Skin grids retain the same transition on entry. The system reduced-motion preference is respected.
- Unknown IDs and failed image requests use the existing local placeholders. `xxx00` continues to use the hero portrait and localized default-skin label.

The sync button tooltip explicitly explains that pending replacements will be discarded. There is no extra confirmation dialog. Ordinary DB import and broom cleanup do not delete the resource index.

## Files and isolation

The resource store is `%LOCALAPPDATA%/HokBetaStudio/catalog/`, outside per-session DB caches:

| File | Purpose |
| --- | --- |
| `index.json` | Applied normalized index, schema 1 |
| `index.backup.json` | Previous applied index after an update |
| `candidate.json` | Validated staged index and its base revision |
| `source-catalog.json`, `source-heroes.json` | Original response bodies plus HTTP validators |
| `sync.log`, `sync.previous.log` | Bounded background diagnostics, not surfaced automatically |
| `catalog.lock` | Cross-process protection during local index writes |

First launch bootstraps from `assets/catalog/remote-index.seed.json`: 133 heroes and 829 skins, normalized from the user-supplied updater's sample output. The executable does not need Python. A broken local index recovers from the backup, then the bundled seed. If no valid catalog can be loaded at all, startup reports a normal application error rather than pretending the collection is valid.

Source endpoints:

- `https://pvp.qq.com/zlkdatasys/heroskinlist.json`
- `https://pvp.qq.com/web201605/js/herolist.json`

The implementation ports the supplied `hok_index.py` core mapping rules, not its CLI or review commands. Four exact correction rules and two explicitly documented supplements are retained in `assets/catalog/corrections.default.json`. Source responses are cached for inspection; unverified IDs are never invented. The normalized desktop schema contains `kind`, `id`, `hero_id`, `hero`, `name`, `image_url` and `status`.

## Validation and failure policy

- Compare normalized record content, not ordering, formatting or check timestamps.
- Validate three-digit hero IDs, five-digit skin IDs, parent mapping, nonempty names, duplicate identities and approved HTTPS image hosts.
- Require both official data sources to succeed. Use ETag/Last-Modified and cached bodies for HTTP 304, 25-second request timeouts, up to two transient retries, a 100-second total check deadline and a 32 MiB response cap.
- Retain missing entries as `missing_from_source`. Conflicting IDs retain the old entry as `quarantined`, with placeholder artwork in the UI. If either current hero or skin collection drops below 75% of the previous current collection, reject the response as potentially incomplete.
- Write staged/applied JSON through a flushed, same-directory temporary file and atomic replacement. Reject stale candidate tokens or base revisions; do not silently overwrite a concurrently applied index.
- Network/schema failure preserves the applied index. A previously validated, still-applicable pending update may remain available offline. Image failure never removes DB records.
- The check compares index links and metadata, not the image bytes at unchanged URLs. URL-stable upstream image replacements are not detected by this index-only mechanism.

## Local verification

```powershell
dotnet run --project backend/Hok.Catalog.Tests -c Release -- .
dotnet run --project backend/Hok.Catalog.Tests -c Release -- . --live
powershell -ExecutionPolicy Bypass -File tooling/build-desktop.ps1
node tooling/test-resource-sync.cjs
```

The desktop integration test requires the authorized local `3200010500` fixture directory, working native helpers/media runtimes and the official network endpoints. It starts a hidden smoke instance with an isolated `.cache/tests/resource-sync-<GUID>` profile. It seeds a deliberately outdated name to exercise a real update, imports the real DB pair (841 assets), checks source hashes and cache removal, verifies the three languages and domino transitions, and saves screenshots only under that ignored test directory. It never writes the normal user's applied index or source packages. Do not run it while another smoke instance owns debug port 9224.

These changes do not modify the asset decoder/exporter or remove the experimental status of DB replacement/rebuild. No release is published by building or testing.
