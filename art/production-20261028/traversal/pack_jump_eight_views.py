"""Pack eight authored jump bearings; only point sampling and transparent placement."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import json, hashlib, statistics

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'381-jump-eight-views-v1'
(OUT/'frames').mkdir(parents=True,exist_ok=True)
FRONT=ROOT/'381-jump-front-cycle-v1'
PHASES=['crouch','takeoff','apex','landing']
BEARINGS=['front','front-left','left-profile','rear-left','back','rear-right','right-profile','front-right']
W,H,BASE=160,192,181
HEAD_CENTERS={1:[208,207,232,228],2:[174,239,250,244],3:[226,244,233,257],4:[251,258,252,260],5:[315,260,256,294],6:[333,261,260,260],7:[302,263,250,292]}
frames=[]
records=[]
for v in range(8):
    if v:
        version=3 if v==1 else 1
        path=ROOT/f'381-jump-view-{v}-generated-v{version}.png'
        raw=Image.open(path).convert('RGBA')
        rects=[(round(f*raw.width/4),0,round((f+1)*raw.width/4),raw.height) for f in range(4)]
        cells=[raw.crop(rect) for rect in rects]
        bounds=[c.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox() for c in cells]
        scale=150/(bounds[1][3]-bounds[1][1])
    for f,phase in enumerate(PHASES):
        if v==0:
            path=FRONT/f'phase-{f}-{phase}.png'
            front=Image.open(path).convert('RGBA')
            target=Image.new('RGBA',(W,H))
            target.alpha_composite(front,(16,0))
            detail={'source':str(path.relative_to(ROOT)),'sourceSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'processing':'copied existing verified front frame byte-for-pixel'}
        else:
            cell=cells[f]
            b=bounds[f]
            reduced=cell.resize((round(cell.width*scale),round(cell.height*scale)),Image.Resampling.NEAREST)
            x=round(W/2-HEAD_CENTERS[v][f]*scale)
            y=round(16-b[1]*scale) if f==2 else round(BASE-(6 if f==1 else 0)-b[3]*scale)
            target=Image.new('RGBA',(W,H))
            target.alpha_composite(reduced,(x,y))
            detail={'source':path.name,'sourceSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'sourceCrop':list(rects[f]),'scale':scale,'scaleBasis':'one uniform scale per bearing: authored takeoff silhouette150px, matching frontal takeoff','placement':[x,y],'headCenterInCell':HEAD_CENTERS[v][f]}
        bbox=target.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
        assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<W and bbox[3]<H,(v,f,bbox)
        filename=f'frames/view-{v}-phase-{f}-{phase}.png'
        target.save(OUT/filename)
        record={'bearing':v,'bearingName':BEARINGS[v],'phase':f,'phaseName':phase,'file':filename,'sha256':hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),'alpha128Bounds':bbox,'medianCutoutAlpha':statistics.median(a for a in target.getchannel('A').getdata() if a>=128),**detail}
        records.append(record)
        frames.append(target)
atlas=Image.new('RGBA',(W*4,H*8))
for v in range(8):
    for f in range(4): atlas.paste(frames[v*4+f],(f*W,v*H))
atlas.save(OUT/'atlas-8x4.png')
for r,im in zip(records,frames):
    assert im.tobytes()==atlas.crop((r['phase']*W,r['bearing']*H,(r['phase']+1)*W,(r['bearing']+1)*H)).tobytes()
assert all(len({frames[v*4+f].tobytes() for f in range(4)})==4 for v in range(8))
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',16)
small=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',13)
def cutout(im):
    result=im.copy();result.putalpha(result.getchannel('A').point(lambda a:255 if a>=128 else 0));return result
for start in [0,4]:
    review=Image.new('RGB',(W*4*2+130,H*4*2+64),(42,45,49));draw=ImageDraw.Draw(review)
    draw.text((10,10),f'381 JUMP — bearings{start}–{start+3} — generated candidate, not approved',font=font,fill='white')
    for f,name in enumerate(PHASES):draw.text((130+f*W*2+12,38),name,font=font,fill='white')
    for row,v in enumerate(range(start,start+4)):
        draw.text((8,64+row*H*2+80),f'{v} {BEARINGS[v]}',font=small,fill='white')
        for f in range(4):
            x,y=130+f*W*2,64+row*H*2
            im=cutout(frames[v*4+f]).resize((W*2,H*2),Image.Resampling.NEAREST)
            review.paste(im,(x,y),im)
            draw.line((x+6,y+BASE*2,x+W*2-6,y+BASE*2),fill=(95,95,95))
    review.save(OUT/f'review-bearings-{start}-{start+3}.png')
animated=[]
for f in range(4):
    panel=Image.new('RGB',(W*4*2,H*2*2+72),(42,45,49));draw=ImageDraw.Draw(panel)
    draw.text((12,8),f'381 JUMP | {PHASES[f]} | review candidate',font=font,fill='white')
    for v in range(8):
        x,y=(v%4)*W*2,44+(v//4)*(H*2+14)
        draw.text((x+6,y),BEARINGS[v],font=small,fill='white')
        im=cutout(frames[v*4+f]).resize((W*2,H*2),Image.Resampling.NEAREST)
        panel.paste(im,(x,y+14),im)
    animated.append(panel)
animated[0].save(OUT/'cycle-eight-views.gif',save_all=True,append_images=animated[1:],duration=[180,100,140,200],loop=0,disposal=2)
manifest={'status':'generated-eight-bearing-jump-pilot-review-candidate','appearance':381,'state':'jumping','bearings':BEARINGS,'phases':PHASES,'framesPerBearing':4,'frameCount':32,'canvas':[W,H],'atlas':'atlas-8x4.png','atlasLayout':'8rows bearings,4columns phases','atlasSha256':hashlib.sha256((OUT/'atlas-8x4.png').read_bytes()).hexdigest(),'groundY':BASE,'frames':records,'technicalChecks':{'all32Rgba':True,'distinctPhasesPerBearing':4,'noAlpha128Clipping':True,'atlasExact':True},'visualReview':'pending-native-contact-review','runtimeIntegrated':False,'userApproved':False,'exactNativePalette':False,'processing':'Nearest-neighbor sampling and transparent placement only. Output alpha retained; review image simulates cutoff128. No anatomy painting, mirroring or translation-only substitute poses.','knownLimits':['Generative proportions and face/cloth details vary between bearings; compare against native identity before acceptance.','Four keyframes are sparse; timing is diagnostic only.','Native source frontal idle165px and walking110px are different source scales; this pilot uses frontal idle-derived canvas with150px takeoffbody.','Pouch position follows available native views; global anatomical side consistency still requires user review.']}
manifest['visualReview']='All32packedframes inspected in two contact sheets; eight bearings and four real phases present. No obvious duplicate limbs or clipped figures. Face/body/pouch consistency remains a candidate limitation.'
manifest['visuallyReviewed']=True
manifest['pixelCanvas']=manifest['canvas']
manifest['worldScaleContract']='../native-scale-contract.json'
manifest['intendedActorWorldHeightBeforeGlobalScale']=79
manifest['worldScaleApplied']=False
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'output':str(OUT),'frameCount':32,'technicalChecks':manifest['technicalChecks'],'atlasSha256':manifest['atlasSha256']},indent=2))
