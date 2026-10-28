"""Prepare native-resolution idle art and source-only inspection sheets."""
from pathlib import Path
from PIL import Image, ImageDraw
import argparse, json, shutil, hashlib

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT.parent / 'source'
NAMES = ['front-native','front-left','left-profile','rear-left','back','rear-right','right-profile','front-right']

def sources(archive):
    folder = ROOT / f'idle-{archive}'
    folder.mkdir(exist_ok=True)
    paths = [SOURCE/'native-idle'/f'{archive}-0.png'] + [SOURCE/f'{archive}-{r}-0.png' for r in range(1,5)]
    images = [Image.open(p).convert('RGBA') for p in paths]
    for record,im in enumerate(images[1:],1):
        im.crop((0,0,im.width,round(im.height*0.45))).save(folder/f'bearing-only-record-{record}.png')
    sheet = Image.new('RGB',(5*300,600),'#303238')
    draw = ImageDraw.Draw(sheet)
    for i,(im,path) in enumerate(zip(images,paths)):
        draw.text((i*300+8,8),f'{path.name} {im.width}x{im.height}',fill='white')
        scaled=im.resize((im.width*3,im.height*3),Image.Resampling.NEAREST)
        sheet.paste(scaled,(i*300+(300-scaled.width)//2,40),scaled)
    sheet.save(folder/'original-reference-review.png')

def prepare(archive):
    folder=ROOT/f'idle-{archive}'
    source=SOURCE/'native-idle'/f'{archive}-0.png'
    original=Image.open(source).convert('RGBA')
    im=Image.open(folder/'generated-sheet-v1.png').convert('RGBA')
    frames=[]
    details=[]
    for i,name in enumerate(NAMES):
        cell=(round(i%4*im.width/4),round(i//4*im.height/2),round((i%4+1)*im.width/4),round((i//4+1)*im.height/2))
        crop=im.crop(cell)
        override=folder/f'generated-view-{i}-correction.png'
        if override.exists():
            crop=Image.open(override).convert('RGBA')
        bounds=crop.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
        if not bounds:
            raise ValueError(f'{archive} view{i} empty')
        cut=crop.crop(bounds)
        width=round(cut.width*original.height/cut.height)
        native=cut.resize((width,original.height),Image.Resampling.NEAREST)
        file=f'{archive}-idle-view-{i}-{name}.png'
        if i==0:
            native=original
            shutil.copyfile(source,folder/file)
        else:
            native.save(folder/file)
        frames.append(native)
        details.append({'view':i,'bearing':name,'file':file,'native_size':list(native.size),'generated_cell':cell,'generated_alpha_bbox_in_cell':bounds,'generation_override':override.name if override.exists() else None})
    cell_width=max(f.width for f in frames)+4
    atlas=Image.new('RGBA',(cell_width*4,original.height*2))
    preview_width=cell_width*4+24
    preview_height=original.height*4+44
    preview=Image.new('RGB',(preview_width*4,preview_height*2),'#303238')
    draw=ImageDraw.Draw(preview)
    for i,(frame,name) in enumerate(zip(frames,NAMES)):
        atlas.paste(frame,(i%4*cell_width+(cell_width-frame.width)//2,i//4*original.height))
        x,y=i%4*preview_width,i//4*preview_height
        draw.text((x+8,y+8),f'{archive} / {i}: {name}',fill='white')
        enlarged=frame.resize((frame.width*4,frame.height*4),Image.Resampling.NEAREST)
        preview.paste(enlarged,(x+(preview_width-enlarged.width)//2,y+30),enlarged)
    atlas.save(folder/f'{archive}-idle-native-atlas-v1.png')
    preview.save(folder/f'{archive}-idle-review-4x-v1.png')
    meta={'archive':archive,'status':'Generated source art; individual user acceptance pending; pilot 391 style accepted','generator':'built-in imagegen','native_source':str(source),'native_source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'native_source_size':list(original.size),'generated_sheet_size':list(im.size),'atlas':f'{archive}-idle-native-atlas-v1.png','atlas_cell_size':[cell_width,original.height],'bearing_convention':'0 front; 1 front-left; 2 left profile; 3 rear-left; 4 back; 5 rear-right; 6 right profile; 7 front-right. Left/right describes nose projection in the image.','processing':'Alpha>=128 crop measurement; proportional nearest-neighbor reduction to original native idle height; natural per-view widths; transparent atlas padding; generated alpha retained. View0 copied byte-for-byte. No mirroring, palette substitution or hand-painted repairs.','frames':details}
    (folder/'manifest-v1.json').write_text(json.dumps(meta,indent=2),encoding='utf-8')
    print(f'{archive}: source={original.size}, frame widths={[f.width for f in frames]}, atlas={atlas.size}')

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('mode',choices=['sources','prepare'])
    parser.add_argument('archives',nargs='+',type=int)
    args=parser.parse_args()
    for archive in args.archives:
        (sources if args.mode=='sources' else prepare)(archive)
