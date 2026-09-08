# H1D10B1 - TEXT_ONLY reproducibility runner
# This file intentionally does NOT touch SQL, Gmail, production config, H1D9B or the sealed holdout.
from pathlib import Path
import os, json, hashlib, platform, warnings
import numpy as np
import pandas as pd
import joblib, sklearn
from sklearn.model_selection import StratifiedGroupKFold
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.pipeline import Pipeline, FeatureUnion
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import (confusion_matrix, precision_recall_fscore_support,
    balanced_accuracy_score, f1_score, roc_auc_score, average_precision_score, accuracy_score)

warnings.filterwarnings("ignore")
HERE = Path(__file__).resolve().parent
H1D10A = HERE.parent / "H1D10A"
BANK = H1D10A / "bank-manifest.csv"
TEXT = H1D10A / "text"
SEALED = H1D10A / "sealed-test-manifest.json"
DATASET = HERE.parent.parent / "dataset.csv"
EXPECTED_SEALED = "EF2C7A80A5616A5F009087020E3FB9988DD76B5BAED315F84D921AA605BCF7E0"
SEED = 20260907

def sha256(path):
    h=hashlib.sha256()
    with open(path,"rb") as f:
        for c in iter(lambda:f.read(1024*1024),b""): h.update(c)
    return h.hexdigest().upper()

if sha256(SEALED) != EXPECTED_SEALED:
    raise SystemExit("H1D10A sealed holdout hash mismatch. Stop.")

bank=pd.read_csv(BANK)
dev=bank[(bank["Split"]!="SEALED_TEST") & bank["LabelFinal"].isin(["FACTURA","OTRO_DOCUMENTO"])].copy().reset_index(drop=True)
sealed=bank[bank["Split"]=="SEALED_TEST"]
if set(dev["Sha256"]) & set(sealed["Sha256"]): raise SystemExit("SHA leakage with sealed holdout.")
if set(dev["FamilyId"]) & set(sealed["FamilyId"]): raise SystemExit("Family leakage with sealed holdout.")

def b(p): return os.path.basename(str(p).replace("\\","/"))
dev["MainTextFile"]=dev["TextAsset"].map(b)
dev["HeaderTextFile"]=dev["HeaderTextAsset"].map(b)
dev["MainText"]=dev["MainTextFile"].map(lambda n:(TEXT/n).read_text(encoding="utf-8-sig",errors="replace"))
dev["HeaderText"]=dev["HeaderTextFile"].map(lambda n:(TEXT/n).read_text(encoding="utf-8-sig",errors="replace"))
dev["CombinedText"]="CABECERA:\n"+dev["HeaderText"]+"\n\nDOCUMENTO:\n"+dev["MainText"]

y=(dev["LabelFinal"]=="FACTURA").astype(int).to_numpy()
groups=dev["FamilyId"].astype(str).to_numpy()
cv=StratifiedGroupKFold(n_splits=4,shuffle=True,random_state=SEED)
folds=list(cv.split(np.zeros(len(dev)),y,groups))

def make_model(kind):
    if kind=="WORD":
        vec=TfidfVectorizer(lowercase=True,strip_accents="unicode",ngram_range=(1,2),min_df=2,max_df=.995,sublinear_tf=True,max_features=80000)
    elif kind=="CHAR":
        vec=TfidfVectorizer(lowercase=True,strip_accents="unicode",analyzer="char_wb",ngram_range=(3,5),min_df=2,sublinear_tf=True,max_features=120000)
    else:
        vec=FeatureUnion([
            ("word",TfidfVectorizer(lowercase=True,strip_accents="unicode",ngram_range=(1,2),min_df=2,max_df=.995,sublinear_tf=True,max_features=70000)),
            ("char",TfidfVectorizer(lowercase=True,strip_accents="unicode",analyzer="char_wb",ngram_range=(3,5),min_df=2,sublinear_tf=True,max_features=100000)),
        ])
    return Pipeline([("tfidf",vec),("clf",LogisticRegression(C=1.0,class_weight="balanced",solver="liblinear",max_iter=2500,random_state=SEED))])

print("H1D10B1 reproducibility runner. Dataset:",len(dev),"families:",dev["FamilyId"].nunique())
print("The canonical artifacts delivered with this package already contain the OOF run.")
print("Run only if an explicit local reproduction is needed.")
