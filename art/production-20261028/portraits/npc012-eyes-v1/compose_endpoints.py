"""Source-preserving endpoint packing; generated art is sampled, never painted."""
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageChops

HERE=Path(__file__).resolve().parent
MASTERS=HERE.parent/'npc012-masters-v1'
MOODS=['neutral','happy','angry','sad','afraid','guarded']
BOXES=[(16,24,30,29),(34,24,46,29)]

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    envelope=Image.open(MASTERS/'eye-envelope-mask.png').convert('L').crop((0,0,64,35))
    mask=Image.new('L',(64,35),0)
    for box in BOXES:mask.paste(255,box)
    mask=ImageChops.darker(mask,envelope)
    mask.save(HERE/'moving-eye-mask.png')
    page=Image.new('RGB',(3*512,6*300),(35,39,43));d=ImageDraw.Draw(page);records=[]
    for row,mood in enumerate(MOODS):
        folder=HERE/mood
        master=Image.open(MASTERS/(mood+'-master.png')).convert('RGBA').crop((0,0,64,35))
        raw=Image.open(folder/'closed-raw.png').convert('RGBA')
        native=raw.resize((64,35),Image.Resampling.NEAREST)
        closed=Image.composite(native,master,mask)
        native.save(folder/'closed-raw-native.png');closed.save(folder/'closed.png');master.save(folder/'front-open.png')
        closed.resize((768,420),Image.Resampling.NEAREST).save(folder/'closed-reference.png')
        assert all(a==b or v for a,b,v in zip(master.getdata(),closed.getdata(),mask.getdata()))
        assert closed.crop((0,29,64,35)).tobytes()==master.crop((0,29,64,35)).tobytes()
        for col,(label,im) in enumerate([('OPEN',master),('raw CLOSED',native),('composed CLOSED',closed)]):
            d.text((col*512+3,row*300+3),mood+' '+label,fill='white');page.paste(im.resize((512,280),Image.Resampling.NEAREST),(col*512,row*300+20))
        records.append({'mood':mood,'rawSha256':digest(folder/'closed-raw.png'),'rawSize':list(raw.size),'masterSha256':digest(MASTERS/(mood+'-master.png')),'closedSha256':digest(folder/'closed.png'),'outsideMovingMaskExact':True,'lowerRows29Through34Exact':True})
    page.save(HERE/'endpoints-review.png')
    (HERE/'endpoints-manifest.json').write_text(json.dumps({'boxesExclusive':BOXES,'maskIntersection':'masters eye envelope','sampling':'whole upper-half nearest neighbor64x35','records':records,'visualStatus':'pending-native-review'},indent=2)+'\n')
    print('6 endpoints packed; outside mask and lower rows exact')

if __name__=='__main__':main()
