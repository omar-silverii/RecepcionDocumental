"""Write the human-readable delivery and enumerate every local preparation artifact."""
import csv,hashlib,json,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parent
REPO=ROOT.parents[3]
def load(name):return json.loads((ROOT/name).read_text(encoding='utf-8-sig'))
def sha(p):
    h=hashlib.sha256()
    with p.open('rb') as s:
        for b in iter(lambda:s.read(1024*1024),b''):h.update(b)
    return h.hexdigest().upper()
def table(values):return '\n'.join('| '+str(k)+' | '+str(v)+' |' for k,v in values.items())
s=load('preparation-manifest.json');n=load('normalization-evidence.json');g=load('gate-report.json')
assert sha(Path(n['SourcePath']))==n['SourceSha256'], 'NORMALIZATION_SOURCE_CHANGED'
assert sha(ROOT/'bin'/'VisualNormalization.dll')==n['CompiledAssemblySha256'], 'NORMALIZATION_BINARY_CHANGED'
assert sha(ROOT/'normalization-regression.json')==n['RegressionReportSha256'], 'NORMALIZATION_EVIDENCE_CHANGED'
with (ROOT/'bank-manifest.csv').open(encoding='utf-8-sig') as f:rows=list(csv.DictReader(f))
for k in ['FACTURA','OTRO_DOCUMENTO','NO_DOCUMENTO','REVISAR']:s['Labels'].setdefault(k,0)
for k in ['FACTURA','OTRO_DOCUMENTO','NO_DOCUMENTO']:s['AutomaticLabels'].setdefault(k,0)
for k in ['FACTURA','NOTA_CREDITO','NOTA_DEBITO','RECIBO','TRANSFERENCIA_ANTICIPO','COMPROBANTE_BANCARIO','IMPUESTO_BOLETA','OTRO_DOCUMENTO','NO_DOCUMENTO','REVISAR']:s['DocumentTypes'].setdefault(k,0)
s['ObservedBatchHashes']=sum(r['ObservedBatch001002']=='True' for r in rows)
s['ObservedBatchFamilies']=len({r['FamilyId'] for r in rows if r['ObservedBatch001002']=='True'})
s['VisualAssets']=sum(bool(r[k]) for r in rows for k in ['FullPageAsset','HeaderAsset'])
source_names=['.gitignore','Prepare-H1D10A.ps1','PrepareBank.cs','FinalizeBank.py','TestEvidenceRules.py','TestPreparationGates.py','PublishReport.py','README.md']
s['PreparationSourceHashes']=[{'Path':str(ROOT/name),'Sha256':sha(ROOT/name)} for name in source_names]
(ROOT/'preparation-manifest.json').write_text(json.dumps(s,ensure_ascii=False,indent=2),encoding='utf-8')
report=f'''# {s['Status']}

## Banco y etiquetas

Se encontraron **{s['PhysicalDocuments']} documentos físicos**, representados por **{s['UniqueSha256']} SHA únicos**. Se conservaron todas las procedencias. No se utilizó H1D9B para labels ni se ejecutó inferencia, entrenamiento, SQL o Gmail.

- SHA con ground truth existente: **{s['ExistingGroundTruth']}**.
- Etiquetados automáticamente por evidencia fuerte: **{s['AutomaticallyLabeledStrong']}**.
- Conflictos entre fuentes fuertes: **{s['Conflicts']}**.
- Documentos que requieren revisión humana: **{s['RequiresHumanReview']}**.
- Registros con incidencias de calidad: **{s['QualityIssues']}**.

| LabelFinal (incluye ground truth y revisión) | Cantidad |
|---|---:|
{table({k:s['Labels'].get(k,0) for k in ['FACTURA','OTRO_DOCUMENTO','NO_DOCUMENTO','REVISAR']})}

| Labels automáticos fuertes exclusivamente | Cantidad |
|---|---:|
{table({k:s['AutomaticLabels'].get(k,0) for k in ['FACTURA','OTRO_DOCUMENTO','NO_DOCUMENTO']})}

| DocumentType | Cantidad |
|---|---:|
{table(s['DocumentTypes'])}

| LabelSource | Cantidad |
|---|---:|
{table(s['LabelSources'])}

Los labels automáticos son reglas de evidencia, no probabilidades calibradas ni ground truth humano certificado. Los títulos ambiguos y las fuentes en conflicto se conservan como REVISAR. Los labels humanos originales permanecen en ExistingGroundTruth y el corpus no se modifica. No se infiere NO_DOCUMENTO por ausencia de texto.

La revisión comprende 681 casos de evidencia insuficiente y un OCR de página completa detenido tras 341 segundos; en ese caso se preservaron imagen, cabecera y texto de cabecera. La etapa no habilita entrenar todavía: faltan revisión humana y cobertura de NO_DOCUMENTO para una futura salida de tres clases.

## Familias y holdout

- FamilyId distintos: **{s['Families']}**.
- Holdout sellado: **{s['SealedHoldoutSamples']} SHA**, en **{s['SealedHoldoutFamilies']} familias**.
- Composición por label: `{json.dumps(s['SealedHoldoutLabels'],ensure_ascii=False)}`.
- Composición por DocumentType: `{json.dumps(s['SealedHoldoutDocumentTypes'],ensure_ascii=False)}`.
- SHA-256 de sealed-test-manifest.json: `{s['SealedManifestSha256']}`.

FamilyId deriva de componentes conexas por CUIT inequívoco de cabecera, GroupId ya auditado o similitud conservadora de plantilla; README.md detalla la fórmula y family-links.csv cada enlace. Los singleton sin evidencia de familia quedan fuera del holdout. Estas heurísticas no prueban que se hayan identificado todos los emisores: se requiere auditoría humana de familias antes de certificación.

| Comprobación de leakage | Resultado |
|---|---:|
{table({k:g[k] for k in ['ShaCrossSplitCount','FamilyCrossSplitCount','LinkedTemplateOrEmitterCrossSplitCount','ObservedHashesInHoldout','ObservedFamiliesInHoldout']})}

Cada familia ocupa un solo split. Se excluyeron del holdout familias observadas en Batch001/Batch002 y familias ya presentes en corpus, con revisión pendiente, problemas de calidad o identidad no resuelta. No se generaron scores. No usar este holdout para seleccionar arquitectura. Sus etiquetas y familias necesitan auditoría independiente antes de certificar un modelo futuro; no cambiar este sello para ajustar resultados.

## Normalización TIFF y regresión

El TIFF observado es `{n['OriginalPixelFormat']}`, dimensiones **{n['OriginalDimensions']}**, con **{n['PixelCount']} píxeles**. La conversión RGB coincidió píxel a píxel con el decodificador de referencia tras canonicalización/EXIF: **{n['TiffPixelParity']}**. El fix resuelve formatos indexados mediante GetPixel sin remuestreo por DPI; no cambia los caminos RGB de 24/32/8 bits ya validados ni resize/letterbox/normalización.

- RGB histórico: **{n['SourceRgbEqual']}/80 idénticos**.
- RGB después del letterbox: **{n['LetterboxRgbEqual']}/80 idénticos**.
- Tensor normalizado: **{n['TensorEqual']}/80 idénticos**.
- Sesiones ONNX en regresión: **{n['ModelSessions']}**; en preparación: **0** en cada uno de los cuatro workers.

La fuente corregida se compiló y comprobó en un ensamblado aislado. No se reemplazó el binario productivo ni se promovió ningún modelo. normalization-evidence.json identifica el hash de la fuente, ensamblado y reporte; normalization-regression.json incluye el resultado de cada caso.

## Integridad y artefactos

Se verificaron por tamaño/SHA los {g['OriginalFilesVerified']} originales y todos los extractos; el corpus y los archivos del modelo conservaron sus hashes. Se mantuvieron {g['PhysicalProvenancesPreserved']} procedencias en el manifest canónico. gate-report.json contiene los gates.

- Carpeta de manifests, código, texto y metadata: `{ROOT}`.
- Imágenes grandes autorizadas por Omar: `E:\\RecepcionDocumental-H1D10A-assets`.
- Los ocho PNG de la prueba inicial se conservaron en `{ROOT / 'assets'}`; no se repitieron.
- **artifact-index.csv** enumera cada artefacto, ruta absoluta, tamaño y SHA-256. Su propia fila no lleva hash recursivo.

Archivos funcionales de esta tarea:

- `{REPO / 'Services' / 'VisualInvoiceShadowService.cs'}`: corrección mínima, 14 líneas agregadas y una reemplazada.
'''
report+=''.join(f'- `{ROOT/name}`\n' for name in source_names)
report+='\nEl cambio que ya existía en tools/DocumentAiProbe/Extract-DmfBank.ps1 pertenece a la tarea anterior de extracción/aplanado; no se modificó durante H1D10A. Los demás archivos de H1D10A son artefactos generados enumerados en artifact-index.csv.\n'
report+='\nNo se entrenó H1D10B. No se promovió un modelo ni se cambiaron thresholds.\n'
(ROOT/'resultado.md').write_text(report,encoding='utf-8')
paths={p.resolve() for p in ROOT.rglob('*') if p.is_file() and p.name!='artifact-index.csv'}
paths.update(Path(r[k]).resolve() for r in rows for k in ['FullPageAsset','HeaderAsset'] if r[k])
with (ROOT/'artifact-index.csv').open('w',encoding='utf-8-sig',newline='') as stream:
    w=csv.DictWriter(stream,fieldnames=['Path','Category','SizeBytes','Sha256']);w.writeheader()
    for p in sorted(paths,key=str):
        category='EXTERNAL_ASSET' if not p.is_relative_to(ROOT) else ('CODE_OR_DOCUMENTATION' if p.name in source_names else 'GENERATED_ARTIFACT')
        w.writerow(dict(Path=str(p),Category=category,SizeBytes=p.stat().st_size,Sha256=sha(p)))
    w.writerow(dict(Path=str(ROOT/'artifact-index.csv'),Category='SELF_INDEX_NO_RECURSIVE_HASH',SizeBytes='',Sha256=''))
print(json.dumps({'Status':s['Status'],'Report':str(ROOT/'resultado.md'),'ArtifactIndex':str(ROOT/'artifact-index.csv'),'IndexedArtifacts':len(paths)+1},indent=2))
