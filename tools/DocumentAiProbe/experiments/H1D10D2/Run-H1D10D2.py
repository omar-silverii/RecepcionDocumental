#!/usr/bin/env python3
"""H1D10D2 - residual real después del resolver documental.

DIAGNÓSTICO SOLAMENTE.

Aplica al Top150 exactamente las piezas ya desarrolladas en H1D10D1/D1b/D1c:
- títulos documentales canónicos;
- precedencia QR / NC / ND / RECIBO;
- gate posicional fuerte para FACTURA.

La revisión visual de ChatGPT se usa únicamente para describir el residual y
comparar diagnósticamente; nunca decide el label.
"""
from __future__ import annotations

import csv
import importlib.util
import json
import os
import zipfile
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXPERIMENTS = HERE.parent
H1D10A = EXPERIMENTS / "H1D10A"
H1D10B = EXPERIMENTS / "H1D10B"
D1 = EXPERIMENTS / "H1D10D1"
D1B = EXPERIMENTS / "H1D10D1b"
D1C = EXPERIMENTS / "H1D10D1c"

BANK_PATH = H1D10A / "bank-manifest.csv"
TOP150_PATH = H1D10B / "review-top150.csv"
VISUAL_REFERENCE_PATH = H1D10B / "ReviewTop150-evaluacion-visual-ciega-ChatGPT.csv"
TEXT_ZIP_PATH = H1D10B / "text.zip"


def load_module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def load_dependencies():
    d1 = load_module(D1 / "Run-H1D10D1.py", "h1d10d1_for_d2")
    d1b = load_module(D1B / "Run-H1D10D1b.py", "h1d10d1b_for_d2")
    d1c = load_module(D1C / "Run-H1D10D1c.py", "h1d10d1c_for_d2")
    return d1, d1b, d1c


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


def asset_basename(path_value: str) -> str:
    return os.path.basename((path_value or "").replace("\\", "/"))


class TextSource:
    """Read only non-sealed Top150 text, preferring the portable text.zip."""

    def __init__(self, text_zip_path: Path):
        self._zip = zipfile.ZipFile(text_zip_path) if text_zip_path.exists() else None
        self._names = set(self._zip.namelist()) if self._zip else set()
        self.sealed_assets_read = False

    def close(self):
        if self._zip:
            self._zip.close()

    def _assert_nonsealed(self, manifest_row):
        if manifest_row.get("Split") == "SEALED_TEST":
            raise RuntimeError("SEALED_TEST row reached D2 text reader")

    def _read_portable(self, manifest_row, field_name: str) -> str:
        self._assert_nonsealed(manifest_row)
        base = asset_basename(manifest_row.get(field_name, ""))
        if not base or not self._zip:
            return ""
        member = f"text/{base}"
        if member not in self._names:
            return ""
        return self._zip.read(member).decode("utf-8-sig", errors="replace")

    def _read_path(self, manifest_row, field_name: str) -> str:
        self._assert_nonsealed(manifest_row)
        value = manifest_row.get(field_name, "")
        if not value:
            return ""
        if "SEALED_TEST" in value.upper():
            raise RuntimeError("SEALED_TEST path rejected by D2 text reader")
        path = Path(value)
        if not path.exists():
            return ""
        return path.read_text(encoding="utf-8-sig", errors="replace")

    def read(self, manifest_row, field_name: str) -> tuple[str, str]:
        text = self._read_portable(manifest_row, field_name)
        if text:
            return text, "H1D10B_TEXT_ZIP"
        text = self._read_path(manifest_row, field_name)
        if text:
            return text, "H1D10A_LOCAL_ASSET"
        return "", "MISSING"


def resolve_current(d1, d1b, d1c, manifest_row, header: str):
    confidence = parse_float(manifest_row.get("HeaderOcrConfidence"))
    title_hits = d1.detect_title_candidates(header, confidence)
    title_types = [item["DocumentType"] for item in title_hits]
    qr_labels = d1b.valid_qr_labels(manifest_row)

    preliminary, preliminary_reason = d1b.resolve_binary(title_types, qr_labels)
    evidence = []
    gate_reason = "NOT_APPLICABLE"

    if preliminary_reason.startswith("QR_ARCA_VALID_"):
        evidence.append("QR=" + "|".join(qr_labels))
        return preliminary, preliminary_reason, title_hits, qr_labels, evidence, gate_reason

    if preliminary_reason == "SPECIFIC_TITLE_PRECEDENCE":
        specifics = [
            item for item in title_hits
            if item["DocumentType"] in {"NOTA_CREDITO", "NOTA_DEBITO", "RECIBO"}
        ]
        evidence.extend(item["OriginalLine"] for item in specifics)
        return preliminary, preliminary_reason, title_hits, qr_labels, evidence, gate_reason

    if preliminary_reason == "FACTURA_TITLE_ONLY":
        factura = next(
            (item for item in title_hits if item["DocumentType"] == "FACTURA"),
            None,
        )
        if factura is None:
            raise RuntimeError("FACTURA_TITLE_ONLY without FACTURA candidate")
        accepted, gate_reason, adjacent = d1c.strong_factura_gate(
            factura, header, d1.canonical
        )
        evidence.append(factura["OriginalLine"])
        if adjacent:
            evidence.append(adjacent)
        if accepted:
            return (
                "FACTURA",
                "FACTURA_TITLE_ONLY_STRONG_POSITIONAL",
                title_hits,
                qr_labels,
                evidence,
                gate_reason,
            )
        return (
            "",
            "NO_DECISION_SECONDARY_FACTURA",
            title_hits,
            qr_labels,
            evidence,
            gate_reason,
        )

    return "", "NO_DECISION", title_hits, qr_labels, evidence, gate_reason


