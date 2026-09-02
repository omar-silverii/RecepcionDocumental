# H1E1 — Diagnóstico de terminación sin cierre

Estado: **NO APROBADO**. Diagnóstico del 2026-09-02. No se reejecutó Gmail, no se recompiló ni se modificó código productivo durante esta investigación. Auditorías 1 y 2 preservadas. No se restauró el ejecutable ni se alteró Norton.

## Flujo real inspeccionado

`Program.Main` selecciona `--sync` por defecto, crea el AppDomain del producto y espera `ExecuteAssembly`; su `finally` descarga el dominio. El Main interior llama a `Worker.Run`.

`Worker.Run` espera `GmailSyncService.SynchronizeAsync("SCHEDULER").GetAwaiter().GetResult()`. El servicio espera `GmailSyncExecution.RunAsync`: primero adquiere el lease SQL, después obtiene la cuenta y crea la auditoría EJECUTANDO. Espera el procesamiento incremental y cada mensaje. El núcleo ejecuta `CompleteSync` sólo si no hubo errores, tras comprobar el lease, y retorna el resultado. El coordinador llama a `Finish`, retorna el resultado y dispone el lease. Worker calcula el código, imprime el resumen y retorna. Main exterior devuelve ese código después de descargar el dominio.

Salidas identificadas:

- Argumentos/configuración de modo inválidos o proceso no x64: código 2.
- Modos diagnósticos explícitos: código 0 tras sus comprobaciones; no corresponden a las invocaciones reales analizadas.
- Lease ocupado: auditoría OMITIDA_YA_EN_EJECUCION, resultado AlreadyRunning y código 10.
- Sin mensajes: continúa por CompleteSync, Finish y resumen; no hay retorno temprano de éxito que omita esas etapas.
- Excepciones del coordinador: intenta Finish FALLIDA y relanza; Worker/Main capturan y devuelven 1.
- Errores por mensaje: se cuentan; el finally del mensaje registra su resumen; se evita CompleteSync si hubo errores y el runner devuelve 1.
- No se encontraron Environment.Exit/FailFast, async void, tareas desprendidas ni cancelación explícita en el flujo inspeccionado. Las tareas de sincronización se esperan.

La rama normal de éxito está en `tools/RecepcionDocumental.SyncRunner/Program.cs`: espera en línea 64, calcula código en 65, imprime resumen en 67, retorna en 68. No hay evidencia de que las ejecuciones 1/2 hayan alcanzado esa rama final. Los códigos 0 capturados por el arnés no demuestran un retorno normal desde Main.

## Reconstrucción con evidencia existente

Horas SQL en UTC; logs operativos locales UTC-03.

| Auditoría | Inicio UTC | Última evidencia operativa | Estado SQL |
| --- | --- | --- | --- |
| 1 | 19:51:29 | 16:51:40.606, mensaje 24/70 terminado | EJECUTANDO, Fin NULL |
| 2 | 19:51:59 | 16:51:59.358, inicio incremental | EJECUTANDO, Fin NULL |

Ambas: cuenta 1, origen SCHEDULER, todos los contadores persistidos en 0, fallback 0 y DetalleError NULL. Esos contadores no son progreso en vivo: sólo se actualizan al finalizar. La primera ejecución sí procesó mensajes y analizó dos adjuntos descartados; sus workspaces registran eliminación. La segunda no tiene log de consulta incremental completada. No hay resumen final ni error correlacionado en los logs operativos.

`termination-sql-readonly.txt` conserva las consultas de sólo lectura: cursor 6808008, UltimaConsultaUtc y FechaModificacion 2026-08-31 18:59:11. Coinciden con la evidencia anterior H1D7A (`sql-before-after.md`); no se dispone de un snapshot inmediatamente anterior a cada ejecución. En la ventana UTC [19:51:29,19:55:00) hay 0 GmailMensaje por FechaDetectadoUtc, 0 DocumentoRecepcion por FechaAltaUtc y 0 GmailAdjunto por FechaDescargaUtc. Total actual: 123 documentos, 0 ground truth. No hay evidencia de CompleteSync ejecutado ni de nuevas persistencias en esa ventana. No se alteró cursor, mensajes, documentos ni auditorías.

## Intervención externa demostrada

El runner de `tools/RecepcionDocumental.SyncRunner/bin` ya no existe. Norton conserva una entrada de cuarentena en `C:\ProgramData\Norton\Antivirus\chest\index.xml`:

- ChestId: 00000037.
- Nombre: RecepcionDocumental.SyncRunner.exe; carpeta: bin del runner de este workspace.
- Detección: IDP.Generic (etiqueta de Norton, no conclusión sobre malicia del programa).
- Tamaño: 8192 bytes.
- FileTime/TransferTime: 1788378725 = 2026-09-02 19:52:05 UTC.
- Hash correlacionado en evidencia de Norton: 032B344F36B9C074C35E3531DD9BDDC58D668436CB102A9FDE8FD6B19DFA770D. Coincide con SHA256 del ejecutable conservado en obj/Release.

`Cleaner.log`, líneas 309–311, registra búsqueda/operación sobre esa ruta a las 19:51:41, temporalmente contigua al último log de la primera ejecución. `NortonSvc.log` registra custodia/envío a análisis del mismo hash a las 19:49:44–19:49:47. `FwServ.log` asocia el runner con PID 26020 a las 19:51:29.671.

La cuarentena del binario está demostrada. La hipótesis de interrupción externa es consistente con los cortes, pero estos registros no acreditan el mecanismo exacto de terminación de ambos procesos ni qué componente estableció cada código 0. La consulta de sólo lectura a History.db de Norton falló con `database is locked`; no se forzó acceso ni se detuvo el antivirus. No se encontró evento .NET Runtime/Application Error/WER en la ventana revisada que cierre esa brecha.

## Defecto independiente comprobado en código

`GmailSyncAuditRepository.Start` captura errores y devuelve null. `Finish` retorna silenciosamente con id null, no verifica filas afectadas por UPDATE y captura errores sin propagarlos. El coordinador puede entonces retornar resultado y Worker informar 0 pese a una auditoría no finalizada.

Esto viola el contrato requerido de auditoría, pero **no explica por sí solo estos incidentes**: ese camino todavía imprimiría el resumen final. No se lo presenta como causa demostrada de las ejecuciones 1/2.

## Conclusión y siguiente paso propuesto

No puede atribuirse una causa exacta completa ni una rama concreta de retorno 0 a ambos incidentes con la evidencia disponible. Por instrucción del usuario se detiene la certificación sin modificar código por hipótesis. No se ejecutaron builds, nuevos probes ni sincronizaciones reales en esta etapa.

Se necesita revisar/exportar el detalle de los eventos IDP.Generic de Norton para identificar sus acciones sobre ambos procesos. No se propone desactivar protección ni agregar exclusiones automáticas. Cualquier restauración requiere revisión y autorización separada.

Cuando pueda retomarse con causa resuelta, la corrección mínima del contrato de auditoría deberá hacer observable el fallo de Start/Finish, verificar la finalización real, emitir AUDIT_FINALIZATION_FAILED y devolver fallo operativo; preservar la excepción primaria, intentar cierre en todos los caminos administrados y certificar AuditFinalized/ExitCodeConsistent. Un finally no garantiza ejecución ante terminación externa del proceso. Las auditorías 1/2 deben seguir intactas salvo autorización explícita para otro tratamiento.
