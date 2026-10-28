from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib,statistics
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'climb-front-v1';OUT.mkdir(exist_ok=True)
source=ROOT/'climb-view-0-generated-v1.png';raw=Image.open(source).convert('RGBA');SCALE=21/90;W,H=160,224
heads=[(317,107),(751,108),(318,876),(785,872)];rects=[(0,0,512,768),(512,0,1024,720),(0,768,512,1536),(512,720,1024,1536)]
names=['reach','pull','tuck','push'];frames=[];records=[];sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
for p,(head,rect) in enumerate(zip(heads,rects)):
    crop=raw.crop(rect);bb=crop.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox();assert bb and bb[0]>0 and bb[1]>0 and bb[2]<crop.width and bb[3]<crop.height,(p,bb)
    small=crop.resize((round(crop.width*SCALE),round(crop.height*SCALE)),Image.Resampling.NEAREST);x,y=round(90-(head[0]-rect[0])*SCALE),round(44-(head[1]-rect[1])*SCALE)
    im=Image.new('RGBA',(W,H));im.alpha_composite(small,(x,y));frames.append(im);bounds=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox();assert bounds and bounds[0]>0 and bounds[1]>0 and bounds[2]<W and bounds[3]<H
    filename=f'phase-{p}-{names[p]}.png';im.save(OUT/filename);records.append({'phase':p,'name':names[p],'file':filename,'sha256':sha(OUT/filename),'sourceCrop':rect,'pixelScale':SCALE,'placement':[x,y],'alpha128Bounds':bounds,'alphaHistogram':im.getchannel('A').histogram(),'medianCutoutAlpha':statistics.median(a for a in im.getchannel('A').getdata() if a>=128)})
atlas=Image.new('RGBA',(W*4,H))
for p,im in enumerate(frames):atlas.paste(im,(p*W,0))
atlas.save(OUT/'atlas.png');assert len({im.tobytes() for im in frames})==4
for p,im in enumerate(frames):assert atlas.crop((p*W,0,(p+1)*W,H)).tobytes()==im.tobytes()
native=Image.open(ROOT.parents[3]/'standing/source/native-idle/383-0.png').convert('RGBA');cell=Image.new('RGBA',(W,H));cell.alpha_composite(native,((W-native.width)//2,200-native.height))
review=Image.new('RGB',(W*5*2,H*2+55),(42,45,49));d=ImageDraw.Draw(review);font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
d.text((8,8),'383 climb front | unchanged native + four poses | shared scale | physical contact unvalidated',font=font,fill='white')
for p,im in enumerate([cell]+frames):
    cut=im.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0));big=cut.resize((W*2,H*2),Image.Resampling.NEAREST);review.paste(big,(p*W*2,55),big);d.text((p*W*2+8,32),(['native original']+names)[p],font=font,fill='white')
review.save(OUT/'native-comparison.png')
m={'identity':'vanilla:mobile:383:None','state':'climbing','bearing':0,'status':'profile-pilot-candidate','frameCount':4,'pixelCanvas':[W,H],'pixelScale':SCALE,'source':source.name,'sourceSha256':sha(source),'sourceManifest':'../source-manifest.json','motionReference':'../../382/climb-view-0-generated-v1.png','atlasSha256':sha(OUT/'atlas.png'),'frames':records,'technicalChecks':{'rgba':True,'fourDistinctPhases':True,'sourceCropNoAlpha128Clipping':True,'noAlpha128Clipping':True,'atlasExact':True,'samePixelScale':True},'visuallyReviewed':False,'integrated':False,'userApproved':False,'generatedWorldScaleApplied':False,'knownLimits':['Far hand/foot occluded in pull/tuck/push; exact grip plane and weight transfer not validated.','Face, tunic folds, hem and yellow saturation differ from native low-detail pixels.','No mirroring authorized for this asymmetric arm phase without correct body-hand mapping.','Seven climb bearings and all swim frames missing for383.']}
m['visuallyReviewed']=True
m['visualReview']='Native comparison inspected: four true joint poses, anatomical right arm reaches in Reach/Push, left hand remains chest. Teal/yellow identity retained. Face/hem/folds differ; fixed wall contact and temporal movement unvalidated.'
(OUT/'manifest.json').write_text(json.dumps(m,indent=2)+'\n',encoding='utf-8');print(json.dumps({'output':str(OUT),'sha256':m['atlasSha256'],'checks':m['technicalChecks']},indent=2))
