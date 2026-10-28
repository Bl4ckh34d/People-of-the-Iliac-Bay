"""Assemble the same reach/pull/tuck/push movement from eight authored bearings."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import hashlib,json,statistics
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'381-climb-eight-views-v1';(OUT/'frames').mkdir(parents=True,exist_ok=True)
W,H=160,224;PHASES=['reach','pull','tuck','push'];NAMES=['front','front-left','left-profile','rear-left','rear','rear-right','right-profile','front-right']
HEADS={0:[(302,100),(732,100),(302,884),(734,888)],1:[(330,88),(770,117),(322,874),(815,820)],2:[(352,96),(778,112),(355,860),(830,814)],3:[(297,82),(767,110),(305,841),(815,819)],4:[(301,120),(755,136),(310,891),(760,870)],5:[(284,94),(793,108),(300,855),(795,774)],7:[(254,76),(770,94),(270,838),(770,807)]}
HEADS[7]=[(265,77),(770,81),(261,861),(762,840)]
HEAD_WIDTH={0:96,1:110,2:112,3:100,4:100,5:100,7:130}
RIGHT_SPLIT={0:720,1:720,2:720,3:690,4:768,5:680,7:740}
frames=[];records=[]
for view in range(8):
    if view==6:
        old=json.loads((ROOT/'381-climb-paired-pilot-v1/manifest.json').read_text())
        for phase,name in enumerate(PHASES):
            source=ROOT/'381-climb-paired-pilot-v1'/f'phase-{phase}-{name}.png'
            im=Image.open(source).convert('RGBA');frames.append(im)
            records.append({'bearing':view,'phase':phase,'name':name,'source':str(source.relative_to(ROOT)),'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'pixelScale':old['pixelScale'],'scaleBasis':'Unchanged pixels from reviewed right-profile pilot.'})
        continue
    source=ROOT/f'381-climb-paired-view-{view}-generated-v{2 if view==7 else 1}.png';raw=Image.open(source).convert('RGBA');scale=21/HEAD_WIDTH[view]
    split=RIGHT_SPLIT[view];rects=[(0,0,512,768),(512,0,1024,split),(0,768,512,1536),(512,split,1024,1536)]
    for phase,(rect,head) in enumerate(zip(rects,HEADS[view])):
        phase_source=source
        if view==0 and phase==3:
            phase_source=ROOT/'381-climb-paired-view-0-generated-v4.png'
            raw=Image.open(phase_source).convert('RGBA')
        crop=raw.crop(rect);cut=crop.getchannel('A').point(lambda a:255 if a>=128 else 0);bb=cut.getbbox()
        assert bb and bb[0]>0 and bb[1]>0 and bb[2]<crop.width and bb[3]<crop.height,('sourceCropCut',view,phase,bb)
        reduced=crop.resize((round(crop.width*scale),round(crop.height*scale)),Image.Resampling.NEAREST)
        x,y=round(80-(head[0]-rect[0])*scale),round(44-(head[1]-rect[1])*scale)
        im=Image.new('RGBA',(W,H));im.alpha_composite(reduced,(x,y));frames.append(im)
        records.append({'bearing':view,'phase':phase,'name':PHASES[phase],'source':phase_source.name,'sourceSha256':hashlib.sha256(phase_source.read_bytes()).hexdigest(),'sourceCrop':rect,'sourceHeadAnchor':head,'pixelScale':scale,'headAuditWidth':HEAD_WIDTH[view],'placement':[x,y],'scaleBasis':'One head-width factor for all four phases in this source; no individual bbox-height scaling.'})
atlas=Image.new('RGBA',(W*4,H*8))
for n,(im,rec) in enumerate(zip(frames,records)):
    view,phase=divmod(n,4);bb=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bb and bb[0]>0 and bb[1]>0 and bb[2]<W and bb[3]<H,(view,phase,bb)
    filename=f'frames/view-{view}-phase-{phase}-{PHASES[phase]}.png';im.save(OUT/filename)
    rec.update(file=filename,sha256=hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),alpha128Bounds=bb,alphaHistogram=im.getchannel('A').histogram(),medianCutoutAlpha=statistics.median(a for a in im.getchannel('A').getdata() if a>=128))
    atlas.paste(im,(phase*W,view*H))
atlas.save(OUT/'atlas.png')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',13)
for start,end in [(0,4),(4,8)]:
    review=Image.new('RGB',(W*4,H*4+70),(42,45,49));d=ImageDraw.Draw(review)
    d.text((8,8),'381 climb | reach-pull-tuck-push | native pixels | cutoff128 | candidate',font=font,fill='white')
    for phase,name in enumerate(PHASES):d.text((phase*W+8,31),name,font=font,fill='white')
    for view in range(start,end):
        for phase in range(4):
            cut=frames[view*4+phase].copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0))
            x,y=phase*W,65+(view-start)*H;review.paste(cut,(x,y),cut)
            if phase==0:d.text((x+5,y+2),f'{view} {NAMES[view]}',font=font,fill='white')
    review.save(OUT/f'review-bearings-{start}-{end-1}.png')
    review.resize((review.width*2,review.height*2),Image.Resampling.NEAREST).save(OUT/f'review-bearings-{start}-{end-1}-2x.png')
animated=[]
for phase in range(4):
    review=Image.new('RGB',(W*4,H*2+35),(42,45,49));d=ImageDraw.Draw(review);d.text((8,8),f'381 climb {PHASES[phase]} | illustrative timing, unaccepted',font=font,fill='white')
    for view in range(8):
        cut=frames[view*4+phase].copy();cut.putalpha(cut.getchannel('A').point(lambda a:255 if a>=128 else 0));x,y=(view%4)*W,35+(view//4)*H;review.paste(cut,(x,y),cut);d.text((x+5,y+3),str(view),font=font,fill='white')
    animated.append(review)
animated[0].save(OUT/'cycle-eight-views.gif',save_all=True,append_images=animated[1:],duration=[180,150,150,180],loop=0,disposal=2)
for view in range(8):assert len({im.tobytes() for im in frames[view*4:view*4+4]})==4
for n,im in enumerate(frames):
    view,phase=divmod(n,4);assert im.tobytes()==atlas.crop((phase*W,view*H,(phase+1)*W,(view+1)*H)).tobytes()
manifest={'status':'eight-bearing-climb-motion-candidate','appearance':381,'state':'climbing','frameCount':32,'bearingCount':8,'bearingNames':NAMES,'phases':PHASES,'pixelCanvas':[W,H],'frames':records,'atlasSha256':hashlib.sha256((OUT/'atlas.png').read_bytes()).hexdigest(),'technicalChecks':{'rgba':True,'fourDistinctPerBearing':True,'sourceCropNoAlpha128Clipping':True,'noAlpha128Clipping':True,'atlasExact':True,'uniformPixelScaleWithinBearing':True},'visuallyReviewed':False,'visualReview':'Pending combined contact review.','runtimeIntegrated':False,'userApproved':False,'worldScaleContract':'../native-scale-contract.json','intendedActorWorldHeightBeforeGlobalScale':79,'worldScaleApplied':False,'knownLimits':['This reach/pull/tuck/push sequence differs from earlier contralateral diagnostics; no relabeling of old poses.','Tuck strongly compresses silhouette through knee flexion; far hand/foot occlusion is not fully anatomically validated.','Contact planes, fixed grip registration and physical weight transfer remain unaccepted.','Face, clothing, pouch position and camera angle vary across independent bearings; view7 is relatively close to profile.','Exact native palette and runtime size/temporal behavior remain unvalidated.']}
manifest['visuallyReviewed']=True
manifest['visualReview']='Both32-frame contact pages personally inspected. Eight bearings with genuine reach/pull/tuck/push articulation; front-right7 subsequently regenerated toward viewer to separate from profile6. Far-limb occlusion, source identity variation and exact wall registration remain unaccepted.'
manifest['knownLimits'][3]='Face, cloth, pouch placement and camera angle vary across independent bearings. Bearing7 camera was repaired; exact cross-bearing consistency remains unaccepted.'
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'output':str(OUT),'technicalChecks':manifest['technicalChecks'],'atlasSha256':manifest['atlasSha256']},indent=2))
