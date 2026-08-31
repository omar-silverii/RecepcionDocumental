# H1D5C1 — Comparación de políticas OCR PDF

> Benchmark experimental. Label se usa sólo después de calcular P0–P3.

## P0_PRODUCT_EMBEDDED_FIRST

### Universo primario: 21 PDF OCR

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 5 | 8 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 0 |

### Vista de 36 PDF

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 14 | 8 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |

- Coincidencia H1D5B: 35/36.
- `FACTURA → DESCARTAR`: 0.
- `OTRO_DOCUMENTO → FACTURA`: 0.

## P1_FULL_PAGE_RASTER

### Universo primario: 21 PDF OCR

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 6 | 7 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 0 |

### Vista de 36 PDF

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 15 | 7 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |

- Coincidencia H1D5B: 36/36.
- `FACTURA → DESCARTAR`: 0.
- `OTRO_DOCUMENTO → FACTURA`: 0.

## P2_EMBEDDED_THEN_RASTER_IF_REVIEW

### Universo primario: 21 PDF OCR

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 6 | 7 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 0 |

### Vista de 36 PDF

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 15 | 7 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |

- Coincidencia H1D5B: 36/36.
- `FACTURA → DESCARTAR`: 0.
- `OTRO_DOCUMENTO → FACTURA`: 0.

## P3_POSITIVE_RESCUE

### Universo primario: 21 PDF OCR

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 6 | 7 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 0 |

### Vista de 36 PDF

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 15 | 7 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |

- Coincidencia H1D5B: 36/36.
- `FACTURA → DESCARTAR`: 0.
- `OTRO_DOCUMENTO → FACTURA`: 0.

## Sanity checks

- E8066: embebidas=1 (198x198), embedded=REVISAR, raster=FACTURA, P0/P1/P2/P3=REVISAR/FACTURA/FACTURA/FACTURA.
- REMITO B4C8: embedded=REVISAR (OCR_NO_CONCLUYENTE), raster=DESCARTAR (OCR), clasificación final P0/P1=REVISAR/REVISAR.
