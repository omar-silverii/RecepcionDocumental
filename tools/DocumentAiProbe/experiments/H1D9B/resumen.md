# H1D9B — Benchmark visual FACTURA vs NO_FACTURA

**APROBADO COMO CANDIDATO**

- Assets: 70/70 de desarrollo; TEST congelado no rasterizado ni utilizado por el entrenador.
- Folds: 5, estratificados por clase y agrupados por GroupId; leakage GroupId/SHA/TEST = 0.
- Ganador: EfficientNet-B0.
- EfficientNet-B0 group-level: recall FACTURA 0.9231, balanced accuracy 0.8643, ROC-AUC 0.8868 y PR-AUC 0.7342.
- MobileNetV3-Large group-level: recall FACTURA 0.3077, balanced accuracy 0.6261, ROC-AUC 0.8739 y PR-AUC 0.6619; no ganó porque colapsó el recall de FACTURA a threshold 0.5.
- Zonas fuertes EfficientNet-B0: `T_NO_FACTURA=0.1169927716`, `T_FACTURA=0.7979831696`; cobertura 17/70 archivos y 11/49 grupos, con 0 errores conocidos en ambas zonas.
- Candidato final: `H1D9B-CANDIDATE-001`, EfficientNet-B0, 8 épocas de head y 6 de ajuste del último bloque.
- ONNX autocontenido: 16708744 bytes; SHA-256 `A1DC24FE90C3C14303C3319EC0BD6D9EF95E91CC82C9B38E2FBAAEB0EF826811`.
- Checkpoint entrenable: 16339269 bytes; SHA-256 `F6F552CF5FAD856D7FB57352C63C4CD68C3E3E0F6C039C3C7623B030FD965F27`.
- Paridad PyTorch/ONNX Runtime: error absoluto máximo 0.0000139475 sobre 10 imágenes de desarrollo.
- ONNX Runtime CPU: media 7.487 ms y P95 7.867 ms sobre 10 imágenes de desarrollo.
- Producto WebForms, H1D8B, SQL, Gmail, OCR, QR y clasificación productiva no fueron modificados.
- H1D9C no fue ejecutado.
