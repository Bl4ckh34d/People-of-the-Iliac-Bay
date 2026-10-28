"""Independent structural gate for the durable six-mood portrait contract.

This checks files and source identity; it does not grant visual/audio acceptance.
"""
import hashlib
import json
import sys
from pathlib import Path
from PIL import Image

MOODS={'neutral','happy','angry','sad','afraid','guarded'}
PHASES={'open','half','closed'}
SHAPES=set('ABCDEFGHX')

def digest(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def gate(path):
    path=Path(path).resolve();m=json.loads(path.read_text(encoding='utf-8-sig'))
    issues=[];assets=[]
    def check(item,size,label):
        p=(path.parent/item['file']).resolve()
        if not p.is_file():
            issues.append(f'{label}: missing {p}');return
        im=Image.open(p)
        if im.size!=size:issues.append(f'{label}: dimensions {im.size}')
        actual=digest(p)
        if item.get('sha256')!=actual:issues.append(f'{label}: stale/missing SHA256')
        assets.append({'state':label,'file':str(p),'sha256':actual,'dimensions':list(im.size)})
    if {s['id'] for s in m['eyeSets']}!=MOODS:issues.append('front mood set mismatch')
    for s in m['eyeSets']:
        if set(s['phases'])!=PHASES:issues.append(f"{s['id']}: phase mismatch")
        for phase,item in s['phases'].items():check(item,(64,35),f"eye/{s['id']}/front/{phase}")
    if set(m['thinkingEyeSetsByEmotion'])!=MOODS:issues.append('lateral mood set mismatch')
    for mood,sets in m['thinkingEyeSetsByEmotion'].items():
        if {s['id'] for s in sets}!={'left','right'}:issues.append(f'{mood}: lateral gaze mismatch')
        for s in sets:
            if set(s['phases'])!=PHASES:issues.append(f"{mood}/{s['id']}: phase mismatch")
            for phase,item in s['phases'].items():check(item,(64,35),f"eye/{mood}/{s['id']}/{phase}")
    if {s['id'] for s in m['mouthSets']}!=MOODS:issues.append('mouth mood set mismatch')
    for s in m['mouthSets']:
        if {f['shape'] for f in s['frames']}!=SHAPES:issues.append(f"{s['id']}: mouth shape mismatch")
        for frame in s['frames']:check(frame,(64,29),f"mouth/{s['id']}/{frame['shape']}")
    original=Image.open(path.parent/m['original']).convert('RGBA')
    for state,box in [('eye/neutral/front/open',(0,0,64,35)),('mouth/neutral/A',(0,35,64,64)),('mouth/neutral/X',(0,35,64,64))]:
        matches=[a for a in assets if a['state']==state]
        if len(matches)!=1 or Image.open(matches[0]['file']).convert('RGBA').tobytes()!=original.crop(box).tobytes():issues.append(f'{state}: not original-exact')
    if len(assets)!=108:issues.append(f'expected108 assets, got{len(assets)}')
    return {'manifest':str(path),'manifestSha256':digest(path),'record':m['portraitRecord'],'structuralPass':not issues,'issues':issues,'assets':assets,'limitations':['No visual, temporal, audio, publication or user acceptance implied.']}

if __name__=='__main__':
    result=gate(sys.argv[1])
    if len(sys.argv)>2:Path(sys.argv[2]).write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k!='assets'},indent=2))
    raise SystemExit(0 if result['structuralPass'] else 1)
