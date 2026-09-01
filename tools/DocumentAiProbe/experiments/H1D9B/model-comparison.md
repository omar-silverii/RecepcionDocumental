# Comparación de modelos H1D9B

- Criterio principal: seguridad FACTURA y métricas group-aware.
- Ganador OOF: **EfficientNet-B0**.
- Gate candidato: **APROBADO COMO CANDIDATO**.

EfficientNet-B0 obtuvo recall FACTURA group-level 0.9231, balanced accuracy 0.8643 y ROC-AUC 0.8868. MobileNetV3-Large logró ROC-AUC 0.8739, pero a threshold 0.5 su recall FACTURA group-level cayó a 0.3077. Según la prioridad definida —seguridad sobre FACTURA antes que accuracy o tamaño— MobileNetV3-Large queda descartado.

EfficientNet mantuvo recall FACTURA group-level 1.0 en cuatro folds y 0.5 en uno; MobileNet colapsó a recall 0 en tres folds. EfficientNet también tuvo variación de ROC-AUC por fold (0.7143–1.0), por lo que el corpus sigue siendo pequeño y H1D9C deberá tratar el candidato con cautela, pero la separación agregada supera claramente el azar y satisface el gate de candidato.

EfficientNet-B0 también produjo zonas fuertes libres de errores conocidos: 17/70 archivos y 11/49 grupos entre `NO_FACTURA_FUERTE` y `FACTURA_FUERTE`. Los thresholds son exclusivamente diagnósticos OOF y no se integraron al producto.
