#!/usr/bin/env python3
"""H1D10D4 - auditoría del residual y del fallback textual existente.

DIAGNÓSTICO SOLAMENTE.

No entrena, no hace inferencia nueva y no toca producción. Reutiliza únicamente:
- la regresión independiente ya calculada por H1D10D3;
- las predicciones OOF group-aware de H1D10B1/B3;
- el residual Top150 ya caracterizado por H1D10D3;
- los scores ya existentes usados para active learning.

Objetivo: determinar si TEXT_ONLY/HEADER_TEXT actuales pueden actuar de fallback
seguro sobre las abstenciones de D3, y medir la forma real del residual antes de
seleccionar una nueva tecnología semántica.
"""
from __future__ import annotations

import csv
import json
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXPERIMENTS = HERE.parent
H1D10B = EXPERIMENTS / "H1D10B"
H1D10D3 = EXPERIMENTS / "H1D10D3"

D3_INDEPENDENT = H1D10D3 / "independent-regression-d3.csv"
D3_RESIDUAL = H1D10D3 / "residual-d3.csv"
OOF_TEXT = H1D10B / "oof-predictions.csv"
OOF_HEADER_TEXT = H1D10B / "header-text-oof-predictions.csv"
TOP150_SCORES = H1D10B / "review-top150.csv"


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


