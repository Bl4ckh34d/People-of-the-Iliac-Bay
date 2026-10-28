"""196 individual eye phase fitting based on its own21..22 source aperture."""
import json,hashlib,sys
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent;WORK=HERE/'npc196-eyes-v1';APERTURE=((20,21,29,23),(37,21,45,23));LIDS=((18,20,31,22),(34,20,47,22))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main(mood,revision=1):
 out=WORK/(mood if revision==1 else f'{mood}-v{revision}');out.mkdir(exist_ok=True);opened=Image.open(WORK/f'{mood}-open-native.png').convert('RGB');closed=Image.open(WORK/f'{mood}-closed-native.png').convert('RGB');halfraw=Image.open(WORK/f'{mood}-half-raw.png').convert('RGB').resize((64,35),Image.Resampling.NEAREST)
 masters=json.loads((HERE/'npc196-masters-v1/masters-manifest.json').read_text());master=Image.open(next(e['file'] for e in masters['masters'] if e['mood']==mood)).convert('RGB');contact=Image.new('RGB',(3*256,3*280),'#242424');d=ImageDraw.Draw(contact);rows=[]
 for row,gaze in enumerate(('front','left','right')):
  eye=opened.copy()
  if gaze!='front':
   if mood!='neutral' or revision==3:
    donor=WORK/('neutral-left-bounded-v3.png' if gaze=='left' else 'neutral-v2/right-open.png')
   else:
    suffix='-color-v2' if revision==2 else ''
    donor=WORK/f'{mood}-{gaze}{suffix}-raw.png'
   raw=Image.open(donor).convert('RGB').resize((64,35),Image.Resampling.NEAREST)
   for box in APERTURE:eye.paste(raw.crop(box),box[:2])
  half=eye.copy()
  for box in LIDS:half.paste(halfraw.crop(box),box[:2])
  for x0,y0,x1,y1 in APERTURE:assert np.array_equal(np.array(half)[22,x0:x1],np.array(eye)[22,x0:x1])
  for col,(phase,im) in enumerate([('open',eye),('half',half),('closed',closed)]):
   p=out/f'{gaze}-{phase}.png';im.save(p);rows.append({'gaze':gaze,'phase':phase,'file':str(p),'sha256':sha(p)});full=master.copy();full.paste(im,(0,0));contact.paste(full.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+3,row*280+5),mood+' '+gaze+' '+phase,fill='white')
 contact.save(out/'review.png');(out/'manifest.json').write_text(json.dumps({'record':196,'mood':mood,'frames':rows,'apertureBoxes':APERTURE,'halfLidBoxes':LIDS,'retainedExposedIrisRow':22,'status':'static-review-pending','lateralSourcePolicy':'Nonneutral and neutral-v3 reuse selected same-face LEFT v3 / RIGHT v2 aperture only. Own brows/socket/lid contour remain fixed. Six-mood aperture-compatibility-review.png inspected: matching21..22 footprint, corners and original palette.','halfSource':str(WORK/f'{mood}-half-raw.png'),'limits':['Same-face reuse is specific to196 aperture geometry; not transferable to another portrait.','Whole lateral eyeball shading requires full-contact inspection.','No temporal/AV/user acceptance.']},indent=2))
if __name__=='__main__':main(sys.argv[1],int(sys.argv[2]) if len(sys.argv)>2 else 1)
