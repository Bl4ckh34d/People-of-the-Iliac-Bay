"""Deterministic export only: cell crops, proportional nearest-neighbor sizing, previews."""
from pathlib import Path
import hashlib
import json
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent
ART = OUT.parents[1]
NAMES = ['front-native', 'front-left', 'left-profile', 'rear-left', 'back', 'rear-right', 'right-profile', 'front-right']

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def cell_bounds(im, cell):
    x,y = cell%4,cell//4
    return (round(x*im.width/4),round(y*im.height/2),round((x+1)*im.width/4),round((y+1)*im.height/2))

def cutout_preview(im):
    out = im.copy()
    out.putalpha(im.getchannel('A').point(lambda a:255 if a>=128 else 0))
    return out

native = Image.open(OUT / 'sources/10015_0-0.png').convert('RGBA')
sheet = Image.open(OUT / 'det-scholar-10015-0-idle-eight-v2.png').convert('RGBA')
idle_dir = OUT / 'idle-det-10015-0'
idle_dir.mkdir(exist_ok=True)
frames, rows = [], []
for cell,name in enumerate(NAMES):
    bounds = cell_bounds(sheet, cell)
    part = sheet.crop(bounds)
    bbox = part.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert bbox, cell
    if cell == 0:
        frame = native.copy()
        path = idle_dir / ('view-0-' + name + '.png')
        path.write_bytes((OUT / 'sources/10015_0-0.png').read_bytes())
    else:
        crop = part.crop(bbox)
        frame = crop.resize((round(crop.width*native.height/crop.height),native.height),Image.Resampling.NEAREST)
        path = idle_dir / ('view-' + str(cell) + '-' + name + '.png')
        frame.save(path)
    rows.append({'view':cell,'bearing':name,'path':str(path),'sha256':sha(path),'nativeSize':list(frame.size),'sourceCellBounds':bounds,'sourceAlpha128Bounds':bbox})
    frames.append(frame)
cw,ch = max(x.width for x in frames),native.height
atlas = Image.new('RGBA',(cw*4,ch*2))
for i,frame in enumerate(frames):
    atlas.paste(frame,(i%4*cw+(cw-frame.width)//2,i//4*ch))
atlas.save(idle_dir / 'atlas.png')
review = Image.new('RGB',(9*150,325),'#555960')
draw = ImageDraw.Draw(review)
for i,frame in enumerate([native]+frames):
    enlarged = cutout_preview(frame).resize((frame.width*2,frame.height*2),Image.Resampling.NEAREST)
    review.paste(enlarged,(i*150+(150-enlarged.width)//2,45),enlarged)
    draw.text((i*150+5,10),'SOURCE' if i==0 else NAMES[i-1],fill='white')
review.save(OUT / 'idle-det-10015-0-native-review.png')
idle = {'provider':'daggerfall expanded textures','providerGuid':'00272292-acc6-4e51-ba4c-3a3e89199609','archive':10015,'record':0,'source':str(OUT/'sources/10015_0-0.png'),'sourceSha256':sha(OUT/'sources/10015_0-0.png'),'nativeFrontByteIdentical':sha(OUT/'sources/10015_0-0.png')==sha(Path(rows[0]['path'])),'frames':rows,'atlasCellSize':[cw,ch],'generated':True,'technicalChecked':True,'visuallyChecked':False,'integrated':False,'userApproved':False,'processing':'Native front copied byte-for-byte. Other cells cropped at alpha>=128 bounds; aspect-preserving nearest-neighbor reduction to source126-pixel height. Original alpha retained. No painting, mirroring, palette substitution or alpha cleanup.'}
(idle_dir/'manifest.json').write_text(json.dumps(idle,indent=2),encoding='utf-8')

sleep = Image.open(OUT / 'det-scholar-sleep-pilot-v2.png').convert('RGBA')
sleep_dir = OUT/'sleep-det-10015'
sleep_dir.mkdir(exist_ok=True)
sleep_rows = []
sleep_preview = Image.new('RGB',(1350,560),'#555960')
draw = ImageDraw.Draw(sleep_preview)
for cell in range(8):
    part = sleep.crop(cell_bounds(sleep,cell))
    bbox = part.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    if cell>=5:
        assert bbox is None, (cell,'Unexpected extra sleeper')
        continue
    assert bbox, cell
    cut = part.crop(bbox)
    path = sleep_dir/('10015-'+str(cell)+'-sleep.png')
    cut.save(path)
    sleep_rows.append({'archive':10015,'record':cell,'cell':cell,'path':str(path),'size':list(cut.size),'sha256':sha(path),'alphaExtrema':list(cut.getchannel('A').getextrema()),'sourceCellBounds':cell_bounds(sleep,cell),'sourceAlpha128Bounds':bbox,'integrated':False,'userApproved':False})
    preview = cutout_preview(cut)
    sleep_preview.paste(preview,(cell*270+(270-preview.width)//2,45),preview)
    draw.text((cell*270+10,10),'DET10015/'+str(cell),fill='white')
sleep_preview.save(OUT/'sleep-det-10015-cutout-review.png')
(sleep_dir/'manifest.json').write_text(json.dumps({'provider':'daggerfall expanded textures','providerGuid':'00272292-acc6-4e51-ba4c-3a3e89199609','generated':True,'technicalChecked':True,'visuallyChecked':False,'integrated':False,'userApproved':False,'sourceAtlas':str(OUT/'det-scholar-sleep-pilot-v2.png'),'sourceAtlasSha256':sha(OUT/'det-scholar-sleep-pilot-v2.png'),'entries':sleep_rows,'processing':'Rectangular per-cell crop at alpha>=128 bounds; original generated alpha retained. Preview alone displays native renderer cutoff128. No resize or manual painting.'},indent=2),encoding='utf-8')

mobile_root = ART/'standing/mobile'
coverage = json.loads((mobile_root/'idle-coverage.json').read_text())
checks = {'appearances':0,'views':0,'nativeFrontByteMatches':0,'nativeHeightMatches':0,'atlasPixelMatches':0}
evidence = []
for item in coverage['appearances']:
    folder = mobile_root/item['folder']
    original = ART/'standing/source/native-idle'/f"{item['archive']}-0.png"
    original_height = Image.open(original).height
    atlas = Image.open(mobile_root/item['atlas']).convert('RGBA')
    cw,ch = item['atlas_cell_size']
    for entry in item['frames']:
        path = mobile_root/entry['file']
        im = Image.open(path).convert('RGBA')
        view = entry['view']
        assert im.height==original_height
        x,y = view%4*cw+(cw-im.width)//2,view//4*ch
        assert atlas.crop((x,y,x+im.width,y+im.height)).tobytes()==im.tobytes()
        if view==0:
            assert path.read_bytes()==original.read_bytes()
            checks['nativeFrontByteMatches']+=1
        checks['views']+=1
        checks['nativeHeightMatches']+=1
        checks['atlasPixelMatches']+=1
        evidence.append({'archive':item['archive'],'view':view,'path':str(path),'sha256':sha(path)})
    checks['appearances']+=1
(OUT/'existing-mobile-idle-verification.json').write_text(json.dumps({'checks':checks,'freshReadOnlyVerification':True,'frames':evidence,'visualReview':'Existing source catalog inspected separately; no new all192-view visual acceptance claimed.'},indent=2),encoding='utf-8')
print(json.dumps({'newIdleViews':len(frames),'newSleepCandidates':len(sleep_rows),'existingMobileChecks':checks,'originalFrontIdentical':idle['nativeFrontByteIdentical']}))
