from pathlib import Path
from PIL import Image,ImageDraw
import json,hashlib,shutil
import numpy as np
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc046-eyes-v1';APERTURE=(21,22,28,24);HALF=(19,21,30,23)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 m=json.loads((HERE/'npc046-masters-v1/masters-manifest.json').read_text());source=np.array(Image.open(m['source']).convert('RGB'));laterals={};frames=[];sheet=Image.new('RGB',(9*192,6*216),'#222222');sd=ImageDraw.Draw(sheet)
 for gaze in ['left','right']:
  receipt=json.loads((OUT/f'neutral-{gaze}-receipt.json').read_text());rawp=OUT/f'neutral-{gaze}-raw.png';shutil.copyfile(receipt['rawSource'],rawp);laterals[gaze]=Image.open(rawp).convert('RGB').resize((64,35),Image.Resampling.NEAREST)
 for row,e in enumerate(m['masters']):
  mood=e['mood'];folder=OUT/mood;folder.mkdir(exist_ok=True);opened=Image.open(OUT/f'{mood}-open-native.png').convert('RGB');h=Image.open(OUT/f'{mood}-half-native.png');closed=Image.open(OUT/f'{mood}-closed-native.png');master=Image.open(e['file']).convert('RGB');contact=Image.new('RGB',(768,840),'#222222');d=ImageDraw.Draw(contact);entries=[]
  assert np.array_equal(np.array(opened)[22:24,21:28],source[22:24,21:28])
  for gi,gaze in enumerate(['front','left','right']):
   eye=opened.copy()
   if gaze!='front':eye.paste(laterals[gaze].crop(APERTURE),APERTURE[:2])
   half=eye.copy();half.paste(h.crop(HALF),HALF[:2]);assert np.array_equal(np.array(half)[23:],np.array(eye)[23:])
   for pi,(phase,im) in enumerate([('open',eye),('half',half),('closed',closed)]):
    target=folder/f'{gaze}-{phase}.png';im.save(target);entry={'mood':mood,'gaze':gaze,'phase':phase,'file':str(target),'sha256':sha(target)};frames.append(entry);entries.append(entry);full=master.copy();full.paste(im,(0,0));contact.paste(full.resize((256,256),Image.Resampling.NEAREST),(pi*256,gi*280+24));d.text((pi*256+3,gi*280+5),mood+' '+gaze+' '+phase,fill='white');col=gi*3+pi;sheet.paste(full.resize((192,192),Image.Resampling.NEAREST),(col*192,row*216+24));sd.text((col*192+3,row*216+5),mood+' '+gaze+' '+phase,fill='white')
  contact.save(folder/'review.png');(folder/'manifest.json').write_text(json.dumps({'record':46,'mood':mood,'frames':entries,'gazeApertureBox':APERTURE,'halfLidBox':HALF,'exposedIrisRow':23,'sameFaceCompatibility':'Every mood FRONT aperture exactly source22..23 before same-face lateral aperture transfer.','limits':['Only image-left eye visible; occluded side exact.','Sclera brightness and HALF readability require native inspection.','No temporal/AV/user acceptance.']},indent=2))
 sheet.save(OUT/'selected54-contact.png');(OUT/'selected54-manifest.json').write_text(json.dumps({'record':46,'source':m['source'],'sourceSha256':m['sourceSha256'],'frames':frames,'masterManifest':str(HERE/'npc046-masters-v1/masters-manifest.json'),'status':'static-candidate-review-pending','limitations':['One visible eye only, same-face aperture reuse after six originalexact comparisons.','Generated HALF raw moved pupil downward; selected upperlid covers original per-gaze iris instead.','Emotion readability restricted by hood occlusion.','Lateral brightness and temporal review remain open.']},indent=2))
if __name__=='__main__':main()
