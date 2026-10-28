"""Fit saved CLOSED endpoints to selected195 masters, keeping own brows fixed."""
import hashlib,json
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent
OUT=HERE/'npc195-eyes-v1'
MASTERS=HERE/'npc195-masters-v1'
MOODS=('neutral','happy','angry','sad','afraid','guarded')
BOXES=((14,17,30,23),(35,17,50,23))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    masters=json.loads((MASTERS/'masters-manifest.json').read_text())
    source=Image.open(masters['source']).convert('RGB');rows=[]
    contact=Image.new('RGB',(4*256,6*280),'#252525');d=ImageDraw.Draw(contact)
    for row,mood in enumerate(MOODS):
        master=next(e for e in masters['masters'] if e['mood']==mood)
        original=Image.open(master['file']).convert('RGB');opened=original.crop((0,0,64,35))
        rawp=OUT/f'{mood}-closed-raw.png';raw=Image.open(rawp).convert('RGB').resize((64,35),Image.Resampling.NEAREST)
        closed=opened.copy()
        for box in BOXES:closed.paste(raw.crop(box),box[:2])
        opened.save(OUT/f'{mood}-open-native.png');closed.save(OUT/f'{mood}-closed-native.png');closed.resize((512,280),Image.Resampling.NEAREST).save(OUT/f'{mood}-closed-reference.png')
        mask=np.zeros((35,64),bool)
        for x0,y0,x1,y1 in BOXES:mask[y0:y1,x0:x1]=True
        assert np.array_equal(np.asarray(opened)[~mask],np.asarray(closed)[~mask])
        for col,(label,upper) in enumerate((('original',source.crop((0,0,64,35))),('OPEN',opened),('CLOSED raw',raw),('CLOSED composed',closed))):
            full=original.copy();full.paste(upper,(0,0));contact.paste(full.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+4,row*280+4),f'{mood} {label}',fill='white')
        rows.append({'mood':mood,'open':str(OUT/f'{mood}-open-native.png'),'openSha256':sha(OUT/f'{mood}-open-native.png'),'closed':str(OUT/f'{mood}-closed-native.png'),'closedSha256':sha(OUT/f'{mood}-closed-native.png'),'rawSha256':sha(rawp),'opaqueMovingEyeBoxes':BOXES,'outsideMovingEyeBoxesMatchesOwnOpen':True,'status':'static-review-pending'})
    contact.save(OUT/'endpoints-review.png');(OUT/'endpoints-manifest.json').write_text(json.dumps({'record':195,'endpoints':rows,'limitations':['No HALF or lateral frames yet.','Closed source anatomy needs actual static review; rectangular mask invariants alone are not acceptance.']},indent=2))
if __name__=='__main__':main()
