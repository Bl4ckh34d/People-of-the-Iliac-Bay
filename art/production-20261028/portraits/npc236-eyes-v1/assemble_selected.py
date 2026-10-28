"""Assemble236 own brows, inspected same-face gaze/lid art and stationary matching iris."""
import hashlib,json
from pathlib import Path
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent;MASTERS=HERE.parent/'npc236-masters-v1'
MOODS=['neutral','happy','angry','sad','afraid','guarded'];GAZES=['front','left','right']
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    mask=Image.open(MASTERS/'moving-eye-mask.png').convert('L');lidmask=mask.copy();lidmask.paste(0,(0,22,64,35))
    mask.save(HERE/'selected-eye-mask.png');lidmask.save(HERE/'selected-half-lid-mask.png')
    halfPath=HERE/'neutral/half-single-native.png';halfRaw=Image.open(halfPath).convert('RGBA');rawGazes={}
    for gaze in ['left','right']:
        path=HERE/'neutral'/(gaze+'-single-raw.png');raw=Image.open(path).convert('RGBA');assert abs(raw.width/raw.height-64/35)<0.01
        rawGazes[gaze]=raw.resize((64,35),Image.Resampling.NEAREST);rawGazes[gaze].save(HERE/'neutral'/(gaze+'-single-native.png'))
    records=[];page=Image.new('RGB',(9*192,6*130),(35,39,43));d=ImageDraw.Draw(page)
    for row,mood in enumerate(MOODS):
        folder=HERE/mood/'selected';folder.mkdir(parents=True,exist_ok=True)
        master=Image.open(MASTERS/(mood+'-upper.png')).convert('RGBA');closed=Image.open(MASTERS/(mood+'-closed.png')).convert('RGBA')
        for gi,gaze in enumerate(GAZES):
            opened=master.copy() if gaze=='front' else Image.composite(rawGazes[gaze],master,mask)
            half=Image.composite(halfRaw,opened,lidmask)
            assert half.crop((0,22,64,35)).tobytes()==opened.crop((0,22,64,35)).tobytes()
            for pi,(phase,im) in enumerate([('open',opened),('half',half),('closed',closed)]):
                path=folder/(gaze+'-'+phase+'.png');im.save(path)
                assert all(v or a==b for v,a,b in zip(mask.getdata(),master.getdata(),im.getdata()))
                assert im.crop((0,23,64,35)).tobytes()==master.crop((0,23,64,35)).tobytes()
                col=gi*3+pi;d.text((col*192+2,row*130+2),mood+' '+gaze+' '+phase,fill='white');page.paste(im.resize((192,105),Image.Resampling.NEAREST),(col*192,row*130+20))
                records.append({'mood':mood,'gaze':gaze,'phase':phase,'path':str(path),'sha256':digest(path),'masterSha256':digest(MASTERS/(mood+'-upper.png')),'stationaryOutsideMaskExact':True,'matchingOpenIrisRow22Exact':phase=='half','sharedSameFaceLidDonor':str(halfPath) if phase=='half' else None,'lateralDonor':str(HERE/'neutral'/(gaze+'-single-raw.png')) if gaze!='front' and phase!='closed' else None})
            seq=[opened,half,closed,half,opened];seq[0].resize((384,210),Image.Resampling.NEAREST).save(folder/(gaze+'-blink.gif'),save_all=True,append_images=[i.resize((384,210),Image.Resampling.NEAREST) for i in seq[1:]],duration=[600,150,180,150,600],loop=0,disposal=2)
    page.save(HERE/'selected54-contact.png')
    report={'portraitRecord':236,'status':'candidate awaiting root full-contact inspection','nativeSize':[64,35],'sourceEyeApertureRows':[21,22],'fixedLowerBoundary':23,'halfRetainedIrisRow':22,'method':'Own six original-conditioned brow masters and own six CLOSED endpoints. Single neutral HALF lid rows20/21 occlude same-gaze OPEN row22 without moving iris. Same-face neutral single LEFT/RIGHT eye pixels are shared under own emotional brows at fixed eye coordinates; no foreign portrait transfer. Two reframed3x3 atlases rejected.','newGenerationBasis':'Five emotional masters, six CLOSED endpoints, one single HALF and two single lateral OPEN images;54 state files are not54 newly generated poses.','limitations':['Lateral eyes have stronger purple/tan contrast and different glints.','Shared same-face gaze/lid art reduces lid-expression variation; mood primarily conveyed by own brows.','Sad/fear and happy differences restrained.','No temporal, voice or runtime acceptance.'],'frames':records}
    (HERE/'selected-manifest.json').write_text(json.dumps(report,indent=2)+'\n');print('54 candidates; per-gaze iris22 and surrounding face exact')
if __name__=='__main__':main()
