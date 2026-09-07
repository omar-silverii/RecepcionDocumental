"""Synthetic end-to-end gate test; never reads external documents or labels."""
import contextlib,csv,io,json,tempfile,shutil
from pathlib import Path
import FinalizeBank as f
real=f.ROOT
root=Path(tempfile.mkdtemp(prefix='H1D10A-gate-test-'))
(root/'raw').mkdir();docs=root/'documents';docs.mkdir()
def js(name,data):(root/name).write_text(json.dumps(data),encoding='utf-8')
def csvfile(name,rows,fields):
    with (root/name).open('w',newline='',encoding='utf-8-sig') as stream:
        w=csv.DictWriter(stream,fieldnames=fields);w.writeheader();w.writerows(rows)
items=[('A1','FACTURA','0000000000000000'),('A2','FACTURA','0000000000000000'),('B1','RECIBO','FFFFFFFFFFFFFFFF'),('B2','RECIBO','FFFFFFFFFFFFFFFF'),('C','NOTA DE CREDITO','AAAAAAAAAAAAAAAA'),('D1','FACTURA','CCCCCCCCCCCCCCCC'),('D2','FACTURA','CCCCCCCCCCCCCCCC'),('E','FACTURA','123456789ABCDEF0')]
bank=[];originals=[];gt=[];observed=[]
for name,title,dh in items:
    path=docs/(name+'.pdf');path.write_bytes(name.encode());s=f.sha(path)
    header=root/(name+'.header.txt');header.write_text(title,encoding='utf-8')
    image=root/(name+'.png');image.write_bytes(('synthetic-asset-'+name).encode())
    b=dict(Sha256=s,SourceFolder=str(docs),SourceRelativePath=path.name,ExtractedPath=str(path),Extension='.pdf',SourceType='DMF',DmfName=name+'.dmf',DmfOriginalPath=str(path),InternalPath=path.name)
    bank.append(b);originals.append(dict(OriginalPath=str(path),SourceFolder=str(docs),SizeBytes=path.stat().st_size,Sha256=s))
    js('raw/'+s+'.json',dict(Sha256=s,FilePath=str(path),HeaderTextAsset=str(header),HeaderOcrConfidence=.95,NativeTextAsset=str(header),NativeUseful=True,TextAsset=str(header),TextMethod='SYNTHETIC_TEST',EmbeddedQr={},RasterQr={},FullPageAsset=str(image),HeaderAsset=str(image),FullPageSha256=f.sha(image),HeaderSha256=f.sha(image),Width=100,Height=200,HeaderHeight=70,FullDHash=dh,HeaderDHash=dh,Status='OK',Errors=[]))
    if name=='C':gt.append(dict(Sha256=s,Label='FACTURA',GroupId='GT_C',Path=str(path),Evidence='SYNTHETIC'))
    if name=='D1':observed.append(s)
    if name=='A1':
        other=docs/'duplicate.pdf';other.write_bytes(path.read_bytes());bank.append(dict(b,SourceRelativePath=other.name,ExtractedPath=str(other)));originals.append(dict(OriginalPath=str(other),SourceFolder=str(docs),SizeBytes=other.stat().st_size,Sha256=s))
js('bank-input.json',bank);js('existing-ground-truth.json',gt);(root/'observed-batch-hashes.txt').write_text('\n'.join(observed))
csvfile('originals-before.csv',originals,['OriginalPath','SourceFolder','SizeBytes','Sha256']);csvfile('protected-before.csv',[],['Path','Sha256'])
js('normalization-regression.json',dict(Passed=True,SessionsCreated=0,HistoricalCases=80))
for i,n in enumerate([2,2,2,2]):js(f'shard-{i}-completion.json',dict(Completed=n,SessionsCreated=0))
e_sha=f.sha(docs/'E.pdf');e= json.loads((root/'raw'/f'{e_sha}.json').read_text());e.update(Status='REVIEW_QUALITY',Errors=['OCR_TIMEOUT']);js('raw/'+e_sha+'.json',e)
for name in ['PrepareBank.cs','FinalizeBank.py']:shutil.copyfile(real/name,root/name)
f.ROOT=root
with contextlib.redirect_stdout(io.StringIO()):f.finish()
with (root/'bank-manifest.csv').open(encoding='utf-8-sig') as stream:rows=list(csv.DictReader(stream))
assert len(rows)==8 and sum(len(json.loads(r['SourcePaths'])) for r in rows)==9
assert any(r['LabelFinal']=='REVISAR' and r['LabelSource']=='QUALITY_REVIEW' for r in rows)
conflicts=[r for r in rows if r['Conflict']];assert len(conflicts)==1 and conflicts[0]['LabelFinal']=='REVISAR' and json.loads(conflicts[0]['ExistingGroundTruth'])[0]['Label']=='FACTURA'
sealed=[r for r in rows if r['Split']=='SEALED_TEST'];assert len(sealed)==4 and {r['LabelFinal'] for r in sealed}=={'FACTURA','OTRO_DOCUMENTO'}
observed_family=next(r['FamilyId'] for r in rows if r['Sha256']==observed[0]);assert all(r['Split']=='DEVELOPMENT_DIAGNOSTIC' for r in rows if r['FamilyId']==observed_family)
sealed_before=(root/'sealed-test-manifest.json').read_bytes()
with contextlib.redirect_stdout(io.StringIO()):f.finish()
assert (root/'sealed-test-manifest.json').read_bytes()==sealed_before
# Deliberately attempt to alter the freeze: the production finalizer must stop.
(root/'sealed-test-manifest.json').write_bytes(sealed_before+b' ')
blocked=False
try:
    with contextlib.redirect_stdout(io.StringIO()):f.finish()
except AssertionError as e:blocked=str(e)=='SEALED_MANIFEST_CHANGE_BLOCKED'
assert blocked
report=dict(Passed=True,Fixture='SYNTHETIC_ONLY',Tests=['sha_dedup_all_provenances','ground_truth_conflict_to_review','whole_family_split','observed_family_exclusion','both_holdout_classes','idempotent_seal','seal_change_rejected','quality_failure_to_review'],FixtureDirectory=str(root))
(real/'evidence-gate-tests.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2))
