# H1D10D4 — auditoría de fallback textual y residual semántico

Diagnóstico posterior a H1D10D3.

No agrega reglas productivas, no entrena y no ejecuta inferencia nueva. Reutiliza:

- `H1D10D3/independent-regression-d3.csv`;
- `H1D10D3/residual-d3.csv`;
- OOF group-aware de H1D10B1/B3;
- scores Top150 ya calculados.

## Pregunta

¿Puede el TEXT_ONLY actual convertirse simplemente en fallback de las abstenciones D3?

La respuesta se decide primero contra las 734 filas independientes con ground truth. El Top150 se usa sólo para caracterizar el residual; la revisión visual de ChatGPT no es verdad productiva.

## Ejecutar

```bash
python Run-H1D10D4.py
python Test-H1D10D4.py
```
