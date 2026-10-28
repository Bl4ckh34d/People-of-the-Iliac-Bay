from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import hashlib,json,statistics
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'swim-profile-v1';OUT.mkdir(exist_ok=True);W,H=256,192;SCALE=21/64
source=ROOT/'swim-profile-generated-v1.png';raw=Image.open(source).convert('RGBA');cuts=[0,620,1060,1565,2170];heads=[(437,293),(994,292),(1445,297),(2015,294)];names=['glide','pull','recovery','kick'];frames=[];records=[];sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
for p,name in enumerate(names):
    rect=(cuts[p],0,cuts[p+1],raw.height);crop=raw.crop(rect);bb=crop.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox();assert bb and bb[0]>0 and bb[1]>0 and bb[2]<crop.width and bb[3]<crop.height,(p,bb)
    small=crop.resize((round(crop.width*SCALE),round(crop.height*SCALE)),Image.Resampling.NEAREST);x,y=round(180-(heads[p][0]-rect[0])*SCALE),round(60-heads[p][1]*SCALE)
    im=Image.new('RGBA',(W,H));im.alpha_composite(small,(x,y));frames.append(im);bb=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox();assert bb and bb[0]>0 and bb[1]>0 and bb[2]<W and bb[3]<H
    filename=f'phase-{p}-{name}.png';im.save(OUT/filename);records.append({'phase':p,'name':name,'file':filename,'sha256':sha(OUT/filename),'sourceCrop':rect,'pixelScale':SCALE,'placement':[x,y],'alpha128Bounds':bb,'alphaHistogram':im.getchannel('A').histogram(),'medianCutoutAlpha':statistics.median(a for a in im.getchannel('A').getdata() if a>=128)})
atlas=Image.new('RGBA',(W*4,H))
for p,im in enumerate(frames):atlas.paste(im,(p*W,0))
atlas.save(OUT/'atlas.png');assert len({im.tobytes() for im in frames})==4
for p,im in enumerate(frames):assert atlas.crop((p*W,0,(p+1)*W,H)).tobytes()==im.tobytes()
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15);review=Image.new('RGB',(W*4+150,H*2+70),(42,45,49));d=ImageDraw.Draw(review)
d.text((8,8),'383 swim | native original + 4 poses | shared head-based scale | no worldscale applied',font=font,fill='white')
native=Image.open(ROOT.parents[3]/'standing/source/native-idle/383-0.png').convert('RGBA');original=native.resize((native.width*2,native.height*2),Image.Resampling.NEAREST);original.putalpha(original.getchannel('A').point(lambda a:255 if a>=128 else 0));review.paste(original,(0,95),original);d.text((8,35),'Native original2x',font=font,fill='white')
for p,im in enumerate(frames):
    x=155+(p%2)*W*2;y=45+(p//2)*(H+5);cut=im.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0));large=cut.resize((W*2,H*2),Image.Resampling.NEAREST);review.paste(large,(x,y-65),large);d.text((x+8,y+8),names[p]+' 2x',font=font,fill='white')
review.save(OUT/'native-comparison.png')
m={'identity':'vanilla:mobile:383:None','state':'swimming','bearing':6,'status':'profile-pilot-candidate','frameCount':4,'pixelCanvas':[W,H],'pixelScale':SCALE,'frames':records,'source':source.name,'sourceSha256':sha(source),'atlasSha256':sha(OUT/'atlas.png'),'sourceManifest':'../source-manifest.json','motionReference':'../../382/swim-profile-generated-v1.png','technicalChecks':{'rgba':True,'fourDistinctPhases':True,'sourceCropNoAlpha128Clipping':True,'noAlpha128Clipping':True,'atlasExact':True,'samePixelScale':True},'visuallyReviewed':False,'integrated':False,'userApproved':False,'generatedWorldScaleApplied':False,'knownLimits':['Head/face, muscles and folds differ from native pixels; exact palette not claimed.','Four phases are static candidates, no temporal or waterline runtime acceptance.','Seven swim bearings missing.']}
m['visuallyReviewed']=True
m['visualReview']='Native comparison inspected: longbody glide/kick and bent-knee pull/recovery maintain teal/yellow tunic and native identity. Face, folds and exact palette differ; no waterline or temporal acceptance.'
(OUT/'manifest.json').write_text(json.dumps(m,indent=2)+'\n',encoding='utf-8');print(json.dumps({'output':str(OUT),'sha256':m['atlasSha256'],'checks':m['technicalChecks']},indent=2))
