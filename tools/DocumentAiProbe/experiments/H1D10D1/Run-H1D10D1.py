#!/usr/bin/env python3
"""
H1D10D1 - Canonical document-title normalization diagnostic.

DIAGNOSTIC ONLY.
- Does not train.
- Does not modify labels.
- Does not score models.
- Does not open/read SEALED_TEST assets.
- Does not modify production code.
- Uses existing H1D10A/H1D10C artifacts only.

Purpose:
1) Normalize OCR title lines canonically (case/accents/spaces/separators removed).
2) Recover explicit documentary titles rejected by the old whole-line rule.
3) Keep transfer/advance prose as semantic cues, NOT as title evidence.
4) Measure false-positive risk on the independent development subset
   QR_ARCA_STRONG + EXISTING_GROUND_TRUTH, excluding SEALED_TEST.
"""
from __future__ import annotations

import argparse
import csv
import json
import re
import unicodedata
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXPERIMENTS = HERE.parent
H1D10A = EXPERIMENTS / "H1D10A"
H1D10C = EXPERIMENTS / "H1D10C"
OUT = HERE / "local-results"

TITLE_ALIASES = {
    # Specific documentary titles first.
    "NOTA_CREDITO": ("NOTADECREDITO", "NOTACREDITO"),
    "NOTA_DEBITO": ("NOTADEDEBITO", "NOTADEBITO"),
    "FACTURA": (
        "FACTURADECREDITOELECTRONICAMIPYMES",
        "FACTURADECREDITOELECTRONICA",
        "FACTURAELECTRONICA",
        "FACTURA",
    ),
    "RECIBO": ("RECIBODEPAGO", "RECIBODECOBRO", "RECIBODESUELDO", "RECIBO"),
}

# These are NOT title classes. They are reported separately as semantic cues.
SEMANTIC_CUES = {
    "TRANSFERENCIA_ANTICIPO": (
        "ANTICIPODEFONDOS",
        "SOLICITUDDEANTICIPO",
        "SOLICITUDDETRANSFERENCIA",
        "TRANSFERENCIA",
        "LIBERACIONDEFONDOS",
    ),
}

# Avoid treating obvious references to an invoice as an invoice title.
FACTURA_REFERENCE_PHRASES = (
    "PAGODEFACTURA",
    "REFERENCIAFACTURA",
    "FACTURAASOCIADA",
    "NOFACTURA",
)

def read_csv(path: Path):
    with path.open("r", encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))

def deaccent_upper(value: str) -> str:
    value = (value or "").upper()
    return "".join(
        c for c in unicodedata.normalize("NFKD", value)
        if not unicodedata.combining(c)
    )

def canonical(value: str) -> str:
    """Canonical OCR comparison form: only A-Z/0-9 remain."""
    return re.sub(r"[^A-Z0-9]+", "", deaccent_upper(value))

def safe_read(path_value: str) -> str:
    if not path_value:
        return ""
    p = Path(path_value)
    if not p.exists():
        return ""
    return p.read_text(encoding="utf-8-sig", errors="replace")

def _find_alias(compact: str, aliases):
    for alias in aliases:
        i = compact.find(alias)
        if i >= 0:
            return alias, i
    return None, -1

