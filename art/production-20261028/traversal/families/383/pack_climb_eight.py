"""Pack eight authored views with explicit anatomical right-arm phase contract."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib,statistics
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'climb-eight-views-v1';OUT.mkdir(exist_ok=True);W,H=160,224
names=['reach','pull','tuck','push'];sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();frames={};records=[]
heads={1:[(331,150),(764,160),(337,915),(790,878)],2:[(333,135),(744,140),(340,875),(778,872)],3:[(285,136),(741,140),(291,903),(728,894)],4:[(295,150),(732,155),(294,897),(762,888)],5:[(287,125),(748,143),(288,880),(754,875)],6:[(264,90),(800,108),(295,864),(775,812)],7:[(343,140),(750,151),(332,896),(766,885)]}
headwidth={1:95,2:95,3:95,4:100,5:95,6:110,7:100};rightcut={1:744,2:720,3:740,4:736,5:736,6:720,7:736}
for v in range(8):
    for p,name in enumerate(names):
        if v==0:
            source=ROOT/'climb-front-v1'/f'phase-{p}-{name}.png';im=Image.open(source).convert('RGBA');meta={'source':str(source.relative_to(ROOT)),'sourceSha256':sha(source),'pixelScale':21/90,'sourceAlreadyPacked':True}
        else:
            version=2 if v==3 and p in [1,2] else 1
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
    page=Image.new('RGB',(W*4+135,(H+30)*4+35),(42,45,49));d=ImageDraw.Draw(page);d.text((8,8),'383 Climb | all8 authored | RIGHT-arm contract | alpha128 | native pixels',font=font,fill='white')
    for v in range(start,start+4):
        y=35+(v-start)*(H+30);d.text((8,y+35),f'View {v}',font=font,fill='white')
        for p,name in enumerate(names):
            x=135+p*W;d.text((x+8,y+4),name,font=font,fill='white');im=cut(frames[v,p]);page.paste(im,(x,y+28),im)
    page.save(OUT/f'review-bearings-{start}-{start+3}.png')
animated=[]
for p in range(4):
    panel=Image.new('RGB',(W*4,H*2+45),(42,45,49));ImageDraw.Draw(panel).text((8,8),'383 Climb '+names[p]+' | preview timing only',font=font,fill='white')
    for v in range(8):
        im=cut(frames[v,p]);panel.paste(im,((v%4)*W,40+(v//4)*H),im)
    animated.append(panel)
animated[0].save(OUT/'cycle-eight-views.gif',save_all=True,append_images=animated[1:],duration=220,loop=0,disposal=2)
m={'identity':'vanilla:mobile:383:None','state':'climbing','status':'eight-bearing-static-candidate','frameCount':32,'authoredFrames':32,'mirrorDerivedFrames':0,'pixelCanvas':[W,H],'frames':records,'atlasSha256':sha(OUT/'atlas.png'),'sourceManifest':'../source-manifest.json','phaseContract':'Anatomical RIGHT arm overhead for reach/push, bent at forehead for pull/tuck; LEFT hand near chest. Front right-arm screen-left, rear screen-right, left-profile far arm, right-profile near arm.','technicalChecks':{'rgba':True,'fourDistinctPhasesPerBearing':True,'sourceCropNoAlpha128Clipping':True,'noAlpha128Clipping':True,'atlasExact':True,'samePixelScaleWithinBearing':True},'visuallyReviewed':False,'integrated':False,'userApproved':False,'generatedWorldScaleApplied':False,'knownLimits':['Far-limb occlusion in right profile prevents full anatomical proof.','Wall grips, foot contacts, weight transfer and temporal behavior not validated.','Head/body scale and muscle detail vary across generated bearings; exact native palette not claimed.','Rear-left pull/tuck arm identity repaired from correct382 motion source; reach/push originals retained.','All swim frames still missing for383.']}
m['visuallyReviewed']=True
m['visualReview']='Both contact pages personally inspected: anatomical right arm readable across8views, four true joint phases. Right profile stronger lean and rear-left repaired sleeve-edge drift remain. Front0 larger; no temporal, wall-contact or palette acceptance.'
(OUT/'manifest.json').write_text(json.dumps(m,indent=2)+'\n',encoding='utf-8');print(json.dumps({'output':str(OUT),'sha256':m['atlasSha256'],'checks':m['technicalChecks']},indent=2))
