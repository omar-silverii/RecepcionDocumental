# H1D5B — Resumen ejecutivo

> Benchmark experimental con evidencia H1D5A congelada. No implementa H1D5C.

1. **FACTURA → DESCARTAR conservadora:** 0; se mantiene en cero.
2. **FACTURA adicionales reconocidas:** 2 (de 13 a 15).
3. **Falsos FACTURA desde OTRO_DOCUMENTO:** 0.
4. **REVISAR eliminados:** 2 (de 17 a 15).
5. **Uso de OCR:** 6/36 actual frente a 21/36 conservador; aumento de 15 PDF.
6. **Costo OCR adicional estimado:** 23908 ms acumulados H1D5A para 15 PDF adicionales.
7. **Sustitución directa insegura:** sí; produce 3 FACTURA → DESCARTAR.
8. **Orden QR C1/C2:** 0 diferencias; equivalentes en este corpus.
9. **Tres falsos descartes OCR:** REMITO — COINCIDENCIA_LITERAL. La señal aparece literalmente en el OCR dentro de un campo o referencia comercial; por sí sola no demuestra que el documento sea REMITO. También está presente en la extracción Mdoc independiente. Coexiste evidencia explícita de FACTURA.; RECIBO — COINCIDENCIA_LITERAL. La señal aparece literalmente en el OCR dentro de un campo o referencia comercial; por sí sola no demuestra que el documento sea RECIBO. También está presente en la extracción Mdoc independiente. Coexiste evidencia explícita de FACTURA.; ORDEN DE COMPRA — COINCIDENCIA_LITERAL. La señal aparece literalmente en el OCR dentro de un campo o referencia comercial; por sí sola no demuestra que el documento sea ORDEN DE COMPRA. No fue localizada en Mdoc; puede ser texto visible que Mdoc omitió o un artefacto OCR. No se localizó evidencia explícita de FACTURA en ambas fuentes.
10. **Evidencia para proponer H1D5C:** sí como candidata a validación productiva controlada, no como integración automática cerrada.
11. **Candidata:** CONSERVATIVE_FUSION, porque sólo permite promoción positiva desde REVISAR y preserva FACTURA → DESCARTAR = 0 sin falsos FACTURA en estos 36 PDF.
12. **Riesgos abiertos:** costo OCR mayor, dos PDF con límites OCR, estabilidad fuera del corpus, semántica de señales negativas y orden QR aún no diferenciable empíricamente con los siete QR actuales.
