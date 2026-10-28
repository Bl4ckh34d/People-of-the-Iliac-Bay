"""Combine NPC425 own mouth art with separately proven same-feature eye derivation."""
import json,hashlib,os,copy
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc425-complete-candidate';EP=HERE/'exact-feature-reuse-wave2/common-425/manifest.json';MOODS=('neutral','happy','angry','sad','afraid','guarded')
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
  folder=HERE/'npc425-mouths-v1'/('guarded-v2' if mood=='guarded' else mood);mp=folder/'selected9-manifest.json'
  if not mp.exists():mp=folder/'manifest.json'
  m=json.loads(mp.read_text());sources.append(ref(mp));frames=[];upper=Image.open(OUT/next(s for s in eyes if s['id']==mood)['phases']['open']['file'])
  for col,f in enumerate(m['frames']):
   path=Path(f['file']);assert sha(path)==f['sha256'];frames.append({'shape':f['shape'],**ref(path)});im=Image.open(path).convert('RGB');bottoms.append(np.array(im));full=original.copy();full.paste(upper,(0,0));full.paste(im,(0,35));contact.paste(full.resize((192,192),Image.Resampling.NEAREST),(col*192,row*216+24));draw.text((col*192+3,row*216+5),mood+' '+f['shape'],fill='white')
  mouths.append({'id':mood,'label':mood.title(),'frames':frames})
 manifest={'schemaVersion':2,'portraitRecord':425,'bank':'TFAC00I0.RCI','width':64,'height':64,'splitY':35,'original':ref(source)['file'],'sourceSha256':sha(source),'originalUpper':eyes[0]['phases']['open']['file'],'originalLower':mouths[0]['frames'][0]['file'],'status':'complete-art-candidate-static-review-pending','completeArtParts':True,'generatedMouthParts':54,'derivedEyeParts':54,'visualAcceptance':'Worker inspected all six native mouth contacts. Parent reviewed derived eyes. Full54 mouth and bounded anchor selection parent review pending.','temporalAcceptance':None,'audiovisualAcceptance':None,'published':False,'userApproved':False,'voiceRoute':'individual female voice required but unassigned; no voice generated or inherited; art-only candidate is not complete delivery','eyeSets':eyes,'thinkingEyeSetsByEmotion':thinking,'mouthSets':mouths,'provenance':{'derivedEyeManifest':ref(EP),'ownMouthManifests':sources,'mouthMastersManifest':ref(HERE/'npc425-mouth-masters-v1/masters-manifest.json')},'mouthMaskFullCoordinates':[20,39,45,52],'limitations':['Derived eyes from075 retain lighter and softer lids than original, weak Happy/Guarded separation; parent static inspection only.','G/FV contact uses own mood upper teeth anchored to B and a generated lowerlip. Happy/Sad use compatible same-face neutral G lowerlip. Semantic and temporal review remains open.','Neutral E-v3 is independently generated from B and preserves its own round narrower contour. Sad E uses that same-face upper contour; other E poses retain generated contour. Some E are tall.','Guarded atlas v1 shifted framing and is rejected. Whole-grid v2 preserves source-centered framing and is selected.','No temporal, audiovisual, publication or user acceptance.']}
 (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2));contact.save(OUT/'mouths54-contact.png')
 allowed=np.zeros((64,64),bool)
 for x,y in eye['featureReuse']['changedPixelUnions']['eyes']:allowed[y,x]=True
 allowed[39:52,20:45]=True;bad=sum(not np.array_equal(np.concatenate((t,b),axis=0)[~allowed],orig[~allowed]) for t in tops for b in bottoms);anchors={}
 for mood in mouths:
  a={f['shape']:np.array(Image.open(OUT/f['file']).convert('RGB')) for f in mood['frames']};anchors[mood['id']]={s:bool(np.array_equal(a['B'][4:9,20:45],a[s][4:9,20:45])) for s in 'CDH'}
 check={'combinations':len(tops)*len(bottoms),'outsideUnionMaskFailures':bad,'sourceExactNeutralA':bool(np.array_equal(bottoms[0],orig[35:])),'sourceExactNeutralX':bool(np.array_equal(bottoms[8],orig[35:])),'ownBUpperToothAnchorCDH':anchors,'EUpperContourPolicy':'Own rounded E contour; neutral/Sad selected toothrow8. Other emotional upper lip exposure differs; no B-width copy to E.','technicalPass':bad==0 and all(all(a.values()) for a in anchors.values()),'visualPass':None,'audiovisualPass':None,'manifestSha256':sha(OUT/'manifest.json')};(OUT/'composition-gate.json').write_text(json.dumps(check,indent=2));print(json.dumps(check))
if __name__=='__main__':main()
