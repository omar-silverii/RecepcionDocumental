"""H1D10A: conservative evidence labels and family-grouped split; never loads scores."""
import csv, hashlib, json, re, unicodedata
from pathlib import Path
from collections import defaultdict, Counter
ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[3]

def read_json(p):
    return json.loads(Path(p).read_text(encoding='utf-8-sig'))
def sha(p):
    h=hashlib.sha256()
    with open(p,'rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''): h.update(b)
    return h.hexdigest().upper()
def digest(s): return hashlib.sha256(s.encode()).hexdigest().upper()
def norm(s):
    s=''.join(c for c in unicodedata.normalize('NFKD',s.upper()) if not unicodedata.combining(c))
    return re.sub(r'[ \t]+',' ',s).strip()
def text(p): return Path(p).read_text(encoding='utf-8-sig') if p and Path(p).exists() else ''
def write_csv(name, rows, fields):
    with (ROOT/name).open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.DictWriter(f,fieldnames=fields,extrasaction='ignore');w.writeheader();w.writerows(rows)
def jsonstr(o): return json.dumps(o,ensure_ascii=False,separators=(',',':'))
# Entire line must look like a title, optionally followed by series/number fields.
# A sentence mentioning an associated invoice is deliberately NOT a title.
TITLES=[
 ('NOTA_CREDITO',r'NOTA (?:DE )?CREDITO(?: ELECTRONICA)?'),
 ('NOTA_DEBITO',r'NOTA (?:DE )?DEBITO(?: ELECTRONICA)?'),
 ('FACTURA',r'FACTURA(?: DE CREDITO ELECTRONICA(?: MIPYMES)?)?(?: ELECTRONICA)?'),
 ('RECIBO',r'RECIBO(?: DE (?:PAGO|COBRO|SUELDO))?'),
 ('TRANSFERENCIA_ANTICIPO',r'SOLICITUD DE (?:TRANSFERENCIA|ANTICIPO)'),
 ('COMPROBANTE_BANCARIO',r'(?:COMPROBANTE|BOLETA) DE (?:DEPOSITO|TRANSFERENCIA)'),
 ('IMPUESTO_BOLETA',r'(?:IMPUESTO (?:INMOBILIARIO|AUTOMOTOR|A LOS INGRESOS BRUTOS)|BOLETA DE (?:PAGO|DEUDA))'),
]
SUFFIX=r'(?:\s+(?:[ABCEM]|(?:N[RO°º.]*|NUMERO|COD(?:IGO)?|COD\.)\s*[:.\-]?|\d[\d\s./:\-]*|ORIGINAL|DUPLICADO|TRIPLICADO))*'
def title_evidence(header, confidence):
    if confidence < .80: return []
    found=[]
    for line in header.splitlines():
        line=norm(line).strip(' |:')
        for typ,pat in TITLES:
            if re.fullmatch(r'(?:ORIGINAL\s+|DUPLICADO\s+|TRIPLICADO\s+)?'+pat+SUFFIX,line):
                found.append({'DocumentType':typ,'Title':line,'Location':'FIRST_PAGE_TOP_35_PERCENT','OcrConfidence':confidence})
    return found

def cuit_valid(s):
    if len(s)!=11: return False
    d=11-sum(int(a)*b for a,b in zip(s[:10],[5,4,3,2,7,6,5,4,3,2]))%11
    return int(s[-1]) == (0 if d==11 else 9 if d==10 else d)
def issuer(header):
    # Only before receiver/address-to markers; no guessing between multiple CUITs.
    h=norm(header)
    h=re.split(r'\b(?:CLIENTE|SENORES|DESTINATARIO|RECEPTOR|RAZON SOCIAL DEL RECEPTOR)\b',h)[0]
    ids=set()
    for m in re.finditer(r'C\.?\s*U\.?\s*I\.?\s*T\.?\s*[:\-]?\s*(\d{2}[ -]?\d{8}[ -]?\d)',h):
        s=re.sub(r'\D','',m[1])
        if cuit_valid(s): ids.add(s)
    return next(iter(ids)) if len(ids)==1 else ''

def finish():
    bank=read_json(ROOT/'bank-input.json');gt=read_json(ROOT/'existing-ground-truth.json')
    completions=[read_json(ROOT/f'shard-{i}-completion.json') for i in range(4)]
    assert sum(c['Completed'] for c in completions)==len({r['Sha256'] for r in bank}), 'INCOMPLETE_SHARDS'
    assert all(c['SessionsCreated']==0 for c in completions), 'FORBIDDEN_MODEL_SESSION'
    sources=defaultdict(list);truth=defaultdict(list)
    for r in bank: sources[r['Sha256']].append(r)
    for r in gt: truth[r['Sha256']].append(r)
    observed=set((ROOT/'observed-batch-hashes.txt').read_text(encoding='utf-8-sig').splitlines())
    # Verify immutability before producing labels/splits.
    for r in bank:
        assert sha(r['ExtractedPath'])==r['Sha256'], 'EXTRACT_CHANGED'
    with (ROOT/'protected-before.csv').open(encoding='utf-8-sig') as f:
        for r in csv.DictReader(f): assert sha(r['Path'])==r['Sha256'], 'GROUND_TRUTH_OR_MODEL_CHANGED'
    with (ROOT/'originals-before.csv').open(encoding='utf-8-sig') as f: originals=list(csv.DictReader(f))
    original_dirs=set(r['SourceFolder'] for r in originals)
    assert sum(sum(1 for p in Path(d).rglob('*') if p.is_file()) for d in original_dirs)==len(originals)
    for r in originals:
        assert Path(r['OriginalPath']).stat().st_size==int(r['SizeBytes']) and sha(r['OriginalPath'])==r['Sha256'], 'ORIGINAL_CHANGED'
    regression=read_json(ROOT/'normalization-regression.json');assert regression['Passed'] and regression['SessionsCreated']==0
    rows=[];raws={};titles_by_sha={}
    for s,provenances in sorted(sources.items()):
        p=ROOT/'raw'/f'{s}.json';assert p.exists(),f'MISSING {s}'
        raw=read_json(p);raws[s]=raw
        assert raw['Sha256']==s and raw['FilePath'] in [v['ExtractedPath'] for v in provenances], 'RAW_TRACEABILITY_MISMATCH'
        head=text(raw.get('HeaderTextAsset'));native=text(raw.get('NativeTextAsset'))
        titles=title_evidence(head,raw.get('HeaderOcrConfidence',0));titles_by_sha[s]=titles
        types=set(t['DocumentType'] for t in titles)
        strong=[];ql=[]
        for name in ['EmbeddedQr','RasterQr']:
            q=raw.get(name,{})
            if q.get('IsValid'):
                # The adapter records this interpretation by calling the existing
                # ArcaQrDecoder API. No ARCA code tables are duplicated here.
                label=q.get('CanonicalLabel','')
                if label: strong.append((name,label,'FACTURA' if label=='FACTURA' else 'OTRO_DOCUMENTO'))
            ql.append({'Source':name,**q})
        for typ in sorted(types): strong.append(('TITLE','FACTURA' if typ=='FACTURA' else 'OTRO_DOCUMENTO',typ))
        existing=truth.get(s,[]);gtlabels=set(g['Label'] for g in existing)
        for label in sorted(gtlabels): strong.append(('GROUND_TRUTH',label,label))
        finals=set(v[1] for v in strong)
        conflict=len(finals)>1 or len(types)>1 or len(gtlabels)>1
        notes=[];label='REVISAR';dtype='REVISAR';source='INSUFFICIENT_EVIDENCE';confidence='UNRESOLVED'
        if conflict: source='CONFLICT';notes.append('Strong evidence disagrees; existing ground truth is preserved in metadata.')
        elif gtlabels:
            label=next(iter(gtlabels));dtype=next(iter(types)) if len(types)==1 else label;source='EXISTING_GROUND_TRUTH';confidence='CONFIRMED_EXISTING'
        elif any(v[0] in ('EmbeddedQr','RasterQr') for v in strong):
            label=next(v[1] for v in strong if v[0] in ('EmbeddedQr','RasterQr'));dtype=next(iter(types)) if len(types)==1 else label;source='QR_ARCA_STRONG';confidence='STRONG_RULE';notes.append('ARCA structural evidence only; no online authenticity claim.')
        elif len(types)==1:
            dtype=next(iter(types));label='FACTURA' if dtype=='FACTURA' else 'OTRO_DOCUMENTO'
            corroborated=raw.get('NativeUseful') and any(t['Title'] in norm(native) for t in titles)
            source='PDF_TEXT_STRONG' if corroborated else 'OCR_STRONG';confidence='STRONG_RULE'
        if raw['Status']!='OK' and not gtlabels and not conflict:
            label='REVISAR';dtype='REVISAR';source='QUALITY_REVIEW';confidence='UNRESOLVED'
        if not raw.get('FullPageAsset') or not raw.get('HeaderAsset'): notes.append('Visual asset missing; review required before training.')
        if raw.get('Errors'): notes.extend(raw['Errors'])
        cu=issuer(head) if raw.get('HeaderOcrConfidence',0)>=.80 else ''
        row={'Sha256':s,'FileName':Path(provenances[0]['ExtractedPath']).name,'Source':'EXTERNAL_DMF_BANK','SourcePaths':jsonstr(provenances),
             'Extension':provenances[0]['Extension'],'ExistingGroundTruth':jsonstr(existing),'QrEvidence':jsonstr(ql),
             'TextMethod':raw.get('TextMethod','UNAVAILABLE'),'DocumentType':dtype,'LabelFinal':label,'LabelSource':source,'LabelConfidence':confidence,
             'Conflict':'CONFLICT' if conflict else '', 'FamilyId':'','FamilyEvidence':'','EmitterCuit':cu,
             'FullPageAsset':raw.get('FullPageAsset',''),'HeaderAsset':raw.get('HeaderAsset',''),'TextAsset':raw.get('TextAsset',''),
             'HeaderTextAsset':raw.get('HeaderTextAsset',''),'TitleEvidence':jsonstr(titles),'Width':raw.get('Width',''),'Height':raw.get('Height',''),
             'MetadataAsset':str(p),'PageCount':raw.get('PageCount',''),'HeaderOcrConfidence':raw.get('HeaderOcrConfidence',''),
             'NativeUseful':raw.get('NativeUseful',False),'TextCharacters':len(text(raw.get('TextAsset'))),
             'HeaderHeight':raw.get('HeaderHeight',''),'VisualMethod':'PRODUCT_FIRST_PAGE_RGB_NO_RESIZE;HEADER_TOP_35_PERCENT_CEIL',
             'FullPageSha256':raw.get('FullPageSha256',''),'HeaderSha256':raw.get('HeaderSha256',''),'NativeTextAsset':raw.get('NativeTextAsset',''),
             'Split':'','ObservedBatch001002':s in observed,'QualityStatus':raw['Status'],'Notes':' | '.join(notes)}
        rows.append(row)
    # Conservative union of emitter, existing human GroupId, and near-equal
    # header+full-page visual template hashes. Connected components never split.
    parent={r['Sha256']:r['Sha256'] for r in rows};edges=[]
    def find(x):
        while parent[x]!=x: parent[x]=parent[parent[x]];x=parent[x]
        return x
    def union(a,b,reason):
        x,y=find(a),find(b)
        if x!=y: parent[max(x,y)]=min(x,y);edges.append({'ShaA':a,'ShaB':b,'Reason':reason})
    keys={}
    for r in rows:
        s=r['Sha256'];ks=[]
        if r['EmitterCuit']: ks.append('HEADER_CUIT:'+r['EmitterCuit'])
        ks += ['EXISTING_GROUP:'+g['GroupId'] for g in truth.get(s,[]) if g.get('GroupId')]
        for k in ks:
            if k in keys: union(s,keys[k],k)
            else: keys[k]=s
    for i,r in enumerate(rows):
        a=raws[r['Sha256']]
        if not a.get('HeaderDHash'): continue
        for q in rows[:i]:
            b=raws[q['Sha256']]
            if not b.get('HeaderDHash'): continue
            hd=(int(a['HeaderDHash'],16)^int(b['HeaderDHash'],16)).bit_count()
            fd=(int(a['FullDHash'],16)^int(b['FullDHash'],16)).bit_count()
            aspecta=a['Width']/a['Height'];aspectb=b['Width']/b['Height']
            if hd<=4 and fd<=8 and abs(aspecta-aspectb)<.04: union(r['Sha256'],q['Sha256'],'TEMPLATE_DHASH_HEADER_LE4_FULL_LE8_ASPECT_DELTA_LT004')
    comps=defaultdict(list)
    for r in rows: comps[find(r['Sha256'])].append(r)
    # Unresolved singleton families are excluded from certification; do not
    # silently assume that every different SHA represents a new template.
    families={}
    for rs in comps.values():
        fid='F-'+digest('|'.join(sorted(r['Sha256'] for r in rs)))[:20]
        identified=any(r['EmitterCuit'] for r in rs) or len(rs)>1 or any(truth.get(r['Sha256']) for r in rs)
        for r in rs:
            r['FamilyId']=fid;r['FamilyEvidence']='CUIT_OR_EXISTING_GROUP_OR_TEMPLATE_COMPONENT' if identified else 'UNRESOLVED_SINGLETON_EXCLUDED_FROM_HOLDOUT'
        families[fid]=rs
    eligible={f:rs for f,rs in families.items() if all(not r['ObservedBatch001002'] and not truth.get(r['Sha256']) and r['LabelFinal']!='REVISAR' and r['QualityStatus']=='OK' and r['FamilyEvidence']!='UNRESOLVED_SINGLETON_EXCLUDED_FROM_HOLDOUT' for r in rs)}
    selected=set()
    # Frozen deterministic 20% grouped allocation, stratified on strong labels.
    # No model scores or architecture metric is loaded here.
    for label in ['FACTURA','OTRO_DOCUMENTO']:
        options=sorted((f for f,rs in eligible.items() if any(r['LabelFinal']==label for r in rs)),key=lambda f:digest('H1D10A_SEALED_V1|'+f))
        target=max(1,round(sum(sum(r['LabelFinal']==label for r in rs) for rs in eligible.values())*.20))
        n=sum(sum(r['LabelFinal']==label for r in eligible[f]) for f in selected)
        for f in options:
            if n>=target: break
            if f not in selected: selected.add(f);n+=sum(r['LabelFinal']==label for r in eligible[f])
    for f,rs in families.items():
        # All rows in a family get the same split, including review rows.
        split='SEALED_TEST' if f in selected else ('DEVELOPMENT_DIAGNOSTIC' if any(r['ObservedBatch001002'] or truth.get(r['Sha256']) for r in rs) else 'DEVELOPMENT')
        for r in rs:r['Split']=split
    assert len(rows)==len(set(r['Sha256'] for r in rows))
    assert all(len(set(r['Split'] for r in rs))==1 for rs in families.values())
    assert not any(r['ObservedBatch001002'] for r in rows if r['Split']=='SEALED_TEST')
    assert all(r['SourcePaths'] and len(json.loads(r['SourcePaths']))>0 for r in rows)
    assert sum(len(json.loads(r['SourcePaths'])) for r in rows)==len(bank), 'LOST_PROVENANCE'
    for r in rows:
        if r['FullPageAsset']: assert sha(r['FullPageAsset'])==r['FullPageSha256']
        if r['HeaderAsset']: assert sha(r['HeaderAsset'])==r['HeaderSha256']
    sealed=[r for r in rows if r['Split']=='SEALED_TEST']
    by_sha={r['Sha256']:r for r in rows}
    observed_families={r['FamilyId'] for r in rows if r['ObservedBatch001002']}
    gates={
        'NoModelSessions':all(c['SessionsCreated']==0 for c in completions),
        'ShaCrossSplitCount':sum(len({r['Split'] for r in rows if r['Sha256']==s})>1 for s in sources),
        'FamilyCrossSplitCount':sum(len({r['Split'] for r in rs})>1 for rs in families.values()),
        'LinkedTemplateOrEmitterCrossSplitCount':sum(by_sha[e['ShaA']]['Split']!=by_sha[e['ShaB']]['Split'] for e in edges),
        'ObservedHashesInHoldout':sum(r['ObservedBatch001002'] for r in sealed),
        'ObservedFamiliesInHoldout':len(observed_families & selected),
        'PhysicalProvenancesPreserved':sum(len(json.loads(r['SourcePaths'])) for r in rows),
        'OriginalFilesVerified':len(originals),
        'ExistingGroundTruthUnchanged':True,
        'NormalizationRegressionPassed':regression['Passed'],
        'HoldoutHasFacturaAndHardNegatives':{'FACTURA','OTRO_DOCUMENTO'} <= {r['LabelFinal'] for r in sealed},
        'TrainingExecuted':False,
    }
    approved=(all(gates[k]==0 for k in ['ShaCrossSplitCount','FamilyCrossSplitCount','LinkedTemplateOrEmitterCrossSplitCount','ObservedHashesInHoldout','ObservedFamiliesInHoldout'])
              and gates['NoModelSessions'] and gates['NormalizationRegressionPassed'] and gates['HoldoutHasFacturaAndHardNegatives'])
    gates['Status']='H1D10A APROBADO — PREPARACION' if approved else 'H1D10A NO APROBADO'
    (ROOT/'gate-report.json').write_text(json.dumps(gates,ensure_ascii=False,indent=2),encoding='utf-8')
    seal={'Version':'H1D10A_SEALED_V1','Policy':'Families excluded if observed batches/existing corpus/unresolved or conflicting labels; no scores','Samples':[{'Sha256':r['Sha256'],'FamilyId':r['FamilyId'],'LabelFinal':r['LabelFinal'],'DocumentType':r['DocumentType'],'FullPageSha256':r['FullPageSha256'],'HeaderSha256':r['HeaderSha256']} for r in sealed]}
    frozen=json.dumps(seal,ensure_ascii=False,sort_keys=True,indent=2).encode('utf-8')
    sealed_path=ROOT/'sealed-test-manifest.json'
    if sealed_path.exists(): assert sealed_path.read_bytes()==frozen,'SEALED_MANIFEST_CHANGE_BLOCKED'
    else: sealed_path.write_bytes(frozen)
    (ROOT/'sealed-test-groups.txt').write_text('\n'.join(sorted(selected))+'\n',encoding='utf-8')
    fields=list(rows[0]);write_csv('bank-manifest.csv',rows,fields)
    needs=[r for r in rows if r['LabelFinal']=='REVISAR' or r['QualityStatus']!='OK']
    write_csv('review-required.csv',needs,fields);write_csv('conflicts.csv',[r for r in rows if r['Conflict']],fields)
    write_csv('family-links.csv',edges,['ShaA','ShaB','Reason'])
    counts=Counter((r['LabelFinal'],r['DocumentType'],r['LabelSource'],r['Split']) for r in rows)
    write_csv('label-summary.csv',[dict(zip(['LabelFinal','DocumentType','LabelSource','Split','Count'],[*k,v])) for k,v in sorted(counts.items())],['LabelFinal','DocumentType','LabelSource','Split','Count'])
    stats={'PhysicalDocuments':len(bank),'UniqueSha256':len(rows),'ExistingGroundTruth':sum(bool(truth.get(r['Sha256'])) for r in rows),'AutomaticallyLabeledStrong':sum(r['LabelSource'] in ['QR_ARCA_STRONG','PDF_TEXT_STRONG','OCR_STRONG'] for r in rows),'Labels':dict(Counter(r['LabelFinal'] for r in rows)),'DocumentTypes':dict(Counter(r['DocumentType'] for r in rows)),'Conflicts':sum(bool(r['Conflict']) for r in rows),'RequiresHumanReview':len(needs),'Families':len(families),'SealedHoldoutSamples':len(sealed),'SealedHoldoutFamilies':len(selected),'SealedHoldoutLabels':dict(Counter(r['LabelFinal'] for r in sealed)),'SealedManifestSha256':hashlib.sha256(frozen).hexdigest().upper(),'NormalizationRegressionPassed':regression['Passed'],'HistoricalPreprocessingParity':regression['HistoricalCases'],'OriginalsIntact':True,'ExistingGroundTruthIntact':True,'ModelUsedForLabels':False,'ModelInferenceExecuted':False,'TrainingExecuted':False,'CertificationStatus':'PENDING_INDEPENDENT_HUMAN_LABEL_VALIDATION_AND_FAMILY_AUDIT','FamilyRules':'Same checksum-valid unambiguous header CUIT before receiver markers OR existing GroupId OR dHash header<=4 AND full<=8 AND aspect delta<.04. Connected components. Unresolved singletons excluded from holdout. FamilyId=SHA256(sorted component document SHA joined by |) first20.','TitleRules':'Whole standalone documentary-title line within deterministic top35% image, OCR mean confidence>=.80. Native text only strong when header title corroborates. QR type interpreted exclusively by product ArcaQrDecoder. Conflicting strong types/final labels -> REVISAR. No automatic NO_DOCUMENTO inference.','VisualRules':'Product PDF first page at300DPI; product image canonicalization/EXIF; decoded RGB at native resolution; top35% ceil, whole width.','ProtectedInputs':[{'Path':str(p),'Sha256':sha(p)} for p in [ROOT/'bank-input.json',ROOT/'existing-ground-truth.json',ROOT/'observed-batch-hashes.txt',ROOT/'PrepareBank.cs',ROOT/'FinalizeBank.py',REPO/'Services'/'VisualInvoiceShadowService.cs']]}
    stats.update({'Status':gates['Status'],'Gates':gates,'AutomaticLabels':dict(Counter(r['LabelFinal'] for r in rows if r['LabelSource'] in ['QR_ARCA_STRONG','PDF_TEXT_STRONG','OCR_STRONG'])),
                  'LabelSources':dict(Counter(r['LabelSource'] for r in rows)),
                  'SealedHoldoutDocumentTypes':dict(Counter(r['DocumentType'] for r in sealed)),
                  'QualityIssues':sum(r['QualityStatus']!='OK' for r in rows),
                  'AssetDirectories':sorted({str(Path(r['FullPageAsset']).parent) for r in rows if r['FullPageAsset']}),
                  'WorkerCompletions':completions})
    (ROOT/'preparation-manifest.json').write_text(json.dumps(stats,ensure_ascii=False,indent=2),encoding='utf-8')
    (ROOT/'resultado.md').write_text('# H1D10A — preparación canónica\n\n'+json.dumps(stats,ensure_ascii=False,indent=2)+'\n\nNo se entrenó ni ejecutó H1D9B. Los labels STRONG_RULE son evidencia automática auditable, no ground truth humano certificado. El holdout queda sellado sin scores; su uso para certificación requiere auditar etiquetas y familias de forma independiente, sin ajustar arquitectura con sus resultados. Los documentos REVISAR no son muestras etiquetadas utilizables hasta revisión.\n\nMdoc no devuelve coordenadas: se conserva el texto del documento y se exige título aislado en OCR de la cabecera. Los códigos ARCA se interpretan únicamente mediante el API productivo existente; no se atribuye subtipo fiscal por una tabla nueva. La ausencia de texto no implica NO_DOCUMENTO.\n\nParidad de RGB, letterbox y tensor en 80 assets históricos; TIFF comprobado pixel a pixel tras canonicalización/EXIF. Se ejecutaron cero sesiones ONNX.\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in stats.items() if k not in ['ProtectedInputs','WorkerCompletions','FamilyRules','TitleRules','VisualRules']},ensure_ascii=False,indent=2))
    if not approved: raise RuntimeError('H1D10A NO APROBADO: consultar gate-report.json; no entrenar H1D10B.')
if __name__=='__main__':
    try: finish()
    except Exception as exc:
        (ROOT/'gate-failure.json').write_text(json.dumps({'Status':'H1D10A NO APROBADO','Error':str(exc),'TrainingExecuted':False},ensure_ascii=False,indent=2),encoding='utf-8')
        if not (ROOT/'resultado.md').exists():
            (ROOT/'resultado.md').write_text('# H1D10A NO APROBADO\n\n'+str(exc)+'\n\nNo se entrenó H1D10B.\n',encoding='utf-8')
        raise
