"""Whole-cell sampling and bounded opaque lip composition, no feature crop scaling."""
import hashlib,json,sys
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent;ROOT=HERE/'npc195-mouths-v1';SHAPES=('A','B','C','D','E','F','G','H','X')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main(mood):
 p=ROOT/mood;raw=Image.open(p/f'mouth/{mood}/raw.png').convert('RGB');source=Image.open(p/'original.png').convert('RGB');lower=source.crop((0,35,64,64));out=p/'candidate';out.mkdir(exist_ok=True)
 masters=json.loads((HERE/'npc195-masters-v1/masters-selected.json').read_text());master=Image.open(next(e['file'] for e in masters['masters'] if e['mood']==mood)).convert('RGB');rows=[]
 contact=Image.new('RGB',(9*192,2*216),'#242424');d=ImageDraw.Draw(contact)
 box=(21,0,45,17)
 anchor=raw.transform((64,29),Image.Transform.EXTENT,(raw.width/3,0,2*raw.width/3,raw.height/3),Image.Resampling.NEAREST)
 for index,shape in enumerate(SHAPES):
  row,col=divmod(index,3);bounds=(col*raw.width/3,row*raw.height/3,(col+1)*raw.width/3,(row+1)*raw.height/3)
  cell=raw.transform((64,29),Image.Transform.EXTENT,bounds,Image.Resampling.NEAREST);native=lower.copy()
  if mood=='neutral' and shape in ('A','X'):pass
  else:
   native.paste(cell.crop(box),box[:2])
   if mood!='neutral' and shape in ('D','H'):
    native.paste(anchor.crop((21,0,45,7)),(21,0))
  target=out/f'{shape}.png';native.save(target);rows.append({'shape':shape,'file':str(target),'sha256':sha(target),'wholeCellBounds':bounds})
  for y,(label,im) in enumerate([('raw',cell),('fit',native)]):
   full=master.copy();full.paste(im,(0,35));contact.paste(full.resize((192,192),Image.Resampling.NEAREST),(index*192,y*216+24));d.text((index*192+3,y*216+5),mood+' '+shape+' '+label,fill='white')
 contact.save(p/'review.png');(p/'manifest.json').write_text(json.dumps({'record':195,'mood':mood,'raw':str(p/f'mouth/{mood}/raw.png'),'rawSha256':sha(p/f'mouth/{mood}/raw.png'),'mouthBoxLowerCoordinates':box,'frames':rows,'status':'native-static-review-pending','poseSemanticsPass':None,'audiovisualPass':None,'upperAnchorPolicy':'D/H retain own B pixels through local row6 inclusive; generated downward opening below. Neutral selected overrides documented separately.','sampler':'fractional complete cell EXTENT nearest64x29; no tight feature resize'},indent=2))
if __name__=='__main__':
 if len(sys.argv)>2:ROOT=Path(sys.argv[2]).resolve()
 main(sys.argv[1])
