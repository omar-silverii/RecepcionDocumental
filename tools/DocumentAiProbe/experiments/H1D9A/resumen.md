# H1D9A — Validación real de ONNX Runtime

**APROBADO**

- Entorno: Microsoft Windows 10.0.19045, CLR 4.0.30319.42000, .NET Framework 4.8, proceso x64.
- ONNX Runtime administrado: 1.29.0.0, cargado desde `tools/PdfRasterProbe/bin/Microsoft.ML.OnnxRuntime.dll`.
- Runtime nativo CPU: `onnxruntime.dll`, versión de archivo 1.29.0.20260811.4.2e2543f, cargado en el proceso x64 desde el output del probe.
- Modelo local: `Assets/H1D9A/mul_1.onnx`, 130 bytes, procedente de `microsoft/onnxruntime`, ruta `onnxruntime/test/testdata/mul_1.onnx`.
- SHA-256: `71F431C4E9321EC6FBEB158D02ED240459A7DCC98673FA79A4F439CE42EFAF10`.
- Metadata validada: `X [3,2] float` → `Y [3,2] float`.
- Salida validada elemento por elemento: `1,4,9,16,25,36`.
- Inferencias repetidas correctas: 1000/1000 usando una única `InferenceSession`.
- Creación de sesión: 94.601 ms; P50: 0.008 ms; P95: 0.012 ms.
- Memoria privada antes del warm-up / después / después del lote: 33308672 / 34537472 / 35528704 bytes.
- Memoria GC administrada antes / después del lote: 1765008 / 2641552 bytes.
- Inferencia offline: el modelo se carga del output local y el probe no contiene ni invoca clientes de red. La red se utilizó solamente para la descarga inicial del asset y la restauración NuGet.
- GPU, CUDA, DirectML y servicios cloud: no utilizados.
- SQL, Gmail, OAuth y `DocumentAnalysisService`: no inicializados ni utilizados.
- WebForms, H1D8B, `InvoiceSelector`, `FusePdfSelections` y comportamiento productivo: no modificados.
- Dependencias de sistema adicionales detectadas: ninguna; el runtime nativo cargó correctamente con las dependencias disponibles en el equipo.

## Archivos modificados o agregados

- `tools/PdfRasterProbe/PdfRasterProbe.csproj`
- `tools/PdfRasterProbe/Program.cs`
- `tools/PdfRasterProbe/H1D9AOnnxRuntimeProbe.cs`
- `tools/PdfRasterProbe/Assets/H1D9A/mul_1.onnx`
- `tools/DocumentAiProbe/experiments/H1D9A/resumen.md`
- `tools/DocumentAiProbe/experiments/H1D9A/metrics.md`
- `tools/DocumentAiProbe/experiments/H1D9A/runtime-files.txt`

El cambio previo del usuario en `docs/PROJECT_STATUS.md` fue preservado y no forma parte de H1D9A.
