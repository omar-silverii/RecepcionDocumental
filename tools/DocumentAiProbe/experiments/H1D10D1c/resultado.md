# H1D10D1c — gate posicional fuerte para FACTURA

Diagnóstico aislado. No entrena, no modifica producción ni ground truth y no lee assets de `SEALED_TEST`.

## Resultado independiente

- Filas totales: **734**
- Decisiones: **727**
- Correctas: **727**
- Incorrectas: **0**
- Precisión cuando decide: **1.0**
- NO_DECISION: **7**

## Gate FACTURA_TITLE_ONLY

- FACTURA_TITLE_ONLY antes: **11**
- Verdaderos antes: **8**
- Verdaderos retenidos: **8**
- Falsos antes: **3**
- Falsos retenidos: **0**
- Convertidos a NO_DECISION: **3**

El gate exige estructura real de título: identificador en la misma línea, descriptor fiscal explícito o una línea de título seguida inmediatamente por numeración fiscal. Las menciones secundarias se resuelven como `NO_DECISION`, no como `OTRO_DOCUMENTO`.

## Invariantes

- Decisiones QR verificadas sin cambios: **True** (719 filas)
- Precedencia NC/ND/RECIBO verificada sin cambios: **True**
- Entrenamiento ejecutado: **False**
- Producción modificada: **False**
- Ground truth modificado: **False**
- Assets SEALED_TEST leídos: **False**
