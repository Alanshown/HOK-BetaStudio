const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const readmes = ['README.md', 'README.zh.md', 'README.vi.md'];
const expectedAnchors = ['workspace', 'export', 'beta', 'structure', 'build', 'download', 'verification', 'attribution'];
const releaseBase = 'https://github.com/Alanshown/HOK-BetaStudio/releases/download/1.2/';
const names = ['HOK-BetaStudio-1.2-rebuild-beta-win-x64.zip', 'HOK-BetaStudio-1.2-rebuild-beta-win-x64-Setup.exe'];
const manifest = fs.readFileSync(path.join(root, 'docs/releases/1.2/SHA256SUMS.txt'), 'utf8');
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
  for (const name of names) assert(text.includes(releaseBase + name), `${file}: download link missing`);
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
console.log('Public documentation checks passed: three languages, selectors, download links, local links, images, anchors, fences, warnings and checksum entries.');
