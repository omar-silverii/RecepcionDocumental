# H1E1 — NO APROBADO

> Registro histórico del gate inicialmente fallido. Cierre posterior: **H1E1 APROBADO**, documentado en `certificacion-final.md`. Se preserva el contenido siguiente como evidencia, no como estado vigente.

Fecha: 2026-09-02. Base de trabajo inicialmente limpia: `c7fafde98bc610d52752b0212287d57925aa1cd6`.

## Gate fallido: finalización de Gmail real / auditoría del runner

Las dos invocaciones síncronas del runner devolvieron exit code 0, pero su única salida fue `SyncRunner | ProcessX64=True`. No emitieron el resumen final de sincronización previsto en Worker.Run.

Consulta posterior de la base real de desarrollo:

- Ejecución 1: cuenta 1, SCHEDULER, inicio UTC `2026-09-02 19:51:29`, estado EJECUTANDO, FechaFinUtc NULL.
- Ejecución 2: cuenta 1, SCHEDULER, inicio UTC `2026-09-02 19:51:59`, estado EJECUTANDO, FechaFinUtc NULL.
- Ambas conservan los contadores iniciales de auditoría; no equivalen a resultados completos ni a cero errores de Gmail demostrado.
- Cero procesos SyncRunner residuales; apertura exclusiva de bin/RecepcionDocumental.dll exitosa.
- Documentos antes/después: 123. Ground truth antes/después: 0. Esto no basta para certificar el cursor ni la finalización incremental.

La segunda invocación se inició tras observar exit code 0 de la primera, antes de descubrir la inconsistencia de auditoría. No se lanzó una tercera ejecución. No se actualizaron artificialmente los estados EJECUTANDO ni el cursor.

Causa observada: el proceso termina sin completar el contrato de salida/auditoría. El origen preciso no está determinado; no se atribuye sin evidencia a OAuth, IA, una excepción administrada o al scheduler. Detención sin corrección automática.

## Gates anteriores superados

- Cierre documental de H1D7C2 registrado en PROJECT_STATUS.md, sin alterar su lógica.
- WebForms Release|Any CPU, runner net48/x64 y PdfRasterProbe: builds exit code 0.
- Runner fuera de IIS: credencial existente descifrable, variables OAuth presentes, sin nueva autorización; diagnóstico sin llamadas Gmail exit code 0 y DLL libre.
- Probe aislado H1E1: exit code 0, Gate=True. Lock de sesión SQL global antes de leer cuenta/cursor; un solo procesamiento; competencia web/runner en ambos sentidos; cursor de fixture protegido; liberación tras error; estados/timestamps de auditoría; fallo secundario de reporting tolerado; runner exit codes 0/10 y lifecycle limpio.
- Migración 011 aislada: THROW observado, rollback limpio, esquema e idempotencia; base temporal eliminada.
- 011 aplicada dos veces en desarrollo: exit code 0; auditoría inicialmente vacía.
- Hashes congelados de dataset, grupos, folds, ONNX y checkpoint intactos. Sin cambios de IA, OCR, ground truth o muestreo H1D7C2. Sin training ni tuning.
- Scripts y documentación Task Scheduler preparados; ninguna tarea instalada.

## Evidencia

`isolated-probe.txt`, `runner-config-check.txt`, `build-webforms.log`, `build-runner.log`, `build-probe.log`, `migration-real-1.txt`, `migration-real-2.txt`, `gmail-real-1.txt`, `gmail-real-2.txt`, `real-audit.txt`.

Archivos del camino pendiente de diagnóstico: `tools/RecepcionDocumental.SyncRunner/Program.cs`, `Services/GmailSyncService.cs`, `Services/GmailSyncExecution.cs`, `Data/GmailSyncAuditRepository.cs`.
