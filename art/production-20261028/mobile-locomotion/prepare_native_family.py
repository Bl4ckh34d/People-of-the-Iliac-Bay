"""Create bounded source-backed native motion references for one mobile family."""
from pathlib import Path
from PIL import Image
import hashlib
import importlib.util
import json
import sys

ROOT=Path(__file__).resolve().parent
archive=int(sys.argv[1])
OUT=ROOT/'families'/str(archive)
(OUT/'native-reference').mkdir(parents=True,exist_ok=True)
decoder_path=ROOT.parent/'extract_native_sources.py'
spec=importlib.util.spec_from_file_location('native_decoder',decoder_path)
decoder=importlib.util.module_from_spec(spec)
spec.loader.exec_module(decoder)
records=[0,1,2,3,4,3,2,1]
names=['front','front-left','left-profile','rear-left','back','rear-right','right-profile','front-right']
entries=[]
for bearing,record in enumerate(records):
    imgs=[]
    evidence=[]
    for frame in range(4):
        im,row=decoder.decode(archive,record,frame)
        if bearing>4:
            im=im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        imgs.append(im)
        evidence.append(row)
    w=max(im.width for im in imgs)+24
    h=max(im.height for im in imgs)+24
    strip=Image.new('RGBA',(w*4,h))
    for frame,im in enumerate(imgs):
        strip.paste(im,(w*frame+(w-im.width)//2,h-im.height-12))
    file=f'native-reference/view-{bearing}.png'
    strip.resize((strip.width*4,strip.height*4),Image.Resampling.NEAREST).save(OUT/file)
    entries.append({'bearing':bearing,'name':names[bearing],'record':record,'native_renderer_mirror':bearing>4,'file':file,'scale_for_reference':4,'frames':evidence})
(OUT/'native-reference.json').write_text(json.dumps({'archive':archive,'decoder':str(decoder_path),'decoder_sha256':hashlib.sha256(decoder_path.read_bytes()).hexdigest(),'references':entries},indent=2)+'\n')
print(f'Prepared eight native four-frame references for {archive}; no synthetic poses.')
