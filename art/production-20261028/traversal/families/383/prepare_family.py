"""Record original383 source hashes and adapt the existing transparent packer."""
from pathlib import Path
from PIL import Image
import json,hashlib
ROOT=Path(__file__).resolve().parent;ART=ROOT.parents[3];sources=[]
for path,role in [(ART/'standing/source/native-idle/383-0.png','native-frontal-idle')]+[(ART/f'standing/source/383-{v}-0.png','native-walking-bearing') for v in range(5)]:
    sources.append({'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'pixelSize':Image.open(path).size,'role':role})
native=ROOT.parents[2]/'mobile-locomotion/families/383/native-reference.json';record=json.loads(native.read_text(encoding='utf-8'))['references'][0]['frames'][0]
m={'identity':'vanilla:mobile:383:None','sourceHierarchy':'Unmodified native source pixels; generated native-resolution art is not original.','sources':sources,'sourceVisualReview':'Original five-view sheet and native front inspected: dark-brown clean-shaven adult man, short dark hair, teal draped tunic, yellow sleeve and jagged hem trim, green pouch, tan shoes.','nativeRecordEvidence':str(native),'nativeRecordEvidenceSha256':hashlib.sha256(native.read_bytes()).hexdigest(),'nativeFrontWalkScaleXY':record['nativeRecordScaleXY'],'nativeFrontWalkDimensions':record['dimensions'],'nativeFrontWalkScaledSizeBeforeGlobalScale':record['scaledSizeBeforeGlobalScaleAndOverrides'],'generatedWorldScaleApplied':False,'mirrorPolicy':'Only symmetric jump may use documented native0,1,2,3,4,mirror3,mirror2,mirror1 derivatives. Climb/swim require phase-specific anatomical evidence.','generated':False,'integrated':False,'userApproved':False}
(ROOT/'source-manifest.json').write_text(json.dumps(m,indent=2)+'\n',encoding='utf-8')
script=(ROOT.parent/'382/pack_jump_front.py').read_text(encoding='utf-8').replace('382','383').replace("HEAD_X=[260,775,1304,1825]","HEAD_X=[255,783,1310,1834]")
script=script.replace('Generated muscles, beard detail, skin highlights and wrap folds exceed native detail; exactpalette not claimed.','Generated facial features, tunic folds and yellow saturation differ from native pixels; exact palette not claimed.')
script=script.replace('Native comparison inspected: beard, bare torso, white wrap, metal bracers and single green pouch retained; four distinct jump poses readable. Muscle/face detail and skin highlights differ from native; no temporal acceptance.','Pending383 native comparison review; do not infer from382.')
script=script.replace("manifest['visuallyReviewed']=True","manifest['visuallyReviewed']=False")
(ROOT/'pack_jump_front.py').write_text(script,encoding='utf-8');print('383 source hashes recorded; jump front packer prepared.')
