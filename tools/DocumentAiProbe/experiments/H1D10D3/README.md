# H1D10D3 — fusión de evidencia fiscal/estructural

Experimento diagnóstico aislado posterior a H1D10D2.

Objetivo: reducir la brecha de evidencia fiscal/perceptiva usando señales generales de alta precisión antes de diseñar una capa semántica.

Principios:

- D1c y D2 son baseline inmutable;
- D3 sólo puede promover abstenciones;
- no modifica decisiones ya tomadas;
- no usa la revisión visual de ChatGPT para decidir;
- no entrena;
- no modifica producción;
- no modifica ground truth;
- no promueve modelos;
- no lee assets de `SEALED_TEST`.

Señales D3:

1. código fiscal `03` + estructura documental => `OTRO_DOCUMENTO`;
2. título `FACTURA DE CREDITO` + numeración/estructura => `FACTURA`;
3. `FACTURA` + número de comprobante + múltiples anclas fiscales => `FACTURA`.

No se agregan reglas por proveedor ni por SHA.

Ejecución:

`py -3 tools\DocumentAiProbe\experiments\H1D10D3\Run-H1D10D3.py`

Pruebas:

`py -3 tools\DocumentAiProbe\experiments\H1D10D3\Test-H1D10D3.py`

Salidas:

- `resultado.md`
- `summary.json`
- `independent-regression-d3.csv`
- `independent-fusion-signal-audit-d3.csv`
- `top150-fused-d3.csv`
- `promotions-d3.csv`
- `residual-d3.csv`
- `remaining-fiscal-gap-d3.csv`
