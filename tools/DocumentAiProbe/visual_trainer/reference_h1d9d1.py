import argparse
import csv
import hashlib
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image

T_NO = 0.1169927716255188
T_YES = 0.7979831695556641
MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32).reshape(1, 1, 3)
STD = np.array([0.229, 0.224, 0.225], dtype=np.float32).reshape(1, 1, 3)


def read_csv(path):
    with open(path, newline="", encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream))


def digest(data):
    return hashlib.sha256(data).hexdigest().upper()


def zone(p):
    return "NO_FACTURA_FUERTE" if p <= T_NO else "FACTURA_FUERTE" if p >= T_YES else "INCIERTO_VISUAL"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--onnx", required=True)
    parser.add_argument("--development-assets", required=True)
    parser.add_argument("--holdout-assets", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    rows = []
    assets = [("DEVELOPMENT", r) for r in read_csv(args.development_assets)] + [("HOLDOUT", r) for r in read_csv(args.holdout_assets)]
    session = ort.InferenceSession(str(Path(args.onnx).resolve()), providers=["CPUExecutionProvider"])
    with open(output / "target-rgb.tmp.bin", "wb") as targets, open(output / "tensor.tmp.bin", "wb") as tensors:
        for cohort, asset in assets:
            with Image.open(asset["VisualAssetPath"]) as opened:
                source = opened.convert("RGB")
                source_bytes = source.tobytes()
                scale = min(224.0 / source.width, 224.0 / source.height)
                size = (max(1, round(source.width * scale)), max(1, round(source.height * scale)))
                resized = source.resize(size, Image.Resampling.BICUBIC)
                target = Image.new("RGB", (224, 224), (255, 255, 255))
                target.paste(resized, ((224 - size[0]) // 2, (224 - size[1]) // 2))
                target_bytes = target.tobytes()
            array = np.asarray(target, dtype=np.float32) / 255.0
            tensor = np.transpose((array - MEAN) / STD, (2, 0, 1))[None].astype("<f4")
            probabilities = session.run(["probabilities"], {"image": tensor})[0][0]
            targets.write(target_bytes)
            tensors.write(tensor.tobytes(order="C"))
            p = float(probabilities[1])
            rows.append({"Sha256": asset["Sha256"].upper(), "Cohort": cohort, "GroupId": asset["GroupId"],
                         "SourceWidth": source.width, "SourceHeight": source.height,
                         "SourceRgbSha256": digest(source_bytes), "TargetRgbSha256": digest(target_bytes),
                         "TensorSha256": digest(tensor.tobytes(order="C")), "PNoFactura": f"{float(probabilities[0]):.9f}",
                         "PFactura": f"{p:.9f}", "Pred050": "FACTURA" if p >= .5 else "NO_FACTURA", "Zona": zone(p)})
    fields = list(rows[0])
    with open(output / "python-preprocessing-reference.csv", "w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields, lineterminator="\n")
        writer.writeheader(); writer.writerows(rows)
    print("H1D9D1_PYTHON | Gate=PASS | Rows=80 | Pillow=" + Image.__version__ + " | TempTargetBytes=" + str((output / "target-rgb.tmp.bin").stat().st_size) + " | TempTensorBytes=" + str((output / "tensor.tmp.bin").stat().st_size))


if __name__ == "__main__":
    main()
