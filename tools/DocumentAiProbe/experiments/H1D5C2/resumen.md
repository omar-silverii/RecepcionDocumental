## Estado histórico

- H1D5C: NO APROBADO (embedded-first, 35/36).
- H1D5C1: benchmark experimental que seleccionó P1.
- H1D5C2: corrección productiva controlada con raster de página completa.

# H1D5C2 — Resumen ejecutivo

- Producto y probe compilados con .NET Framework 4.8.
- PDF coincidentes con H1D5B: **36/36**; corpus: **80/80**.
- Matriz PDF FACTURA: 15 FACTURA, 7 REVISAR, 0 DESCARTAR.
- Matriz PDF OTRO_DOCUMENTO: 0 FACTURA, 8 REVISAR, 6 DESCARTAR.
- Promociones esperadas: 2/2; conflictos conservados en REVISAR con método de conflicto: 3/3.
- Tabla de verdad: 10/10 PASS, incluido Mdoc sin texto + OCR DESCARTAR.
- OCR activado en 21/36 PDF según instrumentación real.
- Tiempo PDF total/media/mediana/P95: 192089 / 5335.81 / 891.5 / 4772.75 ms.
- La medición corresponde a esta computadora y corpus; no constituye capacidad productiva final ni despliegue.

- Fuente OCR PDF validada: `RASTER_PAGINA`.
- Conflictos: 3/3; límites: 2/2.
- Cambios finales de clasificación frente a H1D5C: 1; E8066 pasó de REVISAR a FACTURA.
- H1D5C2 queda **APROBADO COMO CANDIDATO**, sin despliegue ni prueba operativa real.
