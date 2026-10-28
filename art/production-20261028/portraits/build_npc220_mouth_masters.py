"""Fit independent NPC220 mouth masters onto its own source and derived eyes."""
from pathlib import Path
from PIL import Image,ImageDraw
import json,hashlib,shutil
from build_npc195_masters import compose
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc220-mouth-masters-v1';EYE=HERE/'exact-feature-reuse-wave2/common-220/manifest.json';BOX=((22,35,43,47),)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 em=json.loads(EYE.read_text());src=(EYE.parent/em['original']).resolve();original=Image.open(src).convert('RGB');c=Image.new('RGB',(6*256,2*280),'#222222');d=ImageDraw.Draw(c);rows=[];mouthroot=HERE/'npc220-mouths-v1';mouthroot.mkdir(exist_ok=True)
 for col,mood in enumerate(('neutral','happy','angry','sad','afraid','guarded')):
  full=original.copy();rawp=src
  if mood!='neutral':
   rawp=OUT/f'{mood}-raw.png';raw=Image.open(rawp).convert('RGB').resize((64,29),Image.Resampling.NEAREST);candidate=original.copy();candidate.paste(raw,(0,35));full,_=compose(original,candidate,BOX)
  lower=full.crop((0,35,64,64));target=OUT/f'{mood}-lower.png';lower.save(target);eye=next(s for s in em['eyeSets'] if s['id']==mood)['phases']['open'];eyep=(EYE.parent/eye['file']).resolve();full.paste(Image.open(eyep),(0,0))
  for row,(label,im) in enumerate((('own mouth + original eyes',original.copy()),('own mouth + derived mood eyes',full))):
   if row==0:im.paste(lower,(0,35))
   c.paste(im.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+3,row*280+5),mood+' '+label,fill='white')
  rows.append({'mood':mood,'file':str(target),'sha256':sha(target),'raw':str(rawp),'rawSha256':sha(rawp),'eye':str(eyep),'eyeSha256':sha(eyep)})
  o=mouthroot/mood;o.mkdir(exist_ok=True);shutil.copyfile(src,o/'original.png');lower.save(o/'source-lower.png');lower.resize((512,232),Image.Resampling.NEAREST).save(o/'mood-closed-reference.png');grid=Image.new('RGB',(192,87))
  for i in range(9):grid.paste(lower,((i%3)*64,(i//3)*29))
  grid.resize((1536,696),Image.Resampling.NEAREST).save(o/'mouth-reference-grid.png');(o/'layout.json').write_text(json.dumps({'portraitRecord':220,'bank':'TFAC00I0.RCI','width':64,'height':64,'splitY':35,'sourceSha256':sha(src),'mouthRegion':{'x':22,'y':35,'width':21,'height':17},'status':'Provisional envelope; tooth anchor requires neutral atlas inspection.'},indent=2))
 c.save(OUT/'masters-review.png');(OUT/'masters-manifest.json').write_text(json.dumps({'record':220,'source':str(src),'sourceSha256':sha(src),'mouthBoxes':BOX,'eyeManifest':str(EYE),'eyeManifestSha256':sha(EYE),'masters':rows,'status':'static-candidate-review-pending','limits':['Independent220 mouths only; no196 mouth art.','Source-exact neutral lower; derived eyes retain their donor limitations.','No temporal/AV/publication/user acceptance.']},indent=2))
if __name__=='__main__':main()

