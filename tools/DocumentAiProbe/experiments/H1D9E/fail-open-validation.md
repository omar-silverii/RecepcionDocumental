# Fail-open H1D9E

- Shadow deshabilitado: 80 documentos; cero actividad visual, cero rasterizaciones adicionales y cero sesión ONNX.
- Directorio/modelo ausente: `ERROR / MODEL_MISSING`; proceso continúa.
- Manifest con SHA incorrecto: `ERROR / CONTRACT_INVALID`; modelo no cargado y proceso continúa.
- Los errores shadow no modifican `InvoiceSelection` ni impiden guardar el documento.