def f(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return float("nan")


def percentile(values, q):
    values = sorted(v for v in values if v == v)
    if not values:
        return None
    if len(values) == 1:
        return values[0]
    pos = (len(values) - 1) * q
    lo = int(pos)
    hi = min(lo + 1, len(values) - 1)
    frac = pos - lo
    return values[lo] * (1 - frac) + values[hi] * frac


def main():
    required = [D3_INDEPENDENT, D3_RESIDUAL, OOF_TEXT, OOF_HEADER_TEXT, TOP150_SCORES]
    for path in required:
        if not path.exists():
            raise RuntimeError(f"Missing required artifact: {path}")

    independent = read_csv(D3_INDEPENDENT)
    residual_rows = read_csv(D3_RESIDUAL)
    oof_text = {r["Sha256"]: r for r in read_csv(OOF_TEXT)}
    oof_header = {r["Sha256"]: r for r in read_csv(OOF_HEADER_TEXT)}
    top_scores = {r["Sha256"]: r for r in read_csv(TOP150_SCORES)}

    if len(independent) != 734:
        raise RuntimeError(f"Expected 734 independent rows, got {len(independent)}")
    if len(residual_rows) != 86:
        raise RuntimeError(f"Expected 86 rows in D3 residual artifact, got {len(residual_rows)}")

    independent_audit = []
    abstentions = []
    naive_correct = 0
    naive_wrong = 0

    for row in independent:
        sha = row["Sha256"]
        if sha not in oof_text or sha not in oof_header:
            raise RuntimeError(f"Missing OOF prediction for independent SHA {sha}")

        text = oof_text[sha]
        header = oof_header[sha]
        text_p = f(text["WORD_PFactura"])
        header_p = f(header["WORD_PFactura"])
        d3_decision = (row.get("D3Decision") or "").strip()
        expected = row["Expected"]

        fallback_text = "FACTURA" if text_p >= 0.5 else "OTRO_DOCUMENTO"
        fallback_header = "FACTURA" if header_p >= 0.5 else "OTRO_DOCUMENTO"
        hybrid_naive = d3_decision or fallback_text
        hybrid_correct = hybrid_naive == expected
        naive_correct += int(hybrid_correct)
        naive_wrong += int(not hybrid_correct)

        audit = {
            "Sha256": sha,
            "FileName": row.get("FileName", ""),
            "Expected": expected,
            "D3Decision": d3_decision,
            "D3ResolutionReason": row.get("D3ResolutionReason", ""),
            "TextOnlyPFactura": f"{text_p:.9f}",
            "TextOnlyPrediction05": fallback_text,
            "HeaderTextPFactura": f"{header_p:.9f}",
            "HeaderTextPrediction05": fallback_header,
            "HybridD3PlusText05": hybrid_naive,
            "HybridCorrect": str(hybrid_correct),
            "WasD3Abstention": str(not bool(d3_decision)),
            "FamilyId": text.get("FamilyId", ""),
            "Fold": text.get("Fold", ""),
        }
        independent_audit.append(audit)
        if not d3_decision:
            abstentions.append(audit)

    # One-sided low-P(FACTURA) audit over the full independent cohort.
    threshold_rows = []
    for threshold in (0.25, 0.30, 0.35, 0.40, 0.45, 0.50):
        subset = [r for r in independent_audit if f(r["TextOnlyPFactura"]) <= threshold]
        correct_otro = sum(r["Expected"] == "OTRO_DOCUMENTO" for r in subset)
        wrong_factura = sum(r["Expected"] != "OTRO_DOCUMENTO" for r in subset)
        threshold_rows.append({
            "ThresholdMaxPFactura": f"{threshold:.2f}",
            "Rows": len(subset),
            "ExpectedOtro": correct_otro,
            "ExpectedFactura": wrong_factura,
            "ObservedOtroPrecision": f"{(correct_otro / len(subset)):.9f}" if subset else "",
        })

    # Top150 residual audit. Scores are diagnostic; visual reference is not GT.
    residual86 = list(residual_rows)
    if len(residual86) != 86:
        raise RuntimeError(f"Expected 86 D3 residual rows, got {len(residual86)}")

    top_audit = []
    family_groups = defaultdict(Counter)
    family_types = defaultdict(Counter)
    group_scores = defaultdict(list)
    semantic_need_scores = defaultdict(list)

    for row in residual86:
        sha = row["Sha256"]
        score = top_scores.get(sha)
        if score is None:
            raise RuntimeError(f"Missing Top150 score for residual SHA {sha}")
        text_p = f(score["TextScore"])
        header_p = f(score["HeaderTextScore"])
        sim_otro = f(score["SemanticSimilarityToOtro"])
        group = row.get("ResidualDiagnosticGroup", "")
        need = row.get("SemanticNeed", "")
        family = row.get("FamilyId", "")
        visual_type = row.get("VisualReferenceType", "")

        family_groups[family][group] += 1
        family_types[family][visual_type] += 1
        group_scores[group].append(text_p)
        semantic_need_scores[need].append(text_p)

        candidate_zone = text_p <= 0.35
        top_audit.append({
            "CandidateId": row.get("CandidateId", ""),
            "Sha256": sha,
            "FileName": row.get("FileName", ""),
            "FamilyId": family,
            "ResidualDiagnosticGroup": group,
            "SemanticNeed": need,
            "VisualReferenceType": visual_type,
            "VisualReferenceIsGroundTruth": "False",
            "TextOnlyPFactura": f"{text_p:.9f}",
            "HeaderTextPFactura": f"{header_p:.9f}",
            "SemanticSimilarityToOtro": f"{sim_otro:.9f}",
            "TextOnlyPrediction05": "FACTURA" if text_p >= 0.5 else "OTRO_DOCUMENTO",
            "HeaderTextPrediction05": "FACTURA" if header_p >= 0.5 else "OTRO_DOCUMENTO",
            "LowPFacturaCandidateZone035": str(candidate_zone),
            "Important": "Diagnostic only; no automatic label",
        })

    family_rows = []
    family_counts = Counter(r["FamilyId"] for r in residual86)
    for family, count in family_counts.most_common():
        family_rows.append({
            "FamilyId": family,
            "ResidualRows": count,
            "DiagnosticGroups": "|".join(f"{k}:{v}" for k, v in family_groups[family].most_common()),
            "VisualReferenceTypes": "|".join(f"{k}:{v}" for k, v in family_types[family].most_common()),
            "MixedDiagnosticGroups": str(len(family_groups[family]) > 1),
            "MixedVisualReferenceTypes": str(len(family_types[family]) > 1),
        })

    # Summary counts.
    group_counts = Counter(r["ResidualDiagnosticGroup"] for r in residual86)
    need_counts = Counter(r["SemanticNeed"] for r in residual86)
    language = [r for r in top_audit if r["SemanticNeed"] == "LIKELY_SEMANTIC_LANGUAGE_CONTEXT"]
    low035 = [r for r in top_audit if r["LowPFacturaCandidateZone035"] == "True"]

    top_family_counts = [count for _, count in family_counts.most_common()]
    top4 = sum(top_family_counts[:4])
    top10 = sum(top_family_counts[:10])

    abst_text_scores = [f(r["TextOnlyPFactura"]) for r in abstentions]
    abst_header_scores = [f(r["HeaderTextPFactura"]) for r in abstentions]

    summary = {
        "Experiment": "H1D10D4",
        "Purpose": "Audit existing text models as fallback and characterize semantic residual before selecting new technology",
        "SealedAssetsRead": False,
        "TrainingExecuted": False,
        "ProductionModified": False,
        "GroundTruthModified": False,
        "ModelsPromoted": False,
        "NewModelInferenceExecuted": False,
        "ExistingOOFPredictionsReused": True,
        "VisualReferenceIsGroundTruth": False,
        "IndependentRows": len(independent),
        "D3IndependentDecisions": sum(bool((r.get("D3Decision") or "").strip()) for r in independent),
        "D3IndependentAbstentions": len(abstentions),
        "D3AbstentionExpectedLabels": dict(Counter(r["Expected"] for r in abstentions)),
        "D3AbstentionTextOnlyPredictions05": dict(Counter(r["TextOnlyPrediction05"] for r in abstentions)),
        "D3AbstentionHeaderTextPredictions05": dict(Counter(r["HeaderTextPrediction05"] for r in abstentions)),
        "D3AbstentionTextOnlyPFacturaMin": min(abst_text_scores),
        "D3AbstentionTextOnlyPFacturaMax": max(abst_text_scores),
        "D3AbstentionHeaderTextPFacturaMin": min(abst_header_scores),
        "D3AbstentionHeaderTextPFacturaMax": max(abst_header_scores),
        "NaiveD3PlusText05Decisions": len(independent),
        "NaiveD3PlusText05Correct": naive_correct,
        "NaiveD3PlusText05Wrong": naive_wrong,
        "NaiveD3PlusText05Precision": naive_correct / len(independent),
        "ResidualTop150Rows": len(residual86),
        "ResidualTop150Families": len(family_counts),
        "ResidualDiagnosticGroups": dict(group_counts),
        "ResidualSemanticNeed": dict(need_counts),
        "LanguageContextRows": len(language),
        "LanguageContextFamilies": len({r["FamilyId"] for r in residual86 if r["SemanticNeed"] == "LIKELY_SEMANTIC_LANGUAGE_CONTEXT"}),
        "LanguageContextTextOnlyBelow05": sum(f(r["TextOnlyPFactura"]) < 0.5 for r in language),
        "LanguageContextTextOnlyAtOrBelow035": sum(f(r["TextOnlyPFactura"]) <= 0.35 for r in language),
        "LowPFacturaCandidateZone035Rows": len(low035),
        "LowPFacturaCandidateZone035Families": len({r["FamilyId"] for r in low035}),
        "Top4FamiliesRows": top4,
        "Top4FamiliesShare": top4 / len(residual86),
        "Top10FamiliesRows": top10,
        "Top10FamiliesShare": top10 / len(residual86),
        "Interpretation": (
            "TEXT_ONLY retains useful ranking signal, especially for language/context cases, "
            "but fails exactly on all six independent D3 hard-negative abstentions at threshold 0.5. "
            "Therefore it is not validated as an automatic fallback. Low-PFactura cohorts are useful "
            "for candidate selection only until a semantic model is evaluated family-aware with real ground truth."
        ),
    }

    write_csv(HERE / "independent-fallback-audit-d4.csv", independent_audit)
    write_csv(HERE / "independent-low-pfactura-thresholds-d4.csv", threshold_rows)
    write_csv(HERE / "top150-residual-score-audit-d4.csv", top_audit)
    write_csv(HERE / "family-concentration-d4.csv", family_rows)
    (HERE / "summary.json").write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")

    result = f"""# H1D10D4 — auditoría del residual y del fallback textual existente

Experimento diagnóstico aislado. No entrena ni ejecuta un modelo nuevo.
Reutiliza scores OOF group-aware ya generados por H1D10B y resultados D3.

## Hallazgo principal

No es seguro conectar TEXT_ONLY directamente detrás de D3 con threshold 0,5.

En las **{len(abstentions)}** abstenciones independientes de D3:

- ground truth: **{Counter(r['Expected'] for r in abstentions).get('OTRO_DOCUMENTO', 0)} OTRO_DOCUMENTO**;
- TEXT_ONLY @0,5: **{Counter(r['TextOnlyPrediction05'] for r in abstentions).get('FACTURA', 0)} FACTURA**;
- HEADER_TEXT @0,5: **{Counter(r['HeaderTextPrediction05'] for r in abstentions).get('FACTURA', 0)} FACTURA**;
- rango TEXT_ONLY P(FACTURA): **{min(abst_text_scores):.6f} – {max(abst_text_scores):.6f}**.

Si se hiciera el fallback ingenuo D3 + TEXT_ONLY@0,5:

- decisiones: **734 / 734**;
- correctas: **{naive_correct}**;
- incorrectas: **{naive_wrong}**;
- precisión al decidir: **{naive_correct / len(independent):.6f}**.

Eso empeora el contrato de seguridad de D3, que tenía 0 errores al decidir.

## Señal útil que sí conserva TEXT_ONLY

La región de P(FACTURA) baja sigue siendo interesante para selección de candidatos:

| máximo P(FACTURA) | filas independientes | OTRO | FACTURA | precisión OTRO observada |
|---:|---:|---:|---:|---:|
"""
    for row in threshold_rows:
        result += f"| {row['ThresholdMaxPFactura']} | {row['Rows']} | {row['ExpectedOtro']} | {row['ExpectedFactura']} | {row['ObservedOtroPrecision'] or '-'} |\n"

    result += f"""

Pero el soporte independiente de los thresholds muy bajos es pequeño; no corresponde convertir esto en regla productiva.

## Residual Top150 después de D3

- residual: **{len(residual86)} casos / {len(family_counts)} familias**;
- lenguaje/contexto: **{need_counts.get('LIKELY_SEMANTIC_LANGUAGE_CONTEXT', 0)} casos / {summary['LanguageContextFamilies']} familias**;
- semántica o estructura: **{need_counts.get('LIKELY_SEMANTIC_OR_STRUCTURE', 0)} casos**;
- percepción/política determinística primero: **{need_counts.get('DETERMINISTIC_EVIDENCE_GAP_FIRST', 0)} casos**.

Dentro de los **{len(language)}** casos de lenguaje/contexto:

- **{summary['LanguageContextTextOnlyBelow05']} / {len(language)}** tienen TEXT_ONLY P(FACTURA) < 0,5;
- **{summary['LanguageContextTextOnlyAtOrBelow035']} / {len(language)}** están en P(FACTURA) <= 0,35;
- esto coincide diagnósticamente con la referencia visual, pero esa referencia **NO es ground truth**.

La zona <=0,35 contiene **{len(low035)} casos / {summary['LowPFacturaCandidateZone035Families']} familias** y se conserva sólo como cohorte candidata para evaluar una futura capa semántica.

## Concentración familiar

- top 4 familias: **{top4} / {len(residual86)} = {top4 / len(residual86):.1%}**;
- top 10 familias: **{top10} / {len(residual86)} = {top10 / len(residual86):.1%}**.

Esto obliga a mantener evaluación **group-aware por FamilyId**. Varias familias contienen más de un subtipo diagnóstico, por lo que FamilyId no debe usarse como sustituto del label.

## Conclusión

El bag-of-ngrams actual no es una segunda capa automática confiable para los hard negatives que D3 deja abiertos. Sí aporta una señal de ranking clara en correos/transferencias, pero esa señal no alcanza como contrato productivo.

El siguiente experimento no debe ser otra regla ni un threshold ajustado sobre estos casos. Debe comparar una **representación semántica preentrenada** contra el baseline actual, con folds group-aware, sin SEALED_TEST y con abstención explícita.

## Gates

- `SealedAssetsRead = false`
- `TrainingExecuted = false`
- `ProductionModified = false`
- `GroundTruthModified = false`
- `ModelsPromoted = false`
- `NewModelInferenceExecuted = false`
- revisión visual Top150: referencia diagnóstica, no ground truth.
"""
    (HERE / "resultado.md").write_text(result, encoding="utf-8")

    print(json.dumps(summary, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
