"""H1D10B2: fixed B1 folds, local ImageNet weights, CPU, header assets only.

Frozen ImageNet features.0..7 are cached in eval mode without labels or fitting.
Each fold independently trains classifier (8 epochs), then features.8 + classifier
(12 epochs). Outer validation is predicted once, after the fixed training schedule.
No outer-fold early stopping, parameter selection, corpus writes or network access.
"""
import argparse
import csv
import hashlib
import importlib.metadata
import json
import os
import random
import socket
import sys
import time
from collections import Counter
from pathlib import Path

os.environ['CUDA_VISIBLE_DEVICES'] = ''
os.environ['PYTHONDONTWRITEBYTECODE'] = '1'


def deny_network(*args, **kwargs):
    raise RuntimeError('Network disabled for H1D10B2')


socket.create_connection = deny_network
socket.socket.connect = deny_network

import numpy as np
import PIL
from PIL import Image
import torch
from torch import nn
import torchvision
from torchvision import models, transforms

HERE = Path(__file__).resolve().parent
A = HERE.parent / 'H1D10A'
LOCAL = HERE / 'header-local'
WEIGHTS = Path.home() / '.cache/torch/hub/checkpoints/efficientnet_b0_rwightman-7f5810bc.pth'
WEIGHT_SHA = '7F5810BC96DEF8F7552D5B7E68D53C4786F81167D28291B21C0D90E1FCA14934'
SEAL = 'EF2C7A80A5616A5F009087020E3FB9988DD76B5BAED315F84D921AA605BCF7E0'
SEED = 20260907
CONFIG = {
    'Architecture': 'torchvision EfficientNet-B0', 'Initialization': 'IMAGENET1K_V1 local only',
    'Device': 'cpu', 'Threads': 8, 'Seed': SEED, 'InputSize': [384, 384],
    'Input': 'Exact H1D10A deterministic header asset, no full page',
    'Resize': 'RGB, PIL BICUBIC, preserve aspect ratio, round dimensions, centered white letterbox',
    'ImageNetMean': [0.485, 0.456, 0.406], 'ImageNetStd': [0.229, 0.224, 0.225],
    'Augmentations': [], 'Crop': False, 'BatchSize': 16, 'CacheBatchSize': 4,
    'Phase1': {'Epochs': 8, 'LearningRate': 0.001, 'Trainable': 'classifier'},
    'Phase2': {'Epochs': 12, 'LearningRate': 0.0001, 'Trainable': 'features.8 + classifier'},
    'Optimizer': 'AdamW', 'WeightDecay': 0.0001, 'ClassifierDropout': 0.2,
    'Loss': 'mean(weight[label] * cross_entropy); weight = N_train/(2*N_train_class)',
    'ClassMapping': {'OTRO_DOCUMENTO': 0, 'FACTURA': 1},
    'FoldSource': 'Existing B1 fold-manifest.csv, never generated',
    'OuterValidationUse': 'Once after fixed 8+12 epochs; no early stopping or tuning',
    'DiagnosticThreshold': 0.5, 'OperationalThresholdsChanged': False,
    'FrozenCache': 'features.0..7 eval mode, no gradients, no running-stat updates, ImageNet only',
    'ProductionPromotion': False,
}


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda: f.read(1048576), b''):
            h.update(block)
    return h.hexdigest().upper()


def read(path):
    with Path(path).open(encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))


def save(path, obj):
    Path(path).write_text(json.dumps(obj, indent=2, ensure_ascii=False), encoding='utf-8')


def seed(value):
    random.seed(value)
    np.random.seed(value)
    torch.manual_seed(value)
    torch.use_deterministic_algorithms(True)
    torch.set_num_threads(CONFIG['Threads'])


