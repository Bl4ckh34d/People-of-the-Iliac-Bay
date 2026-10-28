"""Measure immutable combat candidates and create review-only contact sheets."""
from pathlib import Path
from collections import Counter
import hashlib
import json
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

FILES = [
    ('385-melee-pilot-v1-rejected.png', 'melee-strip-v1', 4, 2),
    ('385-melee-profile-pilot-v2.png', 'melee-profile-strip-v2', 4, 1),
    ('385-bow-profile-pilot-v1.png', 'bow-profile-strip-v1', 4, 1),
    ('385-melee-right-key-windup-v1.png', 'melee-right-windup-v1', 1, 1),
    ('385-melee-right-key-strike-v1.png', 'melee-right-strike-v1', 1, 1),
    ('385-melee-right-key-strike-bound-v2.png', 'melee-right-strike-bound-v2', 1, 1),
    ('385-melee-right-key-ready-v1.png', 'melee-right-ready-v1-clipped-rejected', 1, 1),
    ('385-melee-right-key-recovery-v1.png', 'melee-right-recovery-v1', 1, 1),
    ('385-melee-right-key-ready-v2.png', 'melee-right-ready-v2', 1, 1),
    ('385-bow-right-key-draw-v1.png', 'bow-right-draw-v1', 1, 1),
    ('385-bow-right-key-release-v1.png', 'bow-right-release-v1', 1, 1),
    ('385-melee-front-key-windup-v1.png', 'melee-front-windup-v1', 1, 1),
    ('385-melee-front-key-strike-v1.png', 'melee-front-strike-v1', 1, 1),
    ('385-melee-front-key-recovery-v1.png', 'melee-front-recovery-v1', 1, 1),
    ('385-melee-front-key-ready-v1.png', 'melee-front-ready-v1-bright-rejected', 1, 1),
    ('385-melee-front-key-ready-v2.png', 'melee-front-ready-v2', 1, 1),
]

known={entry[0] for entry in FILES}
for path in sorted(list(ROOT.glob('*-melee-*-v*.png'))+list(ROOT.glob('*-bow-*-v*.png'))):
    if path.name not in known:
        FILES.append((path.name,path.stem,4 if '-cycle-' in path.name else 1,1))
        known.add(path.name)

report = []
for name, asset_id, columns, rows in FILES:
    path = ROOT / name
    im = Image.open(path).convert('RGBA')
    alpha = im.getchannel('A')
    counts = Counter(alpha.getdata())
    opaque = counts[255]
    soft = sum(n for a,n in counts.items() if 0 < a < 255)
    record = {
        'assetId': asset_id, 'path': name, 'sha256': digest(path),
        'size': list(im.size), 'alphaBoundingBox': alpha.getbbox(),
        'alpha128BoundingBox': alpha.point(lambda a:255 if a>=128 else 0).getbbox(),
        'alphaCounts': {'transparent': counts[0], 'opaque': opaque, 'partial': soft},
        'nonzeroAlphaFractionPartial': soft / (soft + opaque),
        'mostCommonNonzeroAlpha': Counter({a:n for a,n in counts.items() if a}).most_common(8),
        'requestedGrid': [columns,rows],
        'states': {'candidate': True, 'generated': True, 'technically_checked': True,
                   'visually_reviewed': True, 'integrated': False, 'user_approved': False},
        'technicalCheckMeaning': 'Inspected and measured; not a production-quality pass.',
        'cycleAccepted': False,
    }
    cells = []
    for row in range(rows):
        for column in range(columns):
            box = (column*im.width//columns,row*im.height//rows,
                   (column+1)*im.width//columns,(row+1)*im.height//rows)
            local = im.crop(box).getchannel('A').getbbox()
            cells.append({'requestedCell': [column,row], 'rect': box, 'alphaBoundingBox': local})
    record['requestedCells'] = cells
    report.append(record)

# These are disposable-style inspection renders, not modified production art.
# Preserve every generated source unchanged; no alpha repair or palette remap.
contact = Image.new('RGB', (1120, 920), '#30343a')
draw = ImageDraw.Draw(contact)
for index, (name, asset_id, columns, rows) in enumerate(FILES[:3]):
    im = Image.open(ROOT/name).convert('RGBA')
    scale = min(1080/im.width, 250/im.height)
    small = im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.NEAREST)
    y = 32+index*300
    draw.text((20,y-20), asset_id+' | inspection scale only | REJECTED as cycle',fill='white')
    contact.paste(small,(20,y),small)
