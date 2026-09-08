# H1D10D1 — Normalización canónica de títulos

Experimento diagnóstico. No modifica producción.

## Objetivo

Probar la hipótesis de que errores OCR como:

`NOTA D E D É B ITO`

deben compararse en una forma canónica:

`NOTADEDEBITO`

sin agregar a mano cada variante de espaciado.

Además separa deliberadamente:
- títulos documentales explícitos: FACTURA / NOTA_CREDITO / NOTA_DEBITO / RECIBO;
- cues semánticos: TRANSFERENCIA / ANTICIPO.

## Ubicación

Copiar esta carpeta completa como:

`tools\DocumentAiProbe\experiments\H1D10D1\`

Requiere que H1D10C ya haya sido ejecutado y exista:

`tools\DocumentAiProbe\experiments\H1D10C\local-results\cases.csv`

## Pruebas

Desde la raíz del proyecto:

`py -3 tools\DocumentAiProbe\experiments\H1D10D1\Test-H1D10D1.py`

## Ejecución

`py -3 tools\DocumentAiProbe\experiments\H1D10D1\Run-H1D10D1.py`

## Salidas

`tools\DocumentAiProbe\experiments\H1D10D1\local-results\resultado.md`

`tools\DocumentAiProbe\experiments\H1D10D1\local-results\summary.json`

`tools\DocumentAiProbe\experiments\H1D10D1\local-results\top150-canonical-title.csv`

`tools\DocumentAiProbe\experiments\H1D10D1\local-results\independent-regression.csv`

Subir `resultado.md`, `summary.json` y, si hubo falsos positivos, `independent-regression.csv`.

## Seguridad metodológica

El script filtra `SEALED_TEST` antes de abrir cualquier `HeaderTextAsset`.
No entrena, no ejecuta modelos, no cambia labels y no toca producción.
