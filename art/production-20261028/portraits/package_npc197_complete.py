"""Package only NPC197 art with its source-specific masks and review limits."""
import hashlib,json,os
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc197-complete-candidate';MOODS=('neutral','happy','angry','sad','afraid','guarded')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 OUT.mkdir(exist_ok=True);eye=json.loads((HERE/'npc197-eyes-v1/selected54-manifest.json').read_text());source=Path(eye['source']);original=Image.open(source).convert('RGB')
 def ref(path):
  p=Path(path).resolve();return {'file':os.path.relpath(p,OUT).replace(os.sep,'/'),'sha256':sha(p)}
 eye_sets=[];thinking={};mouth_sets=[];contact=Image.new('RGB',(9*192,6*216),'#242424');d=ImageDraw.Draw(contact);tops=[];bottoms=[]
 for row,mood in enumerate(MOODS):
  states={g:{phase:ref(next(e['file'] for e in eye['frames'] if e['mood']==mood and e['gaze']==g and e['phase']==phase)) for phase in ('open','half','closed')} for g in ('front','left','right')}
  eye_sets.append({'id':mood,'label':mood.title(),'phases':states['front']});thinking[mood]=[{'id':g,'phases':states[g]} for g in ('left','right')]
  mouth=json.loads((HERE/f'npc197-mouths-v1/{mood}'/('selected9-manifest.json' if mood=='neutral' else 'manifest.json')).read_text());frames=[];upper=Image.open(OUT/states['front']['open']['file']).convert('RGB')
  for col,f in enumerate(mouth['frames']):
   p=Path(f['file']);frames.append({'shape':f['shape'],**ref(p)});lower=Image.open(p).convert('RGB');bottoms.append(np.array(lower));full=original.copy();full.paste(upper,(0,0));full.paste(lower,(0,35));contact.paste(full.resize((192,192),Image.Resampling.NEAREST),(col*192,row*216+24));d.text((col*192+3,row*216+5),mood+' '+f['shape'],fill='white')
  mouth_sets.append({'id':mood,'label':mood.title(),'frames':frames})
 for f in eye['frames']:tops.append(np.array(Image.open(f['file']).convert('RGB')))
 manifest={'schemaVersion':2,'portraitRecord':197,'bank':'TFAC00I0.RCI','width':64,'height':64,'splitY':35,'original':ref(source)['file'],'sourceSha256':sha(source),'originalUpper':eye_sets[0]['phases']['open']['file'],'originalLower':mouth_sets[0]['frames'][0]['file'],'status':'complete-art-candidate-review-pending','visualAcceptance':'Worker inspected all54 eyes and54 mouths; parent inspected masters,18 front states,lateral-v2 comparison and selected neutral mouths. Final full contacts pending.','temporalAcceptance':None,'audiovisualAcceptance':None,'published':False,'userApproved':False,'voiceRoute':'unassigned-in-this-candidate; no audio generated','eyeSets':eye_sets,'thinkingEyeSetsByEmotion':thinking,'mouthSets':mouth_sets,'provenance':{'eyeManifest':'../npc197-eyes-v1/selected54-manifest.json','mouthManifests':'../npc197-mouths-v1/<mood>/manifest.json'},'limitations':['Sad and afraid expressions remain close.','HALF hides original highlight row21 and retains exposed iris row22; dark half-open appearance requires temporal review.','Same-face lateral aperture reuse follows individual mood footprint comparison; no coordinates transfer to another portrait.','Emotional mouth tension is restrained; expressive readability needs playback.','G shows a broad upper tooth block; F/V lower-lip contact is not clearly readable in static review. No blanket phoneme pass.','RIGHT lateral sclera remains brighter and larger than FRONT, retain static brightness limit.','Neutral E independently corrected narrower and rounder than C; selected9-manifest binds exact frame.'],'mouthMaskFullCoordinates':[20,35,45,51]}
 (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2));contact.save(OUT/'mouths54-contact.png')
 allowed=np.zeros((64,64),bool);allowed[12:26,16:31]=True;allowed[12:26,33:47]=True;allowed[35:51,20:45]=True;orig=np.array(original);bad=0
 for top in tops:
  for bottom in bottoms:
   if not np.array_equal(np.concatenate((top,bottom),axis=0)[~allowed],orig[~allowed]):bad+=1
 anchors={}
 for mood in MOODS:
  selected=next(s for s in mouth_sets if s['id']==mood);a={f['shape']:np.array(Image.open(OUT/f['file']).convert('RGB')) for f in selected['frames']};anchors[mood]={s:bool(np.array_equal(a['B'][:7,20:45],a[s][:7,20:45])) for s in ('D','H')}
 check={'combinations':len(tops)*len(bottoms),'outsideUnionMaskFailures':bad,'sourceExactNeutralA':np.array_equal(bottoms[0],orig[35:]),'sourceExactNeutralX':np.array_equal(bottoms[8],orig[35:]),'ownBUpperToothAnchorDH':anchors,'technicalPass':bad==0 and all(all(a.values()) for a in anchors.values()),'visualPass':None,'audiovisualPass':None,'manifestSha256':sha(OUT/'manifest.json')};(OUT/'composition-gate.json').write_text(json.dumps(check,indent=2));print(json.dumps(check))
if __name__=='__main__':main()

