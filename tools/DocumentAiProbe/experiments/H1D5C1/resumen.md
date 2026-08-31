# H1D5C1 — Resumen ejecutivo

> H1D5C continúa no aprobado. No se implementó ninguna política productiva.

1. PDF con imágenes Mdoc utilizables: **15/36**; dentro del universo OCR: **6/21**.
2. Casos OCR con ambas fuentes y distinta clasificación embedded/raster: **2/21** (2/6 entre los que tienen fuente embebida).
3. P1_FULL_PAGE_RASTER reproduce H1D5B: **36/36**.
4. P2_EMBEDDED_THEN_RASTER_IF_REVIEW reproduce H1D5B: **36/36**.
5. P3_POSITIVE_RESCUE reproduce H1D5B: **36/36**.
6. Políticas con FACTURA→DESCARTAR: ninguna.
7. Políticas con falsos FACTURA: ninguna.
8. Rasterizaciones P2/P3: 21/21.
9. Costos medidos completos en `cost-metrics.csv`; incluyen extracción embebida, OCR y rasterización según cada política.
10. Ventaja de P3 al aceptar FACTURA embebida sin raster: **no evaluable en este corpus**, porque hubo 0 casos de FACTURA embedded entre los 21 PDF OCR; P3 resultó idéntica a P2.
11. Casos con límites raster controlados: 2.
12. Evidencia para H1D5C2: sí; candidata **P1_FULL_PAGE_RASTER**, la más simple entre las que logran 36/36 sin descartes de FACTURA ni falsos FACTURA.

E8066 se resolvió sin excepción por hash en P1/P2/P3: embedded=REVISAR, raster=FACTURA, resultados=FACTURA/FACTURA/FACTURA.
