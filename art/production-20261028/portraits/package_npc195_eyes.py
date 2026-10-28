"""Reference selected individual candidates and generate bounded review artifacts."""
import hashlib,json
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent;WORK=HERE/'npc195-eyes-v1'
MOODS=('neutral','happy','angry','sad','afraid','guarded');GAZES=('front','left','right');PHASES=('open','half','closed')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 masters=json.loads((HERE/'npc195-masters-v1/masters-selected.json').read_text());rows=[]
 contact=Image.new('RGB',(9*192,6*216),'#252525');draw=ImageDraw.Draw(contact)
 for y,mood in enumerate(MOODS):
  local=json.loads((WORK/f'{mood}-v2/manifest.json').read_text())
  if mood in ('neutral','happy'):local['status']='parent-static-contact-reviewed-candidate'
  if mood=='guarded':local['reviewLimitations'].append('Parent static review: HALF looks like a short wink. Only plausible as brief transition; no symmetric blink or timing pass.')
  (WORK/f'{mood}-v2/manifest.json').write_text(json.dumps(local,indent=2))
  master=Image.open(next(e['file'] for e in masters['masters'] if e['mood']==mood)).convert('RGB')
  for x,f in enumerate(local['frames']):
   p=Path(f['file']);full=master.copy();full.paste(Image.open(p).convert('RGB'),(0,0));contact.paste(full.resize((192,192),Image.Resampling.NEAREST),(x*192,y*216+24));draw.text((x*192+3,y*216+5),f"{mood} {f['gaze']} {f['phase']}",fill='white');rows.append({'mood':mood,**f})
 contact.save(WORK/'selected54-contact.png')
 manifest={'portraitRecord':195,'source':masters['source'],'sourceSha256':masters['sourceSha256'],'selectedMasters':str(HERE/'npc195-masters-v1/masters-selected.json'),'frames':rows,'generationMethod':'Independent source-conditioned single states; native composition; same-face neutral HALF lid donor for sad and afraid. No atlas cells selected.','status':'candidate-not-published','technicalGate':'see selected54-gate.json','staticReview':'All six local contacts viewed by producing agent. Parent review partial; separate feedback applies.','temporalReview':'pending; GIF encoding is not a watched playback pass','audiovisualReview':'not performed','userApproved':False,'limitations':['Guarded HALF is asymmetric with right eye already closed; short-wink timing must be checked.','Afraid and corrected sad mood readability still requires direct comparison.','54 mouths absent; this is not a complete portrait set.']}
 (WORK/'selected54-manifest.json').write_text(json.dumps(manifest,indent=2))
 for speed,durations in [('normal',[700,80,110,80,700]),('slow',[700,300,400,300,700])]:
  sequence=[]
  for phase in ('open','half','closed','half','open'):
   canvas=Image.new('RGB',(6*128,3*152),'#252525');d=ImageDraw.Draw(canvas)
   for x,mood in enumerate(MOODS):
    master=Image.open(next(e['file'] for e in masters['masters'] if e['mood']==mood)).convert('RGB')
    for y,gaze in enumerate(GAZES):
     full=master.copy();full.paste(Image.open(WORK/f'{mood}-v2/{gaze}-{phase}.png'),(0,0));canvas.paste(full.resize((128,128),Image.Resampling.NEAREST),(x*128,y*152+24));d.text((x*128+2,y*152+5),mood+' '+gaze,fill='white')
   sequence.append(canvas)
  sequence[0].save(WORK/f'selected54-blinks-{speed}.gif',save_all=True,append_images=sequence[1:],duration=durations,loop=0,optimize=False)
 old=WORK/'eyes-manifest.json'
 if old.exists():
  data=json.loads(old.read_text());data['status']='rejected-atlas-framing-and-eye-anatomy';data['supersededBy']=str(WORK/'selected54-manifest.json');old.write_text(json.dumps(data,indent=2))
 print(json.dumps({'frames':len(rows),'manifestSha256':sha(WORK/'selected54-manifest.json')}))
if __name__=='__main__':main()
