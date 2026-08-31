# H1D5A — Resumen ejecutivo

> Diagnóstico experimental; no define ni implementa un quality gate.

1. **PDF que pasan hoy `Mdoc.HasUsefulText`:** 30 de 36.
2. **Con degradación técnica evidente:** 15 de los que pasan.
3. **Degradaciones observadas:** NUL en 9, controles en 11, fragmentación alta en 4 y tokens anormalmente largos en 6 PDF.
4. **Cambios de clasificación usando OCR:** 9 PDF.
5. **FACTURA donde OCR aporta evidencia mejor:** 6.
6. **OTRO_DOCUMENTO donde OCR evita/genera falso FACTURA:** 0/0.
7. **Aporte QR ARCA:** 7 QR válidos; cambia Mdoc en 0 y OCR en 0 PDF.
8. **Evidencia para futuro quality gate Mdoc→OCR:** sí, existen casos degradados donde cambia el resultado; H1D5B deberá diseñarlo sin calibrar thresholds sobre TEST.
9. **Casos que continúan en REVISAR:** 12 PDF tras QR+OCR y 39 imágenes tras OCR.
