# Métricas H1D9A

## Sesión e inferencia

- Build: Release x64 exitoso.
- Creación de `InferenceSession`: 94.601 ms.
- Inferencia inicial y warm-up: correctos.
- Lote: 1000/1000 inferencias correctas con la misma sesión.
- Tiempo total del lote: 8.651 ms.
- Promedio: 0.009 ms.
- P50: 0.008 ms.
- P95: 0.012 ms.
- Máximo: 0.105 ms.
- Tolerancia por elemento: 0.000001 float.

## Memoria diagnóstica

- PrivateMemory antes del warm-up: 33308672 bytes.
- PrivateMemory después del warm-up: 34537472 bytes.
- PrivateMemory después del lote: 35528704 bytes.
- GC managed memory antes del lote: 1765008 bytes.
- GC managed memory después del lote: 2641552 bytes.
- No se observaron excepciones, corrupción ni crecimiento catastrófico.

## Build

MSBuild conservó el target .NET Framework 4.8 y x64. Se observaron advertencias preexistentes de unificación de versiones de `System.Buffers`, `System.Memory` y `System.Numerics.Vectors`, además de dos usos obsoletos en el probe histórico H1D7A. No impidieron el build ni la carga/ejecución de ONNX Runtime y no se alteró producto para eliminarlas.
