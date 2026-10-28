"""Review four authored run keyframes without synthesizing motion."""
import json
from hashlib import sha256
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
RAW = HERE / 'run-182-0'
SCALE = 76 / 1482
frames = []
rows = []
for phase in range(4):
    path = RAW / ('run-view-2-phase-' + str(phase) + ('-v2.png' if phase == 0 else '-v1.png'))
    image = Image.open(path).convert('RGBA')
    mask = image.getchannel('A').point(lambda a: 255 if a >= 128 else 0)
    bounds = mask.getbbox()
    head = mask.crop((0, bounds[1], image.width, bounds[1] + round((bounds[3] - bounds[1]) * .14))).getbbox()
    hx = (head[0] + head[2]) / 2
    small = image.resize((round(image.width * SCALE), round(image.height * SCALE)), Image.Resampling.NEAREST)
    small.putalpha(small.getchannel('A').point(lambda a: 255 if a >= 128 else 0))
    xy = (48 - round(hx * SCALE), 88 - round(1549 * SCALE))
    frame = Image.new('RGBA', (96, 96))
    frame.alpha_composite(small, xy)
    frames.append(frame)
    rows.append({'phase': phase, 'source': path.name, 'sha256': sha256(path.read_bytes()).hexdigest(), 'size': list(image.size), 'rawBounds': list(bounds), 'headAnchorX': hx, 'placement': list(xy), 'nativeBounds': list(frame.getchannel('A').getbbox())})
atlas = Image.new('RGBA', (384, 96))
sheet = Image.new('RGBA', (384, 112), (77, 84, 83, 255))
draw = ImageDraw.Draw(sheet)
for phase, frame in enumerate(frames):
    atlas.alpha_composite(frame, (phase * 96, 0))
    sheet.alpha_composite(frame, (phase * 96, 16))
    draw.text((phase * 96 + 3, 2), ['L contact', 'R flight', 'R contact', 'L flight'][phase], fill='white')
    draw.line((phase * 96, 104, (phase + 1) * 96 - 1, 104), fill=(130, 140, 135, 255))
atlas.save(RAW / 'run-left-review-native.png')
sheet.resize((1536, 448), Image.Resampling.NEAREST).save(RAW / 'run-left-review-sheet-4x.png')
motion = []
for frame in frames:
    canvas = Image.new('RGBA', (96, 96), (77, 84, 83, 255))
    canvas.alpha_composite(frame)
    motion.append(canvas.convert('RGB').resize((384, 384), Image.Resampling.NEAREST))
motion[0].save(RAW / 'run-left-review.gif', save_all=True, append_images=motion[1:], duration=150, loop=0, disposal=2)
result = {'status': 'run-left-review-candidate-not-production', 'identity': 'vanilla:stationary:182:0', 'sourceDimensions': [47, 76], 'view': 2, 'scale': SCALE, 'commonRawBaseline': 1549, 'nativeBaseline': 88, 'alphaCutoff': 128, 'frames': rows, 'transform': 'Common nearest-neighbor scale and vertical anchor; horizontal head registration. No per-frame foot grounding, pose warping, mirroring or motion interpolation.'}
(RAW / 'run-left-review.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result))
