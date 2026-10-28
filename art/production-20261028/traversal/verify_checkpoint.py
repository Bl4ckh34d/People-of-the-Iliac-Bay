"""Verify current selected frame files against provenance and actual pixels."""
from pathlib import Path
from PIL import Image
import hashlib,json
ROOT=Path(__file__).resolve().parent
folders=['381-jump-eight-views-v1','381-climb-eight-views-v1','381-swim-eight-views-v2','families/382/jump-eight-views-v1','families/382/climb-eight-views-v1','families/382/swim-eight-views-v1']
folders.append('families/383/jump-eight-views-v1')
folders.extend(['families/383/climb-eight-views-v1','families/383/swim-five-views-v1'])
rows=[]
for folder in folders:
    path=ROOT/folder/'manifest.json';m=json.loads(path.read_text(encoding='utf-8'));byview={}
    for r in m['frames']:
        file=ROOT/folder/r['file'];assert hashlib.sha256(file.read_bytes()).hexdigest()==r['sha256'],file
        im=Image.open(file);assert im.mode=='RGBA',file;bb=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox();assert bb and bb[0]>0 and bb[1]>0 and bb[2]<im.width and bb[3]<im.height,file
        byview.setdefault(r.get('bearing',m.get('bearing',0)),[]).append(im.tobytes())
    for v,raws in byview.items():assert len(set(raws))==len(raws),(folder,v)
    atlas=ROOT/folder/('atlas-8x4.png' if folder=='381-jump-eight-views-v1' else 'atlas.png');actual=hashlib.sha256(atlas.read_bytes()).hexdigest();expected=m.get('atlasSha256')
    if expected:assert expected==actual,atlas
    rows.append({'folder':folder,'selectedFrames':len(m['frames']),'manifestSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'atlasSha256':actual,'frameHashesVerified':True,'rgba':True,'noAlpha128Clipping':True,'distinctWithinBearing':True,'visuallyReviewed':m.get('visuallyReviewed',m.get('visualReview') is not None)})
report={'date':'2026-10-28','selectedChecks':rows,'totalSelectedFrames':sum(r['selectedFrames'] for r in rows),'totalFolderBytes':sum(p.stat().st_size for p in ROOT.rglob('*') if p.is_file()),'integrated':False,'userApproved':False,'acceptedProductionFrames':0}
(ROOT/'checkpoint-verification.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(json.dumps(report,indent=2))
