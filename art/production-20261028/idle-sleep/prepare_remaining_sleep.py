"""Build references for remaining ordinary DET identities; special anatomy stays explicit."""
from pathlib import Path
import json
from PIL import Image,ImageDraw
out=Path(__file__).resolve().parent
rows=json.loads((out.parent/'additional-mod-sources.json').read_text())['sources']
idx={(x['archive'],x['record']):x for x in rows if x['provider']=='daggerfall expanded textures'}
groups=[[(10013,i) for i in [1,2,3]]+[(10016,i) for i in range(5)],[(10014,i) for i in [0,2,3,4,5,6,7,8]],[(10014,i) for i in range(9,13)]+[(10018,i) for i in range(4)],[(10017,i) for i in range(8)],[(10018,4),(10019,0),(10020,0)]+[(10020,i) for i in range(2,7)],[(10020,7)]]
batches=[]
for batch,keys in enumerate(groups):
    refs=[idx[k] for k in keys]
    sheet=Image.new('RGB',(1000,700),'#303238')
    d=ImageDraw.Draw(sheet)
    for i,row in enumerate(refs):
        im=Image.open(row['source']).convert('RGBA')
        scale=min(3,220/im.width,310/im.height)
        im=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.NEAREST)
        sheet.paste(im,(i%4*250+(250-im.width)//2,i//4*350+30),im)
        d.text((i%4*250+5,i//4*350+5),f"{row['archive']}/{row['record']}",fill='white')
    path=out/f'det-remaining-{batch}-source-review.png'
    sheet.save(path)
    batches.append({'batch':batch,'reference':str(path),'sources':refs})
(out/'remaining-sleep-batches.json').write_text(json.dumps(batches,indent=2),encoding='utf-8')
print([len(b['sources']) for b in batches])
