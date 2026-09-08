"""HEADER_TEXT_ONLY OOF and text-only review prioritization. No image inference.

All vocabularies/IDF/classifiers are fit only on the other three original B1 folds.
The B1 WORD classifier is reconstructed from its unchanged frozen numerical state
because its sklearn 1.8 pickle is not executable as-is on local sklearn 1.3.2.
An independent word/TF-IDF implementation verifies all 682 review probabilities.
"""
import csv
import hashlib
import html
import importlib.util
import json
import math
import random
import re
import shutil
import socket
import sys
import unicodedata
import warnings
from collections import Counter
from pathlib import Path


def no_network(*args, **kwargs):
    raise RuntimeError('H1D10B3 is offline')


socket.create_connection = no_network
socket.socket.connect = no_network

import joblib
import numpy as np
import scipy
from scipy.special import expit
import sklearn
from sklearn.exceptions import ConvergenceWarning, InconsistentVersionWarning
from sklearn.feature_extraction.text import TfidfVectorizer, TfidfTransformer
from sklearn.pipeline import FeatureUnion, Pipeline
from sklearn.linear_model import LogisticRegression

HERE = Path(__file__).resolve().parent
A = HERE.parent / 'H1D10A'
LOCAL = HERE / 'b3-local'
SEAL = 'EF2C7A80A5616A5F009087020E3FB9988DD76B5BAED315F84D921AA605BCF7E0'
SEED = 20260907
VARIANTS = ['WORD', 'CHAR', 'WORD_CHAR']
PLAN = {
    'Seed': SEED, 'Folds': 'B1 existing fold-manifest.csv, unchanged',
    'Input': 'Only H1D10A HeaderTextAsset for HEADER_TEXT_ONLY',
    'Word': {'ngram_range': [1, 2], 'min_df': 2, 'max_df': 0.995, 'max_features': 80000},
    'Char': {'analyzer': 'char_wb', 'ngram_range': [3, 5], 'min_df': 2, 'max_features': 120000},
    'CombinedMaxFeatures': {'word': 70000, 'char': 100000},
    'CommonVectorizer': {'lowercase': True, 'strip_accents': 'unicode', 'sublinear_tf': True, 'norm': 'l2'},
    'Classifier': {'C': 1.0, 'class_weight': 'balanced', 'solver': 'liblinear', 'max_iter': 2500},
    'Selection': 'Independent-evidence macro F1, then independent balanced accuracy, then global macro F1; ties prefer WORD, CHAR, WORD_CHAR',
    'DiagnosticThreshold': 0.5,
    'PriorityFormula': '0.40*(1-min(p_text,p_header)) + 0.25*abs(p_text-p_header) + 0.15*max(1-2*abs(p_text-.5),1-2*abs(p_header-.5)) + 0.10/(1+FamilyTrainingCount) + 0.10*max_cosine_to_training_OTRO',
    'ReasonCutoffs': {'LowFacturaScore': 0.35, 'ScoreDifference': 0.35, 'Uncertainty': 0.8,
                      'FamilyTrainingCountAtMost': 2, 'SemanticCosine': 0.5},
    'BlindOrder': 'SHA-sorted selected sample, shuffled with random.Random(202609073); unrelated to priority order',
    'NoLabelChanges': True, 'NoHoldoutInference': True, 'NoImageModels': True,
}


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for chunk in iter(lambda: f.read(1048576), b''):
            h.update(chunk)
    return h.hexdigest().upper()


def read(path):
    with Path(path).open(encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))


def save(name, value):
    (HERE / name).write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')


