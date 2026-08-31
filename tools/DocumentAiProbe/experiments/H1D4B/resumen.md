# H1D4B — resumen

- CASO A (dominante): El modelo no aprende ni TRAIN a 0,50.
- CASO C (parcial): existe separación útil en parte de los scores y calibrar mejora el resultado, pero no es el problema principal porque TRAIN también falla.
- Threshold visual seleccionado con VALIDATION: `0.726000`
- Threshold textual seleccionado con VALIDATION: `0.452000`
- TEST end-to-end a 0,50: accuracy=0.466667, macro F1=0.409524
- TEST end-to-end calibrado: accuracy=0.600000, macro F1=0.564935
- Selección de thresholds sin observar TEST: Sí
- Reentrenamiento/modelos nuevos: No
- Conclusión: A dominante + C parcial; no hay evidencia para B porque el textual ya falla en TRAIN.