def inputs():
    pre = json.loads((HERE / 'header-preflight.json').read_text())
    assert sha(A / 'sealed-test-manifest.json') == SEAL  # bytes only, not JSON
    assert sha(A / 'bank-manifest.csv') == pre['BankManifestSha256']
    for name, digest in pre['B1Hashes'].items():
        assert sha(HERE / name) == digest, 'B1 modified: ' + name
    bank = {r['Sha256']: r for r in read(A / 'bank-manifest.csv')}
    folds = read(HERE / 'fold-manifest.csv')
    assert len(folds) == len({r['Sha256'] for r in folds}) == 901
    assert Counter(r['LabelFinal'] for r in folds) == {'FACTURA': 828, 'OTRO_DOCUMENTO': 73}
    assert len({r['FamilyId'] for r in folds}) == 154
    forbidden = {r['FamilyId'] for r in bank.values() if r['Split'] == 'SEALED_TEST'}
    assert not forbidden & {r['FamilyId'] for r in folds}
    for r in folds:
        b = bank[r['Sha256']]
        assert b['Split'] != 'SEALED_TEST' and not b['Conflict']
        for key in ('LabelFinal', 'FamilyId', 'LabelSource'):
            assert r[key] == b[key]
        path = Path(b['HeaderAsset'])
        assert path.name.endswith('.header.png') and path.is_file()
        assert not {'temp', 'work'} & {part.lower() for part in path.parts}
        assert sha(path) == b['HeaderSha256']
        r.update(HeaderAsset=str(path), HeaderSha256=b['HeaderSha256'])
    assert {int(r['Fold']) for r in folds} == {0, 1, 2, 3}
    for k in range(4):
        tr = [r for r in folds if int(r['Fold']) != k]
        va = [r for r in folds if int(r['Fold']) == k]
        for key in ('Sha256', 'FamilyId'):
            assert not {r[key] for r in tr} & {r[key] for r in va}, 'LEAKAGE ' + key
    return folds


def environment():
    assert sys.version_info[:3] == (3, 11, 3)
    assert Path(sys.prefix).resolve() == (LOCAL / '.venv').resolve()
    assert sys.prefix != sys.base_prefix
    expected = {'torch': '2.13.0+cpu', 'torchvision': '0.28.0+cpu', 'numpy': '2.4.6', 'pillow': '12.3.0'}
    imported = {'torch': torch.__version__, 'torchvision': torchvision.__version__,
                'numpy': np.__version__, 'pillow': PIL.__version__}
    assert imported == expected, imported
    assert not torch.cuda.is_available() and torch.version.cuda is None
    assert sha(WEIGHTS) == WEIGHT_SHA
    packages = json.loads((HERE / 'header-offline-packages.json').read_text())['Packages']
    versions = {p['Name']: importlib.metadata.version(p['Name']) for p in packages}
    assert all(versions[p['Name']] == p['Version'] for p in packages)
    return {'Python': sys.version, 'Executable': sys.executable, 'Venv': sys.prefix,
            'ImportedVersions': imported, 'InstalledVersions': versions,
            'CudaAvailable': False, 'Device': 'cpu', 'LocalWeights': str(WEIGHTS),
            'LocalWeightsSha256': WEIGHT_SHA, 'NetworkDisabled': True}


