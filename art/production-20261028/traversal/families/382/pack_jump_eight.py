"""Five authored jump directions; three explicitly documented native-rule mirrors."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont,ImageOps
import json,hashlib,statistics
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'jump-eight-views-v1';OUT.mkdir(exist_ok=True)
W,H=160,208;PHASES=['crouch','takeoff','apex','landing'];frames={};records=[]
head_x={1:[225,745,1260,1780],2:[217,805,1300,1827],3:[237,720,1260,1780],4:[263,733,1314,1800]}
scales={1:21/95,2:21/100,3:21/95,4:21/95}
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
for v in range(5):
    for p,name in enumerate(PHASES):
        if v==0:
            source=ROOT/'jump-front-v1'/f'phase-{p}-{name}.png';im=Image.open(source).convert('RGBA');meta={'source':str(source.relative_to(ROOT)),'sourceSha256':sha(source),'pixelScale':21/80,'sourceAlreadyPacked':True}
        else:
            version=2 if v==2 and p==1 else 1
            source=ROOT/f'jump-view-{v}-generated-v{version}.png';raw=Image.open(source).convert('RGBA');cw=raw.width//4
            cuts=[0,512,1024,1568,2048] if v==4 else [n*cw for n in range(5)]
            rect=(cuts[p],0,cuts[p+1],raw.height);crop=raw.crop(rect);cw=crop.width;bb=crop.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
            assert bb and bb[0]>0 and bb[1]>0 and bb[2]<cw and bb[3]<raw.height,(v,p,bb)
            scale=scales[v];small=crop.resize((round(cw*scale),round(raw.height*scale)),Image.Resampling.NEAREST)
            x=round(W/2-(head_x[v][p]-rect[0])*scale)
            y=round((18-bb[1]*scale) if p==2 else ((184 if p==1 else 190)-bb[3]*scale))
            im=Image.new('RGBA',(W,H));im.alpha_composite(small,(x,y));meta={'source':source.name,'sourceSha256':sha(source),'sourceCrop':rect,'pixelScale':scale,'placement':[x,y]}
        frames[v,p]=im;meta.update({'bearing':v,'phase':p,'name':name,'authored':True});records.append(meta)
for v,original in [(5,3),(6,2),(7,1)]:
    for p,name in enumerate(PHASES):
        frames[v,p]=ImageOps.mirror(frames[original,p]);records.append({'bearing':v,'phase':p,'name':name,'authored':False,'derivedFromBearing':original,'operation':'exact horizontal mirror','phaseMapping':'same phase; both arms and legs act symmetrically','nativeRule':'MobilePersonBillboard native record order 0,1,2,3,4,mirror3,mirror2,mirror1'})
atlas=Image.new('RGBA',(W*4,H*8))
for r in records:
    v,p=r['bearing'],r['phase'];im=frames[v,p];bb=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox();assert bb and bb[0]>0 and bb[1]>0 and bb[2]<W and bb[3]<H,(v,p,bb)
    filename=f'view-{v}-phase-{p}-{PHASES[p]}.png';im.save(OUT/filename);r.update({'file':filename,'sha256':sha(OUT/filename),'alpha128Bounds':bb,'alphaHistogram':im.getchannel('A').histogram(),'medianCutoutAlpha':statistics.median(a for a in im.getchannel('A').getdata() if a>=128)})
    atlas.paste(im,(p*W,v*H))
atlas.save(OUT/'atlas.png')
for v in range(8):
    assert len({frames[v,p].tobytes() for p in range(4)})==4
    for p in range(4):assert atlas.crop((p*W,v*H,(p+1)*W,(v+1)*H)).tobytes()==frames[v,p].tobytes()
for v,o in [(5,3),(6,2),(7,1)]:
    for p in range(4):assert frames[v,p].tobytes()==ImageOps.mirror(frames[o,p]).tobytes()
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
def cut(im):
    out=im.copy();out.putalpha(out.getchannel('A').point(lambda a:255 if a>=128 else 0));return out
for start in [0,4]:
    page=Image.new('RGB',(W*4+160,(H+30)*4+35),(42,45,49));d=ImageDraw.Draw(page)
    d.text((8,8),'382 Jump | native pixel size | alpha cutoff128 | worldscale not applied',font=font,fill='white')
    for v in range(start,start+4):
        row=v-start;y=35+row*(H+30);d.text((8,y+35),f'View {v}',font=font,fill='white');d.text((8,y+58),'authored' if v<5 else f'mirror {8-v}',font=font,fill='white')
        for p,name in enumerate(PHASES):
            x=160+p*W;d.text((x+8,y+4),name,font=font,fill='white');im=cut(frames[v,p]);page.paste(im,(x,y+28),im)
    page.save(OUT/f'review-bearings-{start}-{start+3}.png')
animated=[]
for p in range(4):
    panel=Image.new('RGB',(W*4,H*2+55),(42,45,49));d=ImageDraw.Draw(panel);d.text((8,8),f'382 jump: {PHASES[p]} | 5 authored + 3 mirrors',font=font,fill='white')
    for v in range(8):
        im=cut(frames[v,p]);panel.paste(im,((v%4)*W,45+(v//4)*H),im)
    animated.append(panel)
animated[0].save(OUT/'cycle-eight-views.gif',save_all=True,append_images=animated[1:],duration=[160,90,180,140],loop=0,disposal=2)
m={'identity':'vanilla:mobile:382:None','state':'jumping','status':'eight-bearing-static-candidate','frameCount':32,'authoredFrames':20,'mirrorDerivedFrames':12,'pixelCanvas':[W,H],'frames':records,'atlasSha256':sha(OUT/'atlas.png'),'sourceManifest':'../source-manifest.json','technicalChecks':{'rgba':True,'fourDistinctPhasesPerBearing':True,'sourceCropNoAlpha128Clipping':True,'noAlpha128Clipping':True,'atlasExact':True,'mirrorsExact':True,'samePixelScaleWithinBearing':True},'visuallyReviewed':False,'integrated':False,'userApproved':False,'nativeFrontWalkWorldHeightBeforeGlobalScale':81,'generatedWorldScaleApplied':False,'knownLimits':['Artwork keeps generated muscle/face detail, not exact native palette.','Head/body ratio varies by bearing; one shared head-reference scale per bearing, never per-pose height fit.','Pouch view-relative location follows native examples; continuity not runtime validated.','Mirror rule applies only to this symmetric jump, not automatically to climb or swim.','Timing and jump offsets are preview staging, not runtime acceptance.','All climb/swim frames still missing for382.']}
m['visuallyReviewed']=True
m['visualReview']='Both native-scale contact pages inspected: four poses and five authored cameras distinct; three exact mirrors preserve symmetric limb timing. Front0 body visibly larger than other bearings; generated head/body ratios and native palette remain unaccepted.'
(OUT/'manifest.json').write_text(json.dumps(m,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'output':str(OUT),'atlasSha256':m['atlasSha256'],'checks':m['technicalChecks']},indent=2))
