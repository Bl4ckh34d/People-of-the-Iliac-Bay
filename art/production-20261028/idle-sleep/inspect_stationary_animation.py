"""Bounded original-frame extraction, using parent's verified decoder, not synthesis."""
from pathlib import Path
from PIL import Image,ImageDraw
import importlib.util,json,sys
out=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('native_decoder',out.parent/'extract_native_sources.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
archive,record=map(int,sys.argv[1:3])
first,meta=module.decode(archive,record,0)
frames=[];rows=[]
folder=out/'additional-native-frames';folder.mkdir(exist_ok=True)
for i in range(meta['recordFrameCount']):
    image,row=module.decode(archive,record,i)
    path=out.parent/'sources/native'/f'{archive}_{record}-0.png' if i==0 else folder/f'{archive}_{record}-{i}.png'
    if i:image.save(path)
    row['file']=str(path);rows.append(row);frames.append(image)
cw=max(x.width for x in frames)*4+24;ch=max(x.height for x in frames)*4+35
preview=Image.new('RGB',(cw*len(frames),ch),'#555960');d=ImageDraw.Draw(preview)
for i,frame in enumerate(frames):
    scaled=frame.resize((frame.width*4,frame.height*4),Image.Resampling.NEAREST)
    preview.paste(scaled,(i*cw+12,30),scaled);d.text((i*cw+12,8),f'{archive}/{record} frame{i}',fill='white')
preview.save(out/f'native-animation-{archive}-{record}-review.png')
(out/f'native-animation-{archive}-{record}.json').write_text(json.dumps(rows,indent=2))
print(json.dumps({'archive':archive,'record':record,'frames':len(frames)}))
