"""Deterministic packing of reviewed source poses; no invented limb motion."""
from pathlib import Path
from PIL import Image, ImageDraw
import hashlib
import json
import statistics
import sys

ROOT=Path(__file__).resolve().parent
FAMILY=ROOT/'families'/sys.argv[1]
REV=sys.argv[2] if len(sys.argv)>2 else 'v1'
SELECTION=FAMILY/('source-selection.json' if REV=='v1' else f'source-selection-{REV}.json')
CONFIG=json.loads(SELECTION.read_text())
OUT=FAMILY/f'candidate-{REV}'
(OUT/'frames').mkdir(parents=True,exist_ok=True)
NAMES=['front','front-left','left-profile','rear-left','back','rear-right','right-profile','front-right']
PHASES=['left-contact','right-flight','right-contact','left-flight']
W,H,GROUND=160,176,156
CACHE={}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def bounds(im):
    return im.getchannel('A').point(lambda value:255 if value>=128 else 0).getbbox()

def get_source(item):
    path=FAMILY/item['file']
    im=Image.open(path).convert('RGBA')
    rect=[0,0,*im.size]
    if 'column' in item:
        if item['file'] not in CACHE:
            mask=im.getchannel('A').point(lambda value:255 if value>=128 else 0)
            runs=[];start=None
            for x in range(im.width+1):
                occupied=x<im.width and mask.crop((x,0,x+1,im.height)).getbbox() is not None
                if occupied and start is None:start=x
                if not occupied and start is not None:
                    if runs and start-runs[-1][1]<4:runs[-1][1]=x
                    else:runs.append([start,x])
                    start=None
            assert len(runs)==4,(path,runs)
            assert runs[0][0]>0 and runs[-1][1]<im.width,(path,'source clipping')
            CACHE[item['file']]=[0]+[(runs[i][1]+runs[i+1][0])//2 for i in range(3)]+[im.width]
        edge=CACHE[item['file']];col=item['column']
        rect=[edge[col],0,edge[col+1],im.height]
        im=im.crop(rect)
    b=bounds(im);assert b
    head=im.getchannel('A').point(lambda value:255 if value>=128 else 0).crop((0,b[1],im.width,b[1]+max(6,round((b[3]-b[1])*.10)))).getbbox()
    ratio=item.get('resolution_ratio',im.height/724)
    assert ratio>0
    return {'image':im,'crop':rect,'bounds':b,'head_x':(head[0]+head[2])/2,'ratio':ratio,'source':item,'source_sha256':digest(path)}

frames={};entries=[]
for bearing,selection in enumerate(CONFIG['generated_views']):
    sources=[get_source(item) for item in selection]
    contact_height=statistics.mean((sources[i]['bounds'][3]-sources[i]['bounds'][1])/sources[i]['ratio'] for i in (0,2))
    common_scale=CONFIG['contact_pixel_height']/contact_height
    for phase,item in enumerate(sources):
        im=item['image'];b=item['bounds'];scale=common_scale/item['ratio']
        x=round(W/2-item['head_x']*scale)
        y=round(GROUND-b[3]*scale) if phase in (0,2) else round(GROUND-CONFIG['contact_pixel_height']-3-b[1]*scale)
        reduced=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.NEAREST)
        target=Image.new('RGBA',(W,H));target.alpha_composite(reduced,(x,y))
        cutout=bounds(target);flight_adjust=0
        if phase in (1,3) and cutout[3]>GROUND-3:
            flight_adjust=cutout[3]-(GROUND-3);y-=flight_adjust
            target=Image.new('RGBA',(W,H));target.alpha_composite(reduced,(x,y));cutout=bounds(target)
        assert cutout and 0<cutout[0]<cutout[2]<W and 0<cutout[1]<cutout[3]<H
        frames[bearing,phase]=target
        entries.append({'bearing':bearing,'name':NAMES[bearing],'phase':phase,'phase_name':PHASES[phase],
            'source':item['source'],'source_sha256':item['source_sha256'],'source_crop':item['crop'],
            'source_bounds':list(b),'source_resolution_ratio':item['ratio'],'bearing_common_scale':common_scale,
            'effective_scale':scale,'placement':[x,y],'additional_flight_clearance_shift':flight_adjust,
            'generated':True,'derived_mirrored':False})
for bearing,source_bearing in [(5,3),(6,2),(7,1)]:
    for phase in range(4):
        frames[bearing,phase]=frames[source_bearing,phase].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        entries.append({'bearing':bearing,'name':NAMES[bearing],'phase':phase,'phase_name':PHASES[phase],
            'generated':False,'derived_mirrored':True,'derived_from':{'bearing':source_bearing,'phase':phase},
            'derivation':'Exact horizontal reflection of entire packed frame, matching native mobile renderer record mapping.'})
