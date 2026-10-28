"""Validate all exported vanilla mobile sitting textures and refresh catalog."""
import csv, json
from pathlib import Path
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parent
expected=list(range(381,399))+list(range(451,457))
rows={int(r['archive']):r for r in csv.DictReader((ROOT.parent/'standing/source/native-idle.csv').open()) if r['frame']=='0'}
catalog=[]
for archive in expected:
    directory=ROOT/str(archive)
    if not (directory/'manifest.json').exists():
        continue
    data=json.loads((directory/'manifest.json').read_text())
    size=(int(rows[archive]['width']),int(rows[archive]['height']))
    assert tuple(data['nativeIdleSize'])==size
    assert len(data['frames'])==8
    assert data['animationLoop'] is False
    for i,frame in enumerate(data['frames']):
        assert frame['orientation']==i
        image=Image.open(directory/frame['file'])
        assert image.mode=='RGBA' and image.size==size
        assert set(image.getchannel('A').getdata())=={0,255}
        assert all((r,g,b)==(0,0,0) for r,g,b,a in image.getdata() if a==0)
    data['nativeIdleScale']=[int(rows[archive]['scaleX']),int(rows[archive]['scaleY'])]
    (directory/'manifest.json').write_text(json.dumps(data,indent=2)+'\n')
    catalog.append({'archive':archive,'manifest':f'{archive}/manifest.json','atlas':f'{archive}/atlas.png','nativeIdleSize':list(size),'nativeIdleScale':data['nativeIdleScale']})
manifest=json.loads((ROOT/'manifest.json').read_text())
manifest['production']=catalog
manifest['exportedAppearances']=len(catalog)
manifest['exportedSingleFrames']=len(catalog)*8
manifest['pendingArchives']=[a for a in expected if a not in [c['archive'] for c in catalog]]
manifest['status']='user-approved pilot style; all vanilla mobile exports validated' if len(catalog)==24 else 'user-approved pilot style; production exports in progress'
(ROOT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(f'PASS: {len(catalog)} appearances, {len(catalog)*8} static frames; exact native dimensions, eight views, binary alpha, transparent RGB zero')
print('Pending:',manifest['pendingArchives'])