def tensor(path):
    # The only image reader in this experiment. Caller paths come from validated allowlist.
    with Image.open(path) as source:
        im = source.convert('RGB')
        ratio = min(384 / im.width, 384 / im.height)
        size = (max(1, round(im.width * ratio)), max(1, round(im.height * ratio)))
        resized = im.resize(size, Image.Resampling.BICUBIC)
        canvas = Image.new('RGB', (384, 384), (255, 255, 255))
        canvas.paste(resized, ((384 - size[0]) // 2, (384 - size[1]) // 2))
    return transforms.functional.normalize(transforms.functional.to_tensor(canvas),
                                           CONFIG['ImageNetMean'], CONFIG['ImageNetStd'])


def model(imagenet_head=False):
    net = models.efficientnet_b0(weights=None)
    net.load_state_dict(torch.load(WEIGHTS, map_location='cpu', weights_only=True), strict=True)
    if not imagenet_head:
        net.classifier[1] = nn.Linear(net.classifier[1].in_features, 2)
    assert all(p.device.type == 'cpu' for p in net.parameters())
    return net


def smoke(rows):
    seed(SEED)
    env = environment()
    net = model(imagenet_head=True).eval()
    x = tensor(rows[0]['HeaderAsset']).unsqueeze(0)
    with torch.inference_mode():
        out = net(x)
    assert out.shape == (1, 1000) and torch.isfinite(out).all()
    assert out.device.type == 'cpu'
    env.update(Status='PASSED', Sha256=rows[0]['Sha256'], HeaderAsset=rows[0]['HeaderAsset'],
               HeaderSha256=rows[0]['HeaderSha256'], InputShape=list(x.shape),
               OutputShape=list(out.shape), OutputFinite=True, Holdout=False,
               OutputSha256=hashlib.sha256(out.numpy().tobytes()).hexdigest().upper())
    save(HERE / 'header-smoke-test.json', env)
    print('SMOKE PASSED: local ImageNet EfficientNet-B0, CPU, allowed header, finite output.', flush=True)


def features(rows):
    cache = LOCAL / 'header-features.npy'
    manifest = LOCAL / 'header-feature-cache.json'
    identity = {'Config': CONFIG, 'FoldManifestSha256': sha(HERE / 'fold-manifest.csv'),
                'Headers': [{'Sha256': r['Sha256'], 'HeaderSha256': r['HeaderSha256']} for r in rows],
                'WeightsSha256': WEIGHT_SHA}
    completed = 0
    if manifest.exists():
        state = json.loads(manifest.read_text())
        assert state['Identity'] == identity
        completed = state['Completed']
        if completed == len(rows):
            assert sha(cache) == state['CacheSha256']
            return np.load(cache, mmap_mode='r')
    seed(SEED)
    net = model().eval()
    trunk = net.features[:8].eval()
    for p in trunk.parameters():
        p.requires_grad = False
    frozen_state = {k: v.clone() for k, v in trunk.state_dict().items()}
    mmap = np.lib.format.open_memmap(cache, mode='r+' if completed else 'w+',
                                   dtype=np.float32, shape=(len(rows), 320, 12, 12))
    start = time.time()
    for i in range(completed, len(rows), CONFIG['CacheBatchSize']):
        batch = rows[i:i + CONFIG['CacheBatchSize']]
        x = torch.stack([tensor(r['HeaderAsset']) for r in batch])
        with torch.inference_mode():
            value = trunk(x)
        assert value.shape[1:] == (320, 12, 12) and torch.isfinite(value).all()
        mmap[i:i + len(batch)] = value.numpy()
        mmap.flush()
        done = i + len(batch)
        save(manifest, {'Identity': identity, 'Completed': done})
        if done % 20 == 0 or done == len(rows):
            print(f'ImageNet header cache {done}/{len(rows)}, elapsed {time.time()-start:.1f}s', flush=True)
    assert all(torch.equal(v, trunk.state_dict()[k]) for k, v in frozen_state.items())
    save(manifest, {'Identity': identity, 'Completed': len(rows), 'CacheSha256': sha(cache),
                    'FrozenParametersAndBuffersUnchanged': True, 'LabelsUsed': False,
                    'ImageReads': len(rows), 'HoldoutImageReads': 0})
    return mmap


def train_fold(fold, rows, maps):
    target = LOCAL / f'header-fold-{fold}.pt'
    result = HERE / f'header-fold-{fold}.json'
    if result.exists():
        record = json.loads(result.read_text())
        assert record['Config'] == CONFIG and sha(target) == record['CheckpointSha256']
        assert record['FoldManifestSha256'] == sha(HERE / 'fold-manifest.csv')
        return record
    seed(SEED + fold)
    train_idx = np.array([i for i, r in enumerate(rows) if int(r['Fold']) != fold])
    val_idx = np.array([i for i, r in enumerate(rows) if int(r['Fold']) == fold])
    assert not set(train_idx) & set(val_idx)
    labels = torch.tensor([CONFIG['ClassMapping'][r['LabelFinal']] for r in rows])
    counts = torch.bincount(labels[train_idx], minlength=2)
    class_weights = len(train_idx) / (2 * counts.float())
    net = model()
    for p in net.parameters():
        p.requires_grad = False
    for p in net.classifier.parameters():
        p.requires_grad = True
    block = net.features[8]
    history = []
    started = time.time()
    for phase in (1, 2):
        cfg = CONFIG[f'Phase{phase}']
        if phase == 2:
            for p in block.parameters():
                p.requires_grad = True
        optimizer = torch.optim.AdamW([p for p in net.parameters() if p.requires_grad],
                                     lr=cfg['LearningRate'], weight_decay=CONFIG['WeightDecay'])
        for epoch in range(1, cfg['Epochs'] + 1):
            net.eval()  # frozen trunk BN always eval
            block.train(phase == 2)
            net.classifier.train()
            permutation = train_idx[torch.randperm(len(train_idx)).numpy()]
            total = 0.0
            for start in range(0, len(permutation), CONFIG['BatchSize']):
                indices = permutation[start:start + CONFIG['BatchSize']]
                x = torch.from_numpy(np.array(maps[indices], copy=True))
                y = labels[indices]
                optimizer.zero_grad(set_to_none=True)
                if phase == 1:
                    with torch.no_grad():
                        encoded = block(x)
                else:
                    encoded = block(x)
                logits = net.classifier(net.avgpool(encoded).flatten(1))
                loss = (nn.functional.cross_entropy(logits, y, reduction='none') * class_weights[y]).mean()
                assert torch.isfinite(loss)
                loss.backward()
                optimizer.step()
                total += loss.item() * len(indices)
            history.append({'Phase': phase, 'Epoch': epoch, 'TrainWeightedLoss': total / len(train_idx)})
            save(HERE / f'header-fold-{fold}-progress.json', {'History': history, 'Config': CONFIG})
            print(f'Fold {fold}, phase {phase}, epoch {epoch}/{cfg["Epochs"]}, train loss {total/len(train_idx):.5f}', flush=True)
    net.eval()
    predictions = []
    with torch.inference_mode():
        for start in range(0, len(val_idx), CONFIG['BatchSize']):
            indices = val_idx[start:start + CONFIG['BatchSize']]
            x = torch.from_numpy(np.array(maps[indices], copy=True))
            logits = net.classifier(net.avgpool(block(x)).flatten(1))
            prob = torch.softmax(logits, dim=1)[:, 1]
            assert torch.isfinite(prob).all()
            for index, value in zip(indices, prob.tolist()):
                predictions.append({'Sha256': rows[index]['Sha256'], 'PFactura': value})
    torch.save({'Milestone': 'H1D10B2', 'Fold': fold, 'Config': CONFIG,
                'StateDict': net.state_dict(), 'ClassMapping': CONFIG['ClassMapping'],
                'TrainSha256': [rows[i]['Sha256'] for i in train_idx],
                'ValidationSha256': [rows[i]['Sha256'] for i in val_idx],
                'ImageNetSha256': WEIGHT_SHA}, target)
    # Checkpoint roundtrip and end-to-end parity on one allowed outer-validation header.
    loaded = torch.load(target, map_location='cpu', weights_only=True)
    restored = models.efficientnet_b0(weights=None)
    restored.classifier[1] = nn.Linear(1280, 2)
    restored.load_state_dict(loaded['StateDict'], strict=True)
    restored.eval()
    with torch.inference_mode():
        x = tensor(rows[val_idx[0]]['HeaderAsset']).unsqueeze(0)
        direct = torch.softmax(restored(x), dim=1)[0, 1].item()
    parity = abs(direct - predictions[0]['PFactura'])
    assert parity < 1e-5, parity
    record = {'Fold': fold, 'Config': CONFIG, 'TrainDocuments': len(train_idx),
              'ValidationDocuments': len(val_idx), 'ClassWeightsOtroFactura': class_weights.tolist(),
              'History': history, 'Predictions': predictions, 'Checkpoint': str(target),
              'CheckpointSha256': sha(target), 'FoldManifestSha256': sha(HERE / 'fold-manifest.csv'),
              'TrainFamilies': sorted({rows[i]['FamilyId'] for i in train_idx}),
              'ValidationFamilies': sorted({rows[i]['FamilyId'] for i in val_idx}),
              'TrainSha256': [rows[i]['Sha256'] for i in train_idx],
              'ValidationSha256': [rows[i]['Sha256'] for i in val_idx],
              'ShaLeakage': 0, 'FamilyLeakage': 0, 'HoldoutLeakage': 0,
              'CheckpointDirectVsCachedMaxAbsError': parity, 'ElapsedSeconds': time.time() - started}
    save(result, record)
    print(f'Fold {fold} complete: {len(predictions)} OOF, checkpoint verified.', flush=True)
    return record


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--smoke', action='store_true')
    parser.add_argument('--train', action='store_true')
    args = parser.parse_args()
    rows = inputs()
    env = environment()
    if args.smoke:
        smoke(rows)
    if args.train:
        smoke_result = json.loads((HERE / 'header-smoke-test.json').read_text())
        assert smoke_result['Status'] == 'PASSED' and smoke_result['ImportedVersions'] == env['ImportedVersions']
        save(HERE / 'header-training-plan.json', {'Config': CONFIG, 'Environment': env,
             'TrainerSha256': sha(__file__), 'FoldManifestSha256': sha(HERE / 'fold-manifest.csv')})
        maps = features(rows)
        records = [train_fold(k, rows, maps) for k in range(4)]
        assert sum(len(r['Predictions']) for r in records) == 901
        inputs()  # final unchanged B1 / seal / header gate
        print('TRAINING COMPLETE: four fixed folds, 901 OOF, protected inputs unchanged.', flush=True)


if __name__ == '__main__':
    main()
