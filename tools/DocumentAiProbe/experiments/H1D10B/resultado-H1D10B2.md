# H1D10B2 — HEADER_ONLY

**H1D10B2 APROBADO — GATES DE INTEGRIDAD; NO PROMOCION**

**HEADER_ONLY no mejora TEXT_ONLY WORD.** Macro F1 global 0,500286 frente a 0,716762; evidencia independiente 0,513277 frente a 0,752970. No se selecciona ni promueve esta rama como clasificador final.

901 headers encontrados y verificados por SHA-256; cero faltantes, cero errores de inferencia.
828 FACTURA / 73 OTRO_DOCUMENTO; 154 FamilyId. Mismos cuatro folds B1, sin regenerarlos.
Leakage SHA/FamilyId/holdout: 0/0/0. B1, H1D10A y el sello permanecen intactos. Se verificaron también los 83 archivos protegidos históricos.

## Configuración y ejecución

EfficientNet-B0 ImageNet local, CPU, entrada 384×384. Header determinístico H1D10A completo, RGB, resize bicúbico con aspecto preservado, letterbox blanco centrado, normalización ImageNet. Sin augmentations ni nuevos crops.
Cada fold comienza con ImageNet: 8 epochs del clasificador (AdamW 0.001), luego 12 del bloque final features.8 y clasificador (0.0001). Batch 16, weight decay 0.0001, dropout 0.2. Total 20 epochs por fold, 80 entre cuatro folds. Sin early stopping ni selección usando el fold externo.
Loss ponderada con N_train/(2*N_train_clase), calculada sólo en los otros tres folds. No duplicación ni muestras sintéticas. features.0..7 quedan congelados, incluso sus estadísticas BatchNorm; su caché es independiente de las etiquetas. En fase 2 BatchNorm del bloque final se actualiza sólo con TRAIN.
Se reutilizan el stack, Letterbox/ImageNet, AdamW, fases de transferencia y checkpointing de la infraestructura H1D9B, adaptados a cabecera 384, cuatro folds fijos y evaluación OOF sin early stopping externo. No se usan pesos del candidato H1D9B.
La comprobación end-to-end de checkpoints usa un header permitido de cada fold; no toca holdout.

Venv: `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10B\header-local\.venv`.
Versiones importadas: Python 3.11.3, torch 2.13.0+cpu, torchvision 0.28.0+cpu, numpy 2.4.6, pillow 12.3.0. Resto en header-training-manifest.json. Sólo paquetes locales; hashes en header-offline-packages.json; cero descargas y sin cambios al Python global.
Incidencia inicial: instalación parcial por reubicación del manual de SymPy; import inicial falló por dependencia ausente. Se completó el mismo .venv sin sobrescribir archivos diferentes; smoke test posterior pasó ANTES de entrenar.

## Métricas OOF

Threshold 0.5 exclusivamente diagnóstico; PR-AUC = average precision escalonada, por clase. No calibración ni cambio de thresholds operativos.

| Evidencia | Rama | Recall F | Recall O | Precision F | Precision O | Macro F1 | Balanced acc. | ROC-AUC | AP F | AP O |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| AllStrongLabels | TEXT_ONLY_WORD | 0.996377 | 0.315068 | 0.942857 | 0.884615 | 0.716762 | 0.655723 | 0.837205 | 0.967342 | 0.624352 |
| AllStrongLabels | HEADER_ONLY | 0.842995 | 0.178082 | 0.920844 | 0.090909 | 0.500286 | 0.510539 | 0.539160 | 0.927535 | 0.092455 |
| IndependentEvidence | TEXT_ONLY_WORD | 0.998512 | 0.370968 | 0.945070 | 0.958333 | 0.752970 | 0.684740 | 0.823181 | 0.963049 | 0.660131 |
| IndependentEvidence | HEADER_ONLY | 0.845238 | 0.209677 | 0.920583 | 0.111111 | 0.513277 | 0.527458 | 0.584389 | 0.937057 | 0.159263 |

Evidencia independiente: 734 documentos (672 FACTURA, 62 OTRO_DOCUMENTO), QR_ARCA_STRONG + EXISTING_GROUND_TRUTH. Las 167 etiquetas de texto se excluyen de esta métrica, no del entrenamiento definido por B1.

## Matrices y errores

AllStrongLabels, filas reales / columnas predichas [FACTURA, OTRO_DOCUMENTO]: `[[698, 130], [60, 13]]`.
FACTURA → OTRO_DOCUMENTO: 130; OTRO_DOCUMENTO → FACTURA: 60.

IndependentEvidence, filas reales / columnas predichas [FACTURA, OTRO_DOCUMENTO]: `[[568, 104], [49, 13]]`.
FACTURA → OTRO_DOCUMENTO: 104; OTRO_DOCUMENTO → FACTURA: 49.

## Complementariedad frente a TEXT_ONLY WORD

{"Outcomes": {"HEADER_NEW_ERROR": 146, "BOTH_CORRECT": 702, "BOTH_WRONG": 44, "HEADER_CORRECTS_TEXT": 9}, "StrongProbabilityDisagreementsAbsDeltaAtLeast0_5": 33, "Note": "Diagnostic only, not active learning; no queue, no relabeling."}

Casos completos en header-text-cases.csv; los scores de texto proceden exclusivamente del OOF B1 sin modificarse. No se creó una cola de active learning ni etiquetas nuevas.

## Familias con errores

43 familias con errores HEADER. Lista completa con SHA afectados en header-family-errors.csv.

