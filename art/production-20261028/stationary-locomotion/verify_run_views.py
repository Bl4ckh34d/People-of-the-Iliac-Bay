"""Bounded provenance/layout checks plus explicit unresolved motion diagnostics."""
import json
from hashlib import sha256
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
RAW = HERE / 'run-182-0'
manifest = json.loads((RAW / 'run-review-manifest.json').read_text(encoding='utf-8'))
rows = []
for view in manifest['views']:
    number = view['view']
    image = Image.open(RAW / ('view-' + str(number) + '-review-native.png')).convert('RGBA')
    assert image.size == (384, 96)
    hashes, bounds = [], []
    for phase in range(4):
        frame = image.crop((phase * 96, 0, (phase + 1) * 96, 96))
        hashes.append(sha256(frame.tobytes()).hexdigest())
        assert set(frame.getchannel('A').getdata()).issubset({0, 255})
        box = frame.getchannel('A').getbbox()
        assert box and 0 < box[0] < box[2] < 96 and 0 < box[1] < box[3] < 96
        bounds.append(list(box))
    assert len(set(hashes)) == 4
    for source in view['frames']:
        assert sha256((RAW / source['source']).read_bytes()).hexdigest() == source['sourceSha256']
    gif = Image.open(RAW / ('view-' + str(number) + '-review.gif'))
    assert gif.n_frames == 4 and gif.info['duration'] == 150
    bottoms = [box[3] for box in bounds]
    ground = max(bottoms[0], bottoms[2])
    contact_spread = abs(bottoms[0] - bottoms[2])
    gaps = [ground - bottoms[1], ground - bottoms[3]]
    rows.append({'view': number, 'uniqueFrames': 4, 'pixelHashes': hashes, 'bounds': bounds, 'bottomCoordinates': bottoms, 'contactBaselineSpread': contact_spread, 'contactBodyHeights': [bounds[i][3] - bounds[i][1] for i in (0, 2)], 'contactHeadTopSpread': abs(bounds[0][1] - bounds[2][1]), 'flightClearance': gaps, 'motionGeometryNeedsReview': contact_spread > 1 or min(gaps) < 1, 'sourceHashesMatch': True})
left = json.loads((RAW / 'run-left-review.json').read_text(encoding='utf-8'))
for frame in left['frames']:
    assert sha256((RAW / frame['source']).read_bytes()).hexdigest() == frame['sha256']
result = {'status': 'mechanical-layout-and-provenance-pass', 'inspectedFrameCount': len(rows) * 4, 'candidateFrameCount': manifest['candidateFrameCount'], 'candidateViews': manifest['candidateViews'], 'views': rows, 'unresolvedGeometryViews': [row['view'] for row in rows if row['motionGeometryNeedsReview']], 'contactHeadDriftOverTwoPixels': [row['view'] for row in rows if row['contactHeadTopSpread'] > 2], 'limitations': 'Distinct frames and file/layout checks do not prove gait ownership, palette, camera or identity fidelity. Contacts use disclosed rigid registration; flight clearance remains from authored poses. One or two pixels of clearance can be valid at76pixelbodyheight. Unselected failed directions are inspected for layout but do not count as candidate coverage. Production acceptance remains zero.'}
(RAW / 'run-verification.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'status': result['status'], 'frames': result['candidateFrameCount'], 'geometryReviewViews': result['unresolvedGeometryViews']}))
