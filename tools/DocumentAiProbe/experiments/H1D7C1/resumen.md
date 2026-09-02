# H1D7C1

**H1D7C1 APROBADO**

- Shadow opcional y sin autoridad: True.
- Gate operativo: True.
- FACTURA produce ground truth `FACTURA`; OTRO_DOCUMENTO y NO_DOCUMENTO producen `NO_FACTURA` conservando etiqueta detallada.
- Archivos NO_FACTURA preservados; FACTURA almacenada y verificada por hash/tamaño.
- Reintentos y concurrencia producen una única decisión vigente con `Secuencia=1`.
- UPDATE operativo e INSERT ground truth son atómicos.
- Migración 009 recuperable, idempotente y con rollback DDL/DML limpio: True.
- Backfill: 0 elegibles, 0 insertados; 2 históricos incompletos no fueron inferidos.
- Gmail/cursor, UI y cadena visual sin cambios. Sin training ni tuning.
