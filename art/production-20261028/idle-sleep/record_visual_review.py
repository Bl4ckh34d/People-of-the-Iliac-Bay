"""Record a human/agent visual review only after actually inspecting current outputs."""
from pathlib import Path
import argparse,json
p=argparse.ArgumentParser()
p.add_argument('archive',type=int)
p.add_argument('record',type=int)
p.add_argument('--basis',required=True)
args=p.parse_args()
out=Path(__file__).resolve().parent
data=json.loads((out/'native-idle-candidate-coverage.json').read_text())
row=next(x for x in data['sets'] if x['archive']==args.archive and x['record']==args.record)
path=out/'native-idle-visual-review-records.json'
reviews=json.loads(path.read_text())
reviews[f'{args.archive}-{args.record}']={'rawAtlasSha256':row['rawAtlasSha256'],'frameSha256':[f['sha256'] for f in row['frames']],'reviewer':'idle_sleep_sprites','date':'2026-10-28','basis':args.basis}
path.write_text(json.dumps(reviews,indent=2),encoding='utf-8')
print(f'Recorded inspected current output hashes: {args.archive}/{args.record}')
