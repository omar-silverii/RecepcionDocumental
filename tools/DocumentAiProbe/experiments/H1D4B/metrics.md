# H1D4B — métricas y diagnóstico

## Conclusión

- **CASO A dominante:** el textual no aprende una frontera útil a 0,50 ni siquiera en TRAIN (recall FACTURA 0, macro F1 0,357143).
- **CASO C parcial:** calibrar produce una mejora medible, pero no corrige el problema de aprendizaje; en TEST textual el recall FACTURA sólo sube a 0,166667.
- **CASO B no demostrado:** no puede atribuirse el fracaso principalmente a generalización entre familias porque TRAIN ya falla.

## Matrices por split a threshold 0,50

### TRAIN

#### Visual (positivo = DOCUMENTO)

| | Pred + | Pred - |
|---|---:|---:|
| Real + | 36 | 0 |
| Real - | 8 | 13 |

- Accuracy=0.859649; F1 positivo=0.900000; macro F1=0.832353; recall positivo=1.000000


#### Textual (positivo = FACTURA)

| | Pred + | Pred - |
|---|---:|---:|
| Real + | 0 | 16 |
| Real - | 0 | 20 |

- Accuracy=0.555556; F1 positivo=0.000000; macro F1=0.357143; recall positivo=0.000000


### VALIDATION

#### Visual (positivo = DOCUMENTO)

| | Pred + | Pred - |
|---|---:|---:|
| Real + | 5 | 0 |
| Real - | 2 | 1 |

- Accuracy=0.750000; F1 positivo=0.833333; macro F1=0.666667; recall positivo=1.000000


#### Textual (positivo = FACTURA)

| | Pred + | Pred - |
|---|---:|---:|
| Real + | 0 | 2 |
| Real - | 0 | 3 |

- Accuracy=0.600000; F1 positivo=0.000000; macro F1=0.375000; recall positivo=0.000000


### TEST

#### Visual (positivo = DOCUMENTO)

| | Pred + | Pred - |
|---|---:|---:|
| Real + | 9 | 0 |
| Real - | 2 | 4 |

- Accuracy=0.866667; F1 positivo=0.900000; macro F1=0.850000; recall positivo=1.000000


#### Textual (positivo = FACTURA)

| | Pred + | Pred - |
|---|---:|---:|
| Real + | 0 | 6 |
| Real - | 0 | 3 |

- Accuracy=0.333333; F1 positivo=0.000000; macro F1=0.250000; recall positivo=0.000000


## Distribución de scores

| Modelo | Split | Clase | n | mínimo | máximo | promedio | mediana |
|---|---|---|---:|---:|---:|---:|---:|
| Visual | TRAIN | FACTURA | 16 | 0.561476 | 0.923244 | 0.843452 | 0.874959 |
| Textual | TRAIN | FACTURA | 16 | 0.439877 | 0.471079 | 0.456006 | 0.460490 |
| Visual | TRAIN | OTRO_DOCUMENTO | 20 | 0.598124 | 0.966424 | 0.849764 | 0.845585 |
| Textual | TRAIN | OTRO_DOCUMENTO | 20 | 0.431274 | 0.477478 | 0.442021 | 0.440668 |
| Visual | TRAIN | NO_DOCUMENTO | 21 | 0.006225 | 0.829096 | 0.406407 | 0.425833 |
| Visual | VALIDATION | FACTURA | 2 | 0.763506 | 0.866166 | 0.814836 | 0.814836 |
| Textual | VALIDATION | FACTURA | 2 | 0.443234 | 0.452980 | 0.448107 | 0.448107 |
| Visual | VALIDATION | OTRO_DOCUMENTO | 3 | 0.767536 | 0.888998 | 0.822574 | 0.811189 |
| Textual | VALIDATION | OTRO_DOCUMENTO | 3 | 0.450174 | 0.457602 | 0.452803 | 0.450634 |
| Visual | VALIDATION | NO_DOCUMENTO | 3 | 0.436269 | 0.998180 | 0.719827 | 0.725033 |
| Visual | TEST | FACTURA | 6 | 0.802389 | 0.870327 | 0.829240 | 0.820795 |
| Textual | TEST | FACTURA | 6 | 0.438300 | 0.460724 | 0.446537 | 0.447019 |
| Visual | TEST | OTRO_DOCUMENTO | 3 | 0.791883 | 0.822546 | 0.805516 | 0.802119 |
| Textual | TEST | OTRO_DOCUMENTO | 3 | 0.435137 | 0.441542 | 0.439115 | 0.440668 |
| Visual | TEST | NO_DOCUMENTO | 6 | 0.090837 | 0.742796 | 0.429882 | 0.398728 |

