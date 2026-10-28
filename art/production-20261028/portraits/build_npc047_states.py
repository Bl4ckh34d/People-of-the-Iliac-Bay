from pathlib import Path
from PIL import Image,ImageDraw
import json,hashlib,shutil
import numpy as np
HERE=Path(__file__).resolve().parent;OUT=HERE/'npc047-eyes-v1';APERTURES=[(19,26,28,28),(37,26,43,28)];HALVES=[(17,25,30,27),(35,25,44,27)]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 m=json.loads((HERE/'npc047-masters-v1/masters-manifest.json').read_text());source=np.array(Image.open(m['source']).convert('RGB'));laterals={};frames=[];sheet=Image.new('RGB',(9*192,6*216),'#222222');sd=ImageDraw.Draw(sheet)
 for gaze in ['left','right']:
  receipt=json.loads((OUT/f'neutral-{gaze}-receipt.json').read_text());rawp=OUT/f'neutral-{gaze}-raw.png';shutil.copyfile(receipt['rawSource'],rawp);laterals[gaze]=Image.open(rawp).convert('RGB').resize((64,35),Image.Resampling.NEAREST)
 for row,e in enumerate(m['masters']):
  mood=e['mood'];folder=OUT/mood;folder.mkdir(exist_ok=True);opened=Image.open(OUT/f'{mood}-open-native.png').convert('RGB');h=Image.open(OUT/f'{mood}-half-selected.png');closed=Image.open(OUT/f'{mood}-closed-native.png');master=Image.open(e['file']).convert('RGB');contact=Image.new('RGB',(768,840),'#222222');d=ImageDraw.Draw(contact);entries=[]
  assert all(np.array_equal(np.array(opened)[b[1]:b[3],b[0]:b[2]],source[b[1]:b[3],b[0]:b[2]]) for b in APERTURES)
  for gi,gaze in enumerate(['front','left','right']):
   eye=opened.copy()
   if gaze!='front':[eye.paste(laterals[gaze].crop(b),b[:2]) for b in APERTURES]
   half=eye.copy();[half.paste(h.crop(b),b[:2]) for b in HALVES];assert np.array_equal(np.array(half)[27:],np.array(eye)[27:])
   for pi,(phase,im) in enumerate([('open',eye),('half',half),('closed',closed)]):
    target=folder/f'{gaze}-{phase}.png';im.save(target);entry={'mood':mood,'gaze':gaze,'phase':phase,'file':str(target),'sha256':sha(target)};frames.append(entry);entries.append(entry);full=master.copy();full.paste(im,(0,0));contact.paste(full.resize((256,256),Image.Resampling.NEAREST),(pi*256,gi*280+24));d.text((pi*256+3,gi*280+5),mood+' '+gaze+' '+phase,fill='white');col=gi*3+pi;sheet.paste(full.resize((192,192),Image.Resampling.NEAREST),(col*192,row*216+24));sd.text((col*192+3,row*216+5),mood+' '+gaze+' '+phase,fill='white')
  contact.save(folder/'review.png');(folder/'manifest.json').write_text(json.dumps({'record':47,'mood':mood,'frames':entries,'gazeApertureBoxes':APERTURES,'halfLidBoxes':HALVES,'exposedIrisRow':27,'sameFaceCompatibility':'Every mood FRONT aperture exactly source26..27 before same-face lateral aperture transfer.','limits':['Both eyes visible; gold hood outside masks exact.','Sclera brightness and HALF readability require native inspection.','No temporal/AV/user acceptance.']},indent=2))
 sheet.save(OUT/'selected54-contact.png');(OUT/'selected54-manifest.json').write_text(json.dumps({'record':47,'source':m['source'],'sourceSha256':m['sourceSha256'],'frames':frames,'masterManifest':str(HERE/'npc047-masters-v1/masters-manifest.json'),'status':'static-candidate-review-pending','limitations':['Both exposed eyes; same-face aperture reuse after six original-exact comparisons.','Insufficient HALF raw in neutral/angry/sad/afraid repaired with compatible own Happy generated lid row26; exposed per-gaze iris27 fixed.','Emotion readability restricted by hood occlusion.','Lateral brightness and temporal review remain open.']},indent=2))
if __name__=='__main__':main()
