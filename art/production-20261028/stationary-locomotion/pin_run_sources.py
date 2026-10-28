"""Pin image-generation reference files; never silently refresh an old pin."""
from hashlib import sha256
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
count = 0
for path in sorted((HERE / 'run-182-0').glob('*.json')):
    item = json.loads(path.read_text(encoding='utf-8'))
    if 'referenceInputs' not in item:
        continue
    pins = [{'path': name, 'sha256': sha256(Path(name).read_bytes()).hexdigest()} for name in item['referenceInputs']]
    if 'referenceInputPins' in item:
        assert item['referenceInputPins'] == pins, 'Reference file changed since pin: ' + path.name
    else:
        item['referenceInputPins'] = pins
        path.write_text(json.dumps(item, indent=2) + '\n', encoding='utf-8')
    count += 1
print(json.dumps({'verifiedGenerationReferenceRecords': count}))
