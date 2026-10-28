"""Fit276's own generated brows under the original diagonal green hood; preserve visible source eyes."""
import hashlib,json
from pathlib import Path
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent
MOODS=['neutral','happy','angry','sad','afraid','guarded']
POLYGON=[(25,16),(28,14),(31,14),(31,20),(20,20),(22,18)]
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    source=json.loads((HERE/'source.json').read_text());path=Path(source['source']);assert digest(path)==source['sha256'];original=Image.open(path).convert('RGBA')
    mask=Image.new('L',(64,64));ImageDraw.Draw(mask).polygon(POLYGON,fill=255);mask.save(HERE/'brow-mask.png')
    page=Image.new('RGB',(1536,560),(35,39,43));d=ImageDraw.Draw(page);records=[]
    for col,mood in enumerate(MOODS):
        rawPath=path if mood=='neutral' else HERE/(mood+'-raw.png');raw=Image.open(rawPath).convert('RGBA');assert raw.width==raw.height
        native=raw.resize((64,64),Image.Resampling.NEAREST);native.save(HERE/(mood+'-raw-native.png'))
        master=original.copy() if mood=='neutral' else Image.composite(native,original,mask);out=HERE/(mood+'-master.png');master.save(out)
        master.crop((0,0,64,35)).save(HERE/(mood+'-upper.png'));master.crop((0,0,64,35)).resize((768,420),Image.Resampling.NEAREST).save(HERE/(mood+'-upper-reference.png'))
        assert all(v or a==b for v,a,b in zip(mask.getdata(),master.getdata(),original.getdata()))
        assert master.crop((0,21,64,64)).tobytes()==original.crop((0,21,64,64)).tobytes()
        assert master.crop((32,0,64,64)).tobytes()==original.crop((32,0,64,64)).tobytes()
        for row,(label,pic) in enumerate([('raw native',native),('source-preserving',master)]):
            d.text((col*256+3,row*280+3),mood+' '+label,fill='white');page.paste(pic.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24))
        records.append({'mood':mood,'file':str(out),'sha256':digest(out),'raw':str(rawPath),'rawSha256':digest(rawPath),'outsideBrowMaskExact':True,'nativeApertureAndGoldIrisExact':True})
    page.save(HERE/'masters-review.png')
    (HERE/'masters-manifest.json').write_text(json.dumps({'portraitRecord':276,'bank':source['bank'],'source':str(path),'sourceSha256':digest(path),'nativeSize':[64,64],'browPolygon':POLYGON,'nativeEyeApertureRows':[21,22],'occlusion':'Right side x32 onward original-exact; hood partly covers right eye. Left brow fitted to diagonal opening. Native eyes retained because raw sad/fear brighten/enlarge iris.','masters':records,'visualAcceptance':None,'limitations':['Masters only. No blink/gaze or temporal acceptance.','Expressions limited by hood and source-exact open apertures.']},indent=2)+'\n')
    print('6 own276 brow masters; visible source eyes and right occlusion exact')
if __name__=='__main__':main()
