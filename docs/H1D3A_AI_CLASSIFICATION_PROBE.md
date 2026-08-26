# H1D3A — Probe IA local visual + textual

Fecha: 2026-08-26  
Estado: **PROMETEDOR PERO FALTA CORPUS**. Probe independiente; sin integración ni publicación.

## Alcance y aislamiento

Se creó `tools/DocumentAiProbe`, una aplicación .NET 8 sin referencia desde `RecepcionDocumental.csproj` ni desde la solución WebForms. Declara `Microsoft.ML 4.0.2` y `Microsoft.ML.Vision 4.0.2` para una futura prueba local de `FeaturizeText + SdcaMaximumEntropy` y transfer learning visual. No reutiliza el `Model.zip` histórico, no llama servicios externos y no modifica `InvoiceSelector`.

Se revisó `WorkflowStudio.AITrainer`. Su pipeline textual es una referencia útil, pero evalúa el modelo sobre el mismo conjunto usado para entrenar y no separa por grupos. Ese método no es válido para estimar generalización documental y no fue replicado.

## Corpus explícito

`tools/DocumentAiProbe/dataset.csv` contiene `Path`, `Label`, `GroupId`, `SourceType`, `Sha256`, `Split`, `OriginalPath` y `Diversity`. `Path` es relativo al directorio del dataset y `OriginalPath` conserva la trazabilidad absoluta. Las etiquetas se asignaron sólo a casos cuya naturaleza pudo confirmarse por antecedentes del proyecto o inspección visual; no se derivaron automáticamente de las carpetas `Facturas` o `Revisar`.

| Clase | Filas | Hashes únicos | Grupos |
|---|---:|---:|---:|
| FACTURA | 3 | 3 | 1 |
| OTRO_DOCUMENTO | 3 | 3 | 3 |
| NO_DOCUMENTO | 7 | 7 | 3 |
| **Total** | **13** | **13** | **7** |

Casos incluidos:

- FACTURA: Factura C JPG validada, `FacturaSINQR` y `PRUEBA_OCR_PDF_MDOC`; las tres representaciones pertenecen a la misma factura de homologación y comparten `factura-homologacion-c-00004-00000002`.
- OTRO_DOCUMENTO: orden de compra, credencial y comprobante de pago bancario.
- NO_DOCUMENTO: dos fotografías familiares, firma bancaria y cuatro recursos representativos del newsletter FlightAware.

Los recursos FlightAware comparten un único `GroupId`; no se cuentan como pruebas independientes. Las dos fotografías relacionadas también comparten grupo. Se excluyeron duplicados exactos y elementos dudosos. Los PDF permanecen identificados como `SourceType=PDF`: antes de un entrenamiento visual deberán rasterizarse por página a 300 DPI y conservar el `GroupId` del original; nunca se usaría un ícono PDF.

## División congelada

| Split | Filas | Grupos |
|---|---:|---:|
| TRAIN | 3 | 2 |
| VALIDATION | 2 | 2 |
| TEST | 8 | 3 |

Cada `GroupId` aparece en un solo split y con una única etiqueta. TEST quedó congelado y no se utilizó para entrenamiento ni ajuste.

## Resultado del auditor

El auditor recalcula SHA-256, rechaza duplicados exactos, etiquetas/splits inválidos y fuga de grupos. Confirmó cero errores estructurales y se abstuvo de entrenar: FACTURA tiene un solo grupo real, mientras OTRO_DOCUMENTO y NO_DOCUMENTO tienen tres cada una.

La capa visual tendría solamente un grupo NO_DOCUMENTO efectivo para entrenamiento. La capa textual tendría solamente un grupo FACTURA y un grupo OTRO_DOCUMENTO para entrenamiento, además de no contar todavía con textos OCR congelados y separados del TEST. Entrenar en estas condiciones permitiría memorizar origen, diseño o contenido y produciría métricas sin valor estadístico.

## Modelos y métricas

No se generaron modelos. Tamaño de modelos: no aplicable. El output compilado del probe y sus dependencias ocupa aproximadamente 10,7 MB; esto no representa el tamaño de un modelo entrenado ni de todos los assets nativos que requeriría el entrenamiento visual.

Por abstención responsable, no existen todavía:

- matriz de confusión;
- Accuracy, Macro Accuracy o Macro-F1;
- Precision/Recall por clase;
- scores visuales o textuales;
- falsos positivos/falsos negativos;
- errores de TEST;
- medición de entrenamiento CPU/memoria;
- resultado combinado por caso.

Asignar ceros o evaluar un modelo sobre TRAIN sería inventar rendimiento. En particular, todavía no puede estimarse el riesgo crítico `FACTURA → NO_DOCUMENTO`.

## Criterio combinado a evaluar en una fase posterior

La evaluación futura conservará abstención:

- visual `NO_DOCUMENTO` fuerte y OCR sin evidencia documental: candidato a `DESCARTAR`;
- visual `DOCUMENTO` y textual `FACTURA`: candidato a `FACTURA`, sujeto a validaciones fiscales determinísticas;
- visual `DOCUMENTO` y textual `OTRO_DOCUMENTO`: candidato a `DESCARTAR`;
- desacuerdo, score débil o información insuficiente: `REVISAR`.

No se fijó ningún threshold ni fórmula productiva.

## Corpus mínimo para continuar

Antes de entrenar se requieren, como mínimo, más grupos independientes y confirmados por clase, especialmente NO_DOCUMENTO. Deben cubrir documentos y layouts distintos, fotos, logos, firmas y newsletters de orígenes independientes. También deben congelarse textos OCR útiles por grupo, sin permitir que derivados o reenvíos del mismo original crucen splits.

El auditor exige actualmente al menos cinco grupos por etiqueta y cinco grupos de entrenamiento efectivos para cada clase visual. Ese umbral habilita experimentar; no garantiza suficiencia estadística ni aptitud productiva.

## Conclusión

**PROMETEDOR PERO FALTA CORPUS**.

La separación visual/textual y el esquema de abstención son técnicamente compatibles con un probe local independiente. Sin embargo, el corpus confirmado actual no permite entrenar ni medir generalización de manera honesta. No corresponde integrar IA ni continuar ajustando reglas negativas por ruido hasta ampliar y revisar el dataset.

## H1D3A2 — construcción asistida del corpus

`DocumentAiProbe` incorpora una utilidad de corpus independiente con estos comandos:

- `migrate`: copia las filas históricas a `Corpus/<LABEL>/`, registra `OriginalPath` y nunca mueve originales.
- `add --file <ruta> --label <clase> --group <id> --source-type <tipo> [--diversity <categoría>]`: calcula SHA-256, valida etiqueta/grupo, copia el archivo y agrega la fila. La etiqueta siempre es explícita; la carpeta de origen no la decide.
- `split`: asigna TRAIN/VALIDATION/TEST determinísticamente por `GroupId` y conserva los grupos TEST en `frozen-test-groups.txt`.
- `audit`: detecta duplicados exactos, conflictos de etiqueta, fugas entre splits, archivos desaparecidos, hashes alterados, clases insuficientes y desequilibrio superior a 2:1 por grupos.
- `report`: genera `corpus-report.md` con distribución, diversidad, pendientes y estado global.

La estructura real quedó:

```text
tools/DocumentAiProbe/
  Corpus/
    FACTURA/
    OTRO_DOCUMENTO/
    NO_DOCUMENTO/
  dataset.csv
  frozen-test-groups.txt
  corpus-report.md
```

La migración creó 13 copias y movió cero originales bajo `C:\RecepcionDocumental`. El manifiesto contiene ahora `Path`, `Label`, `GroupId`, `SourceType`, `Sha256`, `Split`, `OriginalPath` y `Diversity`.

El objetivo para pasar de `INSUFICIENTE` a `APTO_PARA_PRIMER_EXPERIMENTO` es 20 grupos independientes por cada clase. Actualmente existe 1 grupo FACTURA y 3 grupos en cada clase restante, por lo que faltan 19 FACTURA, 17 OTRO_DOCUMENTO y 17 NO_DOCUMENTO. Este umbral sólo habilita el primer experimento serio y no garantiza suficiencia estadística ni aptitud productiva.

H1D3A2b corrigió además la portabilidad: `Path` usa valores como `Corpus\FACTURA\archivo.pdf`, resueltos contra el directorio del dataset. Las rutas absolutas preexistentes sólo se aceptan para migración. Hash distinto no implica independencia: SHA-256 controla duplicados exactos y `GroupId` controla variantes semánticas o derivadas del mismo origen.