| FamilyId | Fold | Documentos | Errores HEADER | Errores TEXT |
|---|---:|---:|---:|---:|
| F-6F5CAF1E1AEEB43C5486 | 2 | 82 | 41 | 0 |
| F-EE08C7103D2458E54189 | 0 | 178 | 38 | 1 |
| F-FACD83D8B4281A05571A | 1 | 208 | 27 | 4 |
| F-C4EFEA98CD9D9490E678 | 2 | 16 | 16 | 0 |
| F-2225E7482E0C1A2CF093 | 2 | 17 | 9 | 16 |
| F-319491FEEAF6386D99BD | 0 | 7 | 7 | 7 |
| F-D591D778E05641FBA116 | 2 | 5 | 5 | 0 |
| F-77DF7B7AF18627AF7CC8 | 2 | 3 | 3 | 0 |
| F-99F7A3F5F30B759FC1BA | 3 | 4 | 3 | 3 |
| F-0EE887752345887B2F4F | 2 | 2 | 2 | 2 |
| F-108FF89DFB4EC29C3343 | 0 | 4 | 2 | 2 |
| F-17D8887E1AFD322C7FBE | 3 | 4 | 2 | 2 |
| F-19E3E2219E79C363F63F | 0 | 5 | 2 | 2 |
| F-32D29C25B87BCDD5D9A3 | 3 | 2 | 2 | 0 |
| F-A26E6E68BD787A625DC9 | 3 | 2 | 2 | 0 |
| F-FC411F149001282E7254 | 2 | 2 | 2 | 0 |
| F-127BD2FBDD2CC031C0FE | 2 | 18 | 1 | 0 |
| F-1B5BFBE3C31B9AB023C7 | 1 | 1 | 1 | 0 |
| F-22D8B0B2BC197D116017 | 3 | 3 | 1 | 1 |
| F-33B81ED9CE192B1C2F2E | 2 | 1 | 1 | 0 |
| F-38536FFE21B52E182513 | 3 | 1 | 1 | 1 |
| F-38BC11F2F1438B7B9147 | 0 | 1 | 1 | 1 |
| F-406AFFDAEA0F1DA6D75F | 2 | 1 | 1 | 0 |
| F-463F5D66BCFC1B95B4F2 | 3 | 104 | 1 | 1 |
| F-63F23BB2C7162DAFD254 | 0 | 1 | 1 | 0 |
| F-64920D3701B1A613ABDC | 3 | 1 | 1 | 1 |
| F-73D1F939C9F0B93053BA | 1 | 1 | 1 | 0 |
| F-780F613365E9FD4576C4 | 2 | 1 | 1 | 0 |
| F-86F3ACDC73080FEE11E7 | 1 | 1 | 1 | 0 |
| F-8A2E5C33616E9AAE14BF | 3 | 1 | 1 | 1 |
| F-9556189C2A6C11049E74 | 2 | 1 | 1 | 0 |
| F-B5269576C44B14F588D5 | 1 | 1 | 1 | 0 |
| F-C4297C930D025E93CCD8 | 2 | 1 | 1 | 0 |
| F-DB7766FFE78E622516F9 | 1 | 1 | 1 | 0 |
| F-DCC01CD6C3861C7E4C3E | 3 | 12 | 1 | 1 |
| F-E1B8D918E114096E5077 | 2 | 1 | 1 | 0 |
| F-E311D094CEA0C30D422D | 3 | 3 | 1 | 1 |
| F-E8D73DC2FE1F519E363D | 0 | 1 | 1 | 1 |
| F-EFE6E9C595B24436566B | 3 | 12 | 1 | 1 |
| F-F005EBB69A8DFBD56F0B | 3 | 2 | 1 | 2 |
| F-F47775882EC65DC9A64C | 2 | 1 | 1 | 0 |
| F-F47B73E9F9F7F5973BEB | 0 | 1 | 1 | 1 |
| F-F8EBECB216D6C0D8E1BE | 3 | 2 | 1 | 0 |

## Checkpoints de desarrollo

- Fold 0: `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10B\header-local\header-fold-0.pt`; SHA-256 `C46D069A1DFA2536640E312A2600DDFBC97F301511AA45037C746E99028C576E`.
- Fold 1: `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10B\header-local\header-fold-1.pt`; SHA-256 `D6A40F9EB293FBFEEC6583E39A06DF04613B098C75107A62F673A1B73C751E87`.
- Fold 2: `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10B\header-local\header-fold-2.pt`; SHA-256 `DD31BADD9C72D35B655488CF11A513493FF7C4E2E8DD97D03EB7FCFB8B56444D`.
- Fold 3: `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10B\header-local\header-fold-3.pt`; SHA-256 `D907F1B0B4ACE1731B1934A9C891F59872CFFACD18AF1F0E4BED803BA0B0B9E2`.

## Alcance

Aprobación de integridad del experimento, no certificación de calidad ni promoción. Son métricas de desarrollo con labels fuertes y familias heurísticas; el holdout continúa sellado. No se ejecutó FULL_PAGE, HYBRID, fusión, active learning ni Nivel A. No se modificó SQL, Gmail, H1D9B, binarios ni configuración productiva. Se detiene aquí.

## Artefactos

Archivos nuevos/modificados de B2 y rutas/hashes: header-artifact-index.csv. Se conservan todos los archivos B1; sólo resultado-H1D10B2.md se actualiza desde el estado pendiente. Modelos y entorno están en header-local, excluido de Git.
