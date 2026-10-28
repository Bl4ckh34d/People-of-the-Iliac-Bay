"""Hood-limited visible-left brow; occluded side and native eyeball stay exact."""
from pathlib import Path
import json,hashlib,shutil
import numpy as np
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc046-masters-v1';SOURCE=HERE.parents[2]/'art/portraits/mobile-mouths/split-mask-prototype/catalog/common-046.png';MOODS=('neutral','happy','angry','sad','afraid','guarded')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 original=Image.open(SOURCE).convert('RGB');orig=np.array(original);mask=np.zeros((35,64),bool)
 for y,x0 in [(14,27),(15,26),(16,25),(17,24),(18,23),(19,22),(20,21),(21,20)]:mask[y,x0:32]=True
 Image.fromarray((mask*255).astype('uint8')).save(OUT/'brow-mask.png');contact=Image.new('RGB',(1536,560),'#242424');d=ImageDraw.Draw(contact);rows=[];EYE=HERE/'npc046-eyes-v1';EYE.mkdir(exist_ok=True)
 for col,mood in enumerate(MOODS):
  full=original.copy();raw=original.crop((0,0,64,35));rawp=SOURCE
  if mood!='neutral':
   receipt=json.loads((OUT/f'{mood}-receipt.json').read_text());rawp=OUT/f'{mood}-raw.png';shutil.copyfile(receipt['rawSource'],rawp);raw=Image.open(rawp).convert('RGB').resize((64,35),Image.Resampling.NEAREST);a=orig[:35].copy();a[mask]=np.array(raw)[mask];full.paste(Image.fromarray(a),(0,0))
  target=OUT/f'{mood}-master.png';full.save(target);upper=full.crop((0,0,64,35));upper.resize((512,280),Image.Resampling.NEAREST).save(EYE/f'{mood}-open-reference.png');upper.save(EYE/f'{mood}-open-native.png');rows.append({'mood':mood,'file':str(target),'sha256':sha(target),'raw':str(rawp),'rawSha256':sha(rawp)})
  for row,(name,im) in enumerate([('raw',raw),('hood/iris preserved',upper)]):
   f=original.copy();f.paste(im,(0,0));contact.paste(f.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+3,row*280+5),mood+' '+name,fill='white')
  assert np.array_equal(np.array(upper)[22:],orig[22:35]);assert np.array_equal(np.array(upper)[:,32:],orig[:35,32:])
 contact.save(OUT/'masters-review.png');(OUT/'masters-manifest.json').write_text(json.dumps({'portraitRecord':46,'source':str(SOURCE),'sourceSha256':sha(SOURCE),'masters':rows,'browMask':str(OUT/'brow-mask.png'),'browMaskSha256':sha(OUT/'brow-mask.png'),'nativeApertureRows':[22,23],'stationaryRightFromX':32,'policy':'Original-conditioned generated visible-left eyebrow only; original exposed eyeball, gold hood edge and occluded image-right side remain exact.','status':'static-candidate-review-pending','limitations':['Emotion readability limited by hood occlusion and one visible brow.','No iris invented on occluded image-right eye.','No temporal/audio/published/user acceptance.']},indent=2))
if __name__=='__main__':main()
