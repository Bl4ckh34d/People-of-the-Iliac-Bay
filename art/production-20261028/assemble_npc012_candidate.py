"""Bind own012 eyes to previously proved exact-derived075 mouth parts without copying art."""
import copy,hashlib,json,os
from pathlib import Path
from PIL import Image,ImageDraw

BASE=Path(__file__).resolve().parent/'portraits'
OUT=BASE/'npc012-complete-candidate'
MOODS=['neutral','happy','angry','sad','afraid','guarded']

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def relative(p):return Path(os.path.relpath(p,OUT)).as_posix()

def main():
    OUT.mkdir(exist_ok=True)
    mouthsPath=BASE/'exact-feature-reuse-wave2/common-012/manifest.json'
    mouths=json.loads(mouthsPath.read_text());eyesPath=BASE/'npc012-eyes-v1/selected-manifest.json'
    eyes=json.loads(eyesPath.read_text());original=(mouthsPath.parent/mouths['original']).resolve()
    assert digest(original)==mouths['sourceSha256']
    originalImage=Image.open(original).convert('RGBA')
    manifest={key:mouths[key] for key in ['schemaVersion','portraitRecord','bank','width','height','splitY','sourceSha256']}
    manifest.update({'original':relative(original),'status':'complete-art-candidate-static-reviewed-with-limits','completeArtParts':True,'ownEyeParts':54,'derivedMouthParts':54,'newlyGeneratedPartCount':54,'visualAcceptance':'Root inspected six masters, six endpoints, native row diagnostics, all54 final eyes and existing54 derived mouths. Static candidate only.','temporalAcceptance':None,'audiovisualAcceptance':None,'published':False,'userApproved':False,'voiceRoute':'unassigned individual voice; no donor voice inherited','eyeSets':[],'thinkingEyeSetsByEmotion':{},'mouthSets':copy.deepcopy(mouths['mouthSets']),'provenance':{'eyesManifest':relative(eyesPath),'eyesManifestSha256':digest(eyesPath),'derivedMouthManifest':relative(mouthsPath),'derivedMouthManifestSha256':digest(mouthsPath)},'limitations':eyes['limitations']+mouths['limitations']})
    frames={}
    for frame in eyes['frames']:
        file=Path(frame['path']);assert digest(file)==frame['sha256']
        frames[(frame['mood'],frame['gaze'],frame['phase'])]={'file':relative(file),'sha256':digest(file)}
    for mood in MOODS:
        manifest['eyeSets'].append({'id':mood,'phases':{phase:frames[(mood,'front',phase)] for phase in ['open','half','closed']}})
        manifest['thinkingEyeSetsByEmotion'][mood]=[{'id':gaze,'phases':{phase:frames[(mood,gaze,phase)] for phase in ['open','half','closed']}} for gaze in ['left','right']]
    for group in manifest['mouthSets']:
        for frame in group['frames']:
            path=(mouthsPath.parent/frame['file']).resolve();assert digest(path)==frame['sha256'];frame['file']=relative(path)
    manifest['originalUpper']=frames[('neutral','front','open')]['file'];manifest['originalLower']=manifest['mouthSets'][0]['frames'][0]['file']
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    upper=[Image.open(OUT/f['file']).convert('RGBA') for f in frames.values()]
    lower=[Image.open(OUT/f['file']).convert('RGBA') for g in manifest['mouthSets'] for f in g['frames']]
    assert len(upper)==len(lower)==54 and all(im.size==(64,35) for im in upper) and all(im.size==(64,29) for im in lower)
    assert upper[0].tobytes()==originalImage.crop((0,0,64,35)).tobytes()
    assert lower[0].tobytes()==originalImage.crop((0,35,64,64)).tobytes()
    allowed=Image.new('L',(64,64),0);allowed.paste(Image.open(BASE/'npc012-masters-v1/eye-envelope-mask.png').convert('L'),(0,0))
    for x,y in mouths['featureReuse']['changedPixelUnions']['mouths']:allowed.putpixel((x,y),255)
    sourcePixels=list(originalImage.getdata());allowedPixels=list(allowed.getdata());checked=0
    for top in upper:
        for bottom in lower:
            im=Image.new('RGBA',(64,64));im.paste(top,(0,0));im.paste(bottom,(0,35))
            assert all(a==b or m for a,b,m in zip(im.getdata(),sourcePixels,allowedPixels));checked+=1
    review=Image.new('RGB',(9*192,6*216),(35,39,43));d=ImageDraw.Draw(review)
    for row,mood in enumerate(MOODS):
        top=Image.open(OUT/frames[(mood,'front','open')]['file']).convert('RGBA')
        for col,frame in enumerate(manifest['mouthSets'][row]['frames']):
            full=originalImage.copy();full.paste(top,(0,0));full.paste(Image.open(OUT/frame['file']),(0,35))
            d.text((col*192+3,row*216+3),mood+' '+frame['shape'],fill='white');review.paste(full.resize((192,192),Image.Resampling.NEAREST),(col*192,row*216+24))
    review.save(OUT/'mouths54-contact.png')
    result={'status':'PASS structural/source checks only','manifestSha256':digest(OUT/'manifest.json'),'eyeParts':54,'mouthParts':54,'uniqueAdditionalEyeFiles':54,'mouthFilesReferencedWithoutCopy':54,'combinationsChecked':checked,'outsideSourceMasksExact':True,'neutralOriginalExact':True,'runtimeAcceptance':False,'voiceComplete':False}
    (OUT/'verification.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result))

if __name__=='__main__':main()
