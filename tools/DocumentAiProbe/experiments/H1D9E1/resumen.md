# H1D9E1 — Resultado

`H1D9E1 NO APROBADO`

- `VisionShadowModelVersion` ahora selecciona la versión antes de construir la ruta; sólo `H1D9B-CANDIDATE-001` está admitida.
- Versión inexistente: 70/70 `MODEL_VERSION_UNSUPPORTED`, cero fallback, cero sesiones y cero cambios de clasificación.
- Contrato completo del manifest: validado; cinco corrupciones rechazadas antes de crear sesión.
- Regresión normal: 80 documentos; FACTURA=18, REVISAR=52, DESCARTAR=10; cambios=0; elegibles/OK=70/70.
- Paridad: 70/70 Pred050, 70/70 zona, delta máximo=0.
- Gate fallido: `70943A...` tuvo 1 página renderizada por OCR y 1 por shadow; existe doble render real de la primera página.
- SQL y UI intactos. Sin entrenamiento ni tuning.
