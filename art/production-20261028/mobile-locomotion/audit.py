"""Read native texture headers and audit generated pilot files. No art repair."""
from pathlib import Path
from PIL import Image, ImageDraw
import hashlib
import json
import struct

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
ARENA = REPO.parent / 'GameFiles' / 'arena2'
NAMES = ['front', 'front-left', 'left-profile', 'rear-left', 'back', 'rear-right', 'right-profile', 'front-right']
RECORDS = [0, 1, 2, 3, 4, 3, 2, 1]
ARCHIVES = list(range(381, 399)) + list(range(451, 457))

def native(archive):
    path = ARENA / f'TEXTURE.{archive}'
    data = path.read_bytes()
    count = struct.unpack_from('<h', data, 0)[0]
    records = []
    for record in range(count):
        offset = struct.unpack_from('<i', data, 26 + record * 20 + 2)[0]
        width, height = struct.unpack_from('<hh', data, offset + 4)
        frames = struct.unpack_from('<H', data, offset + 20)[0]
        records.append({'record': record, 'width': width, 'height': height, 'frames': frames})
    return {'archive': archive, 'source': str(path), 'sha256': hashlib.sha256(data).hexdigest(),
            'walking': [{'bearing': index, 'name': NAMES[index], 'record': record,
                         'native_mirror': index > 4, **records[record]} for index, record in enumerate(RECORDS)],
            'running': {'production_ready': False, 'missing_bearings': list(range(8)),
                        'existing_rejected_pilot': archive == 385}}

pilots = []
preview = Image.new('RGB', (768, 550), '#20242a')
draw = ImageDraw.Draw(preview)
for version in (1, 2):
    path = ROOT / f'385-run-profile-v{version}.png'
    with Image.open(path) as image:
        alpha = image.getchannel('A')
        histogram = alpha.histogram()
        record = {'file': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                  'size': list(image.size), 'mode': image.mode, 'alpha_range': list(alpha.getextrema()),
                  'alpha_zero_pixels': histogram[0], 'alpha_255_pixels': histogram[255],
                  'alpha_partial_pixels': sum(histogram[1:255]),
                  'generated': True, 'technical_checked': True, 'technical_passed': True,
                  'visual_reviewed': True, 'visual_passed': False, 'integrated': False,
                  'user_approved': False, 'intended_frames': 8, 'bearing': 2,
                  'rejection': ['The camera-near arm leads forward in every frame; no convincing opposite half-cycle.',
                                'Native palette and pixel scale are not preserved exactly.'],
                  'alpha_note': 'No fully opaque pixels. The local composited preview does not show the large haze suggested by raw RGB tool preview; alpha near 252 is above native cutout 0.5.'}
        pilots.append(record)
        # Bounded inspection thumbnails only; no transformation into production art.
        thumb = image.resize((384, 256), Image.Resampling.NEAREST)
        preview.paste(thumb, ((version - 1) * 384, 32), thumb)
        draw.text(((version - 1) * 384 + 8, 8), f'385 LEFT RUN v{version}: REJECTED', fill='white')

original = Image.open(REPO / 'art/run/mobile/385/native-view-2-four-frames.png').convert('RGBA')
thumb = original.resize((768, 256), Image.Resampling.NEAREST)
preview.paste(thumb, (0, 294), thumb)
draw.text((8, 278), 'NATIVE WALK reference; eight-frame new runs above fail arm alternation', fill='white')
preview.save(ROOT / 'comparison-preview.png')
keyfile = ROOT / '385-run-opposite-keyframe-v1.png'
key = Image.open(keyfile).convert('RGBA')
alpha = key.getchannel('A')
mask = alpha.point(lambda value: 255 if value >= 128 else 0)
box = mask.getbbox()
keyinfo = {'file': keyfile.name, 'sha256': hashlib.sha256(keyfile.read_bytes()).hexdigest(),
           'size': list(key.size), 'alpha_range': list(alpha.getextrema()), 'cutout_bounds': list(box),
           'generated': True, 'technical_checked': True, 'technical_passed': True,
           'visual_reviewed': True, 'visual_passed': True, 'integrated': False, 'user_approved': False,
           'acceptance_scope': 'A single opposite-arm left-profile run keyframe, not an accepted full cycle or exact palette match.',
           'visual_observation': 'The near upper arm visibly extends behind the torso to image-right; the near elbow and wrist remain behind the body. The farther fist is in front to image-left. Bent raised front knee and trailing leg differ from source walk.',
           'limitations': ['Native palette/proportions still need comparison and user review.', 'Seven more chronological phases and seven other bearings remain absent from this new attempt.', 'No runtime atlas or integration created.'],
           'source': 'art/run/mobile/385/native-view-2-four-frames.png', 'native_reference': 'art/run/pilot-385/source/385-2-0.png'}
