import argparse
import csv
import hashlib
import json
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
T_NO_FACTURA = 0.1169927716255188
T_FACTURA = 0.7979831695556641
MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32).reshape(1, 1, 3)
STD = np.array([0.229, 0.224, 0.225], dtype=np.float32).reshape(1, 1, 3)


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


def write_csv(path, rows):
    fields = ["Sha256", "Cohort", "GroupId", "PNoFactura", "PFactura", "Pred050", "ZonaPreRegistrada"]
    with open(path, "w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields, lineterminator="\n")
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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", required=True)
    parser.add_argument("--frozen", required=True)
    parser.add_argument("--fold-manifest", required=True)
    parser.add_argument("--candidate-manifest", required=True)
    parser.add_argument("--onnx", required=True)
    parser.add_argument("--checkpoint", required=True)
    parser.add_argument("--development-assets", required=True)
    parser.add_argument("--holdout-assets", required=True)
    parser.add_argument("--holdout-predictions", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    for key, path in (("dataset", args.dataset), ("frozen", args.frozen), ("fold", args.fold_manifest),
                      ("onnx", args.onnx), ("checkpoint", args.checkpoint)):
        require_hash(path, EXPECTED[key], Path(path).name)
    if Path(args.onnx).stat().st_size != EXPECTED_ONNX_BYTES:
        raise RuntimeError("Tamaño inesperado de candidate.onnx")

    candidate = json.loads(Path(args.candidate_manifest).read_text(encoding="utf-8"))
    if candidate["architecture"] != "EfficientNet-B0" or candidate["class_mapping"] != {"NO_FACTURA": 0, "FACTURA": 1}:
        raise RuntimeError("Identidad o class mapping del candidato inesperado")
    if candidate["development_manifest_sha256"] != EXPECTED["fold"]:
        raise RuntimeError("Hash del development manifest inconsistente")
    if candidate["candidate"]["onnx"].get("external_data") is not False:
        raise RuntimeError("El candidato ONNX no declara external_data=false")

    rows = []
    seen = set()
    cohorts = (("DEVELOPMENT", read_csv(args.development_assets), 70),
               ("HOLDOUT", read_csv(args.holdout_assets), 10))
    session = ort.InferenceSession(str(Path(args.onnx).resolve()), providers=["CPUExecutionProvider"])
    if session.get_providers() != ["CPUExecutionProvider"]:
        raise RuntimeError("ONNX Runtime no quedó limitado a CPUExecutionProvider")
    model_input, model_output = session.get_inputs()[0], session.get_outputs()[0]
    if (model_input.name, model_input.shape, model_output.name, model_output.shape) != \
            ("image", [1, 3, 224, 224], "probabilities", [1, 2]):
        raise RuntimeError("Metadata ONNX inesperada")

    for cohort, assets, expected_count in cohorts:
        if len(assets) != expected_count:
            raise RuntimeError(f"Cantidad inesperada en {cohort}: {len(assets)}")
        for asset in assets:
            digest = asset["Sha256"].upper()
            if digest in seen:
                raise RuntimeError(f"SHA repetido entre cohorts: {digest}")
            seen.add(digest)
            path = Path(asset["VisualAssetPath"])
            if not path.is_file() or path.name.upper() != digest + ".PNG":
                raise RuntimeError(f"Asset ausente o no canónico: {digest}")
            probabilities = session.run(["probabilities"], {"image": preprocess(path)})[0][0]
            p_no, p_yes = float(probabilities[0]), float(probabilities[1])
            if abs((p_no + p_yes) - 1.0) > 1e-5:
                raise RuntimeError(f"Probabilidades no suman 1 para {digest}")
            rows.append({"Sha256": digest, "Cohort": cohort, "GroupId": asset["GroupId"],
                         "PNoFactura": f"{p_no:.9f}", "PFactura": f"{p_yes:.9f}",
                         "Pred050": "FACTURA" if p_yes >= 0.5 else "NO_FACTURA",
                         "ZonaPreRegistrada": zone(p_yes)})

    if len(rows) != 80 or len(seen) != 80:
        raise RuntimeError("El universo combinado no es 80/80 único")
    prior = {row["Sha256"].upper(): float(row["PFactura"]) for row in read_csv(args.holdout_predictions)}
    holdout = [row for row in rows if row["Cohort"] == "HOLDOUT"]
    if set(prior) != {row["Sha256"] for row in holdout}:
        raise RuntimeError("Universo holdout no coincide con test-predictions.csv")
    for row in holdout:
        delta = abs(float(row["PFactura"]) - prior[row["Sha256"]])
        if delta > 5e-9:
            raise RuntimeError(f"Score H1D9C no reproducido para {row['Sha256']}: delta={delta}")

    output = Path(args.output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    write_csv(output, rows)
    print(f"H1D9D_PYTHON | Gate=PASS | Processed=80 | UniqueSha=80 | Development=70 | Holdout=10 | Provider=CPUExecutionProvider | Output={output}")


if __name__ == "__main__":
    main()
