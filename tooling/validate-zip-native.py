"""Independent Python ZIP/native-output equality and Pillow image decoding."""
import hashlib
import json
from pathlib import Path
import sys
import zipfile
from PIL import Image

base = Path(sys.argv[1])
pointer = json.loads((base / 'zip-worker-latest.json').read_text(encoding='utf-8'))
result = json.loads(Path(pointer['report']).read_text(encoding='utf-8'))
archive = next(row for row in result['exported'] if row['type'] == 'AndroidPackage')
original = base / 'probes' / ('zip-' + result['entry']) / (result['entry'] + '.payload')
sha = lambda value: hashlib.sha256(value).hexdigest()
assert sha(original.read_bytes()) == sha(Path(archive['path']).read_bytes())
members = {int(row['id'].rsplit('/', 1)[1]): row for row in result['exported'] if '/zip/' in row['id']}
checked = 0
with zipfile.ZipFile(original) as source:
    assert len(source.infolist()) == result['archiveDirectory']['memberCount']
    for index, entry in enumerate(source.infolist()):
        if entry.is_dir():
            continue
        row = members[index]
        assert row['name'] == entry.filename
        assert sha(source.read(entry)) == sha(Path(row['path']).read_bytes()), entry.filename
        checked += 1
images = 0
for row in result['exported']:
    if row['type'] == 'ImageFile':
        with Image.open(row['path']) as image:
            image.load()
            assert image.width > 0 and image.height > 0
        images += 1
for row in result['pngExports']:
    with Image.open(row['path']) as image:
        image.load()
        assert image.format == 'PNG'
summary = {'membersByteExact': checked, 'nativeImagesDecoded': images, 'pngConversionsDecoded': len(result['pngExports']), 'archiveByteExact': True, 'runtimeSemanticComplete': False}
Path(pointer['output'], 'independent-validation.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
print(json.dumps(summary))
