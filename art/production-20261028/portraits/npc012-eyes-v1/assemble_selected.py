"""Compose reviewed same-face lids over stationary matching-gaze eye contents."""
import hashlib,json
from pathlib import Path
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent
MOODS=['neutral','happy','angry','sad','afraid','guarded']
GAZES=['front','left','right']

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    mask=Image.open(HERE/'moving-eye-mask.png').convert('L');mask.paste(0,(0,28,64,35))
    lidmask=mask.copy();lidmask.paste(0,(0,27,64,35))
    mask.save(HERE/'selected-eye-mask.png');lidmask.save(HERE/'selected-half-lid-mask.png')
    records=[];page=Image.new('RGB',(9*192,6*130),(35,39,43));draw=ImageDraw.Draw(page)
    for row,mood in enumerate(MOODS):
        folder=HERE/mood;out=folder/'selected';out.mkdir(exist_ok=True)
        master=Image.open(folder/'front-open.png').convert('RGBA')
        closedRaw=Image.open(folder/'closed-raw-native.png').convert('RGBA')
        closed=Image.composite(closedRaw,master,mask)
        donorMood='neutral' if mood=='sad' else mood
        donorPath=HERE/donorMood/'left-half-raw-cell.png'
        donor=Image.open(donorPath).convert('RGBA')
        for gazeIndex,gaze in enumerate(GAZES):
            openPath=folder/'front-open.png' if gaze=='front' else folder/(gaze+'-open-raw-cell.png')
            rawOpen=Image.open(openPath).convert('RGBA')
            opened=master.copy() if gaze=='front' else Image.composite(rawOpen,master,mask)
            half=Image.composite(donor,opened,lidmask)
            assert half.crop((0,27,64,35)).tobytes()==opened.crop((0,27,64,35)).tobytes()
            for phaseIndex,(phase,im) in enumerate([('open',opened),('half',half),('closed',closed)]):
                file=out/(gaze+'-'+phase+'.png');im.save(file)
                assert all(a==b or v for a,b,v in zip(master.getdata(),im.getdata(),mask.getdata()))
                assert im.crop((0,28,64,35)).tobytes()==master.crop((0,28,64,35)).tobytes()
                col=gazeIndex*3+phaseIndex;draw.text((col*192+2,row*130+2),mood+' '+gaze+' '+phase,fill='white');page.paste(im.resize((192,105),Image.Resampling.NEAREST),(col*192,row*130+20))
                records.append({'mood':mood,'gaze':gaze,'phase':phase,'path':str(file),'sha256':digest(file),'masterSha256':digest(folder/'front-open.png'),'outsideMovingMaskExact':True,'lowerRows28Through34Exact':True,'halfRetainedMatchingOpenRow27':phase=='half','halfLidDonor':str(donorPath) if phase=='half' else None,'halfLidDonorSha256':digest(donorPath) if phase=='half' else None,'openSource':str(openPath),'openSourceSha256':digest(openPath)})
            sequence=[opened,half,closed,half,opened]
            sequence[0].resize((384,210),Image.Resampling.NEAREST).save(out/(gaze+'-blink.gif'),save_all=True,append_images=[im.resize((384,210),Image.Resampling.NEAREST) for im in sequence[1:]],duration=[600,150,180,150,600],loop=0,disposal=2)
    page.save(HERE/'selected54-contact.png')
    report={'status':'54 static candidates awaiting root composite review; no temporal/runtime acceptance','portraitRecord':12,'nativeSize':[64,35],'sourceApertureRows':[26,27],'afraidApertureRows':[25,26,27],'fixedLowerBoundary':28,'halfRetainedIrisRow':27,'method':'Fixed own-mood FRONT OPEN outside eye mask. Own lateral OPEN art clipped by unchanged native lower eyelid. Generated same-face LEFT HALF upper-lid rows24..26 occlude stationary matching-gaze OPEN row27. Sad uses neutral lid because sad atlas HALF did not descend. CLOSED is own-mood endpoint. No iris translation, palette recoloring or hand-painted pixels.','limitations':['Lateral eyes show more pale sclera and different highlight detail than native FRONT.','Some mood differences restrained.','Masks and source equality do not establish temporal or user acceptance.'],'frames':records}
    (HERE/'selected-manifest.json').write_text(json.dumps(report,indent=2)+'\n');print('54 selected candidates; source lower boundary and matching iris row verified')

if __name__=='__main__':main()
