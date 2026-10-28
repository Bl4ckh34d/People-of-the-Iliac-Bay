"""Pack four authored horizontal swimming poses from their actual non-overlapping regions."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib,statistics
ROOT=Path(__file__).resolve().parent
OUT=ROOT/'381-swim-right-cycle-v1';OUT.mkdir(exist_ok=True)
PATH=ROOT/'381-swim-view-6-generated-v3.png'
raw=Image.open(PATH).convert('RGBA')
PHASES=['glide','pull','recovery','kick']
# The generator did not honor equal columns. These rectangles follow actual separated figures.
RECTS=[(0,180,728,570),(730,180,raw.width,570),(0,730,525,1130),(550,730,raw.width,1130)]
HEAD_CENTER=[510,365,398,500]
HEAD_TOP=[65,49,47,74]
SCALE=21/106
W,H=224,128
frames=[];records=[]
for f,rect in enumerate(RECTS):
    cell=raw.crop(rect)
    reduced=cell.resize((round(cell.width*SCALE),round(cell.height*SCALE)),Image.Resampling.NEAREST)
    x,y=round(154-HEAD_CENTER[f]*SCALE),round(28-HEAD_TOP[f]*SCALE)
    target=Image.new('RGBA',(W,H));target.alpha_composite(reduced,(x,y))
    bbox=target.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<W and bbox[3]<H
    filename=f'phase-{f}-{PHASES[f]}.png';target.save(OUT/filename);frames.append(target)
    records.append({'phase':f,'name':PHASES[f],'file':filename,'sha256':hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),'sourceCrop':rect,'scale':SCALE,'placement':[x,y],'alpha128Bounds':bbox,'medianCutoutAlpha':statistics.median(a for a in target.getchannel('A').getdata() if a>=128)})
atlas=Image.new('RGBA',(W*4,H))
for f,im in enumerate(frames):atlas.paste(im,(f*W,0))
atlas.save(OUT/'atlas.png')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
review=Image.new('RGB',(W*2*3,H*2*3+70),(42,45,49));draw=ImageDraw.Draw(review)
draw.text((10,8),'381 right-profile swim — generated candidate; body proportions require native-source review',font=font,fill='white')
animated=[]
for f,im in enumerate(frames):
    cut=im.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0))
    large=cut.resize((W*3,H*3),Image.Resampling.NEAREST)
    x,y=(f%2)*W*3,50+(f//2)*H*3
    review.paste(large,(x,y),large);draw.text((x+10,y),PHASES[f],font=font,fill='white')
    panel=Image.new('RGB',(W*3,H*3+35),(42,45,49));panel.paste(large,(0,34),large);ImageDraw.Draw(panel).text((10,8),PHASES[f],font=font,fill='white');animated.append(panel)
review.save(OUT/'contact-review.png')
native_idle=Image.open(ROOT.parents[1]/'standing/source/native-idle/381-0.png').convert('RGBA')
native_profile=Image.open(ROOT.parents[1]/'standing/source/381-2-0.png').convert('RGBA')
comparison=Image.new('RGB',(224*3*2,220*2*2+60),(42,45,49));cd=ImageDraw.Draw(comparison)
cd.text((10,8),'Native381 originals + swim candidate | all pixels shown at same2x | source files unchanged',font=font,fill='white')
panels=[('Native idle73x165',native_idle,(75,32)),('Native left-profile67x110',native_profile,(78,32))]+[(PHASES[f],frames[f],(0,32)) for f in range(4)]
for n,(label,im,pos) in enumerate(panels):
    cell=Image.new('RGBA',(224,220));cell.alpha_composite(im,pos)
    cut=cell.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0))
    large=cut.resize((448,440),Image.Resampling.NEAREST)
    x,y=(n%3)*448,50+(n//3)*440
    comparison.paste(large,(x,y),large);cd.text((x+10,y+8),label,font=font,fill='white')
comparison.save(OUT/'native-comparison.png')
animated[0].save(OUT/'cycle-preview.gif',save_all=True,append_images=animated[1:],duration=[220,140,140,160],loop=0,disposal=2)
assert len({im.tobytes() for im in frames})==4
for f,im in enumerate(frames):assert im.tobytes()==atlas.crop((f*W,0,(f+1)*W,H)).tobytes()
manifest={'status':'generated-profile-swim-review-candidate','appearance':381,'state':'swimming','bearing':6,'bearingName':'right-profile','frameCount':4,'canvas':[W,H],'generatedSource':PATH.name,'generatedSha256':hashlib.sha256(PATH.read_bytes()).hexdigest(),'scaleBasis':'single shared21px head-width target; no phase-specific fitting','frames':records,'technicalChecks':{'rgba':True,'distinctPhases':4,'noAlpha128Clipping':True,'atlasExact':True},'visualReview':'pending','runtimeIntegrated':False,'userApproved':False,'nativeSourceFinding':'MobilePersonBillboard supports onlyIdle/Move; no native NPC-swim waterline renderer found. Fullbody art has no waterline baked in.','knownLimits':['Generated body proportions are more compact than native standing source; not accepted as exact native scale.','Full swimming physics/contact/waterline integration is outside this asset pilot.','Only right profile packed; other bearings remain missing.','Imagegen source grid was irregular; crops are recorded explicitly.']}
manifest['visualReview']='Fourpackedframes and unchanged native references inspected at the same2x scale. Stroke phases are readable; torso and legs are too short relative to head. Proportions not accepted; no further bearings produced before parent review.'
manifest['visuallyReviewed']=True
manifest['pixelCanvas']=manifest['canvas']
manifest['worldScaleContract']='../native-scale-contract.json'
manifest['intendedActorWorldHeightBeforeGlobalScale']=79
manifest['worldScaleApplied']=False
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'output':str(OUT),'technicalChecks':manifest['technicalChecks'],'frames':records},indent=2))
