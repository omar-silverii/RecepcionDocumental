"""Report existing OOF outputs; no image decoding, fitting, or inference."""
import csv
import hashlib
import json
from collections import Counter
from pathlib import Path
import numpy as np

HERE = Path(__file__).resolve().parent
CLASSES = ['FACTURA', 'OTRO_DOCUMENTO']


def read(path):
    with Path(path).open(encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda: f.read(1048576), b''):
            h.update(block)
    return h.hexdigest().upper()


def save(name, obj):
    (HERE / name).write_text(json.dumps(obj, indent=2, ensure_ascii=False), encoding='utf-8')


def csvout(name, rows):
    assert rows
    with (HERE / name).open('w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader()
        w.writerows(rows)


def average_precision(y, p):
    # Stepwise AP (same definition as B1 sklearn.average_precision_score), ties grouped.
    order = np.argsort(-p, kind='stable')
    y, p = y[order], p[order]
    ends = np.r_[np.flatnonzero(np.diff(p)), len(p) - 1]
    tp = np.cumsum(y)[ends]
    recall = tp / y.sum()
    precision = tp / (ends + 1)
    return float(np.sum(np.diff(np.r_[0.0, recall]) * precision))


def metrics(rows, score):
    y = np.array([r['LabelFinal'] == 'FACTURA' for r in rows], dtype=int)
    p = np.array([float(r[score]) for r in rows])
    assert np.isfinite(p).all() and ((p >= 0) & (p <= 1)).all()
    pred = p >= 0.5
    tp = int(np.sum((y == 1) & pred)); fn = int(np.sum((y == 1) & ~pred))
    fp = int(np.sum((y == 0) & pred)); tn = int(np.sum((y == 0) & ~pred))
    div = lambda a, b: a / b if b else 0.0
    rf, ro = div(tp, tp + fn), div(tn, tn + fp)
    pf, po = div(tp, tp + fp), div(tn, tn + fn)
    ff, fo = div(2 * tp, 2 * tp + fp + fn), div(2 * tn, 2 * tn + fp + fn)
    valid = len(set(y.tolist())) == 2
    # Pairwise Mann-Whitney AUC with ties = 1/2; small 901-row dataset.
    if valid:
        delta = p[y == 1, None] - p[y == 0][None, :]
        auc = float(np.mean((delta > 0) + 0.5 * (delta == 0)))
    else:
        auc = None
    return {'Count': len(rows), 'Families': len({r['FamilyId'] for r in rows}),
            'Factura': int(y.sum()), 'OtroDocumento': int(len(y) - y.sum()),
            'ThresholdDiagnosticOnly': 0.5, 'RecallFactura': rf, 'RecallOtroDocumento': ro,
            'PrecisionFactura': pf, 'PrecisionOtroDocumento': po,
            'MacroF1': (ff + fo) / 2, 'BalancedAccuracy': (rf + ro) / 2,
            'RocAuc': auc, 'PrAucFactura': average_precision(y, p) if valid else None,
            'PrAucOtroDocumento': average_precision(1-y, 1-p) if valid else None,
            'FacturaToOtroDocumento': fn, 'OtroDocumentoToFactura': fp,
            'ConfusionMatrixRowsFacturaOtro_ColsFacturaOtro': [[tp, fn], [fp, tn]]}


def main():
    pre = json.loads((HERE / 'header-preflight.json').read_text())
    for name, digest in pre['B1Hashes'].items():
        assert sha(HERE / name) == digest, name
    a = HERE.parent / 'H1D10A'
    assert sha(a / 'sealed-test-manifest.json') == pre['SealedHash']
    assert sha(a / 'bank-manifest.csv') == pre['BankManifestSha256']
    folds = read(HERE / 'fold-manifest.csv')
    text = {r['Sha256']: r for r in read(HERE / 'oof-predictions.csv')}
    checkpoints = [json.loads((HERE / f'header-fold-{k}.json').read_text()) for k in range(4)]
    allpred = {}
    for c in checkpoints:
        k = c['Fold']
        assert sha(c['Checkpoint']) == c['CheckpointSha256']
        assert c['FoldManifestSha256'] == sha(HERE / 'fold-manifest.csv')
        tr = {r['Sha256'] for r in folds if int(r['Fold']) != k}
        va = {r['Sha256'] for r in folds if int(r['Fold']) == k}
        assert tr == set(c['TrainSha256']) and va == set(c['ValidationSha256'])
        assert not set(c['TrainFamilies']) & set(c['ValidationFamilies'])
        assert not tr & va
        assert Counter(r['Phase'] for r in c['History']) == {1: 8, 2: 12}
        for r in c['Predictions']:
            assert r['Sha256'] in va and r['Sha256'] not in allpred
            allpred[r['Sha256']] = r['PFactura']
    assert len(allpred) == len(folds) == len(text) == 901
    rows = []
    for r in folds:
        s = r['Sha256']; p = allpred[s]; t = text[s]
        prediction = 'FACTURA' if p >= 0.5 else 'OTRO_DOCUMENTO'
        assert t['Fold'] == r['Fold'] and t['LabelFinal'] == r['LabelFinal']
        rows.append({**r, 'Header_PFactura': p, 'Header_Prediction': prediction,
                     'Header_Error': prediction != r['LabelFinal'],
                     'Text_PFactura': float(t['WORD_PFactura']), 'Text_Prediction': t['WORD_Prediction'],
                     'Text_Error': t['WORD_Prediction'] != r['LabelFinal'],
                     'InferenceStatus': 'OK', 'Error': ''})
    csvout('header-oof-predictions.csv', rows)
    independent = [r for r in rows if r['LabelSource'] in ('QR_ARCA_STRONG', 'EXISTING_GROUND_TRUTH')]
    assert len(independent) == 734
    result = {}
    comparison = []
    confusion = []
    for scope, cohort in [('AllStrongLabels', rows), ('IndependentEvidence', independent)]:
        result[scope] = {}
        for variant, score in [('TEXT_ONLY_WORD', 'Text_PFactura'), ('HEADER_ONLY', 'Header_PFactura')]:
            m = metrics(cohort, score)
            result[scope][variant] = m
            comparison.append({'Scope': scope, 'Variant': variant,
                               **{k: v for k, v in m.items() if not isinstance(v, list)}})
            cm = m['ConfusionMatrixRowsFacturaOtro_ColsFacturaOtro']
            for i, real in enumerate(CLASSES):
                for j, prediction in enumerate(CLASSES):
                    confusion.append({'Scope': scope, 'Variant': variant, 'Actual': real,
                                      'Predicted': prediction, 'Count': cm[i][j]})
    # Verify independently computed metrics against the untouched B1 report.
    b1 = json.loads((HERE / 'metrics.json').read_text())
    for scope in ('AllStrongLabels', 'IndependentEvidence'):
        # Preserve B1's original independent-evidence scope name.
        key = scope if scope in b1 else 'IndependentEvidenceSubset'
        for metric, value in b1[key]['WORD'].items():
            got = result[scope]['TEXT_ONLY_WORD'].get(metric)
            if isinstance(value, (int, float)) and got is not None:
                assert abs(value - got) < 1e-10, (scope, metric, value, got)
    result['PerFold'] = {str(k): metrics([r for r in rows if int(r['Fold']) == k], 'Header_PFactura') for k in range(4)}
    diagnostics = []
    for r in rows:
        th, hh = r['Text_Error'], r['Header_Error']
        kind = 'BOTH_WRONG' if th and hh else 'HEADER_CORRECTS_TEXT' if th else 'HEADER_NEW_ERROR' if hh else 'BOTH_CORRECT'
        delta = abs(r['Text_PFactura'] - r['Header_PFactura'])
        diagnostics.append({**r, 'Outcome': kind, 'ProbabilityAbsDifference': delta,
                            'StrongProbabilityDisagreement': delta >= 0.5})
    csvout('header-text-cases.csv', diagnostics)
    outcomes = dict(Counter(r['Outcome'] for r in diagnostics))
    result['Complementarity'] = {'Outcomes': outcomes,
          'StrongProbabilityDisagreementsAbsDeltaAtLeast0_5': sum(r['StrongProbabilityDisagreement'] for r in diagnostics),
          'Note': 'Diagnostic only, not active learning; no queue, no relabeling.'}
    families = []
    for family in sorted({r['FamilyId'] for r in rows}):
        rr = [r for r in rows if r['FamilyId'] == family]
        hm = metrics(rr, 'Header_PFactura')
        families.append({'FamilyId': family, 'Fold': rr[0]['Fold'], 'Documents': len(rr),
                         'Factura': hm['Factura'], 'OtroDocumento': hm['OtroDocumento'],
                         'HeaderErrors': sum(r['Header_Error'] for r in rr),
                         'TextErrors': sum(r['Text_Error'] for r in rr),
                         'FacturaToOtroDocumento': hm['FacturaToOtroDocumento'],
                         'OtroDocumentoToFactura': hm['OtroDocumentoToFactura'],
                         'HeaderErrorSha256': '|'.join(r['Sha256'] for r in rr if r['Header_Error'])})
    families.sort(key=lambda r: (-r['HeaderErrors'], r['FamilyId']))
    csvout('header-family-errors.csv', families)
    csvout('header-vs-text.csv', comparison)
    csvout('header-confusion-matrices.csv', confusion)
    save('header-metrics.json', result)
    hashes = [{'Fold': c['Fold'], 'Path': c['Checkpoint'], 'Sha256': c['CheckpointSha256'],
               'Bytes': Path(c['Checkpoint']).stat().st_size} for c in checkpoints]
    csvout('header-model-hashes.csv', hashes)
    plan = json.loads((HERE / 'header-training-plan.json').read_text())
    assert plan['TrainerSha256'] == sha(HERE / 'Train-HeaderOnly.py')
    protected = json.loads((HERE / 'header-protected-verification.json').read_text())
    assert all(sha(r['Path']) == r['Sha256'] for r in protected['Files'])
    gate = {'Status': 'H1D10B2 APROBADO — GATES DE INTEGRIDAD; NO PROMOCION',
            'ShaLeakage': 0, 'FamilyLeakage': 0, 'HoldoutLeakage': 0,
            'HoldoutAssetsOpened': 0, 'HoldoutHashUnchanged': True, 'B1HashesUnchanged': True,
            'OofPredictions': 901, 'UniqueOofSha256': 901, 'InferenceErrors': 0,
            'FixedFolds': True, 'RevisarUsedAsGroundTruth': False, 'H1D9BLabelsOrWeightsUsed': False,
            'ProtectedHistoricalFilesVerified': len(protected['Files']),
            'ProductionModified': False, 'FullPageHybridActiveLearningExecuted': False}
    save('header-gate-report.json', gate)
    training = {**plan, 'Status': gate['Status'], 'Models': hashes,
                'EpochsPerFold': {str(c['Fold']): {'Classifier': 8, 'LastBlockAndClassifier': 12} for c in checkpoints},
                'FoldDetails': [{'Fold': c['Fold'], 'TrainDocuments': c['TrainDocuments'],
                                'ValidationDocuments': c['ValidationDocuments'],
                                'ClassWeightsOtroFactura': c['ClassWeightsOtroFactura'],
                                'CheckpointParityError': c['CheckpointDirectVsCachedMaxAbsError']} for c in checkpoints],
                'Limitations': ['Development strong labels are not independent human certification.',
                               '734 independent-evidence samples reported separately.',
                               'Fixed transfer-learning schedule; only last block is fine-tuned.',
                               'Frozen ImageNet cache is shared, not fit to these documents.',
                               'No operational threshold selection or production promotion.'],
                'SmokeTest': json.loads((HERE / 'header-smoke-test.json').read_text()),
                'EnvironmentInstallManifest': str(HERE / 'header-environment-install.json'),
                'ImportedDependencyVerification': str(HERE / 'header-import-verification.json'),
                'PerformanceConclusion': 'HEADER_ONLY is inferior to untouched TEXT_ONLY WORD on overall and independent evidence; not selected or promoted.'}
    save('header-training-manifest.json', training)
    lines = ['# H1D10B2 — HEADER_ONLY', '', '**' + gate['Status'] + '**', '',
             '**HEADER_ONLY no mejora TEXT_ONLY WORD.** Macro F1 global 0,500286 frente a 0,716762; evidencia independiente 0,513277 frente a 0,752970. No se selecciona ni promueve esta rama como clasificador final.', '',
             '901 headers encontrados y verificados por SHA-256; cero faltantes, cero errores de inferencia.',
             '828 FACTURA / 73 OTRO_DOCUMENTO; 154 FamilyId. Mismos cuatro folds B1, sin regenerarlos.',
             'Leakage SHA/FamilyId/holdout: 0/0/0. B1, H1D10A y el sello permanecen intactos. Se verificaron también los 83 archivos protegidos históricos.', '',
             '## Configuración y ejecución', '',
             'EfficientNet-B0 ImageNet local, CPU, entrada 384×384. Header determinístico H1D10A completo, RGB, resize bicúbico con aspecto preservado, letterbox blanco centrado, normalización ImageNet. Sin augmentations ni nuevos crops.',
             'Cada fold comienza con ImageNet: 8 epochs del clasificador (AdamW 0.001), luego 12 del bloque final features.8 y clasificador (0.0001). Batch 16, weight decay 0.0001, dropout 0.2. Total 20 epochs por fold, 80 entre cuatro folds. Sin early stopping ni selección usando el fold externo.',
             'Loss ponderada con N_train/(2*N_train_clase), calculada sólo en los otros tres folds. No duplicación ni muestras sintéticas. features.0..7 quedan congelados, incluso sus estadísticas BatchNorm; su caché es independiente de las etiquetas. En fase 2 BatchNorm del bloque final se actualiza sólo con TRAIN.',
             'Se reutilizan el stack, Letterbox/ImageNet, AdamW, fases de transferencia y checkpointing de la infraestructura H1D9B, adaptados a cabecera 384, cuatro folds fijos y evaluación OOF sin early stopping externo. No se usan pesos del candidato H1D9B.',
             'La comprobación end-to-end de checkpoints usa un header permitido de cada fold; no toca holdout.', '',
             'Venv: `' + plan['Environment']['Venv'] + '`.',
             'Versiones importadas: Python 3.11.3, torch 2.13.0+cpu, torchvision 0.28.0+cpu, numpy 2.4.6, pillow 12.3.0. Resto en header-training-manifest.json. Sólo paquetes locales; hashes en header-offline-packages.json; cero descargas y sin cambios al Python global.',
             'Incidencia inicial: instalación parcial por reubicación del manual de SymPy; import inicial falló por dependencia ausente. Se completó el mismo .venv sin sobrescribir archivos diferentes; smoke test posterior pasó ANTES de entrenar.', '',
             '## Métricas OOF', '',
             'Threshold 0.5 exclusivamente diagnóstico; PR-AUC = average precision escalonada, por clase. No calibración ni cambio de thresholds operativos.', '',
             '| Evidencia | Rama | Recall F | Recall O | Precision F | Precision O | Macro F1 | Balanced acc. | ROC-AUC | AP F | AP O |',
             '|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
    for scope in ('AllStrongLabels', 'IndependentEvidence'):
        for variant in ('TEXT_ONLY_WORD', 'HEADER_ONLY'):
            m = result[scope][variant]
            keys = ['RecallFactura','RecallOtroDocumento','PrecisionFactura','PrecisionOtroDocumento','MacroF1','BalancedAccuracy','RocAuc','PrAucFactura','PrAucOtroDocumento']
            lines.append('| ' + scope + ' | ' + variant + ' | ' + ' | '.join(f'{m[k]:.6f}' for k in keys) + ' |')
    lines += ['', 'Evidencia independiente: 734 documentos (672 FACTURA, 62 OTRO_DOCUMENTO), QR_ARCA_STRONG + EXISTING_GROUND_TRUTH. Las 167 etiquetas de texto se excluyen de esta métrica, no del entrenamiento definido por B1.', '', '## Matrices y errores', '']
    for scope in ('AllStrongLabels', 'IndependentEvidence'):
        m = result[scope]['HEADER_ONLY']
        lines += [f"{scope}, filas reales / columnas predichas [FACTURA, OTRO_DOCUMENTO]: `{m['ConfusionMatrixRowsFacturaOtro_ColsFacturaOtro']}`.",
                  f"FACTURA → OTRO_DOCUMENTO: {m['FacturaToOtroDocumento']}; OTRO_DOCUMENTO → FACTURA: {m['OtroDocumentoToFactura']}.", '']
    lines += ['## Complementariedad frente a TEXT_ONLY WORD', '',
              json.dumps(result['Complementarity'], ensure_ascii=False), '',
              'Casos completos en header-text-cases.csv; los scores de texto proceden exclusivamente del OOF B1 sin modificarse. No se creó una cola de active learning ni etiquetas nuevas.', '',
              '## Familias con errores', '',
              f"{sum(f['HeaderErrors'] > 0 for f in families)} familias con errores HEADER. Lista completa con SHA afectados en header-family-errors.csv.", '',
              '| FamilyId | Fold | Documentos | Errores HEADER | Errores TEXT |', '|---|---:|---:|---:|---:|']
    for f in families:
        if f['HeaderErrors']:
            lines.append(f"| {f['FamilyId']} | {f['Fold']} | {f['Documents']} | {f['HeaderErrors']} | {f['TextErrors']} |")
    lines += ['', '## Checkpoints de desarrollo', '']
    for m in hashes:
        lines += [f"- Fold {m['Fold']}: `{m['Path']}`; SHA-256 `{m['Sha256']}`."]
    lines += ['', '## Alcance', '',
              'Aprobación de integridad del experimento, no certificación de calidad ni promoción. Son métricas de desarrollo con labels fuertes y familias heurísticas; el holdout continúa sellado. No se ejecutó FULL_PAGE, HYBRID, fusión, active learning ni Nivel A. No se modificó SQL, Gmail, H1D9B, binarios ni configuración productiva. Se detiene aquí.', '',
              '## Artefactos', '',
              'Archivos nuevos/modificados de B2 y rutas/hashes: header-artifact-index.csv. Se conservan todos los archivos B1; sólo resultado-H1D10B2.md se actualiza desde el estado pendiente. Modelos y entorno están en header-local, excluido de Git.']
    (HERE / 'resultado-H1D10B2.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    artifacts = []
    names = [p for p in HERE.iterdir() if p.is_file() and p.name not in pre['B1Hashes'] and p.name != 'header-artifact-index.csv']
    names += [Path(m['Path']) for m in hashes]
    names += [HERE / 'header-local/header-feature-cache.json']
    names += [HERE / 'header-local/header-features.npy']
    for p in sorted(names):
        artifacts.append({'Path': str(p), 'SizeBytes': p.stat().st_size, 'Sha256': sha(p)})
    csvout('header-artifact-index.csv', artifacts)
    print(json.dumps({'Metrics': result, 'Models': hashes}, indent=2))


if __name__ == '__main__':
    main()