def detect_title_candidates(header: str, confidence: float):
    """
    Conservative canonical-title detector.

    It does NOT require the entire OCR line to equal the title.
    Instead it canonicalizes each line and looks for documentary title aliases.
    Specific titles (NC/ND/RECIBO) are preferred over FACTURA.

    FACTURA gets a small reference veto so text like "PAGO DE FACTURA" does
    not become title evidence just because spaces/punctuation were removed.
    """
    if confidence < 0.80 or not header:
        return []

    candidates = []
    for line_no, raw_line in enumerate(header.splitlines(), start=1):
        line = raw_line.strip()
        if not line:
            continue
        comp = canonical(line)
        if not comp:
            continue

        # Specific title families first.
        specific_hit = False
        for typ in ("NOTA_CREDITO", "NOTA_DEBITO", "RECIBO"):
            alias, idx = _find_alias(comp, TITLE_ALIASES[typ])
            if idx >= 0:
                candidates.append({
                    "DocumentType": typ,
                    "Alias": alias,
                    "CanonicalLine": comp,
                    "OriginalLine": line,
                    "LineNumber": line_no,
                    "Position": idx,
                    "Reason": "CANONICAL_TITLE_ALIAS",
                })
                specific_hit = True
                break

        if specific_hit:
            continue

        # Generic FACTURA only after specific fiscal types were excluded.
        alias, idx = _find_alias(comp, TITLE_ALIASES["FACTURA"])
        if idx >= 0:
            around = comp[max(0, idx - 24): idx + len(alias) + 36]
            ref = any(x in around for x in FACTURA_REFERENCE_PHRASES)

            # Strong title context:
            # - near line start, including OCR class letter A/B/C/M/E
            # - OR followed by number/class/code-like material
            after = comp[idx + len(alias): idx + len(alias) + 32]
            near_start = idx <= 5
            identifier_after = bool(re.match(
                r"^(?:[ABCEM])?(?:NRO|NUMERO|CODIGO|COD|N|[0-9])", after
            ))
            if (not ref) or identifier_after:
                candidates.append({
                    "DocumentType": "FACTURA",
                    "Alias": alias,
                    "CanonicalLine": comp,
                    "OriginalLine": line,
                    "LineNumber": line_no,
                    "Position": idx,
                    "Reason": "CANONICAL_TITLE_ALIAS",
                })

    # Deduplicate by document type while preserving first evidence.
    out = []
    seen = set()
    for c in candidates:
        if c["DocumentType"] not in seen:
            seen.add(c["DocumentType"])
            out.append(c)
    return out

def detect_semantic_cues(header: str):
    comp = canonical(header)
    found = []
    for typ, aliases in SEMANTIC_CUES.items():
        for alias in aliases:
            if alias in comp:
                found.append({"CueType": typ, "Alias": alias})
                break
    return found

def binary_label(document_type: str) -> str:
    return "FACTURA" if document_type == "FACTURA" else "OTRO_DOCUMENTO"

def parse_float(v, default=0.0):
    try:
        return float(v)
    except Exception:
        return default

def choose_header_text(case_row):
    # Preferred: actual local H1D10C-resolved asset.
    text = safe_read(case_row.get("HeaderTextPathResolved", ""))
    if text:
        return text, "HEADER_ASSET"

    # Fallback only for diagnostic portability: H1D10C stored a short snippet.
    # This does not replace the real run on Omar's workspace.
    snippet = case_row.get("HeaderCueSnippet", "")
    return snippet, "H1D10C_SNIPPET_FALLBACK" if snippet else "MISSING"

def run_top150():
    cases_path = H1D10C / "local-results" / "cases.csv"
    if not cases_path.exists():
        raise RuntimeError(
            f"Missing {cases_path}. Run H1D10C first on the local workspace."
        )

    cases = read_csv(cases_path)
    details = []
    for r in cases:
        header, source = choose_header_text(r)
        conf = parse_float(r.get("HeaderOcrConfidence"))
        titles = detect_title_candidates(header, conf)
        cues = detect_semantic_cues(header)
        types = [x["DocumentType"] for x in titles]

        details.append({
            "CandidateId": r.get("CandidateId", ""),
            "Sha256": r.get("Sha256", ""),
            "VisualReferenceType": r.get("VisualReferenceType", ""),
            "VisualReferenceIsGroundTruth": "False",
            "PreviousDiagnosticCategory": r.get("DiagnosticCategory", ""),
            "HeaderOcrConfidence": r.get("HeaderOcrConfidence", ""),
            "HeaderSource": source,
            "CanonicalTitleTypes": "|".join(types),
            "CanonicalTitleEvidence": json.dumps(titles, ensure_ascii=False),
            "SemanticCueTypes": "|".join(x["CueType"] for x in cues),
            "SemanticCueEvidence": json.dumps(cues, ensure_ascii=False),
        })

    return details

