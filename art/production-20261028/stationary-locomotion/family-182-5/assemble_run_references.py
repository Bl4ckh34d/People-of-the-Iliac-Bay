"""Assemble existing same-bearing motion frames; no new artwork or source duplication."""
from pathlib import Path
from PIL import Image
import json
from hashlib import sha256

HERE = Path(__file__).resolve().parent
SOURCE = HERE.parent.parent / 'mobile-locomotion/eight-view-candidate-v1/frames'
for view in (0, 4, 6):
    paths = [SOURCE / ('view-' + str(view) + '-frame-' + str(phase) + '.png') for phase in range(4)]
    frames = [Image.open(path).convert('RGBA') for path in paths]
    width, height = frames[0].size
    assert all(frame.size == (width, height) for frame in frames)
    strip = Image.new('RGBA', (width * 4, height))
    for phase, frame in enumerate(frames):
        strip.alpha_composite(frame, (width * phase, 0))
    dest = HERE / 'run' / ('motion-385-view-' + str(view) + '-reference.png')
    strip.save(dest)
    pins = [{'path': str(path.resolve()), 'sha256': sha256(path.read_bytes()).hexdigest()} for path in paths]
    dest.with_suffix('.json').write_text(json.dumps({'role': 'Reviewed generated385 same-bearing camera and run motion only; not a native original', 'sources': pins}, indent=2) + '\n', encoding='utf-8')
print('Assembled three bounded motion-reference strips from existing frames')
