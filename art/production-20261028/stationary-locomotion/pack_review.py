"""Render a bounded review of four authored keyframes; never synthesize poses.

Original generated PNGs remain unchanged. One common nearest-neighbor scale and
placement model the native76-pixel body and the existing0.5 billboard alpha cutoff.
The review files are not installed or declared production-approved.
"""
from hashlib import sha256
import json
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
SCALE = 76 / 1482
CELL = (96, 96)
SOURCES = [
    ('flat-182-0-walk-left-contactA-v1.png', 'near-contact', 'Near leg forward; near arm back.'),
    ('flat-182-0-walk-left-passingB-v1.png', 'near-support', 'Near leg supports; far bent leg passes forward behind it.'),
    ('flat-182-0-walk-left-contactB-v1.png', 'far-contact', 'Far leg forward; near arm forward, near leg behind.'),
    ('flat-182-0-walk-left-passing-keyframe-v2.png', 'far-support', 'Far leg supports; near bent leg passes forward in front.'),
]
frames = []
manifest = []
for index, (name, phase, description) in enumerate(SOURCES):
    path = HERE / name
    with Image.open(path) as original:
        sprite = original.convert('RGBA')
        size = (round(sprite.width * SCALE), round(sprite.height * SCALE))
        sprite = sprite.resize(size, Image.Resampling.NEAREST)
        sprite.putalpha(sprite.getchannel('A').point(lambda value: 255 if value >= 128 else 0))
        frame = Image.new('RGBA', CELL)
        # Keep the same canvas anchor for every frame. Do not recenter limbs,
        # normalize each pose's height, or lock every foot to the same location.
        xy = (48 - round(493.5 * SCALE), 88 - round(1549 * SCALE))
        frame.alpha_composite(sprite, xy)
        frames.append(frame)
        manifest.append({
            'index': index, 'source': name, 'sourceSha256': sha256(path.read_bytes()).hexdigest(),
            'phase': phase, 'description': description, 'sourceSize': list(original.size),
            'scaledCanvasSize': list(size), 'placement': list(xy),
            'nativeAlphaBounds': list(frame.getchannel('A').getbbox()),
        })

atlas = Image.new('RGBA', (CELL[0] * 4, CELL[1]))
for index, frame in enumerate(frames):
    atlas.alpha_composite(frame, (index * CELL[0], 0))
atlas.save(HERE / 'cycle-review-atlas-native.png')

background = (77, 84, 83, 255)
sheet = Image.new('RGBA', (CELL[0] * 4, CELL[1] + 14), background)
draw = ImageDraw.Draw(sheet)
for index, frame in enumerate(frames):
    sheet.alpha_composite(frame, (index * CELL[0], 14))
    draw.text((index * CELL[0] + 6, 2), str(index) + ' ' + SOURCES[index][1], fill='white')
    draw.line((index * CELL[0], 102, (index + 1) * CELL[0] - 1, 102), fill=(120, 132, 128, 255))
sheet.resize((sheet.width * 4, sheet.height * 4), Image.Resampling.NEAREST).save(HERE / 'cycle-review-sheet-4x.png')

gif_frames = []
for frame in frames:
    canvas = Image.new('RGBA', CELL, background)
    canvas.alpha_composite(frame)
    gif_frames.append(canvas.convert('RGB').resize((384, 384), Image.Resampling.NEAREST))
gif_frames[0].save(HERE / 'cycle-review-4fps.gif', save_all=True, append_images=gif_frames[1:], duration=250, loop=0, disposal=2)

result = {
    'status': 'one-bearing-four-phase-review-candidate-not-production',
    'identity': 'vanilla:stationary:182:0', 'action': 'walk', 'bearing': 2,
    'frameOrderReason': 'Contact near leg, support near leg with far passing, contact far leg, support far leg with near passing. Original generation labels are not temporal order.',
    'logicalOriginalExtent': [47, 76], 'reviewCell': list(CELL), 'uniformScale': SCALE,
    'commonRawCanvasAnchor': [493.5, 1549], 'nativePlacementAnchor': [48, 88],
    'alphaCutoff': 128, 'reviewFps': 4, 'frames': manifest,
    'sourcePixelsChanged': False,
    'reviewTransform': 'Derived review only: common nearest-neighbor scale, common placement, existing billboard alpha cutoff; no pose warping, limb edits, mirroring, interpolation or duplicate-frame invention.',
}
(HERE / 'cycle-review.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'frames': len(frames), 'nativeAlphaBounds': [row['nativeAlphaBounds'] for row in manifest], 'sourceSha256': [row['sourceSha256'] for row in manifest]}))
