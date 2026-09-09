#!/usr/bin/env python3
import json, os, unicodedata
from pathlib import Path

HERE = Path(__file__).resolve().parent
OCR = Path(os.environ.get('H1D10D5_OCR_ROOT', str(HERE / 'external-ocr-inputs')))

PAYROLL = [
    ['SUELDO','HABERES','REMUNERATIVO','REMUNERACION'],
    ['EMPLEADOR','TRABAJADOR'],
    ['JUBILACION','OBRA SOCIAL','CARGAS SOCIALES','ART'],
    ['DESCUENTOS','SUELDO NETO','NETO A PAGAR','SUELDO BASICO'],
    ['LEGAJO','ANTIGUEDAD','CUIL'],
    ['COMPOSICION SALARIAL','LAPSO LIQUIDADO'],
]
BANK = [
    ['COMISION','COMISIONES','CARGO','CARGOS'],
    ['BANCO','BNA'],
    ['CLIENTE','CARTERA CONSUMO','CARTERA COMERCIAL'],
    ['CUENTA CORRIENTE','CAJA DE AHORRO','TARJETA DE CREDITO','PAQUETE'],
    ['ENTRARAN EN VIGOR','ENTRARA EN VIGOR','COMUNICAR','COMUNICARTE','COMUNICARLE'],
    ['REGIMEN DE TRANSPARENCIA','BCRA','BANCO CENTRAL'],
]
FISCAL = [
    ['CUIT'], ['CAE','CAEA'], ['PUNTO DE VENTA','PTO VTA'],
    ['COMP NRO','COMPROBANTE','NRO COMPROBANTE'], ['IMPORTE TOTAL','TOTAL'],
    ['IVA'], ['FECHA DE EMISION'],
]
EXPLICIT = ['FACTURA A','FACTURA B','FACTURA C','FACTURA M','FACTURA E','FACTURA DE CREDITO ELECTRONICA']


def normalize(value):
    d = unicodedata.normalize('NFD', (value or '').upper())
    b = ''.join(' ' if c.isspace() else c for c in d if unicodedata.category(c) != 'Mn')
    return ' '.join(unicodedata.normalize('NFC', b).split())

def phrase(n, p): return (' ' + p + ' ') in (' ' + n + ' ')
def compact(v): return ''.join(c for c in v if c.isalnum())
def signal(n, c, p): return phrase(n,p) or (' ' in p and compact(p) in c)
def group_hits(n, groups): return [[x for x in g if phrase(n,x)] for g in groups]

def evaluate(case, family, path):
    text = path.read_text(encoding='utf-8', errors='replace')
    n, c = normalize(text), compact(normalize(text))
    fiscal_count = sum(any(signal(n,c,x) for x in g) for g in FISCAL)
    explicit = any(signal(n,c,x) for x in EXPLICIT) or (phrase(n,'FACTURA') and any(phrase(n,x) for x in ['A','B','C','M','E']))
    ocr_confidence = 95 if explicit and fiscal_count >= 3 else 55 if explicit else 45 if fiscal_count >= 3 else None
    strong = any(phrase(n,x) for x in ['CAE','CAEA','PUNTO DE VENTA','PTO VTA'])
    groups = PAYROLL if family == 'RECIBO_HABERES' else BANK
    hits = group_hits(n, groups)
    group_count = sum(bool(h) for h in hits)
    allowed = not strong and not (ocr_confidence is not None and ocr_confidence >= 45)
    if family == 'RECIBO_HABERES':
        allowed = allowed and group_count >= 3
    else:
        allowed = allowed and bool(hits[0]) and bool(hits[1]) and group_count >= 4
    return {
        'case': case, 'family': family, 'fiscal_count': fiscal_count,
        'ocr_confidence': ocr_confidence, 'strong_fiscal_anchor': strong,
        'semantic_groups': group_count, 'group_hits': hits, 'safety_gate_pass': allowed,
    }

rows = [
    evaluate('C04','RECIBO_HABERES', OCR/'TEST_salario_2026_08.txt'),
    evaluate('C05','NOTIFICACION_BANCARIA', OCR/'TEST_bna_2026_08.txt'),
]
out = {'pass': all(r['safety_gate_pass'] for r in rows), 'rows': rows}
(HERE/'safety-policy-validation.json').write_text(json.dumps(out, indent=2, ensure_ascii=False), encoding='utf-8')
print(json.dumps(out, indent=2, ensure_ascii=False))
raise SystemExit(0 if out['pass'] else 1)
