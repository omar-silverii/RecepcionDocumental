# H1D5C — Validación productiva de 36 PDF

> **Resultado no aprobado:** 35/36 no satisface el criterio obligatorio 36/36.

- Coincidencias con `FusionThenQrClassification`: **35/36**.
- OCR activado según logs: **21/36**.
- Fallos/límites finales: 2.
- Tiempo total/media/mediana/P95: 177951 / 4943.08 / 88.5 / 4376.75 ms.

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 14 | 8 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |

## Criterios críticos

- `FACTURA → DESCARTAR`: 0.
- `OTRO_DOCUMENTO → FACTURA`: 0.
- Promociones esperadas: 2/2.
- Conflictos bloqueados `MDOC_OCR_CONFLICTO`: 2/3.
- Diferencias: E8066B5FA877947A47862B566C3A752FD1BA514973CD2B01A7AD31803BBDD28B esperado=FACTURA real=REVISAR.

La diferencia proviene de la fuente OCR: el producto prioriza una imagen embebida Mdoc de 198×198 y obtiene `OCR_NO_CONCLUYENTE`; H1D5A/H1D5B utilizaron la página rasterizada completa y obtuvieron `FACTURA`. Cambiar esa precedencia requiere un benchmark adicional y no se forzó en este hito.
