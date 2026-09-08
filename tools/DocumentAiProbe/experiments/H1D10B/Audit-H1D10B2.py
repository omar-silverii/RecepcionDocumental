"""Read-only input audit; writes only new B2 audit files. Never decodes sealed assets."""
import csv
import hashlib
import json
import re
import zipfile
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
A = HERE.parent / 'H1D10A'
EXPECTED = 'EF2C7A80A5616A5F009087020E3FB9988DD76B5BAED315F84D921AA605BCF7E0'
B1 = ['artifact-index.csv', 'confusion-matrices.csv', 'dataset-manifest.csv',
      'family-errors.csv', 'fold-manifest.csv', 'metrics.json', 'model-comparison.csv',
      'no-document-inventory.csv', 'oof-predictions.csv', 'resultado.md',
      'Run-H1D10B1.py', 'text-only-word-development.joblib', 'text.zip',
      'training-manifest.json']


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest().upper()


def read(path):
    with Path(path).open(encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))


def save(name, value):
    (HERE / name).write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')


def main():
    # Hash only: never JSON-decode the sealed manifest or open its assets.
    assert sha(A / 'sealed-test-manifest.json') == EXPECTED, 'SEALED HASH'
    snapshot = {name: sha(HERE / name) for name in B1}
    bank = read(A / 'bank-manifest.csv')
    d = read(HERE / 'dataset-manifest.csv')
    folds = read(HERE / 'fold-manifest.csv')
    oof = read(HERE / 'oof-predictions.csv')
    assert len(d) == 901 and len({r['FamilyId'] for r in d}) == 154
    assert Counter(r['LabelFinal'] for r in d) == {'FACTURA': 828, 'OTRO_DOCUMENTO': 73}
    lookup = {r['Sha256']: r for r in bank}
    fm = {r['Sha256']: r for r in folds}
    om = {r['Sha256']: r for r in oof}
    for rows in (d, folds, oof):
        assert len(rows) == len({r['Sha256'] for r in rows}) == 901
        assert {r['Sha256'] for r in rows} == {r['Sha256'] for r in d}
    sealed_hashes = {r['Sha256'] for r in bank if r['Split'] == 'SEALED_TEST'}
    sealed_families = {r['FamilyId'] for r in bank if r['Split'] == 'SEALED_TEST'}
    assert not sealed_hashes & {r['Sha256'] for r in d}
    assert not sealed_families & {r['FamilyId'] for r in d}
    audit = []
    for r in d:
        s = r['Sha256']
        b = lookup[s]
        assert b['Split'] != 'SEALED_TEST' and not b['Conflict']
        for field in ('LabelFinal', 'FamilyId', 'LabelSource'):
            assert r[field] == b[field] == fm[s][field] == om[s][field]
        assert fm[s]['Fold'] == om[s]['Fold']
        path = Path(b['HeaderAsset'])
        assert not {'work', 'temp'} & {x.lower() for x in path.parts}
        assert path.name.endswith('.header.png') and path.is_file(), str(path)
        assert sha(path) == b['HeaderSha256'], str(path)
        audit.append({'Sha256': s, 'FamilyId': r['FamilyId'], 'Fold': fm[s]['Fold'],
                      'HeaderAsset': str(path), 'HeaderSha256': b['HeaderSha256']})
    assert {r['Fold'] for r in folds} == {'0', '1', '2', '3'}
    fold_counts = []
    for k in range(4):
        train = [r for r in folds if int(r['Fold']) != k]
        val = [r for r in folds if int(r['Fold']) == k]
        assert not {r['FamilyId'] for r in train} & {r['FamilyId'] for r in val}
        assert not {r['Sha256'] for r in train} & {r['Sha256'] for r in val}
        fold_counts.append({'Fold': k, 'Train': len(train), 'Validation': len(val),
                            'ValidationFamilies': len({r['FamilyId'] for r in val}),
                            'ValidationClasses': dict(Counter(r['LabelFinal'] for r in val))})
    with (HERE / 'header-assets-audit.csv').open('w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=list(audit[0]))
        w.writeheader()
        w.writerows(audit)
    norm = lambda s: re.sub('[-_.]+', '-', s).lower()
    lock = (HERE.parent / 'H1D9B' / 'requirements-lock.txt').read_text()
    wanted = {norm(k): (k, v) for k, v in
              [line.split('==') for line in lock.splitlines() if '==' in line]}
    found = {}
    cache = Path.home() / 'AppData/Local/pip/cache'
    for path in cache.rglob('*'):
        if not path.is_file() or path.suffix not in ('.body', '.whl'):
            continue
        if not zipfile.is_zipfile(path):
            continue
        with zipfile.ZipFile(path) as z:
            wh = [n for n in z.namelist() if n.endswith('.dist-info/WHEEL')]
            if not wh:
                continue
            name, version = wh[0].split('.dist-info/')[0].split('-', 1)
            key = norm(name)
            if key not in wanted or wanted[key][1] != version:
                continue
            found[key] = {'Name': wanted[key][0], 'Version': version,
                          'CachePath': str(path), 'Sha256': sha(path),
                          'WheelMetadata': z.read(wh[0]).decode()}
    missing = dict(wanted[k] for k in wanted if k not in found)
    save('header-offline-packages.json', {'Packages': list(found.values()),
          'MissingFromCompleteHistoricalLock': missing, 'Downloads': 0,
          'Note': 'pip/truststore are installation tooling, not HEADER_ONLY runtime dependencies.'})
    assert snapshot == {name: sha(HERE / name) for name in B1}
    save('header-preflight.json', {
        'Status': 'INPUT_GATES_PASSED; AWAITING_OFFLINE_ENVIRONMENT_DECISION',
        'Documents': 901, 'Classes': {'FACTURA': 828, 'OTRO_DOCUMENTO': 73},
        'Families': 154, 'HeadersFound': len(audit), 'HeaderHashesVerified': len(audit),
        'MissingHeaders': 0, 'HeaderErrors': 0, 'Folds': fold_counts,
        'ShaLeakage': 0, 'FamilyLeakage': 0, 'HoldoutLeakage': 0,
        'SealedHash': EXPECTED, 'SealedAssetsOpened': 0, 'TrainingExecuted': False,
        'B1Hashes': snapshot, 'B1Preserved': True,
        'BankManifestSha256': sha(A / 'bank-manifest.csv'),
        'CachedWeights': {'Path': str(Path.home() / '.cache/torch/hub/checkpoints/efficientnet_b0_rwightman-7f5810bc.pth'),
                          'Sha256': sha(Path.home() / '.cache/torch/hub/checkpoints/efficientnet_b0_rwightman-7f5810bc.pth')}
    })
    print('901 headers verified; SHA/family/holdout leakage = 0; training not executed.')


if __name__ == '__main__':
    main()
