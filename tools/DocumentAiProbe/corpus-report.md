# Informe de corpus H1D3A2

- Archivos: 40
- Hashes únicos: 40
- Grupos: 27

| Clase | Archivos | Grupos | Pendientes | SourceType | Diversidad |
|---|---:|---:|---:|---|---|
| FACTURA | 21 | 13 | 7 | IMAGE=1; PDF=13 | DMF_PDF=9; JPG_ADJUNTO=1; PDF_ESCANEADO=1; REVIEWED_BATCH=3 |
| OTRO_DOCUMENTO | 12 | 11 | 9 | IMAGE=1; PDF=10 | COMPROBANTE_PAGO=1; CORREO_REENVIO=1; CREDENCIAL=1; IMPUESTO_INMOBILIARIO=1; ORDEN_COMPRA=1; REVIEWED_BATCH=6 |
| NO_DOCUMENTO | 7 | 3 | 17 | IMAGE=3 | FIRMA=1; FOTOGRAFIA=1; NEWSLETTER_PUBLICIDAD=1 |

## Splits
- TRAIN: 17 grupos.
- VALIDATION: 5 grupos.
- TEST: 5 grupos.

## Hallazgos
- ADVERTENCIA: Desequilibrio de grupos mayor a 2:1.
- ADVERTENCIA: Clase insuficiente: FACTURA
- ADVERTENCIA: Clase insuficiente: OTRO_DOCUMENTO
- ADVERTENCIA: Clase insuficiente: NO_DOCUMENTO

## Estado global

**INSUFICIENTE**

20 grupos por clase sólo habilitan el primer experimento. Augmentation futura se limita a TRAIN y conserva GroupId; no reemplaza grupos independientes.
