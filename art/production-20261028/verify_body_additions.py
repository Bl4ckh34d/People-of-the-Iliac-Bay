"""Independently check newly root-inspected stationary run and traversal files."""
import hashlib,json
from pathlib import Path
from PIL import Image
HERE=Path(__file__).resolve().parent
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    results=[]
    for name,count in [('jump-eight-views-v1',32),('climb-eight-views-v1',32),('swim-eight-views-v1',32)]:
        folder=HERE/'traversal/families/382'/name;m=json.loads((folder/'manifest.json').read_text())
        assert len(m['frames'])==count;atlasPath=folder/'atlas.png';assert digest(atlasPath)==m['atlasSha256'];atlas=Image.open(atlasPath).convert('RGBA');width,height=m['pixelCanvas']
        for frame in m['frames']:
            path=folder/frame['file'];assert digest(path)==frame['sha256'];im=Image.open(path);assert im.mode=='RGBA' and im.size==(width,height)
            x=frame['phase']*width;y=(frame['bearing'] if count==32 else 0)*height
            assert im.tobytes()==atlas.crop((x,y,x+width,y+height)).tobytes()
        results.append({'candidate':'382/'+name,'frames':count,'fileHashesDimensionsAndAtlasParity':True,'manifestSha256':digest(folder/'manifest.json')})
    folder=HERE/'stationary-locomotion/family-182-5';m=json.loads((folder/'manifest.json').read_text());action=m['actions']['run'];assert action['candidateViews']==list(range(8));count=0
    for view in action['views']:
        strip=Image.open(folder/f"run/view-{view['view']}-review-native.png").convert('RGBA');assert strip.size==(640,160)
        for frame in view['frames']:
            x=frame['phase']*160;assert hashlib.sha256(strip.crop((x,0,x+160,160)).tobytes()).hexdigest()==frame['pixelSha256'];assert digest(folder/frame['source'])==frame['sha256'];count+=1
    assert count==32;results.append({'candidate':'jester182/5-run','frames':32,'sourceHashesAndStripPixelParity':True,'manifestSha256':digest(folder/'manifest.json')})
    report={'status':'PASS','scope':'Additional selected files already root-inspected; static candidate only','additionalFrames':128,'candidates':results,'runtimeAcceptance':False}
    (HERE/'body-additions-verification.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report))

if __name__=='__main__':main()
