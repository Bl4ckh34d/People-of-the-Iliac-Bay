"""Mechanical provenance/layout checks; visual gait acceptance stays separate."""
from hashlib import sha256
import json
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
RAW = HERE / 'walk-182-0'
manifest = json.loads((RAW / 'walk-review-manifest.json').read_text(encoding='utf-8'))
rows = []
for view in manifest['views']:
    number = view['view']
    with Image.open(RAW / ('view-' + str(number) + '-review-native.png')) as opened:
        image = opened.convert('RGBA')
        assert image.size == (384, 96)
        frame_hashes = []
        for index in range(4):
            frame = image.crop((index * 96, 0, (index + 1) * 96, 96))
            frame_hashes.append(sha256(frame.tobytes()).hexdigest())
            assert set(frame.getchannel('A').getdata()).issubset({0, 255})
            box = frame.getchannel('A').getbbox()
            assert box and 0 < box[0] < box[2] < 96 and 0 < box[1] < box[3] < 96
    with Image.open(RAW / ('view-' + str(number) + '-review.gif')) as gif:
        assert gif.n_frames == 4 and gif.info['duration'] == 250
    assert len(set(frame_hashes)) == 4
    for frame in view['frames']:
        source = HERE / frame['source']
        assert sha256(source.read_bytes()).hexdigest() == frame['sourceSha256']
    rows.append({
        'view': number, 'uniqueNativeFrames': len(set(frame_hashes)), 'nativeFramePixelHashes': frame_hashes,
        'nativeBodyHeights': [frame['nativeCutoffBounds'][3] - frame['nativeCutoffBounds'][1] for frame in view['frames']],
        'nativeBottomCoordinates': [frame['nativeCutoffBounds'][3] for frame in view['frames']],
        'sourceHashesMatch': True, 'gifFrames': 4, 'gifFrameDurationMs': 250,
    })
result = {
    'status': 'mechanical-checks-pass', 'candidateViews': [row['view'] for row in rows],
    'candidateFrames': len(rows) * 4, 'views': rows,
    'limitations': 'Four distinct pixel states and correct layout are not proof of correct limb ownership, camera angle, source fidelity or production acceptance. Visual review remains required.',
}
(RAW / 'walk-verification.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result, indent=2))
