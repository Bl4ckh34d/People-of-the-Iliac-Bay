"""Single-source neutral nine-eye candidate with source-aligned exposed iris."""
import json,hashlib
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent;WORK=HERE/'npc195-eyes-v1';OUT=WORK/'neutral-v2'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    OUT.mkdir(exist_ok=True);front=Image.open(WORK/'neutral-open-native.png').convert('RGB');closed=Image.open(WORK/'neutral-closed-native.png').convert('RGB');half_raw=Image.open(WORK/'neutral-half-v2-raw.png').convert('RGB').resize((64,35),Image.Resampling.NEAREST)
    orig=Image.open(HERE.parents[2]/'art/portraits/mobile-mouths/split-mask-prototype/catalog/common-195.png').convert('RGB')
    contact=Image.new('RGB',(3*256,3*280),'#252525');d=ImageDraw.Draw(contact);rows=[]
    for row,gaze in enumerate(('front','left','right')):
        opened=front.copy()
        if gaze!='front':
            raw=Image.open(WORK/f'neutral-{gaze}-open-v2-raw.png').convert('RGB').resize((64,35),Image.Resampling.NEAREST)
            for box in ((17,18,27,20),(36,18,46,20)):opened.paste(raw.crop(box),box[:2])
        half=opened.copy()
        for box in ((14,17,30,19),(35,17,50,19)):half.paste(half_raw.crop(box),box[:2])
        assert np.array_equal(np.asarray(half)[19:],np.asarray(opened)[19:])
        for col,(phase,im) in enumerate((('open',opened),('half',half),('closed',closed))):
            target=OUT/f'{gaze}-{phase}.png';im.save(target);rows.append({'gaze':gaze,'phase':phase,'file':str(target),'sha256':sha(target)})
            full=orig.copy();full.paste(im,(0,0));contact.paste(full.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+4,row*280+4),f'neutral {gaze} {phase}',fill='white')
    contact.save(OUT/'review.png');(OUT/'manifest.json').write_text(json.dumps({'record':195,'mood':'neutral','frames':rows,'sourceApertureRows':[18,19],'lateralApertureBoxes':[[17,18,27,20],[36,18,46,20]],'halfLidBoxes':[[14,17,30,19],[35,17,50,19]],'retainedExposedRow':19,'status':'parent-static-review-pending','reviewLimitations':['No temporal pass or publication.','Own OPEN row19 is retained in every HALF; upper iris row18 is occluded by generated lid.']},indent=2))
if __name__=='__main__':main()