contact.save(ROOT/'combat-strips-review.png')

# Common displayed head-to-ground height from manually reviewed landmarks.
# Raised swords are excluded from body height. Review only, not runtime art.
source = REPO/'art/standing/mobile/idle-385/385-idle-view-6-right-profile.png'
items = [(source,'Source right profile',0,163),
         (ROOT/FILES[3][0],'Windup key / candidate',455,1260),
         (ROOT/FILES[5][0],'Bound strike v2 / candidate',451,1260)]
key_sheet = Image.new('RGB',(1200,560),'#30343a')
kd = ImageDraw.Draw(key_sheet)
for index,(path,label,head_y,ground_y) in enumerate(items):
    im=Image.open(path).convert('RGBA')
    bbox=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    cut=im.crop(bbox)
    factor=326/(ground_y-head_y)
    cut=cut.resize((round(cut.width*factor),round(cut.height*factor)),Image.Resampling.NEAREST)
    x=index*400+(400-cut.width)//2
    y=520-round((ground_y-bbox[1])*factor)
    key_sheet.paste(cut,(x,y),cut)
    kd.text((index*400+15,15),label,fill='white')
    kd.text((index*400+15,32),'approximate common body height; review only',fill='#bbc1ca')
key_sheet.save(ROOT/'combat-keyframes-review.png')

# Two-key hold preview demonstrates the pair only. It is not an attack cycle.
pair_images=[Image.open(ROOT/FILES[i][0]).convert('RGBA') for i in (3,5)]
pair_frames=[]
for im in pair_images:
    frame=Image.new('RGB',(458,574),'#30343a')
    reduced=im.resize((458,550),Image.Resampling.NEAREST)
    frame.paste(reduced,(0,24),reduced)
    ImageDraw.Draw(frame).text((12,8),'Two-key comparison ONLY; incomplete cycle',fill='white')
    pair_frames.append(frame)
pair_frames[0].save(ROOT/'combat-bound-pair-comparison.gif',save_all=True,
                    append_images=pair_frames[1:],duration=650,loop=0)

# Fixed-coordinate reduction; no independent per-frame fit/scale/baseline correction.
cycle_indexes=[8,3,5,7]
cycle_names=['ready','windup','strike','recovery']
cycle_source_rect=(350,100,1145,1300)
cycle_size=(161,243)
cycle_dir=ROOT/'candidate-melee-right-cycle'
cycle_dir.mkdir(exist_ok=True)
cycle_atlas=Image.new('RGBA',(cycle_size[0]*4,cycle_size[1]))
cycle_review=Image.new('RGB',(cycle_size[0]*4,cycle_size[1]+30),'#30343a')
cycle_draw=ImageDraw.Draw(cycle_review)
cycle_preview=[]
cycle_manifest=[]
for column,(index,name) in enumerate(zip(cycle_indexes,cycle_names)):
    raw=Image.open(ROOT/FILES[index][0]).convert('RGBA')
    small=raw.crop(cycle_source_rect).resize(cycle_size,Image.Resampling.NEAREST)
    target=cycle_dir/(name+'.png')
    small.save(target)
    cycle_atlas.paste(small,(column*cycle_size[0],0))
    cycle_review.paste(small,(column*cycle_size[0],30),small)
    cycle_draw.text((column*cycle_size[0]+5,5),name,fill='white')
    # Static landmark lines are identical in every frame.
    for raw_y in (455,1260):
        y=30+round((raw_y-cycle_source_rect[1])*cycle_size[1]/1200)
        cycle_draw.line((column*cycle_size[0],y,(column+1)*cycle_size[0]-1,y),fill='#606b77')
    frame=Image.new('RGB',cycle_size,'#30343a'); frame.paste(small,(0,0),small)
    cycle_preview.append(frame)
    cycle_manifest.append({'phase':name,'source':FILES[index][0],
                           'sourceSha256':digest(ROOT/FILES[index][0]),
                           'file':str(target.relative_to(ROOT)),
                           'sha256':digest(target),'durationMs':[180,160,100,180][column]})
