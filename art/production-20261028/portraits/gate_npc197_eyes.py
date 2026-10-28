"""Separate structural check; no inference of visual motion quality."""
import json,hashlib
from pathlib import Path
from PIL import Image
import numpy as np
p=Path(__file__).resolve().parent/'npc197-eyes-v1';mp=p/'selected54-manifest.json';m=json.loads(mp.read_text());errors=[];frames={};expected={(a,b,c) for a in ('neutral','happy','angry','sad','afraid','guarded') for b in ('front','left','right') for c in ('open','half','closed')}
for f in m['frames']:
 file=Path(f['file']);im=Image.open(file);key=(f['mood'],f['gaze'],f['phase'])
 if key in frames:errors.append('duplicate '+str(key))
 if im.size!=(64,35):errors.append('size '+str(key))
 if hashlib.sha256(file.read_bytes()).hexdigest()!=f['sha256']:errors.append('hash '+str(key))
 frames[key]=np.array(im.convert('RGB'))
if set(frames)!=expected:errors.append('state coverage')
source=np.array(Image.open(m['source']).convert('RGB'))[:35]
if not np.array_equal(source,frames['neutral','front','open']):errors.append('neutral not source exact')
mask=np.zeros((35,64),bool);mask[20:24,19:30]=True;mask[20:24,34:45]=True
for (mood,gaze,phase),im in frames.items():
 if not np.array_equal(im[~mask],frames[mood,'front','open'][~mask]):errors.append('stationary '+str((mood,gaze,phase)))
 if phase=='half':
  for x0,x1 in [(21,28),(36,43)]:
   if not np.array_equal(im[22,x0:x1],frames[mood,gaze,'open'][22,x0:x1]):errors.append('exposed iris '+str((mood,gaze)))
out={'record':197,'frames':len(frames),'structuralPass':not errors,'errors':errors,'sourceExactNeutralOpen':np.array_equal(source,frames['neutral','front','open']),'exposedIris':'Matching gaze OPEN row22 unchanged in each HALF','visualPass':None,'temporalPass':None,'manifestSha256':hashlib.sha256(mp.read_bytes()).hexdigest()};(p/'selected54-gate.json').write_text(json.dumps(out,indent=2));print(json.dumps(out))
