"""Point-sample anatomically regenerated glide and compare without hiding source scale."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib,statistics
ROOT=Path(__file__).resolve().parent
OUT=ROOT/'381-swim-glide-repair-v1';OUT.mkdir(exist_ok=True)
SOURCE=ROOT/'381-swim-right-glide-repair-generated-v1.png'
raw=Image.open(SOURCE).convert('RGBA')
scale=21/206
reduced=raw.resize((round(raw.width*scale),round(raw.height*scale)),Image.Resampling.NEAREST)
x,y=round(154-1521*scale),round(28-149*scale)
frame=Image.new('RGBA',(256,128));frame.alpha_composite(reduced,(x,y));frame.save(OUT/'glide-native.png')
bbox=frame.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<256 and bbox[3]<128
idle=Image.open(ROOT.parents[1]/'standing/source/native-idle/381-0.png').convert('RGBA')
walk=Image.open(ROOT.parents[1]/'standing/source/381-2-0.png').convert('RGBA')
old=Image.open(ROOT/'381-swim-right-cycle-v1/phase-0-glide.png').convert('RGBA')
panels=[('Native idle165px; scale-134; world79',idle,(80,30)),('Native walk110px; scale-70; world80',walk,(80,30)),('Old compact glide; head21px',old,(0,45)),('Redrawn longer glide; head21px',frame,(0,45))]
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
review=Image.new('RGB',(256*4*2,224*2+65),(42,45,49));d=ImageDraw.Draw(review)
d.text((10,8),'381 swim anatomy repair | all panels2x pixel zoom | native world sizes use record scale; generated runtime scale not integrated',font=font,fill='white')
for n,(label,im,pos) in enumerate(panels):
    cell=Image.new('RGBA',(256,224));cell.alpha_composite(im,pos)
    cut=cell.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0));large=cut.resize((512,448),Image.Resampling.NEAREST)
    review.paste(large,(n*512,60),large);d.text((n*512+10,35),label,font=font,fill='white')
review.save(OUT/'native-before-after.png')
manifest={'status':'single-anatomical-repair-keyframe-review-candidate','appearance':381,'state':'swimming','bearing':6,'phase':'glide','generatedSource':SOURCE.name,'generatedSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'file':'glide-native.png','sha256':hashlib.sha256((OUT/'glide-native.png').read_bytes()).hexdigest(),'pixelCanvas':[256,128],'pixelScale':scale,'pixelScaleBasis':'one206px head cross-section becomes21px; body was regenerated longer, not stretched','alpha128Bounds':bbox,'medianCutoutAlpha':statistics.median(a for a in frame.getchannel('A').getdata() if a>=128),'nativeWorldScaleEvidence':{'formula':'size + int(size*scale/256), before MeshReader.GlobalScale','nativeWalking':{'pixelHeight':110,'scaleY':-70,'computedHeight':80},'nativeIdle':{'pixelHeight':165,'scaleY':-134,'computedHeight':79},'source':'MobilePersonBillboard.cs CacheRecordSizesAndFrames; native-idle.csv and parent native walking record inspection'},'intendedActorWorldHeightBeforeGlobalScale':79,'worldScaleApplied':False,'runtimeIntegrated':False,'userApproved':False,'knownLimits':['Only glide repaired; pull/recovery/kick still use compact proportions until this repair is accepted.','Intended actor world height is not the sprite bounding-box height or canvas height. Runtime integration must preserve record-adjusted native world height.','Generated exact palette and anatomy remain under visual review.']}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps(manifest,indent=2))
