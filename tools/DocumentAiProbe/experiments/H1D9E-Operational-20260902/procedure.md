# H1D9E — activación y backfill controlado

## Alcance

- INI operativo (excluido de Git): VisionShadow/Enabled=true; ModelVersion=H1D9B-CANDIDATE-001.
- Tarea RecepcionDocumental-GmailSync temporalmente deshabilitada y WEB bajo app_offline.htm durante la operación.
- VisualDocumentShadowService contiene el bloque visual extraído de DocumentAnalysisService. Gmail y backfill llaman al mismo servicio. No cambian rasterizador, preprocesamiento, ONNX, modelo ni thresholds.
- El backfill recibe documentos ya persistidos. No llama Gmail, importación, clasificación, DocumentStorage, revisiones humanas ni CompleteSync.

## Controles

`--h1d9e-backfill <raíz-producto> <directorio-evidencia> inventory` sólo lee SQL y archivos. Guarda conteos/digests de todas las tablas de usuario salvo DocumentoVisionShadow, e inventario SHA-256/tamaño de archivos. No guarda valores de credenciales, tokens, cuerpos de correo ni connection strings.

`--h1d9e-backfill <raíz-producto> <mismo-directorio> apply` exige esa línea base y toma el applock Gmail existente. Compara el estado de las tablas antes de cada evaluación/inserción y después de cada inserción, y verifica los archivos individualmente y al finalizar. Ante una diferencia, devuelve fallo y se detiene. Única escritura SQL: VisualShadowRepository.Save.

Las evaluaciones existentes se buscan por DocumentoRecepcionId/ModeloVersion/ModeloSha256; no se recalculan ni sobrescriben. Repetir apply debe insertar cero filas y conservar también el digest completo de DocumentoVisionShadow.

Los archivos ausentes se informan como SKIPPED_FILE_MISSING sin inventar evaluación. El DOCX disponible pasa por CreateUnsupportedError del pipeline compartido y se persiste como ERROR/UNSUPPORTED_FORMAT, sin probabilidad ni zona.

Para PDF sin raster OCR previo, se usa la rama productiva RasterizeFirstPage. Las imágenes usan EvaluateImageFile y su canonicalización existente. No se reejecuta OCR/clasificación en el backfill ni se copian archivos productivos; sólo se generan PNG temporales mediante AttachmentWorkspace.

## Incidencia del arnés previa a SQL

El primer inventario rechazó Id 1 por comparación textual sensible a mayúsculas del SHA-256. DocumentStorage persiste hex en minúsculas; el verificador lo calculaba en mayúsculas. Hash y tamaño eran idénticos. Se corrigió únicamente la comparación a OrdinalIgnoreCase; no se alteró el archivo ni se insertaron evaluaciones en ese intento.

Los resultados y el cierre de esta ejecución se documentan por separado; este procedimiento no afirma aprobación anticipada.
