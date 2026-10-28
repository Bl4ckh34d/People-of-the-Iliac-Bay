"""Read-only inventory and nearest-neighbour review contacts for saved portraits."""
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / 'art/portraits/mobile-mouths'
OUT = Path(__file__).resolve().parent
MOODS = ('neutral', 'happy', 'angry', 'sad', 'afraid', 'guarded')

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    catalog_path = BASE / 'split-mask-prototype/portrait-catalog.json'
    catalog = json.loads(catalog_path.read_text(encoding='utf-8-sig'))
    rows = []
    for entry in catalog['entries']:
        row = {'id': entry['id'], 'status': entry['status'], 'animationManifest': entry.get('animationManifest'), 'voiceManifest': entry.get('voiceManifest')}
        source = catalog_path.parent / entry['original']
        row['sourceExists'] = source.is_file()
        if source.is_file():
            row['sourceSha256'] = sha(source)
        if entry.get('animationManifest'):
            mp = (catalog_path.parent / entry['animationManifest']).resolve()
            row['manifestExists'] = mp.is_file()
            if mp.is_file():
                manifest = json.loads(mp.read_text(encoding='utf-8-sig'))
                eyes = list(mp.parent.glob('eyes/*/*/*.png'))
                eyes = [p for p in eyes if not p.stem.endswith('-diff')]
                mouths = [p for p in mp.parent.glob('mouth/*/*.png') if not p.stem.endswith('-diff')]
                row.update(manifestSha256=sha(mp), eyeFiles=len(eyes), mouthFiles=len(mouths), eyeDimensions=sorted({Image.open(p).size for p in eyes}), mouthDimensions=sorted({Image.open(p).size for p in mouths}))
        rows.append(row)
    report = {'catalog': str(catalog_path), 'catalogSha256': sha(catalog_path), 'total': len(rows), 'selectedAnimations': sum(bool(r['animationManifest']) for r in rows), 'entries': rows, 'limitations': ['File coverage is not visual, temporal, audio or user acceptance.', 'Mod sources not yet included in this catalog have no proven coverage.', 'Historical served tree and port8096 unavailable on 2026-10-28.']}
    (OUT/'coverage.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({k: report[k] for k in ('total','selectedAnimations')}))
    for version in ('npc152-keyframe-v2', 'npc152-keyframe-v3', 'npc152-keyframe-v4', 'npc152-keyframe-v5'):
        root = BASE/version
        canvas = Image.new('RGB', (64*4*4, (64*4+24)*6), '#252525')
        draw = ImageDraw.Draw(canvas)
        original = Image.open(root/'original.png').convert('RGB')
        for row,mood in enumerate(MOODS):
            for col,phase in enumerate(('original','open','half','closed')):
                face = original.copy()
                if phase != 'original':
                    face.paste(Image.open(root/f'eyes/{mood}/front/{phase}.png'), (0,0))
                x,y=col*256,row*280
                canvas.paste(face.resize((256,256),Image.Resampling.NEAREST),(x,y+24))
                draw.text((x+4,y+4),f'{mood} {phase}',fill='white')
        canvas.save(OUT/f'{version}-front-contact.png')

if __name__ == '__main__':
    main()
