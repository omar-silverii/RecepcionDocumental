#!/usr/bin/env python3
"""H1D10C2 - Export targeted unresolved perception cases.

Diagnostic only. Reads H1D10C/local-results/cases.csv and packages only:
- NO_EXPECTED_CUE_IN_SAVED_TEXT
- CUE_PRESENT_OUTSIDE_HEADER_OR_HEADER_OCR_MISSED

For each case it exports the source image plus the already-generated H1D10A
header/selected/native texts. It does not OCR, rasterize, train, score models,
change labels, or modify production artifacts.
"""
from __future__ import annotations

import csv
import shutil
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXPERIMENTS = HERE.parent
H1D10B = EXPERIMENTS / "H1D10B"
INPUT = HERE / "local-results" / "cases.csv"
OUTROOT = HERE / "local-results" / "H1D10C2-unresolved"
ZIPOUT = HERE / "local-results" / "H1D10C2-unresolved.zip"

TARGET_CATEGORIES = {
    "NO_EXPECTED_CUE_IN_SAVED_TEXT",
    "CUE_PRESENT_OUTSIDE_HEADER_OR_HEADER_OCR_MISSED",
}

def read_csv(path: Path):
    with path.open("r", encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))

def copy_text(src_value: str, dst: Path):
    if not src_value:
        dst.write_text("[NO PATH]\n", encoding="utf-8")
        return False
    src = Path(src_value)
    if not src.exists():
        dst.write_text("[MISSING] " + str(src) + "\n", encoding="utf-8")
        return False
    dst.write_text(src.read_text(encoding="utf-8-sig", errors="replace"), encoding="utf-8")
    return True

def main():
    if not INPUT.exists():
        raise SystemExit(f"Missing H1D10C result: {INPUT}")

    trace_path = H1D10B / "b3-blind-traceability.csv"
    if not trace_path.exists():
        raise SystemExit(f"Missing traceability file: {trace_path}")

    rows = [r for r in read_csv(INPUT) if r.get("DiagnosticCategory") in TARGET_CATEGORIES]
    trace = {r["CandidateId"]: r for r in read_csv(trace_path)}

    if OUTROOT.exists():
        shutil.rmtree(OUTROOT)
    OUTROOT.mkdir(parents=True)

    manifest_rows = []
    image_ok = header_ok = selected_ok = native_ok = 0

    for r in rows:
        cid = r["CandidateId"]
        cdir = OUTROOT / cid
        cdir.mkdir()

        tr = trace.get(cid, {})
        source_image = Path(tr.get("SourceAsset", "")) if tr.get("SourceAsset") else None
        image_name = "document.png"
        image_exists = bool(source_image and source_image.exists())
        if image_exists:
            shutil.copy2(source_image, cdir / image_name)
            image_ok += 1

        h_ok = copy_text(r.get("HeaderTextPathResolved", ""), cdir / "header.txt")
        s_ok = copy_text(r.get("TextPathResolved", ""), cdir / "selected-text.txt")
        n_ok = copy_text(r.get("NativeTextPathResolved", ""), cdir / "native.txt")
        header_ok += int(h_ok)
        selected_ok += int(s_ok)
        native_ok += int(n_ok)

        info = [
            f"CandidateId: {cid}",
            f"Sha256: {r.get('Sha256','')}",
            f"FileName: {r.get('FileName','')}",
            f"VisualReferenceType: {r.get('VisualReferenceType','')}",
            f"VisualReferenceLabel: {r.get('VisualReferenceLabel','')}",
            f"VisualObservation: {r.get('VisualObservation','')}",
            f"DiagnosticCategory: {r.get('DiagnosticCategory','')}",
            f"TextMethod: {r.get('TextMethod','')}",
            f"NativeUseful: {r.get('NativeUseful','')}",
            f"HeaderOcrConfidence: {r.get('HeaderOcrConfidence','')}",
            f"TextCharactersManifest: {r.get('TextCharactersManifest','')}",
            f"FamilyId: {r.get('FamilyId','')}",
            f"FamilyTrainingCount: {r.get('FamilyTrainingCount','')}",
            f"QualityStatus: {r.get('QualityStatus','')}",
            f"SourceImage: {source_image or ''}",
            f"ImageExported: {image_exists}",
        ]
        (cdir / "info.txt").write_text("\n".join(info) + "\n", encoding="utf-8")

        manifest_rows.append({
            "CandidateId": cid,
            "Sha256": r.get("Sha256",""),
            "VisualReferenceType": r.get("VisualReferenceType",""),
            "VisualObservation": r.get("VisualObservation",""),
            "DiagnosticCategory": r.get("DiagnosticCategory",""),
            "TextMethod": r.get("TextMethod",""),
            "NativeUseful": r.get("NativeUseful",""),
            "HeaderOcrConfidence": r.get("HeaderOcrConfidence",""),
            "ImageExported": str(image_exists),
            "HeaderExported": str(h_ok),
            "SelectedTextExported": str(s_ok),
            "NativeTextExported": str(n_ok),
        })

    fields = list(manifest_rows[0].keys()) if manifest_rows else []
    with (OUTROOT / "manifest.csv").open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        w.writerows(manifest_rows)

    summary = (
        "H1D10C2 - paquete diagnostico no productivo\n"
        f"Casos: {len(rows)}\n"
        f"Imagenes exportadas: {image_ok}/{len(rows)}\n"
        f"Header texts: {header_ok}/{len(rows)}\n"
        f"Selected texts: {selected_ok}/{len(rows)}\n"
        f"Native texts: {native_ok}/{len(rows)}\n"
        "No entrena, no modifica labels y no toca holdout.\n"
    )
    (OUTROOT / "README.txt").write_text(summary, encoding="utf-8")

    if ZIPOUT.exists():
        ZIPOUT.unlink()
    with zipfile.ZipFile(ZIPOUT, "w", compression=zipfile.ZIP_DEFLATED) as z:
        for p in sorted(OUTROOT.rglob("*")):
            if p.is_file():
                z.write(p, p.relative_to(OUTROOT))

    print(summary)
    print(f"ZIP: {ZIPOUT}")
    print("Subi H1D10C2-unresolved.zip a ChatGPT para la inspeccion visual/textual.")

if __name__ == "__main__":
    main()
