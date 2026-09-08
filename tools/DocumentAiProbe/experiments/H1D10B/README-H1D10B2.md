# Ejecución H1D10B2

Resultado definitivo: resultado-H1D10B2.md. Esta etapa terminó: no continuar con B3 ni otras ramas automáticamente.

## Entorno offline

Ruta del entorno exclusivo:

`C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D10B\header-local\.venv`

Create-HeaderEnvironment.py usa Python 3.11.3, venv sin pip/system-site-packages y los 21 wheels de header-offline-packages.json. Verifica SHA antes de extraer. En una reanudación conserva cada archivo coincidente y rechaza archivos distintos. No instala pip ni truststore y no usa Internet. header-environment-install.json enumera las rutas y SHA locales; header-import-verification.json demuestra que los imports relevantes provienen de este venv.

## Comandos del experimento

Desde esta carpeta, con PowerShell (no ejecutarlos para iniciar otra etapa):

```powershell
python Create-HeaderEnvironment.py
& ./header-local/.venv/Scripts/python.exe ./Train-HeaderOnly.py --smoke
& ./header-local/.venv/Scripts/python.exe ./Train-HeaderOnly.py --train
& ./header-local/.venv/Scripts/python.exe ./Report-HeaderOnly.py
```

El smoke test debe pasar antes de --train. El entorno y los inputs se verifican de nuevo al entrenar. Un error de import, peso, asset o leakage detiene la ejecución; no debe ignorarse.

No ejecutar Run-H1D10B1.py: los folds y scores B1 recibidos son la fuente congelada, no se reconstruyen. Los hashes de los 14 archivos B1 están en header-preflight.json. Ese archivo describe la fase previa, cuando todavía no se había entrenado; el estado final es header-gate-report.json y header-training-manifest.json.

## Diseño

- 901 SHA únicos, 154 familias, folds B1 0/1/2/3 exactos.
- Sólo HeaderAsset de las filas permitidas de bank-manifest.csv. No rasterizado, OCR, crop nuevo, nombres de archivo como features, REVISAR ni assets SEALED_TEST.
- Header RGB completo, bicubic resize con aspecto conservado a canvas blanco 384×384, normalización ImageNet.
- Backbone EfficientNet-B0 inicializado desde el checkpoint ImageNet local verificado. Nunca desde el candidato H1D9B.
- Cache de features.0..7 congelados en eval, sin etiquetas, gradientes o cambios BatchNorm. Se comprueba que parámetros y buffers permanecen idénticos. La extracción compartida no ajusta estadísticas al banco.
- Cuatro modelos independientes. Cada uno usa sólo los otros tres folds: 8 epochs classifier, 12 epochs features.8 + classifier. No entrenamiento del backbone completo. Durante fase 2 el último BatchNorm se ajusta sólo a TRAIN. Semillas SEED + Fold, SEED=20260907, algoritmos determinísticos, 8 threads CPU.
- Cross entropy por muestra multiplicada por N_train/(2*N_train_clase), promedio por minibatch. AdamW, batch 16, decay 0.0001; LR 0.001/0.0001. Sin sampler, duplicación ni augmentations.
- Epochs fijados antes de ver scores. El fold externo no se consulta para early stopping o ajuste de hiperparámetros. Una pasada de validación al final produce el OOF. Una segunda inferencia sobre un único header permitido por fold verifica la equivalencia del checkpoint completo con la ejecución mediante cache; no genera una segunda fila OOF.
- Exactamente 901 filas OOF, una por SHA. Checkpoints completos por fold en header-local; no se refiteó un quinto modelo con todo el banco.

## Métricas y comparación

Report-HeaderOnly.py sólo lee scores ya producidos. No entrena ni abre imágenes. Usa threshold 0.5 diagnóstico. Macro F1 y balanced accuracy, recalls/precisions por clase, AUC Mann-Whitney con empates=0.5 y average precision escalonada por clase. Se comprobó paridad contra las métricas B1 globales y de evidencia independiente sin instalar scikit-learn ni modificar el modelo textual.

header-text-cases.csv conserva los 901 casos con complementarity y diferencia absoluta de probabilidades. Desacuerdo fuerte significa diferencia >=0.5, un indicador descriptivo, sin ajuste operativo. No se prioriza una cola ni se adjudican labels.

header-family-errors.csv incluye las 154 familias, incluso las que tienen cero errores, y los SHA afectados. header-artifact-index.csv registra archivos de código, reportes, checkpoints y cache con sus hashes. El runtime se identifica mediante los wheels y manifests del entorno, no mediante una copia versionada de miles de archivos de biblioteca.

No se tocaron producción, SQL, Gmail, H1D9B, thresholds ni el holdout. Una aprobación de gates sólo certifica la integridad de esta ejecución, no que la rama visual sea útil o esté lista para producción.
