# Release 1.2 — publication and verification

The maintainer published [release 1.2](https://github.com/Alanshown/HOK-BetaStudio/releases/tag/1.2) on 2026-10-03. Public availability is a release status, not evidence that distribution review is complete. Unverified items below remain open; do not mark them complete without supporting evidence.

- [ ] Confirm rights to distribute game portraits and skin imagery in the binary package.
- [ ] Confirm redistribution conditions for native FBX, FMOD and codec components.
- [ ] Prepare the complete corresponding source and build information required by the selected FFmpeg binary and its dependencies; a generic source link alone is not sufficient.
- [ ] Audit all license and attribution files included in both packages.
- [ ] Verify clean-machine launch with WebView2 installed.
- [ ] Verify portable extraction, installer shortcuts and uninstallation without deleting user data.
- [x] Review version, unsigned-binary notice and SHA-256 checksums: release metadata matches the two original local files; see [SHA256SUMS.txt](releases/1.2/SHA256SUMS.txt). This is not a fresh clean-machine installation test.
- [x] Keep rebuilding visibly marked Beta; the README and release notes retain the experimental and unverified-compatibility warning.
- [x] Maintainer publication decision: the maintainer published the release and requested post-publication review.

Local installer/portable tests were reported during packaging, but a new clean-machine run and an independently reproducible verification record are still pending. Do not treat source-only CI as a replacement for those checks.

Do not include test DBs, extracted game audio/models, private keys, local logs, caches or absolute-path test reports in release assets.
