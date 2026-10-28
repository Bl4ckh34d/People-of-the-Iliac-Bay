"""Inspect a genuine reach/pull/tuck/push alternative without claiming contact acceptance."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import hashlib,json
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'381-climb-paired-pilot-v1';OUT.mkdir(exist_ok=True)
source=ROOT/'381-climb-paired-pilot-generated-v1.png';raw=Image.open(source).convert('RGBA')
W,H=160,224;SCALE=21/110
heads=[(274,90),(794,105),(294,865),(784,805)]
names=['reach','pull','tuck','push'];frames=[];records=[]
for phase,head in enumerate(heads):
    rect=[(0,0,512,768),(512,0,1024,720),(0,768,512,1536),(512,720,1024,1536)][phase]
    x0,y0=rect[0],rect[1]
    crop=raw.crop(rect);reduced=crop.resize((round(crop.width*SCALE),round(crop.height*SCALE)),Image.Resampling.NEAREST)
    x,y=round(90-(head[0]-x0)*SCALE),round(44-(head[1]-y0)*SCALE)
    im=Image.new('RGBA',(W,H));im.alpha_composite(reduced,(x,y));frames.append(im)
    bb=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox();assert bb and bb[0]>0 and bb[1]>0 and bb[2]<W and bb[3]<H,(phase,bb)
    filename=f'phase-{phase}-{names[phase]}.png';im.save(OUT/filename)
    records.append({'phase':phase,'name':names[phase],'file':filename,'sha256':hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),'sourceCrop':rect,'pixelScale':SCALE,'placement':[x,y],'alpha128Bounds':bb})
atlas=Image.new('RGBA',(W*4,H))
for phase,im in enumerate(frames):atlas.paste(im,(phase*W,0))
atlas.save(OUT/'atlas.png')
review=Image.new('RGB',(W*4*3,H*3+60),(42,45,49));d=ImageDraw.Draw(review);font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
d.text((10,8),'381 climbing alternative | reach / pull / tuck / push | actual joint motion | fixed scale | candidate only',font=font,fill='white')
for phase,im in enumerate(frames):
    cut=im.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0));large=cut.resize((W*3,H*3),Image.Resampling.NEAREST)
    review.paste(large,(phase*W*3,55),large);d.text((phase*W*3+10,30),names[phase],font=font,fill='white')
review.save(OUT/'contact-review.png')
manifest={'status':'alternative-climb-motion-candidate','appearance':381,'state':'climbing','bearing':6,'phases':names,'source':source.name,'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'frameCount':4,'pixelCanvas':[W,H],'pixelScale':SCALE,'frames':records,'technicalChecks':{'rgba':True,'fourDistinctFrames':len({im.tobytes() for im in frames})==4,'noAlpha128Clipping':True,'uniformPixelScale':True},'visuallyReviewed':False,'runtimeIntegrated':False,'userApproved':False,'knownLimits':['Paired-hand alternative, not the same contralateral cycle as prior candidates.','Far limbs overlap substantially in pull/tuck; exact anatomical connectivity not proven.','Static artwork alone does not establish fixed wall grips or physical weight transfer.','No integration or temporal acceptance.']}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(str(OUT/'contact-review.png'))
