import argparse
import csv
import hashlib
import json
import math
import statistics
import time
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image

EXPECTED = {
    "dataset": "AFECA7A2F995CE1B2DF6F9DAF3501A392CC7FB4DD1509900A19D1588E26794C2",
    "frozen": "FADEA71A298125E8CE0EB65C31F6232EAAE72EB71F33141B912D23F4E59603E4",
    "fold": "9E4A9ACC7DB4B042A96A28502745ADC32F78AF7866A45918000668C127D895D9",
    "onnx": "A1DC24FE90C3C14303C3319EC0BD6D9EF95E91CC82C9B38E2FBAAEB0EF826811",
    "checkpoint": "F6F552CF5FAD856D7FB57352C63C4CD68C3E3E0F6C039C3C7623B030FD965F27",
}
EXPECTED_ONNX_BYTES = 16708744
EXPECTED_GROUPS = {
    "factura-familia-arillo", "factura-homologacion-c-00004-00000002", "flightaware-newsletter",
    "comprobante-pago-bancario", "otro-solicitud-nota-credito-banco-patagonia",
}
T_NO_FACTURA = 0.1169927716255188
T_FACTURA = 0.7979831695556641
MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32).reshape(1, 1, 3)
STD = np.array([0.229, 0.224, 0.225], dtype=np.float32).reshape(1, 1, 3)
CONCEPTUAL = {
    "D9AFAF729E3D7965CE69B7DA7477640EFE7CB3725C7753E3C22305D06BF77C6B": "fotografía aeronáutica",
    "122412579CB7F775A638F44BF40EACB229C173965B54C8B8FBC969D28DA6F2DC": "factura electrónica visual clara",
    "31371F0EA09BFB13ECAECD39B9C36CF70E4662CB0E31F2ABE26B8E74402003F5": "solicitud / nota de crédito",
    "2D246A2651752808442383D814C806437999B4C22FD2E77D18BEDAF2AC6786AB": "comprobante de pago bancario",
}


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def require_hash(path, expected, name):
    actual = sha256(path)
    if actual != expected:
        raise RuntimeError(f"SHA-256 inesperado para {name}: {actual}")


def read_csv(path):
    with open(path, newline="", encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream))


