# H1D9E — Integración productiva SHADOW

`H1D9E APROBADO — SHADOW PRODUCTIVO`

- Build WebForms .NET Framework 4.8: correcto; IIS Express x64.
- Microsoft.ML.OnnxRuntime CPU 1.29.0: sesión real correcta, única y lazy.
- Modelo: `H1D9B-CANDIDATE-001`, SHA `A1DC24FE90C3C14303C3319EC0BD6D9EF95E91CC82C9B38E2FBAAEB0EF826811`, 16.708.744 bytes.
- Disabled: 80/80 clasificación histórica; sin actividad visual.
- Enabled: FACTURA 18, REVISAR 52, DESCARTAR 10; cambios de clasificación 0.
- Elegibles/attempted/OK: 70/70/70; descartados evaluados: 0.
- Paridad H1D9D1C: Pred050 70/70, zona 70/70, delta PFactura máximo 0.
- Raster OCR reutilizado: 19; primera página shadow nueva: 11; imágenes canonicalizadas: 40.
- SQL: migración, FK, checks, unique, índice, idempotencia y rollback controlado aprobados.
- Fail-open: modelo ausente y contrato/hash incorrecto producen `ERROR` sin afectar clasificación.
- No hubo red/cloud, Python/Torch en runtime, entrenamiento, tuning, cambios de UI ni uso del checkpoint.
