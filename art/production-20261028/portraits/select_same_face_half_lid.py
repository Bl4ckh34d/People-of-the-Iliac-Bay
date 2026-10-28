"""Bounded own-face generated lid selection, with immutable exposed iris."""
from pathlib import Path
from PIL import Image,ImageDraw
import json,numpy as np,hashlib,sys
HERE=Path(__file__).resolve().parent
record=int(sys.argv[1])
configs={
47:dict(donor='happy',row=26,iris=27,xspans=[(17,30),(35,44)],aperture=(26,28),repair=['neutral','angry','sad','afraid']),
14:dict(donor='afraid',row=26,rowEnd=28,iris=28,xspans=[(17,30),(36,44)],aperture=(27,29),repair=['neutral','happy','angry','sad','guarded']),
13:dict(donor='sad',row=27,iris=28,xspans=[(17,29),(36,44)],aperture=(26,29),repair=['neutral','happy','angry','afraid','guarded'])}
cfg=configs[record];p=HERE/f'npc{record:03}-eyes-v1'
donor=Image.open(p/f"{cfg['donor']}-half-native.png").convert('RGB')
base=np.array(Image.open(p/'neutral-open-native.png').convert('RGB'));rows=[]
c=Image.new('RGB',(768,6*280),'#222222');d=ImageDraw.Draw(c)
for r,m in enumerate(['neutral','happy','angry','sad','afraid','guarded']):
 op=Image.open(p/f'{m}-open-native.png').convert('RGB');im=Image.open(p/f'{m}-half-native.png').convert('RGB');a=np.array(op)
 for x,z in cfg['xspans']:
  y,w=cfg['aperture'];assert np.array_equal(a[y:w,x:z],base[y:w,x:z])
  if m in cfg['repair']:
   box=(x,cfg['row'],z,cfg.get('rowEnd',cfg['row']+1));im.paste(donor.crop(box),box[:2])
 dest=p/f'{m}-half-selected.png';im.save(dest)
 assert np.array_equal(np.array(im)[cfg['iris']:],a[cfg['iris']:])
 rows.append({'mood':m,'file':str(dest),'sha256':hashlib.sha256(dest.read_bytes()).hexdigest(),'selectedLidRowFrom':cfg['donor'] if m in cfg['repair'] else m})
 for col,(label,img) in enumerate([('OPEN',op),('HALF selected',im),('CLOSED',Image.open(p/f'{m}-closed-native.png'))]):
  full=Image.open(HERE/f'npc{record:03}-masters-v1'/f'{m}-master.png');full.paste(img,(0,0));c.paste(full.resize((256,256),Image.Resampling.NEAREST),(col*256,r*280+24));d.text((col*256+3,r*280+5),m+' '+label,fill='white')
c.save(p/'front18-selected-review.png')
(p/'half-selected-manifest.json').write_text(json.dumps({'record':record,'frames':rows,'config':cfg,'policy':'Same-face generated lid row only after exact OPEN aperture comparison; own remaining lids/brows kept, original exposed iris fixed.','status':'static-candidate','temporalPass':None},indent=2))
