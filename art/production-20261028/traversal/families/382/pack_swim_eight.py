"""Pack authored swim cameras; no per-pose height normalization or mirrors."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib,statistics
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'swim-eight-views-v1';OUT.mkdir(exist_ok=True);W,H=256,192
names=['glide','pull','recovery','kick'];sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();frames={};records=[]
heads={0:[(400,210),(1140,210),(402,620),(1144,630)],1:[(318,234),(1008,240),(275,629),(1000,658)],2:[(352,72),(1370,54),(302,397),(1351,394)],3:[(294,124),(980,115),(265,555),(983,580)],4:[(409,91),(1156,110),(454,584),(1148,593)],5:[(641,152),(1316,146),(521,539),(1311,538)],7:[(567,189),(1310,188),(493,614),(1255,635)]}
headwidth={0:140,1:135,2:124,3:110,4:110,5:110,7:130}
splits={0:(768,768,540),1:(900,715,520),2:(1200,980,365),3:(840,740,520),4:(768,700,550),5:(850,670,500),7:(830,650,500)}
for v in range(8):
    for p,name in enumerate(names):
        if v==6:
            source=ROOT/'swim-profile-v1'/f'phase-{p}-{name}.png';im=Image.open(source).convert('RGBA');meta={'source':str(source.relative_to(ROOT)),'sourceSha256':sha(source),'pixelScale':21/64,'sourceAlreadyPacked':True}
        else:
            version=2 if v==4 and p==0 else 1
            source=ROOT/f'swim-view-{v}-generated-v{version}.png';raw=Image.open(source).convert('RGBA');top,bottom,row=splits[v]
            rect=[(0,0,top,row),(top,0,raw.width,row),(0,row,bottom,raw.height),(bottom,row,raw.width,raw.height)][p];crop=raw.crop(rect)
            bb=crop.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox();assert bb and bb[0]>0 and bb[1]>0 and bb[2]<crop.width and bb[3]<crop.height,(v,p,bb,rect)
            scale=21/headwidth[v];head=heads[v][p];small=crop.resize((round(crop.width*scale),round(crop.height*scale)),Image.Resampling.NEAREST)
            anchor=75 if v in [1,2,3] else (180 if v in [5,7] else 128)
            x,y=round(anchor-(head[0]-rect[0])*scale),round(60-(head[1]-rect[1])*scale);im=Image.new('RGBA',(W,H));im.alpha_composite(small,(x,y));meta={'source':source.name,'sourceSha256':sha(source),'sourceCrop':rect,'pixelScale':scale,'placement':[x,y]}
        frames[v,p]=im;filename=f'view-{v}-phase-{p}-{name}.png';im.save(OUT/filename);bb=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox();assert bb and bb[0]>0 and bb[1]>0 and bb[2]<W and bb[3]<H,(v,p,bb)
        meta.update({'bearing':v,'phase':p,'name':name,'file':filename,'sha256':sha(OUT/filename),'alpha128Bounds':bb,'alphaHistogram':im.getchannel('A').histogram(),'medianCutoutAlpha':statistics.median(a for a in im.getchannel('A').getdata() if a>=128),'authored':True});records.append(meta)
atlas=Image.new('RGBA',(W*4,H*8))
for (v,p),im in frames.items():atlas.paste(im,(p*W,v*H))
atlas.save(OUT/'atlas.png')
for v in range(8):
    assert len({frames[v,p].tobytes() for p in range(4)})==4
    for p in range(4):assert atlas.crop((p*W,v*H,(p+1)*W,(v+1)*H)).tobytes()==frames[v,p].tobytes()
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
def cut(im):
    out=im.copy();out.putalpha(out.getchannel('A').point(lambda a:255 if a>=128 else 0));return out
for start in [0,4]:
    page=Image.new('RGB',(W*4+100,(H+25)*4+35),(42,45,49));d=ImageDraw.Draw(page);d.text((8,8),'382 Swim | all8 authored | same scale per view | alpha128 | native pixels',font=font,fill='white')
    for v in range(start,start+4):
        y=35+(v-start)*(H+25);d.text((8,y+35),f'View {v}',font=font,fill='white')
        for p,name in enumerate(names):
            x=100+p*W;d.text((x+8,y+4),name,font=font,fill='white');im=cut(frames[v,p]);page.paste(im,(x,y+25),im)
    page.save(OUT/f'review-bearings-{start}-{start+3}.png')
animated=[]
for p in range(4):
    panel=Image.new('RGB',(W*4,H*2+45),(42,45,49));ImageDraw.Draw(panel).text((8,8),'382 Swim '+names[p]+' | preview timing only',font=font,fill='white')
    for v in range(8):
        im=cut(frames[v,p]);panel.paste(im,((v%4)*W,40+(v//4)*H),im)
    animated.append(panel)
animated[0].save(OUT/'cycle-eight-views.gif',save_all=True,append_images=animated[1:],duration=220,loop=0,disposal=2)
m={'identity':'vanilla:mobile:382:None','state':'swimming','status':'eight-bearing-static-candidate','frameCount':32,'authoredFrames':32,'mirrorDerivedFrames':0,'pixelCanvas':[W,H],'frames':records,'atlasSha256':sha(OUT/'atlas.png'),'sourceManifest':'../source-manifest.json','phaseContract':'Symmetric breaststroke glide/pull/recovery/kick; rear recovery hands occluded under chin/chest.','technicalChecks':{'rgba':True,'fourDistinctPhasesPerBearing':True,'sourceCropNoAlpha128Clipping':True,'noAlpha128Clipping':True,'atlasExact':True,'samePixelScaleWithinBearing':True},'visuallyReviewed':False,'integrated':False,'userApproved':False,'generatedWorldScaleApplied':False,'knownLimits':['Quarter camera elevation and body proportions not perfectly uniform with profiles.','Head/face/muscle detail differs from native low-resolution source.','Waterline, temporal cycle and worldsize not runtime validated.','Rear Glide camera repaired only, other3 original phases retained.']}
m['visuallyReviewed']=True
m['visualReview']='Both contact pages inspected at native pixel size: eight bearings and four breaststroke phases distinct, long profile anatomy and camera foreshortening plausible. Quarter camera elevation and proportions vary. Static candidate, not temporal/waterline acceptance.'
(OUT/'manifest.json').write_text(json.dumps(m,indent=2)+'\n',encoding='utf-8');print(json.dumps({'output':str(OUT),'sha256':m['atlasSha256'],'checks':m['technicalChecks']},indent=2))
