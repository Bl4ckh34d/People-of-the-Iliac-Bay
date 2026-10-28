"""Pack eight authored views with explicit anatomical right-arm phase contract."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib,statistics
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'climb-eight-views-v1';OUT.mkdir(exist_ok=True);W,H=160,224
names=['reach','pull','tuck','push'];sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();frames={};records=[]
heads={0:[(294,100),(748,104),(302,875),(778,867)],1:[(320,150),(774,175),(332,916),(798,898)],2:[(295,100),(750,104),(302,850),(784,848)],3:[(265,125),(741,140),(291,903),(770,876)],4:[(295,150),(732,155),(294,897),(762,888)],5:[(287,125),(748,143),(288,880),(754,875)],7:[(337,138),(749,152),(335,897),(784,888)]}
headwidth={0:110,1:100,2:100,3:95,4:100,5:95,7:110};rightcut={0:720,1:768,2:704,3:736,4:736,5:736,7:736}
for v in range(8):
    for p,name in enumerate(names):
        if v==6:
            source=ROOT/'climb-profile-v1'/f'phase-{p}-{name}.png';im=Image.open(source).convert('RGBA');meta={'source':str(source.relative_to(ROOT)),'sourceSha256':sha(source),'pixelScale':21/110,'sourceAlreadyPacked':True}
        else:
            version=2 if v==4 and p!=2 else 1
            source=ROOT/f'climb-view-{v}-generated-v{version}.png';raw=Image.open(source).convert('RGBA');split=rightcut[v]
            rect=[(0,0,512,768),(512,0,1024,split),(0,768,512,1536),(512,split,1024,1536)][p];crop=raw.crop(rect)
            bb=crop.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox();assert bb and bb[0]>0 and bb[1]>0 and bb[2]<crop.width and bb[3]<crop.height,(v,p,bb,rect)
            scale=21/headwidth[v];head=heads[v][p];small=crop.resize((round(crop.width*scale),round(crop.height*scale)),Image.Resampling.NEAREST)
            x,y=round(80-(head[0]-rect[0])*scale),round(44-(head[1]-rect[1])*scale);im=Image.new('RGBA',(W,H));im.alpha_composite(small,(x,y));meta={'source':source.name,'sourceSha256':sha(source),'sourceCrop':rect,'pixelScale':scale,'placement':[x,y]}
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
    page=Image.new('RGB',(W*4+135,(H+30)*4+35),(42,45,49));d=ImageDraw.Draw(page);d.text((8,8),'382 Climb | all8 authored | RIGHT-arm contract | alpha128 | native pixels',font=font,fill='white')
    for v in range(start,start+4):
        y=35+(v-start)*(H+30);d.text((8,y+35),f'View {v}',font=font,fill='white')
        for p,name in enumerate(names):
            x=135+p*W;d.text((x+8,y+4),name,font=font,fill='white');im=cut(frames[v,p]);page.paste(im,(x,y+28),im)
    page.save(OUT/f'review-bearings-{start}-{start+3}.png')
animated=[]
for p in range(4):
    panel=Image.new('RGB',(W*4,H*2+45),(42,45,49));ImageDraw.Draw(panel).text((8,8),'382 Climb '+names[p]+' | preview timing only',font=font,fill='white')
    for v in range(8):
        im=cut(frames[v,p]);panel.paste(im,((v%4)*W,40+(v//4)*H),im)
    animated.append(panel)
animated[0].save(OUT/'cycle-eight-views.gif',save_all=True,append_images=animated[1:],duration=220,loop=0,disposal=2)
m={'identity':'vanilla:mobile:382:None','state':'climbing','status':'eight-bearing-static-candidate','frameCount':32,'authoredFrames':32,'mirrorDerivedFrames':0,'pixelCanvas':[W,H],'frames':records,'atlasSha256':sha(OUT/'atlas.png'),'sourceManifest':'../source-manifest.json','phaseContract':'Anatomical RIGHT arm overhead for reach/push, bent at forehead for pull/tuck; LEFT hand near chest. Front right-arm screen-left, rear screen-right, left-profile far arm, right-profile near arm.','technicalChecks':{'rgba':True,'fourDistinctPhasesPerBearing':True,'sourceCropNoAlpha128Clipping':True,'noAlpha128Clipping':True,'atlasExact':True,'samePixelScaleWithinBearing':True},'visuallyReviewed':False,'integrated':False,'userApproved':False,'generatedWorldScaleApplied':False,'knownLimits':['Far-limb occlusion in right profile prevents full anatomical proof.','Wall grips, foot contacts, weight transfer and temporal behavior not validated.','Head/body scale and muscle detail vary across generated bearings; exact native palette not claimed.','Rear pouch was source-conditioned repaired in reach/pull/push only; tuck original kept.','All swim frames still missing for382.']}
m['visuallyReviewed']=True
m['visualReview']='Both32-frame contact pages inspected. Right-arm identity readable across front/rear and opposed profiles; four phases distinct. Right profile leans more than other views, far limbs occluded. No temporal, physical-contact or palette acceptance.'
(OUT/'manifest.json').write_text(json.dumps(m,indent=2)+'\n',encoding='utf-8');print(json.dumps({'output':str(OUT),'sha256':m['atlasSha256'],'checks':m['technicalChecks']},indent=2))
