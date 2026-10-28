"""Register authored walk sheets for review; preserve every generated source PNG."""
from hashlib import sha256
import json
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
RAW = HERE / 'walk-182-0'
NAMES = ['front', 'front-left', 'left-profile', 'rear-left', 'back', 'rear-right', 'right-profile', 'front-right']
CELL = 96
CUTOFF = 128


def digest(path):
    return sha256(path.read_bytes()).hexdigest()


def cut_cell(path, index):
    with Image.open(path) as image:
        edges = [round(i * image.width / 4) for i in range(5)]
        box = (edges[index], 0, edges[index + 1], image.height)
        return image.convert('RGBA').crop(box), {'source': path.relative_to(HERE).as_posix(), 'sourceSha256': digest(path), 'sourceCell': index, 'sourceBox': list(box)}


def alpha_mask(image):
    return image.getchannel('A').point(lambda value: 255 if value >= CUTOFF else 0)


def register(images, metadata, pre_native=False):
    bounds = [alpha_mask(image).getbbox() for image in images]
    factors = [item.get('referenceCanvasHeight', image.height) / image.height for image, item in zip(images, metadata)]
    common_top = min(box[1] * factor for box, factor in zip(bounds, factors))
    common_bottom = max(box[3] * factor for box, factor in zip(bounds, factors))
    scale = 1 if pre_native else 76 / (common_bottom - common_top)
    frames = []
    for image, box, item, canvas_factor in zip(images, bounds, metadata, factors):
        # Head-region horizontal registration removes independent canvas shifts.
        # One common scale and one vertical anchor preserve intra-cycle bob.
        head_bottom = box[1] + max(1, round((box[3] - box[1]) * .14))
        head = alpha_mask(image).crop((0, box[1], image.width, head_bottom)).getbbox()
        head_center = (head[0] + head[2]) / 2
        size = (round(image.width * canvas_factor * scale), round(image.height * canvas_factor * scale))
        reduced = image.resize(size, Image.Resampling.NEAREST)
        reduced.putalpha(alpha_mask(reduced))
        xy = (48 - round(head_center * canvas_factor * scale), 88 - round(common_bottom * scale))
        frame = Image.new('RGBA', (CELL, CELL))
        frame.alpha_composite(reduced, xy)
        item.update({'rawCutoffBounds': list(box), 'rawHeadAnchorX': head_center, 'referenceCanvasScale': canvas_factor, 'uniformViewScale': scale, 'commonRawBottom': common_bottom, 'placement': list(xy), 'nativeCutoffBounds': list(alpha_mask(frame).getbbox())})
        frames.append(frame)
    return frames


all_frames = {}
rows = []
for view, name in enumerate(NAMES):
    if view == 2:
        path = HERE / 'cycle-review-atlas-native.png'
        sources = [cut_cell(path, index) for index in range(4)]
    elif view == 0:
        original = RAW / 'view-0-front-v1.png'
        repaired = RAW / 'view-0-front-v2-passing-repair.png'
        if not original.exists() or not repaired.exists():
            continue
        sources = [cut_cell(repaired if index == 1 else original, index) for index in range(4)]
    else:
        path = RAW / ('view-' + str(view) + '-' + name + '-v1.png')
        if not path.exists():
            continue
        sources = [cut_cell(path, index) for index in range(4)]
    for phase in range(4):
        repaired = RAW / ('view-' + str(view) + '-phase-' + str(phase) + '-repair-v1.png')
        if not repaired.exists():
            continue
        reference_height = sources[phase][0].height
        with Image.open(repaired) as image:
            sources[phase] = (image.convert('RGBA'), {
                'source': repaired.relative_to(HERE).as_posix(), 'sourceSha256': digest(repaired),
                'sourceCell': None, 'sourceBox': [0, 0, image.width, image.height],
                'referenceCanvasHeight': reference_height,
                'replacementReason': 'Individually generated opposite gait phase replaces collapsed sheet pose.',
            })
    images, metadata = [source[0] for source in sources], [source[1] for source in sources]
    frames = register(images, metadata, pre_native=view == 2)
    all_frames[view] = frames
    rows.append({'view': view, 'name': name, 'status': 'review-candidate', 'frameCount': 4, 'frames': metadata})
    strip = Image.new('RGBA', (CELL * 4, CELL))
    for index, frame in enumerate(frames):
        strip.alpha_composite(frame, (index * CELL, 0))
    strip.save(RAW / ('view-' + str(view) + '-review-native.png'))
    gif = []
    for frame in frames:
        canvas = Image.new('RGBA', (CELL, CELL), (77, 84, 83, 255))
        canvas.alpha_composite(frame)
        gif.append(canvas.convert('RGB').resize((384, 384), Image.Resampling.NEAREST))
    gif[0].save(RAW / ('view-' + str(view) + '-review.gif'), save_all=True, append_images=gif[1:], duration=250, loop=0, disposal=2)

atlas = Image.new('RGBA', (CELL * 4, CELL * 8))
sheet = Image.new('RGBA', (CELL * 4 + 130, CELL * 8), (77, 84, 83, 255))
draw = ImageDraw.Draw(sheet)
for view in range(8):
    draw.text((5, view * CELL + 32), str(view) + ' ' + NAMES[view], fill='white')
    if view not in all_frames:
        draw.text((135, view * CELL + 32), 'missing', fill='white')
        continue
    for index, frame in enumerate(all_frames[view]):
        atlas.alpha_composite(frame, (index * CELL, view * CELL))
        sheet.alpha_composite(frame, (130 + index * CELL, view * CELL))
atlas.save(RAW / 'walk-eight-view-review-atlas.png')
sheet.resize((sheet.width * 2, sheet.height * 2), Image.Resampling.NEAREST).save(RAW / 'walk-eight-view-review-sheet-2x.png')

motion = []
for phase in range(4):
    canvas = Image.new('RGBA', (CELL * 4, CELL * 2), (77, 84, 83, 255))
    for view, frames in all_frames.items():
        canvas.alpha_composite(frames[phase], ((view % 4) * CELL, (view // 4) * CELL))
    motion.append(canvas.convert('RGB').resize((768, 384), Image.Resampling.NEAREST))
motion[0].save(RAW / 'walk-eight-view-review.gif', save_all=True, append_images=motion[1:], duration=250, loop=0, disposal=2)

native_source = HERE.parent / 'sources/native/182_0-0.png'
result = {
    'identity': 'vanilla:stationary:182:0', 'action': 'walk',
    'status': 'directional-review-candidates-not-production-acceptance',
    'source': {'path': native_source.relative_to(HERE.parent).as_posix(), 'fileSha256': digest(native_source), 'dimensions': [47, 76]},
    'viewConvention': dict(enumerate(NAMES)), 'completeCandidateViews': sorted(all_frames), 'missingViews': [view for view in range(8) if view not in all_frames],
    'candidateFrameCount': len(all_frames) * 4, 'acceptedProductionViews': 0,
    'reviewFps': 4, 'alphaCutoff': CUTOFF, 'cellSize': [CELL, CELL],
    'registration': 'One common scale and vertical anchor per generated direction; per-frame head-region horizontal registration only. Source pixels unchanged; no limb warping, mirroring, interpolation or duplicate-frame synthesis.',
    'frontRepair': 'Only phase1 uses the generated repaired sheet. Other three front phases retain original source pixels; repair changed support-leg ownership.',
    'views': rows,
}
(RAW / 'walk-review-manifest.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'candidateViews': result['completeCandidateViews'], 'candidateFrameCount': result['candidateFrameCount'], 'missingViews': result['missingViews']}))
