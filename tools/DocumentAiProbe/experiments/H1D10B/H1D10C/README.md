# H1D10C — Auditoría de percepción documental

## Objetivo

Determinar por qué los casos `REVISAR` del Top150 no obtuvieron `TitleEvidence`, separando lectura/percepción de interpretación.

Este experimento es **diagnóstico solamente**:

- no entrena modelos;
- no ejecuta el holdout sellado;
- no toca SQL/Gmail/producción;
- no cambia thresholds;
- no promueve modelos;
- no convierte la evaluación visual de ChatGPT en ground truth.

## Qué usa

Inputs versionados:

- `../H1D10A/bank-manifest.csv`
- `../H1D10B/review-top150.csv`
- `../H1D10B/b3-blind-traceability.csv`
- `../H1D10B/ReviewTop150-evaluacion-visual-ciega-ChatGPT.csv`

Inputs locales ya generados por H1D10A:

- `../H1D10A/text/<SHA>.header.txt`
- `../H1D10A/text/<SHA>.native.txt`
- `../H1D10A/text/<SHA>.ocr.txt` cuando corresponda

Opcionalmente verifica que las imágenes fuente de `E:\RecepcionDocumental-H1D10A-assets` sigan presentes.

## Ejecución

Desde la raíz del repositorio:

```bat
py -3 tools\DocumentAiProbe\experiments\H1D10C\Run-H1D10C.py
```

Si `py -3` no está disponible, usar el Python local que ya se empleó para H1D10B.

No es necesario activar el `.venv` de PyTorch: este script usa sólo la biblioteca estándar de Python.

Para verificar también presencia/hash de imágenes:

```bat
py -3 tools\DocumentAiProbe\experiments\H1D10C\Run-H1D10C.py --verify-images
```

## Salida

Se genera en `local-results/`:

- `resultado.md`: resumen agregable/compartible;
- `summary.json`: métricas agregadas;
- `cases.csv`: detalle por caso y snippets para diagnóstico.

`local-results/` está ignorado por Git porque puede contener fragmentos de texto documental.
