"""Extract bounded native civilian reference records, never generated art."""
from pathlib import Path
import importlib.util
import hashlib
import json
import sys
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parent
decoder=ROOT.parent/'extract_native_sources.py'
spec=importlib.util.spec_from_file_location('native_decoder',decoder)
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
archive=int(sys.argv[1]);folder=ROOT/'native-reference'/str(archive);folder.mkdir(parents=True,exist_ok=True)
rows=[];contact=Image.new('RGB',(1000,640),'#30343a');draw=ImageDraw.Draw(contact)
for record in range(5):
    for frame in range(4):
        im,meta=module.decode(archive,record,frame)
        path=folder/f'{archive}-{record}-{frame}.png'
        if not path.exists():im.save(path)
        elif not module.compare_native_reference(im,Image.open(path)):raise ValueError('Existing source parity failure')
        meta['path']=str(path);meta['fileSha256']=hashlib.sha256(path.read_bytes()).hexdigest();rows.append(meta)
        big=im.resize((im.width*2,im.height*2),Image.Resampling.NEAREST)
        x=record*200;y=frame*160
        factor=min(1,130/big.height);small=big.resize((round(big.width*factor),round(big.height*factor)),Image.Resampling.NEAREST)
        contact.paste(small,(x+60,y+24),small);draw.text((x+4,y+4),f'{archive} rec{record} frame{frame}',fill='white')
contact.save(folder/'native-walk-contact.png')
(folder/'manifest.json').write_text(json.dumps({'archive':archive,'decoder':str(decoder),'decoderSha256':hashlib.sha256(decoder.read_bytes()).hexdigest(),'frames':rows},indent=2)+'\n')
print(f'Extracted {archive}: 5 native records, 20 original frames with hashes.')
