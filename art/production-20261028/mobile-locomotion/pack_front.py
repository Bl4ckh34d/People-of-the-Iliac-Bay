from pathlib import Path
from PIL import Image, ImageDraw
import json
import hashlib

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'front-cycle-v1'
OUT.mkdir(exist_ok=True)
names=['385-front-near-contact-source.png','385-front-far-flight-v1.png','385-front-far-contact-v1.png','385-front-near-flight-source.png']
W,H,G=144,160,145
data=[]
for index,name in enumerate(names):
    im=Image.open(ROOT/name).convert('RGBA')
    b=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    scale=0.2/(im.height/732)
    data.append((im,b,scale))
head_contact=sum(G-(b[3]-b[1])*s for im,b,s in (data[0],data[2]))/2
frames=[]
entries=[]
for index,(im,b,s) in enumerate(data):
    y=round(G-b[3]*s) if index in (0,2) else round(head_contact-3-b[1]*s)
    x=round(W/2-im.width*s/2)
    frame=Image.new('RGBA',(W,H))
    frame.alpha_composite(im.resize((round(im.width*s),round(im.height*s)),Image.Resampling.NEAREST),(x,y))
    frame.save(OUT/f'frame-{index}.png')
    frames.append(frame)
    entries.append({'phase':index,'source':names[index],'source_sha256':hashlib.sha256((ROOT/names[index]).read_bytes()).hexdigest(),'scale':s,'placement':[x,y]})
atlas=Image.new('RGBA',(W*4,H))
preview=Image.new('RGB',(W*8,H*2+50),'#24282e')
d=ImageDraw.Draw(preview)
for i,f in enumerate(frames):
    atlas.paste(f,(i*W,0))
    big=f.resize((W*2,H*2),Image.Resampling.NEAREST)
    preview.paste(big,(i*W*2,25),big)
    d.text((i*W*2+5,4),['left-contact','right-flight','right-contact','left-flight'][i],fill='white')
    d.line((i*W*2,25+G*2,(i+1)*W*2,25+G*2),fill='#68707a')
atlas.save(OUT/'atlas.png')
preview.save(OUT/'contact-sheet.png')
(OUT/'manifest.json').write_text(json.dumps({'bearing':0,'source_entries':entries,'generated':True,'technical_checked':True,'visual_reviewed':False,'integrated':False,'user_approved':False,'phase_reference':'left-cycle-v2','packing':'Scale normalized by original input resolution; contact baseline aligned; flight head fixed at mean contact head minus3px, not foot-normalized.'},indent=2)+'\n')
print('Packed four distinct frontal phases.')
