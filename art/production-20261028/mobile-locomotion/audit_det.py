"""Read-only DET mobile Orc texture coverage; folder labels are not identity IDs."""
from pathlib import Path
from PIL import Image, ImageDraw
import hashlib
import json
import UnityPy

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
BUNDLE = REPO.parent / 'Player/DaggerfallUnity_Data/StreamingAssets/Mods/daggerfall expanded textures.dfmod'
env = UnityPy.load(str(BUNDLE))
items = []
texts = []
thumbs = []
for path, pointer in env.container.items():
    if 'billboards mobile/10050-10055 orc/' in path and pointer.type.name == 'Texture2D':
        data = pointer.read()
        image = data.image.convert('RGBA')
        stem = Path(path).stem
        archive, tail = stem.split('_')
        record, frame = tail.split('-')
        items.append({'container_path': path, 'archive_in_filename': int(archive), 'record': int(record),
                      'frame': int(frame), 'size': list(image.size), 'pixel_sha256': hashlib.sha256(image.tobytes()).hexdigest()})
        if int(frame) == 0:
            thumbs.append((int(archive), int(record), image))
    elif pointer.type.name == 'TextAsset':
        data = pointer.read()
        value = data.m_Script
        text = value.decode('utf-8', errors='replace') if isinstance(value, bytes) else value
        matching = [line for line in text.splitlines() if any(term in line for term in ('10050', '10051', '10052', '10053', '10054', '10055', '1035', '1053'))]
        if matching:
            texts.append({'path': path, 'matching_lines': matching[:50]})
families = []
for archive in sorted(set(i['archive_in_filename'] for i in items)):
    selected = [i for i in items if i['archive_in_filename'] == archive]
    families.append({'filename_archive': archive, 'records': {str(r): sorted(i['frame'] for i in selected if i['record'] == r) for r in range(6)},
                     'walking_unique_records': 5, 'walking_frames_per_record': 4,
                     'eight_bearing_contract': 'If mapped to MobilePersonBillboard, native records 3,2,1 mirror for bearings 5,6,7.',
                     'running_frames': 0})
report = {'bundle': str(BUNDLE), 'sha256': hashlib.sha256(BUNDLE.read_bytes()).hexdigest(),
          'folder_label': '10050-10055 orc', 'texture_count': len(items), 'filename_families': families,
          'assets': sorted(items, key=lambda i: (i['archive_in_filename'], i['record'], i['frame'])), 'mapping_text_evidence': texts,
          'finding': '42 textures are exactly two filename families 1035 and 1053, each records 0-4 frames 0-3 plus record 5 frame 0. The folder label does not prove six distinct runtime families.',
          'runtime_mapping_verified': False, 'pigeon_excluded': True}
(ROOT / 'det-mobile-orc-coverage.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
preview = Image.new('RGB', (960, 500), '#24282e')
draw = ImageDraw.Draw(preview)
for index, (archive, record, image) in enumerate(sorted(thumbs, key=lambda x: (x[0], x[1]))):
    x, y = (index % 6) * 160, (index // 6) * 250
    draw.text((x + 4, y + 5), f'{archive} record {record}', fill='white')
    image.thumbnail((150, 220), Image.Resampling.NEAREST)
    preview.paste(image, (x + (160-image.width)//2, y + 28), image)
preview.save(ROOT / 'det-orc-source-preview.png')
print(json.dumps({'texture_count': len(items), 'families': families, 'mapping_text_evidence': texts}, indent=2))
