"""Pack original generated sources into a source-traceable eight-bearing candidate."""
from pathlib import Path
from PIL import Image, ImageDraw
import hashlib
import json
import statistics

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'eight-view-candidate-v1'
(OUT/'frames').mkdir(parents=True,exist_ok=True)
NAMES=['front','front-left','left-profile','rear-left','back','rear-right','right-profile','front-right']
PHASES=['left-contact','right-flight','right-contact','left-flight']
W,H,GROUND=160,176,156
CACHE={}

def sheet(filename):
    """Find actual alpha gutters rather than assume an imperfect AI grid."""
    if filename in CACHE:
        return CACHE[filename]
    im=Image.open(ROOT/filename).convert('RGBA')
    mask=im.getchannel('A').point(lambda a:255 if a>=128 else 0)
    runs=[]
    begin=None
    for x in range(im.width+1):
        occupied=x<im.width and mask.crop((x,0,x+1,im.height)).getbbox() is not None
        if occupied and begin is None:
            begin=x
        elif not occupied and begin is not None:
            runs.append([begin,x])
            begin=None
    # Very small detached edge pixels may separate an otherwise continuous
    # figure projection. Merge only pixel-sized gaps, never whole sprite gaps.
    merged=[]
    for a,b in runs:
        if merged and a-merged[-1][1]<4:
            merged[-1][1]=b
        else:
            merged.append([a,b])
    assert len(merged)==4,(filename,merged)
    assert merged[0][0]>0 and merged[-1][1]<im.width,(filename,'source edge clipped')
    boundaries=[0]+[(merged[i][1]+merged[i+1][0])//2 for i in range(3)]+[im.width]
    result=[]
    for index in range(4):
        rect=[boundaries[index],0,boundaries[index+1],im.height]
        result.append((im.crop(rect),rect))
    CACHE[filename]=result
    return result

def single(filename):
    im=Image.open(ROOT/filename).convert('RGBA')
    return (im,[0,0,*im.size])

def S(filename,index):
    im,crop=sheet(filename)[index]
    return {'source':filename,'source_sheet_column':index,'image':im,'crop':crop}

def I(filename):
    im,crop=single(filename)
    return {'source':filename,'source_sheet_column':None,'image':im,'crop':crop}

sources=[
 [I('385-front-near-contact-source.png'),I('385-front-far-flight-v1.png'),I('385-front-far-contact-v1.png'),I('385-front-near-flight-source.png')],
 [S('385-front-left-cycle-v2.png',i) for i in range(4)],
 [I('385-left-near-contact-v1.png'),I('385-left-far-flight-v1.png'),I('385-left-far-contact-v1.png'),I('385-left-near-flight-v2.png')],
 [S('385-rear-left-cycle-v1.png',i) for i in range(4)],
 [I('385-back-left-contact-source.png'),I('385-back-right-flight-source.png'),I('385-back-right-contact-v1.png'),I('385-back-left-flight-v1.png')],
 [S('385-rear-right-cycle-v1.png',0),I('385-rear-right-right-flight-v3.png'),I('385-rear-right-right-contact-v3.png'),S('385-rear-right-cycle-v1.png',3)],
 [S('385-right-profile-cycle-v1.png',i) for i in range(4)],
 [I('385-front-right-left-contact-v2.png'),S('385-front-right-cycle-v1.png',1),S('385-front-right-cycle-v1.png',2),I('385-front-right-left-flight-v2.png')]
]
base_heights=[732,732,1377,732,724,732,732,732]
frames=[]
entries=[]
atlas=Image.new('RGBA',(W*4,H*8))
for bearing,items in enumerate(sources):
    for item in items:
        im=item['image']
        item['resolution_ratio']=im.height/base_heights[bearing]
        mask=im.getchannel('A').point(lambda a:255 if a>=128 else 0)
        item['bbox']=mask.getbbox()
        b=item['bbox']
        head=mask.crop((0,b[1],im.width,b[1]+max(6,round((b[3]-b[1])*0.10)))).getbbox()
        item['head_x']=(head[0]+head[2])/2
    contact_height=statistics.mean((items[i]['bbox'][3]-items[i]['bbox'][1])/items[i]['resolution_ratio'] for i in (0,2))
    bearing_scale=110/contact_height
    contact_head=GROUND-110
    for phase,item in enumerate(items):
        im=item['image'];b=item['bbox'];scale=bearing_scale/item['resolution_ratio']
        x=round(W/2-item['head_x']*scale)
        y=round(GROUND-b[3]*scale) if phase in (0,2) else round(contact_head-3-b[1]*scale)
        reduced=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.NEAREST)
        target=Image.new('RGBA',(W,H))
        target.alpha_composite(reduced,(x,y))
        cutout=target.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
        flight_adjustment=0
        if phase in (1,3) and cutout[3]>GROUND-3:
            flight_adjustment=cutout[3]-(GROUND-3)
            y-=flight_adjustment
            target=Image.new('RGBA',(W,H))
            target.alpha_composite(reduced,(x,y))
            cutout=target.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
        assert cutout and cutout[0]>0 and cutout[1]>0 and cutout[2]<W and cutout[3]<H,(bearing,phase,cutout)
        file=f'frames/view-{bearing}-frame-{phase}.png'
        target.save(OUT/file)
        atlas.paste(target,(phase*W,bearing*H))
        frames.append(target)
        entries.append({'bearing':bearing,'name':NAMES[bearing],'phase':phase,'phase_name':PHASES[phase],
          'file':file,'source':item['source'],'source_sha256':hashlib.sha256((ROOT/item['source']).read_bytes()).hexdigest(),
          'source_sheet_column':item['source_sheet_column'],'source_crop':item['crop'],'source_cutout_bounds':list(b),
          'source_resolution_ratio':item['resolution_ratio'],'bearing_common_scale':bearing_scale,'effective_scale':scale,
          'head_anchor_x':item['head_x'],'placement':[x,y],'cutout_bounds':list(cutout),
          'additional_flight_clearance_shift':flight_adjustment,
          'lowest_cutout_clearance':GROUND-cutout[3],'frame_pixel_sha256':hashlib.sha256(target.tobytes()).hexdigest(),
          'generated':True,'derived_mirrored':False,'technical_checked':True,'visual_reviewed':False,
          'integrated':False,'user_approved':False})
