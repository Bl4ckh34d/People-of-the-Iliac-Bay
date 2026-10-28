import json,hashlib
from pathlib import Path
from PIL import Image,ImageDraw
P=Path(__file__).resolve().parent/'npc425-mouths-v1/neutral'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
g=Image.open(P/'G-v3-candidate.png').convert('RGB');b=Image.open(P/'candidate/B.png').convert('RGB');g.paste(b.crop((20,4,45,9)),(20,4));g.save(P/'G-v3-anchored.png');m=json.loads((P/'manifest.json').read_text())
for s,file in [('E','E-v3-candidate.png'),('G','G-v3-anchored.png')]:
 f=next(f for f in m['frames'] if f['shape']==s);f.update(file=str(P/file),sha256=sha(P/file))
m['selection']={'E':'v3 generated directly from B; own narrowed/round contour and tooth row8. v1/v2 upperteeth one row high rejected.','G':'v3 raw tooth stripe disappears in nearest native sampling. Matching broad upper contour replaced by own B rows4..8; generated G lowerlip remains atrow9 touching teeth. No foreign art or rescaling of feature crop.'};m['limits']=['E round opening slightly tall, G/FV clarity still requires independent native and AV review.'];(P/'selected9-manifest.json').write_text(json.dumps(m,indent=2));grid=Image.new('RGB',(192,87));canvas=Image.new('RGB',(1728,216),'#222222');draw=ImageDraw.Draw(canvas);src=Image.open(P/'original.png').convert('RGB')
for i,f in enumerate(m['frames']):
 im=Image.open(f['file']);grid.paste(im,((i%3)*64,(i//3)*29));full=src.copy();full.paste(im,(0,35));canvas.paste(full.resize((192,192),Image.Resampling.NEAREST),(i*192,24));draw.text((i*192+3,5),f['shape'],fill='white')
grid.resize((1536,696),Image.Resampling.NEAREST).save(P/'selected9-reference-grid.png');canvas.save(P/'selected9-review.png')
