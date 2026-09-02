# H1D9E SHADOW OPERATIVO — BACKFILL COMPLETADO

2026-09-02. Corte de resultados antes de restituir la tarea programada: 22:08:37 UTC.

## Activación y pipeline único

INI operativo excluido de Git: VisionShadowEnabled=True y VisionShadowModelVersion=H1D9B-CANDIDATE-001, verificados mediante el cargador de la DLL productiva. La aplicación WEB fue puesta en mantenimiento; IIS Express no estaba activo al finalizar el backfill, por lo que se inició el sitio existente en x64. Nuevo Application_Start registrado a las 19:07:51 locales y GET de la bandeja HTTP 200. No se pulsó sincronización ni se ejecutó Gmail durante el backfill.

VisualDocumentShadowService contiene el bloque extraído, usado tanto por DocumentAnalysisService (camino Gmail) como por el backfill. Sin cambios en VisualInvoiceShadowService, PdfPageRasterizer, OCR, thresholds o modelo. El backfill no ejecuta clasificación ni reimportación; sólo lee archivos y escribe resultados mediante VisualShadowRepository.Save.

## Regresión y builds

- WebForms Release|Any CPU: exit 0, 0 errores, 0 advertencias.
- PdfRasterProbe/backfill net48/x64: exit 0, 0 errores, 6 advertencias preexistentes.
- Regresión completa: 80 documentos; 70 elegibles/70 OK; Changed=0; DuplicateFirstPage=0; SessionsCreated=1; Process64Bit=True; Gate=True; exit 0.
- Delta máximo PFactura frente a referencia congelada: 0 en 70 filas.
- 70943A: raster OCR reutilizado, 1 llamada, 1 página OCR, 0 páginas shadow.
- 31371F: 2 llamadas, 0 páginas OCR, 1 página shadow; sin duplicación física de primera página.

## Resultado real

Se recorrieron 125 documentos existentes. Se insertaron 123 filas shadow: 122 OK y 1 ERROR. Dos archivos ausentes se omitieron, sin inventar evaluaciones. Una sola sesión ONNX en el proceso de backfill. Exit 0.

| Resultado visual | Cantidad |
| --- | ---: |
| FACTURA_FUERTE | 14 |
| INCIERTO_VISUAL | 75 |
| NO_FACTURA_FUERTE | 33 |
| ERROR / UNSUPPORTED_FORMAT | 1 |
| Sin evaluación por archivo ausente | 2 |

| Clasificación efectiva actual | Total | FACTURA_FUERTE | INCIERTO_VISUAL | NO_FACTURA_FUERTE | ERROR | Ausente |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| REVISAR | 101 | 3 | 64 | 33 | 1 | 0 |
| FACTURA | 23 | 11 | 11 | 0 | 0 | 1 |
| DESCARTAR | 1 | 0 | 0 | 0 | 0 | 1 |

No procesables individualizados:

- Id 13, Nota de Pedido de Compras.docx: archivo existente pero sin soporte visual; ERROR/UNSUPPORTED_FORMAT generado por el pipeline compartido. Sin PFactura ni zona.
- Id 19, 23175875379_011_00003_00000097.pdf: no existe en su ruta registrada; sin evaluación.
- Id 10116, 23.jpeg: no existe en su ruta registrada; conserva decisión humana DESCARTAR; sin evaluación.

## Los 15 REVISAR con mayor PFactura

| Id | Archivo | PFactura | Zona |
| ---: | --- | ---: | --- |
| 6 | 746omar-silverii_edi-sa-comRESDIA201620261635.pdf | 0.89509737 | FACTURA_FUERTE |
| 10118 | b4c8fa3786e8_Z001023243075.pdf | 0.83469647 | FACTURA_FUERTE |
| 26 | FacturaSINQR.pdf | 0.80033517 | FACTURA_FUERTE |
| 27 | PRUEBA_OCR_PDF_MDOC.pdf | 0.75566918 | INCIERTO_VISUAL |
| 3 | NC_00002_00000001.pdf | 0.74785721 | INCIERTO_VISUAL |
| 24 | FacturaC_PV00004_00000002-1.jpg | 0.70930171 | INCIERTO_VISUAL |
| 4 | FacturaC_PV00004_00000002.pdf | 0.69681281 | INCIERTO_VISUAL |
| 10175 | 082026.pdf | 0.49310505 | INCIERTO_VISUAL |
| 59 | 42.png | 0.45285025 | INCIERTO_VISUAL |
| 10109 | 41.png | 0.45285025 | INCIERTO_VISUAL |
| 1 | Orden de Compra.pdf | 0.43770823 | INCIERTO_VISUAL |
| 10176 | NOTIFICACION AGOSTO.pdf | 0.42800605 | INCIERTO_VISUAL |
| 65 | ejemplo.pdf | 0.39693207 | INCIERTO_VISUAL |
| 66 | image001.png | 0.31608361 | INCIERTO_VISUAL |
| 10089 | 29.jpeg | 0.31365111 | INCIERTO_VISUAL |

## Integridad e idempotencia

Las siete tablas productivas distintas de DocumentoVisionShadow conservaron sus conteos y digests completos de filas/columnas: DocumentoRecepcion (125), DocumentoGroundTruth (0), DocumentoRevisionMuestra (0), GmailAdjunto (265), GmailCuenta (1), GmailMensaje (115), GmailSyncEjecucion (12). Esto incluye clasificaciones, decisiones humanas, rutas, timestamps y cursor. El hash de DocumentoRecepcion permaneció 762A3059637A4FBB100E0C361DE5E140BEB1B15A67008C83ABA6F6214F045E82.

Cursor final, igual al estado previo al backfill: 6814028; UltimaConsultaUtc=2026-09-02 21:48:39. Las auditorías históricas 1/2 permanecen intactas.

Los 123 archivos disponibles conservaron SHA-256 y tamaño; los dos ausentes siguen ausentes. No se movieron ni copiaron archivos productivos. Cada workspace temporal fue eliminado; 0 probes y 0 runners residuales al corte final.

Segunda pasada: Inserted=0, Existing=123, SkippedMissing=2, SessionsCreated=0, exit 0. DocumentoVisionShadow conservó exactamente el digest 01DA1D87F316F6C34D5FB30E535884C0A8A3B2F501B8CB96C1E39F4C8A8DD22D, incluidos resultados y timestamps. No se sobrescribieron filas.

La IA sigue exclusivamente en shadow. Estas discrepancias/opiniones no se contabilizan como aciertos o errores humanos: DocumentoGroundTruth sigue vacío. Sin entrenamiento ni tuning.

## Evidencia

- regression.log y regression/*.csv: paridad y raster.
- build-product.log y build-backfill-final.log.
- inventory-final.log; backfill/protected-database-before.tsv y protected-files-before.tsv.
- backfill-first-pass.log y backfill-idempotency.log.
- backfill/protected-*-after-*.tsv, results-*.csv y summary-*.txt.
- web-restart-stdout.log: registro del sitio y solicitudes GET.
- procedure.md: alcance y corrección del comparador de hashes del arnés.

La tarea programada se mantuvo deshabilitada hasta cerrar estas comprobaciones. Su restitución posterior vuelve a permitir la recepción normal y queda fuera de la ventana de integridad del backfill.
