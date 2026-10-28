"""Verify current candidate package and build a compact local review index."""
from pathlib import Path
from PIL import Image
from html import escape
from collections import Counter
import hashlib,json
OUT=Path(__file__).resolve().parent
def read(name):return json.loads((OUT/name).read_text(encoding='utf-8'))
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def pixels(path):return hashlib.sha256(Image.open(path).convert('RGBA').tobytes()).hexdigest()
native=read('native-idle-candidate-coverage.json')
sleep=read('sleep-candidate-coverage.json')
pilot=read('idle-det-10015-0/manifest.json')
inventory=read('../source-inventory.json')
byid={x['id']:x for x in inventory['identities']}
checks=Counter()
for row in native['sets']:
    assert sha(row['source'])==row['sourcePngSha256']
    assert pixels(row['source'])==row['sourcePixelSha256']
    assert sha(row['rawAtlas'])==row['rawAtlasSha256']
    assert len(row['frames'])==8
    assert row['visualChecked']
    for alias in row['aliases']:
        assert byid[alias]['sourcePixelHash']==row['sourcePixelSha256']
        assert byid[alias]['sourceDimensions']==row['nativeSourceDimensions']
        checks['nativeAliasHashAndDimensions']+=1
    for f in row['frames']:
        assert sha(f['path'])==f['sha256']
        assert list(Image.open(f['path']).size)==f['size']
        assert f['size'][1]==row.get('exportHeight',row['nativeSourceDimensions'][1])
        checks['nativeFrameHashAndSize']+=1
    if row['nativeAssignedView'] is not None:
        assert sha(row['frames'][row['nativeAssignedView']]['path'])==row['sourcePngSha256']
        checks['nativeOriginalBytePreserved']+=1
    else:
        assert row['originalRetainedAsSeparateSource'] and row['sourcePosture']
        checks['nativeOriginalSeparateSourceVerified']+=1
for row in sleep['entries']:
    assert sha(row['file'])==row['sha256']
    assert pixels(row['source'])==row['sourcePixelSha256']
    assert list(Image.open(row['file']).size)==row['size']
    assert not row['integrated'] and not row['userApproved']
    checks['sleepSourceAndOutputHashSize']+=1
for f in pilot['frames']:
    assert sha(f['path'])==f['sha256']
    checks['detPilotFrameHashes']+=1
report={'date':'2026-10-28','checks':dict(checks),'nativeIdleSets':len(native['sets']),'nativeIdleIdentities':native['identityCount'],'detIdleSets':1,'sleepCandidates':len(sleep['entries']),'sleepStatus':dict(Counter(x['candidateStatus'] for x in sleep['entries'])),'integrated':False,'userApproved':False,'limitations':['Existing24 mobile192view checks are file/atlas/native-front technical checks only; generated native-resolution backs are not decoded original artwork or identity proof.','Native 182/35 gold ear hoop omitted; guard183/2 beard and armor detail inconsistent; all generated alternate angles show smoother/broader art than sources.','Seven sleep identities retain explicit source ambiguity/detail limits.','This package does not claim complete idle coverage. See scope-reconciliation.json for remaining provider-qualified identities.']}
report['nativeIdleCandidateStatus']=dict(Counter(x.get('candidateStatus','reviewed-with-rendering-limits') for x in native['sets']))
report['nativeIdleNotCompleteIdentities']=[alias for x in native['sets'] if not x.get('usableAsCompleteEightViewCandidate',True) for alias in x['aliases']]
report['limitations'].append('183/13 back proportions still need repair; its two aliases are not complete eight-view candidates despite having eight exported frames and a recorded visual inspection.')
(OUT/'technical-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
parts=['<!doctype html><meta charset="utf-8"><title>DFU Idle / Sleep candidate review</title><style>body{font:16px system-ui;background:#20242a;color:#eceff2;margin:28px;max-width:1500px}a{color:#8ecbff}img{max-width:100%;height:auto;image-rendering:pixelated;border:1px solid #646b77}section{margin:32px 0}li{margin:8px 0}details{margin:20px 0}summary{cursor:pointer}</style><h1>Idle / Sleep — Kandidaten 28.10.2026</h1>',f'<p>{native["setCount"]} native Idle-Sets / {native["identityCount"]} identische Quellen; ein DET-Idle-Pilot; {len(sleep["entries"])} neue DET-Schlafbilder. Technisch und visuell geprüft, mit dokumentierten Grenzen. <b>Nicht integriert, nicht vom Nutzer abgenommen.</b></p>','<p><a href="technical-verification.json">Prüfergebnis</a> · <a href="native-idle-candidate-coverage.json">Idle-Mapping</a> · <a href="sleep-candidate-coverage.json">Schlaf-Mapping</a> · <a href="scope-reconciliation.json">Restumfang</a> · <a href="rejected-generations.json">Verworfene Entwürfe</a></p>','<ul>']
parts += [f'<li>{escape(s)}</li>' for s in report['limitations']]
parts += ['</ul><h2>Native Idle</h2>']
for row in native['sets']:
    key=f'{row["archive"]}/{row["record"]}'
    parts += [f'<section><h3>{key} — {len(row["aliases"])} identische Quellen</h3><p>{escape("; ".join(row["limits"]))}</p>',f'<img loading="lazy" src="native-{row["archive"]}-{row["record"]}-idle-native-review.png" alt="Original und acht Ansichten {key}"></section>']
parts += ['<h2>DET-Idle-Pilot 10015/0</h2><p>Goldenes Augen-/Brillendetail nicht exakt erhalten.</p><img src="idle-det-10015-0-native-review.png" alt="DET-Pilot Original und acht Ansichten"><h2>75 DET-Schlafkandidaten</h2><p>68 reviewable, sieben mit Identitätsgrenzen. Grüne Decke deckt Hals bis Fuß; weiße Kissen; Augen geschlossen geprüft. Seiten enthalten Originalreferenzen.</p>']
for page in sleep['reviewPages']:
    name=Path(page).name
    parts.append(f'<details><summary>{escape(name)}</summary><img loading="lazy" src="{escape(name)}" alt="Originale und Schlafbilder"></details>')
(OUT/'review.html').write_text('\n'.join(parts),encoding='utf-8')
print(json.dumps(report))
