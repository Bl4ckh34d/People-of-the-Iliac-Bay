"""Compose generated236 expressions with source pixels outside calibrated brow/eye masks."""
import hashlib,json
from pathlib import Path
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent
MOODS=['neutral','happy','angry','sad','afraid','guarded']
POLYGONS=[[(17,16),(22,13),(29,13),(31,16),(31,23),(17,23)],[(34,14),(44,13),(49,16),(49,23),(34,23)]]
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    source=json.loads((HERE/'source.json').read_text());path=Path(source['source']);assert digest(path)==source['sha256']
    original=Image.open(path).convert('RGBA');mask=Image.new('L',(64,64));d=ImageDraw.Draw(mask)
    for poly in POLYGONS:d.polygon(poly,fill=255)
    mask.save(HERE/'eye-envelope-mask.png')
    page=Image.new('RGB',(1536,560),(35,39,43));draw=ImageDraw.Draw(page);rows=[]
    for col,mood in enumerate(MOODS):
        rawPath=path if mood=='neutral' else HERE/(mood+'-raw.png')
        raw=Image.open(rawPath).convert('RGBA');assert raw.width==raw.height
        native=raw.resize((64,64),Image.Resampling.NEAREST)
        master=original.copy() if mood=='neutral' else Image.composite(native,original,mask)
        output=HERE/(mood+'-master.png');master.save(output);native.save(HERE/(mood+'-raw-native.png'))
        upper=master.crop((0,0,64,35));upper.save(HERE/(mood+'-upper.png'));upper.resize((768,420),Image.Resampling.NEAREST).save(HERE/(mood+'-upper-reference.png'))
        assert all(v or a==b for v,a,b in zip(mask.getdata(),master.getdata(),original.getdata()))
        assert master.crop((0,24,64,64)).tobytes()==original.crop((0,24,64,64)).tobytes()
        for row,(label,pic) in enumerate([('raw native',native),('source-preserving',master)]):
            draw.text((col*256+4,row*280+4),mood+' '+label,fill='white');page.paste(pic.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24))
        rows.append({'mood':mood,'file':str(output),'sha256':digest(output),'raw':str(rawPath),'rawSha256':digest(rawPath),'outsideMaskExact':True})
    page.save(HERE/'masters-review.png')
    report={'record':236,'bank':source['bank'],'source':str(path),'sourceSha256':digest(path),'eyeEnvelopePolygons':POLYGONS,'nativeEyeApertureRows':[21,22],'fixedLowerFaceFromRow':24,'sampling':'Whole square NN64x64; no independent iris shift','masters':rows,'visualAcceptance':None,'limitations':['No blink or gaze states selected yet. Original mouth retained.'] }
    (HERE/'masters-manifest.json').write_text(json.dumps(report,indent=2)+'\n')
    print('6 masters composed; exact outside mask and row24 onward')
if __name__=='__main__':main()
