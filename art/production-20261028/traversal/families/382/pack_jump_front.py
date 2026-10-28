"""Pack native382 identity applied to the verified four-phase jump motion."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,hashlib,statistics
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'jump-front-v1';OUT.mkdir(exist_ok=True)
SOURCE=ROOT/'jump-front-generated-v1.png';raw=Image.open(SOURCE).convert('RGBA')
W,H=160,208;SCALE=21/80;PHASES=['crouch','takeoff','apex','landing'];HEAD_X=[260,775,1304,1825]
frames=[];records=[]
for phase,name in enumerate(PHASES):
    rect=(phase*512,0,(phase+1)*512,768);crop=raw.crop(rect)
    bb=crop.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bb and bb[0]>0 and bb[1]>0 and bb[2]<512 and bb[3]<768
    reduced=crop.resize((round(crop.width*SCALE),round(crop.height*SCALE)),Image.Resampling.NEAREST)
    x=round(80-(HEAD_X[phase]-rect[0])*SCALE)
    if phase==2:y=round(18-74*SCALE)
    else:y=round((184 if phase==1 else 190)-bb[3]*SCALE)
    im=Image.new('RGBA',(W,H));im.alpha_composite(reduced,(x,y));frames.append(im)
    bounds=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bounds and bounds[0]>0 and bounds[1]>0 and bounds[2]<W and bounds[3]<H
    filename=f'phase-{phase}-{name}.png';im.save(OUT/filename)
    records.append({'phase':phase,'name':name,'file':filename,'sha256':hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),'sourceCrop':rect,'pixelScale':SCALE,'placement':[x,y],'alpha128Bounds':bounds,'alphaHistogram':im.getchannel('A').histogram(),'medianCutoutAlpha':statistics.median(a for a in im.getchannel('A').getdata() if a>=128)})
atlas=Image.new('RGBA',(W*4,H))
for phase,im in enumerate(frames):atlas.paste(im,(phase*W,0))
atlas.save(OUT/'atlas.png')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
review=Image.new('RGB',(W*5*2,H*2+60),(42,45,49));d=ImageDraw.Draw(review)
d.text((10,8),'382 native identity + source-conditioned four-phase jump | same2x pixel zoom | native worldscale separate',font=font,fill='white')
nativepath=ROOT.parents[3]/'standing/source/native-idle/382-0.png';native=Image.open(nativepath).convert('RGBA');cell=Image.new('RGBA',(W,H));cell.alpha_composite(native,((W-native.width)//2,190-native.height))
for n,im in enumerate([cell]+frames):
    cut=im.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0));large=cut.resize((W*2,H*2),Image.Resampling.NEAREST);review.paste(large,(n*W*2,55),large)
    d.text((n*W*2+10,32),(['Unchanged native idle']+PHASES)[n],font=font,fill='white')
review.save(OUT/'native-comparison.png')
animated=[]
for phase,im in enumerate(frames):
    cut=im.copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0));large=cut.resize((W*3,H*3),Image.Resampling.NEAREST);panel=Image.new('RGB',(W*3,H*3+35),(42,45,49));panel.paste(large,(0,35),large);ImageDraw.Draw(panel).text((10,8),PHASES[phase],font=font,fill='white');animated.append(panel)
animated[0].save(OUT/'cycle-preview.gif',save_all=True,append_images=animated[1:],duration=[160,90,180,140],loop=0,disposal=2)
assert len({im.tobytes() for im in frames})==4
for phase,im in enumerate(frames):assert im.tobytes()==atlas.crop((phase*W,0,(phase+1)*W,H)).tobytes()
manifest={'identity':'vanilla:mobile:382:None','appearance':382,'state':'jumping','bearing':0,'status':'front-jump-native-identity-candidate','frameCount':4,'pixelCanvas':[W,H],'pixelScale':SCALE,'frames':records,'generatedSource':SOURCE.name,'generatedSourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'atlasSha256':hashlib.sha256((OUT/'atlas.png').read_bytes()).hexdigest(),'motionReference':'../../../381-jump-front-cycle-v1/atlas.png','nativeIdentityReferences':['../../../../standing/source/native-idle/382-0.png','../../../../mobile-locomotion/families/382/native-reference/view-0.png'],'sourceManifest':'../source-manifest.json','technicalChecks':{'rgba':True,'fourDistinctPhases':True,'sourceCropNoAlpha128Clipping':True,'noAlpha128Clipping':True,'atlasExact':True,'identicalPixelScale':True},'visuallyReviewed':False,'runtimeIntegrated':False,'userApproved':False,'nativeFrontWalkWorldHeightBeforeGlobalScale':81,'generatedWorldScaleApplied':False,'knownLimits':['Only front4phase produced; sevenjumpbearings and allclimb/swim missing for382.','Generated muscles, beard detail, skin highlights and wrap folds exceed native detail; exactpalette not claimed.','Frame positions are artwork staging, not validated runtime jump offsets or timing.']}
manifest['nativeIdentityReferences'][0]='../../../../../standing/source/native-idle/382-0.png'
manifest['visuallyReviewed']=True
manifest['visualReview']='Native comparison inspected: beard, bare torso, white wrap, metal bracers and single green pouch retained; four distinct jump poses readable. Muscle/face detail and skin highlights differ from native; no temporal acceptance.'
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'output':str(OUT),'technicalChecks':manifest['technicalChecks'],'atlasSha256':manifest['atlasSha256']},indent=2))
