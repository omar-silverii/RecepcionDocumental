# H1D10D3 — fusión de evidencia fiscal/estructural

Experimento diagnóstico aislado posterior a H1D10D2.

No usa la revisión visual de ChatGPT para decidir. La fusión se apoya únicamente en texto/OCR no sellado y en señales fiscales/estructurales generales.

## Regresión independiente — 734 filas

Baseline H1D10D1c:

- decisiones: **727**
- incorrectas: **0**

Después de D3:

- decisiones: **728**
- correctas al decidir: **728**
- incorrectas al decidir: **0**
- precisión al decidir: **1.000000**
- NO_DECISION: **6**
- nuevas promociones sobre abstenciones D1c: **1**

Además, la señal de fusión se auditó sobre las 734 filas, incluso cuando D1c ya había decidido:

- filas donde la señal D3 se activó: **22**
- compatibles con ground truth: **22**
- incompatibles con ground truth: **0**
- precisión observada de la señal: **1.000000**

Por motivo:

- `FISCAL_CODE_03_WITH_STRUCTURE`: **10**
- `FISCAL_CREDIT_INVOICE_TITLE_WITH_STRUCTURE`: **8**
- `STRUCTURED_FACTURA_NUMBER_WITH_FISCAL_ANCHORS`: **4**

## Top150

Antes, D2 resolvía **58 / 150**.

Después de la fusión D3:

- resueltos: **64 / 150**
- nuevas promociones: **6**
- residual: **86**
- brecha fiscal/recibo conocida: **11 → 5**

Promociones por motivo:

- código fiscal 03 + estructura: **2**
- FACTURA DE CREDITO + estructura/numeración: **3**
- FACTURA + número + múltiples anclas fiscales: **1**

La comparación posterior con la referencia visual da **6 coincidencias y 0 desacuerdos**, pero esto **no es accuracy** porque esa referencia no es ground truth.

## Qué NO se forzó

D3 deja abstenciones donde la evidencia general todavía no alcanza. En particular no convierte automáticamente una `Liquidación de Servicios Públicos (LSP)` en FACTURA ni confía en una etiqueta visual de RECIBO/FACTURA para suplir evidencia faltante.

Los casos restantes de la brecha fiscal/perceptiva se exportan en `remaining-fiscal-gap-d3.csv` para decidir si corresponden a:

1. mejora de percepción;
2. definición/política documental;
3. estructura específica generalizable;
4. capa semántica.

## Conclusión

La fusión fiscal/estructural reduce la brecha sin degradar la regresión independiente. No corresponde seguir agregando reglas para maximizar cobertura artificialmente: el residual que queda es más limpio para diseñar la futura capa semántica.

## Gates

- `SealedAssetsRead = false`
- `TrainingExecuted = false`
- `ProductionModified = false`
- `GroundTruthModified = false`
- `ModelsPromoted = false`
- `ModelInferenceExecuted = false`
