"""Package explicitly selected authored sprites; never synthesize a gait."""
from hashlib import sha256
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parent
selection = json.loads((HERE / 'selection.json').read_text(encoding='utf-8'))
family = json.loads((HERE / 'source-family.json').read_text(encoding='utf-8'))
CELL = selection['cellSize']
GROUND = selection['baseline']
HEIGHT = selection['logicalHeight']
NAMES = ['front', 'front-left', 'left-profile', 'rear-left', 'back', 'rear-right', 'right-profile', 'front-right']

def digest(path):
    return sha256(path.read_bytes()).hexdigest()

def mask(image):
    return image.getchannel('A').point(lambda a: 255 if a >= 128 else 0)

results = {}
for action, chosen in selection['actions'].items():
    all_frames, rows = {}, []
    for number, item in sorted(chosen.items(), key=lambda pair: int(pair[0])):
        view = int(number)
        source = HERE / item['source']
        if not source.exists():
            continue
        original = Image.open(source).convert('RGBA')
        images, frames, provenance = [], [], []
        for phase in range(4):
            box = item['sourceBoxes'][phase] if 'sourceBoxes' in item else (round(phase * original.width / 4), 0, round((phase + 1) * original.width / 4), original.height)
            assert 0 <= box[0] < box[2] <= original.width and 0 <= box[1] < box[3] <= original.height, 'Source rectangle outside image'
            image = original.crop(box)
            entry = {'phase': phase, 'source': item['source'], 'sha256': digest(source), 'sourceBox': list(box), 'canvasScale': 1}
            override = item.get('frames', {}).get(str(phase))
            if override:
                replacement = HERE / override
                image = Image.open(replacement).convert('RGBA')
                entry = {'phase': phase, 'source': override, 'sha256': digest(replacement), 'sourceBox': [0, 0, image.width, image.height], 'canvasScale': original.height / image.height}
            images.append(image)
            provenance.append(entry)
        bounds = [mask(image).getbbox() for image in images]
        assert all(bounds), 'Empty authored pose: ' + source.name
        assert all(0 < box[0] and box[2] < image.width for image, box in zip(images, bounds)), 'Source cell touches cut boundary; inspect gutters before packing'
        common_top = min(box[1] * entry['canvasScale'] for box, entry in zip(bounds, provenance))
        common_bottom = max(box[3] * entry['canvasScale'] for box, entry in zip(bounds, provenance))
        scale = HEIGHT / (common_bottom - common_top)
        for phase, (image, box, entry) in enumerate(zip(images, bounds, provenance)):
            factor = entry['canvasScale']
            head = mask(image).crop((0, box[1], image.width, box[1] + round((box[3] - box[1]) * .20))).getbbox()
            head_x = (head[0] + head[2]) / 2
            small = image.resize((round(image.width * factor * scale), round(image.height * factor * scale)), Image.Resampling.NEAREST)
            small.putalpha(mask(small))
            initial_y = GROUND - round(common_bottom * scale)
            y = GROUND - mask(small).getbbox()[3] if action == 'walk' or phase in (0, 2) else initial_y
            x = CELL // 2 - round(head_x * factor * scale)
            frame = Image.new('RGBA', (CELL, CELL))
            frame.alpha_composite(small, (x, y))
            native_box = mask(frame).getbbox()
            assert native_box and 0 < native_box[0] < native_box[2] < CELL and 0 < native_box[1] < native_box[3] < CELL, 'Clipped review pose'
            entry.update({'rawCutoffBounds': list(box), 'commonViewScale': scale, 'headAnchorX': head_x, 'placement': [x, y], 'contactRegistrationY': y - initial_y, 'nativeBounds': list(native_box), 'pixelSha256': sha256(frame.tobytes()).hexdigest()})
            packed_path = HERE / action / 'frames' / ('view-' + str(view) + '-frame-' + str(phase) + '.png')
            packed_path.parent.mkdir(exist_ok=True)
            frame.save(packed_path)
            entry.update({'packedFrame': packed_path.relative_to(HERE).as_posix(), 'packedFileSha256': digest(packed_path)})
            frames.append(frame)
        assert len({entry['pixelSha256'] for entry in provenance}) == 4, 'Duplicate review frames'
        all_frames[view] = frames
        rows.append({'view': view, 'name': NAMES[view], 'status': item['status'], 'frames': provenance})
        atlas = Image.new('RGBA', (CELL * 4, CELL))
        sheet = Image.new('RGBA', (CELL * 4, CELL + 16), (77, 84, 83, 255))
        draw = ImageDraw.Draw(sheet)
        motion = []
        for phase, frame in enumerate(frames):
            atlas.alpha_composite(frame, (phase * CELL, 0))
            sheet.alpha_composite(frame, (phase * CELL, 16))
            draw.text((phase * CELL + 4, 2), str(phase), fill='white')
            canvas = Image.new('RGBA', (CELL, CELL), (77, 84, 83, 255))
            canvas.alpha_composite(frame)
            motion.append(canvas.convert('RGB').resize((CELL * 2, CELL * 2), Image.Resampling.NEAREST))
        atlas.save(HERE / action / ('view-' + str(view) + '-review-native.png'))
        sheet.resize((sheet.width * 2, sheet.height * 2), Image.Resampling.NEAREST).save(HERE / action / ('view-' + str(view) + '-review-2x.png'))
        motion[0].save(HERE / action / ('view-' + str(view) + '-review.gif'), save_all=True, append_images=motion[1:], duration=250 if action == 'walk' else 150, loop=0, disposal=2)
    if rows:
        sheet = Image.new('RGBA', (CELL * 4 + 110, (CELL + 16) * len(rows)), (77, 84, 83, 255))
        draw = ImageDraw.Draw(sheet)
        for row_index, row in enumerate(rows):
            draw.text((4, row_index * (CELL + 16) + 8), str(row['view']) + ' ' + row['name'], fill='white')
            for phase, frame in enumerate(all_frames[row['view']]):
                sheet.alpha_composite(frame, (110 + phase * CELL, row_index * (CELL + 16) + 16))
                draw.text((114 + phase * CELL, row_index * (CELL + 16)), str(phase), fill='white')
        sheet.save(HERE / action / 'selected-views-review-sheet.png')
    candidates = [row['view'] for row in rows if row['status'] == 'review-candidate']
    results[action] = {'generatedViews': sorted(all_frames), 'candidateViews': candidates, 'missingCandidateViews': sorted(set(range(8)) - set(candidates)), 'selectedFrames': len(rows) * 4, 'candidateFrames': len(candidates) * 4, 'remainingProductionViews': list(range(8)), 'views': rows}
result = {'status': 'source-family-review-work-in-progress', 'aliases': family['identities'], 'originals': family['originals'], 'logicalExtent': family['originals'][0]['dimensions'], 'cellSize': [CELL, CELL], 'baseline': GROUND, 'alphaCutoff': 128, 'acceptedProductionViews': 0, 'actions': results, 'transform': 'Common scale per view, head horizontal registration, rigid whole-frame contact alignment. Source PNGs unchanged; no mirroring, interpolation or invented phases. Four unique images do not prove gait or identity fidelity.'}
result['selectionFileSha256'] = digest(HERE / 'selection.json')
(HERE / 'manifest.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps({action: {'generated': data['generatedViews'], 'candidates': data['candidateViews']} for action, data in results.items()}))
