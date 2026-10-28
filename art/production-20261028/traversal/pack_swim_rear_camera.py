"""Bounded camera-only repair audit, separate from selected eight-view candidate."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib
ROOT=Path(__file__).resolve().parent
OUT=ROOT/'381-swim-rear-camera-repair-v1';OUT.mkdir(exist_ok=True)
source=ROOT/'381-swim-view-4-camera-repair-generated-v1.png'
raw=Image.open(source).convert('RGBA');scale=21/110
heads=[(376,133),(1127,138),(375,621),(1125,648)]
frames=[];records=[]
for phase,head in enumerate(heads):
    phase_source=source
    if phase==2:
        phase_source=ROOT/'381-swim-view-4-camera-repair-generated-v2.png'
        raw=Image.open(phase_source).convert('RGBA');head=(380,610)
    else:raw=Image.open(source).convert('RGBA')
    rect=[(0,0,768,512),(768,0,1536,512),(0,512,700,1024),(700,512,1536,1024)][phase]
    x0,y0=rect[0],rect[1];crop=raw.crop(rect)
    reduced=crop.resize((round(crop.width*scale),round(crop.height*scale)),Image.Resampling.NEAREST)
    x,y=round(128-(head[0]-x0)*scale),round(38-(head[1]-y0)*scale)
    im=Image.new('RGBA',(256,192));im.alpha_composite(reduced,(x,y))
    bbox=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<256 and bbox[3]<192
    filename=f'phase-{phase}.png';im.save(OUT/filename);frames.append(im)
    records.append({'phase':phase,'file':filename,'sha256':hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),'source':phase_source.name,'sourceSha256':hashlib.sha256(phase_source.read_bytes()).hexdigest(),'sourceCrop':rect,'pixelScale':scale,'placement':[x,y],'alpha128Bounds':bbox})
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
sheet=Image.new('RGB',(1024,192*3+60),(42,45,49));draw=ImageDraw.Draw(sheet)
draw.text((10,8),'Rear camera repair | original rear / regenerated lower rear / unchanged right profile | cutoff128 | native pixels',font=font,fill='white')
for row in range(3):
    for phase in range(4):
        if row==1:im=frames[phase].copy()
        else:
            bearing=4 if row==0 else 6
            name=['glide','pull','recovery','kick'][phase]
            im=Image.open(ROOT/'381-swim-eight-views-v1/frames'/f'view-{bearing}-phase-{phase}-{name}.png').convert('RGBA')
        im.putalpha(im.getchannel('A').point(lambda a:255 if a>=128 else 0));sheet.paste(im,(phase*256,60+row*192),im)
        if phase==0:draw.text((10,60+row*192),['Original rear','Regenerated lower rear','Right profile master'][row],font=font,fill='white')
sheet.save(OUT/'camera-comparison.png')
sheet.resize((2048,1272),Image.Resampling.NEAREST).save(OUT/'camera-comparison-2x.png')
manifest={'status':'camera-repair-candidate-pending-parent-review','source':source.name,'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'appearance':381,'state':'swimming','bearing':4,'frames':records,'technicalChecks':{'rgba':True,'noAlpha128Clipping':True,'identicalPixelScale':True},'visuallyReviewed':False,'uniformCameraAccepted':False,'runtimeIntegrated':False,'userApproved':False,'notes':['Camera projection genuinely regenerated; no canvas squashing or perspective warping.','Feet more foreground, torso foreshortened; exact match to profile eye height remains under review.','Recovery arm projection differs from master; phase consistency remains a limitation.']}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(str(OUT/'camera-comparison.png'))
