# H1D5B — Métricas primarias de 36 PDF

> Regresión/diagnóstico experimental; no certificación final independiente.

## CURRENT_PRODUCT

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 13 | 9 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |

## DIRECT_REPLACEMENT

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 15 | 4 | 3 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |

## CONSERVATIVE_FUSION

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 15 | 7 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |

## FUSION_THEN_QR (C1)

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 15 | 7 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |

## QR_THEN_FUSION (C2)

| Label | FACTURA | REVISAR | DESCARTAR |
|---|---:|---:|---:|
| FACTURA | 15 | 7 | 0 |
| OTRO_DOCUMENTO | 0 | 8 | 6 |

## Indicadores prioritarios

| Estrategia | FACTURA→DESCARTAR | Falso FACTURA | Recall FACTURA | REVISAR | OCR requeridos |
|---|---:|---:|---:|---:|---:|
| CURRENT_PRODUCT | 0 | 0 | 13/22 | 17 | 6/36 |
| DIRECT_REPLACEMENT | 3 | 0 | 15/22 | 12 | 21/36 |
| CONSERVATIVE_FUSION | 0 | 0 | 15/22 | 15 | 21/36 |
| C1_FUSION_THEN_QR | 0 | 0 | 15/22 | 15 | 21/36 |
| C2_QR_THEN_FUSION | 0 | 0 | 15/22 | 15 | 21/36 |

## Orden QR

C1 vs C2 difieren en **0** PDF. En este corpus son equivalentes.
