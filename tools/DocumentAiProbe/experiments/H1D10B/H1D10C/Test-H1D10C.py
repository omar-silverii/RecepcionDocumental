import importlib.util
import pathlib
import unittest

HERE = pathlib.Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("h1d10c", HERE / "Run-H1D10C.py")
MOD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MOD)

class H1D10CTests(unittest.TestCase):
    def test_strict_rule_reproduces_exact_title(self):
        self.assertEqual(MOD.strict_title_evidence("FACTURA ELECTRONICA A", .95), ["FACTURA"])

    def test_multiline_credit_note_is_not_strict_but_has_flattened_cue(self):
        text = "NOTA\nDE CREDITO\nA 0001-00000001"
        self.assertEqual(MOD.strict_title_evidence(text, .95), [])
        cue, _, _ = MOD.find_cue("NOTA_CREDITO", text)
        self.assertEqual(cue, "NOTA DE CREDITO")

    def test_low_confidence_stays_out_of_strict_gate(self):
        self.assertEqual(MOD.strict_title_evidence("FACTURA", .79), [])

    def test_generic_other_is_semantic_not_forced_to_title(self):
        self.assertEqual(
            MOD.classify("OTRO_DOCUMENTO", [], "", "", "", .95, True, True),
            "SEMANTIC_OR_NON_TITLE_DOCUMENT",
        )

    def test_header_cue_rejected_is_diagnostic_only(self):
        category = MOD.classify("NOTA_DEBITO", [], "NOTA DE DEBITO", "", "", .92, True, True)
        self.assertEqual(category, "HEADER_CUE_PRESENT_RULE_REJECTED")

if __name__ == "__main__":
    unittest.main()
