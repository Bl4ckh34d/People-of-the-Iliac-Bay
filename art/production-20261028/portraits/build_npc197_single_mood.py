"""NPC197 same-face eye fitting, only its own inspected aperture."""
import json,hashlib,sys
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent;WORK=HERE/'npc197-eyes-v1';APERTURE=((21,21,28,23),(36,21,43,23));LIDS=((19,20,30,22),(34,20,45,22))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main(mood):
 out=WORK/('neutral-v2' if mood=='neutral' else mood);out.mkdir(exist_ok=True);opened=Image.open(WORK/f'{mood}-open-native.png').convert('RGB');closed=Image.open(WORK/f'{mood}-closed-native.png').convert('RGB');halfsource=Image.open(WORK/f'{mood}-half-selected.png').convert('RGB')
 masters=json.loads((HERE/'npc197-masters-v1/masters-manifest.json').read_text());master=Image.open(next(e['file'] for e in masters['masters'] if e['mood']==mood)).convert('RGB');contact=Image.new('RGB',(3*256,3*280),'#242424');d=ImageDraw.Draw(contact);rows=[]
 for row,gaze in enumerate(('front','left','right')):
  eye=opened.copy()
  if gaze!='front':
   donor=WORK/f'neutral-{gaze}-v2-candidate.png';raw=Image.open(donor).convert('RGB').resize((64,35),Image.Resampling.NEAREST)
   for box in APERTURE:eye.paste(raw.crop(box),box[:2])
  half=eye.copy()
  for box in LIDS:half.paste(halfsource.crop(box),box[:2])
  assert np.array_equal(np.array(half)[22],np.array(eye)[22])
  for col,(phase,im) in enumerate([('open',eye),('half',half),('closed',closed)]):
   p=out/f'{gaze}-{phase}.png';im.save(p);rows.append({'gaze':gaze,'phase':phase,'file':str(p),'sha256':sha(p)});full=master.copy();full.paste(im,(0,0));contact.paste(full.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+3,row*280+5),mood+' '+gaze+' '+phase,fill='white')
 contact.save(out/'review.png');(out/'manifest.json').write_text(json.dumps({'record':197,'mood':mood,'frames':rows,'apertureBoxes':APERTURE,'halfLidBoxes':LIDS,'retainedExposedIrisRow':22,'status':'static-review-pending','lateralSourcePolicy':'Same-face neutral-generated lateral aperture only. Own mood brows/socket/lid contour fixed. Source-specific21..22 eye aperture inspected across six masters.','halfSelectionManifest':'../front-half-selection.json','limits':['No universal coordinates across portraits.','Lateral sclera contrast remains subject to static review.','No temporal/AV/user acceptance.']},indent=2))
if __name__=='__main__':main(sys.argv[1])

