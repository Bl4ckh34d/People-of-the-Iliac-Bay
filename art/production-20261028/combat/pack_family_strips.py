"""Pack one appearance's reviewed four-phase directional strips, without regex."""
from pathlib import Path
import csv
import hashlib
import json
import sys
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parent
REPO=ROOT.parents[2]
archive=int(sys.argv[1])
OUT=ROOT/f'candidate-melee-{archive}-eight-directions';OUT.mkdir(exist_ok=True)
names=['front','front-left','left','rear-left','back','rear-right','right','front-right']
bearings=['front','front-left','left-profile','rear-left','back','rear-right','right-profile','front-right']
phases=['ready','windup','strike','recovery']
cell=(190,260)
native=REPO/f'art/standing/source/native-idle/{archive}-0.png'
native_height=Image.open(native).height
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def bounds(im):
    start=round(im.height*.84)
    box=im.getchannel('A').crop((0,start,im.width,im.height)).point(lambda a:255 if a>=128 else 0).getbbox()
    if not box:raise ValueError('Missing feet')
    return (box[0]+box[2])/2,box[3]+start

def segments(im):
    alpha=im.getchannel('A').point(lambda a:255 if a>=128 else 0)
    occupied=[bool(alpha.crop((x,0,x+1,im.height)).getbbox()) for x in range(im.width)]
    runs=[];begin=None
    for x,value in enumerate(occupied+[False]):
        if value and begin is None:begin=x
        elif not value and begin is not None:runs.append([begin,x]);begin=None
    merged=[]
    for run in runs:
        if merged and run[0]-merged[-1][1]<=8:merged[-1][1]=run[1]
        else:merged.append(run)
    significant=[r for r in merged if r[1]-r[0]>=40]
    if len(significant)!=4:raise ValueError(f'Expected four separate frame silhouettes, got {significant}')
    return [(max(0,a-8),0,min(im.width,b+8),im.height) for a,b in significant]