## Calibración exclusivamente con VALIDATION

- `visual-threshold-selected`: `0.726000`
- `textual-threshold-selected`: `0.452000`
- Visual: primero minimiza DOCUMENTO → NO_DOCUMENTO; luego F1 y accuracy.
- Textual: primero maximiza macro F1; luego recall FACTURA y accuracy.


## TEST — H1D4A threshold 0,50

#### Visual

| | Pred + | Pred - |
|---|---:|---:|
| Real + | 9 | 0 |
| Real - | 2 | 4 |

- Accuracy=0.866667; F1 positivo=0.900000; macro F1=0.850000; recall positivo=1.000000


#### Textual

| | Pred + | Pred - |
|---|---:|---:|
| Real + | 0 | 6 |
| Real - | 0 | 3 |

- Accuracy=0.333333; F1 positivo=0.000000; macro F1=0.250000; recall positivo=0.000000


### Matriz end-to-end 3×3

| real/pred | FACTURA | OTRO_DOCUMENTO | NO_DOCUMENTO |
|---|---:|---:|---:|
| FACTURA | 0 | 6 | 0 |
| OTRO_DOCUMENTO | 0 | 3 | 0 |
| NO_DOCUMENTO | 0 | 2 | 4 |

- Accuracy: 0.466667
- Macro F1: 0.409524
- Recall FACTURA: 0.000000
- Recall OTRO_DOCUMENTO: 1.000000
- Recall NO_DOCUMENTO: 0.666667

- FACTURA → NO_DOCUMENTO: 0
- FACTURA → OTRO_DOCUMENTO: 6

## TEST — H1D4B thresholds calibrados

#### Visual

| | Pred + | Pred - |
|---|---:|---:|
| Real + | 9 | 0 |
| Real - | 1 | 5 |

- Accuracy=0.933333; F1 positivo=0.947368; macro F1=0.928230; recall positivo=1.000000


#### Textual

| | Pred + | Pred - |
|---|---:|---:|
| Real + | 1 | 5 |
| Real - | 0 | 3 |

- Accuracy=0.444444; F1 positivo=0.285714; macro F1=0.415584; recall positivo=0.166667


### Matriz end-to-end 3×3

| real/pred | FACTURA | OTRO_DOCUMENTO | NO_DOCUMENTO |
|---|---:|---:|---:|
| FACTURA | 1 | 5 | 0 |
| OTRO_DOCUMENTO | 0 | 3 | 0 |
| NO_DOCUMENTO | 0 | 1 | 5 |

- Accuracy: 0.600000
- Macro F1: 0.564935
- Recall FACTURA: 0.166667
- Recall OTRO_DOCUMENTO: 1.000000
- Recall NO_DOCUMENTO: 0.833333

- FACTURA → NO_DOCUMENTO: 0
- FACTURA → OTRO_DOCUMENTO: 5

## Diagnóstico técnico del texto (sin contenido documental)

| Split | Clase | n | TextLen prom. | Origen | alfanumérico prom. | tokens prom. | FACTURA | CUIT | IVA | CAE/CAEA | TOTAL |
|---|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|
| TRAIN | FACTURA | 16 | 2791.125000 | MDOC=14; OCR=2 | 57.326926 | 402.750000 | 10 | 10 | 11 | 11 | 10 |
| TRAIN | OTRO_DOCUMENTO | 20 | 3156.700000 | MDOC=10; NONE=1; OCR=9 | 57.292893 | 438.250000 | 2 | 6 | 3 | 1 | 3 |
| VALIDATION | FACTURA | 2 | 2042.500000 | MDOC=2 | 62.313309 | 668.000000 | 1 | 1 | 1 | 1 | 1 |
| VALIDATION | OTRO_DOCUMENTO | 3 | 1622.666667 | MDOC=2; OCR=1 | 66.992029 | 288.000000 | 0 | 2 | 2 | 1 | 1 |
| TEST | FACTURA | 6 | 1348.500000 | MDOC=1; OCR=5 | 71.078632 | 202.333333 | 4 | 6 | 6 | 6 | 6 |
| TEST | OTRO_DOCUMENTO | 3 | 9229.000000 | MDOC=1; OCR=2 | 50.535524 | 1842.333333 | 0 | 0 | 0 | 0 | 1 |