atlas.save(OUT/'atlas-8x4.png')
sheetimage=Image.new('RGB',(W*4+110,H*8+45),'#24282e')
d=ImageDraw.Draw(sheetimage)
d.text((8,7),'385 RUN: 8 bearings x 4 phases | REVIEW CANDIDATE',fill='white')
for phase,name in enumerate(PHASES):
    d.text((110+phase*W+8,27),name,fill='white')
for bearing in range(8):
    d.text((6,45+bearing*H+65),f'{bearing} {NAMES[bearing]}',fill='white')
    for phase in range(4):
        x,y=110+phase*W,45+bearing*H
        d.line((x,y+GROUND,x+W-1,y+GROUND),fill='#59616b')
        sheetimage.paste(frames[bearing*4+phase],(x,y),frames[bearing*4+phase])
sheetimage.save(OUT/'full-sheet.png')
for group in range(2):
    # Split review at native display size to keep every bearing inspectable.
    y=45+group*4*H
    part=Image.new('RGB',(sheetimage.width,4*H+45),'#24282e')
    part.paste(sheetimage.crop((0,0,sheetimage.width,45)),(0,0))
    part.paste(sheetimage.crop((0,y,sheetimage.width,y+4*H)),(0,45))
    part.save(OUT/f'review-{group}.png')
animated=[]
for phase in range(4):
    im=Image.new('RGB',(W*4,H*2+65),'#24282e')
    draw=ImageDraw.Draw(im)
    draw.text((8,6),f'385 RUN candidate | {PHASES[phase]} | 8 fps',fill='white')
    for bearing in range(8):
        x=(bearing%4)*W;y=30+(bearing//4)*(H+15)
        draw.text((x+4,y),f'{bearing} {NAMES[bearing]}',fill='white')
        im.paste(frames[bearing*4+phase],(x,y+15),frames[bearing*4+phase])
    animated.append(im.resize((im.width*2,im.height*2),Image.Resampling.NEAREST))
animated[0].save(OUT/'animated-preview.gif',save_all=True,append_images=animated[1:],duration=[120,130,120,130],loop=0,disposal=2)
assert len({e['frame_pixel_sha256'] for e in entries})==32
for entry in entries:
    im=Image.open(OUT/entry['file']).convert('RGBA')
    b,p=entry['bearing'],entry['phase']
    assert im.tobytes()==atlas.crop((p*W,b*H,(p+1)*W,(b+1)*H)).tobytes()
manifest={'archive':385,'status':'complete-eight-bearing-review-candidate-not-approved','date':'2026-10-28',
 'bearing_mapping':NAMES,'native_record_mapping':[0,1,2,3,4,3,2,1],
 'native_reference_mirrored':[False,False,False,False,False,True,True,True],
 'generated_output_mirrored':False,'frame_count':32,'phase_order':PHASES,'fps':8,'canvas':[W,H],
 'packing':'Actual alpha gutters for generated strips; resolution-normalized common per-bearing scale, head anchor, 110px mean contact body height. Contact feet use ground156; flight heads start3px above mean contact heads, with a further documented0-3px upward clearance shift where needed. Alpha retained; no palette edit or limb transformation. Every phase still comes from its own distinct generated pose.',
 'generated':True,'technical_checked':True,'visual_reviewed':False,'integrated':False,'user_approved':False,
 'frames':entries,'known_limits':['Source-conditioned model outputs still vary in palette, body detail and sleeve folds.',
 'Near/far thigh depth is partly obscured by long tunic; native game animation acceptance remains open.',
 'A technical atlas is not a validated runtime integration. No other appearance family is completed.']}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'frames':32,'unique_frames':32,'bearings':8,'atlas_cells_equal':True,'cutout_clipping':False,'mirrored_outputs':0,
 'flight_clearance':[e['lowest_cutout_clearance'] for e in entries if e['phase'] in (1,3)]},indent=2))
