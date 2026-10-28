"""Read installed bundle metadata; extract only the five DET scholar references."""
from pathlib import Path
import hashlib
import json
import UnityPy
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[4]
OUT = Path(__file__).resolve().parent
MODS = ROOT / 'Player/DaggerfallUnity_Data/StreamingAssets/Mods'
OUT.mkdir(parents=True, exist_ok=True)
sources = OUT / 'sources'
sources.mkdir(exist_ok=True)
index = []
scholars = []
for bundle in sorted(MODS.glob('*.dfmod')):
    env = UnityPy.load(str(bundle))
    entries = []
    for path, obj in env.container.items():
        entries.append({'path': path, 'type': obj.type.name})
        if bundle.name == 'daggerfall expanded textures.dfmod' and '/10015 scholars/' in path and path.endswith('-0.png'):
            im = obj.read().image.convert('RGBA')
            dest = sources / Path(path).name
            im.save(dest)
            scholars.append({'provider': 'daggerfall expanded textures', 'providerGuid': '00272292-acc6-4e51-ba4c-3a3e89199609', 'archive': 10015, 'record': int(dest.stem.split('_')[1].split('-')[0]), 'bundleAsset': path, 'file': str(dest), 'size': list(im.size), 'pixelSha256': hashlib.sha256(im.tobytes()).hexdigest(), 'pngSha256': hashlib.sha256(dest.read_bytes()).hexdigest()})
    index.append({'bundle': str(bundle), 'sha256': hashlib.file_digest(bundle.open('rb'), 'sha256').hexdigest(), 'containerCount': len(entries), 'assets': entries})
(OUT.parent / 'mod-container-index.json').write_text(json.dumps({'scope': 'All installed dfmod files. Enabled status must be joined from current Mods configuration.', 'bundles': index}, indent=2), encoding='utf-8')
scholars.sort(key=lambda row: row['record'])
(OUT / 'det-scholar-sources.json').write_text(json.dumps(scholars, indent=2), encoding='utf-8')
sheet = Image.new('RGB', (900, 440), '#303238')
draw = ImageDraw.Draw(sheet)
for i, item in enumerate(scholars):
    im = Image.open(item['file'])
    factor = min(3, 160 // im.width, 390 // im.height)
    enlarged = im.resize((im.width * factor, im.height * factor), Image.Resampling.NEAREST)
    sheet.paste(enlarged, (i * 180 + (180 - enlarged.width)//2, 35), enlarged)
    draw.text((i*180+8, 12), 'DET 10015/' + str(item['record']), fill='white')
sheet.save(OUT / 'det-scholar-source-review.png')
print(json.dumps({'bundles': len(index), 'assets': sum(x['containerCount'] for x in index), 'sources': len(scholars)}))
