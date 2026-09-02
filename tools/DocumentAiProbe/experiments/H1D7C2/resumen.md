# H1D7C2 — certificación de desarrollo

**H1D7C2 APROBADO** — 2026-09-02.

Base del hito: `62a32d81ecdaa8d53d0a1c83a76ade866b6515ac`, árbol limpio antes de iniciar H1D7C2. Aplicación de 010 a la base real de desarrollo autorizada expresamente por Omar. No se creó un commit durante la certificación.

## Migración

- 010 original SHA-256: `6051BBA70A5EB0D0B6616576A3EC7FD532819CCD6B703D5DE39085FA54BA33B0`.
- Gate aislado previamente aprobado: inyección SQL 51001 observada, rollback sin objetos/índices/backfill parcial y CHECK original restaurado exactamente; esquema completo e idempotencia verificados; base temporal eliminada.
- Base real: primera y segunda aplicación con sqlcmd `-b`, ambas exit code 0.
- Muestras idénticas entre ejecuciones; fingerprint de clasificaciones/decisiones humanas y de todas las filas ground truth sin cambios antes/después de la migración y después de limpiar los fixtures.

| Conteo real | Primera aplicación | Segunda aplicación | Tras limpiar el probe |
|---|---:|---:|---:|
| FACTURA automáticas elegibles | 22 | 22 | 22 |
| Muestras seleccionadas | 0 | 0 | 0 |
| Muestras pendientes | 0 | 0 | 0 |
| Ground truth existentes | 0 | 0 | 0 |

El backfill real produjo 0/22 (0%). No se forzó una cuota ni se cambió la regla: una selección determinística aproximadamente al 10% no garantiza seleccionar documentos en un conjunto pequeño. No se infirieron decisiones históricas.

## Builds y probe

- WebForms `Release|Any CPU`: exit code 0, 0 errores.
- PdfRasterProbe net48, PlatformTarget=x64, Prefer32Bit=false: build exit code 0, 0 errores; 4 warnings MSB3277 preexistentes.
- Probe H1D7C2 ejecutado síncronamente: `ProcessX64=True`, exit code 0, `Gate=True`.
- Distribución de 1000 SHA-256 sintéticos, buckets 0–9: `111,94,104,95,102,104,92,84,104,110`. Seleccionados: 111/1000 (11,1%). Determinismo y rechazo de hashes inválidos verificados.
- Selección posterior a persistencia; reintentos sin duplicados; FACTURA seleccionada conserva clasificación original; REVISAR, descartados y resueltos excluidos.
- Cola y navegación independientes; contrato UI ciego verificado por inspección automática de markup/código (no mediante navegador).
- FACTURA / OTRO_DOCUMENTO / NO_DOCUMENTO producen decisión operativa, ground truth y enlace de muestra correctos; fuente MUESTREO_FACTURA_CIEGO; una sola fila vigente y Secuencia=1.
- Falsos positivos preservados con hash/tamaño correctos y sin copia huérfana en Facturas. Destinos preexistentes preservados.
- Concurrencia: un ganador, segundo intento no exitoso, sin excepción. Atomicidad ante fallo controlado de INSERT ground truth: decisión/muestra pendientes, ground truth cero y copia nueva compensada.
- Shadow opcional: casos con y sin enlace; selección y UI independientes del score. No se da autoridad al shadow.
- Fallo de muestreo inducido sólo para un mensaje fixture: documento persistido, clasificación y cursor conservados, sin duplicados. Gmail API real no utilizada.
- Fixtures retirados de SQL y archivos; `FixtureCleanup=True`. Después del probe: cero PdfRasterProbe y apertura exclusiva de RecepcionDocumental.dll exitosa. No se terminaron procesos ni se repitieron los tres ciclos H1D7C1.
- Cinco hashes congelados exactos; sin cambios a dataset/modelo, thresholds, OCR o H1D9E1A. Sin training ni tuning.

## Evidencia

- `migration-evidence.txt`: gate aislado.
- `development-migration-1.txt`, `development-migration-2.txt`: aplicación real e idempotencia.
- `development-build-webforms.log`, `development-build-probe.log`: builds.
- `probe-evidence.txt`: gates funcionales y hashes.
- `development-certification.txt`: ejecución completa, exit codes, conteos y lifecycle.

Meta de recolección posterior: 100 decisiones humanas de FACTURA automáticas muestreadas, antes de la siguiente evaluación estadística. Esta certificación valida el mecanismo; no afirma haber alcanzado esa meta ni haber medido la precisión productiva.
