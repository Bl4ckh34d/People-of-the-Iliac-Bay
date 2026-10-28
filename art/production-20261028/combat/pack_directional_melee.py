"""Pack reviewed 385 melee candidate poses; preserve alpha and source art."""
from pathlib import Path
import hashlib
import json
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'candidate-melee-385-eight-directions'
OUT.mkdir(exist_ok=True)
NAMES=['ready','windup','strike','recovery']
CELL=(190,260)
DIRECTIONS=['front','front-left','left-profile','rear-left','back','rear-right','right-profile','front-right']

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

# One common scale per generated strip. Reviewed approximate head landmarks;
# foot baselines are measured per cell. Replacement strike has its own source
# resolution and an independently recorded, aspect-preserving body scale.
strips={
 1:('385-melee-front-left-cycle-v1.png',227,[(0,517),(517,1034),(1034,1551),(1551,2068)]),
 2:('385-melee-left-cycle-v1.png',213,[(0,520),(520,950),(950,1560),(1560,2079)]),
 3:('385-melee-rear-left-cycle-v1.png',228,[(0,500),(520,960),None,(1510,2067)]),
 4:('385-melee-back-cycle-v1.png',231,[(0,517),(517,1034),(1034,1570),(1570,2068)]),
 5:('385-melee-rear-right-cycle-v2.png',217,[(0,517),(517,1034),(1034,1590),(1590,2068)]),
 7:('385-melee-front-right-cycle-v1.png',211,[(0,517),(517,1034),(1034,1580),(1580,2069)]),
}

def feet_bounds(im):
    start=round(im.height*0.84)
    box=im.getchannel('A').crop((0,start,im.width,im.height)).point(lambda a:255 if a>=128 else 0).getbbox()
    if not box:raise ValueError('No opaque feet in lower body window')
    return ((box[0]+box[2])/2,box[3]+start)

atlas=Image.new('RGBA',(CELL[0]*4,CELL[1]*8))
pages=[Image.new('RGB',(760,1140),'#30343a') for _ in range(2)]
rows=[]
for direction in range(8):
    sources=[]
    if direction in (0,6):
        parent=ROOT/('candidate-melee-front-cycle' if direction==0 else 'candidate-melee-right-cycle')
        for name in NAMES:
            sources.append((parent/(name+'.png'),None,1.0))
    else:
        filename,head_y,boxes=strips[direction]
        raw=Image.open(ROOT/filename).convert('RGBA')
        first=raw.crop((boxes[0][0],0,boxes[0][1],raw.height))
        baseline=feet_bounds(first)[1]
        scale=163/(baseline-head_y)
        for phase,box in enumerate(boxes):
            if direction==3 and phase==2:
                path=ROOT/'385-melee-rear-left-key-strike-v2.png'
                replacement=Image.open(path).convert('RGBA')
                replacement_scale=163/(feet_bounds(replacement)[1]-492)
                sources.append((path,None,replacement_scale))
            else:
                sources.append((ROOT/filename,(box[0],0,box[1],raw.height),scale))
    frames=[]
    row_records=[]
    for column,(name,(source,box,scale)) in enumerate(zip(NAMES,sources)):
        raw=Image.open(source).convert('RGBA')
        cut=raw.crop(box) if box else raw
        foot_x,foot_y=feet_bounds(cut)
        resized=cut.resize((round(cut.width*scale),round(cut.height*scale)),Image.Resampling.NEAREST)
        offset=(round(95-foot_x*scale),round(244-foot_y*scale))
        frame=Image.new('RGBA',CELL);frame.paste(resized,offset)
        filename=f'{direction}-{DIRECTIONS[direction]}-{name}.png'
        path=OUT/filename;frame.save(path)
        atlas.paste(frame,(column*CELL[0],direction*CELL[1]))
        page=pages[direction//4];x=column*190;y=(direction%4)*285
        page.paste(frame,(x,y+25),frame)
        draw=ImageDraw.Draw(page)
        draw.text((x+5,y+5),f'{direction} {DIRECTIONS[direction]} / {name}',fill='white')
        for ly in (244,81):draw.line((x,y+25+ly,x+189,y+25+ly),fill='#5c6672')
        row_records.append({'phase':name,'path':filename,'sha256':sha(path),
                            'source':str(source.relative_to(ROOT)),'sourceSha256':sha(source),
                            'sourceRect':box,'scale':scale,'translation':offset,
                            'measuredSourceFootCenterX':foot_x,'measuredSourceFootBaseline':foot_y})
        bg=Image.new('RGB',CELL,'#30343a');bg.paste(frame,(0,0),frame);frames.append(bg)
    frames[0].save(OUT/f'{direction}-{DIRECTIONS[direction]}-preview.gif',save_all=True,
                   append_images=frames[1:],duration=[180,160,100,180],loop=0)
    rows.append({'direction':direction,'bearing':DIRECTIONS[direction],
                 'phaseCount':4,'weaponHand':'anatomical right','frames':row_records,
                 'review':'candidate reviewed at native size; no user/runtime acceptance',
                 'specificLimit':'Far right-arm grip partially occluded in strike/recovery; candidate needs final occlusion review.' if direction==3 else None})
atlas.save(OUT/'atlas.png')
for index,page in enumerate(pages):page.save(ROOT/f'melee-385-directions-{index*4}-{index*4+3}-review.png')
manifest={'schemaVersion':1,'archive':385,'action':'melee-one-handed-sword','status':'candidate-eight-direction-four-phase-set',
    'selectedUniquePoses':32,'cellSize':CELL,'atlas':'atlas.png','atlasSha256':sha(OUT/'atlas.png'),
    'processing':'Native-size nearest-neighbor packing. Per-strip shared scale; source-resolution-specific scale for replacement key. Measured foot center/baseline translation. Alpha and source RGB retained; no mirroring, silhouette erosion or palette substitution.',
    'nativeReference':{'nativeRecord':5,'nativePixelSize':[72,163],'nativeRecordScaleXY':[-134,-134],
       'nativeSizeBeforeOverridesGlobalScale':[35,78],
       'metadataSource':'art/standing/source/native-idle.csv; art/run/pilot-385/source/records.csv'},
    'worldSizePolicy':'Canvas and foot pivot are pixels only. Native record-scaled world size, TextureReplacement override and GlobalScale remain separate; no runtime size was set.',
    'pixelFootPivot':[95,244],
    'directions':rows,'states':{'candidate':True,'generated':True,'technically_checked':True,
      'visually_reviewed':True,'integrated':False,'user_approved':False},
    'limitations':['No runtime playback or user approval.','Minor palette/anatomical differences remain between generated views.',
      'Partial alpha/fringes retained.','Rear-left original strip strike rejected for switching hands; selected strike replaced with bound correction.',
      '385 rear-right v1 rear pouch removed in v2 after direct original native records3/4 inspection.',
      'This manifest covers one appearance only; broader coverage is tracked separately.']}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
assert len(rows)==8 and all(len(r['frames'])==4 for r in rows)
assert all(sha(OUT/f['path'])==f['sha256'] for r in rows for f in r['frames'])
print('Verified 8 directions / 32 selected phase PNG hashes. Candidate only.')
