const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const readmes = ['README.md', 'README.zh.md', 'README.vi.md'];
const expectedAnchors = ['workspace', 'export', 'beta', 'structure', 'build', 'download', 'verification', 'attribution'];
const version = JSON.parse(fs.readFileSync(path.join(root, 'frontend/package.json'), 'utf8')).version.split('.').slice(0, 2).join('.');
const releasePage = 'https://github.com/Alanshown/HOK-BetaStudio/releases';
const names = [`HOK-BetaStudio-${version}-win-x64-portable.zip`, `HOK-BetaStudio-${version}-win-x64-setup.exe`];
const manifest = fs.readFileSync(path.join(root, `docs/releases/${version}/SHA256SUMS.txt`), 'utf8');
const sourceLabels = [`Source version ${version}`, `源码版本 ${version}`, `Mã nguồn phiên bản ${version}`];
assert.equal(manifest.trim().split(/\r?\n/).length, 2);
for (const name of names) assert(manifest.includes(`  ${name}`), `Missing checksum for ${name}`);
for (const file of readmes) {
  const text = fs.readFileSync(path.join(root, file), 'utf8');
  assert.equal(text.split('<!-- README-I18N:START -->').length - 1, 1, file);
  assert.equal(text.split('<!-- README-I18N:END -->').length - 1, 1, file);
  assert.equal((text.match(/^```/gm) || []).length, 6, `${file}: code fences changed`);
  assert(!text.includes('[!NOTE]'), `${file}: removed Note must stay removed`);
  assert(text.includes('[!WARNING]'), `${file}: Beta warning is required`);
  for (const anchor of expectedAnchors) assert(text.includes(`<a id="${anchor}"></a>`), `${file}: ${anchor}`);
  assert(text.includes(`](${releasePage}/tag/${version})`), `${file}: current release page missing`);
  for (const name of names) assert(text.includes(`${releasePage}/download/${version}/${name}`), `${file}: current download link missing`);
  for (const [, linkedVersion] of text.matchAll(/releases\/(?:download|tag)\/([^/\s)]+)/g)) assert.equal(linkedVersion, version, `${file}: stale release version`);
  assert(text.includes(`version-${version}-`), `${file}: badge differs from source version`);
  assert(text.includes(`Windows x64 · ${sourceLabels[readmes.indexOf(file)]} ·`), `${file}: source version label differs from package.json`);
  assert(text.includes('(docs/GIT-PR-GUIDE.zh.md)'), `${file}: Git workflow guide missing`);
  assert(text.includes(`(docs/releases/${version}/RELEASE-NOTES.md)`), `${file}: current release notes missing`);
  assert(text.includes('(LICENSE)'), `${file}: license link missing`);
  for (const [, href] of text.matchAll(/\]\(([^)]+)\)/g)) {
    if (/^https?:/.test(href)) continue;
    if (href.startsWith('#')) {
      assert(expectedAnchors.includes(href.slice(1)), `${file}: unresolved anchor ${href}`);
    } else {
      const target = decodeURIComponent(href.split('#')[0]);
      assert(fs.existsSync(path.resolve(root, target)), `${file}: missing ${href}`);
    }
  }
  for (const [, src] of text.matchAll(/src="([^"]+)"/g)) {
    if (!/^https?:/.test(src)) assert(fs.existsSync(path.resolve(root, src)), `${file}: missing image ${src}`);
  }
}
console.log('Public documentation checks passed: three languages, source-version badges and labels, current release/download links, Git guide, local links, images, anchors, fences, warnings and current package checksums.');
