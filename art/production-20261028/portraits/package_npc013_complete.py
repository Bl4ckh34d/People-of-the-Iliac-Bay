"""Reference own occlusion-conditioned eyes and proven exact-feature derived mouths."""
from pathlib import Path
from PIL import Image,ImageDraw
import json,hashlib,os,copy
import numpy as np
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc013-complete-candidate';EP=HERE/'npc013-eyes-v1/selected54-manifest.json';MP=HERE/'exact-feature-reuse-wave2/common-013/manifest.json';MOODS=('neutral','happy','angry','sad','afraid','guarded')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 OUT.mkdir(exist_ok=True);e=json.loads(EP.read_text());mouth=json.loads(MP.read_text());source=Path(e['source']);original=Image.open(source).convert('RGB');orig=np.array(original)
 def ref(p):
  p=Path(p).resolve();return {'file':os.path.relpath(p,OUT).replace(os.sep,'/'),'sha256':sha(p)}
 eyes=[];thinking={};mouths=copy.deepcopy(mouth['mouthSets']);tops=[];bottoms=[];contact=Image.new('RGB',(1728,1296),'#222222');d=ImageDraw.Draw(contact)
 for row,mood in enumerate(MOODS):
  states={g:{ph:ref(next(f['file'] for f in e['frames'] if f['mood']==mood and f['gaze']==g and f['phase']==ph)) for ph in ['open','half','closed']} for g in ['front','left','right']};eyes.append({'id':mood,'phases':states['front']});thinking[mood]=[{'id':g,'phases':states[g]} for g in ['left','right']];upper=Image.open(OUT/states['front']['open']['file'])
  ms=next(s for s in mouths if s['id']==mood)
  for col,f in enumerate(ms['frames']):
   path=MP.parent/f['file'];assert sha(path)==f['sha256'];f.update(ref(path));lower=Image.open(path).convert('RGB');bottoms.append(np.array(lower));full=original.copy();full.paste(upper,(0,0));full.paste(lower,(0,35));contact.paste(full.resize((192,192),Image.Resampling.NEAREST),(col*192,row*216+24));d.text((col*192+3,row*216+5),mood+' '+f['shape'],fill='white')
 for f in e['frames']:assert sha(Path(f['file']))==f['sha256'];tops.append(np.array(Image.open(f['file']).convert('RGB')))
 m={'schemaVersion':2,'portraitRecord':13,'bank':'TFAC00I0.RCI','width':64,'height':64,'splitY':35,'original':ref(source)['file'],'sourceSha256':sha(source),'originalUpper':eyes[0]['phases']['open']['file'],'originalLower':mouths[0]['frames'][0]['file'],'status':'complete-art-candidate-static-review-pending','completeArtParts':True,'generatedEyeParts':54,'derivedMouthParts':54,'visualAcceptance':'Worker inspected six9-eye-state contacts and full54 contact. Parent review pending.','temporalAcceptance':None,'audiovisualAcceptance':None,'published':False,'userApproved':False,'voiceRoute':'individual female voice unassigned; no donor voice inherited; art-only candidate not complete delivery','eyeSets':eyes,'thinkingEyeSetsByEmotion':thinking,'mouthSets':mouths,'provenance':{'ownEyeManifest':ref(EP),'derivedMouthManifest':ref(MP),'masters':ref(HERE/'npc013-masters-v1/masters-manifest.json')},'limitations':['Both eyes exposed; image-right outer corner covered by gold hood remains unchanged.','Mood expression limited by hood; Sad/Afraid close, Happy restrained.','HALF uses own generated lids25..27 over stationary per-gaze irisrow28. Neutral/Happy/Angry/Afraid/Guarded displaced-highlight raw repaired with compatible own Sad lidrow27 after exact original-aperture check.','Lateral gaze uses own generated neutral aperture after all six mood source-aperture comparisons; lateral sclera is brighter than frontal in static view.','Mouths inheritcommon-076 donor art and unverified phoneme/temporal limitations. Exact source compatibility does not improve donor quality.','No temporal, audiovisual, publication or user acceptance.']}
 (OUT/'manifest.json').write_text(json.dumps(m,indent=2));contact.save(OUT/'mouths54-contact.png');allowed=np.zeros((64,64),bool);allowed[:35]=np.array(Image.open(HERE/'npc013-masters-v1/brow-mask.png'))>0;allowed[25:30,17:29]=True;allowed[25:30,36:44]=True
 for x,y in mouth['featureReuse']['changedPixelUnions']['mouths']:allowed[y,x]=True
 bad=sum(not np.array_equal(np.concatenate((t,b),axis=0)[~allowed],orig[~allowed]) for t in tops for b in bottoms);checks={'combinations':len(tops)*len(bottoms),'outsideUnionMaskFailures':bad,'sourceExactNeutralA':bool(np.array_equal(bottoms[0],orig[35:])),'sourceExactNeutralX':bool(np.array_equal(bottoms[8],orig[35:])),'rightHoodFromX44All54Exact':all(np.array_equal(t[:,44:],orig[:35,44:]) for t in tops),'technicalPass':bad==0,'visualPass':None,'temporalPass':None,'manifestSha256':sha(OUT/'manifest.json')};(OUT/'composition-gate.json').write_text(json.dumps(checks,indent=2));print(json.dumps(checks))
if __name__=='__main__':main()
