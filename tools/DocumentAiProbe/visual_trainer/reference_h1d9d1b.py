import argparse
import csv
import gzip
import hashlib
from collections import Counter
from pathlib import Path

from PIL import Image


def read_csv(path):
    with open(path, newline="", encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--development-assets", required=True)
    parser.add_argument("--holdout-assets", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    output = Path(args.output).resolve()
    temporary = output / "python-source-rgb.tmp"
    output.mkdir(parents=True, exist_ok=True)
    temporary.mkdir(exist_ok=False)
    rows = []
    assets = [("DEVELOPMENT", r) for r in read_csv(args.development_assets)] + [("HOLDOUT", r) for r in read_csv(args.holdout_assets)]
    for cohort, asset in assets:
        with Image.open(asset["VisualAssetPath"]) as image:
            rgb = image.convert("RGB").tobytes()
            info = image.info
            rows.append({"Sha256": asset["Sha256"].upper(), "Cohort": cohort, "Width": image.width, "Height": image.height,
                         "PillowMode": image.mode, "PillowBands": "|".join(image.getbands()),
                         "HasAlpha": str("A" in image.getbands()).lower(), "HasPalette": str(image.palette is not None).lower(),
                         "HasIcc": str(bool(info.get("icc_profile"))).lower(), "HasGamma": str("gamma" in info).lower(),
                         "SourceRgbSha256": hashlib.sha256(rgb).hexdigest().upper()})
        with gzip.open(temporary / (asset["Sha256"].upper() + ".rgb.gz"), "wb", compresslevel=6) as stream:
            stream.write(rgb)
    fields = list(rows[0])
    with open(output / "python-source-reference.csv", "w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields, lineterminator="\n")
        writer.writeheader(); writer.writerows(rows)
    print("H1D9D1B_PYTHON | Rows=80 | Modes=" + repr(dict(Counter(r["PillowMode"] for r in rows))))


if __name__ == "__main__":
    main()
