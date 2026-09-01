import argparse, csv, gzip, hashlib
from pathlib import Path
import numpy as np
import onnxruntime as ort
from PIL import Image

MEAN=np.array([.485,.456,.406],dtype=np.float32).reshape(1,1,3); STD=np.array([.229,.224,.225],dtype=np.float32).reshape(1,1,3)
TNO=.1169927716255188; TYES=.7979831695556641
def read(p):
    with open(p,newline="",encoding="utf-8-sig") as f:return list(csv.DictReader(f))
def h(b):return hashlib.sha256(b).hexdigest().upper()
def zone(p):return "NO_FACTURA_FUERTE" if p<=TNO else "FACTURA_FUERTE" if p>=TYES else "INCIERTO_VISUAL"
def main():
    q=argparse.ArgumentParser();q.add_argument("--onnx",required=True);q.add_argument("--development-assets",required=True);q.add_argument("--holdout-assets",required=True);q.add_argument("--output",required=True);a=q.parse_args()
    out=Path(a.output).resolve();out.mkdir(parents=True,exist_ok=True);tmp=out/"python-resized.tmp";tmp.mkdir(exist_ok=False)
    assets=[("DEVELOPMENT",r) for r in read(a.development_assets)]+[("HOLDOUT",r) for r in read(a.holdout_assets)]; rows=[]; session=ort.InferenceSession(str(Path(a.onnx).resolve()),providers=["CPUExecutionProvider"])
    with open(out/"target-rgb.tmp.bin","wb") as targets,open(out/"tensor.tmp.bin","wb") as tensors:
      for cohort,r in assets:
        with Image.open(r["VisualAssetPath"]) as opened:
          src=opened.convert("RGB"); source=src.tobytes(); scale=min(224.0/src.width,224.0/src.height); size=(max(1,round(src.width*scale)),max(1,round(src.height*scale))); resized=src.resize(size,Image.Resampling.BICUBIC); rb=resized.tobytes(); left=(224-size[0])//2;top=(224-size[1])//2;canvas=Image.new("RGB",(224,224),(255,255,255));canvas.paste(resized,(left,top));tb=canvas.tobytes()
        tensor=np.transpose((np.asarray(canvas,dtype=np.float32)/255.0-MEAN)/STD,(2,0,1))[None].astype("<f4");p=session.run(["probabilities"],{"image":tensor})[0][0]
        with gzip.open(tmp/(r["Sha256"].upper()+".rgb.gz"),"wb") as z:z.write(rb)
        targets.write(tb);tensors.write(tensor.tobytes());py=float(p[1]);rows.append({"Sha256":r["Sha256"].upper(),"Cohort":cohort,"GroupId":r["GroupId"],"SourceWidth":src.width,"SourceHeight":src.height,"SourceRgbSha256":h(source),"ResizedWidth":size[0],"ResizedHeight":size[1],"Left":left,"Top":top,"ResizedRgbSha256":h(rb),"TargetRgbSha256":h(tb),"TensorSha256":h(tensor.tobytes()),"PFactura":f"{py:.9f}","Pred050":"FACTURA" if py>=.5 else "NO_FACTURA","Zona":zone(py)})
    with open(out/"python-resize-reference.csv","w",newline="",encoding="utf-8") as f:w=csv.DictWriter(f,fieldnames=list(rows[0]),lineterminator="\n");w.writeheader();w.writerows(rows)
    print("H1D9D1C_PYTHON | Rows=80 | Pillow="+Image.__version__)
if __name__=="__main__":main()