def run_independent_regression():
    """
    Independent-label safety regression.
    IMPORTANT: filter SEALED_TEST before opening any header asset.
    """
    manifest_path = H1D10A / "bank-manifest.csv"
    if not manifest_path.exists():
        raise RuntimeError(f"Missing {manifest_path}")

    rows = read_csv(manifest_path)

    # Never read sealed assets.
    rows = [r for r in rows if r.get("Split") != "SEALED_TEST"]

    independent = [
        r for r in rows
        if r.get("LabelSource") in ("QR_ARCA_STRONG", "EXISTING_GROUND_TRUTH")
        and r.get("LabelFinal") in ("FACTURA", "OTRO_DOCUMENTO")
    ]

    detail = []
    for r in independent:
        header = safe_read(r.get("HeaderTextAsset", ""))
        conf = parse_float(r.get("HeaderOcrConfidence"))
        titles = detect_title_candidates(header, conf)
        detected = sorted({binary_label(x["DocumentType"]) for x in titles})
        ambiguous = len(detected) > 1
        decision = detected[0] if len(detected) == 1 else ""

        detail.append({
            "Sha256": r.get("Sha256", ""),
            "LabelFinal": r.get("LabelFinal", ""),
            "LabelSource": r.get("LabelSource", ""),
            "Split": r.get("Split", ""),
            "HeaderAvailable": str(bool(header)),
            "CanonicalTitleTypes": "|".join(x["DocumentType"] for x in titles),
            "CanonicalBinaryDecision": decision,
            "Ambiguous": str(ambiguous),
            "CorrectWhenDecided": (
                str(decision == r.get("LabelFinal")) if decision else ""
            ),
        })

    return detail

def write_csv(path, rows):
    if not rows:
        path.write_text("", encoding="utf-8")
        return
    with path.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)

