# H1D10A — preparación externa reproducible

## Alcance

El adaptador `PrepareBank.cs` invoca exclusivamente MdocPdfTextExtractor, MdocPdfQrDetector, RasterQrDetector, PdfPageRasterizer y DocumentOcrService del ensamblado productivo. La normalización visual usa el código de VisualInvoiceShadowService compilado por separado para probar la corrección sin reemplazar el binario de la aplicación. No llama a inferencia, selección documental, SQL ni Gmail. VisionShadow está deshabilitado en la configuración aislada del proceso.

## Estado retomado

Antes de la reanudación existían los índices de entrada, los baselines, cuatro shards y la regresión correcta de 80 assets históricos. No existían registros raw ni assets del banco. Se conservaron esos insumos. La prueba inicial preparó cuatro documentos que se reutilizaron al ejecutar los shards.

C: tenía 1,68 GB libres. Omar eligió expresamente E:\RecepcionDocumental-H1D10A-assets para las imágenes restantes. Los cuatro documentos iniciales conservan sus ocho PNG en assets/; no se volvieron a generar. Cada ruta absoluta está registrada en bank-manifest.csv.

## Métodos congelados para esta preparación

- SHA-256 idéntico: una fila. SourcePaths mantiene todas las filas del inventario externo y sus procedencias.
- Ground truth: dataset.csv y carpetas canónicas FACTURA/OTRO_DOCUMENTO/NO_DOCUMENTO, con verificación de hashes. Se conservan labels y GroupId originales como evidencia; no se escribe sobre el corpus.
- QR: la interpretación factura/no factura proviene de ArcaQrDecoder.IsInvoiceType / IsKnownNonInvoiceType. No se agregó una tabla fiscal. Los detectores existentes buscan QR embebido y en el raster de la primera página; no garantizan detectar todos los QR del documento.
- Texto: Mdoc conserva el texto del documento. No informa coordenadas; por ello una palabra encontrada en ese texto no basta para etiquetar. El título debe ser una línea documental explícita en OCR del 35% superior de la primera página, con confianza media OCR >=0,80. Si el texto nativo lo corrobora, LabelSource=PDF_TEXT_STRONG; de lo contrario, OCR_STRONG. Esa confianza no es una probabilidad calibrada de acierto.
- Conflictos entre fuentes fuertes o múltiples tipos de título: REVISAR/CONFLICT. No se adjudica automáticamente NO_DOCUMENTO por página vacía, error o falta de texto.
- Imagen: primer raster productivo PDF a 300 DPI; canonicalización productiva para imágenes; RGB sin resize; cabecera de ancho completo, altura ceil(0,35 * alto), origen (0,0). Dimensiones y hashes en metadata.
- Familia: componentes conexas por CUIT inequívoco de cabecera previo a marcadores de receptor, GroupId humano existente, o similitud de plantilla (dHash cabecera <=4 bits, página <=8 bits y diferencia de aspecto <0,04). family-links.csv explicita los enlaces. Los singleton sin evidencia se marcan no resueltos y se excluyen del holdout. La heurística es conservadora, no prueba identidad semántica de todo emisor; se requiere auditoría humana de familias antes de certificación.
- Split: familia completa. DEVELOPMENT_DIAGNOSTIC para familias observadas en Batch001/002 o ya presentes en corpus; SEALED_TEST para una selección determinística por hash H1D10A_SEALED_V1, estratificada por FACTURA y OTRO_DOCUMENTO; el resto DEVELOPMENT. No se asignan TRAIN/VALIDATION hasta la etapa posterior. Familias con REVISAR, problemas de calidad o identidad no resuelta no son elegibles para el sellado.

## Reanudar sin sobrescribir

`Prepare-H1D10A.ps1` verifica y conserva los baselines cuando ya existen. No vuelve a preparar ni recalcular etiquetas. Para continuar los registros faltantes, ejecutar bin/PrepareBank.exe con los argumentos: raíz de la aplicación, carpeta H1D10A, shard-N.tsv, E:\RecepcionDocumental-H1D10A-assets. El adaptador reutiliza los registros raw existentes. No lanzar dos procesos sobre el mismo shard. Los cuatro shards son disjuntos por SHA.

Después de que existan los cuatro shard-N-completion.json, ejecutar `python FinalizeBank.py`. El finalizador valida original/corpus/modelo, trazabilidad y splits antes de finalizar. Se niega a cambiar el contenido de un sealed-test-manifest.json existente. Nunca usar una opción que elimine ese sello para ajustar resultados.

`python TestEvidenceRules.py` valida títulos, menciones a facturas asociadas, ambigüedad, confianza OCR y receptor. `normalization-regression.json` compara RGB/letterbox/tensor de 80 assets históricos y todos los píxeles del TIFF C000096 normalizado; `normalization-evidence.json` identifica fuente y ensamblado comprobados. No hubo sesiones ONNX en estas pruebas.

## Límites y uso posterior

Los labels fuertes automáticos son evidencia auditable y no sustituyen certificación humana. review-required.csv debe resolverse antes de usar esas filas para aprendizaje. No usar el holdout para elegir arquitectura ni thresholds. Su auditoría humana posterior debe mantenerse independiente y versionada; el sello actual identifica exactamente esta preparación, sin scores. Ningún archivo Batch*-model.csv ni evaluación visual ciega humana participa en la generación de labels.

Las imágenes, textos, raw y binarios son datos locales excluidos por .gitignore. No agregar documentos ni textos al control de versiones automáticamente. artifact-index.csv enumera las rutas y hashes de los artefactos entregados.

## Incidencia OCR y recuperación

Un SHA (9DEFF370610668A672B5743E1D56640197DD73C29661C593A4E07656610312BE) no devolvió el OCR de página completa durante 341 segundos después de tener la cabecera. Se detuvo sólo ese worker, preservando los 1.479 registros completos. ocr-timeout-audit.json registra la observación y ocr-timeouts.json evita repetir ese OCR fallido. Se conservan página/cabecera; su texto queda como OCR_HEADER_ONLY_FULL_PAGE_TIMEOUT y LabelFinal=REVISAR salvo ground truth previo confirmado. No se sustituyó Tesseract ni se modificó el OCR productivo.

Los 147 registros pendientes se dividieron en tres fragmentos disjuntos. Los PNG ya presentes se verifican contra la reconstrucción determinística y no se sobrescriben. Tras completar esos fragmentos, la pasada de cierre del shard original reutiliza todos sus raw y emite el comprobante de finalización. Las pruebas sintéticas TestPreparationGates.py cubren deduplicación, conflictos, fallo de calidad, familias, exclusión observada y rechazo de resellado.
