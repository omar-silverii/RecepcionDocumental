# H1D10D2 — residual real después del resolver documental

Experimento diagnóstico aislado.

Objetivo: aplicar al `Top150` el resolver ya validado por H1D10D1/D1b/D1c y medir qué queda sin decisión antes de diseñar una capa semántica.

Reglas de seguridad:

- no entrena;
- no modifica producción;
- no modifica ground truth;
- no promueve modelos;
- no lee assets de `SEALED_TEST`;
- la revisión visual de ChatGPT se conserva sólo como referencia diagnóstica y nunca participa en la decisión determinística.

Ejecución:

`py -3 tools\DocumentAiProbe\experiments\H1D10D2\Run-H1D10D2.py`

Pruebas:

`py -3 tools\DocumentAiProbe\experiments\H1D10D2\Test-H1D10D2.py`

Salidas:

- `resultado.md`
- `summary.json`
- `top150-resolved-d2.csv`
- `residual-d2.csv`
- `visual-reference-disagreements-d2.csv`