def main():
    OUT.mkdir(parents=True, exist_ok=True)

    top = run_top150()
    reg = run_independent_regression()

    write_csv(OUT / "top150-canonical-title.csv", top)
    write_csv(OUT / "independent-regression.csv", reg)

    old_rejected = [
        r for r in top
        if r["PreviousDiagnosticCategory"] == "HEADER_CUE_PRESENT_RULE_REJECTED"
    ]

    explicit_types = {"FACTURA", "NOTA_CREDITO", "NOTA_DEBITO", "RECIBO"}
    explicit_rejected = [
        r for r in old_rejected if r["VisualReferenceType"] in explicit_types
    ]
    explicit_recovered = [
        r for r in explicit_rejected
        if r["VisualReferenceType"] in set(
            filter(None, r["CanonicalTitleTypes"].split("|"))
        )
    ]

    transfer_rejected = [
        r for r in old_rejected
        if r["VisualReferenceType"] == "TRANSFERENCIA_ANTICIPO"
    ]
    transfer_as_title = [
        r for r in transfer_rejected if r["CanonicalTitleTypes"]
    ]
    transfer_as_semantic_cue = [
        r for r in transfer_rejected
        if "TRANSFERENCIA_ANTICIPO" in r["SemanticCueTypes"].split("|")
    ]

    decided = [r for r in reg if r["CanonicalBinaryDecision"]]
    correct = [r for r in decided if r["CorrectWhenDecided"] == "True"]
    wrong = [r for r in decided if r["CorrectWhenDecided"] == "False"]
    ambiguous = [r for r in reg if r["Ambiguous"] == "True"]

    summary = {
        "Experiment": "H1D10D1",
        "Purpose": "Canonical title normalization diagnostic only",
        "TrainingExecuted": False,
        "ProductionModified": False,
        "LabelsModified": False,
        "ModelInferenceExecuted": False,
        "SealedAssetsRead": False,
        "Top150": {
            "Cases": len(top),
            "VisualReferenceIsGroundTruth": False,
            "OldHeaderCueRuleRejected": len(old_rejected),
            "ExplicitDocumentTitleCasesAmongOldRejected": len(explicit_rejected),
            "ExplicitDocumentTitlesRecovered": len(explicit_recovered),
            "ExplicitRecoveryRate": (
                len(explicit_recovered) / len(explicit_rejected)
                if explicit_rejected else 0.0
            ),
            "TransferAdvanceCasesAmongOldRejected": len(transfer_rejected),
            "TransferAdvanceIncorrectlyPromotedToTitle": len(transfer_as_title),
            "TransferAdvanceKeptAsSemanticCue": len(transfer_as_semantic_cue),
            "RecoveredByVisualReferenceType": dict(Counter(
                r["VisualReferenceType"] for r in explicit_recovered
            )),
        },
        "IndependentDevelopmentRegression": {
            "Rows": len(reg),
            "DecisionsByCanonicalTitle": len(decided),
            "CorrectWhenDecided": len(correct),
            "WrongWhenDecided": len(wrong),
            "Ambiguous": len(ambiguous),
            "PrecisionWhenDecided": (
                len(correct) / len(decided) if decided else None
            ),
            "WrongExamples": [
                {
                    "Sha256": r["Sha256"],
                    "Expected": r["LabelFinal"],
                    "Detected": r["CanonicalBinaryDecision"],
                    "Types": r["CanonicalTitleTypes"],
                }
                for r in wrong[:20]
            ],
        },
    }

    (OUT / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

    md = f"""# H1D10D1 — normalización canónica de títulos

Diagnóstico solamente. No entrena, no cambia labels, no usa modelos y no lee assets del holdout sellado.

## Top150

- Casos: **{len(top)}**
- Rechazados previamente por la regla de título con cue en cabecera: **{len(old_rejected)}**
- De esos, títulos documentales explícitos (FACTURA/NC/ND/RECIBO): **{len(explicit_rejected)}**
- Recuperados por normalización canónica: **{len(explicit_recovered)}/{len(explicit_rejected)}**
- Transferencias/anticipos dentro del mismo grupo: **{len(transfer_rejected)}**
- Transferencias/anticipos promovidos erróneamente a título: **{len(transfer_as_title)}**
- Transferencias/anticipos conservados como cue semántico: **{len(transfer_as_semantic_cue)}**

Recuperados por tipo:
`{json.dumps(summary["Top150"]["RecoveredByVisualReferenceType"], ensure_ascii=False)}`

## Regresión independiente de desarrollo

Se usaron solamente `QR_ARCA_STRONG + EXISTING_GROUND_TRUTH`, excluyendo `SEALED_TEST` antes de abrir cualquier texto.

- Filas independientes: **{len(reg)}**
- Decisiones producidas por título canónico: **{len(decided)}**
- Correctas cuando decidió: **{len(correct)}**
- Incorrectas cuando decidió: **{len(wrong)}**
- Ambiguas: **{len(ambiguous)}**
- Precisión al decidir: **{summary["IndependentDevelopmentRegression"]["PrecisionWhenDecided"]}**

## Regla

La comparación canónica:
- mayúsculas;
- elimina tildes;
- elimina espacios;
- elimina guiones, puntos y separadores;
- conserva sólo `A-Z0-9`.

Ejemplo:

`NOTA D E D É B ITO` → `NOTADEDEBITO`

No se agregan variantes espaciales una por una.

IMPORTANTE: `TRANSFERENCIA/ANTICIPO` se informa como evidencia semántica y no como título documental.
"""
    (OUT / "resultado.md").write_text(md, encoding="utf-8")

    print(md)
    if wrong:
        print("\nATENCION: hay falsos positivos en la regresión independiente. No promover esta regla.")
    else:
        print("\nH1D10D1 diagnóstico completado sin falsos positivos observados en las decisiones independientes.")

if __name__ == "__main__":
    main()
