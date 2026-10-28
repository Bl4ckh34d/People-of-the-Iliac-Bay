"""Assemble selected authored run frames; never invent missing directions."""
import json
from hashlib import sha256
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
RAW = HERE / 'run-182-0'
NAMES = ['front', 'front-left', 'left-profile', 'rear-left', 'back', 'rear-right', 'right-profile', 'front-right']
# Only visually selected corrections belong here. Newer is not automatically better.
REPAIRS = {
    (1, 0): 'run-view-1-phase-0-same-bearing-single-v1.png',
    (1, 1): 'run-view-1-phase-1-same-bearing-single-v1.png',
    (1, 2): 'run-view-1-phase-2-same-bearing-single-v1.png',
    (1, 3): 'run-view-1-phase-3-same-bearing-single-v1.png',
    (3, 0): 'run-view-3-phase-0-back-camera-v1.png',
    (3, 1): 'run-view-3-phase-1-camera-match-v4.png',
    (3, 2): 'run-view-3-phase-2-camera-match-v4.png',
    (3, 3): 'run-view-3-phase-3-back-camera-v1.png',
    (5, 2): 'run-view-5-phase-2-same-bearing-single-v1.png',
    (5, 1): 'run-view-5-phase-1-same-bearing-single-v1.png',
    (5, 3): 'run-view-5-phase-3-same-bearing-single-v1.png',
    (0, 0): 'run-view-0-phase-0-repair-v3.png',
    (0, 2): 'run-view-0-phase-2-repair-v3.png',
    (0, 3): 'run-view-0-phase-3-repair-v2.png',
    (4, 0): 'run-view-4-phase-0-repair-v2.png',
    (4, 1): 'run-view-4-phase-1-repair-v1.png',
    (4, 2): 'run-view-4-phase-2-repair-v3.png',
    (4, 3): 'run-view-4-phase-3-repair-v1.png',
    (6, 1): 'run-view-6-phase-1-repair-v1.png',
}
REVIEWABLE = set(range(8))
FLIGHT_REGISTRATION_Y = {(5, 3): -3}

def digest(path):
    return sha256(path.read_bytes()).hexdigest()

def mask(image):
    return image.getchannel('A').point(lambda a: 255 if a >= 128 else 0)

views = []
all_frames = {}
for view, name in enumerate(NAMES):
    path = RAW / ('run-left-review-native.png' if view == 2 else 'run-view-' + str(view) + '-' + name + '-v1.png')
    if view in (1, 3, 5, 7):
        path = RAW / ('run-view-' + str(view) + '-' + name + '-v2-same-bearing.png')
    if not path.exists():
        continue
    strip = Image.open(path).convert('RGBA')
    images, rows = [], []
    for phase in range(4):
        source_phase = [2, 1, 0, 3][phase] if view == 6 else phase
        box = (round(source_phase * strip.width / 4), 0, round((source_phase + 1) * strip.width / 4), strip.height)
        image = strip.crop(box)
        row = {'phase': phase, 'source': path.name, 'sourceSha256': digest(path), 'sourceBox': list(box), 'referenceCanvasScale': 1}
        if (view, phase) in REPAIRS:
            selected = RAW / REPAIRS[(view, phase)]
            image = Image.open(selected).convert('RGBA')
            row = {'phase': phase, 'source': selected.name, 'sourceSha256': digest(selected), 'sourceBox': [0, 0, image.width, image.height], 'referenceCanvasScale': strip.height / image.height}
        images.append(image)
        rows.append(row)
    bounds = [mask(image).getbbox() for image in images]
    if view == 2:
        frames = images
        scale, common_bottom = 1, 88
    else:
        common_top = min(b[1] * row['referenceCanvasScale'] for b, row in zip(bounds, rows))
        common_bottom = max(b[3] * row['referenceCanvasScale'] for b, row in zip(bounds, rows))
        scale = 76 / (common_bottom - common_top)
        frames = []
        for image, b, row in zip(images, bounds, rows):
            factor = row['referenceCanvasScale']
            head = mask(image).crop((0, b[1], image.width, b[1] + round((b[3] - b[1]) * .14))).getbbox()
            hx = (head[0] + head[2]) / 2
            small = image.resize((round(image.width * factor * scale), round(image.height * factor * scale)), Image.Resampling.NEAREST)
            small.putalpha(mask(small))
            xy = (48 - round(hx * factor * scale), 88 - round(common_bottom * scale))
            common_y = xy[1]
            if row['phase'] in (0, 2):
                # A rigid whole-frame translation registers an actual authored
                # contact foot. Never lower flight feet or deform a pose.
                xy = (xy[0], 88 - mask(small).getbbox()[3])
            flight_shift = FLIGHT_REGISTRATION_Y.get((view, row['phase']), 0)
            xy = (xy[0], xy[1] + flight_shift)
            frame = Image.new('RGBA', (96, 96))
            frame.alpha_composite(small, xy)
            frames.append(frame)
            row.update({'rawHeadAnchorX': hx, 'placement': list(xy), 'contactRegistrationShiftY': xy[1] - common_y - flight_shift, 'flightRegistrationShiftY': flight_shift})
    for frame, row in zip(frames, rows):
        row['nativeBounds'] = list(mask(frame).getbbox())
        row['nativePixelSha256'] = sha256(frame.tobytes()).hexdigest()
    all_frames[view] = frames
    views.append({'view': view, 'name': name, 'status': 'unapproved-review-candidate' if view in REVIEWABLE else 'unselected-gait-or-camera-failure', 'uniformViewScale': scale, 'commonRawBottom': common_bottom, 'frames': rows})
    output = Image.new('RGBA', (384, 96))
    motion = []
    for phase, frame in enumerate(frames):
        output.alpha_composite(frame, (phase * 96, 0))
        canvas = Image.new('RGBA', (96, 96), (77, 84, 83, 255))
        canvas.alpha_composite(frame)
        motion.append(canvas.convert('RGB').resize((384, 384), Image.Resampling.NEAREST))
    output.save(RAW / ('view-' + str(view) + '-review-native.png'))
    motion[0].save(RAW / ('view-' + str(view) + '-review.gif'), save_all=True, append_images=motion[1:], duration=150, loop=0, disposal=2)