cycle_atlas.save(cycle_dir/'atlas.png')
cycle_review.save(ROOT/'melee-right-cycle-native-review.png')
cycle_review.resize((cycle_review.width*2,cycle_review.height*2),Image.Resampling.NEAREST).save(ROOT/'melee-right-cycle-2x-review.png')
cycle_preview[0].save(ROOT/'melee-right-cycle-preview.gif',save_all=True,append_images=cycle_preview[1:],
                      duration=[180,160,100,180],loop=0)
(cycle_dir/'manifest.json').write_text(json.dumps({'schemaVersion':1,'archive':385,
    'direction':6,'bearing':'right-profile','action':'melee-one-handed-sword',
    'status':'candidate-complete-four-key-cycle; user approval and runtime integration pending',
    'sourceRect':cycle_source_rect,'cellSize':cycle_size,'bodyHeightApproximate':163,
    'processing':'Same crop and nearest-neighbor reduction for all frames. Generated alpha preserved.',
    'generatedUniqueFrames':4,'reusedFrames':0,'cycle':cycle_manifest,
    'states':{'candidate':True,'generated':True,'technically_checked':True,
              'visually_reviewed':True,'integrated':False,'user_approved':False},
    'limitations':['No native runtime playback verified.','Partial alpha preserved; no binary-alpha cleanup.',
                   'Subtle generated body and weapon differences remain.','Other seven directions absent.']},indent=2)+'\n',encoding='utf-8')

bow_dir=ROOT/'candidate-bow-right-pair'
bow_dir.mkdir(exist_ok=True)
bow_size=(171,229)
bow_rect=(300,150,1145,1280)
bow_review=Image.new('RGB',(342,259),'#30343a')
bow_draw=ImageDraw.Draw(bow_review)
bow_frames=[]
bow_records=[]
for column,(index,name) in enumerate(zip((9,10),('draw','release'))):
    raw=Image.open(ROOT/FILES[index][0]).convert('RGBA')
    small=raw.crop(bow_rect).resize(bow_size,Image.Resampling.NEAREST)
    target=bow_dir/(name+'.png'); small.save(target)
    bow_review.paste(small,(column*171,30),small)
    bow_draw.text((column*171+5,5),name+' / pair only',fill='white')
    for raw_y in (430,1240):
        y=30+round((raw_y-150)*229/1130)
        bow_draw.line((column*171,y,(column+1)*171-1,y),fill='#606b77')
    bow_records.append({'phase':name,'source':FILES[index][0],
                        'sourceSha256':digest(ROOT/FILES[index][0]),'file':str(target.relative_to(ROOT)),
                        'sha256':digest(target)})
    frame=Image.new('RGB',bow_size,'#30343a');frame.paste(small,(0,0),small);bow_frames.append(frame)
bow_review.save(ROOT/'bow-right-pair-native-review.png')
bow_review.resize((684,518),Image.Resampling.NEAREST).save(ROOT/'bow-right-pair-2x-review.png')
bow_frames[0].save(ROOT/'bow-right-pair-comparison.gif',save_all=True,append_images=bow_frames[1:],duration=650,loop=0)
(bow_dir/'manifest.json').write_text(json.dumps({'schemaVersion':1,'archive':385,'direction':6,
    'bearing':'right-profile','action':'bow','status':'candidate-two-key-pair-incomplete-cycle',
    'sourceRect':bow_rect,'cellSize':bow_size,'generatedUniqueFrames':2,'cycleComplete':False,
    'processing':'Same crop and nearest-neighbor reduction for both frames. Generated alpha preserved.',
    'frames':bow_records,'missingPhases':['nock-ready','recovery'],
    'states':{'candidate':True,'generated':True,'technically_checked':True,'visually_reviewed':True,
              'integrated':False,'user_approved':False},
    'block':'Further nock generation rejected by image_gen moderation_blocked violence; see generation-block.json.'},indent=2)+'\n',encoding='utf-8')

