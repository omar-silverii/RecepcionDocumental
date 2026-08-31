# H1D5C — Resumen ejecutivo

> **HITO NO APROBADO.** La validación productiva obtuvo 35/36 PDF y 79/80 archivos, por debajo del criterio obligatorio 36/36 y 80/80.

- Producto y probe compilados con .NET Framework 4.8.
- PDF coincidentes con H1D5B: **35/36**; corpus: **79/80**.
- Matriz PDF FACTURA: 14 FACTURA, 8 REVISAR, 0 DESCARTAR.
- Matriz PDF OTRO_DOCUMENTO: 0 FACTURA, 8 REVISAR, 6 DESCARTAR.
- Promociones esperadas: 2/2; conflictos conservados en REVISAR con método de conflicto: 2/3.
- Tabla de verdad: 10/10 PASS, incluido Mdoc sin texto + OCR DESCARTAR.
- OCR activado en 21/36 PDF según instrumentación real.
- Tiempo PDF total/media/mediana/P95: 177951 / 4943.08 / 88.5 / 4376.75 ms.
- La medición corresponde a esta computadora y corpus; no constituye capacidad productiva final ni despliegue.

## Bloqueo encontrado

El PDF `E8066B5FA877947A47862B566C3A752FD1BA514973CD2B01A7AD31803BBDD28B` esperaba `FACTURA` según H1D5B y produjo `REVISAR` con método `OCR_NO_CONCLUYENTE` en el producto real.

La inspección confirmó que contiene una imagen embebida Mdoc de 198×198. El pipeline productivo `AnalyzePdfWithOcr` prioriza esa imagen y no rasteriza la página completa. H1D5A/H1D5B usaron OCR sobre la página rasterizada, que sí produjo `FACTURA`. También uno de los tres conflictos esperados quedó `OCR_NO_CONCLUYENTE`, por lo que sólo 2/3 exponen `MDOC_OCR_CONFLICTO`.

Resolver esta diferencia requiere decidir y benchmarkear una política para imagen embebida no concluyente frente a rasterización de página. No se cambió esa precedencia en H1D5C porque implicaría una nueva regla OCR no validada por H1D5B.