La diversidad actual por grupos comprende PDF escaneado y JPG adjunto para FACTURA; orden de compra, credencial y comprobante de pago para OTRO_DOCUMENTO; fotografía, firma y newsletter/publicidad para NO_DOCUMENTO. Las categorías faltantes deberán provenir de originales independientes, no de transformaciones pequeñas.

La augmentation queda prevista únicamente para TRAIN. Toda variante conservará el `GroupId` original y nunca se utilizará como evidencia independiente ni en VALIDATION/TEST. No se implementó augmentation ni se entrenó ningún modelo en H1D3A2.

## H1D3A2c — selección validada del banco DMF

Se incorporaron mediante el comando `add` 17 PDF seleccionados manualmente del banco DMF extraído en `C:\temp\Pruebas\Facturas\_Extraidas\20260826-113613-1eb93599`. Antes de copiar se verificó que cada nombre apareciera exactamente una vez, que los 17 SHA-256 fueran distintos entre sí y que ninguno existiera en `dataset.csv`.

La selección añadió 15 archivos FACTURA organizados en nueve familias de template/origen y dos archivos OTRO_DOCUMENTO en dos grupos. La agrupación deliberada conserva juntos layouts fuertemente relacionados: cinco ARCA FCE standard, dos condominio PDI y dos Aquavita. Los restantes grupos FACTURA corresponden a inmuebles FCE, Vitalis, Ascensores Orem, Excell, Arillo y Manantial.

Distribución resultante:

| Clase | Archivos | Hashes únicos | Grupos | Pendientes hasta 20 |
|---|---:|---:|---:|---:|
| FACTURA | 18 | 18 | 10 | 10 |
| OTRO_DOCUMENTO | 5 | 5 | 5 | 15 |
| NO_DOCUMENTO | 7 | 7 | 3 | 17 |
| **Total** | **30** | **30** | **18** | — |

La regeneración determinística mantuvo congelados `factura-homologacion-c-00004-00000002`, `comprobante-pago-bancario` y `flightaware-newsletter`, y añadió `factura-familia-arillo` a `frozen-test-groups.txt` para alcanzar la proporción TEST de la clase FACTURA. No se detectaron hashes duplicados, grupos con etiquetas mezcladas, fugas entre splits, rutas absolutas en `Path` ni archivos alterados.

El estado continúa en `INSUFICIENTE`; el corpus permanece desequilibrado y ninguna clase alcanzó todavía 20 grupos independientes. No se entrenó ningún modelo.

## H1D3A2e — importación auditada de candidatos revisados

`DocumentAiProbe` incorporó `import-reviewed <csv> [--dry-run]`. El comando valida el lote completo antes de cualquier escritura y reconoce decisiones `ADD`, `SKIP` y `PENDING`. Controla CandidateId y evidencia repetidos, archivos inexistentes, SHA-256 distinto, etiquetas/GroupId inválidos, hashes ya presentes y conflictos de etiqueta. La ejecución real llama al mismo camino interno utilizado por `add` para validar, copiar y persistir cada evidencia.

El archivo `reviewed-decisions.csv` registra 36 decisiones humanas: 2 `ADD`, 33 `SKIP` y 1 `PENDING`. El dry-run finalizó sin errores y mantuvo idénticos el hash de `dataset.csv` y los 30 archivos preexistentes. Se incorporaron únicamente R0002, una nota de crédito electrónica, y R0003, un resumen de operaciones Banco Comafi, ambos como grupos nuevos de `OTRO_DOCUMENTO`. No se incorporaron variantes ya representadas de la factura de homologación, FlightAware ni el comprobante de pago; el DOCX R0004 quedó pendiente.

Distribución posterior:

| Clase | Archivos | Hashes únicos | Grupos | Pendientes hasta 20 |
|---|---:|---:|---:|---:|
| FACTURA | 18 | 18 | 10 | 10 |
| OTRO_DOCUMENTO | 7 | 7 | 7 | 13 |
| NO_DOCUMENTO | 7 | 7 | 3 | 17 |
| **Total** | **32** | **32** | **20** | — |

La auditoría confirmó cero duplicados exactos, cero fugas entre splits, cero grupos con etiquetas mezcladas, rutas `Path` relativas y hashes de corpus válidos. Los cuatro grupos TEST previamente congelados permanecieron sin cambios. El estado global continúa `INSUFICIENTE`; no se entrenó IA ni se modificó el producto.
