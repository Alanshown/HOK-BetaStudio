"""Independently validate extracted bank identities with a local wwiser checkout.

No game DB is opened and no wwiser code is vendored into the application.
Retain reference field names/offsets; not every Wwise tid is a playable object.
"""
import hashlib
import json
from pathlib import Path
import subprocess
import sys

root, reference = map(Path, sys.argv[1:3])
sys.path.insert(0, str(reference.resolve()))
from wwiser.parser.wparser import Parser

base = root / 'evidence'
links = json.loads((base / 'audio-event-bank-links.json').read_text(encoding='utf-8'))
selected = {(c['source'], c['bankAssetId']) for m in links['matches'] for c in m['candidates']}
selected.update((c['source'], c['bankAssetId']) for m in links.get('configuredBankCandidates', []) for c in m['candidates'])
revision = subprocess.check_output(['git', '-C', str(reference), 'rev-parse', 'HEAD'], text=True).strip()
results = []
for row in links['banks']:
    if (row['source'], row['assetId']) not in selected and '3200014006' not in row['source'] and row['bankId'] != '1355168291':
        continue
    payload = Path(row['path']).read_bytes()
    assert hashlib.sha256(payload).hexdigest() == row['sha256'], 'Exported bank changed'
    parser = Parser()
    assert parser.parse_bank(row['path']), 'Independent bank parse failed'
    banks = parser.get_banks()
    assert len(banks) == 1
    bank = banks[0]
    records = []
    for field in bank.finds(name='eHircType'):
        obj = field.get_parent()
        fields = obj.finds(types=['sid', 'tid'])
        identity = next(f for f in fields if f.get_attr('type') == 'sid' and f.get_attr('offset') == field.get_attr('offset') + 5)
        values = [{'name': f.get_name(), 'id': str(f.get_attr('value')), 'offset': f.get_attr('offset'),
                   'fieldType': f.get_attr('type')} for f in fields]
        records.append({'type': field.get_attr('value'), 'id': str(identity.get_attr('value')),
                        'offset': field.get_attr('offset'), 'className': obj.get_name(), 'fields': values})
    expected = {(r['type'], r['id'], r['offset']) for r in row['objects']}
    observed = {(r['type'], r['id'], r['offset']) for r in records}
    assert expected == observed and len(records) == len(row['objects']), 'HIRC identity/range disagreement'
    checks = {'identityTableEqual': True, 'parserErrors': bank.get_error_count(), 'parserSkips': bank.get_skip_count()}
    result = {'source': row['source'], 'assetId': row['assetId'], 'bankId': row['bankId'],
              'sha256': row['sha256'], 'path': row['path'], 'checks': checks, 'objects': records}
    results.append(result)
    print(json.dumps({**result, 'objects': len(records)}, ensure_ascii=True), flush=True)
    parser.unload_bank(row['path'])

report = {'validator': 'bnnm/wwiser', 'revision': revision, 'banks': results,
          'semanticComplete': False, 'scope': 'Independent bounded bank parsing and exact HIRC identity tables; runtime branch applicability remains separate.'}
(base / 'audio-bank-hierarchy-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
if any(r['checks']['parserErrors'] or r['checks']['parserSkips'] for r in results):
    raise SystemExit('Independent parser reported errors/skips; see evidence')
