#!/usr/bin/env python3
"""H1D10D3 - fusión de evidencia fiscal/estructural de alta precisión.

DIAGNÓSTICO SOLAMENTE.

Parte de los resultados ya validados de H1D10D1c/H1D10D2 y sólo intenta
resolver abstenciones mediante evidencia fiscal/estructural general:

- código fiscal 03 + estructura documental => OTRO_DOCUMENTO;
- título fuerte FACTURA DE CREDITO + numeración/estructura => FACTURA;
- línea FACTURA + número de comprobante + múltiples anclas fiscales => FACTURA.

La revisión visual de ChatGPT NO participa de la decisión. Se conserva sólo
como referencia diagnóstica para describir el Top150.
"""
from __future__ import annotations

import csv
import importlib.util
import json
import os
import re
import unicodedata
import zipfile
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXPERIMENTS = HERE.parent
H1D10A = EXPERIMENTS / "H1D10A"
H1D10B = EXPERIMENTS / "H1D10B"
D1 = EXPERIMENTS / "H1D10D1"
D1C = EXPERIMENTS / "H1D10D1c"
D2 = EXPERIMENTS / "H1D10D2"

BANK_PATH = H1D10A / "bank-manifest.csv"
TEXT_ZIP_PATH = H1D10B / "text.zip"
D1C_BASELINE_PATH = D1C / "independent-resolved-d1c.csv"
D2_TOP150_PATH = D2 / "top150-resolved-d2.csv"

INVOICE_NUMBER_RE = re.compile(
    r"(?i)(?:\b[A-Z]\s*)?\d{4,5}\s*[-–—]\s*\d{5,8}\b"
)
FACTURA_NUMBER_LINE_RE = re.compile(
    r"(?i)\bFACTURA\b.{0,12}(?:[A-Z]\s*)?\d{4,5}\s*[-–—]\s*\d{5,8}\b"
)


def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def load_d1():
    return load_module(D1 / "Run-H1D10D1.py", "h1d10d1_for_d3")


def read_csv(path: Path):
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def write_csv(path: Path, rows, fieldnames=None):
    if not rows:
        path.write_text("", encoding="utf-8")
        return
    fields = fieldnames or list(rows[0].keys())
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def parse_float(value, default=0.0):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def deaccent_upper(value: str) -> str:
    value = (value or "").upper()
    return "".join(
        c for c in unicodedata.normalize("NFKD", value)
        if not unicodedata.combining(c)
    )


def canonical(value: str) -> str:
    return re.sub(r"[^A-Z0-9]+", "", deaccent_upper(value))


def asset_basename(path_value: str) -> str:
    return os.path.basename((path_value or "").replace("\\", "/"))


class TextSource:
    """Portable reader restricted to non-sealed H1D10B text assets."""

    def __init__(self, text_zip_path: Path):
        self._zip = zipfile.ZipFile(text_zip_path)
        self._names = set(self._zip.namelist())
        self.sealed_assets_read = False

    def close(self):
        self._zip.close()

    def read(self, manifest_row, field_name: str) -> str:
        if manifest_row.get("Split") == "SEALED_TEST":
            raise RuntimeError("SEALED_TEST row reached D3 text reader")
        value = manifest_row.get(field_name, "")
        if "SEALED_TEST" in value.upper():
            raise RuntimeError("SEALED_TEST path rejected by D3 text reader")
        base = asset_basename(value)
        if not base:
            return ""
        member = f"text/{base}"
        if member not in self._names:
            return ""
        return self._zip.read(member).decode("utf-8-sig", errors="replace")


def nonempty_lines(text: str):
    return [line.strip() for line in (text or "").splitlines() if line.strip()]


def email_like(header: str) -> bool:
    compact = canonical(header)
    markers = sum(
        marker in compact
        for marker in ("ASUNTO", "DATOSADJUNTOS", "ENVIADOEL", "PARA")
    )
    return markers >= 2


def _has_word(text: str, pattern: str) -> bool:
    return bool(re.search(pattern, deaccent_upper(text), flags=re.IGNORECASE))


