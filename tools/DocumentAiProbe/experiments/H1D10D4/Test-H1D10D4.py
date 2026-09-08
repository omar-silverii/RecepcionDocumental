#!/usr/bin/env python3
import csv
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent


def read_csv(name):
    with (HERE / name).open("r", encoding="utf-8-sig", newline="") as h:
        return list(csv.DictReader(h))


def main():
    summary = json.loads((HERE / "summary.json").read_text(encoding="utf-8"))
    indep = read_csv("independent-fallback-audit-d4.csv")
    thresholds = read_csv("independent-low-pfactura-thresholds-d4.csv")
    top = read_csv("top150-residual-score-audit-d4.csv")
    fam = read_csv("family-concentration-d4.csv")

    tests = []
    tests.append(("gates", not summary["SealedAssetsRead"] and not summary["TrainingExecuted"] and not summary["ProductionModified"] and not summary["GroundTruthModified"] and not summary["ModelsPromoted"] and not summary["NewModelInferenceExecuted"]))
    tests.append(("independent-734", len(indep) == 734 and summary["IndependentRows"] == 734))
    abst = [r for r in indep if r["WasD3Abstention"].lower() == "true"]
    tests.append(("six-d3-abstentions", len(abst) == 6 and summary["D3IndependentAbstentions"] == 6))
    tests.append(("all-six-expected-otro", all(r["Expected"] == "OTRO_DOCUMENTO" for r in abst)))
    tests.append(("text05-fails-all-six", all(r["TextOnlyPrediction05"] == "FACTURA" for r in abst) and summary["NaiveD3PlusText05Wrong"] == 6))
    tests.append(("header05-fails-all-six", all(r["HeaderTextPrediction05"] == "FACTURA" for r in abst)))
    tests.append(("residual-86", len(top) == 86 and summary["ResidualTop150Rows"] == 86))
    lang = [r for r in top if r["SemanticNeed"] == "LIKELY_SEMANTIC_LANGUAGE_CONTEXT"]
    tests.append(("language-62", len(lang) == 62 and all(float(r["TextOnlyPFactura"]) < 0.5 for r in lang)))
    zone = [r for r in top if r["LowPFacturaCandidateZone035"].lower() == "true"]
    tests.append(("candidate-zone-56", len(zone) == 56 and summary["LowPFacturaCandidateZone035Rows"] == 56))
    tests.append(("families-46", len(fam) == 46 and summary["ResidualTop150Families"] == 46))
    tests.append(("threshold-audit-present", len(thresholds) == 6))

    failed = [name for name, ok in tests if not ok]
    for name, ok in tests:
        print(f"{'OK' if ok else 'FAIL'} - {name}")
    if failed:
        raise SystemExit("Failed: " + ", ".join(failed))
    print(f"TOTAL: {len(tests)} OK / 0 FAIL")


if __name__ == "__main__":
    main()
