"""Register DET portrait originals with native hashes and exact reuse evidence.

Reads the installed Unity bundle without modifying it. Sprite duplicates are
excluded. Exact originals already in the native catalog are referenced in place.
"""
import hashlib
import json
from pathlib import Path
import UnityPy
from PIL import Image, ImageDraw

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
CATALOG=ROOT/'art/portraits/mobile-mouths/split-mask-prototype/portrait-catalog.json'

def sha(data):return hashlib.sha256(data).hexdigest()

def main():
    index=json.loads((ROOT/'art/production-20261028/mod-container-index.json').read_text())
    bundle=next(b for b in index['bundles'] if any('portraits/billboardmatch' in a['path'] for a in b['assets']))
    catalog=json.loads(CATALOG.read_text(encoding='utf-8-sig'))
    exact={};uppers={};lowers={}
    for entry in catalog['entries']:
        p=CATALOG.parent/entry['original'];im=Image.open(p).convert('RGBA')
        exact.setdefault(sha(im.tobytes()),[]).append((entry['id'],str(p)))
        uppers.setdefault(sha(im.crop((0,0,64,35)).tobytes()),[]).append(entry['id'])
        lowers.setdefault(sha(im.crop((0,35,64,64)).tobytes()),[]).append(entry['id'])
    env=UnityPy.load(bundle['bundle']);out=HERE/'det-source-originals';out.mkdir(exist_ok=True)
    rows=[];pixels={};thumbs=[]
    for asset,obj in sorted(env.container.items(),key=lambda pair:pair[0]):
        if 'portraits/billboardmatch' not in asset or obj.type.name!='Texture2D':continue
        im=obj.read().image.convert('RGBA');pixel=sha(im.tobytes())
        filename=Path(asset).name;matches=exact.get(pixel,[])
        if matches:p=Path(matches[0][1])
        elif pixel in pixels:p=pixels[pixel]
        else:p=out/filename;im.save(p);pixels[pixel]=p
        rows.append({'asset':asset,'fileName':filename,'dimensions':list(im.size),'original':str(p),'fileSha256':sha(p.read_bytes()),'rgbaSha256':pixel,'exactCatalogMatches':[m[0] for m in matches],'exactUpperMatches':uppers.get(sha(im.crop((0,0,64,35)).tobytes()),[]),'exactLowerMatches':lowers.get(sha(im.crop((0,35,64,64)).tobytes()),[]),'animationStatus':'not-produced','visualStatus':'source-inventory-only','voiceStatus':'not-assigned'})
        thumbs.append((filename,im))
    sheet=Image.new('RGB',(8*128,9*150),'#252525');d=ImageDraw.Draw(sheet)
    for i,(label,im) in enumerate(thumbs):
        x,y=(i%8)*128,(i//8)*150;sheet.paste(im.convert('RGB').resize((128,128),Image.Resampling.NEAREST),(x,y+22));d.text((x+2,y+3),label.split('_')[-1],fill='white')
    sheet.save(HERE/'det-originals-contact.png')
    report={'sourceBundle':bundle['bundle'],'sourceBundleSha256':bundle['sha256'],'sourceCount':len(rows),'uniquePixelCount':len({r['rgbaSha256'] for r in rows}),'exactCatalogMatchesCount':sum(bool(r['exactCatalogMatches']) for r in rows),'newFilesWritten':len(pixels),'sourceCatalogSha256':sha(CATALOG.read_bytes()),'entries':rows,'limitations':['Installed mod source only; enabled runtime selection must be joined separately.','Exact half matches prove source equality only, not every intended expressive feature mask.','No new animation or voice coverage claimed.']}
    (HERE/'det-portrait-sources.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('entries','limitations')},indent=2))

if __name__=='__main__':main()
