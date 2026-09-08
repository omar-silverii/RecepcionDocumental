import importlib.util
import unittest
from pathlib import Path


HERE = Path(__file__).resolve().parent


def load_module():
    path = HERE / "Run-H1D10D1c.py"
    spec = importlib.util.spec_from_file_location("h1d10d1c", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


D1C = load_module()
D1 = D1C.load_d1()


class StrongFacturaGateTests(unittest.TestCase):
    def gate(self, header):
        candidates = D1.detect_title_candidates(header, 0.95)
        factura = next(
            (item for item in candidates if item["DocumentType"] == "FACTURA"),
            None,
        )
        if factura is None:
            return False, "NO_D1_FACTURA_CANDIDATE", ""
        return D1C.strong_factura_gate(factura, header, D1.canonical)

    def assertAccepted(self, header):
        accepted, reason, _ = self.gate(header)
        self.assertTrue(accepted, reason)

    def assertRejected(self, header):
        accepted, reason, _ = self.gate(header)
        self.assertFalse(accepted, reason)

    def test_explicit_number_same_line(self):
        self.assertAccepted("FACTURA A N° 0001-00001234")
        self.assertAccepted("FACTURA NRO 0001-00001234")
        self.assertAccepted("A FACTURA N° 0001-00001234")
        self.assertAccepted("FACTURA Ng 00007-00001955")

    def test_title_line_with_adjacent_identifier(self):
        self.assertAccepted("FACTURA\nA00004-00021367")
        self.assertAccepted("Original Factura\nA N* 0005-00076675")
        self.assertAccepted("FACTURA\nN* 0003 - 00009564")
        self.assertAccepted("FACTURA\nCod. 01 0003 00005207")

    def test_explicit_fiscal_descriptor(self):
        self.assertAccepted("FACTURA ELECTRONICA A")
        self.assertAccepted("FACTURA CRÉDITO ELECTRÓNICA")

    def test_top_position_issuer_title(self):
        self.assertAccepted(
            "- ARILLO HUGO GERMAN FACTURA\n"
            "Punto de Venta: 00004 Comp. Nro: 00002843"
        )

    def test_bare_word_without_identifier_is_not_strong(self):
        self.assertRejected("FACTURA")

    def test_secondary_mentions(self):
        self.assertRejected("ECONOMICAS FACTURA DE CREDITO")
        self.assertRejected("Asunto: RV: RV: FACTURA ALQUILER MES 1, E 10662 + NC")
        self.assertRejected("Asunto: RV: Factura alquiler Sucursal Bahia Blanca 016880 mas nc")
        self.assertRejected("PAGO DE FACTURA 0001-123")
        self.assertRejected("FACTURA ASOCIADA 0001-123")


if __name__ == "__main__":
    unittest.main()
