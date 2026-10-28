"""197 complete-cell mouth sampling with its own B tooth-anchor row6."""
import json,hashlib,sys
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent;ROOT=HERE/'npc197-mouths-v1';SHAPES='ABCDEFGHX';BOX=(20,0,45,16)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main(mood):
 p=ROOT/mood;rawp=p/f'mouth/{mood}/raw.png';raw=Image.open(rawp).convert('RGB');src=Image.open(p/'original.png').convert('RGB');lower=src.crop((0,35,64,64));out=p/'candidate';out.mkdir(exist_ok=True);masters=json.loads((HERE/'npc197-masters-v1/masters-manifest.json').read_text());master=Image.open(next(e['file'] for e in masters['masters'] if e['mood']==mood)).convert('RGB');cells={};bounds={}
 for i,s in enumerate(SHAPES):
  row,col=divmod(i,3);bounds[s]=(col*raw.width/3,row*raw.height/3,(col+1)*raw.width/3,(row+1)*raw.height/3);cells[s]=raw.transform((64,29),Image.Transform.EXTENT,bounds[s],Image.Resampling.NEAREST)
 c=Image.new('RGB',(9*192,2*216),'#242424');d=ImageDraw.Draw(c);rows=[]
 for i,s in enumerate(SHAPES):
  im=lower.copy()
  if not (mood=='neutral' and s in 'AX'):
   im.paste(cells[s].crop(BOX),BOX[:2])
   if s in 'DH':im.paste(cells['B'].crop((20,0,45,7)),(20,0))
  target=out/f'{s}.png';im.save(target);rows.append({'shape':s,'file':str(target),'sha256':sha(target),'wholeCellBounds':bounds[s]})
  for y,(name,half) in enumerate([('raw',cells[s]),('fit',im)]):
   full=master.copy();full.paste(half,(0,35));c.paste(full.resize((192,192),Image.Resampling.NEAREST),(i*192,y*216+24));d.text((i*192+3,y*216+5),mood+' '+s+' '+name,fill='white')
 c.save(p/'review.png');(p/'manifest.json').write_text(json.dumps({'record':197,'mood':mood,'raw':str(rawp),'rawSha256':sha(rawp),'frames':rows,'mouthBoxFullCoordinates':[20,35,45,51],'upperToothAnchor':'D/H keep own B through localrow6/full41; only lower opening changes.','sampler':'fractional whole atlas cell EXTENT nearest64x29','status':'native-static-review-pending','poseSemanticsPass':None,'audiovisualPass':None,'limits':['E/G upper-lip occlusion differs from wide shapes; oral contact semantics need playback.']},indent=2))
if __name__=='__main__':main(sys.argv[1])
