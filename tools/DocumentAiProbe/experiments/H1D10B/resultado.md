# H1D10B1 — TEXT_ONLY

## Estado

**H1D10B1 de preparación/validación: APROBADO.**

**TEXT_ONLY como clasificador final: NO APROBADO.**

No se ejecutó ni modificó H1D9B. No se tocó SQL, Gmail, configuración productiva ni ground truth.
El holdout sellado H1D10A se verificó exclusivamente por hash y **no recibió inferencia**.

SHA-256 holdout sellado:

`EF2C7A80A5616A5F009087020E3FB9988DD76B5BAED315F84D921AA605BCF7E0`

## Dataset real

- Documentos de desarrollo: **901**
- FACTURA: **828**
- OTRO_DOCUMENTO: **73**
- FamilyId: **154**
- Holdout sellado excluido: **43 documentos**
- Leakage SHA con holdout: **0**
- Leakage FamilyId con holdout: **0**

Fuentes de etiqueta en desarrollo:

- QR_ARCA_STRONG: **709**
- EXISTING_GROUND_TRUTH: **25**
- OCR_STRONG: **113**
- PDF_TEXT_STRONG: **54**

### Control metodológico importante

**167 de 901** etiquetas fuertes provienen de evidencia textual (`OCR_STRONG` o `PDF_TEXT_STRONG`).
Por eso no se considera suficiente la métrica global para juzgar TEXT_ONLY: se calculó además un subconjunto de **734 documentos** cuya etiqueta proviene de evidencia independiente del texto:

- `QR_ARCA_STRONG`
- `EXISTING_GROUND_TRUTH`

Este control evita presentar una métrica artificialmente optimista por circularidad etiqueta-feature.

## Cross-validation

- `StratifiedGroupKFold`
- 4 folds
- Group = `FamilyId`
- random_state = `20260907`
- class_weight = `balanced`
- sin duplicación física de muestras

Distribución de folds:

| Fold | Documentos | Familias | FACTURA | OTRO_DOCUMENTO |
|---:|---:|---:|---:|---:|
| 0 | 225 | 35 | 208 | 17 |
| 1 | 229 | 22 | 206 | 23 |
| 2 | 223 | 48 | 206 | 17 |
| 3 | 224 | 49 | 208 | 16 |

## Comparación TEXT_ONLY — threshold diagnóstico 0.5

| Variante | Macro F1 | Balanced Acc. | Recall FACTURA | Recall OTRO | Precision FACTURA | Precision OTRO | ROC-AUC |
|---|---:|---:|---:|---:|---:|---:|---:|
| WORD | 0.7168 | 0.6557 | 0.9964 | 0.3151 | 0.9429 | 0.8846 | 0.8372 |
| CHAR | 0.6750 | 0.6354 | 0.9831 | 0.2877 | 0.9400 | 0.6000 | 0.6296 |
| WORD_CHAR | 0.7061 | 0.6483 | 0.9952 | 0.3014 | 0.9417 | 0.8462 | 0.7958 |

Ganador por Macro F1 / balanced accuracy a threshold diagnóstico 0.5: **WORD**.

Matriz OOF de WORD, filas real / columnas predicho `[FACTURA, OTRO_DOCUMENTO]`:

`[[825, 3], [50, 23]]`

Equivale a:

- FACTURA → FACTURA: **825**
- FACTURA → OTRO_DOCUMENTO: **3**
- OTRO_DOCUMENTO → FACTURA: **50**
- OTRO_DOCUMENTO → OTRO_DOCUMENTO: **23**

## Métrica más confiable: etiquetas de evidencia independiente

Para WORD sobre QR_ARCA_STRONG + EXISTING_GROUND_TRUTH:

- documentos: **734**
- Recall FACTURA: **0.9985**
- Recall OTRO_DOCUMENTO: **0.3710**
- Precision FACTURA: **0.9451**
- Precision OTRO_DOCUMENTO: **0.9583**
- Macro F1: **0.7530**
- Balanced accuracy: **0.6847**
- ROC-AUC: **0.8232**

Matriz independiente:

`[[671, 1], [39, 23]]`

## Diagnóstico

TEXT_ONLY aprendió señal útil, pero **no resuelve el problema**.

El ganador WORD conserva muy bien FACTURA, pero a threshold diagnóstico 0.5 sólo recupera:

- **31,5 %** de OTRO_DOCUMENTO en el conjunto completo.
- **37,1 %** de OTRO_DOCUMENTO en el subconjunto con etiqueta independiente.

Los errores están además concentrados en familias documentales completas. Eso es precisamente lo que la validación por `FamilyId` debía revelar: el texto memoriza bastante bien vocabulario/template conocido, pero generaliza mal hacia familias nuevas de notas de crédito, recibos y otros documentos similares.

Por lo tanto **no corresponde ajustar un threshold para declarar éxito**. Las probabilidades se conservarán como señal para una fusión posterior, pero H1D10 debe continuar con las ramas visuales `FULL_PAGE` y `HEADER`.

## NO_DOCUMENTO

El corpus canónico histórico contiene:

- **30 NO_DOCUMENTO**
- **20 grupos**

No se forzó un Nivel A con esta cobertura en H1D10B1.
Queda pendiente ampliar/validar ejemplos reales antes de entrenar `DOCUMENTO / NO_DOCUMENTO` con pretensión productiva.

## Modelo generado

Se guarda un modelo **sólo de desarrollo**:

`text-only-word-development.joblib`

No debe copiarse a producción ni sustituir H1D9B.

SHA-256:

`9D9BBE858F9E0EE24EFCCB2FF1C4B104B5444150FBF273961898BBB631A2AA39`

## Próximo paso recomendado

**H1D10B2 — Visual**

1. FULL_PAGE_ONLY.
2. HEADER_ONLY.
3. validación OOF por FamilyId usando exactamente los mismos folds.
4. comparar contra estas predicciones OOF de TEXT_ONLY.
5. después, recién H1D10B3 realiza late fusion usando scores OOF.

No abrir el holdout sellado.
