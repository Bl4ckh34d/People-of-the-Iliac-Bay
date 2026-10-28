"""Sample whole236 CLOSED endpoints and keep each emotion's surrounding original-derived face fixed."""
import hashlib,json
from pathlib import Path
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent
MOODS=['neutral','happy','angry','sad','afraid','guarded']
BOXES=[(18,20,30,23),(35,20,47,23)]
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    mask=Image.new('L',(64,35))
    for x0,y0,x1,y1 in BOXES:mask.paste(255,(x0,y0,x1,y1))
    mask.save(HERE/'moving-eye-mask.png');rows=[]
    page=Image.new('RGB',(1536,468),(35,39,43));draw=ImageDraw.Draw(page)
    for col,mood in enumerate(MOODS):
        master=Image.open(HERE/(mood+'-upper.png')).convert('RGBA')
        rawPath=HERE/('closed-'+mood+'-raw.png');raw=Image.open(rawPath).convert('RGBA')
        assert abs(raw.width/raw.height-64/35)<0.01
        sampled=raw.resize((64,35),Image.Resampling.NEAREST);sampled.save(HERE/('closed-'+mood+'-raw-native.png'))
        closed=Image.composite(sampled,master,mask);path=HERE/(mood+'-closed.png');closed.save(path)
        closed.resize((768,420),Image.Resampling.NEAREST).save(HERE/(mood+'-closed-reference.png'))
        assert all(v or a==b for v,a,b in zip(mask.getdata(),master.getdata(),closed.getdata()))
        for row,(label,pic) in enumerate([('OPEN',master),('raw CLOSED',sampled),('selected CLOSED',closed)]):
            draw.text((col*256+3,row*156+3),mood+' '+label,fill='white');page.paste(pic.resize((256,140),Image.Resampling.NEAREST),(col*256,row*156+16))
        rows.append({'mood':mood,'raw':str(rawPath),'rawSha256':digest(rawPath),'rawDimensions':list(raw.size),'selected':str(path),'selectedSha256':digest(path),'fixedOutsideMovingMask':True})
    page.save(HERE/'endpoints-review.png')
    (HERE/'endpoints-manifest.json').write_text(json.dumps({'portrait':236,'boxesExclusive':BOXES,'maskSha256':digest(HERE/'moving-eye-mask.png'),'endpoints':rows,'visualAcceptance':None,'scope':'Endpoints only; HALF and lateral gaze still missing'},indent=2)+'\n')
    print('6 source-conditioned endpoints; stationary face exact')
if __name__=='__main__':main()
