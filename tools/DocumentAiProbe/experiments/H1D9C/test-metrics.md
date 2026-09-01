# Métricas TEST H1D9C

## File-level

| TN | FP | FN | TP | Recall FACTURA | Precision FACTURA | Specificity NO_FACTURA | F1 FACTURA | Balanced accuracy | ROC-AUC | PR-AUC |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 6 | 0 | 0 | 4 | 1.000000 | 1.000000 | 1.000000 | 1.000000 | 1.000000 | 1.000000 | 1.000000 |

## Zonas OOF pre-registradas

- NO_FACTURA_FUERTE: 2; errores: 0.
- FACTURA_FUERTE: 2; errores: 0.
- INCIERTO_VISUAL: 6.

## Group-level (métrica principal)

| TN | FP | FN | TP | Recall FACTURA | Specificity NO_FACTURA | Balanced accuracy | ROC-AUC | PR-AUC |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3 | 0 | 0 | 2 | 1.000000 | 1.000000 | 1.000000 | 1.000000 | 1.000000 |

### Resultados individuales por grupo

| GroupId | Archivos | Label | PFactura media | Pred050 | Correcto |
|---|---:|---|---:|---|---|
| comprobante-pago-bancario | 1 | NO_FACTURA | 0.316084 | NO_FACTURA | True |
| factura-familia-arillo | 1 | FACTURA | 0.836895 | FACTURA | True |
| factura-homologacion-c-00004-00000002 | 3 | FACTURA | 0.755102 | FACTURA | True |
| flightaware-newsletter | 4 | NO_FACTURA | 0.184919 | NO_FACTURA | True |
| otro-solicitud-nota-credito-banco-patagonia | 1 | NO_FACTURA | 0.326597 | NO_FACTURA | True |
