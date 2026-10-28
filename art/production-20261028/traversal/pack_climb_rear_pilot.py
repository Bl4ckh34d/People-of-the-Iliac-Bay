"""Audit visible opposite-arm/knee alternation in the existing rear climb pilot."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import hashlib,json
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'381-climb-rear-cycle-v1';OUT.mkdir(exist_ok=True)
source=ROOT/'381-climb-view-4-generated-v2.png';raw=Image.open(source).convert('RGBA')
SCALE=21/100;W,H=160,224
frames=[];records=[]
for phase in range(4):
    rect=(phase*512,0,(phase+1)*512,768);crop=raw.crop(rect)
    reduced=crop.resize((round(crop.width*SCALE),round(crop.height*SCALE)),Image.Resampling.NEAREST)
    im=Image.new('RGBA',(W,H));im.alpha_composite(reduced,(26,23));frames.append(im)
    bb=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bb and bb[0]>0 and bb[1]>0 and bb[2]<W and bb[3]<H
    filename=f'phase-{phase}.png';im.save(OUT/filename)
    records.append({'phase':phase,'file':filename,'sha256':hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),'source':source.name,'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'sourceCrop':rect,'pixelScale':SCALE,'placement':[26,23],'alpha128Bounds':bb})
atlas=Image.new('RGBA',(W*4,H))
for phase,im in enumerate(frames):atlas.paste(im,(phase*W,0))
atlas.save(OUT/'atlas.png')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
review=Image.new('RGB',(W*4*2,H*2*2+70),(42,45,49));d=ImageDraw.Draw(review)
d.text((10,8),'381 rear / profile climb | opposite-arm and knee audit | contact not accepted |2x native pixels',font=font,fill='white')
labels=['Left reach / right knee','Left pull / right knee retained','Right reach / left knee','Right pull / left knee retained']
for phase,im in enumerate(frames):
    for row in range(2):
        if row:im=Image.open(ROOT/'381-climb-right-cycle-v2'/f'phase-{phase}-{["near-reach","near-pull","far-reach","far-pull"][phase]}.png').convert('RGBA')
        cut=im.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0));large=cut.resize((W*2,H*2),Image.Resampling.NEAREST)
        x,y=phase*W*2,70+row*H*2;review.paste(large,(x,y),large)
        if row==0:d.text((x+10,y-23),labels[phase],font=font,fill='white')
review.save(OUT/'rear-profile-comparison.png')
assert len({im.tobytes() for im in frames})==4
manifest={'status':'rear-climb-alternation-candidate','appearance':381,'state':'climbing','bearing':4,'frameCount':4,'frames':records,'pixelCanvas':[W,H],'technicalChecks':{'rgba':True,'fourDistinctPhases':True,'noAlpha128Clipping':True,'uniformPixelScale':True},'visuallyReviewed':False,'runtimeIntegrated':False,'userApproved':False,'knownLimits':['Reach phases visibly alternate opposite hand/knee. Pull phases retain previous knee lead; intermediate leg transition is sparse.','Rear view cannot prove front-back wall contact depth.','Comparison does not prove matching limb depth identity in profile.']}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(str(OUT/'rear-profile-comparison.png'))
