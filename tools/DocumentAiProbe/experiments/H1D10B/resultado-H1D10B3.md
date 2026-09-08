# H1D10B3 — HEADER_TEXT_ONLY y revisión ciega

**H1D10B3 APROBADO — GATES; SIN PROMOCION**

901 documentos, 828 FACTURA / 73 OTRO_DOCUMENTO, 154 FamilyId. Mismos 4 folds B1 sin regenerarlos. TF-IDF e IDF ajustados dentro de cada TRAIN, nunca sobre VALIDATION. Sólo HeaderTextAsset; ninguna imagen participa en entrenamiento o scoring.
WORD: ngrams 1–2, min_df=2, max_df=.995, max_features=80000. CHAR: char_wb 3–5, min_df=2, max_features=120000. WORD_CHAR: 70000/100000 features. Lowercase, strip_accents unicode, sublinear_tf, L2. LogisticRegression C=1, balanced, liblinear, max_iter=2500, seed=20260907.
Ganador elegido por macro F1 en evidencia independiente, con desempates predefinidos: **WORD**. Threshold .5 sólo diagnóstico. No se ajustaron thresholds operativos.
Runtime local existente: sklearn 1.3.2, numpy 1.26.2, scipy 1.11.3. B1 se entrenó con sklearn 1.8.0: se conserva su OOF original para comparación. Su modelo WORD se ejecuta mediante los pesos/IDF/vocabulario congelados, con paridad de fórmula independiente para los 682 casos. No se refiteó ni modificó B1. No se instalaron paquetes ni se descargó nada.

| Evidencia | Variante | Recall F | Recall O | Precision F | Precision O | Macro F1 | Balanced acc. | ROC-AUC |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| AllStrongLabels | TEXT_ONLY | 0.996377 | 0.315068 | 0.942857 | 0.884615 | 0.716762 | 0.655723 | 0.837205 |
| AllStrongLabels | WORD | 0.996377 | 0.178082 | 0.932203 | 0.812500 | 0.627679 | 0.587230 | 0.905268 |
| AllStrongLabels | CHAR | 0.993961 | 0.123288 | 0.927847 | 0.642857 | 0.583332 | 0.558625 | 0.779383 |
| AllStrongLabels | WORD_CHAR | 0.996377 | 0.109589 | 0.926966 | 0.727273 | 0.575448 | 0.552983 | 0.893968 |
| IndependentEvidence | TEXT_ONLY | 0.998512 | 0.370968 | 0.945070 | 0.958333 | 0.752970 | 0.684740 | 0.823181 |
| IndependentEvidence | WORD | 0.998512 | 0.209677 | 0.931944 | 0.928571 | 0.653093 | 0.604095 | 0.909874 |
| IndependentEvidence | CHAR | 0.995536 | 0.145161 | 0.926593 | 0.750000 | 0.601536 | 0.570349 | 0.786362 |
| IndependentEvidence | WORD_CHAR | 0.998512 | 0.129032 | 0.925517 | 0.888889 | 0.592991 | 0.563772 | 0.893913 |

Evidencia independiente: 734 documentos (672 FACTURA, 62 OTRO_DOCUMENTO), sólo QR_ARCA_STRONG y EXISTING_GROUND_TRUTH. Las métricas globales incluyen 167 labels derivados de texto y no deben presentarse como certificación humana.

## Matrices OOF

Filas reales / columnas predichas [FACTURA, OTRO_DOCUMENTO].
- AllStrongLabels / TEXT_ONLY: `[[825, 3], [50, 23]]`.
- AllStrongLabels / WORD: `[[825, 3], [60, 13]]`.
- AllStrongLabels / CHAR: `[[823, 5], [64, 9]]`.
- AllStrongLabels / WORD_CHAR: `[[825, 3], [65, 8]]`.
- IndependentEvidence / TEXT_ONLY: `[[671, 1], [39, 23]]`.
- IndependentEvidence / WORD: `[[671, 1], [49, 13]]`.
- IndependentEvidence / CHAR: `[[669, 3], [53, 9]]`.
- IndependentEvidence / WORD_CHAR: `[[671, 1], [54, 8]]`.

WORD no supera TEXT_ONLY al threshold diagnóstico .5: Macro F1 0,627679 vs 0,716762 (global) y 0,653093 vs 0,752970 (independiente). Su ROC-AUC es mayor: 0,905268 vs 0,837205 y 0,909874 vs 0,823181. Se conserva como señal para revisión, sin ajuste operativo ni promoción.

## Prioridad de revisión

