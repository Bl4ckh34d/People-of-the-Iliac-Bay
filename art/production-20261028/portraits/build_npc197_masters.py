"""NPC197 own native envelopes; reuse composition code, not NPC195 geometry."""
import hashlib,json
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
from build_npc195_masters import compose
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc197-masters-v1';SOURCE=HERE.parents[2]/'art/portraits/mobile-mouths/split-mask-prototype/catalog/common-197.png'
MOODS=('neutral','happy','angry','sad','afraid','guarded');EYES=((16,12,31,26),(33,12,47,26));MOUTH=((20,35,45,46),)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 original=Image.open(SOURCE).convert('RGB');sheet=Image.new('RGB',(6*256,3*280),'#242424');d=ImageDraw.Draw(sheet);detail=Image.new('RGB',(6*384,200),'#242424');dd=ImageDraw.Draw(detail);entries=[]
 for col,mood in enumerate(MOODS):
  if mood=='neutral':raw=original;full=original;target=SOURCE;rawp=SOURCE
  else:
   rawp=OUT/f'{mood}-raw.png';raw=Image.open(rawp).convert('RGB').resize((64,64),Image.Resampling.NEAREST);raw.save(OUT/f'{mood}-raw-native.png');full,_=compose(original,raw,EYES+MOUTH);target=OUT/f'{mood}-master.png';full.save(target)
  entries.append({'mood':mood,'file':str(target),'sha256':sha(target),'raw':str(rawp),'rawSha256':sha(rawp)})
  for row,(kind,im) in enumerate([('raw',raw),('composed',full),('eye only',compose(original,raw,EYES)[0])]):sheet.paste(im.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+3,row*280+5),mood+' '+kind,fill='white')
  detail.paste(full.crop((16,10,48,27)).resize((352,187),Image.Resampling.NEAREST),(col*384,13));dd.text((col*384+3,0),mood,fill='white')
  for y in range(10,27):dd.text((col*384+353,13+(y-10)*11),str(y),fill='white')
 sheet.save(OUT/'masters-review.png');detail.save(OUT/'eye-coordinate-review.png');(OUT/'masters-manifest.json').write_text(json.dumps({'portraitRecord':197,'bank':'TFAC00I0.RCI','source':str(SOURCE),'sourceSha256':sha(SOURCE),'masters':entries,'eyeBoxes':EYES,'mouthBoxes':MOUTH,'status':'native-review-pending','limitations':['Possible old-brow retention beneath raised brows needs native anatomy inspection.','No animation frames or publication.']},indent=2))
if __name__=='__main__':main()