def diagnostic_residual_group(visual_type: str, observation: str) -> tuple[str, str, str]:
    """Diagnostic grouping only. It never feeds the deterministic resolver."""
    visual_type = visual_type or ""
    observation_upper = (observation or "").upper()

    if visual_type == "TRANSFERENCIA_ANTICIPO":
        return (
            "SEMANTIC_TRANSFERENCIA_ANTICIPO",
            "LIKELY_SEMANTIC_LANGUAGE_CONTEXT",
            "Comprender intención/contexto de transferencia o anticipo; la mención textual no es un título documental fiscal.",
        )
    if visual_type == "OTRO_DOCUMENTO" and observation_upper.startswith("CORREO"):
        return (
            "SEMANTIC_CORREO_ADMINISTRATIVO",
            "LIKELY_SEMANTIC_LANGUAGE_CONTEXT",
            "Distinguir la intención administrativa del correo/gestión de menciones secundarias a facturas, pagos o notas de crédito.",
        )
    if visual_type in {"FACTURA", "NOTA_CREDITO", "NOTA_DEBITO", "RECIBO"}:
        return (
            "KNOWN_FISCAL_OR_RECEIPT_EVIDENCE_GAP",
            "DETERMINISTIC_EVIDENCE_GAP_FIRST",
            "Antes de semántica, fusionar mejor título OCR, estructura fiscal y texto principal; el gate actual no tiene evidencia fuerte suficiente.",
        )
    if visual_type == "COMPROBANTE_BANCARIO":
        return (
            "STRUCTURED_BANK_DOCUMENT",
            "LIKELY_SEMANTIC_OR_STRUCTURE",
            "Reconocer estructura/intención de depósito o comprobante bancario sin depender de un título fiscal conocido.",
        )
    if visual_type == "IMPUESTO_BOLETA":
        return (
            "STRUCTURED_TAX_OR_PAYMENT_DOCUMENT",
            "LIKELY_SEMANTIC_OR_STRUCTURE",
            "Reconocer boleta/cupón de pago como documento no factura mediante semántica o estructura documental.",
        )
    return (
        "OTHER_ADMIN_OR_GENERIC_DOCUMENT",
        "LIKELY_SEMANTIC_OR_STRUCTURE",
        "Comprender el tipo/intención documental más allá del conjunto actual de títulos fiscales explícitos.",
    )


def binary_visual_reference(visual_type: str) -> str:
    if not visual_type:
        return ""
    return "FACTURA" if visual_type == "FACTURA" else "OTRO_DOCUMENTO"