pages=[Image.new('RGB',(760,1140),'#30343a') for _ in range(2)]
atlas=Image.new('RGBA',(760,2080));records=[]
for direction,name in enumerate(names):
    choices=list(ROOT.glob(f'{archive}-melee-{name}-cycle-v*.png'))
    source=max(choices,key=lambda p:int(p.stem.rsplit('-v',1)[1]))
    # Native walk records supersede the synthetic idle-reference pouch omission.
    if archive==381 and name in ('back','rear-left'):
        source=ROOT/f'{archive}-melee-{name}-cycle-v1.png'
    raw=Image.open(source).convert('RGBA');rects=segments(raw)
    first=raw.crop(rects[0]);foot_x,foot_y=bounds(first)
    band=first.getchannel('A').crop((round(foot_x)-18,0,round(foot_x)+18,first.height)).point(lambda a:255 if a>=128 else 0).getbbox()
    head_y=band[1];scale=native_height/(foot_y-head_y)
    row=[];animated=[]
    for column,(phase,rect) in enumerate(zip(phases,rects)):
        cut=raw.crop(rect);fx,fy=bounds(cut)
        reduced=cut.resize((round(cut.width*scale),round(cut.height*scale)),Image.Resampling.NEAREST)
        offset=(round(95-fx*scale),round(244-fy*scale))
        frame=Image.new('RGBA',cell);frame.paste(reduced,offset)
        filename=f'{direction}-{bearings[direction]}-{phase}.png';path=OUT/filename;frame.save(path)
        atlas.paste(frame,(column*190,direction*260))
        page=pages[direction//4];x=column*190;y=(direction%4)*285
        page.paste(frame,(x,y+25),frame);draw=ImageDraw.Draw(page)
        draw.text((x+5,y+5),f'{direction} {bearings[direction]} / {phase}',fill='white')
        for ly in (244,244-native_height):draw.line((x,y+25+ly,x+189,y+25+ly),fill='#5c6672')
        bg=Image.new('RGB',cell,'#30343a');bg.paste(frame,(0,0),frame);animated.append(bg)
        row.append({'phase':phase,'path':filename,'sha256':sha(path),'source':source.name,
                    'sourceSha256':sha(source),'sourceRect':rect,'sharedStripScale':scale,
                    'translation':offset,'measuredFootCenter':fx,'measuredFootBaseline':fy})
    animated[0].save(OUT/f'{direction}-{bearings[direction]}-preview.gif',save_all=True,
        append_images=animated[1:],duration=[180,160,100,180],loop=0)
    reference=REPO/f'art/standing/mobile/idle-{archive}/{archive}-idle-view-{direction}-{bearings[direction] if direction else "front-native"}.png'
    reference_kind='Original native frontal idle' if direction==0 else 'Generated standing derivative; not authoritative for hidden clothing details'
    if archive>=386:
        record=[0,1,2,3,4,3,2,1][direction]
        reference=ROOT/f'native-reference/{archive}/{archive}-{record}-0.png'
        reference_kind='Original native walk, generation identity input; opposite bearing interpreted via native renderer mirror contract' if direction>4 else 'Original native walk, generation identity input'
    original_manifest=ROOT/'native-reference'/str(archive)/'manifest.json'
    if not original_manifest.exists():
        original_manifest=ROOT.parent/'mobile-locomotion/families'/str(archive)/'native-reference.json'
    records.append({'direction':direction,'bearing':bearings[direction],'phaseCount':4,'frames':row,
                    'identityReference':str(reference),'identityReferenceSha256':sha(reference),
                    'identityReferenceKind':reference_kind,
                    'frontalIdleIdentityInput':str(native),'frontalIdleIdentityInputSha256':sha(native),
                    'originalWalkEvidenceManifest':str(original_manifest) if original_manifest.exists() else None,
                    'originalWalkEvidenceManifestSha256':sha(original_manifest) if original_manifest.exists() else None,
                    'motionTemplate':f'motion-templates/385-view-{direction}.png',
                    'motionTemplateSha256':sha(ROOT/f'motion-templates/385-view-{direction}.png'),
                    'headMeasurementY':head_y,'bodyPixelHeightTarget':native_height,
                    'reviewLimit':'Far weapon grip occluded in strike/recovery; final occlusion review remains open.' if direction==3 else None})
atlas.save(OUT/'atlas.png')
for i,page in enumerate(pages):page.save(ROOT/f'melee-{archive}-directions-{i*4}-{i*4+3}-review.png')
with (REPO/'art/standing/source/native-idle.csv').open(newline='',encoding='utf-8') as handle:
    native_metadata=next(r for r in csv.DictReader(handle) if int(r['archive'])==archive and int(r['frame'])==0)
native_w=int(native_metadata['width']);native_h=int(native_metadata['height']);sx=int(native_metadata['scaleX']);sy=int(native_metadata['scaleY'])
manifest={'schemaVersion':1,'archive':archive,'action':'melee-one-handed-sword','status':'candidate-eight-direction-four-phase-set',
    'selectedUniquePoses':32,'cellSize':cell,'pixelFootPivot':[95,244],
    'nativeReference':{'path':str(native),'sha256':sha(native),'nativeRecord':5,'nativePixelSize':[native_w,native_h],
                      'nativeRecordScaleXY':[sx,sy],'nativeSizeBeforeOverridesGlobalScale':[native_w+int(native_w*sx/256),native_h+int(native_h*sy/256)]},
    'worldSizePolicy':'Pixel canvas/pivot are not world size. Preserve native record-scaled size and TextureReplacement overrides separately at future integration. No runtime world size was set.',
    'atlas':'atlas.png','atlasSha256':sha(OUT/'atlas.png'),'directions':records,
    'processing':'Measured four silhouette spans; same nearest-neighbor scale per strip, foot-center/baseline translation. Original alpha preserved; no mirroring or anatomy edits.',
    'states':{'candidate':True,'generated':True,'technically_checked':True,'visually_reviewed':True,'integrated':False,'user_approved':False},
    'limitations':['No runtime playback/user acceptance.','Generated art is not pixel-identical to native source.',
      'Partial alpha/fringes retained.','Rear-left grip occlusion remains an open final review.'],
    'repairs':(['SUPERSEDED: initial back/rear-left pouch-removal assumption used generated idle views, not original walk records. Native 381 records3/4 show rear pouch. Selected back/rear-left v1 restored; rear-right v2 restores source-visible pouch.'] if archive==381 else
               ['Rear-left v1 strike switched weapon hand; v2 restored correct near empty hand. Original walk records supersede generated standing pouch omission; rear-left v3, back/rear-right v2 restore native-visible rear pouch.'] if archive==382 else
               ['Back v1 windup long sleeve corrected in v2. Original walk records supersede generated standing pouch omission; rear-left/rear-right v2 and back v3 restore native-visible pouch.'] if archive==383 else
               ['Native walk shows pale-green pouch omitted by frontal idle. Profile and three rear strips v2 restore source-visible pouch.'] if archive==384 else [])}
if archive in (381,382,383):manifest['repairs'].append('Left/right profile v2 moves pouch from front to rear hip, following original native walk record2 and mirrored native bearing.')
if archive==386:manifest['repairs'].append('Front-left v1 inherited teal weapon-forearm cloth from motion template. Selected v2 changes it to native dark-brown wristguard; upper arm remains bare.')
if archive==387:manifest['repairs'].extend(['Front v2 restores brown glove continuity in strike/recovery.','Rear-left v1 strike switched to near left hand; v2 restores lowered empty near hand and occluded far right grip.'])
if archive==388:manifest['repairs'].extend(['SUPERSEDED: initial brown rear/profile pouch assumption misread small native orange/brown pixels; rear-right v2 relocation was also wrong. Enlarged original records2/3/4 clearly show no pouch.','Selected left/rear-left/back/right v2 and rear-right v3 remove invented pouch; original sources independently reviewed by parent. Frontal pouch remains supported by native idle.'])
if archive==389:manifest['repairs'].extend(['Left-profile v2 corrects recovery hand switch, restoring near lowered empty glove and far right weapon arm.','Back v2 removes invented silver dangling object below native green pouch. Enlarged original walk records2/3/4 independently checked for actual green pouch.'])
if archive==390:manifest['repairs'].append('Rear-right v2 removes broad frontal cream collar from back; native rear garment remains teal with small bare nape. Native records2/3/4 enlarged before generation; green rear/profile pouch supported.')
if archive==392:
    manifest['repairs'].append('All eight v1 strips omitted the original walk blue projecting bonnet. Frontal idle alone led to an incomplete white-headband interpretation. All eight selected v2 strips restore source-visible blue crown/side panels, with rear blonde hair occluding most bonnet from back. Original native directional walk references, including enlarged four-frame sheets, controlled these edits.')
    manifest['headwearReviewStatus']='corrected-v2-root-static-candidate-reviewed-see-392-parent-review.json'
    manifest['headwearCorrectionReferences']=[{'path':str(ROOT.parent/f'mobile-locomotion/families/392/native-reference/view-{r}.png'),'sha256':sha(ROOT.parent/f'mobile-locomotion/families/392/native-reference/view-{r}.png')} for r in range(5)]
    manifest['additionalHeadwearMasters']=[{'path':n,'sha256':sha(ROOT/n)} for n in ('392-melee-left-cycle-v2.png','392-melee-front-cycle-v2.png')]
if archive==393:
    manifest['repairs'].extend(['Front v2 reduces exaggerated upper-arm musculature and replaces ankle boots with native low shoes and exposed lower legs.', 'Left profile v2 removes unsupported frontal pouch. Back v2 removes invented yellow rear hood rim; yellow belongs around the face opening.', 'Rear-left v2 corrects visible near-left-hand sword ownership in strike. Selected strike passes horizontally behind torso; the grip remains occluded and its wrist path cannot be fully inspected.'])
    manifest['additionalIdentityMaster']={'path':'393-melee-front-cycle-v2.png','sha256':sha(ROOT/'393-melee-front-cycle-v2.png'),'usedForDirections':[1,2,3,4,5,6,7],'authority':'Generated corrected front master for proportions only; original native walk/idle remain authoritative.'}
if archive==395:
    manifest['repairs'].extend(['Front v2 removes invented gold wrist bracelets from strike/recovery.', 'Rear-right v2 removes front buckle incorrectly visible on side waist; plain yellow sash remains.'])
if archive==396:manifest['repairs'].append('Front-left v2 removes an invented pale curved hip strap below the belt, restoring native bare skin at that side.')
if archive==398:
    manifest['repairs'].extend(['Left profile v2 removes an invented front pouch; enlarged original walk record2 shows no pouch.', 'Rear-left v1 and v2 strike visibly switched to the near left hand. Selected v3 uses the 397 rear-left motion master to restore a lowered empty near hand; sword passes behind torso, so far wrist path remains occluded.'])
    manifest['limitations'].append('Front strike and recovery have only a small arm difference; skirt obscures leg mechanics.')
    manifest['additionalRepairMotionMaster']={'path':'397-melee-rear-left-cycle-v1.png','sha256':sha(ROOT/'397-melee-rear-left-cycle-v1.png'),'usedForDirection':3,'authority':'Generated motion reference only; original398 native art controls identity.'}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
assert len(records)==8 and all(len(r['frames'])==4 for r in records)
assert all(sha(OUT/f['path'])==f['sha256'] for r in records for f in r['frames'])
print(f'Archive {archive}: 8 directions / 32 selected hashes verified. Native art height {native_height}px; world-size policy separate.')
