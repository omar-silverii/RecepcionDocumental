# Cierre de sesión H1D3A2f — 2026-08-26

## Estado verificado

- Corpus: 40 archivos y hashes únicos, distribuidos en 27 grupos.
- FACTURA: 21 archivos / 13 grupos.
- OTRO_DOCUMENTO: 12 archivos / 11 grupos.
- NO_DOCUMENTO: 7 archivos / 3 grupos.
- Splits determinísticos por `GroupId`, sin duplicados exactos, fugas ni etiquetas mezcladas.
- Estado global: `INSUFICIENTE`.

## Archivos relevantes de H1D3A2f

- `tools/DocumentAiProbe/reviewed-decisions-h1d3a2f.csv`.
- `tools/DocumentAiProbe/dataset.csv`, `frozen-test-groups.txt` y `corpus-report.md`.
- Corpus experimental bajo `tools/DocumentAiProbe/Corpus`.
- `tools/DocumentAiProbe/H1D3A2f_NoDocumento_Revision.zip` y su material local de revisión.
- Probe Gmail experimental bajo `tools/PdfRasterProbe/GmailCorpusProbe.cs` y sus referencias de proyecto.
- El handler ASP.NET temporal utilizado para ejecutar el probe fue retirado.

## Parte B

Se inspeccionaron 1.615 mensajes dentro de una ventana Gmail de 365 días. Se obtuvieron 27 candidatos únicos de 15 mensajes/orígenes: 10 JPEG y 17 PNG, con un máximo de cinco por MessageId. Se excluyeron 21 hashes existentes, 60 duplicados internos y 10 mensajes FlightAware. Los candidatos no tienen decisiones oficiales de Label ni GroupId.

## Cierre

No se entrenó IA, no se modificó la lógica productiva y no se publicó. Las compilaciones finales Release de `DocumentAiProbe` y `RecepcionDocumental.sln` se ejecutan como parte de este cierre.

Próximo punto exacto: esperar la revisión humana H1D3A2g y, sólo con decisiones aprobadas, preparar una futura importación mediante `import-reviewed`.
