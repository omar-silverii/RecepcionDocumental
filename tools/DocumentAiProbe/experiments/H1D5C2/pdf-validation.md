# H1D5C2 — Validación productiva de 36 PDF

- Coincidencias con `FusionThenQrClassification`: **36/36**.
- OCR activado según logs: **21/36**.
- Fallos/límites finales: 2.
- Tiempo total/media/mediana/P95: 192089 / 5335.81 / 891.5 / 4772.75 ms.

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 15 | 7 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |

## Criterios críticos

- `FACTURA → DESCARTAR`: 0.
- `OTRO_DOCUMENTO → FACTURA`: 0.
- Promociones esperadas: 2/2.
- Conflictos bloqueados `MDOC_OCR_CONFLICTO`: 3/3.
- Diferencias: ninguna.

## Verificación H1D5C2

- Fuente OCR en los 21 PDF activados: `RASTER_PAGINA`.
- E8066: `FACTURA` mediante `OCR`.
- Conflictos `MDOC_OCR_CONFLICTO`: 3/3, todos en REVISAR.
- Límites raster controlados: 2/2, todos en REVISAR.
- Diferencias frente a H1D5C: 7 filas por clasificación, método o fuente; ver `h1d5c-comparison.csv`.
