from pathlib import Path
from PIL import Image,ImageDraw
import json,hashlib,shutil
import numpy as np
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc015-eyes-v1';BOXES=[(17,26,30,30),(36,26,44,30)]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 m=json.loads((HERE/'npc015-masters-v1/masters-manifest.json').read_text());c=Image.new('RGB',(3*256,6*280),'#222222');d=ImageDraw.Draw(c);rows=[]
 for row,e in enumerate(m['masters']):
  mood=e['mood'];receipt=json.loads((OUT/f'{mood}-closed-receipt.json').read_text());rawp=OUT/f'{mood}-closed-raw.png';shutil.copyfile(receipt['rawSource'],rawp);raw=Image.open(rawp).convert('RGB').resize((64,35),Image.Resampling.NEAREST);opened=Image.open(OUT/f'{mood}-open-native.png').convert('RGB');closed=opened.copy();[closed.paste(raw.crop(b),b[:2]) for b in BOXES];closed.save(OUT/f'{mood}-closed-native.png');closed.resize((512,280),Image.Resampling.NEAREST).save(OUT/f'{mood}-closed-reference.png');master=Image.open(e['file']).convert('RGB')
  for col,(name,im) in enumerate([('OPEN',opened),('raw CLOSED',raw),('fit CLOSED',closed)]):
   full=master.copy();full.paste(im,(0,0));c.paste(full.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+3,row*280+5),mood+' '+name,fill='white')
  a=np.array(opened);z=np.array(closed);mask=np.zeros((35,64),bool);mask[26:30,17:30]=True;mask[26:30,36:44]=True;assert np.array_equal(a[~mask],z[~mask]);rows.append({'mood':mood,'open':str(OUT/f'{mood}-open-native.png'),'closed':str(OUT/f'{mood}-closed-native.png'),'closedSha256':sha(OUT/f'{mood}-closed-native.png'),'raw':str(rawp),'rawSha256':sha(rawp)})
 c.save(OUT/'endpoints-review.png');(OUT/'endpoints-manifest.json').write_text(json.dumps({'record':15,'movingBoxes':BOXES,'visibleEye':'both; hood outside boxes source-exact','endpoints':rows,'status':'static-review-pending','temporalPass':None},indent=2))
if __name__=='__main__':main()
