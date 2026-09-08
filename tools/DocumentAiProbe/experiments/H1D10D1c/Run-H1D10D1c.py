#!/usr/bin/env python3
"""H1D10D1c - gate posicional fuerte para FACTURA.

Diagnostico aislado. No entrena, no modifica ground truth ni produccion y
excluye SEALED_TEST antes de abrir cualquier asset de texto.
"""
from __future__ import annotations

import csv
import importlib.util
import json
import re
from collections import Counter
from pathlib import Path


HERE = Path(__file__).resolve().parent
EXPERIMENTS = HERE.parent
D1 = EXPERIMENTS / "H1D10D1"
D1B = EXPERIMENTS / "H1D10D1b"
H1D10A = EXPERIMENTS / "H1D10A"

BASELINE_PATH = D1B / "local-results" / "independent-resolved.csv"
MANIFEST_PATH = H1D10A / "bank-manifest.csv"

SECONDARY_MARKERS = (
    "ASUNTO",
    "PAGODEFACTURA",
    "REFERENCIAFACTURA",
    "FACTURAASOCIADA",
    "FECHAFACTURA",
    "SEGUNDAFACTURA",
    "GENERARFACTURA",
)

ALLOWED_PREFIXES = {"", "A", "B", "C", "E", "M", "ORIGINAL"}
DESCRIPTOR_TAILS = {
    "ELECTRONICA",
    "ELECTRONICAA",
    "ELECTRONICAB",
    "ELECTRONICAC",
    "ELECTRONICAM",
    "CREDITOELECTRONICA",
    "CREDITOELECTRONICAA",
    "CREDITOELECTRONICAB",
    "CREDITOELECTRONICAC",
    "DECREDITOELECTRONICA",
    "DECREDITOELECTRONICAA",
    "MIPYMES",
    "MIPYMESA",
}


