"""Composite generated 012 eye masters inside the inspected hood opening only."""
import hashlib
import json
from pathlib import Path

from PIL import Image,ImageDraw

HERE=Path(__file__).resolve().parent
MOODS=['neutral','happy','angry','sad','afraid','guarded']
POLYGONS=[[(23,16),(30,16),(31,23),(31,30),(15,30),(17,24),(19,21),(21,18)],
          [(33,16),(37,16),(40,19),(42,21),(45,24),(46,28),(46,30),(33,30)]]


def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    source=json.loads((HERE/'source.json').read_text())
    path=Path(source['source'])
    assert digest(path)==source['sha256']
    original=Image.open(path).convert('RGBA')
    mask=Image.new('L',(64,64),0)
    drawing=ImageDraw.Draw(mask)
    for polygon in POLYGONS: drawing.polygon(polygon,fill=255)
    mask.save(HERE/'eye-envelope-mask.png')
    result=[]
    page=Image.new('RGB',(6*256,2*280),(35,39,43));draw=ImageDraw.Draw(page)
    for column,mood in enumerate(MOODS):
        if mood=='neutral':
            raw=original.copy();native=original.copy();master=original.copy();raw_path=path
        else:
            raw_path=HERE/(mood+'-raw.png')
            raw=Image.open(raw_path).convert('RGBA')
            assert raw.width==raw.height
            native=raw.resize((64,64),Image.Resampling.NEAREST)
            native.save(HERE/(mood+'-raw-native.png'))
            master=Image.composite(native,original,mask)
        output=HERE/(mood+'-master.png')
        master.save(output)
        assert all(m or a==b for m,a,b in zip(mask.getdata(),original.getdata(),master.getdata()))
        assert master.crop((0,31,64,64)).tobytes()==original.crop((0,31,64,64)).tobytes()
        for row,(label,picture) in enumerate((('raw native',native),('source-preserving master',master))):
            draw.text((column*256+3,row*280+4),mood+' '+label,fill='white')
            page.paste(picture.resize((256,256),Image.Resampling.NEAREST),(column*256,row*280+24))
        result.append({'mood':mood,'file':str(output),'sha256':digest(output),'raw':str(raw_path),'rawSha256':digest(raw_path),
                       'outsideEnvelopeExact':True,'mouthAndLowerFaceOriginalExact':True})
    page.save(HERE/'masters-review.png')
    report={'portraitRecord':12,'bank':'TFAC00I0.RCI','source':str(path),'sourceSha256':digest(path),'nativeSize':[64,64],
            'eyeEnvelopePolygons':POLYGONS,'maskSha256':digest(HERE/'eye-envelope-mask.png'),
            'nativeEyeApertureRows':[26,27,28],'sampling':'Whole full-square nearest-neighbor to64x64; no independent pupil shifting.',
            'masters':result,'visualAcceptance':None,'limitations':['Candidate envelopes require native inspection against hood edges and all generated brow/lash footprints.','Happy is restrained; original mouth is retained separately from existing mood mouths.','No blink, lateral gaze, voice, gallery or runtime acceptance.']}
    (HERE/'masters-manifest.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({'masters':len(result),'outsideMaskExact':True,'status':'candidate-awaiting-native-composite-review'}))


if __name__=='__main__': main()