682/682 scoreados, 0 errores. 150 SHA únicos, 64 familias en la cola; 87 documentos de familias no presentes en TRAIN.
Score de prioridad: 0.40*(1-min(p_text,p_header)) + 0.25*abs(p_text-p_header) + 0.15*max(1-2*abs(p_text-.5),1-2*abs(p_header-.5)) + 0.10/(1+FamilyTrainingCount) + 0.10*max_cosine_to_training_OTRO. Criterios y cortes fijados antes del scoring; no son nuevos thresholds de clasificación. TextScore usa el contrato B1: CABECERA + texto de cabecera + DOCUMENTO + TextAsset. HeaderTextScore usa sólo cabecera. Similitud semántica = máximo coseno TF-IDF contra los 73 OTRO de desarrollo.
Composición de criterios (solapados, no suman necesariamente el total):

{
  "Scored": 682,
  "Errors": 0,
  "TopCount": 150,
  "ReviewFamilies": 271,
  "TopFamilies": 64,
  "AllReasonCountsOverlapping": {
    "BAJO_PFACTURA": 84,
    "INCERTIDUMBRE": 125,
    "FAMILIA_POCO_REPRESENTADA": 525,
    "SIMILITUD_CON_OTRO_CONOCIDO": 115,
    "PRIORIDAD_RELATIVA": 88
  },
  "TopReasonCountsOverlapping": {
    "BAJO_PFACTURA": 84,
    "INCERTIDUMBRE": 85,
    "FAMILIA_POCO_REPRESENTADA": 102,
    "SIMILITUD_CON_OTRO_CONOCIDO": 105,
    "PRIORIDAD_RELATIVA": 1
  },
  "TopUnseenTrainingFamilyDocuments": 87,
  "Note": "Criteria overlap. Scores are review heuristics, never labels. Existing training-family overlap in REVISAR is allowed for prioritization, not validation."
}

## Revisión ciega

Carpeta para el operador: `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10B\ReviewTop150`. Abrir `index.html`. Contiene únicamente IDs R000001…R000150 e imágenes de primera página ya preparadas por H1D10A, copiadas byte a byte y verificadas. No se ejecutó FULL_PAGE como modelo ni se renderizaron documentos nuevos.
El orden de IDs se mezcló independientemente del ranking para no revelar prioridad. No hay scores, predicciones, razones, FamilyId, SHA ni nombres originales en el índice. review-priority.csv, review-top150.csv y b3-blind-traceability.csv son archivos analíticos fuera de la carpeta; NO entregarlos al operador junto con el material ciego.
La ceguera se refiere a señales del modelo: el contenido original del documento permanece visible sin alteraciones.

## Artefactos y preservación

b3-artifact-index.csv enumera rutas absolutas y SHA-256 de scripts, métricas, manifiestos, 12 modelos OOF y el modelo final de cabecera, estado portátil B1 y 150 PNG. header-text-model-hashes.csv contiene los hashes de los 13 modelos de cabecera. b3-text-only-inference-verification.json identifica B1 y demuestra la paridad de ejecución.
Los artefactos B1/B2, bank-manifest, corpus y H1D9B conservan sus hashes. Holdout sólo verificado por sello; no texto/imágenes/inferencia. REVISAR no se usó para entrenamiento ni se cambió LabelFinal. No se modificó SQL, Gmail, producción ni se promovieron modelos. La inferencia sobre familias REVISAR también presentes en desarrollo sólo prioriza revisión; no es validación de generalización.
Se detiene H1D10B3 aquí.

## Verificación final

Los criterios de la cola se solapan: 84 bajo PFactura, 0 desacuerdos fuertes (diferencia >=.35), 85 incertidumbre, 102 familia poco representada y 105 similitud textual con OTRO conocido; un caso sin estos cortes queda por prioridad relativa. Son motivos de revisión, no labels.

Se verificaron las métricas contra sklearn, la integridad CRC de los 150 PNG y ausencia de metadatos textuales/EXIF. El índice contiene sólo IDs neutrales y rutas locales. No pudo realizarse vista previa en navegador porque CUA falló al localizar sus assets de kernel; la validación del HTML y enlaces fue estática.

Recuperación de ejecución: los 12 modelos OOF y el refit de cabecera se completaron antes de un fallo al exportar índices NumPy del vocabulario B1 a JSON. Se corrigió la representación a int nativo y se retomó sólo la priorización, conservando modelos y OOF. La fórmula independiente B1 coincide en los 682 casos con error absoluto máximo 1,832e-15.
