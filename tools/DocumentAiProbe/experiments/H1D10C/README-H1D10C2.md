# H1D10C2 — inspección dirigida de los casos no resueltos

Diagnóstico solamente.

Toma `H1D10C/local-results/cases.csv` y exporta únicamente los casos:
- `NO_EXPECTED_CUE_IN_SAVED_TEXT`
- `CUE_PRESENT_OUTSIDE_HEADER_OR_HEADER_OCR_MISSED`

Incluye para cada caso:
- imagen fuente H1D10A;
- OCR de cabecera;
- texto seleccionado por H1D10A;
- texto nativo Mdoc;
- metadatos diagnósticos.

No vuelve a rasterizar.
No vuelve a OCRizar.
No entrena.
No cambia labels.
No toca holdout ni producción.

Desde la raíz de RecepcionDocumental:

    py -3 tools\DocumentAiProbe\experiments\H1D10C\Run-H1D10C2.py

Salida:

    tools\DocumentAiProbe\experiments\H1D10C\local-results\H1D10C2-unresolved.zip

Subir ese ZIP a ChatGPT.
