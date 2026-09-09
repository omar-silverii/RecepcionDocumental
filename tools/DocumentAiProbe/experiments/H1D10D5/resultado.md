# H1D10D5 — Residual Review Reduction / IA documental por familia

## Estado
**CANDIDATO IMPLEMENTADO — pendiente de build y validación end-to-end en Windows/.NET Framework 4.8.**

H1D10D5 introduce una IA documental propia y local para intentar resolver únicamente documentos que el pipeline productivo deja en `REVISAR` después de Mdoc/OCR/QR.

No reemplaza el resolver determinístico. No modifica una decisión ya concluida como `FACTURA` o `DESCARTAR`.

## Modelo
- Versión: `H1D10D5-FAMILY-001`
- Tipo: TF-IDF word 1–2 grams + SGDClassifier log-loss multiclase
- Clases aprendidas V1:
  - `RECIBO_HABERES`
  - `NOTIFICACION_BANCARIA`
  - `OTRO_BASE`
- Umbral productivo: `0.80`
- Runtime: C# propio, offline, sin DLL/servicio de IA de terceros
- SHA-256 del modelo: `F8EB7568FAC1613F13FB91ACB4A06A86D827A9A142C29544515F9195642C2A8B`

## Entrenamiento / evaluación
- Banco negativo conocido: 901 documentos / 154 familias H1D10B.
- Positivos históricos independientes: 7 documentos / 6 grupos.
- C04 y C05 NO fueron usados para entrenar.
- Los positivos crudos no se guardan en el repositorio; se conserva hash/provenance y el modelo aprendido.

### Gate negativo group-aware
- Máxima probabilidad de familia en negativos: `0.4868837736`
- Activaciones `>= 0.80`: **0**

### Auditoría residual D2 (no ground truth)
- Casos auditados: 92
- Máxima probabilidad de familia: `0.4924860045`
- Activaciones `>= 0.80`: **0**

### Casos ciegos del live test
- C04 `082026.pdf` → `RECIBO_HABERES` = `0.9068933312`
- C05 `NOTIFICACION AGOSTO.pdf` → `NOTIFICACION_BANCARIA` = `0.8663741685`
- Ambos superan umbral 0.80.

## Desidentificación / anti-overfit
Se reentrenó después de detectar que una variante previa aprendía identidad/template/issuer en exceso.

La versión aprobada usa `D5_SANITIZE_003` y elimina antes de vectorizar:
- URLs/emails;
- números/fechas/importes/identificadores;
- un conjunto versionado de tokens de identidad/proveedor/producto presentes en los positivos de desarrollo.

Auditoría de vocabulario: **0 tokens excluidos presentes**.

## Safety gate productivo
Una predicción aprendida NO basta para descartar.

Sólo se permite `REVISAR → DESCARTAR` cuando:
1. la IA supera 0.80;
2. no existe QR ARCA válido;
3. OCR no decidió FACTURA;
4. OCR no conserva evidencia fiscal no concluyente con confianza >=45;
5. no aparecen anclas fiscales fuertes (`CAE`, `CAEA`, `PUNTO DE VENTA`, `PTO VTA`);
6. existe evidencia semántica mínima independiente compatible con la familia.

C04 pasa el gate con 6 grupos semánticos de haberes.
C05 pasa el gate con 5 grupos semánticos de notificación bancaria.

Si falla el modelo, el hash, el contrato, la versión, el cálculo o el safety gate, se conserva `REVISAR`.

## Integración productiva
El orden queda:

`Mdoc / OCR / QR / resolver actual → si sigue REVISAR → IA_DOCUMENTAL → safety gate → DESCARTAR o REVISAR`

Cuando la IA decide correctamente:
- `DetectionMethod = IA_DOCUMENTAL+OCR`
- se loguea modelo, familia, confianza, features y safety gate.

Los 8/10 casos que ya eran automáticos en el live test no ingresan a la nueva decisión por construcción, porque la IA sólo se consulta después de una clasificación final `REVISAR`.

## Limitación deliberada V1
La familia `RECIBO_HABERES` todavía no alcanza confianza 0.80 cuando se retira por completo una familia/template de entrenamiento. Esto significa que V1 **no debe describirse como un reconocedor universal de recibos de sueldo**.

La política correcta es abstenerse ante templates nuevos hasta ampliar el corpus por familias independientes.

## Validaciones realizadas en este entorno
- Paridad modelo exportado ↔ runtime: PASS.
- C04/C05 safety gate: PASS.
- Modelo SHA/contrato: PASS.
- XML del csproj: PASS.
- Estructura léxica de C# modificado: PASS.
- Scripts Python: `py_compile` PASS.

No hay MSBuild/.NET Framework disponible en este entorno Linux; por eso el gate de build real y la prueba Gmail end-to-end deben ejecutarse en Windows/Visual Studio 2022.
