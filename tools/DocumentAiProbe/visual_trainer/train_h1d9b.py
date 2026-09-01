import argparse
import csv
import hashlib
import json
import math
import os
import platform
import random
import statistics
import sys
import time
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
from PIL import Image
import torch
from torch import nn
from torch.utils.data import DataLoader, Dataset
from torchvision import models, transforms
from torchvision.transforms import InterpolationMode
import torchvision
import onnxruntime as ort

SEED = 19092026
DATASET_SHA = "AFECA7A2F995CE1B2DF6F9DAF3501A392CC7FB4DD1509900A19D1588E26794C2"
FROZEN_SHA = "FADEA71A298125E8CE0EB65C31F6232EAAE72EB71F33141B912D23F4E59603E4"
CLASS_TO_INDEX = {"NO_FACTURA": 0, "FACTURA": 1}
MEAN = [0.485, 0.456, 0.406]
STD = [0.229, 0.224, 0.225]
MODEL_SPECS = {
    "EfficientNet-B0": {
        "factory": models.efficientnet_b0,
        "weights": models.EfficientNet_B0_Weights.IMAGENET1K_V1,
        "classifier_index": 1,
        "last_block": "features.8",
    },
    "MobileNetV3-Large": {
        "factory": models.mobilenet_v3_large,
        "weights": models.MobileNet_V3_Large_Weights.IMAGENET1K_V2,
        "classifier_index": 3,
        "last_block": "features.16",
    },
}
CONFIG = {
    "seed": SEED,
    "folds": 5,
    "batch_size": 8,
    "num_workers": 0,
    "phase1": {"max_epochs": 8, "learning_rate": 0.001, "patience": 3},
    "phase2": {"max_epochs": 12, "learning_rate": 0.0001, "patience": 4},
    "weight_decay": 0.0001,
    "optimizer": "AdamW",
    "loss": "per-sample weighted CrossEntropyLoss",
    "sample_weight_formula": "(1 / number_of_train_groups_in_binary_class) / number_of_train_files_in_sample_GroupId; normalized to train mean 1",
    "augmentation_train_only": {
        "rotation_degrees": [-3, 3], "translation_fraction": [0.03, 0.03], "scale": [0.95, 1.05],
        "brightness": 0.10, "contrast": 0.10, "horizontal_flip": False, "vertical_flip": False, "crop": False,
    },
    "preprocessing": {
        "input": "complete page/image", "resize": "preserve aspect ratio to fit 224x224", "crop": False,
        "canvas": [224, 224], "centering": True, "padding_rgb": [255, 255, 255], "color": "RGB",
        "float_range": [0.0, 1.0], "mean": MEAN, "std": STD,
    },
    "class_mapping": CLASS_TO_INDEX,
}


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest().upper()


def set_seed(seed):
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.use_deterministic_algorithms(True)
    torch.set_num_threads(max(1, min(8, os.cpu_count() or 1)))


