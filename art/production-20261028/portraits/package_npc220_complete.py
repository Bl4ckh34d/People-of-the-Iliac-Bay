"""Combine NPC220 own mouth art with separately proven same-feature eye derivation."""
import json,hashlib,os,copy
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc220-complete-candidate';EP=HERE/'exact-feature-reuse-wave2/common-220/manifest.json';MOODS=('neutral','happy','angry','sad','afraid','guarded')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 OUT.mkdir(exist_ok=True);eye=json.loads(EP.read_text());source=(EP.parent/eye['original']).resolve();original=Image.open(source).convert('RGB');orig=np.array(original)
 def ref(p):
  p=Path(p).resolve();return {'file':os.path.relpath(p,OUT).replace(os.sep,'/'),'sha256':sha(p)}
 eyes=copy.deepcopy(eye['eyeSets']);thinking=copy.deepcopy(eye['thinkingEyeSetsByEmotion']);tops=[]
 for s in eyes+[s for sets in thinking.values() for s in sets]:
  for phase,item in s['phases'].items():
   path=EP.parent/item['file'];assert sha(path)==item['sha256'];s['phases'][phase]=ref(path);tops.append(np.array(Image.open(path).convert('RGB')))
 mouths=[];bottoms=[];sources=[];contact=Image.new('RGB',(1728,1296),'#242424');draw=ImageDraw.Draw(contact)
 for row,mood in enumerate(MOODS):
  folder=HERE/f'npc220-mouths-v1/{mood}';mp=folder/'selected9-manifest.json'
  if not mp.exists():mp=folder/'manifest.json'
  m=json.loads(mp.read_text());sources.append(ref(mp));frames=[];upper=Image.open(OUT/next(s for s in eyes if s['id']==mood)['phases']['open']['file'])
  for col,f in enumerate(m['frames']):
   path=Path(f['file']);assert sha(path)==f['sha256'];frames.append({'shape':f['shape'],**ref(path)});im=Image.open(path).convert('RGB');bottoms.append(np.array(im));full=original.copy();full.paste(upper,(0,0));full.paste(im,(0,35));contact.paste(full.resize((192,192),Image.Resampling.NEAREST),(col*192,row*216+24));draw.text((col*192+3,row*216+5),mood+' '+f['shape'],fill='white')
  mouths.append({'id':mood,'label':mood.title(),'frames':frames})
 manifest={'schemaVersion':2,'portraitRecord':220,'bank':'TFAC00I0.RCI','width':64,'height':64,'splitY':35,'original':ref(source)['file'],'sourceSha256':sha(source),'originalUpper':eyes[0]['phases']['open']['file'],'originalLower':mouths[0]['frames'][0]['file'],'status':'complete-art-candidate-static-review-pending','completeArtParts':True,'generatedMouthParts':54,'derivedEyeParts':54,'visualAcceptance':'Worker inspected all six native mouth contacts and bounded E/atlas-edge repairs. Parent reviewed derived eyes and neutral pilot. Full54 mouth parent review pending.','temporalAcceptance':None,'audiovisualAcceptance':None,'published':False,'userApproved':False,'voiceRoute':'unassigned; no voice generated or inherited','eyeSets':eyes,'thinkingEyeSetsByEmotion':thinking,'mouthSets':mouths,'provenance':{'derivedEyeManifest':ref(EP),'ownMouthManifests':sources,'mouthMastersManifest':ref(HERE/'npc220-mouth-masters-v1/masters-manifest.json')},'mouthMaskFullCoordinates':[22,35,43,48],'limitations':['Eye source compatibility preserves donor limitations: dark native aperture, darker HALF as highlight is occluded; Sad/Afraid expressions close.','G/FV upper-teeth to lower-lip contact is not clearly readable; no blanket phoneme pass.','Neutral and Happy E have independent corrections preserving own round upper contour. C/E distinction remains subtle at native size.','Angry E/F atlas border contamination at full y35 was replaced only with immutable original skin row.','No temporal, audiovisual, publication or user acceptance.']}
 (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2));contact.save(OUT/'mouths54-contact.png')
 allowed=np.zeros((64,64),bool)
 for x,y in eye['featureReuse']['changedPixelUnions']['eyes']:allowed[y,x]=True
 allowed[35:48,22:43]=True;bad=sum(not np.array_equal(np.concatenate((t,b),axis=0)[~allowed],orig[~allowed]) for t in tops for b in bottoms);anchors={}
 for mood in mouths:
  a={f['shape']:np.array(Image.open(OUT/f['file']).convert('RGB')) for f in mood['frames']};anchors[mood['id']]={s:bool(np.array_equal(a['B'][:5,22:43],a[s][:5,22:43])) for s in 'CDH'}
 check={'combinations':len(tops)*len(bottoms),'outsideUnionMaskFailures':bad,'sourceExactNeutralA':bool(np.array_equal(bottoms[0],orig[35:])),'sourceExactNeutralX':bool(np.array_equal(bottoms[8],orig[35:])),'ownBUpperToothAnchorCDH':anchors,'EUpperContourPolicy':'Independent generated contour, no B-width copy. Static row4 inspection only.','technicalPass':bad==0 and all(all(a.values()) for a in anchors.values()),'visualPass':None,'audiovisualPass':None,'manifestSha256':sha(OUT/'manifest.json')};(OUT/'composition-gate.json').write_text(json.dumps(check,indent=2));print(json.dumps(check))
if __name__=='__main__':main()