def csvout(name, rows):
    with (HERE / name).open('w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader(); w.writerows(rows)


def metric(rows, column):
    # Reuse the independently verified B2 metric definitions, not its inference.
    spec = importlib.util.spec_from_file_location('b2_report_metrics', HERE / 'Report-HeaderOnly.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.metrics(rows, column)


def protect():
    file = HERE / 'b3-protected-inputs.json'
    if file.exists():
        baseline = json.loads(file.read_text())
    else:
        # Existing B1/B2 artifacts are immutable; new B3 output uses separate prefixes.
        paths = [p for p in HERE.iterdir() if p.is_file() and not p.name.startswith(('b3-', 'header-text-', 'review-', 'Run-H1D10B3', 'resultado-H1D10B3'))]
        paths += [A / 'bank-manifest.csv', A / 'sealed-test-manifest.json']
        paths += [Path(r['Path']) for r in read(A / 'protected-before.csv')]
        baseline = {str(p): sha(p) for p in paths}
        save('b3-protected-inputs.json', baseline)
    for p, digest in baseline.items():
        assert sha(p) == digest, 'Protected artifact changed: ' + p
    assert sha(A / 'sealed-test-manifest.json') == SEAL  # Hash bytes only.
    return baseline


def cohorts():
    bank = {r['Sha256']: r for r in read(A / 'bank-manifest.csv')}
    folds = read(HERE / 'fold-manifest.csv')
    ds = {r['Sha256']: r for r in read(HERE / 'dataset-manifest.csv')}
    assert len(folds) == len({r['Sha256'] for r in folds}) == 901
    assert len({r['FamilyId'] for r in folds}) == 154
    assert Counter(r['LabelFinal'] for r in folds) == {'FACTURA': 828, 'OTRO_DOCUMENTO': 73}
    sealed_sha = {s for s, r in bank.items() if r['Split'] == 'SEALED_TEST'}
    sealed_families = {r['FamilyId'] for r in bank.values() if r['Split'] == 'SEALED_TEST'}
    assert {r['Sha256'] for r in folds} == set(ds)
    development = []
    for r in folds:
        b = bank[r['Sha256']]
        assert b['Split'] != 'SEALED_TEST' and not b['Conflict']
        for k in ('LabelFinal', 'FamilyId', 'LabelSource'):
            assert r[k] == b[k] == ds[r['Sha256']][k]
        development.append({**b, 'Fold': int(r['Fold'])})
    review = [r for r in bank.values() if r['LabelFinal'] == 'REVISAR']
    assert len(review) == len({r['Sha256'] for r in review}) == 682
    assert not {r['Sha256'] for r in development} & {r['Sha256'] for r in review}
    for cohort in (development, review):
        assert not {r['Sha256'] for r in cohort} & sealed_sha
        assert not {r['FamilyId'] for r in cohort} & sealed_families
    assert {r['Fold'] for r in development} == set(range(4))
    for k in range(4):
        tr, va = [r for r in development if r['Fold'] != k], [r for r in development if r['Fold'] == k]
        for key in ('Sha256', 'FamilyId'):
            assert not {r[key] for r in tr} & {r[key] for r in va}, 'LEAKAGE ' + key
    return development, review


TEXT_READS = []


def text(row, key):
    assert row['Split'] != 'SEALED_TEST'
    p = Path(row[key]).resolve()
    assert p.is_relative_to((A / 'text').resolve()) and p.suffix == '.txt' and p.is_file()
    value = p.read_text(encoding='utf-8-sig', errors='replace')
    TEXT_READS.append({'Sha256': row['Sha256'], 'Role': key, 'Path': str(p), 'Sha256Text': sha(p)})
    return value


def make_model(kind):
    common = dict(lowercase=True, strip_accents='unicode', min_df=2, sublinear_tf=True)
    def word(n):
        return TfidfVectorizer(**common, ngram_range=(1, 2), max_df=.995, max_features=n)
    def char(n):
        return TfidfVectorizer(**common, analyzer='char_wb', ngram_range=(3, 5), max_features=n)
    vec = word(80000) if kind == 'WORD' else char(120000) if kind == 'CHAR' else FeatureUnion([('word', word(70000)), ('char', char(100000))])
    return Pipeline([('tfidf', vec), ('clf', LogisticRegression(C=1.0, class_weight='balanced', solver='liblinear', max_iter=2500, random_state=SEED))])


def train_headers(dev):
    headers = np.array([text(r, 'HeaderTextAsset') for r in dev], dtype=object)
    assert all(s.strip() for s in headers), 'Empty header text: stop before fit'
    if (HERE / 'header-text-model-hashes.csv').exists():
        # Resume completed A without refitting or changing any OOF model/prediction.
        hashes = read(HERE / 'header-text-model-hashes.csv')
        assert len(hashes) == 13
        for r in hashes:
            assert sha(r['Path']) == r['Sha256']
        saved = read(HERE / 'header-text-oof-predictions.csv')
        assert len(saved) == len({r['Sha256'] for r in saved}) == 901
        assert {r['Sha256'] for r in saved} == {r['Sha256'] for r in dev}
        for r, original in zip(saved, dev):
            assert r['Sha256'] == original['Sha256'] and int(r['Fold']) == original['Fold']
        print('Resuming completed HEADER_TEXT OOF and final model, no refitting.', flush=True)
        return joblib.load(LOCAL / 'header-text-development.joblib'), json.loads((HERE / 'header-text-metrics.json').read_text()), hashes
    y = np.array([r['LabelFinal'] == 'FACTURA' for r in dev], dtype=int)
    oof = [{k: r[k] for k in ('Sha256', 'FileName', 'FamilyId', 'LabelFinal', 'LabelSource', 'Fold')} for r in dev]
    audit = []; model_hashes = []
    for kind in VARIANTS:
        probs = np.full(len(dev), np.nan)
        for fold in range(4):
            tr = np.array([i for i, r in enumerate(dev) if r['Fold'] != fold])
            va = np.array([i for i, r in enumerate(dev) if r['Fold'] == fold])
            m = make_model(kind)
            with warnings.catch_warnings():
                warnings.simplefilter('error', ConvergenceWarning)
                m.fit(headers[tr], y[tr])
            assert m.classes_.tolist() == [0, 1]
            probs[va] = m.predict_proba(headers[va])[:, 1]
            path = LOCAL / f'header-text-{kind.lower()}-fold-{fold}.joblib'
            joblib.dump(m, path)
            model_hashes.append({'Variant': kind, 'Role': 'OOF_FOLD', 'Fold': fold, 'Path': str(path), 'Sha256': sha(path)})
            audit.append({'Variant': kind, 'Fold': fold, 'TrainSha256': [dev[i]['Sha256'] for i in tr],
                          'ValidationSha256': [dev[i]['Sha256'] for i in va],
                          'TrainFamilies': sorted({dev[i]['FamilyId'] for i in tr}),
                          'ValidationFamilies': sorted({dev[i]['FamilyId'] for i in va}),
                          'Iterations': m.named_steps['clf'].n_iter_.tolist(),
                          'TrainLabels': dict(Counter(dev[i]['LabelFinal'] for i in tr))})
            print(f'HEADER_TEXT {kind} fold {fold}: {len(tr)} train / {len(va)} OOF', flush=True)
        assert np.isfinite(probs).all() and ((probs >= 0) & (probs <= 1)).all()
        for r, p in zip(oof, probs):
            r[kind + '_PFactura'] = float(p)
    metrics = {}
    comparison = []
    matrices = []
    baseline = {r['Sha256']: r for r in read(HERE / 'oof-predictions.csv')}
    for r in oof:
        r['TEXT_ONLY_PFactura'] = float(baseline[r['Sha256']]['WORD_PFactura'])
    for scope, rows in [('AllStrongLabels', oof), ('IndependentEvidence', [r for r in oof if r['LabelSource'] in ('QR_ARCA_STRONG', 'EXISTING_GROUND_TRUTH')])]:
        metrics[scope] = {}
        for kind in ['TEXT_ONLY'] + VARIANTS:
            mm = metric(rows, kind + '_PFactura')
            metrics[scope][kind] = mm
            comparison.append({'Scope': scope, 'Variant': kind, **{k: v for k, v in mm.items() if not isinstance(v, list)}})
            matrices.append({'Scope': scope, 'Variant': kind, 'MatrixRowsFacturaOtroColsFacturaOtro': json.dumps(mm['ConfusionMatrixRowsFacturaOtro_ColsFacturaOtro'])})
    winner = max(VARIANTS, key=lambda k: (metrics['IndependentEvidence'][k]['MacroF1'], metrics['IndependentEvidence'][k]['BalancedAccuracy'], metrics['AllStrongLabels'][k]['MacroF1'], -VARIANTS.index(k)))
    metrics['Winner'] = winner
    csvout('header-text-oof-predictions.csv', oof)
    csvout('header-text-vs-text.csv', comparison)
    csvout('header-text-confusion-matrices.csv', matrices)
    save('header-text-metrics.json', metrics)
    save('header-text-fold-audit.json', audit)
    # Only after OOF comparison, fit the chosen header model to all 901 accepted labels.
    final = make_model(winner)
    with warnings.catch_warnings():
        warnings.simplefilter('error', ConvergenceWarning)
        final.fit(headers, y)
    path = LOCAL / 'header-text-development.joblib'
    joblib.dump(final, path)
    model_hashes.append({'Variant': winner, 'Role': 'DEVELOPMENT_REFIT', 'Fold': '', 'Path': str(path), 'Sha256': sha(path)})
    csvout('header-text-model-hashes.csv', model_hashes)
    return final, metrics, model_hashes


def frozen_b1():
    source = HERE / 'text-only-word-development.joblib'
    # Read the trusted B1 numerical state, never call incompatible loaded methods.
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter('always', InconsistentVersionWarning)
        old = joblib.load(source)
    original_vec = old.named_steps['tfidf']
    old_clf = old.named_steps['clf']
    params = original_vec.get_params(deep=False)
    assert params['analyzer'] == 'word' and params['ngram_range'] == (1, 2)
    assert params['norm'] == 'l2' and params['sublinear_tf'] and params['stop_words'] is None
    assert params['strip_accents'] == 'unicode' and params['lowercase']
    assert params['token_pattern'] == r'(?u)\b\w\w+\b' and not params['binary']
    assert old_clf.classes_.tolist() == [0, 1]
    vocab = {term: int(index) for term, index in original_vec.vocabulary_.items()}
    idf = np.asarray(vars(original_vec._tfidf)['idf_'], dtype=np.float64).copy()
    coef = np.asarray(old_clf.coef_[0], dtype=np.float64).copy()
    intercept = float(old_clf.intercept_[0])
    assert len(vocab) == len(idf) == len(coef) == 17786
    vec = TfidfVectorizer(**params)
    vec.vocabulary_ = vocab
    vec.fixed_vocabulary_ = True
    vec._tfidf = TfidfTransformer(norm='l2', use_idf=True, smooth_idf=True, sublinear_tf=True)
    vec._tfidf.idf_ = idf
    np.savez(LOCAL / 'text-only-b1-frozen.npz', idf=idf, coef=coef, intercept=np.array([intercept]))
    (LOCAL / 'text-only-b1-vocabulary.json').write_text(json.dumps(vocab, ensure_ascii=False), encoding='utf-8')
    provenance = {'SourcePath': str(source), 'SourceSha256': sha(source), 'SourceSklearn': '1.8.0',
                  'ExecutionSklearn': sklearn.__version__, 'VersionWarnings': [str(w.message) for w in caught],
                  'State': 'Exact learned vocabulary, IDF, coefficients and intercept; no fitting or changed weights',
                  'Features': len(vocab), 'WeightsPath': str(LOCAL / 'text-only-b1-frozen.npz'),
                  'WeightsSha256': sha(LOCAL / 'text-only-b1-frozen.npz'),
                  'VocabularyPath': str(LOCAL / 'text-only-b1-vocabulary.json'),
                  'VocabularySha256': sha(LOCAL / 'text-only-b1-vocabulary.json')}
    return vec, vocab, idf, coef, intercept, provenance


def reference_score(s, vocab, idf, coef, intercept):
    # Independent implementation of fixed B1 word analyzer, TF-IDF and logistic score.
    s = ''.join(c for c in unicodedata.normalize('NFKD', s.lower()) if not unicodedata.combining(c))
    tokens = re.findall(r'(?u)\b\w\w+\b', s)
    counts = Counter(tokens + [' '.join(tokens[i:i+2]) for i in range(len(tokens)-1)])
    values = [(vocab[t], (1 + math.log(n)) * idf[vocab[t]]) for t, n in counts.items() if t in vocab]
    norm = math.sqrt(math.fsum(v*v for _, v in values))
    margin = intercept + (math.fsum(coef[i]*v for i, v in values) / norm if norm else 0)
    return float(expit(margin))


def prioritize(dev, review, header_model):
    vec, vocab, idf, coef, intercept, provenance = frozen_b1()
    main = [text(r, 'TextAsset') for r in review]
    header = [text(r, 'HeaderTextAsset') for r in review]
    # Reproduce B1's actual input contract, including its prepared header prefix.
    combined = ['CABECERA:\n' + h + '\n\nDOCUMENTO:\n' + t for h, t in zip(header, main)]
    x = vec.transform(combined)
    p_text = expit(np.asarray(x @ coef).reshape(-1) + intercept)
    reference = np.array([reference_score(s, vocab, idf, coef, intercept) for s in combined])
    error = float(np.max(np.abs(p_text - reference)))
    assert error < 1e-10, 'Frozen B1 inference parity failed'
    p_header = header_model.predict_proba(header)[:, 1]
    assert np.isfinite(p_text).all() and np.isfinite(p_header).all()
    provenance.update(ReviewSamplesVerified=682, IndependentFormulaMaxAbsError=error, HoldoutUsed=False)
    save('b3-text-only-inference-verification.json', provenance)
    # Semantic similarity is a review heuristic using only known development negatives.
    negatives = [r for r in dev if r['LabelFinal'] == 'OTRO_DOCUMENTO']
    neg_text = ['CABECERA:\n' + text(r, 'HeaderTextAsset') + '\n\nDOCUMENTO:\n' + text(r, 'TextAsset') for r in negatives]
    similarity = np.asarray((x @ vec.transform(neg_text).T).toarray()).max(axis=1)
    family_counts = Counter(r['FamilyId'] for r in dev)
    rows = []
    for r, t, h, sim, mt, ht in zip(review, p_text, p_header, similarity, main, header):
        low = 1 - min(t, h)
        diff = abs(t-h)
        uncertainty = max(1-2*abs(t-.5), 1-2*abs(h-.5))
        family_count = family_counts[r['FamilyId']]
        priority = .40*low + .25*diff + .15*uncertainty + .10/(1+family_count) + .10*sim
        reasons = []
        if min(t,h) <= .35: reasons.append('BAJO_PFACTURA')
        if diff >= .35: reasons.append('DESACUERDO_TEXTO_CABECERA')
        if uncertainty >= .8: reasons.append('INCERTIDUMBRE')
        if family_count <= 2: reasons.append('FAMILIA_POCO_REPRESENTADA')
        if sim >= .5: reasons.append('SIMILITUD_CON_OTRO_CONOCIDO')
        rows.append({'Sha256': r['Sha256'], 'FileName': r['FileName'], 'FamilyId': r['FamilyId'],
                     'TextAvailable': bool(mt.strip()), 'TextScore': float(t), 'HeaderTextScore': float(h),
                     'ScoreDifference': float(diff), 'Uncertainty': float(uncertainty),
                     'FamilyTrainingCount': family_count, 'ReviewReason': ';'.join(reasons) or 'PRIORIDAD_RELATIVA',
                     'HeaderTextAvailable': bool(ht.strip()), 'SemanticSimilarityToOtro': float(sim),
                     'PriorityScore': float(priority), 'InferenceStatus': 'OK'})
    rows.sort(key=lambda r: (-r['PriorityScore'], r['Sha256']))
    assert len(rows) == len({r['Sha256'] for r in rows}) == 682
    csvout('review-priority.csv', rows)
    top = [dict(r) for r in rows[:150]]
    assert len({r['Sha256'] for r in top}) == 150
    mapping = sorted(top, key=lambda r:r['Sha256'])
    random.Random(202609073).shuffle(mapping)
    for i, r in enumerate(mapping, 1):
        r['CandidateId'] = f'R{i:06d}'
    csvout('review-top150.csv', top)
    return rows, top


def blind(top, review):
    target = HERE / 'ReviewTop150'
    target.mkdir(exist_ok=True)
    (target / '.gitignore').write_text('*\n', encoding='utf-8')
    assets = target / 'assets'; assets.mkdir(exist_ok=True)
    lookup = {r['Sha256']: r for r in review}
    trace = []
    ids = []
    for r in sorted(top, key=lambda r:r['CandidateId']):
        source = lookup[r['Sha256']]
        assert source['LabelFinal'] == 'REVISAR' and source['Split'] != 'SEALED_TEST'
        src = Path(source['FullPageAsset'])
        assert src.is_file() and src.name.endswith('.full.png')
        digest = sha(src)
        assert digest == source['FullPageSha256']
        dest = assets / (r['CandidateId'] + '.png')
        if dest.exists(): assert sha(dest) == digest
        else: shutil.copyfile(src, dest)
        assert sha(dest) == digest
        ids.append(r['CandidateId'])
        trace.append({'CandidateId': r['CandidateId'], 'Sha256': r['Sha256'],
                      'SourceAsset': str(src), 'BlindAsset': str(dest), 'AssetSha256': digest})
    # Mapping remains OUTSIDE the folder handed to the operator.
    csvout('b3-blind-traceability.csv', trace)
    with (target / 'index.csv').open('w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=['CandidateId','Image']);w.writeheader()
        w.writerows({'CandidateId': i, 'Image': 'assets/' + i + '.png'} for i in ids)
    doc = '''<!doctype html><html lang="es"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Revisión documental</title><style>body{margin:0;background:#edf0f3;color:#17212b;font:16px system-ui}header{position:sticky;top:0;background:#fff;padding:14px 22px;display:flex;gap:16px;align-items:center;box-shadow:0 1px 4px #bbc}h1{font-size:18px;margin:0 auto 0 0}button,select{font:inherit;padding:8px;border:1px solid #aab7c4;border-radius:5px;background:white}main{padding:18px;text-align:center}img{max-width:100%;height:auto;background:white;box-shadow:0 2px 10px #bbc}p{color:#596875;font-size:14px}a{color:#175799}</style>
<header><h1>Revisión documental</h1><button id="prev">Anterior</button><label>Documento <select id="choice"></select></label><button id="next">Siguiente</button></header>
<main><p>Primera página del documento. <a id="open" target="_blank" rel="noopener">Abrir imagen en tamaño completo</a></p><img id="page" alt="Documento"></main>
<script>const ids=IDS;const choice=document.getElementById('choice');for(const id of ids){const o=document.createElement('option');o.value=id;o.textContent=id;choice.appendChild(o)}function show(){const id=choice.value;document.getElementById('page').src='assets/'+id+'.png';document.getElementById('page').alt=id;document.getElementById('open').href='assets/'+id+'.png';document.getElementById('prev').disabled=choice.selectedIndex===0;document.getElementById('next').disabled=choice.selectedIndex===ids.length-1}choice.onchange=show;document.getElementById('prev').onclick=()=>{choice.selectedIndex--;show();scrollTo(0,0)};document.getElementById('next').onclick=()=>{choice.selectedIndex++;show();scrollTo(0,0)};show();</script></html>'''.replace('IDS',json.dumps(ids))
    for r in top:
        assert r['Sha256'] not in doc and r['FileName'] not in doc and r['FamilyId'] not in doc
    assert not any(token in doc.lower() for token in ['score','predic','prioridad','reviewreason'])
    (target / 'index.html').write_text(doc, encoding='utf-8')
    return target, trace


def finish(dev, review, metrics, hashes, rows, top, target, trace, baseline):
    protect()
    # Verify every read was limited to the two permitted cohorts; no holdout text.
    allowed = {r['Sha256'] for r in dev + review}
    assert all(r['Sha256'] in allowed for r in TEXT_READS)
    csvout('b3-text-read-audit.csv', TEXT_READS)
    reason_names = ['BAJO_PFACTURA', 'DESACUERDO_TEXTO_CABECERA', 'INCERTIDUMBRE',
                    'FAMILIA_POCO_REPRESENTADA', 'SIMILITUD_CON_OTRO_CONOCIDO', 'PRIORIDAD_RELATIVA']
    def counts(rr):
        count = Counter(reason for r in rr for reason in r['ReviewReason'].split(';'))
        return {name: count[name] for name in reason_names}
    summary = {'Scored':len(rows),'Errors':sum(r['InferenceStatus']!='OK' for r in rows),
               'TopCount':len(top),'ReviewFamilies':len({r['FamilyId'] for r in rows}),
               'TopFamilies':len({r['FamilyId'] for r in top}),
               'AllReasonCountsOverlapping':counts(rows),'TopReasonCountsOverlapping':counts(top),
               'TopUnseenTrainingFamilyDocuments':sum(r['FamilyTrainingCount']==0 for r in top),
               'Note':'Criteria overlap. Scores are review heuristics, never labels. Existing training-family overlap in REVISAR is allowed for prioritization, not validation.'}
    save('b3-review-summary.json',summary)
    gates={'Status':'H1D10B3 APROBADO — GATES; SIN PROMOCION','OofShaCount':901,
           'ShaLeakage':0,'FamilyLeakage':0,'HoldoutLeakage':0,'HoldoutInferred':False,
           'RevisarUsedAsGroundTruth':False,'LabelsChanged':False,'ProtectedFilesUnchanged':len(baseline),
           'ReviewScored':682,'BlindUniqueShaCount':150,'BlindAssetsHashVerified':150,
           'BlindContainsScoresOrReasons':False,'ProductionSqlGmailModified':False,'ModelsPromoted':False}
    save('b3-gate-report.json',gates)
    manifest={'Plan':PLAN,'Python':sys.version,'Executable':sys.executable,
              'Versions':{'sklearn':sklearn.__version__,'numpy':np.__version__,'scipy':scipy.__version__,'joblib':joblib.__version__},
              'NoPackagesInstalled':True,'InternetDownloads':0,'B1TrainingSklearn':'1.8.0',
              'RuntimeDifference':'HEADER_TEXT trained with existing sklearn 1.3.2, same estimator parameters; B1 frozen WORD inference verified independently without refitting.',
              'HeaderWinner':metrics['Winner'],'ModelHashes':hashes,'FoldManifestSha256':sha(HERE/'fold-manifest.csv'),
              'HoldoutSeal':SEAL,'DevelopmentCount':901,'RevisarCount':682,'BlindFolder':str(target),
              'BlindFirstPageOnly':True,'TrainerSha256':sha(__file__)}
    save('b3-training-manifest.json',manifest)
    lines=['# H1D10B3 — HEADER_TEXT_ONLY y revisión ciega','', '**'+gates['Status']+'**','',
           '901 documentos, 828 FACTURA / 73 OTRO_DOCUMENTO, 154 FamilyId. Mismos 4 folds B1 sin regenerarlos. TF-IDF e IDF ajustados dentro de cada TRAIN, nunca sobre VALIDATION. Sólo HeaderTextAsset; ninguna imagen participa en entrenamiento o scoring.',
           'WORD: ngrams 1–2, min_df=2, max_df=.995, max_features=80000. CHAR: char_wb 3–5, min_df=2, max_features=120000. WORD_CHAR: 70000/100000 features. Lowercase, strip_accents unicode, sublinear_tf, L2. LogisticRegression C=1, balanced, liblinear, max_iter=2500, seed=20260907.',
           'Ganador elegido por macro F1 en evidencia independiente, con desempates predefinidos: **'+metrics['Winner']+'**. Threshold .5 sólo diagnóstico. No se ajustaron thresholds operativos.',
           'Runtime local existente: sklearn '+sklearn.__version__+', numpy '+np.__version__+', scipy '+scipy.__version__+'. B1 se entrenó con sklearn 1.8.0: se conserva su OOF original para comparación. Su modelo WORD se ejecuta mediante los pesos/IDF/vocabulario congelados, con paridad de fórmula independiente para los 682 casos. No se refiteó ni modificó B1. No se instalaron paquetes ni se descargó nada.', '',
           '| Evidencia | Variante | Recall F | Recall O | Precision F | Precision O | Macro F1 | Balanced acc. | ROC-AUC |',
           '|---|---|---:|---:|---:|---:|---:|---:|---:|']
    for scope in ['AllStrongLabels','IndependentEvidence']:
        for kind in ['TEXT_ONLY']+VARIANTS:
            m=metrics[scope][kind];keys=['RecallFactura','RecallOtroDocumento','PrecisionFactura','PrecisionOtroDocumento','MacroF1','BalancedAccuracy','RocAuc']
            lines.append('| '+scope+' | '+kind+' | '+' | '.join(f'{m[k]:.6f}' for k in keys)+' |')
    lines+=['','Evidencia independiente: 734 documentos (672 FACTURA, 62 OTRO_DOCUMENTO), sólo QR_ARCA_STRONG y EXISTING_GROUND_TRUTH. Las métricas globales incluyen 167 labels derivados de texto y no deben presentarse como certificación humana.','', '## Matrices OOF','', 'Filas reales / columnas predichas [FACTURA, OTRO_DOCUMENTO].']
    for scope in ['AllStrongLabels','IndependentEvidence']:
        for kind in ['TEXT_ONLY']+VARIANTS:
            lines.append(f"- {scope} / {kind}: `{metrics[scope][kind]['ConfusionMatrixRowsFacturaOtro_ColsFacturaOtro']}`.")
    lines+=['','## Prioridad de revisión','',f"682/682 scoreados, {summary['Errors']} errores. 150 SHA únicos, {summary['TopFamilies']} familias en la cola; {summary['TopUnseenTrainingFamilyDocuments']} documentos de familias no presentes en TRAIN.",
            'Score de prioridad: '+PLAN['PriorityFormula']+'. Criterios y cortes fijados antes del scoring; no son nuevos thresholds de clasificación. TextScore usa el contrato B1: CABECERA + texto de cabecera + DOCUMENTO + TextAsset. HeaderTextScore usa sólo cabecera. Similitud semántica = máximo coseno TF-IDF contra los 73 OTRO de desarrollo.',
            'Composición de criterios (solapados, no suman necesariamente el total):','',json.dumps(summary,ensure_ascii=False,indent=2),'',
            '## Revisión ciega','',f'Carpeta para el operador: `{target}`. Abrir `index.html`. Contiene únicamente IDs R000001…R000150 e imágenes de primera página ya preparadas por H1D10A, copiadas byte a byte y verificadas. No se ejecutó FULL_PAGE como modelo ni se renderizaron documentos nuevos.',
            'El orden de IDs se mezcló independientemente del ranking para no revelar prioridad. No hay scores, predicciones, razones, FamilyId, SHA ni nombres originales en el índice. review-priority.csv, review-top150.csv y b3-blind-traceability.csv son archivos analíticos fuera de la carpeta; NO entregarlos al operador junto con el material ciego.',
            'La ceguera se refiere a señales del modelo: el contenido original del documento permanece visible sin alteraciones.','',
            '## Artefactos y preservación','',
            'b3-artifact-index.csv enumera rutas absolutas y SHA-256 de scripts, métricas, manifiestos, 12 modelos OOF y el modelo final de cabecera, estado portátil B1 y 150 PNG. header-text-model-hashes.csv contiene los hashes de los 13 modelos de cabecera. b3-text-only-inference-verification.json identifica B1 y demuestra la paridad de ejecución.',
            'Los artefactos B1/B2, bank-manifest, corpus y H1D9B conservan sus hashes. Holdout sólo verificado por sello; no texto/imágenes/inferencia. REVISAR no se usó para entrenamiento ni se cambió LabelFinal. No se modificó SQL, Gmail, producción ni se promovieron modelos. La inferencia sobre familias REVISAR también presentes en desarrollo sólo prioriza revisión; no es validación de generalización.',
            'Se detiene H1D10B3 aquí.']
    (HERE/'resultado-H1D10B3.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    paths=[p for p in HERE.iterdir() if p.is_file() and p.name.startswith(('b3-','header-text-','review-','Run-H1D10B3','resultado-H1D10B3')) and p.name!='b3-artifact-index.csv']
    paths+=list(LOCAL.glob('*'))+list(target.rglob('*'))
    csvout('b3-artifact-index.csv',[{'Path':str(p),'SizeBytes':p.stat().st_size,'Sha256':sha(p)} for p in sorted(set(paths)) if p.is_file()])
    print(json.dumps({'Winner':metrics['Winner'],'Metrics':metrics,'Review':summary},indent=2),flush=True)


def main():
    assert not (HERE/'b3-gate-report.json').exists(), 'Completed B3 exists: do not automatically repeat'
    baseline=protect()
    dev,review=cohorts()
    LOCAL.mkdir(exist_ok=True);(LOCAL/'.gitignore').write_text('*\n',encoding='utf-8')
    save('b3-plan.json',PLAN)
    final,metrics,hashes=train_headers(dev)
    rows,top=prioritize(dev,review,final)
    target,trace=blind(top,review)
    finish(dev,review,metrics,hashes,rows,top,target,trace,baseline)


if __name__=='__main__':
    main()
