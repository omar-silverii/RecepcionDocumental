# H1D10D4 — auditoría del residual y del fallback textual existente

Experimento diagnóstico aislado. No entrena ni ejecuta un modelo nuevo.
Reutiliza scores OOF group-aware ya generados por H1D10B y resultados D3.

## Hallazgo principal

No es seguro conectar TEXT_ONLY directamente detrás de D3 con threshold 0,5.

En las **6** abstenciones independientes de D3:

- ground truth: **6 OTRO_DOCUMENTO**;
- TEXT_ONLY @0,5: **6 FACTURA**;
- HEADER_TEXT @0,5: **6 FACTURA**;
- rango TEXT_ONLY P(FACTURA): **0.642502 – 0.768376**.

Si se hiciera el fallback ingenuo D3 + TEXT_ONLY@0,5:

- decisiones: **734 / 734**;
- correctas: **728**;
- incorrectas: **6**;
- precisión al decidir: **0.991826**.

Eso empeora el contrato de seguridad de D3, que tenía 0 errores al decidir.

## Señal útil que sí conserva TEXT_ONLY

La región de P(FACTURA) baja sigue siendo interesante para selección de candidatos:

| máximo P(FACTURA) | filas independientes | OTRO | FACTURA | precisión OTRO observada |
|---:|---:|---:|---:|---:|
| 0.25 | 1 | 1 | 0 | 1.000000000 |
| 0.30 | 2 | 2 | 0 | 1.000000000 |
| 0.35 | 3 | 3 | 0 | 1.000000000 |
| 0.40 | 16 | 15 | 1 | 0.937500000 |
| 0.45 | 21 | 20 | 1 | 0.952380952 |
| 0.50 | 24 | 23 | 1 | 0.958333333 |


Pero el soporte independiente de los thresholds muy bajos es pequeño; no corresponde convertir esto en regla productiva.

## Residual Top150 después de D3

- residual: **86 casos / 46 familias**;
- lenguaje/contexto: **62 casos / 27 familias**;
- semántica o estructura: **19 casos**;
- percepción/política determinística primero: **5 casos**.

Dentro de los **62** casos de lenguaje/contexto:

- **62 / 62** tienen TEXT_ONLY P(FACTURA) < 0,5;
- **56 / 62** están en P(FACTURA) <= 0,35;
- esto coincide diagnósticamente con la referencia visual, pero esa referencia **NO es ground truth**.

La zona <=0,35 contiene **56 casos / 24 familias** y se conserva sólo como cohorte candidata para evaluar una futura capa semántica.

## Concentración familiar

- top 4 familias: **36 / 86 = 41.9%**;
- top 10 familias: **50 / 86 = 58.1%**.

Esto obliga a mantener evaluación **group-aware por FamilyId**. Varias familias contienen más de un subtipo diagnóstico, por lo que FamilyId no debe usarse como sustituto del label.

## Conclusión

El bag-of-ngrams actual no es una segunda capa automática confiable para los hard negatives que D3 deja abiertos. Sí aporta una señal de ranking clara en correos/transferencias, pero esa señal no alcanza como contrato productivo.

El siguiente experimento no debe ser otra regla ni un threshold ajustado sobre estos casos. Debe comparar una **representación semántica preentrenada** contra el baseline actual, con folds group-aware, sin SEALED_TEST y con abstención explícita.

## Gates

- `SealedAssetsRead = false`
- `TrainingExecuted = false`
- `ProductionModified = false`
- `GroundTruthModified = false`
- `ModelsPromoted = false`
- `NewModelInferenceExecuted = false`
- revisión visual Top150: referencia diagnóstica, no ground truth.
