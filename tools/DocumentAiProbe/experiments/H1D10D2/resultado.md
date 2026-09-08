# H1D10D2 — residual real después del resolver documental

Diagnóstico aislado sobre los 150 hard cases seleccionados por H1D10B3.

La decisión determinística **no usa** la revisión visual de ChatGPT. Esa revisión se conserva sólo para describir el residual y no constituye ground truth.

## Resultado principal

- Top150: **150**
- SHA únicos: **150**
- Familias: **64**
- Resueltos por D1 + D1b + D1c: **58** (38.7%)
- FACTURA: **13**
- OTRO_DOCUMENTO: **45**
- QR ARCA válidos que resolvieron casos del Top150: **0**
- Residual / NO_DECISION: **92** (61.3%)

## Por qué resolvió

- `SPECIFIC_TITLE_PRECEDENCE`: **45**
- `FACTURA_TITLE_ONLY_STRONG_POSITIONAL`: **13**
- `QR_ARCA_VALID_*`: **0**
- `NO_DECISION_SECONDARY_FACTURA`: **23**
- `NO_DECISION`: **69**

## Residual diagnóstico

Según la referencia visual de ChatGPT —sólo diagnóstica, no ground truth—:

- transferencia/anticipo: **30**
- correo/documento administrativo: **32**
- documentos fiscales/recibos conocidos con evidencia insuficiente para el gate: **11**
- comprobante bancario: **4**
- impuesto/boleta: **1**
- otro administrativo/genérico: **14**

Necesidad diagnóstica:

- lenguaje/contexto claramente semántico: **62**
- semántica o estructura documental: **19**
- primero cerrar una brecha determinística de evidencia fiscal/percepción: **11**

## Comparación con la revisión visual

Entre los **58** casos decididos determinísticamente:

- coincidencias binarias con la referencia visual: **56**
- desacuerdos: **2**

Esto **no** se interpreta como accuracy: la referencia visual no es ground truth. Los desacuerdos se exportan para inspección separada.

## Conclusión

El resolver determinístico actual elimina una parte importante del hard set sin entrenamiento, pero el residual sigue siendo material. No corresponde ampliarlo con una lista indefinida de regex.

La siguiente decisión arquitectónica debe separar:

1. casos fiscales conocidos donde todavía falta fusionar mejor evidencia determinística/perceptiva;
2. lenguaje administrativo/transferencias/correos que sí requiere comprensión semántica;
3. otros documentos estructurados que pueden requerir semántica, estructura o ambos.

No se implementa todavía una nueva IA.

## Gates

- `SealedAssetsRead = false`
- `TrainingExecuted = false`
- `ProductionModified = false`
- `GroundTruthModified = false`
- `ModelsPromoted = false`
- `ModelInferenceExecuted = false`
