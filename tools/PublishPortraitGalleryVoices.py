"""Assemble the two shared review voices without synthesizing per-portrait audio."""
import hashlib
import json
import shutil
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'art/portraits/mobile-mouths'
MOODS = {'neutral', 'happy', 'angry', 'sad', 'afraid', 'guarded'}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def assemble():
    shared = ART / 'shared-voices'
    female = read(ART / 'split-mask-prototype/audio/emotion-samples.json')
    female['voiceSet'] = 'female'
    for row in female['audioSets']:
        row['audio'] = '../split-mask-prototype/' + row['audio']
        row['timing'] = '../split-mask-prototype/' + row['timing']

    batch = read(ROOT / 'work/breeze-tts-20261022/portrait-emotion-samples-long-male/manifest.json')
    assert batch['status'] == 'completed'
    assert {row['id'] for row in batch['samples']} == MOODS
    assert sha(Path(batch['referenceWav'])) == batch['referenceSha256']
    male = {'schemaVersion': 1, 'voiceSet': 'male', 'sameText': batch['exactText'],
            'sameReferenceSha256': batch['referenceSha256'], 'audioSets': []}
    for row in batch['samples']:
        assert row['status'] == 'succeeded'
        request = row['requestPayload']
        assert request['input'] == batch['exactText']
        assert request['options']['seed'] == batch['seed']
        assert sha(Path(row['rawWav'])) == row['rawWavSha256']
        assert sha(Path(row['wav'])) == row['wavSha256']
        target = shared / 'male'
        target.mkdir(parents=True, exist_ok=True)
        for key, suffix in [('wav', '.wav'), ('visemes', '.visemes.json')]:
            shutil.copy2(Path(row[key]), target / (row['id'] + suffix))
        male['audioSets'].append({
            'id': row['id'], 'audio': f"male/{row['id']}.wav",
            'timing': f"male/{row['id']}.visemes.json", 'audioSha256': row['wavSha256'],
            'exactText': batch['exactText'], 'profileId': batch['profileId'],
            'referenceSha256': batch['referenceSha256'], 'deliveryText': row['deliveryText']})

    proof = []
    for manifest in (female, male):
        assert len(manifest['audioSets']) == 6
        assert {row['id'] for row in manifest['audioSets']} == MOODS
        for row in manifest['audioSets']:
            audio = shared / row['audio']
            timing = read(shared / row['timing'])
            assert sha(audio) == row['audioSha256'] == timing['provenance']['audioSha256']
            assert timing['text'] == manifest['sameText'] == row['exactText']
            with wave.open(str(audio), 'rb') as wav:
                assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (1, 2, 24000)
                assert wav.getnframes() == timing['audioFrames']
                seconds = wav.getnframes() / wav.getframerate()
            cursor = 0
            for cue in timing['cues']:
                assert cue['startSample'] == cursor and cue['endSample'] > cursor
                cursor = cue['endSample']
            assert cursor == timing['audioFrames']
            assert {cue['shape'] for cue in timing['cues']} == set('ABCDEFGHX')
            row['durationSeconds'] = seconds
            proof.append({'voiceSet': manifest['voiceSet'], 'mood': row['id'],
                          'audioSha256': row['audioSha256'], 'seconds': seconds})
        write(shared / (manifest['voiceSet'] + '.json'), manifest)

    catalog_path = ART / 'split-mask-prototype/portrait-catalog.json'
    catalog = read(catalog_path)
    catalog['voiceSets'] = {'female': '../shared-voices/female.json', 'male': '../shared-voices/male.json'}
    speaker_map = {row['id']: row for row in read(shared / 'speaker-map.json')['entries']}
    for row in catalog['entries']:
        assignment = speaker_map.get(row['id'])
        if assignment and assignment['voiceSet'] in ('female', 'male'):
            row['voiceSet'] = assignment['voiceSet']
            row['voiceSetSource'] = '../shared-voices/speaker-map.json'
    write(catalog_path, catalog)
    write(ROOT / 'work/portrait-shared-voices-verification.json',
          {'status': 'pass', 'scope': 'review-gallery-only; individual runtime voices unchanged', 'clips': proof})
    print(json.dumps({'status': 'pass', 'voiceSets': 2, 'clips': len(proof), 'allNineShapes': True}))


if __name__ == '__main__':
    assemble()
