"""Assemble a reviewable gallery-contract candidate by referencing existing art."""
import hashlib,json,os
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc195-complete-candidate';MOODS=('neutral','happy','angry','sad','afraid','guarded')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 OUT.mkdir(exist_ok=True);eye=json.loads((HERE/'npc195-eyes-v1/selected54-manifest.json').read_text());source=Path(eye['source']);original=Image.open(source).convert('RGB')
 def ref(path):
  p=Path(path).resolve();return {'file':os.path.relpath(p,OUT).replace(os.sep,'/'),'sha256':sha(p)}
 eye_sets=[];thinking={};mouth_sets=[];mouth_contact=Image.new('RGB',(9*192,6*216),'#242424');d=ImageDraw.Draw(mouth_contact);eye_arrays=[];mouth_arrays=[]
 for row,mood in enumerate(MOODS):
  states={f['gaze']:{phase:ref(next(e['file'] for e in eye['frames'] if e['mood']==mood and e['gaze']==f['gaze'] and e['phase']==phase)) for phase in ('open','half','closed')} for f in eye['frames'] if f['mood']==mood}
  eye_sets.append({'id':mood,'label':mood.title(),'phases':states['front']});thinking[mood]=[{'id':g,'phases':states[g]} for g in ('left','right')]
  local=HERE/('npc195-mouths-v2/neutral/selected9-manifest.json' if mood=='neutral' else f'npc195-mouths-v1/{mood}/manifest.json');mouth=json.loads(local.read_text());frames=[]
  upper=Image.open(next(e['file'] for e in eye['frames'] if e['mood']==mood and e['gaze']=='front' and e['phase']=='open')).convert('RGB')
  for col,f in enumerate(mouth['frames']):
   p=Path(f['file']);frames.append({'shape':f['shape'],**ref(p)});lower=Image.open(p).convert('RGB');mouth_arrays.append(np.array(lower));full=original.copy();full.paste(upper,(0,0));full.paste(lower,(0,35));mouth_contact.paste(full.resize((192,192),Image.Resampling.NEAREST),(col*192,row*216+24));d.text((col*192+3,row*216+5),mood+' '+f['shape'],fill='white')
  mouth_sets.append({'id':mood,'label':mood.title(),'frames':frames})
 for f in eye['frames']:eye_arrays.append(np.array(Image.open(f['file']).convert('RGB')))
 manifest={'schemaVersion':2,'portraitRecord':195,'bank':'TFAC00I0.RCI','width':64,'height':64,'splitY':35,'original':ref(source)['file'],'sourceSha256':sha(source),'originalUpper':eye_sets[0]['phases']['open']['file'],'originalLower':mouth_sets[0]['frames'][0]['file'],'status':'complete-art-candidate-review-pending','visualAcceptance':'eyes-static-candidate-parent-reviewed-with-guarded-wink-limit; mouths-worker-reviewed-parent-pending','temporalAcceptance':None,'audiovisualAcceptance':None,'published':False,'userApproved':False,'voiceRoute':'unassigned-in-this-candidate; no audio generated','eyeSets':eye_sets,'thinkingEyeSetsByEmotion':thinking,'mouthSets':mouth_sets,'provenance':{'eyeManifest':os.path.relpath(HERE/'npc195-eyes-v1/selected54-manifest.json',OUT),'neutralMouth':os.path.relpath(HERE/'npc195-mouths-v2/neutral/selected9-manifest.json',OUT),'otherMouths':'../npc195-mouths-v1/<mood>/manifest.json'},'limitations':['Guarded HALF is an asymmetric short-wink candidate. Long HALF holds are not accepted.','Sad and afraid remain close but distinguishable in static parent review.','All mouth pose semantics and audio timing still require playback; no audio acceptance inferred.','Each fitted region belongs only to195; no universal pixel-row policy.'],'mouthMaskFullCoordinates':[21,35,45,52]}
 manifest['visualAcceptance']='Parent inspected54eyes and54mouths; static candidate only, explicit limitations remain.'
 manifest['limitations'].append('Parent semantic review: C/E distinction weak in neutral, afraid and guarded. E needs clearer slight rounding for AO/ER and must be no wider than C. No blanket phoneme pass; bounded E-only repair remains.')
 (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2));mouth_contact.save(OUT/'mouths54-contact.png')
 allowed=np.zeros((64,64),bool);allowed[12:23,14:30]=True;allowed[12:23,35:50]=True;allowed[35:52,21:45]=True;orig=np.array(original);bad=0
 for top in eye_arrays:
  for bottom in mouth_arrays:
   full=np.concatenate((top,bottom),axis=0)
   if not np.array_equal(full[~allowed],orig[~allowed]):bad+=1
 anchors={}
 for mood in MOODS:
  selected=next(s for s in mouth_sets if s['id']==mood);a={f['shape']:np.array(Image.open(OUT/f['file']).convert('RGB')) for f in selected['frames']}
  anchors[mood]={s:bool(np.array_equal(a['B'][:7,21:45],a[s][:7,21:45])) for s in ('D','H')}
 check={'combinations':len(eye_arrays)*len(mouth_arrays),'outsideUnionMaskFailures':bad,'sourceExactNeutralA':np.array_equal(mouth_arrays[0],orig[35:]),'sourceExactNeutralX':np.array_equal(mouth_arrays[8],orig[35:]),'ownBUpperToothAnchorDH':anchors,'technicalPass':bad==0 and all(all(a.values()) for a in anchors.values()),'visualPass':None,'audiovisualPass':None,'manifestSha256':sha(OUT/'manifest.json')};(OUT/'composition-gate.json').write_text(json.dumps(check,indent=2));print(json.dumps(check))
if __name__=='__main__':main()
