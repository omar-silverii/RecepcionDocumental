# H1D9F — cierre solicitado por Omar, 2026-09-02

## A

Id 6: ResultadoRevision=DESCARTAR, EtiquetaRevision=OTRO_DOCUMENTO. Ground truth NO_FACTURA/OTRO_DOCUMENTO, Secuencia=1, EsVigente=True, Fuente=REVISION_OPERATIVA. FechaDecisionUtc y FechaRevisionUtc=2026-09-02 22:24:45 UTC. Código real DocumentRepository.TryResolve: UPDATE e INSERT comparten SqlTransaction; commit después de ambos, rollback ante error. La observación SQL confirma persistencia; la atomicidad está sustentada por el código, no por una traza histórica de transacciones.

Nuevas decisiones humanas: Id 26 y 10118 FACTURA. En los tres positivos fuertes: 2 aciertos y 1 falso positivo (muestra de sólo 3). Otros 8 documentos inciertos también tienen decisiones humanas, fuera de la cohorte. No inferir etiquetas de clasificación productiva. Lista original de 36 conservada; 33 negativos fuertes pendientes.

## B

Tarea real invoca SyncRunner, no PdfRasterProbe. PdfPageRasterizer usa PDFtoImage.Conversion en proceso. Norton autosandbox.log sí registra el probe exacto tools/PdfRasterProbe/bin/PdfRasterProbe.exe; última referencia observada 19:06:07 local, durante pruebas anteriores. No se demostró invocación cada 5 minutos. Hash del probe observado: 5BFBDA400BED5292245865758E56C89316BD3FB282EDC3D59EC9644FCF2D50C2; LastWriteTimeUtc=22:04:17.5805484. No se configuró ni recomendó exclusión. Pendiente correlación de un nuevo aviso real.

## C

Instalado SyncLauncher net48/x64 WinExe, que inicia el MISMO SyncRunner con UseShellExecute=false y CreateNoWindow=true, espera y propaga ExitCode. SyncRunner no modificado; diagnóstico manual sigue disponible. Build launcher exit 0. Pruebas sin Gmail: lock libre=0, raíz inexistente=1, lock ocupado=10. Manage-ScheduledTask incorpora UseHiddenLauncher y usa launcher en nuevas instalaciones Interactive; mantiene modo productivo sin sesión.

Actualizada únicamente la acción de RecepcionDocumental-GmailSync, conservando principal, triggers y settings. Auditoría automática Id 17, SCHEDULER, 22:33:39–22:33:41 UTC, COMPLETADA, nuevos=0, errores=0. Falta certificar visualmente varios disparos consecutivos y comprobar el cierre final de la serie. Observación de procesos interrumpida por solicitud de cierre; WMI ProcessStartTrace denegado, reemplazado por polling diagnóstico (no prueba visual).

No declarar H1D9F certificado todavía. Retomar comprobando tarea/exit codes/auditorías y pedir confirmación visual humana. No ejecutar pruebas ni modificar datos automáticamente al leer este informe.
