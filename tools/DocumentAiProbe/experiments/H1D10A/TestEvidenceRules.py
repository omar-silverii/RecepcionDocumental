"""Regression of conservative title semantics, independent of bank/holdout labels."""
import unittest
from FinalizeBank import title_evidence, issuer
class TitleRules(unittest.TestCase):
    def types(self,s,confidence=.95):return {x['DocumentType'] for x in title_evidence(s,confidence)}
    def test_invoice_title(self):
        self.assertEqual(self.types('FACTURA ELECTRONICA A'),{'FACTURA'})
        self.assertEqual(self.types('FACTURA DE CREDITO ELECTRONICA MIPYMES'),{'FACTURA'})
    def test_associated_invoice_is_not_a_title(self):
        self.assertEqual(self.types('NOTA DE CREDITO\nFactura asociada 0001-123'),{'NOTA_CREDITO'})
        for line in ['PAGO DE FACTURA','REFERENCIA FACTURA A','FACTURA ASOCIADA 100','La presente factura corresponde al pago','NO FACTURA']:
            self.assertEqual(self.types(line),set())
    def test_ambiguity_preserved(self):
        self.assertEqual(self.types('FACTURA\nNOTA DE CREDITO'),{'FACTURA','NOTA_CREDITO'})
    def test_low_confidence(self):self.assertEqual(self.types('FACTURA',.79),set())
    def test_hard_negatives(self):
        for line,label in [('RECIBO','RECIBO'),('SOLICITUD DE ANTICIPO','TRANSFERENCIA_ANTICIPO'),('NOTA DE DEBITO','NOTA_DEBITO'),('BOLETA DE DEPOSITO','COMPROBANTE_BANCARIO'),('IMPUESTO AUTOMOTOR','IMPUESTO_BOLETA')]:
            self.assertEqual(self.types(line),{label})
    def test_receiver_not_emitter(self):
        self.assertEqual(issuer('CLIENTE\nCUIT: 30-70906754-6'),'')
    def test_no_invented_type(self):self.assertEqual(self.types('DOCUMENTACION VARIA'),set())
if __name__=='__main__':unittest.main()
