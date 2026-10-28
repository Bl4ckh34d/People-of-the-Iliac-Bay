"""Pack corrected climbing contact poses with explicit contact-edge diagnostics."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib,statistics
ROOT=Path(__file__).resolve().parent
OUT=ROOT/'381-climb-right-cycle-v2';OUT.mkdir(exist_ok=True)
SOURCES=['381-climb-right-contact-master-generated-v1.png','381-climb-right-near-pull-contact-generated-v1.png','381-climb-right-far-reach-contact-generated-v2.png','381-climb-right-far-pull-contact-generated-v2.png']
PHASES=['near-reach','near-pull','far-reach','far-pull']
HEAD_TOP=[170,130,170,155]
CONTACT_RECTS=[
[(760,20,1024,220),(740,430,1024,580),(680,800,1024,1000),(630,1250,1024,1450)],
[(800,0,1024,200),(760,220,1024,400),(680,850,1024,1030),(620,1250,1024,1420)],
[(760,20,1024,220),(760,400,1024,600),(740,800,1024,1000),(680,1250,1024,1420)],
[(800,0,1024,210),(800,200,1024,380),(780,880,1024,1040),(720,1270,1024,1430)]]
SCALE=21/200;W,H=160,224
frames=[];records=[]
for phase,source in enumerate(SOURCES):
    path=ROOT/source;raw=Image.open(path).convert('RGBA')
    alpha=raw.getchannel('A').point(lambda a:255 if a>=128 else 0)
    contacts=[]
    for rect in CONTACT_RECTS[phase]:
        bb=alpha.crop(rect).getbbox();assert bb
        contacts.append(rect[0]+bb[2]-1)
    plane=statistics.median(contacts)
    reduced=raw.resize((round(raw.width*SCALE),round(raw.height*SCALE)),Image.Resampling.NEAREST)
    x,y=round(128-plane*SCALE),round(44-HEAD_TOP[phase]*SCALE)
    im=Image.new('RGBA',(W,H));im.alpha_composite(reduced,(x,y));frames.append(im)
    bounds=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bounds and bounds[0]>0 and bounds[1]>0 and bounds[2]<W and bounds[3]<H
    filename=f'phase-{phase}-{PHASES[phase]}.png';im.save(OUT/filename)
    records.append({'phase':phase,'name':PHASES[phase],'file':filename,'sha256':hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),'generatedSource':source,'generatedSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'pixelScale':SCALE,'placement':[x,y],'contactAuditRects':CONTACT_RECTS[phase],'contactRightEdgeSourceX':contacts,'contactRightEdgePackedX':[x+c*SCALE for c in contacts],'contactHorizontalSpanPackedPixels':(max(contacts)-min(contacts))*SCALE,'alpha128Bounds':bounds,'medianCutoutAlpha':statistics.median(a for a in im.getchannel('A').getdata() if a>=128)})
atlas=Image.new('RGBA',(W*4,H))
for phase,im in enumerate(frames):atlas.paste(im,(phase*W,0))
atlas.save(OUT/'atlas.png')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
review=Image.new('RGB',(W*4*3,H*3+70),(42,45,49));draw=ImageDraw.Draw(review)
draw.text((10,8),'381 climb contact repair | fixed pixel scale all phases | line = median contact plane, not validated runtime wall',font=font,fill='white')
animated=[]
for phase,im in enumerate(frames):
    cut=im.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0));large=cut.resize((W*3,H*3),Image.Resampling.NEAREST)
    x=phase*W*3;review.paste(large,(x,64),large)
    draw.line((x+384,64,x+384,64+H*3),fill=(90,90,90))
    draw.text((x+10,32),f'{PHASES[phase]} | contact span {records[phase]["contactHorizontalSpanPackedPixels"]:.1f}px',font=font,fill='white')
    panel=Image.new('RGB',(W*3,H*3+35),(42,45,49));panel.paste(large,(0,35),large);ImageDraw.Draw(panel).text((10,8),PHASES[phase],font=font,fill='white');animated.append(panel)
review.save(OUT/'contact-review.png')
animated[0].save(OUT/'cycle-preview.gif',save_all=True,append_images=animated[1:],duration=[180,140,180,140],loop=0,disposal=2)
assert len({im.tobytes() for im in frames})==4
for phase,im in enumerate(frames):assert im.tobytes()==atlas.crop((phase*W,0,(phase+1)*W,H)).tobytes()
manifest={'status':'climb-contact-repair-candidate','appearance':381,'state':'climbing','bearing':6,'bearingName':'right-profile','frameCount':4,'pixelCanvas':[W,H],'pixelScale':SCALE,'scaleBasis':'One shared21/200 factor for all four regenerated poses; no individual pose height fitting.','frames':records,'atlasSha256':hashlib.sha256((OUT/'atlas.png').read_bytes()).hexdigest(),'technicalChecks':{'rgba':True,'distinctPhases':4,'noAlpha128Clipping':True,'atlasExact':True,'identicalPixelScale':True},'visuallyReviewed':False,'visualReview':'Pending contact review.','runtimeIntegrated':False,'userApproved':False,'worldScaleContract':'../native-scale-contract.json','intendedActorWorldHeightBeforeGlobalScale':79,'worldScaleApplied':False,'knownLimits':['Contact right-edge span is measured, not guaranteed physical palm/toe contact.','Strict profile leaves near/far leg identity partly ambiguous; no alternating leg pass claimed.','Face, cloth, skin and outline differ across independently regenerated phases.','Seven other climb bearings remain missing.']}
manifest['visuallyReviewed']=True
manifest['visualReview']='Contact sheet inspected at3x: feet point toward wall, near/far arm leads differ, no isolated elbow. Measured contact edge spans2.4–7.5 nativepixels remain; no exact contact or leg-identity acceptance.'
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'output':str(OUT),'contactSpanPixels':[r['contactHorizontalSpanPackedPixels'] for r in records],'atlasSha256':manifest['atlasSha256']},indent=2))