def write_csv(path, fields, rows):
    with open(path, "w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields, extrasaction="ignore", lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def preprocess(path):
    with Image.open(path) as source:
        image = source.convert("RGB")
        scale = min(224.0 / image.width, 224.0 / image.height)
        size = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
        resized = image.resize(size, Image.Resampling.BICUBIC)
        canvas = Image.new("RGB", (224, 224), (255, 255, 255))
        canvas.paste(resized, ((224 - size[0]) // 2, (224 - size[1]) // 2))
        array = np.asarray(canvas, dtype=np.float32) / 255.0
    array = (array - MEAN) / STD
    return np.transpose(array, (2, 0, 1))[None, :, :, :].astype(np.float32)


def zone(probability):
    if probability <= T_NO_FACTURA:
        return "NO_FACTURA_FUERTE"
    if probability >= T_FACTURA:
        return "FACTURA_FUERTE"
    return "INCIERTO_VISUAL"


def confusion(labels, probabilities):
    tp = sum(y == 1 and p >= 0.5 for y, p in zip(labels, probabilities))
    tn = sum(y == 0 and p < 0.5 for y, p in zip(labels, probabilities))
    fp = sum(y == 0 and p >= 0.5 for y, p in zip(labels, probabilities))
    fn = sum(y == 1 and p < 0.5 for y, p in zip(labels, probabilities))
    return tn, fp, fn, tp


def roc_auc(labels, probabilities):
    positives = [p for y, p in zip(labels, probabilities) if y == 1]
    negatives = [p for y, p in zip(labels, probabilities) if y == 0]
    return sum((p > n) + 0.5 * (p == n) for p in positives for n in negatives) / (len(positives) * len(negatives))


def average_precision(labels, probabilities):
    ranked = sorted(zip(probabilities, labels), reverse=True)
    positives = sum(labels)
    found = 0
    total = 0.0
    for rank, (_, label) in enumerate(ranked, 1):
        if label:
            found += 1
            total += found / rank
    return total / positives


def metrics(labels, probabilities):
    tn, fp, fn, tp = confusion(labels, probabilities)
    recall = tp / (tp + fn) if tp + fn else 0.0
    precision = tp / (tp + fp) if tp + fp else 0.0
    specificity = tn / (tn + fp) if tn + fp else 0.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {"tn": tn, "fp": fp, "fn": fn, "tp": tp, "recall_factura": recall, "precision_factura": precision,
            "specificity_no_factura": specificity, "f1_factura": f1, "balanced_accuracy": (recall + specificity) / 2,
            "roc_auc": roc_auc(labels, probabilities), "pr_auc": average_precision(labels, probabilities)}


def fmt(value):
    return f"{value:.6f}"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", required=True)
    parser.add_argument("--frozen", required=True)
    parser.add_argument("--fold-manifest", required=True)
    parser.add_argument("--candidate-manifest", required=True)
    parser.add_argument("--onnx", required=True)
    parser.add_argument("--checkpoint", required=True)
    parser.add_argument("--test-assets", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    output = Path(args.output).resolve()

    require_hash(args.dataset, EXPECTED["dataset"], "dataset.csv")
    require_hash(args.frozen, EXPECTED["frozen"], "frozen-test-groups.txt")
    require_hash(args.fold_manifest, EXPECTED["fold"], "fold-manifest.csv")
    require_hash(args.onnx, EXPECTED["onnx"], "candidate.onnx")
    require_hash(args.checkpoint, EXPECTED["checkpoint"], "candidate-checkpoint.pt")
    if Path(args.onnx).stat().st_size != EXPECTED_ONNX_BYTES:
        raise RuntimeError("Tamaño inesperado de candidate.onnx")

    candidate = json.loads(Path(args.candidate_manifest).read_text(encoding="utf-8"))
    if candidate["candidate_id"] != "H1D9B-CANDIDATE-001" or candidate["architecture"] != "EfficientNet-B0":
        raise RuntimeError("Identidad/arquitectura del candidato inesperada")
    if candidate["class_mapping"] != {"NO_FACTURA": 0, "FACTURA": 1}:
        raise RuntimeError("Class mapping inesperado")
    if candidate["development_manifest_sha256"] != EXPECTED["fold"] or candidate["candidate"]["onnx"]["external_data"] is not False:
        raise RuntimeError("Manifest del candidato inconsistente")
    preprocessing = candidate["preprocessing"]
    if preprocessing != {"input": "complete page/image", "resize": "preserve aspect ratio to fit 224x224", "crop": False,
                         "canvas": [224, 224], "centering": True, "padding_rgb": [255, 255, 255], "color": "RGB",
                         "float_range": [0.0, 1.0], "mean": [0.485, 0.456, 0.406], "std": [0.229, 0.224, 0.225]}:
        raise RuntimeError("Preprocessing congelado inesperado")

    frozen = {line.strip() for line in Path(args.frozen).read_text(encoding="utf-8").splitlines() if line.strip()}
    if frozen != EXPECTED_GROUPS:
        raise RuntimeError("Grupos frozen inesperados")
    assets = read_csv(args.test_assets)
    if len(assets) != 10 or len({r["Sha256"] for r in assets}) != 10 or {r["GroupId"] for r in assets} != EXPECTED_GROUPS:
        raise RuntimeError("Test asset manifest no coincide con 10 archivos/5 grupos")
    if sum(r["LabelBinario"] == "FACTURA" for r in assets) != 4:
        raise RuntimeError("Composición binaria TEST inesperada")

    session = ort.InferenceSession(str(Path(args.onnx).resolve()), providers=["CPUExecutionProvider"])
    if session.get_providers() != ["CPUExecutionProvider"]:
        raise RuntimeError("ONNX Runtime no quedó limitado a CPUExecutionProvider")
    model_input = session.get_inputs()[0]
    model_output = session.get_outputs()[0]
    if model_input.name != "image" or model_input.shape != [1, 3, 224, 224] or model_output.name != "probabilities" or model_output.shape != [1, 2]:
        raise RuntimeError("Metadata ONNX inesperada")

    predictions = []
    latencies = []
    for row in assets:
        tensor = preprocess(row["VisualAssetPath"])
        start = time.perf_counter()
        values = session.run(["probabilities"], {"image": tensor})[0][0]
        latencies.append((time.perf_counter() - start) * 1000)
        p_no, p_yes = float(values[0]), float(values[1])
        if abs((p_no + p_yes) - 1.0) > 1e-5:
            raise RuntimeError("La salida ONNX no suma 1")
        predictions.append({"Sha256": row["Sha256"], "GroupId": row["GroupId"], "LabelOriginal": row["LabelOriginal"],
                            "LabelBinario": row["LabelBinario"], "PNoFactura": p_no, "PFactura": p_yes,
                            "Pred050": "FACTURA" if p_yes >= 0.5 else "NO_FACTURA", "ZonaOOFPreRegistrada": zone(p_yes)})
    write_csv(output / "test-predictions.csv", ["Sha256", "GroupId", "LabelOriginal", "LabelBinario", "PNoFactura", "PFactura", "Pred050", "ZonaOOFPreRegistrada"], predictions)

    file_labels = [1 if row["LabelBinario"] == "FACTURA" else 0 for row in predictions]
    file_probabilities = [row["PFactura"] for row in predictions]
    file_metrics = metrics(file_labels, file_probabilities)
    grouped = defaultdict(list)
    for row in predictions:
        grouped[row["GroupId"]].append(row)
    group_results = []
    for group_id in sorted(grouped):
        items = grouped[group_id]
        probability = statistics.mean(row["PFactura"] for row in items)
        group_results.append({"GroupId": group_id, "LabelBinario": items[0]["LabelBinario"], "Files": len(items), "PFactura": probability,
                              "Pred050": "FACTURA" if probability >= 0.5 else "NO_FACTURA", "Correct": (probability >= 0.5) == (items[0]["LabelBinario"] == "FACTURA")})
    group_metrics = metrics([1 if row["LabelBinario"] == "FACTURA" else 0 for row in group_results], [row["PFactura"] for row in group_results])

    strong_no = [row for row in predictions if row["ZonaOOFPreRegistrada"] == "NO_FACTURA_FUERTE"]
    strong_yes = [row for row in predictions if row["ZonaOOFPreRegistrada"] == "FACTURA_FUERTE"]
    uncertain = [row for row in predictions if row["ZonaOOFPreRegistrada"] == "INCIERTO_VISUAL"]
    strong_no_errors = sum(row["LabelBinario"] == "FACTURA" for row in strong_no)
    strong_yes_errors = sum(row["LabelBinario"] == "NO_FACTURA" for row in strong_yes)
    conceptual = {row["Sha256"]: row for row in predictions if row["Sha256"] in CONCEPTUAL}
    if set(conceptual) != set(CONCEPTUAL):
        raise RuntimeError("Falta un caso conceptual obligatorio")

    gates = {
        "A_integrity": len(predictions) == 10 and len(group_results) == 5,
        "B_factura_to_no_factura_fuerte_zero": strong_no_errors == 0,
        "C_no_no_factura_to_factura_fuerte": strong_yes_errors == 0,
        "D_all_groups_correct_050": all(row["Correct"] for row in group_results),
        "E_conceptual_predictions": conceptual["D9AFAF729E3D7965CE69B7DA7477640EFE7CB3725C7753E3C22305D06BF77C6B"]["Pred050"] == "NO_FACTURA"
                                    and conceptual["122412579CB7F775A638F44BF40EACB229C173965B54C8B8FBC969D28DA6F2DC"]["Pred050"] == "FACTURA"
                                    and conceptual["31371F0EA09BFB13ECAECD39B9C36CF70E4662CB0E31F2ABE26B8E74402003F5"]["Pred050"] == "NO_FACTURA"
                                    and conceptual["2D246A2651752808442383D814C806437999B4C22FD2E77D18BEDAF2AC6786AB"]["Pred050"] == "NO_FACTURA",
    }
    approved = all(gates.values())

    metric_lines = ["# Métricas TEST H1D9C", "", "## File-level", "",
                    "| TN | FP | FN | TP | Recall FACTURA | Precision FACTURA | Specificity NO_FACTURA | F1 FACTURA | Balanced accuracy | ROC-AUC | PR-AUC |",
                    "|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
                    f"| {file_metrics['tn']} | {file_metrics['fp']} | {file_metrics['fn']} | {file_metrics['tp']} | {fmt(file_metrics['recall_factura'])} | {fmt(file_metrics['precision_factura'])} | {fmt(file_metrics['specificity_no_factura'])} | {fmt(file_metrics['f1_factura'])} | {fmt(file_metrics['balanced_accuracy'])} | {fmt(file_metrics['roc_auc'])} | {fmt(file_metrics['pr_auc'])} |",
                    "", "## Zonas OOF pre-registradas", "", f"- NO_FACTURA_FUERTE: {len(strong_no)}; errores: {strong_no_errors}.",
                    f"- FACTURA_FUERTE: {len(strong_yes)}; errores: {strong_yes_errors}.", f"- INCIERTO_VISUAL: {len(uncertain)}.",
                    "", "## Group-level (métrica principal)", "",
                    "| TN | FP | FN | TP | Recall FACTURA | Specificity NO_FACTURA | Balanced accuracy | ROC-AUC | PR-AUC |",
                    "|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
                    f"| {group_metrics['tn']} | {group_metrics['fp']} | {group_metrics['fn']} | {group_metrics['tp']} | {fmt(group_metrics['recall_factura'])} | {fmt(group_metrics['specificity_no_factura'])} | {fmt(group_metrics['balanced_accuracy'])} | {fmt(group_metrics['roc_auc'])} | {fmt(group_metrics['pr_auc'])} |",
                    "", "### Resultados individuales por grupo", "", "| GroupId | Archivos | Label | PFactura media | Pred050 | Correcto |", "|---|---:|---|---:|---|---|"]
    for row in group_results:
        metric_lines.append(f"| {row['GroupId']} | {row['Files']} | {row['LabelBinario']} | {fmt(row['PFactura'])} | {row['Pred050']} | {row['Correct']} |")
    (output / "test-metrics.md").write_text("\n".join(metric_lines) + "\n", encoding="utf-8")

    conceptual_lines = ["# Casos conceptuales H1D9C", "", "| Caso | SHA-256 | Label | PFactura | Pred050 | Zona OOF pre-registrada | Observación |", "|---|---|---|---:|---|---|---|"]
    for sha, description in CONCEPTUAL.items():
        row = conceptual[sha]
        limitation = "Cumple expectativa conceptual."
        if sha == "D9AFAF729E3D7965CE69B7DA7477640EFE7CB3725C7753E3C22305D06BF77C6B" and row["Pred050"] == "NO_FACTURA" and row["ZonaOOFPreRegistrada"] != "NO_FACTURA_FUERTE":
            limitation = "Correcta a 0.5, pero no alcanzó zona NO_FACTURA_FUERTE."
        if sha == "122412579CB7F775A638F44BF40EACB229C173965B54C8B8FBC969D28DA6F2DC" and row["Pred050"] == "FACTURA" and row["ZonaOOFPreRegistrada"] != "FACTURA_FUERTE":
            limitation = "Correcta a 0.5, pero no alcanzó zona FACTURA_FUERTE."
        conceptual_lines.append(f"| {description} | `{sha}` | {row['LabelBinario']} | {fmt(row['PFactura'])} | {row['Pred050']} | {row['ZonaOOFPreRegistrada']} | {limitation} |")
    (output / "conceptual-cases.md").write_text("\n".join(conceptual_lines) + "\n", encoding="utf-8")

    evaluation_manifest = {
        "candidate_id": "H1D9B-CANDIDATE-001", "candidate_onnx_sha256": EXPECTED["onnx"], "dataset_sha256": EXPECTED["dataset"],
        "frozen_test_groups_sha256": EXPECTED["frozen"], "h1d9b_development_manifest_sha256": EXPECTED["fold"],
        "preprocessing": preprocessing, "thresholds_pre_registered": {"T_NO_FACTURA": T_NO_FACTURA, "T_FACTURA": T_FACTURA},
        "timestamp_utc": datetime.now(timezone.utc).isoformat(), "onnxruntime_version": ort.__version__, "execution_provider": "CPUExecutionProvider",
        "test_file_count": len(predictions), "test_group_count": len(group_results), "file_metrics": file_metrics, "group_metrics": group_metrics,
        "zones": {"NO_FACTURA_FUERTE": len(strong_no), "FACTURA_FUERTE": len(strong_yes), "INCIERTO_VISUAL": len(uncertain),
                  "strong_no_errors": strong_no_errors, "strong_factura_errors": strong_yes_errors},
        "group_results": group_results, "gates": gates, "status": "H1D9C APROBADO" if approved else "H1D9C NO APROBADO",
        "ort_inference_mean_ms": statistics.mean(latencies), "ort_inference_p95_ms": sorted(latencies)[math.ceil(0.95 * len(latencies)) - 1],
        "training_performed": False, "threshold_tuning_performed": False,
    }
    (output / "evaluation-manifest.json").write_text(json.dumps(evaluation_manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    summary = ["# H1D9C — Evaluación congelada sobre TEST", "", f"**{'H1D9C APROBADO' if approved else 'H1D9C NO APROBADO'}**", "",
               "- Evaluación holdout/regresión sobre 10 archivos y 5 grupos históricos; no es certificación estadística productiva.",
               f"- Archivos/grupos procesados: {len(predictions)}/10 y {len(group_results)}/5.",
               f"- File-level: recall FACTURA {fmt(file_metrics['recall_factura'])}, specificity {fmt(file_metrics['specificity_no_factura'])}, balanced accuracy {fmt(file_metrics['balanced_accuracy'])}, ROC-AUC {fmt(file_metrics['roc_auc'])}.",
               f"- Group-level: {sum(row['Correct'] for row in group_results)}/5 correctos; recall FACTURA {fmt(group_metrics['recall_factura'])}, specificity {fmt(group_metrics['specificity_no_factura'])}, balanced accuracy {fmt(group_metrics['balanced_accuracy'])}.",
               f"- Zonas: NO_FACTURA_FUERTE={len(strong_no)}, FACTURA_FUERTE={len(strong_yes)}, INCIERTO_VISUAL={len(uncertain)}; errores fuertes={strong_no_errors + strong_yes_errors}.",
               "- Entrenamiento realizado: false. Tuning de thresholds: false.", "- Producto WebForms/H1D8B no modificado. Integración productiva no ejecutada."]
    for name, passed in gates.items():
        summary.append(f"- Gate {name}: {'PASS' if passed else 'FAIL'}.")
    (output / "resumen.md").write_text("\n".join(summary) + "\n", encoding="utf-8")
    print("H1D9C | " + ("APROBADO" if approved else "NO APROBADO"))
    for row in predictions:
        print(f"PRED | {row['Sha256']} | {row['GroupId']} | {row['LabelBinario']} | PFactura={row['PFactura']:.9f} | {row['Pred050']} | {row['ZonaOOFPreRegistrada']}")


if __name__ == "__main__":
    main()
