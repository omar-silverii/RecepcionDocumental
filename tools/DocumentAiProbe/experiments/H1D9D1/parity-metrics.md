# H1D9D1 — Métricas

- Source RGB iguales: 80/80.
- ONNX C# con tensor Python: delta máximo 0.
- Target RGB iguales: 0/80.
- Tensor: media 0.190062, máximo 4.464286, floats distintos 1339788.
- Pred050: 78/80; zonas: 76/80.
- PFactura delta: media 0.018621, P95 0.108028, máximo 0.461397.
- Decode ms: medido dentro de carga inicial no temporizada individualmente.
- Resize/letterbox ms: media 244.56156, P50 313.81225, P95 480.67603, máximo 1050.3688.
- Normalización ms: media 0.699026, P50 0.618, P95 0.941415, máximo 1.1215.
- ONNX ms: media 7.109603, P50 7.2946, P95 8.35635, máximo 11.2557.
- Total ms: media 254.130799, P50 321.8836, P95 488.87186, máximo 1058.6052.
- HOLDOUT: 10/10 archivos, 5/5 grupos, 0 errores fuertes.

Gates: decode=PASS, ONNX tensor Python=PASS, target RGB=FAIL, tensor=FAIL, ONNX final=FAIL, HOLDOUT=PASS.
