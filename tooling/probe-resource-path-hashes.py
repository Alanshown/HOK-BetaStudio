"""Offline hypotheses only; exact hash hits are not runtime binding claims."""
import hashlib
import json
import re
import sys
from pathlib import Path

import mmh3
import xxhash

base = Path(sys.argv[1]) / 'evidence'
exports = json.loads((base / 'mapping-exports.json').read_text(encoding='utf-8'))
record = next(r for r in exports if r['type'] == 'QtsResourceMap')
file = next(r['path'] for r in record['results'] if r['format'] == 'json')
resources = json.loads(Path(file).read_text(encoding='utf-8'))['records']
keys = {}
for r in resources:
    keys.setdefault(int(r['resourceKey']), []).append(r)
references = json.loads((base / 'configuration-graph.json').read_text(encoding='utf-8'))['references']
paths = set()
for r in references:
    original = r['path']
    if not re.fullmatch(r'[A-Za-z0-9_ /.-]+', original):
        continue
    stem = re.sub(r'\.(prefab|xml|action|asset)$', '', original, flags=re.I)
    for quality in ['', '_LODHDR', '_LOD0', '_LOD1', '_LOD2', '_LOD3', '_High', '_Mid', '_Low']:
        for ext in ['', '.prefab', '.asset']:
            for prefix in ['', '/', 'assets/', '/assets/', 'assets/resources/', '/assets/resources/', 'resources/', '/resources/']:
                paths.add(prefix + stem + quality + ext)
matches = []
attempts = 0
for logical in sorted(paths):
    for text in {logical, logical.lower()}:
        data = text.encode('utf-8')
        hashes = {
            'xxh64-seed0': xxhash.xxh64_intdigest(data),
            'xxh3_64-seed0': xxhash.xxh3_64_intdigest(data),
        }
        murmur = mmh3.hash64(data, signed=False)
        hashes.update({'murmur3-x64-low': murmur[0], 'murmur3-x64-high': murmur[1]})
        for kind in ['md5', 'sha1']:
            digest = hashlib.new(kind, data).digest()
            for at in [0, len(digest) - 8]:
                for endian in ['little', 'big']:
                    hashes[f'{kind}-{at}-{endian}'] = int.from_bytes(digest[at:at + 8], endian)
        fnv = 14695981039346656037
        for byte in data:
            fnv = ((fnv ^ byte) * 1099511628211) & 0xffffffffffffffff
        hashes['fnv1a64'] = fnv
        for algorithm, value in hashes.items():
            attempts += 1
            if value in keys:
                matches.append({'logicalPathHypothesis': text, 'algorithm': algorithm,
                                'resourceKey': str(value), 'records': keys[value]})
result = {'paths': len(paths), 'attempts': attempts, 'matches': matches,
          'scope': 'Selected common hash hypotheses and path variants only. A negative result does not prove that paths cannot be resolved; no mapping is installed in the loader.'}
(base / 'resource-path-common-hash-probe.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'paths': len(paths), 'attempts': attempts, 'matches': len(matches)}))