atlas = Image.new('RGBA', (384, 768))
sheet = Image.new('RGBA', (514, 768), (77, 84, 83, 255))
draw = ImageDraw.Draw(sheet)
for view in range(8):
    draw.text((5, view * 96 + 32), str(view) + ' ' + NAMES[view], fill='white')
    if view not in REVIEWABLE:
        draw.text((5, view * 96 + 48), 'UNSELECTED', fill=(255, 170, 120, 255))
    if view not in all_frames:
        draw.text((140, view * 96 + 32), 'missing', fill='white')
        continue
    for phase, frame in enumerate(all_frames[view]):
        atlas.alpha_composite(frame, (phase * 96, view * 96))
        sheet.alpha_composite(frame, (130 + phase * 96, view * 96))
    draw.line((130, view * 96 + 88, 513, view * 96 + 88), fill=(120, 132, 128, 255))
atlas.save(RAW / 'run-eight-view-review-atlas.png')
sheet.resize((1028, 1536), Image.Resampling.NEAREST).save(RAW / 'run-eight-view-review-sheet-2x.png')
motion = []
for phase in range(4):
    canvas = Image.new('RGBA', (384, 192), (77, 84, 83, 255))
    for view, frames in all_frames.items():
        canvas.alpha_composite(frames[phase], ((view % 4) * 96, (view // 4) * 96))
    motion.append(canvas.convert('RGB').resize((768, 384), Image.Resampling.NEAREST))
motion[0].save(RAW / 'run-eight-view-review.gif', save_all=True, append_images=motion[1:], duration=150, loop=0, disposal=2)
source = HERE.parent / 'sources/native/182_0-0.png'
result = {'identity': 'vanilla:stationary:182:0', 'action': 'run', 'status': 'partial-directional-review-not-production', 'source': {'path': source.relative_to(HERE.parent).as_posix(), 'sha256': digest(source), 'dimensions': [47, 76]}, 'generatedViews': sorted(all_frames), 'candidateViews': sorted(REVIEWABLE.intersection(all_frames)), 'candidateFrameCount': len(REVIEWABLE.intersection(all_frames)) * 4, 'acceptedProductionViews': 0, 'missingViews': [i for i in range(8) if i not in REVIEWABLE or i not in all_frames], 'alphaCutoff': 128, 'frameDurationMs': 150, 'cellSize': [96, 96], 'views': views, 'transform': 'Common scale per view and horizontal head registration. Contact poses receive only rigid whole-frame translation to baseline88; authored flight poses retain their canvas placement except the disclosed rigid -3px view5 phase3 clearance registration. No limb deformation, mirroring, pose warping, interpolation or duplicate invention.'}
if len(result['candidateViews']) == 8:
    result['status'] = 'eight-directional-review-candidate-not-production'
result['remainingProductionViews'] = list(range(8))
result['coverageMeaning'] = 'Selected authored motion review candidates only. Production acceptance is zero; other stationary identities are not covered by this pilot.'
(RAW / 'run-review-manifest.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'views': result['candidateViews'], 'frames': result['candidateFrameCount'], 'missing': result['missingViews']}))