def fiscal_anchors(header: str, native: str):
    """Return general fiscal/structural anchors, not supplier-specific rules."""
    text = f"{header or ''}\n{native or ''}"
    compact = canonical(text)
    anchors = []

    if "CUIT" in compact:
        anchors.append("CUIT")
    if "IVA" in compact or "RESPONSABLEINSCRIPTO" in compact:
        anchors.append("IVA")
    if "INGBRUTOS" in compact or "INGRESOSBRUTOS" in compact:
        anchors.append("ING_BRUTOS")
    if "INICIODEACTIVIDADES" in compact:
        anchors.append("START_DATE")
    if "FECHADEEMISION" in compact or "FECHAEMISION" in compact:
        anchors.append("ISSUE_DATE")
    if "PUNTODEVENTA" in compact and (
        "COMPNRO" in compact or "COMPROBANTE" in compact or "NRO" in compact
    ):
        anchors.append("POS_COMP")
    if _has_word(text, r"\bC\s*\.?\s*A\s*\.?\s*E\s*\.?\b"):
        anchors.append("CAE")
    if (
        "TOTALAPAGAR" in compact
        or "IMPORTETOTAL" in compact
        or "SUBTOTAL" in compact
    ):
        anchors.append("TOTAL")
    if _has_word(text, r"\bORIGINAL\b"):
        anchors.append("ORIGINAL")

    return anchors


def code_03_evidence(header: str) -> str:
    """Find a documentary code line such as COD. 03 / Codigo 03."""
    for line in nonempty_lines(header):
        compact = canonical(line)
        if re.match(r"^(?:COD|CODIGO)0?3(?:[A-Z]|$)", compact):
            return line
    return ""


def specific_non_factura_title_types(d1, header: str, confidence: float):
    return [
        item["DocumentType"]
        for item in d1.detect_title_candidates(header, confidence)
        if item["DocumentType"] != "FACTURA"
    ]


def credit_invoice_title_evidence(header: str):
    """Return title line and nearby number support for strong credit-invoice title."""
    lines = nonempty_lines(header)
    for index, line in enumerate(lines):
        compact = canonical(line)
        accepted_title = compact.startswith("FACTURADECREDITO")
        accepted_ocr_original = bool(re.match(
            r"^.{0,2}ORIGINALFACTURADECREDITOELECTRONICA", compact
        ))
        if not (accepted_title or accepted_ocr_original):
            continue

        nearby = " ".join(lines[index:index + 7])
        return line, bool(INVOICE_NUMBER_RE.search(nearby))
    return "", False


def structured_factura_number_evidence(header: str) -> str:
    """Find FACTURA followed by a recognizable fiscal document number."""
    for line in nonempty_lines(header):
        if FACTURA_NUMBER_LINE_RE.search(deaccent_upper(line)):
            return line
    return ""


def propose_fusion(d1, manifest_row, header: str, native: str):
    """Return decision, reason, evidence and anchors; blank decision = abstain.

    This function does not inspect visual-reference labels and is deliberately
    conservative. It is safe to call on all non-sealed rows for diagnostics.
    """
    confidence = parse_float(manifest_row.get("HeaderOcrConfidence"))
    anchors = fiscal_anchors(header, native)

    # Specific fiscal code 03 is accepted only with documentary/fiscal support.
    code03 = code_03_evidence(header)
    if code03 and len(anchors) >= 2:
        return (
            "OTRO_DOCUMENTO",
            "FISCAL_CODE_03_WITH_STRUCTURE",
            code03,
            anchors,
        )

    # Positive FACTURA fusion must not turn a printed email into an invoice.
    if email_like(header):
        return "", "", "", anchors

    # Do not compete with already-recognized NC/ND/RECIBO title evidence.
    if specific_non_factura_title_types(d1, header, confidence):
        return "", "", "", anchors

    title_line, has_nearby_number = credit_invoice_title_evidence(header)
    if title_line:
        title_compact = canonical(title_line)
        explicit_electronic = (
            "ELECTRONICA" in title_compact or "MIPYME" in canonical(header)
        )
        if has_nearby_number and (len(anchors) >= 2 or explicit_electronic):
            return (
                "FACTURA",
                "FISCAL_CREDIT_INVOICE_TITLE_WITH_STRUCTURE",
                title_line,
                anchors,
            )

    factura_line = structured_factura_number_evidence(header)
    if (
        factura_line
        and len(anchors) >= 4
        and "ORIGINAL" in anchors
        and ("ISSUE_DATE" in anchors or "START_DATE" in anchors)
    ):
        return (
            "FACTURA",
            "STRUCTURED_FACTURA_NUMBER_WITH_FISCAL_ANCHORS",
            factura_line,
            anchors,
        )

    return "", "", "", anchors


