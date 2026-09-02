# Concurrencia

- Reintentos rechazados: True.
- Exactamente un ganador concurrente: True.
- Ganador observado: tarea `race-b`, decisión `NO_DOCUMENTO`.
- Perdedor observado: tarea `race-a`, intento `FACTURA`, retorno `false` sin excepción.
- Resultado final: `DESCARTAR / NO_DOCUMENTO`.
- Ground truth final: 1 fila, `Secuencia=1`, `EsVigente=True`.
- Rollback UPDATE + ground truth ante fallo: True.
- Fixture atómico: `ResultadoRevision=NULL`, `EtiquetaRevision=NULL`, ground truth 0.
- Atomicidad limpia de 009: `MigrationRollbackClean=True`; tabla, keys, FKs, CHECKs e índices parciales = 0.
