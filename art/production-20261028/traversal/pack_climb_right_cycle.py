"""Pack independently authored climbing keyframes with shared body and contact anchors."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib,statistics
ROOT=Path(__file__).resolve().parent
OUT=ROOT/'381-climb-right-cycle-v1'
OUT.mkdir(exist_ok=True)
PHASES=['near-reach','near-pull','far-reach','far-pull']
HEAD_WIDTH=[200,199,211,220]
HEAD_TOP=[170,138,175,151]
HAND_X=[810,780,820,840]
W,H=160,224
frames=[];records=[]
for f,phase in enumerate(PHASES):
    path=ROOT/f'381-climb-right-{phase}-generated-v1.png'
    raw=Image.open(path).convert('RGBA')
    scale=21/HEAD_WIDTH[f]
    reduced=raw.resize((round(raw.width*scale),round(raw.height*scale)),Image.Resampling.NEAREST)
    x,y=round(128-HAND_X[f]*scale),round(44-HEAD_TOP[f]*scale)
    target=Image.new('RGBA',(W,H));target.alpha_composite(reduced,(x,y))
    bbox=target.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<W and bbox[3]<H,(f,bbox)
    filename=f'phase-{f}-{phase}.png';target.save(OUT/filename);frames.append(target)
    records.append({'phase':f,'name':phase,'file':filename,'sha256':hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),'generatedSource':path.name,'generatedSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'scale':scale,'scaleBasis':'21px head-width target from source-scaled directional identity, not per-pose height fitting','placement':[x,y],'headTopTarget':44,'approximateHandPlaneX':128,'alpha128Bounds':bbox,'medianCutoutAlpha':statistics.median(a for a in target.getchannel('A').getdata() if a>=128)})
atlas=Image.new('RGBA',(W*4,H))
for f,im in enumerate(frames):atlas.paste(im,(f*W,0))
atlas.save(OUT/'atlas.png')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
review=Image.new('RGB',(W*4*3,H*3+55),(42,45,49));draw=ImageDraw.Draw(review)
draw.text((10,8),'381 right-profile climb — source-conditioned keyframes, contact placement still requires review',font=font,fill='white')
animated=[]
for f,im in enumerate(frames):
    cut=im.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0))
    large=cut.resize((W*3,H*3),Image.Resampling.NEAREST)
    review.paste(large,(f*W*3,50),large);draw.text((f*W*3+10,30),PHASES[f],font=font,fill='white')
    draw.line((f*W*3+128*3,52,f*W*3+128*3,H*3+50),fill=(80,80,80))
    panel=Image.new('RGB',(W*3,H*3+40),(42,45,49));panel.paste(large,(0,38),large);ImageDraw.Draw(panel).text((12,10),PHASES[f],font=font,fill='white');animated.append(panel)
review.save(OUT/'contact-review.png')
animated[0].save(OUT/'cycle-preview.gif',save_all=True,append_images=animated[1:],duration=[180,140,180,140],loop=0,disposal=2)
assert len({im.tobytes() for im in frames})==4
for f,im in enumerate(frames):assert im.tobytes()==atlas.crop((f*W,0,(f+1)*W,H)).tobytes()
manifest={'status':'generated-profile-climb-review-candidate','appearance':381,'state':'climbing','bearing':6,'bearingName':'right-profile','frameCount':4,'canvas':[W,H],'frames':records,'technicalChecks':{'rgba':True,'distinctPhases':4,'noAlpha128Clipping':True,'atlasExact':True},'visualReview':'pending','runtimeIntegrated':False,'userApproved':False,'knownLimits':['Hands follow an approximate common plane; real wall-contact registration not validated.','Leg alternation is difficult to prove from strict profile because near/far legs overlap; compare rear pilot before acceptance.','Generated complexion and cloth pixels differ from native source; exact palette not claimed.','Only right profile is packed; raw rear four-phase pilot is separate.']}
manifest['visualReview']='Fourpackedframes inspected. Near/far arm reach differs and pull poses exist. Leg-side alternation is not proven in profile; far-pull rear elbow and exact wall contacts remain unresolved. Do not scale this candidate yet.'
manifest['visuallyReviewed']=True
manifest['pixelCanvas']=manifest['canvas']
manifest['worldScaleContract']='../native-scale-contract.json'
manifest['intendedActorWorldHeightBeforeGlobalScale']=79
manifest['worldScaleApplied']=False
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'output':str(OUT),'technicalChecks':manifest['technicalChecks'],'records':records},indent=2))