def load_inputs():
    for required in (
        BANK_PATH,
        TEXT_ZIP_PATH,
        D1C_BASELINE_PATH,
        D2_TOP150_PATH,
    ):
        if not required.exists():
            raise RuntimeError(f"Missing required artifact: {required}")

    d1 = load_d1()
    bank = read_csv(BANK_PATH)
    baseline = read_csv(D1C_BASELINE_PATH)
    top150 = read_csv(D2_TOP150_PATH)

    if len(baseline) != 734:
        raise RuntimeError("D1c independent baseline must contain 734 rows")
    if len(top150) != 150:
        raise RuntimeError("D2 Top150 baseline must contain 150 rows")

    bank_by_sha = {row["Sha256"]: row for row in bank}
    if len(bank_by_sha) != len(bank):
        raise RuntimeError("bank-manifest SHA must be unique")

    # Gate before any text is opened.
    required_shas = {row["Sha256"] for row in baseline} | {
        row["Sha256"] for row in top150
    }
    missing = [sha for sha in required_shas if sha not in bank_by_sha]
    if missing:
        raise RuntimeError(f"Traceability missing for {len(missing)} SHA")
    sealed = [
        sha for sha in required_shas
        if bank_by_sha[sha].get("Split") == "SEALED_TEST"
    ]
    if sealed:
        raise RuntimeError(f"D3 input unexpectedly contains SEALED_TEST: {sealed}")

    return d1, bank_by_sha, baseline, top150


def run_independent_regression(d1, bank_by_sha, baseline, text_source):
    output = []
    signal_audit = []

    for previous in baseline:
        manifest = bank_by_sha[previous["Sha256"]]
        header = text_source.read(manifest, "HeaderTextAsset")
        native = text_source.read(manifest, "NativeTextAsset")

        fusion_decision, fusion_reason, fusion_evidence, anchors = propose_fusion(
            d1, manifest, header, native
        )

        # Audit the signal over all 734 independent rows, even if D1c already
        # decided the row. This measures whether the new evidence pattern itself
        # disagrees with known ground truth.
        if fusion_decision:
            signal_audit.append({
                "Sha256": previous["Sha256"],
                "FileName": previous.get("FileName", ""),
                "Expected": previous["Expected"],
                "FusionSignalDecision": fusion_decision,
                "FusionSignalReason": fusion_reason,
                "FusionSignalCorrect": str(fusion_decision == previous["Expected"]),
                "FusionEvidence": fusion_evidence,
                "FiscalAnchors": "|".join(anchors),
            })

        decision = previous["ResolvedDecision"]
        reason = previous["ResolutionReason"]
        promoted = False

        # Incremental contract: never change a D1c decision. Fusion may only
        # reduce abstentions.
        if not decision and fusion_decision:
            decision = fusion_decision
            reason = fusion_reason
            promoted = True

        correct = (decision == previous["Expected"]) if decision else None
        output.append({
            "Sha256": previous["Sha256"],
            "FileName": previous.get("FileName", ""),
            "Expected": previous["Expected"],
            "LabelSource": previous.get("LabelSource", ""),
            "D1cDecision": previous["ResolvedDecision"],
            "D1cResolutionReason": previous["ResolutionReason"],
            "FusionSignalDecision": fusion_decision,
            "FusionSignalReason": fusion_reason,
            "FusionEvidence": fusion_evidence,
            "FiscalAnchors": "|".join(anchors),
            "D3Promoted": str(promoted),
            "D3Decision": decision,
            "D3ResolutionReason": reason,
            "CorrectWhenDecided": "" if correct is None else str(correct),
        })

    # Hard invariant: every row already decided by D1c is bitwise preserved at
    # the decision/reason level.
    changed_existing = [
        row for row in output
        if row["D1cDecision"] and (
            row["D3Decision"] != row["D1cDecision"]
            or row["D3ResolutionReason"] != row["D1cResolutionReason"]
        )
    ]
    if changed_existing:
        raise RuntimeError("D3 changed an existing D1c decision")

    return output, signal_audit


