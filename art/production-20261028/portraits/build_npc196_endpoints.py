"""196 blink endpoints with immutable mood brow/socket above row20."""
import json,hashlib
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc196-eyes-v1';BOXES=((18,20,31,24),(34,20,47,24))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 m=json.loads((HERE/'npc196-masters-v1/masters-manifest.json').read_text());contact=Image.new('RGB',(4*256,6*280),'#242424');d=ImageDraw.Draw(contact);rows=[]
 for row,e in enumerate(m['masters']):
  mood=e['mood'];master=Image.open(e['file']).convert('RGB');opened=master.crop((0,0,64,35));raw=Image.open(OUT/f'{mood}-closed-raw.png').convert('RGB').resize((64,35),Image.Resampling.NEAREST);closed=opened.copy()
  for box in BOXES:closed.paste(raw.crop(box),box[:2])
  opened.save(OUT/f'{mood}-open-native.png');closed.save(OUT/f'{mood}-closed-native.png');closed.resize((512,280),Image.Resampling.NEAREST).save(OUT/f'{mood}-closed-reference.png')
  mask=np.zeros((35,64),bool)
  for x0,y0,x1,y1 in BOXES:mask[y0:y1,x0:x1]=True
  assert np.array_equal(np.array(opened)[~mask],np.array(closed)[~mask])
  for col,(label,im) in enumerate([('OPEN',opened),('raw CLOSED',raw),('fit CLOSED',closed),('original',Image.open(m['source']).convert('RGB').crop((0,0,64,35)))]):
   full=master.copy();full.paste(im,(0,0));contact.paste(full.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+3,row*280+5),mood+' '+label,fill='white')
  rows.append({'mood':mood,'open':str(OUT/f'{mood}-open-native.png'),'openSha256':sha(OUT/f'{mood}-open-native.png'),'closed':str(OUT/f'{mood}-closed-native.png'),'closedSha256':sha(OUT/f'{mood}-closed-native.png'),'raw':str(OUT/f'{mood}-closed-raw.png'),'rawSha256':sha(OUT/f'{mood}-closed-raw.png')})
 contact.save(OUT/'endpoints-review.png');(OUT/'endpoints-manifest.json').write_text(json.dumps({'record':196,'movingBoxes':BOXES,'sourceApertureRows':[21,22],'stationaryLowerStartY':24,'stationaryBrowSocketEndY':19,'endpoints':rows,'status':'static-review-pending','temporalPass':None},indent=2))
if __name__=='__main__':main()
