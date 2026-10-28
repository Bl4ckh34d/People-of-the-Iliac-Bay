"""Own-face tooth anchoring; retain each pose's generated contour where valid."""
import json,hashlib
from pathlib import Path
from PIL import Image
ROOT=Path(__file__).resolve().parent/'npc425-mouths-v1'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
neutral_g=Image.open(ROOT/'neutral/G-v3-anchored.png').convert('RGB');neutral_e=Image.open(ROOT/'neutral/E-v3-candidate.png').convert('RGB')
for folder in ['happy','angry','sad','afraid','guarded-v2']:
 p=ROOT/folder;m=json.loads((p/'manifest.json').read_text());b=Image.open(p/'candidate/B.png').convert('RGB');g=Image.open(p/'candidate/G.png').convert('RGB');g.paste(b.crop((20,4,45,9)),(20,4));notes=['G upper teeth/upperlip anchored to this mood B rows4..8, retaining distinct lowerlip below.']
 if folder in ['happy','sad']:
  g.paste(neutral_g.crop((20,9,45,17)),(20,9));notes.append('Raw G lowerlip is atrow8 and would be overwritten by the correct toothrow8. Use inspected same-face neutral generated G lowerlip rows9..16; own mood B uppercontour retained. No foreign anatomy.')
 target=p/'G-anchored-v2.png';g.save(target);f=next(f for f in m['frames'] if f['shape']=='G');f.update(file=str(target),sha256=sha(target))
 if folder=='sad':
  e=Image.open(p/'candidate/E.png').convert('RGB');e.paste(neutral_e.crop((20,4,45,9)),(20,4));target=p/'E-anchored-v2.png';e.save(target);f=next(f for f in m['frames'] if f['shape']=='E');f.update(file=str(target),sha256=sha(target));notes.append('Sad E toothrow7 rejected. Same-face selected neutral E rounded uppercontour rows4..8 retains own E width, sad generated lower opening remains below.')
 m['selection']=notes;m['limits']=['G upper-teeth/lower-lip contact and native expression readability remain subject to independent and temporal review.'];(p/'selected9-manifest.json').write_text(json.dumps(m,indent=2))
