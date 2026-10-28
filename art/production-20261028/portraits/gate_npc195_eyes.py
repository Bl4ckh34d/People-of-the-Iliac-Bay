"""Independent structural audit; deliberately makes no anatomy/animation pass claim."""
import hashlib,json
from pathlib import Path
import numpy as np
from PIL import Image
p=Path('art/production-20261028/portraits/npc195-eyes-v1');mp=p/'selected54-manifest.json';m=json.loads(mp.read_text());errors=[];frames={}
for f in m['frames']:
 key=(f['mood'],f['gaze'],f['phase']);file=Path(f['file']);im=Image.open(file)
 if key in frames:errors.append('duplicate '+str(key))
 if im.size!=(64,35):errors.append('size '+str(key))
 if hashlib.sha256(file.read_bytes()).hexdigest()!=f['sha256']:errors.append('hash '+str(key))
 frames[key]=np.array(im.convert('RGB'))
expected={(m,g,s) for m in ('neutral','happy','angry','sad','afraid','guarded') for g in ('front','left','right') for s in ('open','half','closed')}
if set(frames)!=expected:errors.append('incomplete states')
source=np.array(Image.open(m['source']).convert('RGB'))[:35]
if not np.array_equal(source,frames['neutral','front','open']):errors.append('neutral not source exact')
mask=np.zeros((35,64),bool);mask[17:23,14:30]=True;mask[17:23,35:50]=True
for (mood,gaze,phase),im in frames.items():
 front=frames[mood,'front','open']
 if not np.array_equal(im[~mask],front[~mask]):errors.append('stationary region '+str((mood,gaze,phase)))
 if phase=='half':
  for x0,x1 in [(17,27)]+([] if mood=='guarded' else [(36,46)]):
   if not np.array_equal(im[19,x0:x1],frames[mood,gaze,'open'][19,x0:x1]):errors.append('exposed iris '+str((mood,gaze)))
report={'structuralPass':not errors,'errors':errors,'frames':len(frames),'uniqueFileHashes':len(set(f['sha256'] for f in m['frames'])),'sourceExactNeutralOpen':np.array_equal(source,frames['neutral','front','open']),'stationaryOutsideDeclaredMovingBoxes':True if not errors else 'see errors','exposedIrisChecks':'Matching OPEN row19 retained; guarded right omitted because fully closed at HALF.','visualPass':None,'temporalPass':None,'manifestSha256':hashlib.sha256(mp.read_bytes()).hexdigest()}
(p/'selected54-gate.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
