"""Pack the anatomically regenerated four-phase swim using one fixed pixel scale."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib,statistics
ROOT=Path(__file__).resolve().parent
OUT=ROOT/'381-swim-right-cycle-v2';OUT.mkdir(exist_ok=True)
PHASES=['glide','pull','recovery','kick']
HEAD_CENTER=[1521.5,1538.5,1636.5,1524.5]
HEAD_TOP=[149,95,111,147]
HEAD_WIDTH=[205,215,217,205]
SCALE=21/206
W,H=256,128
frames=[];records=[]
for f,phase in enumerate(PHASES):
    path=ROOT/f'381-swim-right-{phase}-repair-generated-v1.png'
    raw=Image.open(path).convert('RGBA')
    reduced=raw.resize((round(raw.width*SCALE),round(raw.height*SCALE)),Image.Resampling.NEAREST)
    x,y=round(154-HEAD_CENTER[f]*SCALE),round(28-HEAD_TOP[f]*SCALE)
    target=Image.new('RGBA',(W,H));target.alpha_composite(reduced,(x,y))
    bbox=target.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<W and bbox[3]<H
    filename=f'phase-{f}-{phase}.png';target.save(OUT/filename);frames.append(target)
    records.append({'phase':f,'name':phase,'file':filename,'sha256':hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),'generatedSource':path.name,'generatedSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'generatedSize':raw.size,'pixelScale':SCALE,'headWidthAtAuditCrossSection':HEAD_WIDTH[f],'expectedPackedHeadWidth':HEAD_WIDTH[f]*SCALE,'placement':[x,y],'alpha128Bounds':bbox,'medianCutoutAlpha':statistics.median(a for a in target.getchannel('A').getdata() if a>=128)})
atlas=Image.new('RGBA',(W*4,H))
for f,im in enumerate(frames):atlas.paste(im,(f*W,0))
atlas.save(OUT/'atlas.png')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
review=Image.new('RGB',(W*2*3,H*2*3+70),(42,45,49));draw=ImageDraw.Draw(review)
draw.text((10,8),'381 repaired right-profile swim | one fixed21/206 pixel scale for every phase | not integrated',font=font,fill='white')
animated=[]
for f,im in enumerate(frames):
    cut=im.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0));large=cut.resize((W*3,H*3),Image.Resampling.NEAREST)
    x,y=(f%2)*W*3,50+(f//2)*H*3;review.paste(large,(x,y),large);draw.text((x+10,y),PHASES[f],font=font,fill='white')
    panel=Image.new('RGB',(W*3,H*3+35),(42,45,49));panel.paste(large,(0,34),large);ImageDraw.Draw(panel).text((10,8),PHASES[f],font=font,fill='white');animated.append(panel)
review.save(OUT/'contact-review.png')
animated[0].save(OUT/'cycle-preview.gif',save_all=True,append_images=animated[1:],duration=[220,140,140,160],loop=0,disposal=2)
idle=Image.open(ROOT.parents[1]/'standing/source/native-idle/381-0.png').convert('RGBA')
walk=Image.open(ROOT.parents[1]/'standing/source/381-2-0.png').convert('RGBA')
comparison=Image.new('RGB',(W*3*2,224*2*2+60),(42,45,49));cd=ImageDraw.Draw(comparison)
cd.text((10,8),'Native originals + regenerated swim | same2x pixel zoom | record-adjusted native worldheight79/80',font=font,fill='white')
panels=[('Native idle165px; scale-134; world79',idle,(80,30)),('Native walk110px; scale-70; world80',walk,(80,30))]+[(PHASES[f],frames[f],(0,44)) for f in range(4)]
for n,(label,im,pos) in enumerate(panels):
    cell=Image.new('RGBA',(W,224));cell.alpha_composite(im,pos)
    cut=cell.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0));large=cut.resize((W*2,448),Image.Resampling.NEAREST)
    x,y=(n%3)*W*2,50+(n//3)*448;comparison.paste(large,(x,y),large);cd.text((x+10,y+8),label,font=font,fill='white')
comparison.save(OUT/'native-comparison.png')
assert len({im.tobytes() for im in frames})==4
for f,im in enumerate(frames):assert im.tobytes()==atlas.crop((f*W,0,(f+1)*W,H)).tobytes()
manifest={'status':'anatomically-repaired-profile-swim-review-candidate','appearance':381,'state':'swimming','bearing':6,'bearingName':'right-profile','frameCount':4,'pixelCanvas':[W,H],'pixelScale':SCALE,'pixelScaleBasis':'One shared factor21/206 derived from corrected glide head width. No per-pose size fitting or canvas stretching.','frames':records,'atlasSha256':hashlib.sha256((OUT/'atlas.png').read_bytes()).hexdigest(),'technicalChecks':{'rgba':True,'distinctPhases':4,'noAlpha128Clipping':True,'atlasExact':True,'identicalPixelScaleAllPhases':True},'visualReview':'pending-parent-four-phase-review','worldScaleContract':'../native-scale-contract.json','intendedActorWorldHeightBeforeGlobalScale':79,'worldScaleApplied':False,'runtimeIntegrated':False,'userApproved':False,'masterReview':'Parent inspected corrected glide native-before-after and accepted it as a production reference, not final user approval.','knownLimits':['Only right-profile4phase is generated on corrected anatomy; other bearings remain missing.','Source-conditioned new phases vary head width by about6percent; shared scale retains that difference rather than distorting each pose.','No native NPC swim waterline renderer exists in reviewed sources; no water is baked into art.','Exact palette, full temporal motion and runtime world mapping remain unaccepted.']}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'output':str(OUT),'technicalChecks':manifest['technicalChecks'],'atlasSha256':manifest['atlasSha256']},indent=2))
