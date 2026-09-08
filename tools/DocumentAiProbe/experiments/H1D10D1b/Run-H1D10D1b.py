#!/usr/bin/env python3
"""
H1D10D1b - Precedencia documental y veto de evidencia fuerte.

DIAGNOSTICO SOLAMENTE.
No entrena, no modifica labels, no toca produccion, no abre SEALED_TEST.

Requiere:
- H1D10D1/Run-H1D10D1.py
- H1D10D1/local-results/independent-regression.csv
- H1D10A/bank-manifest.csv

Objetivos:
1) A nivel documento, NOTA_CREDITO / NOTA_DEBITO / RECIBO prevalecen sobre
   menciones secundarias de FACTURA.
2) Un QR ARCA valido con CanonicalLabel=OTRO_DOCUMENTO veta una decision
   FACTURA basada solo en titulo OCR.
3) Exporta los falsos FACTURA residuales con las lineas OCR relevantes para
   inspeccion, sin modificar la regla de produccion.
"""
from __future__ import annotations
import csv, json, importlib.util, re
from pathlib import Path
from collections import Counter

HERE=Path(__file__).resolve().parent
EXPERIMENTS=HERE.parent
D1=EXPERIMENTS/"H1D10D1"
A=EXPERIMENTS/"H1D10A"
OUT=HERE/"local-results"

def load_d1():
    p=D1/"Run-H1D10D1.py"
    spec=importlib.util.spec_from_file_location("h1d10d1",p)
    m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
    return m

def read_csv(p):
    with p.open("r",encoding="utf-8-sig",newline="") as f:
        return list(csv.DictReader(f))

def safe_read(path):
    if not path: return ""
    p=Path(path)
    if not p.exists(): return ""
    return p.read_text(encoding="utf-8-sig",errors="replace")

def valid_qr_labels(row):
    out=[]
    try:
        items=json.loads(row.get("QrEvidence") or "[]")
    except Exception:
        items=[]
    for q in items:
        if q.get("IsValid") and q.get("CanonicalLabel"):
            out.append(str(q["CanonicalLabel"]))
    return out

def resolve_binary(title_types, qr_labels):
    types=set(title_types)
    specifics=types & {"NOTA_CREDITO","NOTA_DEBITO","RECIBO"}

    # Strong source veto/support comes first.
    if "OTRO_DOCUMENTO" in qr_labels and "FACTURA" not in qr_labels:
        return "OTRO_DOCUMENTO","QR_ARCA_VALID_OTHER"
    if "FACTURA" in qr_labels and "OTRO_DOCUMENTO" not in qr_labels:
        return "FACTURA","QR_ARCA_VALID_FACTURA"

    # Specific documentary titles beat a secondary FACTURA mention.
    if specifics:
        return "OTRO_DOCUMENTO","SPECIFIC_TITLE_PRECEDENCE"

    if "FACTURA" in types:
        return "FACTURA","FACTURA_TITLE_ONLY"

    return "","NO_DECISION"

def factura_hit_lines(header):
    lines=[]
    for i,line in enumerate(header.splitlines(),1):
        if "FACTURA" in re.sub(r"[^A-Z0-9]+","",line.upper()):
            lines.append(f"{i}: {line.strip()}")
    return lines

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    d1=load_d1()
    manifest=read_csv(A/"bank-manifest.csv")
    by_sha={r["Sha256"]:r for r in manifest if r.get("Split")!="SEALED_TEST"}

    independent=[
        r for r in manifest
        if r.get("Split")!="SEALED_TEST"
        and r.get("LabelSource") in ("QR_ARCA_STRONG","EXISTING_GROUND_TRUTH")
        and r.get("LabelFinal") in ("FACTURA","OTRO_DOCUMENTO")
    ]

    rows=[]
    residual=[]
    for r in independent:
        header=safe_read(r.get("HeaderTextAsset",""))
        conf=float(r.get("HeaderOcrConfidence") or 0)
        hits=d1.detect_title_candidates(header,conf)
        types=[x["DocumentType"] for x in hits]
        qrs=valid_qr_labels(r)
        decision,reason=resolve_binary(types,qrs)
        correct=(decision==r["LabelFinal"]) if decision else None

        out={
            "Sha256":r["Sha256"],
            "FileName":r.get("FileName",""),
            "Expected":r["LabelFinal"],
            "LabelSource":r["LabelSource"],
            "TitleTypes":"|".join(types),
            "QrLabels":"|".join(qrs),
            "ResolvedDecision":decision,
            "ResolutionReason":reason,
            "CorrectWhenDecided":"" if correct is None else str(correct),
        }
        rows.append(out)

        if decision=="FACTURA" and r["LabelFinal"]=="OTRO_DOCUMENTO":
            hit_lines=factura_hit_lines(header)
            residual.append({
                **out,
                "ExistingGroundTruth":r.get("ExistingGroundTruth",""),
                "DocumentTypeManifest":r.get("DocumentType",""),
                "FamilyId":r.get("FamilyId",""),
                "HeaderOcrConfidence":r.get("HeaderOcrConfidence",""),
                "FacturaHitLines":" || ".join(hit_lines),
                "HeaderText":header,
            })

    decided=[r for r in rows if r["ResolvedDecision"]]
    correct=[r for r in decided if r["CorrectWhenDecided"]=="True"]
    wrong=[r for r in decided if r["CorrectWhenDecided"]=="False"]

    with (OUT/"independent-resolved.csv").open("w",encoding="utf-8-sig",newline="") as f:
        w=csv.DictWriter(f,fieldnames=list(rows[0].keys()));w.writeheader();w.writerows(rows)

    if residual:
        with (OUT/"residual-false-factura.csv").open("w",encoding="utf-8-sig",newline="") as f:
            w=csv.DictWriter(f,fieldnames=list(residual[0].keys()));w.writeheader();w.writerows(residual)
    else:
        (OUT/"residual-false-factura.csv").write_text("",encoding="utf-8")

    summary={
        "Experiment":"H1D10D1b",
        "TrainingExecuted":False,
        "ProductionModified":False,
        "LabelsModified":False,
        "SealedAssetsRead":False,
        "Rows":len(rows),
        "Decisions":len(decided),
        "CorrectWhenDecided":len(correct),
        "WrongWhenDecided":len(wrong),
        "PrecisionWhenDecided":len(correct)/len(decided) if decided else None,
        "ResidualFalseFactura":len(residual),
        "ResolutionReasons":dict(Counter(r["ResolutionReason"] for r in rows)),
    }
    (OUT/"summary.json").write_text(json.dumps(summary,indent=2,ensure_ascii=False),encoding="utf-8")

    md=f"""# H1D10D1b — precedencia documental + evidencia fuerte

Diagnóstico solamente.

- Filas independientes: **{len(rows)}**
- Decisiones: **{len(decided)}**
- Correctas cuando decidió: **{len(correct)}**
- Incorrectas cuando decidió: **{len(wrong)}**
- Precisión cuando decidió: **{summary['PrecisionWhenDecided']}**
- Falsos FACTURA residuales: **{len(residual)}**

Reglas diagnósticas:
1. QR ARCA válido prevalece sobre una mención OCR.
2. NOTA_CREDITO / NOTA_DEBITO / RECIBO prevalecen sobre FACTURA secundaria.
3. Si queda FACTURA sola, todavía NO se promueve: los falsos residuales se exportan para inspección.

No se modifica producción ni ground truth.
"""
    (OUT/"resultado.md").write_text(md,encoding="utf-8")
    print(md)
    print("Subir resultado.md, summary.json y residual-false-factura.csv.")

if __name__=="__main__":
    main()
