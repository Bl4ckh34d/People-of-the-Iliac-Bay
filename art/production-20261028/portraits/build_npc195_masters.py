"""Assemble original-conditioned NPC195 masters for independent still review."""
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw

HERE=Path(__file__).resolve().parent
OUT=HERE/'npc195-masters-v1'
SOURCE=HERE.parents[2]/'art/portraits/mobile-mouths/split-mask-prototype/catalog/common-195.png'
MOODS=('neutral','happy','angry','sad','afraid','guarded')
EYES=((14,12,30,23),(35,12,50,23))
MOUTH=((22,35,43,43),)

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def compose(original,raw,boxes):
    base=np.asarray(original).astype(float);paint=np.asarray(raw).astype(float)
    alpha=np.zeros((64,64),float)
    for x0,y0,x1,y1 in boxes:
        alpha[y0:y1,x0:x1]=1
        alpha[y0,x0:x1]=.5;alpha[y1-1,x0:x1]=.5
        alpha[y0:y1,x0]=.5;alpha[y0:y1,x1-1]=.5
    output=np.rint(base*(1-alpha[:,:,None])+paint*alpha[:,:,None]).astype('uint8')
    assert np.array_equal(output[alpha==0],np.asarray(original)[alpha==0])
    return Image.fromarray(output),alpha

def main():
    original=Image.open(SOURCE).convert('RGB')
    sheet=Image.new('RGB',(6*256,4*280),'#252525');d=ImageDraw.Draw(sheet)
    rows=[]
    for col,mood in enumerate(MOODS):
        if mood=='neutral':
            raw=original;full=original;eyes=original;mouth=original
            entry={'mood':mood,'file':str(SOURCE),'sha256':sha(SOURCE),'originalExact':True}
        else:
            rawpath=OUT/f'{mood}-raw.png';raw=Image.open(rawpath).convert('RGB').resize((64,64),Image.Resampling.NEAREST)
            raw.save(OUT/f'{mood}-raw-native.png')
            full,alpha=compose(original,raw,EYES+MOUTH)
            eyes,_=compose(original,raw,EYES);mouth,_=compose(original,raw,MOUTH)
            full.save(OUT/f'{mood}-master.png');eyes.save(OUT/f'{mood}-eyes-only.png');mouth.save(OUT/f'{mood}-mouth-only.png')
            entry={'mood':mood,'file':str(OUT/f'{mood}-master.png'),'sha256':sha(OUT/f'{mood}-master.png'),'raw':str(rawpath),'rawSha256':sha(rawpath),'rawDimensions':list(Image.open(rawpath).size),'receipt':str(OUT/f'{mood}-receipt.json'),'outsideFeatureEnvelopeOriginalExact':True,'changedPixels':int(np.any(np.asarray(full)!=np.asarray(original),axis=2).sum())}
        rows.append(entry)
        for row,(kind,im) in enumerate((('raw',raw),('composite',full),('eyes only',eyes),('mouth only',mouth))):
            sheet.paste(im.resize((256,256),Image.Resampling.NEAREST),(col*256,row*280+24));d.text((col*256+4,row*280+4),f'{mood} {kind}',fill='white')
    sheet.save(OUT/'masters-review.png')
    report={'portraitRecord':195,'bank':'TFAC00I0.RCI','source':str(SOURCE),'sourceSha256':sha(SOURCE),'eyeBoxes':EYES,'mouthBoxes':MOUTH,'composition':'Whole64x64 nearest-neighbour sampling; opaque feature interiors; half-alpha stationary outer perimeter; all outside pixels original-exact.','masters':rows,'status':'generated-parent-master-review-pending','animationFramesProduced':0,'publication':'not published','voice':'not assigned','limitations':['These are expression masters, not54-eye/54-mouth delivery.','Eyeball shading, brow anatomy, mood readability and mouth selection still require independent review before derived frames.']}
    (OUT/'masters-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({'masters':len(rows),'manifest':str(OUT/'masters-manifest.json'),'sha256':sha(OUT/'masters-manifest.json')}))

if __name__=='__main__':main()
