"""Build individually generated mood eyes with explicitly reviewed eye geometry."""
import json,hashlib,sys
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent;WORK=HERE/'npc195-eyes-v1'
FITS={
 'sad':{'aperture':[(17,18,27,20),(36,18,46,20)],'lids':[(14,17,30,19),(35,17,50,19)],'exposedRows':[19,19],'halfDonor':'neutral','nativePrefix':'sad-v2','reason':'Corrected sad-v2 master restores original aperture18..19. Own HALF remained OPEN; reviewed neutral same-face descending lid used, sad brow above17 retained.'},
 'happy':{'aperture':[(17,18,27,20),(36,18,46,20)],'lids':[(14,17,30,19),(35,17,50,19)],'exposedRows':[19,19],'reason':'Own happy master coordinate review: both apertures18..19; generated descending lid covers18.'},
 'angry':{'aperture':[(17,18,27,20),(36,18,46,20)],'lids':[(14,17,30,19),(35,17,50,19)],'exposedRows':[19,19],'reason':'Own angry coordinate review: aperture18..19, narrowed generated lash occludes18.'},
 'afraid':{'aperture':[(17,18,27,20),(36,18,46,20)],'lids':[(14,17,30,19),(35,17,50,19)],'exposedRows':[19,19],'halfDonor':'neutral','reason':'Own afraid HALF raw rejected as OPEN. Same-face reviewed neutral lid donor covers18; own fearful brow above17 and exposed19 retained.'},
 'guarded':{'aperture':[(17,18,27,20),(36,19,46,20)],'lids':[(14,17,30,19)],'exposedRows':[19,19],'rightClosedAtHalf':True,'reason':'Guarded right eye has only one aperture row19. HALF is asymmetric: left partial, right fully closed; no impossible fractional native-pixel vertical aperture.'},
}
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main(mood):
    fit=FITS[mood];out=WORK/f'{mood}-v2';out.mkdir(exist_ok=True)
    prefix=fit.get('nativePrefix',mood)
    front=Image.open(WORK/f'{prefix}-open-native.png').convert('RGB');closed=Image.open(WORK/f'{prefix}-closed-native.png').convert('RGB');half_raw=Image.open(WORK/f"{fit.get('halfDonor',mood)}-half-v2-raw.png").convert('RGB').resize((64,35),Image.Resampling.NEAREST)
    masters=json.loads((HERE/'npc195-masters-v1/masters-selected.json').read_text());master=Image.open(next(e['file'] for e in masters['masters'] if e['mood']==mood)).convert('RGB')
    contact=Image.new('RGB',(3*256,3*280),'#252525');d=ImageDraw.Draw(contact);rows=[]
    for row,gaze in enumerate(('front','left','right')):
        opened=front.copy()
        if gaze!='front':
            raw=Image.open(WORK/f'{mood}-{gaze}-v2-raw.png').convert('RGB').resize((64,35),Image.Resampling.NEAREST)
            for box in fit['aperture']:opened.paste(raw.crop(box),box[:2])
        half=opened.copy()
        for box in fit['lids']:half.paste(half_raw.crop(box),box[:2])
        if fit.get('rightClosedAtHalf'):
            box=(35,17,50,23);half.paste(closed.crop(box),box[:2])
        checks=list(zip(fit['aperture'],fit['exposedRows']))
        if fit.get('rightClosedAtHalf'):checks=checks[:1]
        for box,y in checks:
            assert np.array_equal(np.asarray(half)[y:box[3],box[0]:box[2]],np.asarray(opened)[y:box[3],box[0]:box[2]])
        for col,(phase,im) in enumerate((('open',opened),('half',half),('closed',closed))):
            target=out/f'{gaze}-{phase}.png';im.save(target);rows.append({'gaze':gaze,'phase':phase,'file':str(target),'sha256':sha(target)})
            full=master.copy();full.paste(im,(0,0));contact.paste(full.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+4,row*280+4),f'{mood} {gaze} {phase}',fill='white')
    contact.save(out/'review.png');(out/'manifest.json').write_text(json.dumps({'record':195,'mood':mood,'frames':rows,'fit':fit,'status':'parent-static-review-pending','reviewLimitations':['No temporal pass or publication.','Fit belongs to this particular mood, not a universal row lock.']},indent=2))
if __name__=='__main__':main(sys.argv[1])
