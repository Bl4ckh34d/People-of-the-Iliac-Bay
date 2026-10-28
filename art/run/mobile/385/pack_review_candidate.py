from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import json, hashlib, statistics
P=Path(__file__).parent
versions=[1,1,2,2,2,4,2,1]
names=['Front','Front-left','Left profile','Rear-left','Back','Rear-right','Right profile','Front-right']
W,H=192,224
BASE=194
frames=[]
records=[]
out=P/'review-candidate'
(out/'frames').mkdir(parents=True,exist_ok=True)
atlas=Image.new('RGBA',(W*4,H*8))
for v,ver in enumerate(versions):
    path=P/f'generated-abab-view-{v}-v{ver}.png'
    im=Image.open(path).convert('RGBA')
    cells=[]
    bounds=[]
    for f in range(4):
        rect=(round(f*im.width/4),0,round((f+1)*im.width/4),im.height)
        cell=im.crop(rect)
        box=cell.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
        cells.append((cell,rect,box))
        bounds.append(box)
    scale=163/statistics.mean(b[3]-b[1] for b in bounds[:2])
    source_ground=statistics.mean(b[3] for b in bounds[:2])
    for f,(cell,rect,box) in enumerate(cells):
        mask=cell.getchannel('A').point(lambda a:255 if a>=128 else 0)
        head=mask.crop((0,box[1]+4,cell.width,min(box[1]+65,box[3]))).getbbox()
        anchor=(head[0]+head[2])/2 if head else cell.width/2
        size=(round(cell.width*scale),round(cell.height*scale))
        reduced=cell.resize(size,Image.Resampling.NEAREST)
        x=round(W/2-anchor*scale)
        y=round(BASE-source_ground*scale)
        # Contact frames share a baseline. Flight frames retain authored clearance.
        if f<2: y=round(BASE-box[3]*scale)
        target=Image.new('RGBA',(W,H))
        target.alpha_composite(reduced,(x,y))
        visible=target.getchannel('A').getbbox()
        assert visible and visible[0]>0 and visible[1]>0 and visible[2]<W and visible[3]<H,(v,f,visible)
        file=f'frames/view-{v}-frame-{f}.png'
        target.save(out/file)
        atlas.paste(target,(f*W,v*H))
        frames.append(target)
        records.append(dict(bearing=v,frame=f,phase='A' if f%2==0 else 'B',rightArm='forward' if f%2==0 else 'back',leftLeg='forward' if f%2==0 else 'back',intendedSupport='contact' if f<2 else 'low-flight',source=path.name,sourceCrop=list(rect),scale=scale,placement=[x,y],nativeCanvas=[W,H],alphaBounds=list(visible),file=file))
atlas.save(out/'atlas-8x4.png')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',14)
small=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',12)
review=Image.new('RGB',(W*4+130,H*8+50),(27,30,33))
d=ImageDraw.Draw(review)
d.text((12,8),'385 RUN PILOT — REVIEW CANDIDATE, NOT APPROVED',font=font,fill='white')
for f in range(4): d.text((130+f*W+15,30),f'{f+1}  '+('A' if f%2==0 else 'B')+(' contact' if f<2 else ' low flight'),font=small,fill=(220,220,220))
for v in range(8):
    d.text((8,50+v*H+70),f'{v}  {names[v]}',font=small,fill='white')
    for f in range(4):
        x,y=130+f*W,50+v*H
        d.rectangle((x,y,x+W-1,y+H-1),outline=(60,63,66))
        d.line((x+5,y+BASE,x+W-5,y+BASE),fill=(75,78,82))
        review.paste(frames[v*4+f],(x,y),frames[v*4+f])
review.save(out/'full-sheet.png')
review.resize((review.width*2,review.height*2),Image.Resampling.NEAREST).save(out/'full-sheet-2x.png')
animated=[]
for f in range(4):
    a=Image.new('RGB',(W*4,H*2+65),(27,30,33));d=ImageDraw.Draw(a)
    d.text((10,6),f'385 review candidate | frame {f+1}/4 | '+('A' if f%2==0 else 'B')+' | 8 fps',font=font,fill='white')
    for v in range(8):
        x,y=(v%4)*W,35+(v//4)*(H+15)
        d.text((x+5,y),f'{v} {names[v]}',font=small,fill='white')
        a.paste(frames[v*4+f],(x,y+15),frames[v*4+f])
    animated.append(a.resize((a.width*2,a.height*2),Image.Resampling.NEAREST))
animated[0].save(out/'animated-preview.gif',save_all=True,append_images=animated[1:],duration=[120,130,120,130],loop=0,optimize=False,disposal=2)
for f,a in enumerate(animated): a.save(out/f'preview-phase-{f}.png')
manifest=dict(appearance=385,status='review-candidate-not-approved',nativeIdleSize=[72,163],bearings=names,frameCount=4,fps=8,frameDurationMs=125,loop=[0,1,2,3],atlas='atlas-8x4.png',layout='8 rows bearing, 4 columns frame',canvas=[W,H],groundY=BASE,generation='built-in imagegen; independent bearings, no generated sprite mirroring',packing='nearest-neighbor scaling, uniform per bearing; alpha preserved; contact baseline aligned; flight not foot-normalized',phaseContract='A-B-A-B, opposing anatomical arm/leg',runtimeIntegrated=False,frames=records,knownLimitations=['Four poses still emphasize stride extremes; passing/recovery transitions are weak.','Side and rear-quarter leg depth/occlusion needs user review; no final anatomical approval claimed.','Authored flight clearance varies between bearings; no full playback acceptance claimed.','Generated shading/detail remains a review candidate rather than a proven vanilla match.'])
(out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
checks=[]
for r in records:
    a=Image.open(out/r['file']).convert('RGBA')
    b=atlas.crop((r['frame']*W,r['bearing']*H,(r['frame']+1)*W,(r['bearing']+1)*H))
    assert a.tobytes()==b.tobytes()
    assert a.getchannel('A').getextrema()[0]==0
    checks.append(dict(bearing=r['bearing'],frame=r['frame'],rgba=True,transparent=True,atlasExact=True))
(out/'verification.json').write_text(json.dumps(dict(frames=32,bearings=8,framesPerBearing=4,mechanicalChecks='passed',visualStatus='review-candidate-with-known-limitations',checks=checks),indent=2)+'\n')
print(json.dumps(dict(output=str(out),sheet=str(out/'full-sheet.png'),animation=str(out/'animated-preview.gif'),frames=32)))

