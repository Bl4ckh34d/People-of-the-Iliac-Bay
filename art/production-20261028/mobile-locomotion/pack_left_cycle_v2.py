"""Pack four independently generated poses without redraws, mirrors or palette edits."""
from pathlib import Path
from PIL import Image, ImageDraw
import hashlib
import json

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'left-cycle-v2'
OUT.mkdir(exist_ok=True)
# All source canvases and head-to-waist proportions match. One global scale
# preserves actual relative body size instead of normalizing each pose's bbox.
SCALE=0.096
W,H=144,152
GROUND=138
SPECS=[('near-contact','385-left-near-contact-v1.png','near foot supports, near arm back',0),
       ('far-flight','385-left-far-flight-v1.png','far knee rising, near arm forward',4),
       ('far-contact','385-left-far-contact-v1.png','far foot supports, near arm forward',0),
       ('near-flight','385-left-near-flight-v2.png','near knee rising, near arm back',4)]
atlas=Image.new('RGBA',(W*4,H))
sheet=Image.new('RGB',(W*4*2, H*2+85),'#24282e')
draw=ImageDraw.Draw(sheet)
frames=[]
records=[]
for index,(phase,name,anatomy,clearance) in enumerate(SPECS):
    src=Image.open(ROOT/name).convert('RGBA')
    mask=src.getchannel('A').point(lambda a:255 if a>=128 else 0)
    bbox=mask.getbbox()
    # Common master head centre is x=525, not whole body centre (limbs move).
    x=round(W/2-525*SCALE)
    y=round(GROUND-bbox[3]*SCALE-clearance)
    reduced=src.resize((round(src.width*SCALE),round(src.height*SCALE)),Image.Resampling.NEAREST)
    target=Image.new('RGBA',(W,H))
    target.alpha_composite(reduced,(x,y))
    outname=f'{index:02d}-{phase}.png'
    target.save(OUT/outname)
    atlas.paste(target,(index*W,0))
    frames.append(target)
    draw.text((index*W*2+6,10),phase,fill='white')
    sheet.paste(target.resize((W*2,H*2),Image.Resampling.NEAREST),(index*W*2,35),target.resize((W*2,H*2),Image.Resampling.NEAREST))
    draw.line((index*W*2,35+GROUND*2,(index+1)*W*2,35+GROUND*2),fill='#68707a')
    records.append({'index':index,'phase':phase,'file':outname,'source':name,'source_sha256':hashlib.sha256((ROOT/name).read_bytes()).hexdigest(),
                    'anatomical_intent':anatomy,'global_scale':SCALE,'placement':[x,y],
                    'cutout_source_bounds':list(bbox),'ground_y':GROUND,'flight_clearance':clearance,
                    'generated':True,'technical_checked':True,'visual_reviewed':False,'integrated':False,'user_approved':False})
atlas.save(OUT/'atlas.png')
draw.text((8,H*2+45),'Common scale and master head anchor. Alpha preserved. Contact/flight baseline placement only.',fill='white')
draw.text((8,H*2+63),'Left profile only; draft requires visual temporal review. Native palette match unapproved.',fill='white')
sheet.save(OUT/'contact-sheet.png')
animation=[]
for index,frame in enumerate(frames):
    view=Image.new('RGB',(W*3,H*3+40),'#24282e')
    d=ImageDraw.Draw(view)
    d.text((8,8),f'385 LEFT RUN: {SPECS[index][0]}',fill='white')
    big=frame.resize((W*3,H*3),Image.Resampling.NEAREST)
    view.paste(big,(0,30),big)
    d.line((0,30+GROUND*3,W*3,30+GROUND*3),fill='#68707a')
    animation.append(view)
animation[0].save(OUT/'preview.gif',save_all=True,append_images=animation[1:],duration=125,loop=0,disposal=2)
manifest={'appearance':385,'bearing':2,'view':'left profile','phase_order':[s[0] for s in SPECS],
          'frame_count':4,'fps':8,'canvas':[W,H],'atlas':'atlas.png','frames':records,
          'method':'Separate source-conditioned imagegen keyframes, then common nearest-neighbor scale and head anchor. No mirroring, no duplicate frame substitution, no palette edits.',
          'generated':True,'technical_checked':True,'visual_reviewed':False,'integrated':False,'user_approved':False,
          'limits':['Only bearing2 has this candidate cycle.','Source palette differs slightly across generations.','Head vertical travel differs between flight phases; inspect temporal bob.']}
for index,frame in enumerate(frames):
    assert frame.tobytes()==atlas.crop((index*W,0,(index+1)*W,H)).tobytes()
    assert frame.getchannel('A').getextrema()[0]==0
assert len(set(hashlib.sha256(f.tobytes()).hexdigest() for f in frames))==4
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print('PASS: 4 distinct sources/frames, common scale, alpha preserved, atlas cells exact.')
