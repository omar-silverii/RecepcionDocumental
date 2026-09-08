import unittest
from Run_H1D10D1_import import canonical, detect_title_candidates

class CanonicalTitleTests(unittest.TestCase):
    def types(self, s, conf=.95):
        return {x["DocumentType"] for x in detect_title_candidates(s, conf)}

    def test_spaced_ocr_debito(self):
        self.assertEqual(canonical("NOTA D E D É B ITO"), "NOTADEDEBITO")
        self.assertEqual(self.types("A | NOTA D E D É B ITO N° 0001-2"), {"NOTA_DEBITO"})

    def test_spaced_ocr_credito(self):
        self.assertEqual(self.types("NOTA D E C R É D I T O N° 12"), {"NOTA_CREDITO"})

    def test_factura(self):
        self.assertEqual(self.types('"A" FACTURA N° A-0018-00355841'), {"FACTURA"})

    def test_reference_not_invoice_title(self):
        for s in ("PAGO DE FACTURA", "REFERENCIA FACTURA", "FACTURA ASOCIADA 0001-123", "NO FACTURA"):
            self.assertEqual(self.types(s), set())

    def test_specific_wins(self):
        self.assertEqual(self.types("NOTA DE CREDITO - factura asociada 0001-123"), {"NOTA_CREDITO"})

    def test_low_confidence(self):
        self.assertEqual(self.types("FACTURA", .79), set())

if __name__ == "__main__":
    unittest.main()
