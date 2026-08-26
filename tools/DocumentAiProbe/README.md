# DocumentAiProbe — corpus asistido

La herramienta no entrena modelos ni modifica archivos originales.

```powershell
dotnet run --project tools/DocumentAiProbe -- migrate
dotnet run --project tools/DocumentAiProbe -- add --file "C:\ruta\archivo.pdf" --label FACTURA --group factura-proveedor-001 --source-type PDF --diversity PDF_NATIVO
dotnet run --project tools/DocumentAiProbe -- import-reviewed reviewed-decisions.csv --dry-run
dotnet run --project tools/DocumentAiProbe -- import-reviewed reviewed-decisions.csv
dotnet run --project tools/DocumentAiProbe -- split
dotnet run --project tools/DocumentAiProbe -- audit
dotnet run --project tools/DocumentAiProbe -- report
```

`--label` y `--group` son siempre decisiones explícitas. La carpeta de origen no asigna etiquetas y la herramienta no intenta inferir grupos por filename, hash parcial, tamaño o palabras.

`GroupId` representa una **familia de evidencia que no puede repartirse entre entrenamiento y evaluación**. Deben compartirlo, entre otros:

- el mismo documento en PDF, JPG o PNG;
- rasterizaciones y páginas derivadas del mismo PDF;
- el mismo documento con o sin QR;
- reenvíos del mismo documento;
- variantes producidas desde un mismo original;
- imágenes fuertemente relacionadas de un newsletter;
- fotografías del mismo sujeto o serie cuando puedan generar fuga visual.

Un hash diferente no implica automáticamente un grupo diferente. SHA-256 elimina duplicados exactos; `GroupId` evita duplicados semánticos o de origen y continúa siendo una decisión humana explícita y auditable.

Opcionalmente, `--dataset`, `--corpus` y `--out` permiten usar ubicaciones alternativas. `add` copia el archivo; nunca lo mueve. Un SHA-256 ya existente se rechaza.

`import-reviewed` incorpora decisiones humanas en lote desde un CSV con las columnas `CandidateId`, `OriginalPath`, `ExpectedSha256`, `Action`, `Label`, `GroupId` y `Notes`. Las acciones son `ADD`, `SKIP` y `PENDING`. Antes de copiar, valida el lote completo: IDs y evidencias repetidas, existencia y hash de originales, etiquetas, grupos, conflictos y duplicados contra el corpus. Si falla cualquier fila, no ejecuta ningún `ADD`. `--dry-run` muestra decisiones y distribución proyectada sin copiar archivos ni modificar `dataset.csv`. La ejecución real reutiliza el mismo método de copia, validación y escritura que `add`; no existe un segundo camino de persistencia.

`reviewed-decisions.csv` registra las decisiones de H1D3A2e y `reviewed-import-report.md` conserva el resultado auditable de su aplicación.

En `dataset.csv`, `Path` se guarda relativo al directorio del dataset para que `Corpus\...` sea portable. `OriginalPath` conserva la ruta absoluta del archivo original exclusivamente como trazabilidad.

`split` es determinístico por `GroupId`; ningún grupo cruza splits. Los grupos TEST se registran en `frozen-test-groups.txt` y permanecen congelados en ejecuciones posteriores.

El estado sólo pasa a `APTO_PARA_PRIMER_EXPERIMENTO` con al menos 20 grupos independientes en cada clase y sin errores de auditoría. Ese umbral no garantiza aptitud productiva.
