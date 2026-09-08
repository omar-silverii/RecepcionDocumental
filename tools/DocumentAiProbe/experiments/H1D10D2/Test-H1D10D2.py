import csv
import importlib.util
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent


def load_d2():
    path = HERE / "Run-H1D10D2.py"
    spec = importlib.util.spec_from_file_location("h1d10d2", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


D2 = load_d2()
D1, D1B, D1C = D2.load_dependencies()


class D2Tests(unittest.TestCase):
    def test_top150_contract(self):
        rows = D2.read_csv(D2.TOP150_PATH)
        self.assertEqual(150, len(rows))
        self.assertEqual(150, len({row["Sha256"] for row in rows}))
        self.assertEqual(150, len({row["CandidateId"] for row in rows}))

    def test_top150_excludes_sealed_before_text(self):
        bank = {row["Sha256"]: row for row in D2.read_csv(D2.BANK_PATH)}
        top = D2.read_csv(D2.TOP150_PATH)
        self.assertFalse(any(bank[row["Sha256"]].get("Split") == "SEALED_TEST" for row in top))

    def test_text_source_rejects_sealed(self):
        source = D2.TextSource(D2.TEXT_ZIP_PATH)
        try:
            with self.assertRaises(RuntimeError):
                source.read({"Split": "SEALED_TEST", "HeaderTextAsset": "x.header.txt"}, "HeaderTextAsset")
        finally:
            source.close()

    def test_specific_title_precedence(self):
        row = {"HeaderOcrConfidence": "0.95", "QrEvidence": "[]"}
        decision, reason, *_ = D2.resolve_current(
            D1, D1B, D1C, row,
            "NOTA D E C R É D I T O\nFACTURA ASOCIADA 0001-123"
        )
        self.assertEqual("OTRO_DOCUMENTO", decision)
        self.assertEqual("SPECIFIC_TITLE_PRECEDENCE", reason)

    def test_strong_factura_kept(self):
        row = {"HeaderOcrConfidence": "0.95", "QrEvidence": "[]"}
        decision, reason, *_ = D2.resolve_current(
            D1, D1B, D1C, row,
            "FACTURA A N° 0001-00001234"
        )
        self.assertEqual("FACTURA", decision)
        self.assertEqual("FACTURA_TITLE_ONLY_STRONG_POSITIONAL", reason)

    def test_secondary_factura_abstains(self):
        row = {"HeaderOcrConfidence": "0.95", "QrEvidence": "[]"}
        decision, reason, *_ = D2.resolve_current(
            D1, D1B, D1C, row,
            "Asunto: RV: FACTURA ALQUILER MES 1 + NC"
        )
        self.assertEqual("", decision)
        self.assertEqual("NO_DECISION_SECONDARY_FACTURA", reason)

    def test_visual_group_does_not_return_decision(self):
        group, semantic_need, gap = D2.diagnostic_residual_group(
            "TRANSFERENCIA_ANTICIPO", "Solicitud de transferencia"
        )
        self.assertEqual("SEMANTIC_TRANSFERENCIA_ANTICIPO", group)
        self.assertEqual("LIKELY_SEMANTIC_LANGUAGE_CONTEXT", semantic_need)
        self.assertTrue(gap)


if __name__ == "__main__":
    unittest.main()
