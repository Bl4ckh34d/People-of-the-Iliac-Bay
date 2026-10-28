"""NPC425 source-specific lower lip and upper teeth geometry."""
import json,hashlib,sys
from pathlib import Path
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent;ROOT=HERE/'npc425-mouths-v1';SHAPES='ABCDEFGHX';BOX=(20,4,45,17)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main(mood):
 p=ROOT/(sys.argv[2] if len(sys.argv)>2 else mood);rawp=p/f'mouth/{mood}/raw.png';raw=Image.open(rawp).convert('RGB');src=Image.open(p/'original.png').convert('RGB');lower=src.crop((0,35,64,64));out=p/'candidate';out.mkdir(exist_ok=True);mm=json.loads((HERE/'npc425-mouth-masters-v1/masters-manifest.json').read_text());entry=next(e for e in mm['masters'] if e['mood']==mood);master=src.copy();master.paste(Image.open(entry['eye']),(0,0));cells={};bounds={}
 for i,s in enumerate(SHAPES):
  row,col=divmod(i,3);bounds[s]=(col*raw.width/3,row*raw.height/3,(col+1)*raw.width/3,(row+1)*raw.height/3);cells[s]=raw.transform((64,29),Image.Transform.EXTENT,bounds[s],Image.Resampling.NEAREST)
 c=Image.new('RGB',(9*192,2*216),'#242424');d=ImageDraw.Draw(c);rows=[]
 for i,s in enumerate(SHAPES):
  im=lower.copy()
  if not (mood=='neutral' and s in 'AX'):
   im.paste(cells[s].crop(BOX),BOX[:2])
   if s in 'CDH':im.paste(cells['B'].crop((20,4,45,9)),(20,4))
  target=out/f'{s}.png';im.save(target);im.resize((512,232),Image.Resampling.NEAREST).save(p/f'{s}-reference.png');rows.append({'shape':s,'file':str(target),'sha256':sha(target),'wholeCellBounds':bounds[s]})
  for y,(name,half) in enumerate([('raw',cells[s]),('fit',im)]):
   full=master.copy();full.paste(half,(0,35));c.paste(full.resize((192,192),Image.Resampling.NEAREST),(i*192,y*216+24));d.text((i*192+3,y*216+5),mood+' '+s+' '+name,fill='white')
 c.save(p/'review.png');(p/'manifest.json').write_text(json.dumps({'record':425,'mood':mood,'raw':str(rawp),'rawSha256':sha(rawp),'frames':rows,'mouthBoxFullCoordinates':[20,39,45,52],'upperToothAnchor':'C/D/H keep own B through localrow8/full43; E/G preserve own upper contours and require native inspection.','sampler':'fractional whole atlas cell EXTENT nearest64x29','status':'native-static-review-pending','poseSemanticsPass':None,'audiovisualPass':None,'limits':['E/G upper tooth anchor and F/V contact require bounded neutral review.']},indent=2))
if __name__=='__main__':main(sys.argv[1])
