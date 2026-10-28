"""Create bounded identity contact sheets from the shared source manifest."""
from pathlib import Path
import argparse,json
from PIL import Image,ImageDraw
p=argparse.ArgumentParser()
p.add_argument('archive',type=int)
p.add_argument('start',type=int)
p.add_argument('end',type=int)
a=p.parse_args()
out=Path(__file__).resolve().parent
rows=[x for x in json.loads((out.parent/'additional-mod-sources.json').read_text())['sources'] if x['archive']==a.archive and a.start<=x['record']<=a.end]
rows.sort(key=lambda x:x['record'])
assert len(rows)<=8
stem=f'det-{a.archive}-{a.start}-{a.end}'
sheet=Image.new('RGB',(1000,700),'#303238')
d=ImageDraw.Draw(sheet)
for i,row in enumerate(rows):
    im=Image.open(row['source']).convert('RGBA')
    scale=min(3,220/im.width,310/im.height)
    im=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.NEAREST)
    x,y=i%4*250+(250-im.width)//2,i//4*350+30
    sheet.paste(im,(x,y),im)
    d.text((i%4*250+5,i//4*350+5),str(row['archive'])+'/'+str(row['record']),fill='white')
sheet.save(out/(stem+'-source-review.png'))
(out/(stem+'-source-map.json')).write_text(json.dumps(rows,indent=2),encoding='utf-8')
print(stem)