def run_top150(d1, bank_by_sha, baseline, text_source):
    output = []
    promotions = []

    for previous in baseline:
        manifest = bank_by_sha[previous["Sha256"]]
        header = text_source.read(manifest, "HeaderTextAsset")
        native = text_source.read(manifest, "NativeTextAsset")

        fusion_decision, fusion_reason, fusion_evidence, anchors = propose_fusion(
            d1, manifest, header, native
        )

        decision = previous.get("DeterministicDecision", "")
        reason = previous.get("ResolutionReason", "")
        promoted = False
        if not decision and fusion_decision:
            decision = fusion_decision
            reason = fusion_reason
            promoted = True

        residual = not bool(decision)
        visual_binary = previous.get("VisualReferenceLabel", "")
        # D2 VisualReferenceLabel is binary; use it only after decision for
        # descriptive agreement. It never enters propose_fusion().
        agreement = ""
        if promoted and visual_binary:
            agreement = str(decision == visual_binary)

        row = dict(previous)
        row.update({
            "D2Decision": previous.get("DeterministicDecision", ""),
            "D2ResolutionReason": previous.get("ResolutionReason", ""),
            "FusionSignalDecision": fusion_decision,
            "FusionSignalReason": fusion_reason,
            "FusionEvidence": fusion_evidence,
            "FiscalAnchors": "|".join(anchors),
            "D3Promoted": str(promoted),
            "D3Decision": decision,
            "D3ResolutionReason": reason,
            "D3Residual": str(residual),
            "D3PromotionVisualReferenceAgreement": agreement,
        })
        output.append(row)

        if promoted:
            promotions.append(row)

    # Hard invariant: D2 resolved decisions are never changed.
    changed_existing = [
        row for row in output
        if row["D2Decision"] and (
            row["D3Decision"] != row["D2Decision"]
            or row["D3ResolutionReason"] != row["D2ResolutionReason"]
        )
    ]
    if changed_existing:
        raise RuntimeError("D3 changed an existing D2 decision")

    return output, promotions


