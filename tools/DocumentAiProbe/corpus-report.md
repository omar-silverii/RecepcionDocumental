# Informe de corpus H1D3A2

- Archivos: 80
- Hashes únicos: 80
- Grupos: 54

| Clase | Archivos | Grupos | Pendientes | SourceType | Diversidad |
|---|---:|---:|---:|---|---|
| FACTURA | 24 | 15 | 5 | IMAGE=2; PDF=15 | DMF_PDF=9; JPG_ADJUNTO=1; PDF_ESCANEADO=1; REVIEWED_BATCH=5 |
| OTRO_DOCUMENTO | 26 | 19 | 1 | IMAGE=8; PDF=11 | COMPROBANTE_PAGO=1; CORREO_REENVIO=1; CREDENCIAL=1; IMPUESTO_INMOBILIARIO=1; ORDEN_COMPRA=1; REVIEWED_BATCH=14 |
| NO_DOCUMENTO | 30 | 20 | 0 | IMAGE=20 | FIRMA=1; FOTOGRAFIA=1; NEWSLETTER_PUBLICIDAD=1; REVIEWED_BATCH=17 |

## Splits
- TRAIN: 17 grupos.
- VALIDATION: 5 grupos.
- TEST: 5 grupos.

## Hallazgos
- ADVERTENCIA: Clase insuficiente: FACTURA
- ADVERTENCIA: Clase insuficiente: OTRO_DOCUMENTO

## Estado global

**INSUFICIENTE**

20 grupos por clase sólo habilitan el primer experimento. Augmentation futura se limita a TRAIN y conserva GroupId; no reemplaza grupos independientes.
