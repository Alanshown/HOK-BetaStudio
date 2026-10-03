# Release 1.2 — private preparation

The maintainer requested a full portable package and installer, retained as a non-public draft until distribution review is complete.

- [ ] Confirm rights to distribute game portraits and skin imagery in the binary package.
- [ ] Confirm redistribution conditions for native FBX, FMOD and codec components.
- [ ] Prepare the complete corresponding source and build information required by the selected FFmpeg binary and its dependencies; a generic source link alone is not sufficient.
- [ ] Audit all license and attribution files included in both packages.
- [ ] Verify clean-machine launch with WebView2 installed.
- [ ] Verify portable extraction, installer shortcuts and uninstallation without deleting user data.
- [ ] Review version, unsigned-binary notice and SHA-256 checksums.
- [ ] Keep rebuilding visibly marked Beta; do not claim game compatibility.
- [ ] Obtain maintainer approval before making a draft public.

Do not include test DBs, extracted game audio/models, private keys, local logs, caches or absolute-path test reports in release assets.
