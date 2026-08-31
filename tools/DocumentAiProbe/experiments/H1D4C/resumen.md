# H1D4C — resumen ejecutivo

## Cierre canónico

- La evaluación cruzada es negativa: ninguna variante aprende razonablemente.
- El primer cierre completo seleccionó `B_WORD_NGRAMS`: 3 de 18 FACTURA detectadas fuera de fold, recall FACTURA `0,166667` y macro F1 `0,420202`.
- En TEST no superó H1D4B calibrado.
- Se activa la condición de parada: no explorar más variantes ni tocar datos.
- Una regeneración posterior necesaria para corregir la fidelidad del pipeline produjo variación estocástica en SDCA y seleccionó otra variante igualmente insuficiente. Los CSV conservan esa ejecución final disponible; la conclusión técnica no cambia.

- Variantes: baseline; word n-grams; word+character n-grams; híbrida word n-grams+señales fiscales aprendidas.
- Ganador CV: `C_WORD_CHAR_NGRAMS`; recall FACTURA=0.222222, macro F1=0.264354.
- TEST textual: recall FACTURA=0.166667, macro F1=0.415584, FACTURA→OTRO_DOCUMENTO=5.
- End-to-end: macro F1=0.564935, recall FACTURA=0.166667, FACTURA→OTRO_DOCUMENTO=5, FACTURA→NO_DOCUMENTO=0.
- Modelo experimental: `C:\Users\omard\source\repos\RecepcionDocumental\RecepcionDocumental\tools\DocumentAiProbe\experiments\H1D4C\textual-model.zip`; no reemplaza H1D4A ni producto.
- TEST previamente observado: regresión experimental, no certificación independiente.
- Comparación: H1D4A tuvo recall FACTURA 0 y macro F1 0,250000; H1D4B calibrado e H1D4C obtuvieron ambos recall 0,166667 y macro F1 0,415584.
- **Condición de parada:** ninguna variante aprendió razonablemente en CV group-aware; H1D4C no mejora H1D4B.
- Conclusión técnica: las representaciones dispersas y señales fiscales probadas no generalizan entre familias; no reemplazar el modelo existente ni integrar en producto.
