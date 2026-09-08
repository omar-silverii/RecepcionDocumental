import importlib.util
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent


def load_d3():
    path = HERE / "Run-H1D10D3.py"
    spec = importlib.util.spec_from_file_location("h1d10d3", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


D3 = load_d3()
D1 = D3.load_d1()


class D3Tests(unittest.TestCase):
    def manifest(self, confidence="0.90"):
        return {"HeaderOcrConfidence": confidence, "Split": "DEV"}

    def test_code_03_with_fiscal_structure_is_other(self):
        header = """NOTA CRED\nCodigo 03\nCUIT 30-12345678-9\nIVA Responsable Inscripto"""
        decision, reason, *_ = D3.propose_fusion(D1, self.manifest(), header, "")
        self.assertEqual("OTRO_DOCUMENTO", decision)
        self.assertEqual("FISCAL_CODE_03_WITH_STRUCTURE", reason)

    def test_code_03_without_structure_abstains(self):
        decision, reason, *_ = D3.propose_fusion(
            D1, self.manifest(), "Codigo 03", ""
        )
        self.assertEqual("", decision)
        self.assertEqual("", reason)

    def test_credit_invoice_title_with_number_and_structure_is_factura(self):
        header = """FACTURA DE CREDITO\nN° 00004-00000631\nCUIT: 30-12345678-9\nIVA RESPONSABLE INSCRIPTO"""
        decision, reason, *_ = D3.propose_fusion(D1, self.manifest(), header, "")
        self.assertEqual("FACTURA", decision)
        self.assertEqual("FISCAL_CREDIT_INVOICE_TITLE_WITH_STRUCTURE", reason)

    def test_known_false_secondary_credit_phrase_still_abstains(self):
        header = """INVESTIGACIÓN N° C00005-00000073\nINVESTIGACIONES\nECONOMICAS FACTURA DE CREDITO\nCUIT 30-54010330-1\nIVA EXENTO"""
        decision, reason, *_ = D3.propose_fusion(D1, self.manifest(), header, "")
        self.assertEqual("", decision)
        self.assertEqual("", reason)

    def test_email_factura_reference_still_abstains(self):
        header = """Para: Proveedores\nAsunto: RV FACTURA 0001-00001234\nDatos adjuntos: factura.pdf\nCUIT 30-12345678-9\nIVA Responsable Inscripto\nOriginal\nFecha de Emisión: 17/01/2025"""
        decision, reason, *_ = D3.propose_fusion(D1, self.manifest(), header, "")
        self.assertEqual("", decision)
        self.assertEqual("", reason)

    def test_structured_factura_number_needs_multiple_anchors(self):
        strong = """Proveedor SA Factura 3100-00115246\nIVA RESPONSABLE INSCRIPTO\nFecha de emisión: 06/01/2025\nCUIT 30-12345678-9\nORIGINAL\nIngresos Brutos 123"""
        decision, reason, *_ = D3.propose_fusion(D1, self.manifest(), strong, "")
        self.assertEqual("FACTURA", decision)
        self.assertEqual("STRUCTURED_FACTURA_NUMBER_WITH_FISCAL_ANCHORS", reason)

        weak = "Proveedor SA Factura 3100-00115246\nCUIT 30-12345678-9"
        decision, reason, *_ = D3.propose_fusion(D1, self.manifest(), weak, "")
        self.assertEqual("", decision)
        self.assertEqual("", reason)

    def test_low_conf_title_can_be_recovered_by_fusion(self):
        header = """FACTURA DE CRÉDITO\nELECTRONICA MIPYMES\nFactura n° A00007-00000262\nCUIT 30-50000661-3\nIVA Responsable Inscripto"""
        native = "ORIGINAL A00007-00000262 CAE: 74511586023526"
        decision, reason, *_ = D3.propose_fusion(
            D1, self.manifest("0.73"), header, native
        )
        self.assertEqual("FACTURA", decision)
        self.assertEqual("FISCAL_CREDIT_INVOICE_TITLE_WITH_STRUCTURE", reason)

    def test_text_source_rejects_sealed(self):
        source = D3.TextSource(D3.TEXT_ZIP_PATH)
        try:
            with self.assertRaises(RuntimeError):
                source.read(
                    {"Split": "SEALED_TEST", "HeaderTextAsset": "x.header.txt"},
                    "HeaderTextAsset",
                )
        finally:
            source.close()

    def test_real_input_contract(self):
        _, bank_by_sha, baseline, top150 = D3.load_inputs()
        self.assertEqual(734, len(baseline))
        self.assertEqual(150, len(top150))
        shas = {r["Sha256"] for r in baseline} | {r["Sha256"] for r in top150}
        self.assertFalse(any(bank_by_sha[s].get("Split") == "SEALED_TEST" for s in shas))


if __name__ == "__main__":
    unittest.main()
