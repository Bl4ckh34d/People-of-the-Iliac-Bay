"""Inspect236 raw atlas cells before selecting blink/gaze art."""
import hashlib,json
from pathlib import Path
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent
MOODS=['neutral','happy','angry','sad','afraid','guarded'];GAZES=['front','left','right'];PHASES=['open','half','closed']
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    selected=[m for m in MOODS if (HERE/m/'atlas-raw.png').is_file()]
    page=Image.new('RGB',(9*256,len(selected)*360),(35,39,43));d=ImageDraw.Draw(page);records=[]
    for r,mood in enumerate(selected):
        folder=HERE/mood;raw=Image.open(folder/'atlas-raw.png').convert('RGBA')
        for row,gaze in enumerate(GAZES):
            for col,phase in enumerate(PHASES):
                box=[col*raw.width/3,row*raw.height/3,(col+1)*raw.width/3,(row+1)*raw.height/3]
                im=raw.resize((64,35),Image.Resampling.NEAREST,box=box);path=folder/(gaze+'-'+phase+'-raw-cell.png');im.save(path)
                x=(row*3+col)*256;y=r*360;d.text((x+3,y+3),mood+' '+gaze+' '+phase,fill='white');page.paste(im.resize((256,140),Image.Resampling.NEAREST),(x,y+20))
                detail=im.crop((16,18,49,26)).resize((231,56),Image.Resampling.NEAREST);page.paste(detail,(x+15,y+175))
                for yy in range(18,26):
                    d.text((x+1,y+235+(yy-18)*14),str(yy),fill='white')
                    line=im.crop((16,yy,49,yy+1)).resize((231,12),Image.Resampling.NEAREST);page.paste(line,(x+20,y+235+(yy-18)*14))
                records.append({'mood':mood,'gaze':gaze,'phase':phase,'rawSha256':digest(folder/'atlas-raw.png'),'rawDimensions':list(raw.size),'fractionalBox':box,'cell':str(path),'sha256':digest(path)})
    page.save(HERE/'raw-cells-coordinate-review.png');(HERE/'sampling-receipts.json').write_text(json.dumps(records,indent=2)+'\n');print(len(records))
if __name__=='__main__':main()
