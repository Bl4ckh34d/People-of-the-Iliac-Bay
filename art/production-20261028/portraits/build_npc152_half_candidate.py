"""Bounded NPC152 afraid HALF repair from saved generated eyelid artwork.

The eyelid is copied at its native coordinates. Exposed eyeball remains the
same-gaze OPEN; no pupil translation, source overwrite or gallery publication.
"""
import hashlib
import json
import os
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
BASE = ROOT / 'art/portraits/mobile-mouths/npc152-keyframe-v2'
OUT = HERE / 'npc152-afraid-half-v2-candidate'
GAZES = ('front', 'left', 'right')
BOXES = ((16, 23, 29, 26), (36, 23, 50, 26))

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    OUT.mkdir(exist_ok=True)
    raw_path = HERE/'npc152-afraid-half-v2-raw.png'
    raw = Image.open(raw_path).convert('RGB').resize((64,35), Image.Resampling.NEAREST)
    selected = {}
    reviews = []
    original = Image.open(BASE/'original.png').convert('RGB')
    contact = Image.new('RGB', (5*256, 3*280), '#252525')
    draw = ImageDraw.Draw(contact)
    for row,gaze in enumerate(GAZES):
        opened_path = BASE/f'eyes/afraid/{gaze}/open.png'
        old_path = BASE/f'eyes/afraid/{gaze}/half.png'
        opened = Image.open(opened_path).convert('RGB')
        candidate = opened.copy()
        for box in BOXES:
            candidate.paste(raw.crop(box), box[:2])
        target = OUT/f'afraid-{gaze}-half.png'
        candidate.save(target)
        new_a,open_a,old_a = map(np.asarray,(candidate,opened,Image.open(old_path).convert('RGB')))
        mask=np.zeros((35,64),bool)
        for x0,y0,x1,y1 in BOXES:
            mask[y0:y1,x0:x1]=True
        assert np.array_equal(new_a[~mask],open_a[~mask])
        assert np.array_equal(new_a[26:],open_a[26:])
        selected[str(old_path.resolve())]=target
        reviews.append({'gaze':gaze,'sourceOpenSha256':sha(opened_path),'baselineHalfSha256':sha(old_path),'candidateSha256':sha(target),'changedFromOldHalf':int(np.any(new_a!=old_a,axis=2).sum()),'changedFromOpen':int(np.any(new_a!=open_a,axis=2).sum()),'outsideEnvelopeEqualsOwnOpen':True,'exposedEyeballRows26OnwardEqualsOwnOpen':True})
        frames=[('OPEN',opened),('old HALF',Image.open(old_path)),('new HALF',candidate),('CLOSED',Image.open(BASE/f'eyes/afraid/{gaze}/closed.png')),('original',original.crop((0,0,64,35)))]
        for col,(label,upper) in enumerate(frames):
            full=original.copy();full.paste(upper,(0,0))
            contact.paste(full.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24))
            draw.text((col*256+4,row*280+4),f'afraid {gaze} {label}',fill='white')
        frames_full=[]
        for upper in (opened,candidate,Image.open(BASE/f'eyes/afraid/{gaze}/closed.png'),candidate,opened):
            full=original.copy();full.paste(upper,(0,0));frames_full.append(full.resize((384,384),Image.Resampling.NEAREST))
        frames_full[0].save(OUT/f'afraid-{gaze}-blink-slow.gif',save_all=True,append_images=frames_full[1:],duration=[650,250,300,250,650],loop=0)
    contact.save(OUT/'comparison.png')
    manifest=json.loads((BASE/'manifest.json').read_text(encoding='utf-8-sig'))
    def rebase(obj):
        if isinstance(obj,list):
            return [rebase(x) for x in obj]
        if not isinstance(obj,dict):
            return obj
        out={k:rebase(v) for k,v in obj.items()}
        for key in ('file','difference','original','originalUpper','originalLower'):
            value=obj.get(key)
            if isinstance(value,str):
                source=(BASE/value).resolve()
                if source.is_file():
                    target=selected.get(str(source),source)
                    out[key]=Path(os.path.relpath(target,OUT)).as_posix()
                    if key=='file' and str(source) in selected:
                        out['sha256']=sha(target)
                        a=np.asarray(Image.open(target).convert('RGB'))
                        out['changedPixels']=int(np.any(a!=np.asarray(original.crop((0,0,64,35))),axis=2).sum())
                        out.pop('difference',None)
        return out
    manifest=rebase(manifest)
    manifest['status']='unresolved-front-half-parent-rejected'
    manifest['visualAcceptance']='failed-front-half-insufficient-closure'
    manifest['reviewLimitations']=['Parent inspected comparison.png and rejected FRONT HALF as practically OPEN; lateral closure remains weak.', 'The y26 lock prevents sufficient lid descent. Preserve only still-exposed iris in the next art attempt, not this fixed row boundary.', 'Only three afraid HALF images changed. Other pre-existing defects remain.', 'Ordered static contacts reviewed; GIF is supplied for playback, not evidence of temporal review.', 'Not published, not user-approved, no audio or runtime acceptance.']
    manifest['repair']={'sourceManifest':str(BASE/'manifest.json'),'sourceManifestSha256':sha(BASE/'manifest.json'),'rawSource':str(raw_path),'rawSha256':sha(raw_path),'rawDimensions':list(Image.open(raw_path).size),'sampling':'whole upper half resized nearest-neighbour to64x35; no local eye/pupil scaling','opaqueNativeBoxes':BOXES,'retainedExposedRows':'26..34 from same-gaze OPEN','frames':reviews}
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    (OUT/'review.json').write_text(json.dumps(manifest['repair'],indent=2),encoding='utf-8')
    print(json.dumps({'output':str(OUT),'changedFrames':len(reviews),'manifestSha256':sha(OUT/'manifest.json')}))

if __name__=='__main__':
    main()
