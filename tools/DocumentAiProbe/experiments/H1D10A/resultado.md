# H1D10A APROBADO — PREPARACION

## Banco y etiquetas

Se encontraron **1718 documentos físicos**, representados por **1626 SHA únicos**. Se conservaron todas las procedencias. No se utilizó H1D9B para labels ni se ejecutó inferencia, entrenamiento, SQL o Gmail.

- SHA con ground truth existente: **25**.
- Etiquetados automáticamente por evidencia fuerte: **919**.
- Conflictos entre fuentes fuertes: **0**.
- Documentos que requieren revisión humana: **682**.
- Registros con incidencias de calidad: **1**.

| LabelFinal (incluye ground truth y revisión) | Cantidad |
|---|---:|
| FACTURA | 869 |
| OTRO_DOCUMENTO | 75 |
| NO_DOCUMENTO | 0 |
| REVISAR | 682 |

| Labels automáticos fuertes exclusivamente | Cantidad |
|---|---:|
| FACTURA | 851 |
| OTRO_DOCUMENTO | 68 |
| NO_DOCUMENTO | 0 |

| DocumentType | Cantidad |
|---|---:|
| REVISAR | 682 |
| FACTURA | 869 |
| NOTA_CREDITO | 31 |
| OTRO_DOCUMENTO | 36 |
| NOTA_DEBITO | 8 |
| RECIBO | 0 |
| TRANSFERENCIA_ANTICIPO | 0 |
| COMPROBANTE_BANCARIO | 0 |
| IMPUESTO_BOLETA | 0 |
| NO_DOCUMENTO | 0 |

| LabelSource | Cantidad |
|---|---:|
| INSUFFICIENT_EVIDENCE | 681 |
| QR_ARCA_STRONG | 744 |
| PDF_TEXT_STRONG | 54 |
| EXISTING_GROUND_TRUTH | 25 |
| OCR_STRONG | 121 |
| QUALITY_REVIEW | 1 |

Los labels automáticos son reglas de evidencia, no probabilidades calibradas ni ground truth humano certificado. Los títulos ambiguos y las fuentes en conflicto se conservan como REVISAR. Los labels humanos originales permanecen en ExistingGroundTruth y el corpus no se modifica. No se infiere NO_DOCUMENTO por ausencia de texto.

La revisión comprende 681 casos de evidencia insuficiente y un OCR de página completa detenido tras 341 segundos; en ese caso se preservaron imagen, cabecera y texto de cabecera. La etapa no habilita entrenar todavía: faltan revisión humana y cobertura de NO_DOCUMENTO para una futura salida de tres clases.

## Familias y holdout

- FamilyId distintos: **410**.
- Holdout sellado: **43 SHA**, en **10 familias**.
- Composición por label: `{"FACTURA": 41, "OTRO_DOCUMENTO": 2}`.
- Composición por DocumentType: `{"FACTURA": 41, "OTRO_DOCUMENTO": 1, "NOTA_CREDITO": 1}`.
- SHA-256 de sealed-test-manifest.json: `EF2C7A80A5616A5F009087020E3FB9988DD76B5BAED315F84D921AA605BCF7E0`.

FamilyId deriva de componentes conexas por CUIT inequívoco de cabecera, GroupId ya auditado o similitud conservadora de plantilla; README.md detalla la fórmula y family-links.csv cada enlace. Los singleton sin evidencia de familia quedan fuera del holdout. Estas heurísticas no prueban que se hayan identificado todos los emisores: se requiere auditoría humana de familias antes de certificación.

| Comprobación de leakage | Resultado |
|---|---:|
| ShaCrossSplitCount | 0 |
| FamilyCrossSplitCount | 0 |
| LinkedTemplateOrEmitterCrossSplitCount | 0 |
| ObservedHashesInHoldout | 0 |
| ObservedFamiliesInHoldout | 0 |

Cada familia ocupa un solo split. Se excluyeron del holdout familias observadas en Batch001/Batch002 y familias ya presentes en corpus, con revisión pendiente, problemas de calidad o identidad no resuelta. No se generaron scores. No usar este holdout para seleccionar arquitectura. Sus etiquetas y familias necesitan auditoría independiente antes de certificar un modelo futuro; no cambiar este sello para ajustar resultados.

## Normalización TIFF y regresión

El TIFF observado es `Format1bppIndexed`, dimensiones **1656x2339**, con **3873384 píxeles**. La conversión RGB coincidió píxel a píxel con el decodificador de referencia tras canonicalización/EXIF: **True**. El fix resuelve formatos indexados mediante GetPixel sin remuestreo por DPI; no cambia los caminos RGB de 24/32/8 bits ya validados ni resize/letterbox/normalización.

- RGB histórico: **80/80 idénticos**.
- RGB después del letterbox: **80/80 idénticos**.
- Tensor normalizado: **80/80 idénticos**.
- Sesiones ONNX en regresión: **0**; en preparación: **0** en cada uno de los cuatro workers.

La fuente corregida se compiló y comprobó en un ensamblado aislado. No se reemplazó el binario productivo ni se promovió ningún modelo. normalization-evidence.json identifica el hash de la fuente, ensamblado y reporte; normalization-regression.json incluye el resultado de cada caso.

## Integridad y artefactos

Se verificaron por tamaño/SHA los 1718 originales y todos los extractos; el corpus y los archivos del modelo conservaron sus hashes. Se mantuvieron 1718 procedencias en el manifest canónico. gate-report.json contiene los gates.

- Carpeta de manifests, código, texto y metadata: `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10A`.
- Imágenes grandes autorizadas por Omar: `E:\RecepcionDocumental-H1D10A-assets`.
- Los ocho PNG de la prueba inicial se conservaron en `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10A\assets`; no se repitieron.
- **artifact-index.csv** enumera cada artefacto, ruta absoluta, tamaño y SHA-256. Su propia fila no lleva hash recursivo.

Archivos funcionales de esta tarea:

- `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\Services\VisualInvoiceShadowService.cs`: corrección mínima, 14 líneas agregadas y una reemplazada.
- `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10A\.gitignore`
- `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10A\Prepare-H1D10A.ps1`
- `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10A\PrepareBank.cs`
- `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10A\FinalizeBank.py`
- `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10A\TestEvidenceRules.py`
- `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10A\TestPreparationGates.py`
- `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10A\PublishReport.py`
- `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10A\README.md`

El cambio que ya existía en tools/DocumentAiProbe/Extract-DmfBank.ps1 pertenece a la tarea anterior de extracción/aplanado; no se modificó durante H1D10A. Los demás archivos de H1D10A son artefactos generados enumerados en artifact-index.csv.

No se entrenó H1D10B. No se promovió un modelo ni se cambiaron thresholds.
