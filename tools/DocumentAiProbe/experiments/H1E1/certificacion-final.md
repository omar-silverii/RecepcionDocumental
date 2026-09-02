# H1E1 APROBADO

Fecha: 2026-09-02. Certificación completada contra el workspace real, preservando los cambios H1E1 existentes. No se instaló una tarea programada.

## Sincronización real incremental, antes de cambiar código

Se ejecutó exactamente una sincronización adicional, la segunda tras la restauración confirmada por Omar. PID 21576, inicio 20:21:01.9251756 UTC, proceso finalizado a las 20:21:03.0972457 UTC. Código capturado directamente con System.Diagnostics.Process: 0; HasExited=True; ProcessX64=True.

Resumen emitido: `COMPLETADA | Mensajes=0 | Nuevos=0 | Errores=0 | ExitCode=0`.

Auditoría nueva Id=4: COMPLETADA, inicio 20:21:02 UTC, FechaFinUtc=20:21:03 UTC. La ejecución restaurada anterior Id=3 permanece COMPLETADA con FechaFinUtc=20:17:45 UTC.

| Dato | Antes | Después |
| --- | --- | --- |
| UltimoHistoryId | 6813797 | 6813797 |
| UltimaConsultaUtc | 20:17:45 | 20:21:03 |
| GmailMensaje | 115 | 115 |
| DocumentoRecepcion | 125 | 125 |
| Grupos duplicados por cuenta/GmailMessageId | 0 | 0 |
| Grupos repetidos HashSha256/TamanioBytes | 21 | 21 |

No se crearon mensajes/documentos ni duplicados nuevos. Los 21 grupos de contenido repetido son preexistentes y no se eliminaron ni alteraron. No se presenta ese conteo como cero duplicados globales. Auditorías 1 y 2 siguen intactas, EJECUTANDO/FechaFinUtc=NULL, como evidencia histórica; los gates de nuevas ejecuciones no las borran ni reclasifican.

Una consulta inicial de baseline usó nombres de columnas incorrectos; se corrigió únicamente esa consulta de sólo lectura contra el esquema real, antes de lanzar el runner. No hubo escritura SQL ni repetición de Gmail por ese error del diagnóstico.

## Defecto demostrado y corrección mínima

En la base temporal, un trigger rechazó el UPDATE a COMPLETADA con THROW 51102, permitiendo registrar FALLIDA. Antes de la corrección: `ControlledFinishFailurePropagated=False | UnfinishedAudits=1`, gate fallido y probe exit 1. La base temporal fue eliminada.

Cambios acotados:

- GmailSyncAuditRepository: Start/Finish propagan errores sanitizados; Finish verifica estado final y FechaFinUtc en SQL. Una fila ausente no cuenta como éxito.
- GmailSyncExecution: intenta cerrar FALLIDA tras un fallo y preserva la excepción primaria si también falla ese intento, manteniendo observable el error secundario de auditoría. El lease sigue dispuesto por using.
- SyncRunner: marcador AUDIT_FINALIZATION_FAILED/AUDIT_START_FAILED y código 1; la rutina de sincronización/resumen/exit se extrajo sin otro cambio de flujo para probar la misma implementación con callbacks aislados.
- H1E1SyncProbe y documentación: regresiones de estados, códigos y fallos de auditoría. Sin nuevos modos CLI de sincronización ficticia.

Después: THROW 51102 comprobado en InnerException, `AUDIT_FINALIZATION_FAILED | ExitCode=1`, `ControlledFinishFailurePropagated=True | UnfinishedAudits=0`, auditoría FALLIDA cerrada. También pasan inicio de auditoría indisponible (sin procesar Gmail) y cierre de Id inexistente, ambos con código 1.

La auditoría no revierte cursor/documentos ya persistidos. Una indisponibilidad permanente de SQL o terminación externa puede impedir el cierre físico; no se promete que finally venza un cierre forzado. En fallos administrados de auditoría el runner ya no informa éxito silencioso. Las auditorías históricas 1/2 no forman parte del conjunto nuevo certificado.

## Gates finales

`isolated-probe-final.txt`: exit 0, Gate=True.

- Migración 011 aislada: rollback, esquema, idempotencia y original intacto; base temporal eliminada.
- AlreadyRunning: resumen y auditoría cerrada; código 10.
- WEB ganador frente al proceso SyncRunner --sync: sólo un procesador, runner perdedor código 10 sin leer/procesar cuenta.
- Runner con lease retenido (--probe-lock-hold) frente a WEB: WEB omitido; proceso finaliza y libera lock.
- Fallo intencional: lock liberado; cursor de fixture preservado.
- Rutina real de salida del runner, invocada con callbacks en el dominio aislado: COMPLETADA=0, COMPLETADA_CON_ERRORES=1, FALLIDA=1, OMITIDA_YA_EN_EJECUCION=10; estado y FechaFinUtc verificados en cada caso.
- Proceso hijo real --sync con fixture sin refresh token: FALLIDA cerrada, exit 1, sin llegar a OAuth/red.
- AuditFinalized=True y ExitCodeConsistent=True para las rutas aisladas ensayadas.
- Fallo de cierre controlado deja FALLIDA cerrada, código 1 y sin resumen falso de éxito.

## Builds y lifecycle

- WebForms Release|Any CPU: exit 0, 0 errores, 0 advertencias.
- SyncRunner Release, net48/x64, Prefer32Bit=false: exit 0, 0 errores, 0 advertencias.
- PdfRasterProbe Release, net48/x64: exit 0, 0 errores, 6 advertencias preexistentes MSB3277/CS0618.
- Ambos ejecutables: PE Machine=0x8664. Probe ProcessX64=True.
- Verificación final: 0 SyncRunner y 0 PdfRasterProbe residuales; apertura exclusiva de DLL del producto, DLL del probe y ejecutable del runner exitosa.
- 0 bases temporales H1E1_Probe y 0 carpetas temporales H1E1_Probe restantes.
- Runner presente, 0 entradas de cuarentena del runner al finalizar. No se alteró Norton durante esta certificación.

## Preservación

Hash congelados recomputados y coincidentes con H1D9E1A: dataset AFECA7A2..., grupos FADEA71A..., folds 9E4A9ACC..., ONNX A1DC24FE... tanto experimental como productivo, checkpoint F6F552CF.... Sin cambios de IA, OCR, H1D7C1/H1D7C2, OAuth, thresholds, modelo, ground truth o muestreo. Ground truth real permanece 0. Sin training ni tuning.

No se ejecutó Gmail adicional después de la corrección: las verificaciones finales son aisladas. No se creó ningún commit ni se instaló scheduler.

## Evidencia

- `20260902-172101-incremental-stdout.txt` / `20260902-172101-incremental-stderr.txt`.
- `restored-single-sync-result.md` (Id 3, ejecución anterior).
- `audit-defect-before-fix.txt`.
- `isolated-probe-final.txt`.
- `build-webforms-final.log`, `build-runner-final.log`, `build-probe-final.log`.
- `termination-diagnostic.md` y `resumen.md`: evidencia histórica preservada.
