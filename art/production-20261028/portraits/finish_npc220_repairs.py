import json,hashlib,shutil
from pathlib import Path
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent; ROOT=HERE/'npc220-mouths-v1'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
p=ROOT/'happy'; receipt=json.loads((p/'E-v2-receipt.json').read_text());shutil.copyfile(receipt['rawSource'],p/'E-v2-raw.png');raw=Image.open(p/'E-v2-raw.png').convert('RGB').resize((64,29),Image.Resampling.NEAREST);im=Image.open(p/'original.png').convert('RGB').crop((0,35,64,64));im.paste(raw.crop((22,0,43,13)),(22,0));im.save(p/'E-v2-candidate.png');m=json.loads((p/'manifest.json').read_text());f=next(f for f in m['frames'] if f['shape']=='E');f.update(file=str(p/'E-v2-candidate.png'),sha256=sha(p/'E-v2-candidate.png'));m['selection']='E-v2 own round contour; larger opening than rejected puckered E-v1, upper tooth row4.';(p/'selected9-manifest.json').write_text(json.dumps(m,indent=2))
p=ROOT/'angry';m=json.loads((p/'manifest.json').read_text());original=Image.open(p/'original.png').convert('RGB').crop((0,35,64,64));repairs=[]
for shape in 'EF':
 before=Image.open(p/f'candidate/{shape}.png').convert('RGB');after=before.copy();after.paste(original.crop((22,0,43,1)),(22,0));target=p/f'{shape}-edge-v2.png';after.save(target);f=next(f for f in m['frames'] if f['shape']==shape);f.update(file=str(target),sha256=sha(target));repairs.append({'shape':shape,'replacedBoxLower':[22,0,43,1],'reason':'Generated turquoise atlas edge contaminates skin at full y35; restore immutable source row, above lip.'})
m['boundedAssemblyRepairs']=repairs;(p/'selected9-manifest.json').write_text(json.dumps(m,indent=2))
canvas=Image.new('RGB',(5*384,256),'#242424');d=ImageDraw.Draw(canvas)
for i,(label,path) in enumerate([('Happy C',ROOT/'happy/candidate/C.png'),('Happy E v1',ROOT/'happy/candidate/E.png'),('Happy E v2',ROOT/'happy/E-v2-candidate.png'),('Happy F',ROOT/'happy/candidate/F.png'),('Angry E edge v2',ROOT/'angry/E-edge-v2.png')]):
 canvas.paste(Image.open(path).resize((384,174),Image.Resampling.NEAREST),(i*384,40));d.text((i*384+4,8),label,fill='white')
canvas.save(ROOT/'bounded-repairs-review.png')