class Letterbox:
    def __call__(self, image):
        image = image.convert("RGB")
        scale = min(224.0 / image.width, 224.0 / image.height)
        size = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
        resized = image.resize(size, Image.Resampling.BICUBIC)
        canvas = Image.new("RGB", (224, 224), (255, 255, 255))
        canvas.paste(resized, ((224 - size[0]) // 2, (224 - size[1]) // 2))
        return canvas


TRAIN_TRANSFORM = transforms.Compose([
    Letterbox(),
    transforms.RandomAffine(degrees=3, translate=(0.03, 0.03), scale=(0.95, 1.05), interpolation=InterpolationMode.BILINEAR, fill=255),
    transforms.ColorJitter(brightness=0.10, contrast=0.10),
    transforms.ToTensor(),
    transforms.Normalize(MEAN, STD),
])
EVAL_TRANSFORM = transforms.Compose([Letterbox(), transforms.ToTensor(), transforms.Normalize(MEAN, STD)])


class VisualDataset(Dataset):
    def __init__(self, rows, train, sample_weights=None):
        self.rows = rows
        self.transform = TRAIN_TRANSFORM if train else EVAL_TRANSFORM
        self.sample_weights = sample_weights

    def __len__(self):
        return len(self.rows)

    def __getitem__(self, index):
        row = self.rows[index]
        with Image.open(row["VisualAssetPath"]) as image:
            tensor = self.transform(image)
        label = CLASS_TO_INDEX[row["LabelBinario"]]
        weight = 1.0 if self.sample_weights is None else self.sample_weights[index]
        return tensor, label, float(weight), index


def read_csv(path):
    with open(path, newline="", encoding="utf-8-sig") as f:
        return list(csv.DictReader(f))


def write_csv(path, fieldnames, rows):
    with open(path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames, extrasaction="ignore", lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def prepare_rows(asset_manifest, frozen_path):
    if sha256(frozen_path) != FROZEN_SHA:
        raise RuntimeError("SHA-256 de frozen-test-groups.txt cambió")
    frozen = {line.strip() for line in Path(frozen_path).read_text(encoding="utf-8").splitlines() if line.strip()}
    rows = read_csv(asset_manifest)
    if len(rows) != 70 or len({r["Sha256"] for r in rows}) != 70 or len({r["GroupId"] for r in rows}) != 49:
        raise RuntimeError("Asset manifest no coincide con 70 archivos/49 grupos")
    if {r["GroupId"] for r in rows} & frozen:
        raise RuntimeError("TEST frozen leakage en asset manifest")
    for row in rows:
        if not Path(row["VisualAssetPath"]).is_file():
            raise RuntimeError("Falta asset: " + row["VisualAssetPath"])
    return rows, frozen


def assign_folds(rows, frozen):
    group_labels = {}
    for row in rows:
        previous = group_labels.setdefault(row["GroupId"], row["LabelBinario"])
        if previous != row["LabelBinario"]:
            raise RuntimeError("GroupId con etiquetas binarias inconsistentes: " + row["GroupId"])
    group_fold = {}
    for label in ["NO_FACTURA", "FACTURA"]:
        groups = [g for g, value in group_labels.items() if value == label]
        groups.sort(key=lambda g: hashlib.sha256(f"{SEED}|{label}|{g}".encode("utf-8")).hexdigest())
        for index, group in enumerate(groups):
            group_fold[group] = index % 5
    manifest = []
    for row in rows:
        manifest.append({
            "Sha256": row["Sha256"], "GroupId": row["GroupId"], "LabelOriginal": row["LabelOriginal"],
            "LabelBinario": row["LabelBinario"], "Fold": group_fold[row["GroupId"]], "VisualAssetPath": row["VisualAssetPath"],
        })
    if len(manifest) != 70 or len({x["Sha256"] for x in manifest}) != 70:
        raise RuntimeError("SHA leakage o pérdida en folds")
    for fold in range(5):
        train_groups = {x["GroupId"] for x in manifest if x["Fold"] != fold}
        val_groups = {x["GroupId"] for x in manifest if x["Fold"] == fold}
        if train_groups & val_groups:
            raise RuntimeError("GroupId leakage en fold " + str(fold))
        if not any(x["LabelBinario"] == "FACTURA" and x["Fold"] == fold for x in manifest):
            raise RuntimeError("Fold sin FACTURA: " + str(fold))
    if {x["GroupId"] for x in manifest} & frozen:
        raise RuntimeError("TEST frozen leakage en folds")
    return manifest


def sample_weights(rows):
    group_sizes = Counter(r["GroupId"] for r in rows)
    group_class = {r["GroupId"]: r["LabelBinario"] for r in rows}
    class_groups = Counter(group_class.values())
    raw = [(1.0 / class_groups[r["LabelBinario"]]) / group_sizes[r["GroupId"]] for r in rows]
    mean = sum(raw) / len(raw)
    return [x / mean for x in raw]


def build_model(name):
    spec = MODEL_SPECS[name]
    model = spec["factory"](weights=spec["weights"])
    if name == "EfficientNet-B0":
        in_features = model.classifier[1].in_features
        model.classifier[1] = nn.Linear(in_features, 2)
    else:
        in_features = model.classifier[3].in_features
        model.classifier[3] = nn.Linear(in_features, 2)
    return model


def configure_phase(model, name, phase):
    for parameter in model.parameters():
        parameter.requires_grad = False
    for parameter in model.classifier.parameters():
        parameter.requires_grad = True
    if phase == 2:
        block_index = 8 if name == "EfficientNet-B0" else 16
        for parameter in model.features[block_index].parameters():
            parameter.requires_grad = True


def weighted_loss(logits, labels, weights):
    losses = nn.functional.cross_entropy(logits, labels, reduction="none")
    return (losses * weights).sum() / weights.sum()


def evaluate(model, loader):
    model.eval()
    loss_total = 0.0
    count = 0
    probabilities = {}
    with torch.no_grad():
        for images, labels, weights, indices in loader:
            logits = model(images)
            loss = nn.functional.cross_entropy(logits, labels, reduction="sum")
            probs = torch.softmax(logits, dim=1)[:, 1]
            loss_total += float(loss)
            count += len(labels)
            for idx, prob in zip(indices.tolist(), probs.tolist()):
                probabilities[idx] = prob
    return loss_total / count, probabilities


def train_phase(model, name, phase, train_rows, val_rows, max_epochs=None):
    configure_phase(model, name, phase)
    cfg = CONFIG[f"phase{phase}"]
    epochs = max_epochs or cfg["max_epochs"]
    patience = cfg["patience"] if max_epochs is None else max_epochs + 1
    train_set = VisualDataset(train_rows, True, sample_weights(train_rows))
    val_set = VisualDataset(val_rows, False)
    generator = torch.Generator().manual_seed(SEED + phase)
    train_loader = DataLoader(train_set, batch_size=CONFIG["batch_size"], shuffle=True, num_workers=0, generator=generator)
    val_loader = DataLoader(val_set, batch_size=CONFIG["batch_size"], shuffle=False, num_workers=0)
    optimizer = torch.optim.AdamW([p for p in model.parameters() if p.requires_grad], lr=cfg["learning_rate"], weight_decay=CONFIG["weight_decay"])
    best_loss = float("inf")
    best_state = None
    best_epoch = 0
    stale = 0
    history = []
    for epoch in range(1, epochs + 1):
        model.train()
        total = 0.0
        seen = 0
        for images, labels, weights, _ in train_loader:
            optimizer.zero_grad(set_to_none=True)
            loss = weighted_loss(model(images), labels, weights)
            loss.backward()
            optimizer.step()
            total += float(loss) * len(labels)
            seen += len(labels)
        val_loss, _ = evaluate(model, val_loader)
        history.append({"phase": phase, "epoch": epoch, "train_loss": total / seen, "validation_loss": val_loss})
        if val_loss < best_loss - 1e-5:
            best_loss = val_loss
            best_state = {k: v.detach().cpu().clone() for k, v in model.state_dict().items()}
            best_epoch = epoch
            stale = 0
        else:
            stale += 1
            if stale >= patience:
                break
    model.load_state_dict(best_state)
    return best_epoch, history


def run_fold(name, fold, rows):
    set_seed(SEED + fold)
    train_rows = [r for r in rows if int(r["Fold"]) != fold]
    val_rows = [r for r in rows if int(r["Fold"]) == fold]
    if {r["GroupId"] for r in train_rows} & {r["GroupId"] for r in val_rows}:
        raise RuntimeError("Group leakage al entrenar")
    model = build_model(name)
    best1, history1 = train_phase(model, name, 1, train_rows, val_rows)
    best2, history2 = train_phase(model, name, 2, train_rows, val_rows)
    val_loader = DataLoader(VisualDataset(val_rows, False), batch_size=CONFIG["batch_size"], num_workers=0)
    _, probs = evaluate(model, val_loader)
    predictions = []
    for index, row in enumerate(val_rows):
        p = probs[index]
        predictions.append({**row, "Model": name, "PNoFactura": 1.0 - p, "PFactura": p, "Pred050": "FACTURA" if p >= 0.5 else "NO_FACTURA"})
    del model
    return predictions, {"fold": fold, "best_phase1_epoch": best1, "best_phase2_epoch": best2, "history": history1 + history2}


def confusion(labels, probs, threshold=0.5):
    tp = sum(y == 1 and p >= threshold for y, p in zip(labels, probs))
    tn = sum(y == 0 and p < threshold for y, p in zip(labels, probs))
    fp = sum(y == 0 and p >= threshold for y, p in zip(labels, probs))
    fn = sum(y == 1 and p < threshold for y, p in zip(labels, probs))
    return tn, fp, fn, tp


def roc_auc(labels, probs):
    positives = [p for y, p in zip(labels, probs) if y == 1]
    negatives = [p for y, p in zip(labels, probs) if y == 0]
    wins = sum((p > n) + 0.5 * (p == n) for p in positives for n in negatives)
    return wins / (len(positives) * len(negatives))


def average_precision(labels, probs):
    ordered = sorted(zip(probs, labels), reverse=True)
    found = 0
    total = sum(labels)
    score = 0.0
    for rank, (_, label) in enumerate(ordered, 1):
        if label:
            found += 1
            score += found / rank
    return score / total


def metric_set(labels, probs):
    tn, fp, fn, tp = confusion(labels, probs)
    recall = tp / (tp + fn) if tp + fn else 0.0
    precision = tp / (tp + fp) if tp + fp else 0.0
    specificity = tn / (tn + fp) if tn + fp else 0.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {"tn": tn, "fp": fp, "fn": fn, "tp": tp, "recall_factura": recall, "precision_factura": precision,
            "f1_factura": f1, "balanced_accuracy": (recall + specificity) / 2, "roc_auc": roc_auc(labels, probs),
            "pr_auc": average_precision(labels, probs)}


def aggregate_groups(predictions):
    grouped = defaultdict(list)
    for row in predictions:
        grouped[row["GroupId"]].append(row)
    result = []
    for group, items in grouped.items():
        result.append({"GroupId": group, "LabelBinario": items[0]["LabelBinario"], "PFactura": statistics.mean(x["PFactura"] for x in items)})
    return result


def analyze_thresholds(model, file_rows, group_rows):
    file_positive = [r["PFactura"] for r in file_rows if r["LabelBinario"] == "FACTURA"]
    file_negative = [r["PFactura"] for r in file_rows if r["LabelBinario"] == "NO_FACTURA"]
    group_positive = [r["PFactura"] for r in group_rows if r["LabelBinario"] == "FACTURA"]
    group_negative = [r["PFactura"] for r in group_rows if r["LabelBinario"] == "NO_FACTURA"]
    no_limit = min(file_positive + group_positive)
    no_candidates = sorted({p for p in file_negative + group_negative if p < no_limit})
    t_no = no_candidates[-1] if no_candidates else 0.0
    factura_limit = max(file_negative + group_negative)
    factura_candidates = sorted({p for p in file_positive + group_positive if p > factura_limit})
    t_factura = factura_candidates[0] if factura_candidates else 1.0
    output = []
    for level, rows in [("FILE", file_rows), ("GROUP", group_rows)]:
        strong_no = [r for r in rows if r["PFactura"] <= t_no]
        strong_yes = [r for r in rows if r["PFactura"] >= t_factura]
        output.append({"Model": model, "Level": level, "TNoFactura": t_no, "TFactura": t_factura, "Total": len(rows),
                       "NoFacturaFuerte": len(strong_no), "FacturaFuerte": len(strong_yes),
                       "Incierto": len(rows) - len(strong_no) - len(strong_yes),
                       "NoFacturaFuerteErrors": sum(r["LabelBinario"] == "FACTURA" for r in strong_no),
                       "FacturaFuerteErrors": sum(r["LabelBinario"] == "NO_FACTURA" for r in strong_yes),
                       "StrongCoverage": (len(strong_no) + len(strong_yes)) / len(rows), "UncertainWidth": t_factura - t_no})
    return output


class ProbabilityWrapper(nn.Module):
    def __init__(self, model):
        super().__init__()
        self.model = model

    def forward(self, image):
        return torch.softmax(self.model(image), dim=1)


def base_weight_info(name):
    weights = MODEL_SPECS[name]["weights"]
    filename = Path(weights.url).name
    path = Path(torch.hub.get_dir()) / "checkpoints" / filename
    if not path.is_file():
        raise RuntimeError("No se encontró el peso base cacheado: " + str(path))
    return {"enum": str(weights), "url": weights.url, "path": str(path), "sha256": sha256(path), "bytes": path.stat().st_size, "license": "torchvision models; BSD-3-Clause code, pretrained weight provenance ImageNet-1K"}


def train_candidate(name, rows, fold_runs, output):
    set_seed(SEED)
    model = build_model(name)
    phase1_epochs = max(1, round(statistics.median(r["best_phase1_epoch"] for r in fold_runs)))
    phase2_epochs = max(1, round(statistics.median(r["best_phase2_epoch"] for r in fold_runs)))
    train_phase(model, name, 1, rows, rows, phase1_epochs)
    train_phase(model, name, 2, rows, rows, phase2_epochs)
    checkpoint = output / "candidate-checkpoint.pt"
    torch.save({"candidate_id": "H1D9B-CANDIDATE-001", "architecture": name, "state_dict": model.state_dict(),
                "class_mapping": CLASS_TO_INDEX, "preprocessing": CONFIG["preprocessing"], "seed": SEED,
                "phase1_epochs": phase1_epochs, "phase2_epochs": phase2_epochs}, checkpoint)
    wrapper = ProbabilityWrapper(model.eval()).eval()
    onnx_path = output / "candidate.onnx"
    dummy = torch.zeros(1, 3, 224, 224)
    torch.onnx.export(wrapper, (dummy,), str(onnx_path), input_names=["image"], output_names=["probabilities"],
                      opset_version=18, dynamo=True, external_data=False)
    session = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    samples = rows[: min(10, len(rows))]
    errors = []
    latencies = []
    for row in samples:
        with Image.open(row["VisualAssetPath"]) as image:
            tensor = EVAL_TRANSFORM(image).unsqueeze(0)
        with torch.no_grad():
            expected = wrapper(tensor).numpy()
        start = time.perf_counter()
        actual = session.run(["probabilities"], {"image": tensor.numpy()})[0]
        latencies.append((time.perf_counter() - start) * 1000)
        errors.append(float(np.max(np.abs(expected - actual))))
    return {"checkpoint": checkpoint, "onnx": onnx_path, "phase1_epochs": phase1_epochs, "phase2_epochs": phase2_epochs,
            "max_abs_error": max(errors), "ort_cpu_mean_ms": statistics.mean(latencies), "ort_cpu_p95_ms": sorted(latencies)[math.ceil(0.95 * len(latencies)) - 1]}


def fmt(value):
    return f"{value:.4f}"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", required=True)
    parser.add_argument("--frozen", required=True)
    parser.add_argument("--asset-manifest", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    if sha256(args.dataset) != DATASET_SHA:
        raise RuntimeError("SHA-256 de dataset.csv cambió")
    set_seed(SEED)
    rows, frozen = prepare_rows(args.asset_manifest, args.frozen)
    folds = assign_folds(rows, frozen)
    write_csv(output / "fold-manifest.csv", ["Sha256", "GroupId", "LabelOriginal", "LabelBinario", "Fold", "VisualAssetPath"], folds)
    development_hash = sha256(output / "fold-manifest.csv")
    Path(output / "training-configuration.json").write_text(json.dumps(CONFIG, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("H1D9B | Leakage GroupId=0 | SHA=0 | TEST=0 | Folds=5", flush=True)

    all_predictions = []
    run_info = {}
    for name in MODEL_SPECS:
        run_info[name] = []
        for fold in range(5):
            print(f"H1D9B | Model={name} | Fold={fold + 1}/5 | START", flush=True)
            predictions, info = run_fold(name, fold, folds)
            all_predictions.extend(predictions)
            run_info[name].append(info)
            print(f"H1D9B | Model={name} | Fold={fold + 1}/5 | Phase1Epoch={info['best_phase1_epoch']} | Phase2Epoch={info['best_phase2_epoch']} | DONE", flush=True)

    if len(all_predictions) != 140 or any(sum(p["Sha256"] == r["Sha256"] and p["Model"] == name for p in all_predictions) != 1 for r in folds for name in MODEL_SPECS):
        raise RuntimeError("Cada archivo debe tener exactamente una predicción OOF por modelo")
    oof_fields = ["Sha256", "GroupId", "LabelOriginal", "LabelBinario", "Model", "Fold", "PNoFactura", "PFactura", "Pred050"]
    write_csv(output / "oof-predictions.csv", oof_fields, all_predictions)

    comparison = {}
    threshold_rows = []
    for name in MODEL_SPECS:
        files = [r for r in all_predictions if r["Model"] == name]
        groups = aggregate_groups(files)
        file_labels = [CLASS_TO_INDEX[r["LabelBinario"]] for r in files]
        group_labels = [CLASS_TO_INDEX[r["LabelBinario"]] for r in groups]
        comparison[name] = {"file": metric_set(file_labels, [r["PFactura"] for r in files]),
                            "group": metric_set(group_labels, [r["PFactura"] for r in groups]),
                            "folds": run_info[name], "fold_oof_metrics": []}
        for fold in range(5):
            fold_files = [r for r in files if int(r["Fold"]) == fold]
            fold_groups = aggregate_groups(fold_files)
            fold_metrics = metric_set([CLASS_TO_INDEX[r["LabelBinario"]] for r in fold_groups], [r["PFactura"] for r in fold_groups])
            comparison[name]["fold_oof_metrics"].append({"fold": fold, "files": len(fold_files), "groups": len(fold_groups), **fold_metrics})
        threshold_rows.extend(analyze_thresholds(name, files, groups))
    write_csv(output / "threshold-analysis.csv", ["Model", "Level", "TNoFactura", "TFactura", "Total", "NoFacturaFuerte", "FacturaFuerte", "Incierto", "NoFacturaFuerteErrors", "FacturaFuerteErrors", "StrongCoverage", "UncertainWidth"], threshold_rows)

    eligible = []
    for name, metrics in comparison.items():
        threshold_file = next(x for x in threshold_rows if x["Model"] == name and x["Level"] == "FILE")
        if metrics["group"]["roc_auc"] >= 0.80 and metrics["group"]["recall_factura"] >= 0.50 and threshold_file["NoFacturaFuerte"] > 0 and threshold_file["NoFacturaFuerteErrors"] == 0:
            eligible.append(name)
    winner = None
    candidate = None
    if eligible:
        winner = max(eligible, key=lambda n: (comparison[n]["group"]["recall_factura"], comparison[n]["group"]["roc_auc"], next(x["StrongCoverage"] for x in threshold_rows if x["Model"] == n and x["Level"] == "GROUP"), comparison[n]["group"]["balanced_accuracy"]))
        candidate = train_candidate(winner, folds, run_info[winner], output)

    environment = {
        "timestamp_utc": datetime.now(timezone.utc).isoformat(), "platform": platform.platform(), "python": sys.version,
        "torch": torch.__version__, "torchvision": torchvision.__version__, "onnxruntime": ort.__version__, "numpy": np.__version__,
        "pillow": Image.__version__, "cpu_threads": torch.get_num_threads(), "cuda_available": torch.cuda.is_available(),
    }
    Path(output / "environment.md").write_text("# Entorno H1D9B\n\n" + "\n".join(f"- {k}: `{v}`" for k, v in environment.items()) + "\n", encoding="utf-8")
    lines = ["# Métricas CV H1D9B", ""]
    for name, metrics in comparison.items():
        lines += [f"## {name}", "", "| Nivel | TN | FP | FN | TP | Recall FACTURA | Precision FACTURA | F1 | Balanced accuracy | ROC-AUC | PR-AUC |", "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]
        for level in ["file", "group"]:
            m = metrics[level]
            lines.append(f"| {level} | {m['tn']} | {m['fp']} | {m['fn']} | {m['tp']} | {fmt(m['recall_factura'])} | {fmt(m['precision_factura'])} | {fmt(m['f1_factura'])} | {fmt(m['balanced_accuracy'])} | {fmt(m['roc_auc'])} | {fmt(m['pr_auc'])} |")
        lines += [""]
    Path(output / "cv-metrics.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    status = "APROBADO COMO CANDIDATO" if winner else "NO APROBADO"
    comparison_lines = ["# Comparación de modelos H1D9B", "", "- Criterio principal: seguridad FACTURA y métricas group-aware.",
                        f"- Ganador OOF: **{winner or 'ninguno'}**.", f"- Gate candidato: **{status}**.", "",
                        "| Modelo | Recall FACTURA grupo | Balanced accuracy grupo | ROC-AUC grupo | PR-AUC grupo |",
                        "|---|---:|---:|---:|---:|"]
    for name, metrics in comparison.items():
        group = metrics["group"]
        comparison_lines.append(f"| {name} | {fmt(group['recall_factura'])} | {fmt(group['balanced_accuracy'])} | {fmt(group['roc_auc'])} | {fmt(group['pr_auc'])} |")
    if winner:
        winner_group = comparison[winner]["group"]
        others = [name for name in comparison if name != winner]
        comparison_lines += ["", f"**{winner}** fue seleccionado por priorizar seguridad sobre FACTURA: recall group-level {fmt(winner_group['recall_factura'])}, "
                             f"balanced accuracy {fmt(winner_group['balanced_accuracy'])} y ROC-AUC {fmt(winner_group['roc_auc'])}."]
        for name in others:
            group = comparison[name]["group"]
            comparison_lines.append(f"{name} quedó descartado: aunque obtuvo ROC-AUC {fmt(group['roc_auc'])}, su recall FACTURA group-level fue {fmt(group['recall_factura'])}.")
        comparison_lines += [""]
        for name, metrics in comparison.items():
            recalls = [fold["recall_factura"] for fold in metrics["fold_oof_metrics"]]
            aucs = [fold["roc_auc"] for fold in metrics["fold_oof_metrics"]]
            zero_folds = sum(value == 0 for value in recalls)
            comparison_lines.append(f"- Estabilidad {name}: recall FACTURA por fold {', '.join(fmt(x) for x in recalls)}; "
                                    f"ROC-AUC {fmt(min(aucs))}–{fmt(max(aucs))}; folds con recall 0: {zero_folds}.")
        winner_file_threshold = next(x for x in threshold_rows if x["Model"] == winner and x["Level"] == "FILE")
        winner_group_threshold = next(x for x in threshold_rows if x["Model"] == winner and x["Level"] == "GROUP")
        comparison_lines += ["", f"Cobertura segura {winner}: {winner_file_threshold['NoFacturaFuerte'] + winner_file_threshold['FacturaFuerte']}/{winner_file_threshold['Total']} archivos y "
                             f"{winner_group_threshold['NoFacturaFuerte'] + winner_group_threshold['FacturaFuerte']}/{winner_group_threshold['Total']} grupos, "
                             "con 0 errores conocidos en ambas zonas fuertes.",
                             "", "El corpus sigue siendo pequeño; la variación entre folds exige tratar el candidato con cautela en H1D9C. Los thresholds son diagnósticos OOF y no se integraron al producto."]
    Path(output / "model-comparison.md").write_text("\n".join(comparison_lines) + "\n", encoding="utf-8")

    base_weights = {name: base_weight_info(name) for name in MODEL_SPECS}
    manifest = {
        "candidate_id": "H1D9B-CANDIDATE-001" if winner else None, "status": "APROBADO COMO CANDIDATO" if winner else "NO APROBADO",
        "architecture": winner, "class_mapping": CLASS_TO_INDEX, "preprocessing": CONFIG["preprocessing"], "training_configuration": CONFIG,
        "dataset_sha256": DATASET_SHA, "frozen_test_groups_sha256": FROZEN_SHA, "development_manifest_sha256": development_hash,
        "base_weights": base_weights, "versions": environment, "oof_metrics": comparison, "thresholds": threshold_rows,
        "test_used": False, "test_scores_generated": False,
    }
    if candidate:
        manifest["candidate"] = {"onnx": {"file": candidate["onnx"].name, "sha256": sha256(candidate["onnx"]), "bytes": candidate["onnx"].stat().st_size, "external_data": False},
                                 "checkpoint": {"file": candidate["checkpoint"].name, "sha256": sha256(candidate["checkpoint"]), "bytes": candidate["checkpoint"].stat().st_size},
                                 "phase1_epochs": candidate["phase1_epochs"], "phase2_epochs": candidate["phase2_epochs"],
                                 "pytorch_onnx_max_abs_error": candidate["max_abs_error"], "ort_cpu_mean_ms": candidate["ort_cpu_mean_ms"], "ort_cpu_p95_ms": candidate["ort_cpu_p95_ms"]}
    Path(output / "candidate-manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    summary = ["# H1D9B — Benchmark visual FACTURA vs NO_FACTURA", "", f"**{status}**", "",
               "- Assets: 70/70 de desarrollo; TEST congelado no rasterizado ni utilizado por el entrenador.", "- Folds: 5, estratificados por clase y agrupados por GroupId; leakage GroupId/SHA/TEST = 0.",
               f"- Ganador: {winner or 'ninguno'}."]
    for name, metrics in comparison.items():
        group = metrics["group"]
        summary.append(f"- {name} group-level: recall FACTURA {fmt(group['recall_factura'])}, balanced accuracy {fmt(group['balanced_accuracy'])}, ROC-AUC {fmt(group['roc_auc'])} y PR-AUC {fmt(group['pr_auc'])}.")
    if winner and candidate:
        file_threshold = next(x for x in threshold_rows if x["Model"] == winner and x["Level"] == "FILE")
        group_threshold = next(x for x in threshold_rows if x["Model"] == winner and x["Level"] == "GROUP")
        onnx_hash = sha256(candidate["onnx"])
        checkpoint_hash = sha256(candidate["checkpoint"])
        summary += [f"- Zonas fuertes {winner}: `T_NO_FACTURA={file_threshold['TNoFactura']:.10f}`, `T_FACTURA={file_threshold['TFactura']:.10f}`; "
                    f"cobertura {file_threshold['NoFacturaFuerte'] + file_threshold['FacturaFuerte']}/{file_threshold['Total']} archivos y "
                    f"{group_threshold['NoFacturaFuerte'] + group_threshold['FacturaFuerte']}/{group_threshold['Total']} grupos, con 0 errores conocidos.",
                    f"- Candidato final: `H1D9B-CANDIDATE-001`, {winner}, {candidate['phase1_epochs']} épocas de head y {candidate['phase2_epochs']} de ajuste del último bloque.",
                    f"- ONNX autocontenido: {candidate['onnx'].stat().st_size} bytes; SHA-256 `{onnx_hash}`; `external_data=false`.",
                    f"- Checkpoint entrenable: {candidate['checkpoint'].stat().st_size} bytes; SHA-256 `{checkpoint_hash}`.",
                    f"- Paridad PyTorch/ONNX Runtime: error absoluto máximo {candidate['max_abs_error']:.10f} sobre imágenes de desarrollo.",
                    f"- ONNX Runtime CPU: media {candidate['ort_cpu_mean_ms']:.3f} ms y P95 {candidate['ort_cpu_p95_ms']:.3f} ms."]
    summary += ["- TEST no utilizado y sin scores generados.", "- Producto WebForms, H1D8B, SQL, Gmail, OCR, QR y clasificación productiva no fueron modificados.", "- H1D9C no fue ejecutado."]
    Path(output / "resumen.md").write_text("\n".join(summary) + "\n", encoding="utf-8")
    print("H1D9B | " + ("APROBADO COMO CANDIDATO | Winner=" + winner if winner else "NO APROBADO"), flush=True)


if __name__ == "__main__":
    main()
