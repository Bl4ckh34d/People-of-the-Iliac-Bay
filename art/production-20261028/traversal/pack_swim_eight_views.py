"""Pack independently generated swim bearings; fixed scale within each bearing."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import hashlib, json, statistics
ROOT=Path(__file__).resolve().parent
OUT=ROOT/'381-swim-eight-views-v2'
(OUT/'frames').mkdir(parents=True,exist_ok=True)
W,H=256,192
PHASES=['glide','pull','recovery','kick']
NAMES=['front','front-left','left-profile','rear-left','rear','rear-right','right-profile','front-right']
# Audited approximate head hair cross-sections. One factor per source sheet;
# no frame-specific bounding-box fitting and no anisotropic transformation.
HEAD_WIDTH={0:135,1:108,2:80,3:88,4:91,5:89,7:119}
RECTS={}
HEADS={
0:[(376,198),(1128,210),(377,632),(1134,665)],
1:[(285,187),(1008,187),(263,663),(1008,695)],
2:[(234,226),(914,226),(202,682),(930,689)],
3:[(239,98),(954,97),(223,580),(993,583)],
4:[(378,103),(998,98),(366,682),(1016,714)],
5:[(537,103),(1308,98),(540,582),(1308,582)],
7:[(562,183),(1355,156),(558,628),(1330,664)]}
TARGETS={0:(128,86),1:(95,72),2:(86,62),3:(92,48),4:(128,38),5:(164,48),7:(164,72)}
frames=[];records=[]
for view in range(8):
    if view==4:
        old=json.loads((ROOT/'381-swim-rear-camera-repair-v1/manifest.json').read_text())
        for phase,name in enumerate(PHASES):
            source=ROOT/'381-swim-rear-camera-repair-v1'/f'phase-{phase}.png'
            im=Image.open(source).convert('RGBA');frames.append(im)
            records.append({'bearing':view,'phase':phase,'name':name,'source':str(source.relative_to(ROOT)),'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'pixelScale':old['frames'][phase]['pixelScale'],'scaleBasis':'Camera regenerated; fixed factor all phases; recovery arms repaired independently, no perspective warp.'})
        continue
    if view==6:
        old=json.loads((ROOT/'381-swim-right-cycle-v2/manifest.json').read_text())
        for phase,name in enumerate(PHASES):
            source=ROOT/'381-swim-right-cycle-v2'/f'phase-{phase}-{name}.png'
            im=Image.new('RGBA',(W,H));im.alpha_composite(Image.open(source).convert('RGBA'),(0,32))
            frames.append(im);records.append({'bearing':view,'phase':phase,'name':name,'source':str(source.relative_to(ROOT)),'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'pixelScale':old['pixelScale'],'sourcePlacement':[0,32],'scaleBasis':'Existing corrected profile, unchanged pixels; canvas padded vertically.'})
        continue
    source=ROOT/f'381-swim-view-{view}-corrected-generated-v1.png'
    if view==0:source=ROOT/'381-swim-view-0-camera-repair-generated-v1.png'
    raw=Image.open(source).convert('RGBA');rw,rh=raw.size
    sy=512
    sx=rw//2
    rects=[(0,0,sx,sy),(sx,0,rw,sy),(0,sy,sx,rh),(sx,sy,rw,rh)]
    if view==7:rects=[(0,0,825,512),(825,0,rw,512),(0,512,720,rh),(720,512,rw,rh)]
    scale=21/HEAD_WIDTH[view]
    for phase,(rect,head) in enumerate(zip(rects,HEADS[view])):
        crop=raw.crop(rect)
        reduced=crop.resize((round(crop.width*scale),round(crop.height*scale)),Image.Resampling.NEAREST)
        hx,hy=head;tx,ty=TARGETS[view]
        x=round(tx-(hx-rect[0])*scale);y=round(ty-(hy-rect[1])*scale)
        im=Image.new('RGBA',(W,H));im.alpha_composite(reduced,(x,y));frames.append(im)
        records.append({'bearing':view,'phase':phase,'name':PHASES[phase],'source':source.name,'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'sourceCrop':rect,'sourceSize':raw.size,'pixelScale':scale,'headAuditWidth':HEAD_WIDTH[view],'sourceHeadAnchor':head,'placement':[x,y],'scaleBasis':'Single shared scale for all four phases based on audited head width; actual foreshortening retained.'})
atlas=Image.new('RGBA',(W*4,H*8))
for n,(im,rec) in enumerate(zip(frames,records)):
    view,phase=divmod(n,4)
    bounds=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bounds and bounds[0]>0 and bounds[1]>0 and bounds[2]<W and bounds[3]<H,(n,bounds)
    filename=f'frames/view-{view}-phase-{phase}-{PHASES[phase]}.png'
    im.save(OUT/filename)
    rec.update(file=filename,sha256=hashlib.sha256((OUT/filename).read_bytes()).hexdigest(),alpha128Bounds=bounds,alphaHistogram=im.getchannel('A').histogram(),medianCutoutAlpha=statistics.median(a for a in im.getchannel('A').getdata() if a>=128))
    atlas.paste(im,(phase*W,view*H))
atlas.save(OUT/'atlas.png')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
for begin,end in [(0,4),(4,8)]:
    sheet=Image.new('RGB',(W*4,H*4+100),(42,45,49));draw=ImageDraw.Draw(sheet)
    draw.text((10,8),'381 swim | independently generated bearings | cutoff128 | native packed pixels | not integrated',font=font,fill='white')
    for phase,name in enumerate(PHASES):draw.text((phase*W+10,32),name,font=font,fill='white')
    for view in range(begin,end):
        for phase in range(4):
            im=frames[view*4+phase].copy();im.putalpha(im.getchannel('A').point(lambda a:255 if a>=128 else 0))
            x,y=phase*W,70+(view-begin)*H;sheet.paste(im,(x,y),im)
            if phase==0:draw.text((x+8,y+5),f'{view}: {NAMES[view]}',font=font,fill='white')
    sheet.save(OUT/f'review-bearings-{begin}-{end-1}.png')
animated=[]
for phase in range(4):
    sheet=Image.new('RGB',(W*4,H*2+40),(42,45,49));draw=ImageDraw.Draw(sheet)
    draw.text((10,8),f'381 swim / {PHASES[phase]} / timing illustrative, not accepted',font=font,fill='white')
    for view in range(8):
        im=frames[view*4+phase].copy();im.putalpha(im.getchannel('A').point(lambda a:255 if a>=128 else 0))
        x,y=(view%4)*W,40+(view//4)*H;sheet.paste(im,(x,y),im);draw.text((x+5,y+5),str(view),font=font,fill='white')
    animated.append(sheet)
animated[0].save(OUT/'cycle-eight-views.gif',save_all=True,append_images=animated[1:],duration=[220,140,140,160],loop=0,disposal=2)
for view in range(8):assert len({im.tobytes() for im in frames[view*4:view*4+4]})==4
for n,im in enumerate(frames):
    view,phase=divmod(n,4);assert im.tobytes()==atlas.crop((phase*W,view*H,(phase+1)*W,(view+1)*H)).tobytes()
manifest={'status':'eight-bearing-swim-candidate','appearance':381,'state':'swimming','frameCount':32,'bearingCount':8,'phases':PHASES,'bearingNames':NAMES,'pixelCanvas':[W,H],'frames':records,'atlasSha256':hashlib.sha256((OUT/'atlas.png').read_bytes()).hexdigest(),'technicalChecks':{'rgba':True,'fourDistinctPerBearing':True,'noAlpha128Clipping':True,'atlasExact':True,'uniformPixelScaleWithinBearing':True},'visuallyReviewed':False,'runtimeIntegrated':False,'userApproved':False,'worldScaleContract':'../native-scale-contract.json','intendedActorWorldHeightBeforeGlobalScale':79,'worldScaleApplied':False,'knownLimits':['Independent generations vary face, head, garment and camera elevation; no temporal or cross-bearing acceptance yet.','Four-phase sparse cycle; playback timing illustrative.','Pouch side/depth can differ from native and needs cross-bearing review.','No water plane or waterline encoded; renderer behavior remains undecided.','Native exact palette and world mapping remain unvalidated.']}
manifest['visuallyReviewed']=True
manifest['visualReview']='Both native-size contact sheets personally inspected: eight distinct bearings and four readable phases, no cropped limbs. Camera elevation, head/face and pouch variation remain; no temporal acceptance.'
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'output':str(OUT),'technicalChecks':manifest['technicalChecks'],'atlasSha256':manifest['atlasSha256']},indent=2))
