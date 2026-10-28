"""Export generated sitting sheets without repainting or recomposing body parts."""
import argparse, csv, hashlib, json
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
VIEWS = ['front','front-left','left','rear-left','rear','rear-right','right','front-right']

def export(archive):
    folder = ROOT / str(archive)
    source = Image.open(folder / 'source-idle.png').convert('RGBA')
    raw = Image.open(folder / 'generated.png').convert('RGBA')
    width, height = source.size
    with (ROOT.parent/'standing'/'source'/'native-idle.csv').open(newline='') as native_file:
        native_record = next(row for row in csv.DictReader(native_file) if int(row['archive']) == archive and int(row['frame']) == 0)
    target_height = round(height * .70)
    cells = []
    for i in range(8):
        col, row = i % 4, i // 4
        rect = (col*raw.width//4,row*raw.height//2,(col+1)*raw.width//4,(row+1)*raw.height//2)
        cell = raw.crop(rect)
        cell.putalpha(cell.getchannel('A').point(lambda a: 255 if a >= 128 else 0))
        bounds = cell.getchannel('A').getbbox()
        if not bounds:
            raise ValueError(f'{archive} view {i}: empty alpha')
        cells.append(cell.crop(bounds))
    # Uniform scaling across all views preserves generated proportions.
    factor = min(target_height / max(c.height for c in cells), (width-2) / max(c.width for c in cells))
    cells = [c.resize((max(1,round(c.width*factor)),max(1,round(c.height*factor))),Image.Resampling.NEAREST) for c in cells]
    canvas_width = width
    atlas = Image.new('RGBA',(canvas_width*4,height*2))
    frames=[]
    for i,cell in enumerate(cells):
        x=(canvas_width-cell.width)//2; y=height-1-cell.height
        frame=Image.new('RGBA',(canvas_width,height)); frame.alpha_composite(cell,(x,y))
        # Clear hidden RGB so every transparent texel is exactly zero.
        frame.putdata([(r,g,b,a) if a else (0,0,0,0) for r,g,b,a in frame.getdata()])
        filename=f'view-{i}-{VIEWS[i]}.png'; frame.save(folder/filename)
        atlas.alpha_composite(frame,((i%4)*canvas_width,(i//4)*height))
        # In profile, pelvis lies behind the knees rather than at silhouette centre.
        pelvis_x_ratio=[.5,.60,.82,.65,.5,.35,.18,.40][i]
        frames.append({'orientation':i,'view':VIEWS[i],'file':filename,'rect':[i%4*canvas_width,i//4*height,canvas_width,height],'content':[x,y,cell.width,cell.height],'groundPivot':[canvas_width/2,height-1],'seatPivotEstimate':[x+round(cell.width*pelvis_x_ratio),y+round(cell.height*.58)]})
    atlas.save(folder/'atlas.png')
    preview=Image.new('RGB',atlas.size,(70,70,70)); preview.paste(atlas,mask=atlas.getchannel('A')); preview.save(folder/'preview-native.png')
    preview.resize((preview.width*3,preview.height*3),Image.Resampling.NEAREST).save(folder/'preview-3x.png')
    manifest={'archive':archive,'status':'exported; pilot style user-approved; per-appearance visual review pending','nativeIdleSize':[width,height],'cellSize':[canvas_width,height],'atlasSize':list(atlas.size),'atlas':'atlas.png','framesPerDirection':1,'animationLoop':False,'alpha':'binary threshold128; transparent RGB cleared','resampling':'nearest-neighbour; uniform scale across views','seatPivotStatus':'estimated from silhouette; runtime seat alignment unverified','frames':frames,'generatedSHA256':hashlib.sha256((folder/'generated.png').read_bytes()).hexdigest()}
    manifest['nativeIdleScale'] = [int(native_record['scaleX']),int(native_record['scaleY'])]
    (folder/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    for f in frames:
        im=Image.open(folder/f['file']); assert im.size==(canvas_width,height); assert set(im.getchannel('A').getdata())=={0,255}
    print(f'{archive}: 8 frames, {canvas_width}x{height}, binary alpha PASS')

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('archives',nargs='+',type=int)
    for archive in parser.parse_args().archives: export(archive)
