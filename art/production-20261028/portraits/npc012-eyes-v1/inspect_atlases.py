"""Sample whole atlas cells and expose source-preserving candidates for review."""
import json,hashlib
from pathlib import Path
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent
MOODS=['neutral','happy','angry','sad','afraid','guarded']

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    mask=Image.open(HERE/'moving-eye-mask.png').convert('L')
    reports=[]
    for mood in MOODS:
        folder=HERE/mood;raw=Image.open(folder/'atlas-raw.png').convert('RGBA')
        master=Image.open(folder/'front-open.png').convert('RGBA');closed=Image.open(folder/'closed.png').convert('RGBA')
        page=Image.new('RGB',(6*320,3*200),(35,39,43));d=ImageDraw.Draw(page)
        for row,gaze in enumerate(['front','left','right']):
            for col,phase in enumerate(['open','half','closed']):
                bounds=(col*raw.width/3,row*raw.height/3,(col+1)*raw.width/3,(row+1)*raw.height/3)
                native=raw.resize((64,35),Image.Resampling.NEAREST,box=bounds)
                native.save(folder/(gaze+'-'+phase+'-raw-cell.png'))
                candidate=master.copy() if (gaze,phase)==('front','open') else closed.copy() if phase=='closed' else Image.composite(native,master,mask)
                target=folder/(gaze+'-'+phase+'-candidate.png');candidate.save(target)
                assert all(a==b or v for a,b,v in zip(master.getdata(),candidate.getdata(),mask.getdata()))
                for offset,(label,im) in enumerate([('raw',native),('composed',candidate)]):
                    x=(col+offset*3)*320;y=row*200
                    d.text((x+3,y+2),gaze+' '+phase+' '+label,fill='white');page.paste(im.resize((320,175),Image.Resampling.NEAREST),(x,y+20))
                reports.append({'mood':mood,'gaze':gaze,'phase':phase,'cellBounds':bounds,'path':str(target),'sha256':digest(target),'outsideMovingMaskExact':True})
        page.save(folder/'atlas-native-review.png')
    (HERE/'atlas-inspection.json').write_text(json.dumps({'status':'diagnostic candidates, not selected','sampling':'whole cell fractional bounds, nearest neighbor, zero offsets','frames':reports},indent=2)+'\n')
    print('54 diagnostic candidate frames, fixed outer face')

if __name__=='__main__':main()
