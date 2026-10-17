"""Deterministic runtime packing of generated artwork; does not synthesize poses."""
from pathlib import Path
import json
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
source = Image.open(ROOT / 'generated-run-v2.png').convert('RGBA')
cells = []
# Imagegen did not obey equal row spacing. Verified transparent gutters separate
# these authored rows; do not slice through feet or the following row's heads.
row_edges = [0,298,567,838,1105,1402]
for row in range(5):
    for frame in range(4):
        rect = (round(frame*source.width/4), row_edges[row],
                round((frame+1)*source.width/4), row_edges[row+1])
        cell = source.crop(rect)
        alpha = cell.getchannel('A').point(lambda a: 255 if a >= 128 else 0)
        cell.putalpha(alpha)
        cells.append(cell.crop(alpha.getbbox()))
# One common scale avoids stretching bodies or independently scaling frames.
factor = 106 / max(cell.height for cell in cells)
atlas = Image.new('RGBA', (384,640))
metadata = []
for i, cell in enumerate(cells):
    row,frame = divmod(i,4)
    cell = cell.resize((round(cell.width*factor), round(cell.height*factor)), Image.Resampling.NEAREST)
    x = (96-cell.width)//2
    y = 120-cell.height-(3 if frame%2 else 0)
    atlas.alpha_composite(cell, (frame*96+x,row*128+y))
    metadata.append(dict(record=row,frame=frame,rect=[frame*96,row*128,96,128],
                         content=[x,y,cell.width,cell.height]))
atlas.save(ROOT/'run-385-atlas.png')
preview = Image.new('RGBA',atlas.size,(82,82,82,255)); preview.alpha_composite(atlas)
preview.resize((768,1280),Image.Resampling.NEAREST).save(ROOT/'run-385-preview.png')
animation=[]
for frame in range(4):
    canvas=Image.new('RGBA',(5*96,128),(82,82,82,255))
    for row in range(5):
        canvas.alpha_composite(atlas.crop((frame*96,row*128,(frame+1)*96,(row+1)*128)),(row*96,0))
    animation.append(canvas.resize((960,256),Image.Resampling.NEAREST))
animation[0].save(ROOT/'run-385-preview.gif',save_all=True,append_images=animation[1:],duration=100,loop=0,disposal=2)
manifest=dict(archive=385,race='Breton',gender='Male',variant=0,status='pilot; gameplay acceptance pending',
    authoredRecords=[0,1,2,3,4],orientationRecords=[0,1,2,3,4,3,2,1],
    orientationMirrored=[False,False,False,False,False,True,True,True],
    views=['front','front-left','left','rear-left','rear'],framesPerView=4,framesPerSecond=10,
    cycleSeconds=0.4,atlas='run-385-atlas.png',atlasSize=[384,640],cellSize=[96,128],
    sourceSizes=[[52,110],[65,110],[71,110],[64,110],[52,109]],sourceScale=[-70,-70],
    pixelGroundPivot=[48,120],alpha='binary threshold128; generated alpha preserved above threshold',
    filtering='Point',mipmaps=False,uvOrigin='bottom-left in Unity; manifest rects top-left',frames=metadata)
(ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
assert len(metadata)==20 and all(c['content'][0]>=0 and c['content'][1]>=0 for c in metadata)
assert set(atlas.getchannel('A').getdata())=={0,255}
print('RUN_PILOT_PACK_PASS: 20 frames; 5 authored views; 8 runtime orientations; 384x640 RGBA')