def main():
    d1, bank_by_sha, independent_baseline, top150_baseline = load_inputs()
    text_source = TextSource(TEXT_ZIP_PATH)
    try:
        independent, signal_audit = run_independent_regression(
            d1, bank_by_sha, independent_baseline, text_source
        )
        top150, promotions = run_top150(
            d1, bank_by_sha, top150_baseline, text_source
        )
    finally:
        text_source.close()

    ind_decided = [row for row in independent if row["D3Decision"]]
    ind_correct = [row for row in ind_decided if row["CorrectWhenDecided"] == "True"]
    ind_wrong = [row for row in ind_decided if row["CorrectWhenDecided"] == "False"]
    ind_no_decision = [row for row in independent if not row["D3Decision"]]
    ind_promoted = [row for row in independent if row["D3Promoted"] == "True"]

    signal_correct = [
        row for row in signal_audit if row["FusionSignalCorrect"] == "True"
    ]
    signal_wrong = [
        row for row in signal_audit if row["FusionSignalCorrect"] == "False"
    ]
    signal_by_reason = Counter(row["FusionSignalReason"] for row in signal_audit)

    top_resolved = [row for row in top150 if row["D3Decision"]]
    top_residual = [row for row in top150 if not row["D3Decision"]]
    promotion_reasons = Counter(row["D3ResolutionReason"] for row in promotions)
    promoted_visual_agree = [
        row for row in promotions
        if row["D3PromotionVisualReferenceAgreement"] == "True"
    ]
    promoted_visual_disagree = [
        row for row in promotions
        if row["D3PromotionVisualReferenceAgreement"] == "False"
    ]

    old_gap = [
        row for row in top150_baseline
        if row.get("ResidualDiagnosticGroup") == "KNOWN_FISCAL_OR_RECEIPT_EVIDENCE_GAP"
        and not row.get("DeterministicDecision")
    ]
    remaining_gap = [
        row for row in top_residual
        if row.get("ResidualDiagnosticGroup") == "KNOWN_FISCAL_OR_RECEIPT_EVIDENCE_GAP"
    ]

    remaining_gap_candidates = [
        {
            "CandidateId": row.get("CandidateId", ""),
            "Sha256": row.get("Sha256", ""),
            "FileName": row.get("FileName", ""),
            "FamilyId": row.get("FamilyId", ""),
            "VisualReferenceType": row.get("VisualReferenceType", ""),
            "VisualReferenceObservation": row.get("VisualReferenceObservation", ""),
            "D2ResolutionReason": row.get("D2ResolutionReason", ""),
            "D2GateReason": row.get("GateReason", ""),
            "D3Decision": row.get("D3Decision", ""),
            "D3ResolutionReason": row.get("D3ResolutionReason", ""),
            "FiscalAnchors": row.get("FiscalAnchors", ""),
            "Interpretation": (
                "Sigue sin evidencia determinística general suficiente. "
                "No promover usando la referencia visual; requiere mejor percepción, "
                "política documental o capa semántica según el caso."
            ),
        }
        for row in remaining_gap
    ]

    summary = {
        "Experiment": "H1D10D3",
        "Purpose": "High-precision fiscal/structural evidence fusion before semantic layer",
        "SealedAssetsRead": False,
        "TrainingExecuted": False,
        "ProductionModified": False,
        "GroundTruthModified": False,
        "ModelsPromoted": False,
        "ModelInferenceExecuted": False,
        "VisualReferenceIsGroundTruth": False,
        "IndependentRows": len(independent),
        "IndependentD1cDecisionsBefore": sum(bool(r["D1cDecision"]) for r in independent),
        "IndependentD3Decisions": len(ind_decided),
        "IndependentCorrectWhenDecided": len(ind_correct),
        "IndependentWrongWhenDecided": len(ind_wrong),
        "IndependentPrecisionWhenDecided": (
            len(ind_correct) / len(ind_decided) if ind_decided else None
        ),
        "IndependentNoDecision": len(ind_no_decision),
        "IndependentNewPromotions": len(ind_promoted),
        "IndependentFusionSignalRows": len(signal_audit),
        "IndependentFusionSignalCorrect": len(signal_correct),
        "IndependentFusionSignalWrong": len(signal_wrong),
        "IndependentFusionSignalPrecision": (
            len(signal_correct) / len(signal_audit) if signal_audit else None
        ),
        "IndependentFusionSignalsByReason": dict(signal_by_reason),
        "Top150Rows": len(top150),
        "Top150D2ResolvedBefore": sum(bool(r.get("D2Decision")) for r in top150),
        "Top150D3Resolved": len(top_resolved),
        "Top150D3Residual": len(top_residual),
        "Top150NewPromotions": len(promotions),
        "Top150PromotionReasons": dict(promotion_reasons),
        "Top150PromotionVisualReferenceAgreement": len(promoted_visual_agree),
        "Top150PromotionVisualReferenceDisagreement": len(promoted_visual_disagree),
        "KnownFiscalGapBefore": len(old_gap),
        "KnownFiscalGapAfter": len(remaining_gap),
        "KnownFiscalGapReducedBy": len(old_gap) - len(remaining_gap),
        "ImportantInterpretation": (
            "Top150 visual-reference agreement is diagnostic only. The safety/accuracy gate "
            "comes from the independent 734-row ground-truth regression."
        ),
    }

    # Required hard gates for accepting this diagnostic result.
    if summary["IndependentWrongWhenDecided"] != 0:
        raise RuntimeError("H1D10D3 gate failed: independent regression introduced errors")
    if summary["IndependentFusionSignalWrong"] != 0:
        raise RuntimeError("H1D10D3 gate failed: fusion signal conflicts with independent ground truth")
    if summary["IndependentD1cDecisionsBefore"] != 727:
        raise RuntimeError("H1D10D3 gate failed: unexpected D1c baseline")

    write_csv(HERE / "independent-regression-d3.csv", independent)
    write_csv(HERE / "independent-fusion-signal-audit-d3.csv", signal_audit)
    write_csv(HERE / "top150-fused-d3.csv", top150)
    write_csv(HERE / "promotions-d3.csv", promotions)
    write_csv(HERE / "residual-d3.csv", top_residual)
    write_csv(HERE / "remaining-fiscal-gap-d3.csv", remaining_gap_candidates)
    (HERE / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    result = f"""# H1D10D3 — fusión de evidencia fiscal/estructural

Experimento diagnóstico aislado posterior a H1D10D2.

No usa la revisión visual de ChatGPT para decidir. La fusión se apoya únicamente en texto/OCR no sellado y en señales fiscales/estructurales generales.

## Regresión independiente — 734 filas

Baseline H1D10D1c:

- decisiones: **{summary['IndependentD1cDecisionsBefore']}**
- incorrectas: **0**

Después de D3:

- decisiones: **{summary['IndependentD3Decisions']}**
- correctas al decidir: **{summary['IndependentCorrectWhenDecided']}**
- incorrectas al decidir: **{summary['IndependentWrongWhenDecided']}**
- precisión al decidir: **{summary['IndependentPrecisionWhenDecided']:.6f}**
- NO_DECISION: **{summary['IndependentNoDecision']}**
- nuevas promociones sobre abstenciones D1c: **{summary['IndependentNewPromotions']}**

Además, la señal de fusión se auditó sobre las 734 filas, incluso cuando D1c ya había decidido:

- filas donde la señal D3 se activó: **{summary['IndependentFusionSignalRows']}**
- compatibles con ground truth: **{summary['IndependentFusionSignalCorrect']}**
- incompatibles con ground truth: **{summary['IndependentFusionSignalWrong']}**
- precisión observada de la señal: **{summary['IndependentFusionSignalPrecision']:.6f}**

Por motivo:

- `FISCAL_CODE_03_WITH_STRUCTURE`: **{signal_by_reason.get('FISCAL_CODE_03_WITH_STRUCTURE', 0)}**
- `FISCAL_CREDIT_INVOICE_TITLE_WITH_STRUCTURE`: **{signal_by_reason.get('FISCAL_CREDIT_INVOICE_TITLE_WITH_STRUCTURE', 0)}**
- `STRUCTURED_FACTURA_NUMBER_WITH_FISCAL_ANCHORS`: **{signal_by_reason.get('STRUCTURED_FACTURA_NUMBER_WITH_FISCAL_ANCHORS', 0)}**

## Top150

Antes, D2 resolvía **{summary['Top150D2ResolvedBefore']} / 150**.

Después de la fusión D3:

- resueltos: **{summary['Top150D3Resolved']} / 150**
- nuevas promociones: **{summary['Top150NewPromotions']}**
- residual: **{summary['Top150D3Residual']}**
- brecha fiscal/recibo conocida: **{summary['KnownFiscalGapBefore']} → {summary['KnownFiscalGapAfter']}**

Promociones por motivo:

- código fiscal 03 + estructura: **{promotion_reasons.get('FISCAL_CODE_03_WITH_STRUCTURE', 0)}**
- FACTURA DE CREDITO + estructura/numeración: **{promotion_reasons.get('FISCAL_CREDIT_INVOICE_TITLE_WITH_STRUCTURE', 0)}**
- FACTURA + número + múltiples anclas fiscales: **{promotion_reasons.get('STRUCTURED_FACTURA_NUMBER_WITH_FISCAL_ANCHORS', 0)}**

La comparación posterior con la referencia visual da **{len(promoted_visual_agree)} coincidencias y {len(promoted_visual_disagree)} desacuerdos**, pero esto **no es accuracy** porque esa referencia no es ground truth.

## Qué NO se forzó

D3 deja abstenciones donde la evidencia general todavía no alcanza. En particular no convierte automáticamente una `Liquidación de Servicios Públicos (LSP)` en FACTURA ni confía en una etiqueta visual de RECIBO/FACTURA para suplir evidencia faltante.

Los casos restantes de la brecha fiscal/perceptiva se exportan en `remaining-fiscal-gap-d3.csv` para decidir si corresponden a:

1. mejora de percepción;
2. definición/política documental;
3. estructura específica generalizable;
4. capa semántica.

## Conclusión

La fusión fiscal/estructural reduce la brecha sin degradar la regresión independiente. No corresponde seguir agregando reglas para maximizar cobertura artificialmente: el residual que queda es más limpio para diseñar la futura capa semántica.

## Gates

- `SealedAssetsRead = false`
- `TrainingExecuted = false`
- `ProductionModified = false`
- `GroundTruthModified = false`
- `ModelsPromoted = false`
- `ModelInferenceExecuted = false`
"""
    (HERE / "resultado.md").write_text(result, encoding="utf-8")
    print(result)


if __name__ == "__main__":
    main()
