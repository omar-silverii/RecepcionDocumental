import csv
import json
import math
import os
import pathlib
import re
import unicodedata

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[3]
MODEL = ROOT / 'App_Data' / 'DocumentAi' / 'FamilyModels' / 'H1D10D5-FAMILY-001' / 'family-model.json'
OCR = pathlib.Path(os.environ.get('H1D10D5_OCR_ROOT', str(HERE / 'external-ocr-inputs')))


def preprocess(text, skip_chars, max_chars):
    text = (text or '')[skip_chars:skip_chars + max_chars]
    text = unicodedata.normalize('NFKD', text)
    chars = []
    for c in text:
        if unicodedata.category(c) == 'Mn':
            continue
        if ord(c) <= 127:
            chars.append(c.lower())
        elif c.isspace():
            chars.append(' ')
    value = ''.join(chars)
    value = re.sub(r'https?://\S+|www\.\S+|\b\S+@\S+\b', ' ', value, flags=re.I)
    value = re.sub(r'\b\d[\d.,:/\-]*\b', ' ', value)
    value = re.sub(r'[_|]+', ' ', value)
    drop_words = globals().get('_ACTIVE_DROP_WORDS', [])
    if drop_words:
        pattern = r'(?<![a-z0-9])(?:' + '|'.join(re.escape(x) for x in sorted(drop_words, key=len, reverse=True)) + r')(?![a-z0-9])'
        value = re.sub(pattern, ' ', value)
    return re.sub(r'\s+', ' ', value).strip()


def sigmoid(v):
    if v >= 0:
        z = math.exp(-v)
        return 1.0 / (1.0 + z)
    e = math.exp(v)
    return e / (1.0 + e)


def evaluate(contract, text):
    vocab = {term: i for i, term in enumerate(contract['vocabulary'])}
    value = preprocess(text, contract['skip_chars'], contract['max_chars'])
    tokens = re.findall(r'[a-z0-9]{2,}', value)
    counts = {}
    for i, token in enumerate(tokens):
        for term in ([token] if i + 1 >= len(tokens) else [token, token + ' ' + tokens[i + 1]]):
            idx = vocab.get(term)
            if idx is not None:
                counts[idx] = counts.get(idx, 0) + 1
    weighted = {}
    norm2 = 0.0
    for idx, count in counts.items():
        w = (1.0 + math.log(count)) * contract['idf'][idx]
        weighted[idx] = w
        norm2 += w * w
    if not weighted:
        return {'family': 'OTRO_BASE', 'confidence': 0.0, 'features': 0}
    norm = math.sqrt(norm2)
    ps = []
    for c in range(len(contract['classes'])):
        score = contract['intercept'][c]
        coef = contract['coef'][c]
        for idx, w in weighted.items():
            score += coef[idx] * (w / norm)
        ps.append(sigmoid(score))
    total = sum(ps)
    ps = [x / total for x in ps]
    best = max(range(len(ps)), key=lambda i: ps[i])
    return {'family': contract['classes'][best], 'confidence': ps[best], 'features': len(weighted), 'probabilities': dict(zip(contract['classes'], ps))}


def main():
    contract = json.loads(MODEL.read_text(encoding='utf-8'))
    global _ACTIVE_DROP_WORDS
    _ACTIVE_DROP_WORDS = contract.get('drop_words', [])
    expected = {row['id']: row for row in csv.DictReader((HERE / 'live-c04-c05.csv').open(encoding='utf-8'))}
    inputs = {
        'C04': OCR / 'TEST_salario_2026_08.txt',
        'C05': OCR / 'TEST_bna_2026_08.txt',
    }
    rows = []
    passed = True
    for case, path in inputs.items():
        actual = evaluate(contract, path.read_text(encoding='utf-8', errors='ignore'))
        exp = expected[case]
        target = exp['pred']
        target_prob = float(exp[target])
        delta = abs(actual['confidence'] - target_prob)
        ok = actual['family'] == target and delta <= 1e-10 and actual['confidence'] >= contract['decision_threshold']
        passed = passed and ok
        rows.append({'case': case, 'expected_family': target, 'actual_family': actual['family'], 'expected_probability': target_prob, 'actual_probability': actual['confidence'], 'delta': delta, 'features': actual['features'], 'pass': ok})
    out = HERE / 'runtime-parity.json'
    out.write_text(json.dumps({'pass': passed, 'rows': rows}, indent=2), encoding='utf-8')
    print(json.dumps({'pass': passed, 'rows': rows}, indent=2))
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(main())