def main():
    for required in (BANK_PATH, TOP150_PATH, VISUAL_REFERENCE_PATH):
        if not required.exists():
            raise RuntimeError(f"Missing required artifact: {required}")

    d1, d1b, d1c = load_dependencies()
    bank = read_csv(BANK_PATH)
    top150 = read_csv(TOP150_PATH)
    visual_rows = read_csv(VISUAL_REFERENCE_PATH)

    if len(top150) != 150 or len({row["Sha256"] for row in top150}) != 150:
        raise RuntimeError("Top150 must contain exactly 150 unique SHA rows")
    if len({row["CandidateId"] for row in top150}) != 150:
        raise RuntimeError("Top150 CandidateId must be unique")

    bank_by_sha = {row["Sha256"]: row for row in bank}
    visual_by_candidate = {row["CandidateId"]: row for row in visual_rows}

    missing_bank = [row["Sha256"] for row in top150 if row["Sha256"] not in bank_by_sha]
    missing_visual = [
        row["CandidateId"] for row in top150
        if row["CandidateId"] not in visual_by_candidate
    ]
    if missing_bank or missing_visual:
        raise RuntimeError(
            f"Traceability missing. bank={len(missing_bank)} visual={len(missing_visual)}"
        )

    # Gate before any text asset is opened.
    sealed_top150 = [
        row["Sha256"] for row in top150
        if bank_by_sha[row["Sha256"]].get("Split") == "SEALED_TEST"
    ]
    if sealed_top150:
        raise RuntimeError(f"Top150 unexpectedly contains SEALED_TEST: {sealed_top150}")

    text_source = TextSource(TEXT_ZIP_PATH)
    output_rows = []
    try:
        for top in top150:
            manifest = bank_by_sha[top["Sha256"]]
            visual = visual_by_candidate[top["CandidateId"]]
            header, header_source = text_source.read(manifest, "HeaderTextAsset")

            (
                decision,
                reason,
                title_hits,
                qr_labels,
                evidence,
                gate_reason,
            ) = resolve_current(d1, d1b, d1c, manifest, header)

            semantic_cues = d1.detect_semantic_cues(header)
            residual = not bool(decision)
            group = "RESOLVED_DETERMINISTIC"
            semantic_need = "NOT_APPLICABLE"
            evidence_gap = ""
            if residual:
                group, semantic_need, evidence_gap = diagnostic_residual_group(
                    visual.get("DocumentType", ""), visual.get("Observacion", "")
                )

            visual_binary = binary_visual_reference(visual.get("DocumentType", ""))
            agreement = ""
            if decision and visual_binary:
                agreement = str(decision == visual_binary)

            output_rows.append({
                "CandidateId": top["CandidateId"],
                "Sha256": top["Sha256"],
                "FileName": top.get("FileName", ""),
                "FamilyId": top.get("FamilyId", ""),
                "Split": manifest.get("Split", ""),
                "HeaderOcrConfidence": manifest.get("HeaderOcrConfidence", ""),
                "HeaderSource": header_source,
                "VisualReferenceType": visual.get("DocumentType", ""),
                "VisualReferenceLabel": visual.get("LabelFinal", ""),
                "VisualReferenceObservation": visual.get("Observacion", ""),
                "VisualReferenceIsGroundTruth": "False",
                "QrLabels": "|".join(qr_labels),
                "CanonicalTitleTypes": "|".join(item["DocumentType"] for item in title_hits),
                "CanonicalTitleEvidence": json.dumps(title_hits, ensure_ascii=False),
                "SemanticCueTypes": "|".join(item["CueType"] for item in semantic_cues),
                "DeterministicDecision": decision,
                "ResolutionReason": reason,
                "GateReason": gate_reason,
                "DecisionEvidence": " || ".join(evidence),
                "Residual": str(residual),
                "ResidualDiagnosticGroup": group,
                "SemanticNeed": semantic_need,
                "EvidenceGap": evidence_gap,
                "VisualReferenceBinaryAgreement": agreement,
            })
    finally:
        text_source.close()

    resolved = [row for row in output_rows if row["DeterministicDecision"]]
    residual = [row for row in output_rows if not row["DeterministicDecision"]]
    disagreements = [
        row for row in resolved
        if row["VisualReferenceBinaryAgreement"] == "False"
    ]

    resolved_factura = [row for row in resolved if row["DeterministicDecision"] == "FACTURA"]
    resolved_other = [row for row in resolved if row["DeterministicDecision"] == "OTRO_DOCUMENTO"]
    qr_resolved = [row for row in resolved if row["ResolutionReason"].startswith("QR_ARCA_VALID_")]
    visual_agreements = [
        row for row in resolved
        if row["VisualReferenceBinaryAgreement"] == "True"
    ]

    residual_groups = Counter(row["ResidualDiagnosticGroup"] for row in residual)
    semantic_need = Counter(row["SemanticNeed"] for row in residual)
    residual_visual = Counter(row["VisualReferenceType"] for row in residual)
    reasons = Counter(row["ResolutionReason"] for row in output_rows)

    summary = {
        "Experiment": "H1D10D2",
        "Purpose": "Residual real after D1/D1b/D1c deterministic resolver",
        "SealedAssetsRead": False,
        "TrainingExecuted": False,
        "ProductionModified": False,
        "GroundTruthModified": False,
        "ModelsPromoted": False,
        "ModelInferenceExecuted": False,
        "VisualReferenceIsGroundTruth": False,
        "Top150Rows": len(output_rows),
        "UniqueSha": len({row["Sha256"] for row in output_rows}),
        "UniqueFamilyId": len({row["FamilyId"] for row in output_rows}),
        "DeterministicResolved": len(resolved),
        "DeterministicResolvedRate": len(resolved) / len(output_rows),
        "ResolvedFactura": len(resolved_factura),
        "ResolvedOtroDocumento": len(resolved_other),
        "ResidualNoDecision": len(residual),
        "ResidualRate": len(residual) / len(output_rows),
        "QrResolved": len(qr_resolved),
        "ResolutionReasons": dict(reasons),
        "ResidualByVisualReferenceType": dict(residual_visual),
        "ResidualDiagnosticGroups": dict(residual_groups),
        "ResidualSemanticNeed": dict(semantic_need),
        "ResolvedVsVisualReferenceAgreement": len(visual_agreements),
        "ResolvedVsVisualReferenceDisagreement": len(disagreements),
        "ImportantInterpretation": (
            "Visual-reference agreement is diagnostic only; it is not an accuracy score "
            "because ChatGPT visual review is not human ground truth."
        ),
    }

    write_csv(HERE / "top150-resolved-d2.csv", output_rows)
    write_csv(HERE / "residual-d2.csv", residual)
    write_csv(HERE / "visual-reference-disagreements-d2.csv", disagreements)
    (HERE / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    result = f"""# H1D10D2 — residual real después del resolver documental

Diagnóstico aislado sobre los 150 hard cases seleccionados por H1D10B3.

La decisión determinística **no usa** la revisión visual de ChatGPT. Esa revisión se conserva sólo para describir el residual y no constituye ground truth.

## Resultado principal

- Top150: **{len(output_rows)}**
- SHA únicos: **{summary['UniqueSha']}**
- Familias: **{summary['UniqueFamilyId']}**
- Resueltos por D1 + D1b + D1c: **{len(resolved)}** ({summary['DeterministicResolvedRate']:.1%})
- FACTURA: **{len(resolved_factura)}**
- OTRO_DOCUMENTO: **{len(resolved_other)}**
- QR ARCA válidos que resolvieron casos del Top150: **{len(qr_resolved)}**
- Residual / NO_DECISION: **{len(residual)}** ({summary['ResidualRate']:.1%})

## Por qué resolvió

- `SPECIFIC_TITLE_PRECEDENCE`: **{reasons.get('SPECIFIC_TITLE_PRECEDENCE', 0)}**
- `FACTURA_TITLE_ONLY_STRONG_POSITIONAL`: **{reasons.get('FACTURA_TITLE_ONLY_STRONG_POSITIONAL', 0)}**
- `QR_ARCA_VALID_*`: **{len(qr_resolved)}**
- `NO_DECISION_SECONDARY_FACTURA`: **{reasons.get('NO_DECISION_SECONDARY_FACTURA', 0)}**
- `NO_DECISION`: **{reasons.get('NO_DECISION', 0)}**

## Residual diagnóstico

Según la referencia visual de ChatGPT —sólo diagnóstica, no ground truth—:

- transferencia/anticipo: **{residual_visual.get('TRANSFERENCIA_ANTICIPO', 0)}**
- correo/documento administrativo: **{residual_groups.get('SEMANTIC_CORREO_ADMINISTRATIVO', 0)}**
- documentos fiscales/recibos conocidos con evidencia insuficiente para el gate: **{residual_groups.get('KNOWN_FISCAL_OR_RECEIPT_EVIDENCE_GAP', 0)}**
- comprobante bancario: **{residual_visual.get('COMPROBANTE_BANCARIO', 0)}**
- impuesto/boleta: **{residual_visual.get('IMPUESTO_BOLETA', 0)}**
- otro administrativo/genérico: **{residual_groups.get('OTHER_ADMIN_OR_GENERIC_DOCUMENT', 0)}**

Necesidad diagnóstica:

- lenguaje/contexto claramente semántico: **{semantic_need.get('LIKELY_SEMANTIC_LANGUAGE_CONTEXT', 0)}**
- semántica o estructura documental: **{semantic_need.get('LIKELY_SEMANTIC_OR_STRUCTURE', 0)}**
- primero cerrar una brecha determinística de evidencia fiscal/percepción: **{semantic_need.get('DETERMINISTIC_EVIDENCE_GAP_FIRST', 0)}**

## Comparación con la revisión visual

Entre los **{len(resolved)}** casos decididos determinísticamente:

- coincidencias binarias con la referencia visual: **{len(visual_agreements)}**
- desacuerdos: **{len(disagreements)}**

Esto **no** se interpreta como accuracy: la referencia visual no es ground truth. Los desacuerdos se exportan para inspección separada.

## Conclusión

El resolver determinístico actual elimina una parte importante del hard set sin entrenamiento, pero el residual sigue siendo material. No corresponde ampliarlo con una lista indefinida de regex.

La siguiente decisión arquitectónica debe separar:

1. casos fiscales conocidos donde todavía falta fusionar mejor evidencia determinística/perceptiva;
2. lenguaje administrativo/transferencias/correos que sí requiere comprensión semántica;
3. otros documentos estructurados que pueden requerir semántica, estructura o ambos.

No se implementa todavía una nueva IA.

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
