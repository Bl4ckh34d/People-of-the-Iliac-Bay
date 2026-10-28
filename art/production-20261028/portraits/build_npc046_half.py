from pathlib import Path
from PIL import Image,ImageDraw
import json,hashlib,shutil
import numpy as np
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc046-eyes-v1';BOX=(19,21,30,23)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
m=json.loads((HERE/'npc046-masters-v1/masters-manifest.json').read_text());c=Image.new('RGB',(4*256,6*280),'#222222');d=ImageDraw.Draw(c);rows=[]
for row,e in enumerate(m['masters']):
 mood=e['mood'];receipt=json.loads((OUT/f'{mood}-half-receipt.json').read_text());rawp=OUT/f'{mood}-half-raw.png';shutil.copyfile(receipt['rawSource'],rawp);raw=Image.open(rawp).convert('RGB').resize((64,35),Image.Resampling.NEAREST);opened=Image.open(OUT/f'{mood}-open-native.png').convert('RGB');half=opened.copy();half.paste(raw.crop(BOX),BOX[:2]);half.save(OUT/f'{mood}-half-native.png');closed=Image.open(OUT/f'{mood}-closed-native.png');master=Image.open(e['file']).convert('RGB')
 for col,(name,im) in enumerate([('OPEN',opened),('raw HALF',raw),('fit HALF',half),('CLOSED',closed)]):
  full=master.copy();full.paste(im,(0,0));c.paste(full.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+3,row*280+5),mood+' '+name,fill='white')
 assert np.array_equal(np.array(half)[23:],np.array(opened)[23:]);rows.append({'mood':mood,'half':str(OUT/f'{mood}-half-native.png'),'halfSha256':sha(OUT/f'{mood}-half-native.png'),'raw':str(rawp),'rawSha256':sha(rawp)})
c.save(OUT/'front18-review.png');(OUT/'half-manifest.json').write_text(json.dumps({'record':46,'movingHalfLidBox':BOX,'retainedOriginalExposedIrisRow':23,'selection':'Generated upperlid21..22 over original uncovered iris row23. Raw moved dark pupil downward; that raw iris is not selected.','halves':rows,'status':'static-candidate-review-pending','temporalPass':None},indent=2))
