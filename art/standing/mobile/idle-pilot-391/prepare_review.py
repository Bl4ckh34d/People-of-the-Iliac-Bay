"""Extract the review pilot without modifying any source artwork."""
from pathlib import Path
from PIL import Image, ImageDraw
import json
import shutil

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT.parents[1] / 'source' / 'native-idle' / '391-0.png'
im = Image.open(ROOT / 'generated-sheet-v1.png').convert('RGBA')
names = ['front-native', 'front-left', 'left-profile', 'rear-left', 'back', 'rear-right', 'right-profile', 'front-right']
frames = []
details = []
for i, name in enumerate(names):
    cell = (round(i % 4 * im.width / 4), round(i // 4 * im.height / 2), round((i % 4 + 1) * im.width / 4), round((i // 4 + 1) * im.height / 2))
    crop = im.crop(cell)
    # Ignore effectively invisible model feathering only for crop measurement.
    bounds = crop.getchannel('A').point(lambda a: 255 if a >= 128 else 0).getbbox()
    cut = crop.crop(bounds)
    target_width = round(cut.width * 166 / cut.height)
    native = cut.resize((target_width, 166), Image.Resampling.NEAREST)
    if i == 0:
        native = Image.open(SOURCE).convert('RGBA')
        shutil.copyfile(SOURCE, ROOT / '391-idle-view-0-front-native.png')
    else:
        native.save(ROOT / f'391-idle-view-{i}-{name}.png')
    frames.append(native)
    details.append({'view': i, 'bearing': name, 'native_size': list(native.size), 'generated_cell': cell, 'generated_alpha_bbox_in_cell': bounds, 'resized_width': target_width, 'source': str(SOURCE) if i == 0 else 'generated-sheet-v1.png'})
atlas = Image.new('RGBA', (80*4, 166*2))
for i, frame in enumerate(frames):
    atlas.paste(frame, (i%4*80+(80-frame.width)//2, i//4*166))
atlas.save(ROOT / '391-idle-native-atlas-v1.png')
preview = Image.new('RGB', (4*336, 2*720), '#303238')
draw = ImageDraw.Draw(preview)
for i, (frame, name) in enumerate(zip(frames, names)):
    x, y = (i%4)*336, (i//4)*720
    draw.text((x+14, y+8), f'{i}: {name}', fill='white')
    enlarged = frame.resize((frame.width*4,664), Image.Resampling.NEAREST)
    preview.paste(enlarged, (x+(336-enlarged.width)//2,y+35), enlarged)
preview.save(ROOT / '391-idle-review-4x-v1.png')
meta = {'status': 'Unaccepted review pilot; no runtime integration', 'generator': 'built-in imagegen', 'native_source': str(SOURCE), 'native_source_size': [71,166], 'generated_sheet_size': list(im.size), 'atlas_cell_size': [80,166], 'bearing_convention': '0 front; 1 front-left; 2 left profile; 3 rear-left; 4 back; 5 rear-right; 6 right profile; 7 front-right. Left/right refer to nose projection in the image, not anatomical side.', 'processing': 'Grid crop; alpha>=128 used only to find bounds; proportional nearest-neighbor reduction to 166px height with natural per-view width; atlas pads to 80x166 cells without scaling; original generated alpha preserved; view0 copied byte-for-byte from native source. No mirroring, palette substitution or hand-painted repairs.', 'frames': details}
(ROOT / 'manifest-v1.json').write_text(json.dumps(meta, indent=2), encoding='utf-8')
print(json.dumps(meta, indent=2))
