import hashlib,json
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent;P=HERE/'npc197-eyes-v1';MOODS=('neutral','happy','angry','sad','afraid','guarded')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 masters=json.loads((HERE/'npc197-masters-v1/masters-manifest.json').read_text());rows=[];c=Image.new('RGB',(9*192,6*216),'#242424');d=ImageDraw.Draw(c)
 for y,mood in enumerate(MOODS):
  folder=P/('neutral-v2' if mood=='neutral' else mood);local=json.loads((folder/'manifest.json').read_text());master=Image.open(next(e['file'] for e in masters['masters'] if e['mood']==mood)).convert('RGB')
  for x,f in enumerate(local['frames']):
   rows.append({'mood':mood,**f});full=master.copy();full.paste(Image.open(f['file']),(0,0));c.paste(full.resize((192,192),Image.Resampling.NEAREST),(x*192,y*216+24));d.text((x*192+3,y*216+5),mood+' '+f['gaze']+' '+f['phase'],fill='white')
 c.save(P/'selected54-contact.png');manifest={'portraitRecord':197,'source':masters['source'],'sourceSha256':masters['sourceSha256'],'frames':rows,'movingBoxes':[(19,20,30,24),(34,20,45,24)],'apertureBoxes':[(21,21,28,23),(36,21,43,23)],'retainedExposedRow':22,'selectedSources':'Own six OPEN/CLOSED, neutral/sad/afraid generated HALF. Happy/angry/guarded use inspected same-face neutral lid strip20..21; front-half-selection.json binds provenance and rejected raw attempts. Lateral neutral v2 aperture donors, only21..22 copied after own face comparison.','status':'static-candidates-parent-full-contact-pending','limitations':['FRONT HALF is dark because source highlight row21 becomes covered; no invented highlight on lower iris.','Sad/fear expressions are close.','RIGHT lateral sclera remains brighter and larger than FRONT; static candidate limit.','No temporal/audiovisual/publication/user acceptance.']};mp=P/'selected54-manifest.json';mp.write_text(json.dumps(manifest,indent=2))
 for speed,durations in [('normal',[700,80,110,80,700]),('slow',[700,300,400,300,700])]:
  seq=[]
  for phase in ('open','half','closed','half','open'):
   canvas=Image.new('RGB',(768,456),'#242424');dr=ImageDraw.Draw(canvas)
   for x,mood in enumerate(MOODS):
    master=Image.open(next(e['file'] for e in masters['masters'] if e['mood']==mood)).convert('RGB')
    for y,gaze in enumerate(('front','left','right')):
     f=next(f for f in rows if f['mood']==mood and f['gaze']==gaze and f['phase']==phase);full=master.copy();full.paste(Image.open(f['file']),(0,0));canvas.paste(full.resize((128,128),Image.Resampling.NEAREST),(x*128,y*152+24));dr.text((x*128+3,y*152+5),mood+' '+gaze,fill='white')
   seq.append(canvas)
  seq[0].save(P/f'selected54-blinks-{speed}.gif',save_all=True,append_images=seq[1:],duration=durations,loop=0,optimize=False)
 print(sha(mp))
if __name__=='__main__':main()
