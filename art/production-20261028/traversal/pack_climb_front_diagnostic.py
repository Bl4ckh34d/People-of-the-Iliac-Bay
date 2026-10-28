"""Pack front limb-identity diagnostic; this is not a temporally accepted cycle."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import hashlib,json
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'381-climb-front-diagnostic-v1';OUT.mkdir(exist_ok=True)
sheet=ROOT/'381-climb-view-0-diagnostic-generated-v2.png'
single=ROOT/'381-climb-front-right-reach-generated-v1.png'
W,H=160,224;frames=[];records=[]
for phase in range(4):
    source=single if phase==2 else sheet
    raw=Image.open(source).convert('RGBA')
    if phase==2:rect=(0,0,1024,1536);head=(522,403);scale=21/145
    else:
        rect=[(0,0,724,543),(724,0,1448,543),(0,543,724,1086),(724,543,1448,1086)][phase]
        head=[(482,76),(990,76),(482,620),(990,602)][phase];scale=21/72
    crop=raw.crop(rect);reduced=crop.resize((round(crop.width*scale),round(crop.height*scale)),Image.Resampling.NEAREST)
    x,y=round(80-(head[0]-rect[0])*scale),round(55-(head[1]-rect[1])*scale)
    im=Image.new('RGBA',(W,H));im.alpha_composite(reduced,(x,y));frames.append(im)
    bb=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bb and bb[0]>0 and bb[1]>0 and bb[2]<W and bb[3]<H,(phase,bb)
    filename=f'phase-{phase}.png';im.save(OUT/filename)
    records.append({'phase':phase,'file':filename,'sha256':hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),'source':source.name,'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'sourceCrop':rect,'pixelScale':scale,'scaleBasis':'Head-width target21; single repaired phase has different body/head ratio, disclosed and not normalized by bbox.','placement':[x,y],'alpha128Bounds':bb})
review=Image.new('RGB',(W*4*3,H*3+70),(42,45,49));d=ImageDraw.Draw(review);font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
d.text((10,8),'381 front climb DIAGNOSTIC | phase2 single repair | anatomy differs; no cycle acceptance |3x',font=font,fill='white')
for phase,im in enumerate(frames):
    im=im.copy();im.putalpha(im.getchannel('A').point(lambda a:255 if a>=128 else 0));large=im.resize((W*3,H*3),Image.Resampling.NEAREST);review.paste(large,(phase*W*3,64),large)
    d.text((phase*W*3+10,32),['Left reach / right knee','Left pull / left knee mid','Right reach / left knee','Right pull / right knee'][phase],font=font,fill='white')
review.save(OUT/'contact-review.png')
manifest={'status':'front-limb-identity-diagnostic-not-production','appearance':381,'state':'climbing','bearing':0,'frameCount':4,'frames':records,'technicalChecks':{'rgba':True,'noAlpha128ClippingPacked':True},'visuallyReviewed':False,'runtimeIntegrated':False,'userApproved':False,'knownLimits':['Phase2 regenerated as single frame after grid repair changed the wrong knee; body/head ratio differs from other phases.','Green pouch stays screenright in selected candidates.','Pull leg transitions differ from rear cycle and remain temporally unaccepted.','This is limb-side audit evidence, not added to selected coverage.']}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(str(OUT/'contact-review.png'))
