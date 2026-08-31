# H1D5A — Métricas de evidencia textual

> Los resultados sobre TEST son regresión/diagnóstico experimental, no certificación final independiente. No se seleccionaron thresholds productivos.

## A. Calidad técnica de extracción

- Archivos auditados: 80; PDF: 36; PNG: 30; JPEG: 14; GroupId: 54.
- PDF que pasan `Mdoc.HasUsefulText`: 30.
- Mdoc útil con degradación estructural evidente: 15.
- PDF con NUL: 9; con controles: 11; fragmentación alta: 4.
- OCR ejecutado/exitoso/no disponible: 78/77/3.
- PDF con clasificación Mdoc/OCR distinta: 9.

## B. Evaluación contra Label

### Mdoc + InvoiceSelector (sólo PDF)

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 9 | 13 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |
| NO_DOCUMENTO | 0 | 0 | 0 |

### OCR + InvoiceSelector

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 16 | 5 | 3 |
| OTRO_DOCUMENTO | 0 | 19 | 7 |
| NO_DOCUMENTO | 0 | 27 | 3 |

### QR + Mdoc (sólo PDF)

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 9 | 13 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |
| NO_DOCUMENTO | 0 | 0 | 0 |

### QR + OCR (sólo PDF)

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 15 | 4 | 3 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |
| NO_DOCUMENTO | 0 | 0 | 0 |

### Indicadores críticos

- `FACTURA → DESCARTAR` con Mdoc: 0.
- `FACTURA → DESCARTAR` con OCR: 3.
- Falsos FACTURA Mdoc/OCR: 0/0.
- REVISAR Mdoc/OCR: 21/51.
