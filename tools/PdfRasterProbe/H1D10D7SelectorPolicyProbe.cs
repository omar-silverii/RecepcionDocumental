using System;
using System.Collections.Generic;
using RecepcionDocumental.Services;

namespace PdfRasterProbe
{
    internal static class H1D10D7SelectorPolicyProbe
    {
        internal static int Run(string[] args)
        {
            var tests = new List<TestCase>
            {
                new TestCase("FACTURA_con_OC_secundaria", "FACTURA A CUIT 30-12345678-9 ORDEN DE COMPRA No. 029164 TOTAL 1000", "FACTURA A\n00001-00012345", 0.95f, "FACTURA"),
                new TestCase("FACTURA_con_RECIBO_secundario", "FACTURA A CUIT 30-12345678-9 IVA TOTAL 1000 factura y recibo simultaneamente", "FACTURA A\n00002-00012345", 0.95f, "FACTURA"),
                new TestCase("FACTURA_con_REMITO_secundario", "FACTURA A CUIT 30-12345678-9 IVA TOTAL 1000 Remito 123", "FACTURA A\n00003-00012345", 0.95f, "FACTURA"),
                new TestCase("FACTURA_con_ND_secundaria", "FACTURA A CUIT 30-12345678-9 IVA TOTAL 1000 una diferencia podra originar nota de debito", "FACTURA A\n00004-00012345", 0.95f, "FACTURA"),
                new TestCase("RECIBO_real_no_promueve", "RECIBO DE PAGO CUIT 30-12345678-9 TOTAL 1000", "RECIBO DE PAGO\n00005", 0.95f, "DESCARTAR"),
                new TestCase("NC_real_no_promueve", "NOTA DE CREDITO A CUIT 30-12345678-9 TOTAL 1000", "NOTA DE CREDITO A\n00006-00012345", 0.95f, "DESCARTAR"),
                new TestCase("Mencion_pago_factura_no_promueve", "PAGO DE FACTURA CUIT 30-12345678-9 TOTAL 1000", "PAGO DE FACTURA\nReferencia 123", 0.95f, "REVISAR"),
                new TestCase("Header_baja_confianza_no_promueve", "ORDEN DE COMPRA factura A CUIT 30-12345678-9 TOTAL 1000", "FACTURA A\n00007-00012345", 0.60f, "DESCARTAR")
            };

            var failed = 0;
            foreach (var test in tests)
            {
                var actual = InvoiceSelector.SelectOcrText(test.Text, true, test.Header, test.HeaderConfidence);
                var pass = string.Equals(actual.Classification, test.Expected, StringComparison.Ordinal);
                if (!pass) failed++;
                Console.WriteLine((pass ? "PASS" : "FAIL") + " | " + test.Name + " | Expected=" + test.Expected + " | Actual=" + actual.Classification + " | Method=" + actual.DetectionMethod + " | Reason=" + actual.Reason);
            }
            Console.WriteLine("H1D10D7_SELECTOR_POLICY | " + (failed == 0 ? "APROBADO" : "NO_APROBADO") + " | Tests=" + tests.Count + " | Fallas=" + failed);
            return failed == 0 ? 0 : 1;
        }

        private sealed class TestCase
        {
            internal TestCase(string name, string text, string header, float headerConfidence, string expected)
            { Name = name; Text = text; Header = header; HeaderConfidence = headerConfidence; Expected = expected; }
            internal string Name { get; private set; }
            internal string Text { get; private set; }
            internal string Header { get; private set; }
            internal float HeaderConfidence { get; private set; }
            internal string Expected { get; private set; }
        }
    }
}