atlas=Image.new('RGBA',(W*4,H*8))
for entry in entries:
    b,p=entry['bearing'],entry['phase'];im=frames[b,p]
    file=f'frames/view-{b}-frame-{p}.png';im.save(OUT/file);atlas.paste(im,(p*W,b*H))
    cutout=bounds(im)
    entry.update({'file':file,'frame_pixel_sha256':hashlib.sha256(im.tobytes()).hexdigest(),'cutout_bounds':list(cutout),
        'lowest_cutout_clearance':GROUND-cutout[3],'technical_checked':True,'visual_reviewed':False,'integrated':False,'user_approved':False})
atlas.save(OUT/'atlas-8x4.png')
for group in range(2):
    page=Image.new('RGB',(750,749),'#24282e');draw=ImageDraw.Draw(page)
    draw.text((8,7),f'{CONFIG["archive"]} RUN candidate | 20 generated poses + 12 native-rule mirrors',fill='white')
    for p,name in enumerate(PHASES):draw.text((110+p*W+8,27),name,fill='white')
    for row in range(4):
        b=group*4+row
        draw.text((5,45+row*H+65),f'{b} {NAMES[b]}',fill='white')
        if b>4:draw.text((5,45+row*H+80),'derived mirror',fill='#ffc77e')
        for p in range(4):
            x,y=110+p*W,45+row*H
            draw.line((x,y+GROUND,x+W-1,y+GROUND),fill='#59616b')
            page.paste(frames[b,p],(x,y),frames[b,p])
    page.save(OUT/f'review-{group}.png')
animated=[]
for p in range(4):
    page=Image.new('RGB',(640,410),'#24282e');draw=ImageDraw.Draw(page)
    draw.text((8,6),f'{CONFIG["archive"]} RUN candidate | {PHASES[p]} | 8 fps',fill='white')
    for b in range(8):
        x=(b%4)*W;y=30+(b//4)*191
        draw.text((x+4,y),f'{b} {NAMES[b]}'+(' [mirror]' if b>4 else ''),fill='white')
        page.paste(frames[b,p],(x,y+15),frames[b,p])
    animated.append(page.resize((1280,820),Image.Resampling.NEAREST))
animated[0].save(OUT/'animated-preview.gif',save_all=True,append_images=animated[1:],duration=[120,130,120,130],loop=0,disposal=2)
assert len({e['frame_pixel_sha256'] for e in entries})==32
for e in entries:
    b,p=e['bearing'],e['phase']
    assert frames[b,p].tobytes()==atlas.crop((p*W,b*H,(p+1)*W,(b+1)*H)).tobytes()
native=json.loads((FAMILY/'native-reference.json').read_text())
world_height=native['references'][0]['frames'][0]['scaledSizeBeforeGlobalScaleAndOverrides'][1]*.025
pixel_world_scale=world_height/CONFIG['contact_pixel_height']
manifest={'archive':CONFIG['archive'],'status':'eight-bearing-review-candidate-not-approved','frame_count':32,
    'generated_pose_count':20,'derived_mirrored_frame_count':12,'native_record_mapping':[0,1,2,3,4,3,2,1],
    'bearing_names':NAMES,'phase_order':PHASES,'fps':8,'canvas':[W,H],'contact_pixel_height':CONFIG['contact_pixel_height'],
    'pivot_pixels_top_left':[80,156],'pivot_uv_bottom_left':[.5,20/176],'target_native_body_world_height':world_height,
    'proposed_units_per_pixel':pixel_world_scale,'proposed_padded_quad_world_size':[W*pixel_world_scale,H*pixel_world_scale],
    'world_scale_status':'Export proposal only; native GlobalScale=0.025, current texture override integration not applied.',
    'native_reference_manifest_sha256':digest(FAMILY/'native-reference.json'),'source_selection_sha256':digest(SELECTION),
    'packing':'Alpha preserved, actual alpha128 gutters, nearest-neighbor resolution-normalized common bearing scale and head anchor. Contacts ground156; flight head starts3px higher; extra clearance translations recorded. Distinct source poses, no limb transformations.',
    'generated':True,'technical_checked':True,'visual_reviewed':False,'integrated':False,'user_approved':False,
    'frames':entries,'known_limits':CONFIG['known_limits']}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps({'archive':CONFIG['archive'],'frames':32,'generated_poses':20,'derived_mirrors':12,'unique_frames':32,'atlas_cells_equal':True,'source_and_output_cutouts_unclipped':True,'world_body_height':world_height}))