comparison = Image.new('RGB', (768, 380), '#24282e')
cdraw = ImageDraw.Draw(comparison)
examples = [('Native walk record2/frame0', Image.open(REPO / keyinfo['native_reference']).convert('RGBA')),
            ('Run v2: arm forward', Image.open(ROOT / '385-run-profile-v2.png').convert('RGBA').crop((0,0,384,512))),
            ('NEW: near arm behind', key)]
for index, (label, sample) in enumerate(examples):
    bbox = sample.getchannel('A').point(lambda value:255 if value>=128 else 0).getbbox()
    sample = sample.crop(bbox)
    scale = 220 / sample.height
    sample = sample.resize((round(sample.width * scale),220), Image.Resampling.NEAREST)
    x = index*256 + (256-sample.width)//2
    comparison.paste(sample,(x,50),sample)
    cdraw.text((index*256+6,12),label,fill='white')
cdraw.text((10,296),'Inspection only: equal bounding height; not runtime scale/pivot packing.',fill='white')
cdraw.text((10,320),'Single keyframe candidate. No full-cycle or user acceptance claimed.',fill='white')
comparison.save(ROOT / 'opposite-keyframe-comparison.png')
inventory = {'date': '2026-10-28', 'scope': '24 ordinary mobile civilian archives; native guard 399 separately recorded',
             'parser_reference': 'daggerfall-unity/Assets/Scripts/API/TextureFile.cs:483-533',
             'bearing_reference': 'daggerfall-unity/Assets/Scripts/Internal/MobilePersonBillboard.cs:145-154',
             'walking_fps': 4, 'native': [native(a) for a in ARCHIVES], 'guard_native': native(399),
             'generated_pilots': pilots, 'generated_opposite_keyframe': keyinfo, 'new_production_ready_run_frames': 0,
             'native_walk_asset_gap': 0, 'missing_civilian_run_bearing_sequences': 192,
             'mod_override_scope': 'See det-mobile-orc-coverage.json: two DET filename families have native-like walking records. The folder range 10050-10055 does not establish six runtime families.',
             'tool_boundary': 'SpriteCook tools unavailable. Built-in imagegen produced both local pilot images.',
             'decision': 'Whole-strip generation failed limb alternation twice. Single source-conditioned opposite-arm keyframe produces useful visible progress; complete chronological cycles remain missing.'}
(ROOT / 'coverage.json').write_text(json.dumps(inventory, indent=2) + '\n', encoding='utf-8')
assert len(inventory['native']) == 24
assert all(len(a['walking']) == 8 for a in inventory['native'])
assert all(v['frames'] == 4 for a in inventory['native'] for v in a['walking'])
print(json.dumps({'civilian_archives': 24, 'native_walk_bearings': 192, 'native_frames_per_bearing': 4,
                  'guard_frames': [v['frames'] for v in inventory['guard_native']['walking']],
                  'generated_pilots': 2, 'production_ready_new_run_frames': 0,
                  'pilot_alpha': [{k: p[k] for k in ('file', 'alpha_zero_pixels', 'alpha_255_pixels', 'alpha_partial_pixels')} for p in pilots]}, indent=2))
