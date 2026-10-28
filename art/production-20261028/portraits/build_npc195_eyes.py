"""Sample conditioned195 upper-half atlases without inventing missing frames."""
import hashlib,json
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent
WORK=HERE/'npc195-eyes-v1'
OUT=WORK/'candidate'
MOODS=('neutral','happy','angry','sad','afraid','guarded')
GAZES=('front','left','right')
PHASES=('open','half','closed')
BOXES=((14,17,30,23),(35,17,50,23))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    OUT.mkdir(exist_ok=True)
    masters=json.loads((HERE/'npc195-masters-v1/masters-manifest.json').read_text())
    original=Image.open(masters['source']).convert('RGB');rows=[]
    mask=np.zeros((35,64),bool)
    for x0,y0,x1,y1 in BOXES:mask[y0:y1,x0:x1]=True
    stable=np.ones((35,64),bool);stable[10:25,11:53]=False
    for mood in MOODS:
        master=Image.open(next(m['file'] for m in masters['masters'] if m['mood']==mood)).convert('RGB')
        opened=Image.open(WORK/f'{mood}-open-native.png').convert('RGB')
        closed=Image.open(WORK/f'{mood}-closed-native.png').convert('RGB')
        rawp=WORK/f'{mood}-atlas-raw.png';atlas=Image.open(rawp).convert('RGB');w,h=atlas.size
        contact=Image.new('RGB',(3*256,3*280),'#252525');d=ImageDraw.Draw(contact)
        for gazeindex,gaze in enumerate(GAZES):
            for phaseindex,phase in enumerate(PHASES):
                box=(phaseindex*w/3,gazeindex*h/3,(phaseindex+1)*w/3,(gazeindex+1)*h/3)
                cell=atlas.transform((64,35),Image.Transform.EXTENT,box,Image.Resampling.NEAREST)
                cost=float(np.abs(np.asarray(cell).astype(float)-np.asarray(opened).astype(float))[stable].mean())
                if phase=='closed':result=closed.copy();method='selected same-mood CLOSED endpoint shared across directions'
                elif phase=='open' and gaze=='front':result=opened.copy();method='selected same-mood FRONT OPEN endpoint'
                else:
                    result=opened.copy()
                    for region in BOXES:result.paste(cell.crop(region),region[:2])
                    method='own atlas whole-cell fractional nearest-neighbour sample; opaque moving-eye regions'
                assert np.array_equal(np.asarray(result)[~mask],np.asarray(opened)[~mask])
                target=OUT/f'eyes/{mood}/{gaze}/{phase}.png';target.parent.mkdir(parents=True,exist_ok=True);result.save(target)
                rows.append({'mood':mood,'gaze':gaze,'phase':phase,'file':str(target),'sha256':sha(target),'dimensions':[64,35],'method':method,'rawAtlas':str(rawp),'rawSha256':sha(rawp),'rawCellBounds':box,'stationaryLandmarkMeanAbsoluteDifference':cost,'outsideMovingEyeRegionsMatchesOwnFrontOpen':True,'movingEyeRegions':BOXES})
                full=master.copy();full.paste(result,(0,0));contact.paste(full.resize((256,256),Image.Resampling.NEAREST),(phaseindex*256,gazeindex*280+24));d.text((phaseindex*256+3,gazeindex*280+3),f'{mood} {gaze} {phase}',fill='white')
        contact.save(OUT/f'{mood}-review.png')
    report={'record':195,'sourceSha256':masters['sourceSha256'],'eyeFrames':rows,'eyeCount':len(rows),'status':'generated-native-review-pending','reviewLimitations':['54 dimension/hash/outer-stationarity checks do not establish pupil stability, clean HALF anatomy or motion.','No mouth atlas, voice, gallery publication or user acceptance.']}
    (OUT/'eyes-manifest.json').write_text(json.dumps(report,indent=2));print('eye frames',len(rows))
if __name__=='__main__':main()
