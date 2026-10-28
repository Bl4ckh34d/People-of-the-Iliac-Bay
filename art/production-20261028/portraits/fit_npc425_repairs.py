import json,hashlib,shutil
from pathlib import Path
from PIL import Image,ImageDraw
P=Path(__file__).resolve().parent/'npc425-mouths-v1/neutral';src=Image.open(P/'original.png').convert('RGB').crop((0,35,64,64));canvas=Image.new('RGB',(6*384,300),'#222222');draw=ImageDraw.Draw(canvas)
for s in 'EG':
 receipt=json.loads((P/f'{s}-v2-receipt.json').read_text());shutil.copyfile(receipt['rawSource'],P/f'{s}-v2-raw.png');raw=Image.open(P/f'{s}-v2-raw.png').convert('RGB').resize((64,29),Image.Resampling.NEAREST);im=src.copy();im.paste(raw.crop((20,4,45,17)),(20,4));im.save(P/f'{s}-v2-candidate.png');im.resize((512,232),Image.Resampling.NEAREST).save(P/f'{s}-v2-reference.png')
for i,(label,file) in enumerate([('B','candidate/B.png'),('C','candidate/C.png'),('E v1','candidate/E.png'),('E v2','E-v2-candidate.png'),('G v1','candidate/G.png'),('G v2','G-v2-candidate.png')]):
 im=Image.open(P/file);canvas.paste(im.resize((384,174),Image.Resampling.NEAREST),(i*384,30));draw.text((i*384+4,5),label,fill='white');crop=im.crop((20,5,45,11)).resize((300,72),Image.Resampling.NEAREST);canvas.paste(crop,(i*384,220))
 for y in range(5,11):draw.text((i*384,220+(y-5)*12),str(y),fill='white')
canvas.save(P/'EG-v2-review.png')
