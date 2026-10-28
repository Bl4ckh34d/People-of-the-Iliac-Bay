"""Package explicitly selected complete actions, preserving every source RGBA pixel.

No image generation, alpha cleanup, mirroring, resampling or inferred provider aliases.
The selection receipt pins manifests and frame hashes. --check verifies an existing
package without writing. Portrait promotion uses the established catalogue format.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
from PIL import Image

REPO = Path(__file__).resolve().parents[1]
ART = REPO / 'art/production-20261028'
OUT = REPO / 'mod/Textures'
RECEIPT = ART / 'runtime-integration.json'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def png(path, expected=None, pixel=None):
    data = path.read_bytes()
    if expected and sha(data) != expected:
        raise ValueError(f'Changed source PNG: {path}')
    image = Image.open(io.BytesIO(data)).convert('RGBA')
    if pixel and sha(image.tobytes()) != pixel:
        raise ValueError(f'Changed source pixels: {path}')
    if not image.getchannel('A').point(lambda a: 255 if a >= 128 else 0).getbbox():
        raise ValueError(f'Empty frame: {path}')
    return image


def encode(image):
    stream = io.BytesIO()
    image.save(stream, format='PNG')
    return stream.getvalue()


def pack(frames, cell, phases):
    if set(frames) != {(v,p) for v in range(8) for p in range(phases)}:
        raise ValueError('Incomplete or duplicate action directions/phases')
    atlas = Image.new('RGBA', (cell[0]*phases, cell[1]*8))
    for (view, phase), im in frames.items():
        if im.size != tuple(cell):
            raise ValueError('Frame/cell dimension mismatch')
        atlas.paste(im, (phase*cell[0], view*cell[1]))
        if atlas.crop((phase*cell[0],view*cell[1],(phase+1)*cell[0],(view+1)*cell[1])).tobytes()!=im.tobytes():
            raise ValueError('Atlas pixel mismatch')
    if phases>1:
        for view in range(8):
            if len({sha(frames[view,p].tobytes()) for p in range(phases)})!=phases:
                raise ValueError('Repeated poses in action')
    return encode(atlas)


def build():
    assets, entries, sources, excluded = {}, [], {}, []
    def manifest(path):
        sources[str(path.relative_to(REPO)).replace('\\','/')] = sha(path.read_bytes())
        return read(path)
    def add(action, archive, record, frames, cell, pivot, reference, reference_record,
            source, aliases=None, fps=8, provider=''):
        name = f'npc-complete-{archive}-{record}-{action}.png.bytes'
        phases=1 if action=='idle' else 4
        assets[name]=pack(frames,cell,phases)
        for a,r in aliases or [(archive,record)]:
            entries.append(dict(archive=a,record=r,provider=provider,action=action,
                file=name,sha256=sha(assets[name]),cellWidth=cell[0],cellHeight=cell[1],
                phases=phases,pivotX=pivot[0],pivotY=pivot[1],referenceHeight=reference,
                referenceRecord=reference_record,fps=fps,source=str(source.relative_to(REPO)).replace('\\','/')))
    for archive in list(range(381,399))+[451,452,453,454]:
        folder=ART/'mobile-locomotion'/('eight-view-candidate-v1' if archive==385 else f'families/{archive}/candidate-v{3 if archive==396 else 1}')
        path=folder/'manifest-reviewed.json'; m=manifest(path)
        assert m['visual_reviewed'] and m['technical_checked'] and len(m['frames'])==32
        frames={(f['bearing'],f['phase']):png(folder/f['file'],pixel=f['frame_pixel_sha256']) for f in m['frames']}
        add('run',archive,0,frames,m['canvas'],m.get('pivot_pixels_top_left',[80,156]),m.get('contact_pixel_height',110),0,path,fps=m['fps'])
    for archive in range(381,399):
        folder=ART/f'combat/candidate-melee-{archive}-eight-directions'; path=folder/'manifest.json'; m=manifest(path)
        frames={(d['direction'],i):png(folder/f['path'],f['sha256']) for d in m['directions'] for i,f in enumerate(d['frames'])}
        native_height=Image.open(REPO/f'art/standing/source/native-idle/{archive}-0.png').height
        add('melee',archive,0,frames,m['cellSize'],m['pixelFootPivot'],native_height,5,path,fps=7)
    for archive in [381,382,383]:
        for action in ['jump','climb','swim']:
            if archive==383 and action=='swim':continue
            folder=ART/'traversal'/ (f'381-{action}-eight-views-v{2 if action=="swim" else 1}' if archive==381 else f'families/{archive}/{action}-eight-views-v1')
            path=folder/'manifest.json';m=manifest(path)
            assert m.get('visuallyReviewed',True) and len(m['frames'])==32
            frames={(f['bearing'],f['phase']):png(folder/f['file'],f['sha256']) for f in m['frames']}
            cell=m.get('pixelCanvas',m.get('canvas'))
            # All traversal exports use native-idle head scale, fixed across phases.
            # Swim is centred on the chest; crouched poses keep the same pixel scale.
            pivot=[cell[0]/2,cell[1]/2] if action=='swim' else [cell[0]/2,m.get('groundY',190 if action=='jump' else cell[1]-12)]
            add(action,archive,0,frames,cell,pivot,165,5,path,fps=6)
    for a,r in [(182,5),(183,3)]:
        folder=ART/f'stationary-locomotion/family-{a}-{r}';path=folder/'manifest.json';m=manifest(path)
        aliases=[tuple(map(int,x.split(':')[-2:])) for x in m['aliases']]
        for action,data in m['actions'].items():
            if data['candidateViews']!=list(range(8)):
                excluded.append(f'{a}/{r} {action}: incomplete reviewed bearings');continue
            cell=m['cellSize'];frames={}
            for view in data['views']:
                strip=png(folder/f'{action}/view-{view["view"]}-review-native.png')
                assert strip.size==(cell[0]*4,cell[1])
                for f in view['frames']:
                    im=strip.crop((f['phase']*cell[0],0,(f['phase']+1)*cell[0],cell[1]))
                    assert sha(im.tobytes())==f['pixelSha256']
                    frames[view['view'],f['phase']]=im
            add(action,a,r,frames,cell,[cell[0]/2,m['baseline']],m['logicalExtent'][1],r,path,aliases,fps=6 if action=='walk' else 8)
    for action in ['walk','run']:
        folder=ART/f'stationary-locomotion/{action}-182-0';path=folder/f'{action}-review-manifest.json';m=manifest(path)
        assert m.get('completeCandidateViews',m.get('candidateViews'))==list(range(8))
        cell=m['cellSize'];frames={}
        for v in range(8):
            strip=png(folder/f'view-{v}-review-native.png')
            assert strip.size==(cell[0]*4,cell[1])
            sources[str((folder/f'view-{v}-review-native.png').relative_to(REPO)).replace('\\','/')]=sha((folder/f'view-{v}-review-native.png').read_bytes())
            for p in range(4):frames[v,p]=strip.crop((p*cell[0],0,(p+1)*cell[0],cell[1]))
        add(action,182,0,frames,cell,[cell[0]/2,87],76,0,path,fps=6 if action=='walk' else 8)
    path=ART/'idle-sleep/native-idle-candidate-coverage.json';coverage=manifest(path)
    for m in coverage['sets']:
        if not m.get('usableAsCompleteEightViewCandidate') or m['sourcePosture']!='standing':
            excluded.append(f'{m["archive"]}/{m["record"]} idle: repair or uncalibrated changed posture');continue
        cell=m['atlasCellSize'];frames={}
        for f in m['frames']:
            im=png(Path(f['path']),f['sha256']);padded=Image.new('RGBA',tuple(cell))
            assert im.width<=cell[0] and im.height<=cell[1]
            padded.paste(im,((cell[0]-im.width)//2,cell[1]-im.height));frames[f['view'],0]=padded
        aliases=[tuple(map(int,x.split(':')[-2:])) for x in m['aliases']]
        add('idle',m['archive'],m['record'],frames,cell,[cell[0]/2,cell[1]],m['exportHeight'],m['record'],path,aliases,fps=1)
    sleep=[]
    path=ART/'idle-sleep/sleep-candidate-coverage.json'
    for m in manifest(path)['entries']:
        if not m['technicalChecked'] or not m['visualChecked'] or m['candidateStatus']!='reviewable-candidate':
            excluded.append(f'DET {m["archive"]}/{m["record"]} sleep: identity limits');continue
        p=Path(m['file']);png(p,m['sha256'])
        name=f'npc-complete-det-{m["archive"]}-{m["record"]}-sleep.png.bytes';assets[name]=p.read_bytes()
        sleep.append(dict(archive=m['archive'],record=m['record'],provider=m['providerGuid'],file=name))
    keys=[(e['provider'],e['archive'],e['record'],e['action']) for e in entries]
    assert len(keys)==len(set(keys))
    assets['npc-complete-catalog.json.bytes']=(json.dumps(dict(schemaVersion=1,entries=entries),indent=2)+'\n').encode()
    code=['using System.Collections.Generic;','namespace PeopleOfTheIliacBay.Core {','internal static class CompletedSleepArtCatalog {','internal static void AddTo(Dictionary<string,SleepArtCatalog.Entry> entries) {']
    for m in sleep:
        key=f'{m["provider"]}:{m["archive"]}:{m["record"]}'
        code.append(f'entries.Add("{key}",new SleepArtCatalog.Entry({m["archive"]},{m["record"]},"{m["file"]}",-1,"{m["provider"]}"));')
    code+=['}','}','}']
    report=dict(schemaVersion=1,status='packaged-complete-actions',sources=sources,actionMappings=len(entries),
        uniqueActionSets=len({e['file'] for e in entries}),bodyFrames=sum(8*e['phases'] for i,e in enumerate(entries) if e['file'] not in {x['file'] for x in entries[:i]}),
        sleepMappings=len(sleep),excluded=excluded+['run 455/456 incomplete','bow incomplete','383 swim incomplete','183/2 locomotion incomplete','unassembled portrait feature derivatives','DET mobile provider mapping unresolved'],
        assets={k:sha(v) for k,v in assets.items()},runtimeVisualAcceptance=False)
    return assets,'\n'.join(code)+'\n',report


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--check',action='store_true');args=parser.parse_args()
    assets,code,report=build()
    generated=REPO/'src/PeopleOfTheIliacBay.Core/CompletedSleepArtCatalog.cs'
    if args.check:
        for name,data in assets.items():assert (OUT/name).read_bytes()==data,name
        assert generated.read_text()==code
        existing=read(RECEIPT)
        assert existing['sources']==report['sources'] and existing['assets']==report['assets'],'Selection changed since export'
    else:
        OUT.mkdir(exist_ok=True)
        for name,data in assets.items():
            p=OUT/name
            if not p.exists() or p.read_bytes()!=data:p.write_bytes(data)
        generated.write_text(code,encoding='utf-8')
        RECEIPT.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:report[k] for k in ['status','uniqueActionSets','actionMappings','bodyFrames','sleepMappings']}))


if __name__=='__main__':main()
