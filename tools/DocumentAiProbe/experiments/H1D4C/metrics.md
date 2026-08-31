# H1D4C — evaluación textual group-aware

## Nota de cierre

El primer cierre completo obtuvo como mejor resultado `B_WORD_NGRAMS`, con 3/18 FACTURA, recall `0,166667` y macro F1 `0,420202`. Al regenerar para aplicar la corrección de fidelidad end-to-end, SDCA mostró variación estocástica y los CSV finales registraron otra selección, también insuficiente. Se preserva como conclusión canónica la evaluación negativa: ninguna variante aprende razonablemente y H1D4C no supera H1D4B en TEST.

- Desarrollo: TRAIN+VALIDATION H1D4A, 41 archivos, 28 grupos.
- Folds: 5, estratificados por clase y atómicos por GroupId.
- Selección: sólo predicciones out-of-fold; TEST no intervino.

## Métricas CV agregadas

| Variante | Accuracy | P Factura | R Factura | F1 Factura | P Otro | R Otro | F1 Otro | Macro F1 | Matriz TP/TN/FP/FN |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| A_BASELINE | 0.317073 | 0.142857 | 0.111111 | 0.125000 | 0.407407 | 0.478261 | 0.440000 | 0.282500 | 2/11/12/16 |
| B_WORD_NGRAMS | 0.439024 | 0.272727 | 0.166667 | 0.206897 | 0.500000 | 0.652174 | 0.566038 | 0.386467 | 3/15/8/15 |
| C_WORD_CHAR_NGRAMS | 0.268293 | 0.200000 | 0.222222 | 0.210526 | 0.333333 | 0.304348 | 0.318182 | 0.264354 | 4/7/16/14 |
| D_HYBRID_FISCAL | 0.487805 | 0.333333 | 0.166667 | 0.222222 | 0.531250 | 0.739130 | 0.618182 | 0.420202 | 3/17/6/15 |

## Ganador

`C_WORD_CHAR_NGRAMS`, elegido primero por recall FACTURA, luego macro F1, FN y accuracy.

## Condición de parada

Ninguna variante aprendió razonablemente en evaluación group-aware. Incluso el ganador sólo detectó 4 de 18 facturas fuera de fold (recall 0,222222) y dejó 14 FACTURA→OTRO_DOCUMENTO. Se detiene la búsqueda sin recolectar datos ni ampliar automáticamente el espacio experimental.

## TEST H1D4A — regresión experimental

Este TEST ya fue observado en H1D4A/H1D4B; no constituye certificación final independiente.

| | Pred FACTURA | Pred OTRO |
|---|---:|---:|
| Real FACTURA | 1 | 5 |
| Real OTRO | 0 | 3 |

- Accuracy=0.444444; recall FACTURA=0.166667; F1 FACTURA=0.285714; recall OTRO=1.000000; F1 OTRO=0.545455; macro F1=0.415584

### Comparación textual en TEST

| Experimento | Recall FACTURA | Macro F1 | FACTURA→OTRO_DOCUMENTO |
|---|---:|---:|---:|
| H1D4A, threshold 0,50 | 0,000000 | 0,250000 | 6 |
| H1D4B calibrado | 0,166667 | 0,415584 | 5 |
| H1D4C ganador | 0,166667 | 0,415584 | 5 |

H1D4C no mejora el resultado textual de H1D4B.

## Integración temporal con visual H1D4B (threshold 0,726)

| real/pred | FACTURA | OTRO_DOCUMENTO | NO_DOCUMENTO |
|---|---:|---:|---:|
| FACTURA | 1 | 5 | 0 |
| OTRO_DOCUMENTO | 0 | 3 | 0 |
| NO_DOCUMENTO | 0 | 1 | 5 |

- Accuracy=0.600000; macro F1=0.564935; recall FACTURA=0.166667; recall OTRO_DOCUMENTO=1.000000; recall NO_DOCUMENTO=0.833333
- FACTURA→OTRO_DOCUMENTO=5; FACTURA→NO_DOCUMENTO=0

## Conclusión técnica

Cambiar entre estas representaciones dispersas y añadir señales fiscales explícitas no produce generalización suficiente entre GroupId. El problema no queda resuelto por n-grams ni por las señales híbridas ensayadas. No se recomienda reemplazar el modelo textual H1D4A ni integrar este artefacto en producto.