# Front sources have global canvas shifts. Translate by measured foot bounds,
# retaining one common scale for every phase (no independent body stretching).
front_dir=ROOT/'candidate-melee-front-cycle';front_dir.mkdir(exist_ok=True)
front_size=(170,250);front_scale=0.16
front_sheet=Image.new('RGB',(680,280),'#30343a');fd=ImageDraw.Draw(front_sheet)
front_atlas=Image.new('RGBA',(680,250));front_records=[];front_gif=[]
for column,(index,name) in enumerate(zip((15,11,12,13),cycle_names)):
    raw=Image.open(ROOT/FILES[index][0]).convert('RGBA')
    feet=raw.getchannel('A').crop((0,1100,raw.width,raw.height)).point(lambda a:255 if a>=128 else 0).getbbox()
    center_x=(feet[0]+feet[2])/2;ground=feet[3]+1100
    resized=raw.resize((round(raw.width*front_scale),round(raw.height*front_scale)),Image.Resampling.NEAREST)
    dx=round(85-center_x*front_scale);dy=round(240-ground*front_scale)
    small=Image.new('RGBA',front_size);small.paste(resized,(dx,dy))
    target=front_dir/(name+'.png');small.save(target)
    front_atlas.paste(small,(column*170,0))
    front_sheet.paste(small,(column*170,30),small)
    fd.text((column*170+5,5),name,fill='white')
    for y in (270,107):fd.line((column*170,y,column*170+169,y),fill='#606b77')
    frame=Image.new('RGB',front_size,'#30343a');frame.paste(small,(0,0),small);front_gif.append(frame)
    front_records.append({'phase':name,'source':FILES[index][0],'sourceSha256':digest(ROOT/FILES[index][0]),
                          'file':str(target.relative_to(ROOT)),'sha256':digest(target),
                          'nativeCanvasTranslation':[dx,dy],'measuredRawFootBaseline':ground,
                          'measuredRawFootCenterX':center_x})
front_atlas.save(front_dir/'atlas.png');front_sheet.save(ROOT/'melee-front-cycle-native-review.png')
front_sheet.resize((1360,560),Image.Resampling.NEAREST).save(ROOT/'melee-front-cycle-2x-review.png')
front_gif[0].save(ROOT/'melee-front-cycle-preview.gif',save_all=True,append_images=front_gif[1:],duration=[180,160,100,180],loop=0)
(front_dir/'manifest.json').write_text(json.dumps({'schemaVersion':1,'archive':385,'direction':0,
    'bearing':'front','action':'melee-one-handed-sword','generatedUniqueFrames':4,'reusedFrames':0,
    'status':'candidate-four-key-cycle; minor size/palette variation remains; no user/runtime acceptance',
    'cellSize':front_size,'commonScale':front_scale,'cycle':front_records,
    'processing':'One common nearest-neighbor scale, each full image translated to measured shared foot baseline/center. Original alpha retained.',
    'states':{'candidate':True,'generated':True,'technically_checked':True,'visually_reviewed':True,
              'integrated':False,'user_approved':False},
    'limitations':['Generated front does not preserve native front pixels exactly.','Minor head-height/palette variation remains.',
                   'Foot-based translation aligns global generator shifts; no per-frame scaling or anatomy repair.',
                   'Partial-alpha/fringe pixels remain.']},indent=2)+'\n',encoding='utf-8')
regions={'head':(570,451,726,610),'legs_feet':(550,1000,770,1260),
         'pouch':(645,775,742,870),'far_empty_hand':(730,865,781,938)}
pair_measurements={}
for name,box in regions.items():
    first=list(pair_images[0].crop(box).getdata())
    second=list(pair_images[1].crop(box).getdata())
    intersection=sum(a[3]>=128 and b[3]>=128 for a,b in zip(first,second))
    union=sum(a[3]>=128 or b[3]>=128 for a,b in zip(first,second))
    pair_measurements[name]={'region':box,'pixels':len(first),
        'exactRGBAEqual':sum(a==b for a,b in zip(first,second)),
        'alpha128IntersectionOverUnion':intersection/union if union else 1,
        'meanAbsoluteRGBDifference':sum(abs(a[c]-b[c]) for a,b in zip(first,second) for c in range(3))/(len(first)*3)}

payload={'schemaVersion':1,'inspectionOnly':True,'assets':report,
         'boundPairRegionMeasurements':pair_measurements,
         'sources':[{'path':str(source),'sha256':digest(source)},
                    {'path':str(REPO/'art/standing/source/native-idle/385-0.png'),
                     'sha256':digest(REPO/'art/standing/source/native-idle/385-0.png')}]}
(ROOT/'technical-checks.json').write_text(json.dumps(payload,indent=2)+'\n',encoding='utf-8')
print(f'Checked {len(report)} generated PNGs; technical-checks.json retains dimensions, hashes and alpha measurements.')