def load_d1():
    path = D1 / "Run-H1D10D1.py"
    spec = importlib.util.spec_from_file_location("h1d10d1_for_d1c", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def read_csv(path: Path):
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def write_csv(path: Path, rows):
    if not rows:
        path.write_text("", encoding="utf-8")
        return
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def parse_float(value, default=0.0):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def _same_line_identifier(tail: str) -> bool:
    return bool(re.match(
        r"^(?:[ABCEM])?(?:NRO|NUMERO|N[A-Z]?|CODIGO|COD)?[0-9]{2,}",
        tail,
    ))


def _nearby_identifier(lines, candidate_index: int, canonical) -> tuple[bool, str]:
    nonempty = []
    for raw in lines[candidate_index + 1:]:
        compact = canonical(raw.strip())
        if compact:
            nonempty.append((raw.strip(), compact))
        if len(nonempty) == 2:
            break

    for original, compact in nonempty:
        if re.match(
            r"^(?:[ABCEM])?(?:N|NRO|NUMERO|COD|CODIGO)?[0-9]{2,}",
            compact,
        ):
            return True, original
        if re.match(r"^PUNTODEVENTA[0-9]+COMP(?:ROBANTE)?NRO[0-9]+", compact):
            return True, original
        if re.match(r"^NUMERO[ABCEM]?[0-9]+", compact):
            return True, original
    return False, ""


def strong_factura_gate(candidate, header: str, canonical) -> tuple[bool, str, str]:
    """Return accepted, reason and adjacent evidence for one FACTURA hit."""
    line = candidate["OriginalLine"].strip()
    compact = canonical(line)
    alias = candidate["Alias"]
    position = int(candidate["Position"])
    line_number = int(candidate["LineNumber"])
    prefix = compact[:position]
    tail = compact[position + len(alias):]

    if any(marker in compact for marker in SECONDARY_MARKERS):
        return False, "SECONDARY_MENTION_MARKER", ""

    lines = header.splitlines()
    adjacent, adjacent_line = _nearby_identifier(
        lines, line_number - 1, canonical
    )

    if prefix not in ALLOWED_PREFIXES:
        # A supplier/name followed by terminal FACTURA can behave as a title only
        # in the first two OCR lines and with an immediate fiscal identifier.
        if line_number <= 2 and not tail and adjacent:
            return True, "TOP_POSITION_ISSUER_TITLE_WITH_ADJACENT_ID", adjacent_line
        return False, "FACTURA_NOT_IN_TITLE_POSITION", ""

    if _same_line_identifier(tail):
        return True, "EXPLICIT_IDENTIFIER_SAME_LINE", ""

    if alias != "FACTURA" and tail in {"", "A", "B", "C", "E", "M"}:
        return True, "EXPLICIT_FISCAL_TITLE_ALIAS", ""

    if tail in DESCRIPTOR_TAILS:
        return True, "EXPLICIT_FISCAL_TITLE_DESCRIPTOR", ""

    if not tail and adjacent:
        return True, "TITLE_LINE_WITH_ADJACENT_ID", adjacent_line

    return False, "FACTURA_WITHOUT_STRONG_TITLE_STRUCTURE", ""


def safe_read_nonsealed(manifest_row) -> str:
    if manifest_row.get("Split") == "SEALED_TEST":
        raise RuntimeError("SEALED_TEST row reached asset reader")
    value = manifest_row.get("HeaderTextAsset", "")
    if not value:
        return ""
    if "SEALED_TEST" in value.upper():
        raise RuntimeError("SEALED_TEST path rejected")
    path = Path(value)
    if not path.exists():
        return ""
    return path.read_text(encoding="utf-8-sig", errors="replace")


def main():
    if not BASELINE_PATH.exists():
        raise RuntimeError(f"Missing D1b baseline: {BASELINE_PATH}")
    if not MANIFEST_PATH.exists():
        raise RuntimeError(f"Missing manifest: {MANIFEST_PATH}")

    d1 = load_d1()
    baseline = read_csv(BASELINE_PATH)

    # The manifest may describe the sealed split, but those rows are discarded
    # before any asset path is selected or opened.
    manifest_nonsealed = [
        row for row in read_csv(MANIFEST_PATH)
        if row.get("Split") != "SEALED_TEST"
    ]
    independent = [
        row for row in manifest_nonsealed
        if row.get("LabelSource") in ("QR_ARCA_STRONG", "EXISTING_GROUND_TRUTH")
        and row.get("LabelFinal") in ("FACTURA", "OTRO_DOCUMENTO")
    ]
    manifest_by_sha = {row["Sha256"]: row for row in independent}

    baseline_shas = {row["Sha256"] for row in baseline}
    manifest_shas = set(manifest_by_sha)
    if len(baseline) != 734 or baseline_shas != manifest_shas:
        raise RuntimeError(
            "D1b baseline and non-sealed independent manifest do not match"
        )

    output_rows = []
    inspection = []
    changed_non_title = []

    for previous in baseline:
        decision = previous["ResolvedDecision"]
        reason = previous["ResolutionReason"]
        gate_applied = reason == "FACTURA_TITLE_ONLY"
        accepted = ""
        evidence_line = ""
        adjacent_line = ""
        gate_reason = "NOT_APPLICABLE"

        if gate_applied:
            manifest_row = manifest_by_sha[previous["Sha256"]]
            header = safe_read_nonsealed(manifest_row)
            confidence = parse_float(manifest_row.get("HeaderOcrConfidence"))
            candidates = d1.detect_title_candidates(header, confidence)
            factura = next(
                (item for item in candidates if item["DocumentType"] == "FACTURA"),
                None,
            )
            if factura is None:
                raise RuntimeError(
                    f"D1b FACTURA_TITLE_ONLY evidence missing for {previous['Sha256']}"
                )

            keep, gate_reason, adjacent_line = strong_factura_gate(
                factura, header, d1.canonical
            )
            accepted = str(keep)
            evidence_line = factura["OriginalLine"]
            if keep:
                decision = "FACTURA"
                reason = "FACTURA_TITLE_ONLY_STRONG_POSITIONAL"
            else:
                decision = ""
                reason = "NO_DECISION_SECONDARY_FACTURA"

            inspection.append({
                "Sha256": previous["Sha256"],
                "FileName": previous["FileName"],
                "Expected": previous["Expected"],
                "PreviousDecision": previous["ResolvedDecision"],
                "PreviousCorrectWhenDecided": previous["CorrectWhenDecided"],
                "EvidenceLineNumber": factura["LineNumber"],
                "EvidenceLine": evidence_line,
                "AdjacentIdentifierLine": adjacent_line,
                "StrongTitleAccepted": accepted,
                "GateReason": gate_reason,
                "D1cDecision": decision,
                "D1cResolutionReason": reason,
            })

        correct = (decision == previous["Expected"]) if decision else None
        row = {
            "Sha256": previous["Sha256"],
            "FileName": previous["FileName"],
            "Expected": previous["Expected"],
            "LabelSource": previous["LabelSource"],
            "TitleTypes": previous["TitleTypes"],
            "QrLabels": previous["QrLabels"],
            "PreviousDecision": previous["ResolvedDecision"],
            "PreviousResolutionReason": previous["ResolutionReason"],
            "ResolvedDecision": decision,
            "ResolutionReason": reason,
            "CorrectWhenDecided": "" if correct is None else str(correct),
            "D1cGateApplied": str(gate_applied),
            "StrongFacturaTitle": accepted,
            "StrongEvidenceLine": evidence_line,
            "GateReason": gate_reason,
        }
        output_rows.append(row)

        if not gate_applied and (
            decision != previous["ResolvedDecision"]
            or reason != previous["ResolutionReason"]
        ):
            changed_non_title.append(previous["Sha256"])

    if changed_non_title:
        raise RuntimeError(f"D1c changed non-title decisions: {changed_non_title}")

    decided = [row for row in output_rows if row["ResolvedDecision"]]
    correct = [row for row in decided if row["CorrectWhenDecided"] == "True"]
    wrong = [row for row in decided if row["CorrectWhenDecided"] == "False"]
    no_decision = [row for row in output_rows if not row["ResolvedDecision"]]

    before_true = [
        row for row in inspection if row["PreviousCorrectWhenDecided"] == "True"
    ]
    before_false = [
        row for row in inspection if row["PreviousCorrectWhenDecided"] == "False"
    ]
    true_retained = [
        row for row in before_true if row["StrongTitleAccepted"] == "True"
    ]
    false_retained = [
        row for row in before_false if row["StrongTitleAccepted"] == "True"
    ]
    converted = [
        row for row in inspection if row["StrongTitleAccepted"] == "False"
    ]

    qr_rows = [row for row in baseline if row["ResolutionReason"].startswith("QR_ARCA_VALID_")]
    qr_unchanged = all(
        output_rows[index]["ResolvedDecision"] == baseline[index]["ResolvedDecision"]
        and output_rows[index]["ResolutionReason"] == baseline[index]["ResolutionReason"]
        for index in range(len(baseline))
        if baseline[index]["ResolutionReason"].startswith("QR_ARCA_VALID_")
    )
    precedence_unchanged = all(
        output_rows[index]["ResolvedDecision"] == baseline[index]["ResolvedDecision"]
        and output_rows[index]["ResolutionReason"] == baseline[index]["ResolutionReason"]
        for index in range(len(baseline))
        if baseline[index]["ResolutionReason"] == "SPECIFIC_TITLE_PRECEDENCE"
    )

    summary = {
        "Experiment": "H1D10D1c",
        "TrainingExecuted": False,
        "ProductionModified": False,
        "GroundTruthModified": False,
        "SealedAssetsRead": False,
        "Rows": len(output_rows),
        "Decisions": len(decided),
        "CorrectWhenDecided": len(correct),
        "WrongWhenDecided": len(wrong),
        "PrecisionWhenDecided": len(correct) / len(decided) if decided else None,
        "NoDecision": len(no_decision),
        "FacturaTitleOnlyBefore": len(inspection),
        "FacturaTitleOnlyTrueBefore": len(before_true),
        "FacturaTitleOnlyFalseBefore": len(before_false),
        "TrueFacturaTitleOnlyRetained": len(true_retained),
        "FalseFacturaTitleOnlyRetained": len(false_retained),
        "FacturaTitleOnlyConvertedToNoDecision": len(converted),
        "QrDecisionCount": len(qr_rows),
        "QrDecisionsUnchanged": qr_unchanged,
        "SpecificPrecedenceDecisionsUnchanged": precedence_unchanged,
        "ResolutionReasons": dict(Counter(row["ResolutionReason"] for row in output_rows)),
        "RemainingWrongCases": [
            {
                "Sha256": row["Sha256"],
                "Expected": row["Expected"],
                "Decision": row["ResolvedDecision"],
                "EvidenceLine": row["StrongEvidenceLine"],
            }
            for row in wrong
        ],
    }

    write_csv(HERE / "factura-title-only-inspection.csv", inspection)
    write_csv(HERE / "independent-resolved-d1c.csv", output_rows)
    (HERE / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

    result = f"""# H1D10D1c — gate posicional fuerte para FACTURA

Diagnóstico aislado. No entrena, no modifica producción ni ground truth y no lee assets de `SEALED_TEST`.

## Resultado independiente

- Filas totales: **{len(output_rows)}**
- Decisiones: **{len(decided)}**
- Correctas: **{len(correct)}**
- Incorrectas: **{len(wrong)}**
- Precisión cuando decide: **{summary['PrecisionWhenDecided']}**
- NO_DECISION: **{len(no_decision)}**

## Gate FACTURA_TITLE_ONLY

- FACTURA_TITLE_ONLY antes: **{len(inspection)}**
- Verdaderos antes: **{len(before_true)}**
- Verdaderos retenidos: **{len(true_retained)}**
- Falsos antes: **{len(before_false)}**
- Falsos retenidos: **{len(false_retained)}**
- Convertidos a NO_DECISION: **{len(converted)}**

El gate exige estructura real de título: identificador en la misma línea, descriptor fiscal explícito o una línea de título seguida inmediatamente por numeración fiscal. Las menciones secundarias se resuelven como `NO_DECISION`, no como `OTRO_DOCUMENTO`.

## Invariantes

- Decisiones QR verificadas sin cambios: **{qr_unchanged}** ({len(qr_rows)} filas)
- Precedencia NC/ND/RECIBO verificada sin cambios: **{precedence_unchanged}**
- Entrenamiento ejecutado: **False**
- Producción modificada: **False**
- Ground truth modificado: **False**
- Assets SEALED_TEST leídos: **False**
"""
    (HERE / "resultado.md").write_text(result, encoding="utf-8")
    print(result)

    if false_retained:
        for row in false_retained:
            print(
                "FALSE_FACTURA_RETAINED: "
                f"{row['Sha256']} | {row['EvidenceLine']}"
            )
        raise SystemExit(2)
    if wrong:
        for row in wrong:
            print(
                "WRONG_DECISION_REMAINING: "
                f"{row['Sha256']} | {row['StrongEvidenceLine']}"
            )
        raise SystemExit(3)


if __name__ == "__main__":
    main()
