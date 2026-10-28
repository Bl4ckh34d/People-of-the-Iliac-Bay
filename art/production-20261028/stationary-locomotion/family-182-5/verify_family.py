"""Check provenance and review geometry; never equate layout with animation approval."""
import json
import sys
from hashlib import sha256
from pathlib import Path
from PIL import Image

HERE = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parent
manifest = json.loads((HERE / 'manifest.json').read_text(encoding='utf-8'))
input_evidence = {'manifestSha256': sha256((HERE / 'manifest.json').read_bytes()).hexdigest(), 'selectionFileSha256': sha256((HERE / 'selection.json').read_bytes()).hexdigest()}
(HERE / 'verification.json').write_text(json.dumps({'mechanicalChecks': 'INCOMPLETE', 'reason': 'Verification has started but has not reached successful completion.', **input_evidence}, indent=2) + '\n', encoding='utf-8')
assert manifest.get('selectionFileSha256') == input_evidence['selectionFileSha256'], 'Packed manifest is stale relative to selection.json; pack current selection before verifying'
CELL = manifest['cellSize'][0]
GROUND = manifest['baseline']
for original in manifest['originals']:
    source_path = Path(original['path'])
    assert sha256(source_path.read_bytes()).hexdigest() == original['fileSha256']
    source_image = Image.open(source_path).convert('RGBA')
    assert list(source_image.size) == original['dimensions']
    assert sha256(source_image.tobytes()).hexdigest() == original['pixelSha256']
checks = []
for action, data in manifest['actions'].items():
    for view in data['views']:
        atlas_path = HERE / action / ('view-' + str(view['view']) + '-review-native.png')
        atlas = Image.open(atlas_path).convert('RGBA')
        assert atlas.size == (CELL * 4, CELL)
        hashes = []
        heights = []
        gaps = []
        for phase, record in enumerate(view['frames']):
            source = HERE / record['source']
            assert sha256(source.read_bytes()).hexdigest() == record['sha256']
            frame = atlas.crop((phase * CELL, 0, (phase + 1) * CELL, CELL))
            pixel_hash = sha256(frame.tobytes()).hexdigest()
            assert pixel_hash == record['pixelSha256']
            packed_path = HERE / record['packedFrame']
            assert sha256(packed_path.read_bytes()).hexdigest() == record['packedFileSha256']
            packed = Image.open(packed_path).convert('RGBA')
            assert packed.size == frame.size and packed.tobytes() == frame.tobytes()
            assert set(frame.getchannel('A').getdata()) <= {0, 255}
            bounds = frame.getbbox()
            assert list(bounds) == record['nativeBounds']
            assert 0 < bounds[0] and bounds[2] < CELL and 0 < bounds[1] and bounds[3] < CELL
            hashes.append(pixel_hash)
            heights.append(bounds[3] - bounds[1])
            gaps.append(GROUND - bounds[3])
        assert len(set(hashes)) == 4
        gif = Image.open(HERE / action / ('view-' + str(view['view']) + '-review.gif'))
        assert gif.n_frames == 4
        checks.append({'action': action, 'view': view['view'], 'status': view['status'], 'frames': 4, 'fullSpriteHeights': heights, 'baselineGaps': gaps, 'atlasSha256': sha256(atlas_path.read_bytes()).hexdigest()})

generation_count = 0
for action in ('walk', 'run'):
    for path in (HERE / action).glob('*.json'):
        record = json.loads(path.read_text(encoding='utf-8'))
        if 'derivedFrom' in record:
            source_path = path.parent / record['derivedFrom']
            assert sha256(source_path.read_bytes()).hexdigest() == record['sourceSha256']
            crop = Image.open(source_path).crop(record['sourceBox']).convert('RGBA')
            selected = Image.open(path.with_suffix('.png')).convert('RGBA')
            assert crop.size == selected.size and crop.tobytes() == selected.tobytes()
            assert sha256(path.with_suffix('.png').read_bytes()).hexdigest() == record['sha256']
        if 'referenceInputs' not in record:
            continue
        pins = []
        for source in record['referenceInputs']:
            source_path = Path(source)
            pins.append({'path': source, 'sha256': sha256(source_path.read_bytes()).hexdigest()})
        if 'referencePins' in record:
            assert record['referencePins'] == pins, 'Changed generation reference: ' + path.name
        else:
            record['referencePins'] = pins
            path.write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
        generation_count += 1
result = {'mechanicalChecks': 'PASS', 'checkedFrames': sum(item['frames'] for item in checks), 'generationRecordsPinned': generation_count, 'acceptedProductionFrames': 0, 'limitation': 'Hashes, geometry, alpha and distinctness only. Visual gait, camera and original fidelity remain separate review decisions.', 'views': checks}
result.update(input_evidence)
(HERE / 'verification.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result))
