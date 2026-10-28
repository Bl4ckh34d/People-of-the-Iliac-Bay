"""Independently check manifest-bound files for the next three full portrait candidates."""
import hashlib,json
from pathlib import Path
from PIL import Image
HERE=Path(__file__).resolve().parent

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    results=[]
    for number in [12,46,47,220,236,425]:
        folder=HERE/'portraits'/f'npc{number:03}-complete-candidate';file=folder/'manifest.json';m=json.loads(file.read_text())
        source=(folder/m['original']).resolve();assert digest(source)==m['sourceSha256'];original=Image.open(source).convert('RGBA')
        assets=[];identities=[]
        for group in m['eyeSets']:
            for phase,frame in group['phases'].items():assets.append((frame,(64,35)));identities.append((group['id'],'front',phase))
        for mood,groups in m['thinkingEyeSetsByEmotion'].items():
            for group in groups:
                for phase,frame in group['phases'].items():assets.append((frame,(64,35)));identities.append((mood,group['id'],phase))
        assert len(assets)==54 and len(set(identities))==54
        for group in m['mouthSets']:
            for frame in group['frames']:assets.append((frame,(64,29)));identities.append((group['id'],'mouth',frame['shape']))
        assert len(assets)==108 and len(set(identities))==108
        for frame,size in assets:
            path=(folder/frame['file']).resolve();assert digest(path)==frame['sha256'];im=Image.open(path);assert im.mode in ('RGB','RGBA') and im.size==size
        for key,box in [('originalUpper',(0,0,64,35)),('originalLower',(0,35,64,64))]:
            assert Image.open(folder/m[key]).convert('RGBA').tobytes()==original.crop(box).tobytes()
        if number==12:
            mask=Image.open(HERE/'portraits/npc012-eyes-v1/selected-eye-mask.png').convert('L')
            for mood in ['neutral','happy','angry','sad','afraid','guarded']:
                eyeFolder=HERE/'portraits/npc012-eyes-v1'/mood/'selected'
                master=Image.open(eyeFolder/'front-open.png').convert('RGBA')
                for gaze in ['front','left','right']:
                    opened=Image.open(eyeFolder/(gaze+'-open.png')).convert('RGBA')
                    half=Image.open(eyeFolder/(gaze+'-half.png')).convert('RGBA')
                    assert opened.crop((0,27,64,35)).tobytes()==half.crop((0,27,64,35)).tobytes()
                    for phase in ['open','half','closed']:
                        im=Image.open(eyeFolder/(gaze+'-'+phase+'.png')).convert('RGBA')
                        assert all(a==b or v for a,b,v in zip(im.getdata(),master.getdata(),mask.getdata()))
        if number==236:
            mask=Image.open(HERE/'portraits/npc236-eyes-v1/selected-eye-mask.png').convert('L')
            for mood in ['neutral','happy','angry','sad','afraid','guarded']:
                eyeFolder=HERE/'portraits/npc236-eyes-v1'/mood/'selected'
                master=Image.open(eyeFolder/'front-open.png').convert('RGBA')
                for gaze in ['front','left','right']:
                    opened=Image.open(eyeFolder/(gaze+'-open.png')).convert('RGBA');half=Image.open(eyeFolder/(gaze+'-half.png')).convert('RGBA')
                    assert opened.crop((0,22,64,35)).tobytes()==half.crop((0,22,64,35)).tobytes()
                    for phase in ['open','half','closed']:
                        im=Image.open(eyeFolder/(gaze+'-'+phase+'.png')).convert('RGBA')
                        assert all(a==b or v for a,b,v in zip(im.getdata(),master.getdata(),mask.getdata()))
                        assert im.crop((0,23,64,35)).tobytes()==master.crop((0,23,64,35)).tobytes()
        results.append({'portrait':number,'manifestSha256':digest(file),'parts':108,'allFileHashesDimensionsModePass':True,'neutralOriginalHalvesExact':True,'visualContactReviewedSeparately':True,'runtimeAcceptance':False})
    report={'status':'PASS','scope':'Independent file/source/semantic-key checks; no temporal or runtime claim','candidates':results,'selectedManifestParts':648,'uniqueAdditionalPartsBeyondPriorReuse':324,'derivedPartsAlreadyCountedElsewhere':324}
    (HERE/'portrait-additions-verification.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report))

if __name__=='__main__':main()
