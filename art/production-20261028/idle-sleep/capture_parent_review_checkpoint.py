"""Bind explicit parent visual review of twelve inspected strips to current hashes."""
from pathlib import Path
import json,hashlib
out=Path(__file__).resolve().parent
data=json.loads((out/'native-idle-candidate-coverage.json').read_text())
reviewed=[(182,39),(182,47),(182,56),(183,5),(183,4),(183,0),(183,1),(182,46),(182,37),(182,38),(183,6),(183,13)]
rows=[]
for row in data['sets']:
    key=(row['archive'],row['record'])
    if key not in reviewed:continue
    strip=out/f'native-{key[0]}-{key[1]}-idle-native-review.png'
    finding='Hand and bearing assignment broadly traceable; reconstruction/rendering/prop detail limits retained.'
    if key==(182,37):finding+=' All-new quiet idle; active source separate.'
    if key==(182,38):finding+=' Seated source separate, derived128px standing correctly declared; orange object ambiguous.'
    if key==(183,6):finding+=' Front-left torso rotation weak.'
    if key==(183,13):finding+=' Back4 smaller head, narrower torso, longer cape: needs-proportion-repair; not a clean complete eight-view set.'
    rows.append({'archive':key[0],'record':key[1],'reviewer':'root','date':'2026-10-28','basis':'Explicit parent message: all12 source-paired strips personally seen','rawAtlasSha256':row['rawAtlasSha256'],'frameSha256':[f['sha256'] for f in row['frames']],'strip':str(strip),'stripSha256':hashlib.sha256(strip.read_bytes()).hexdigest(),'finding':finding,'runtimeChecked':False,'temporalChecked':False,'userApproved':False})
target=out/'parent-visual-review-checkpoint-33.json'
assert not target.exists(),'Immutable checkpoint already captured; do not overwrite after changes.'
target.write_text(json.dumps({'reviews':rows,'count':len(rows)},indent=2),encoding='utf-8')
print({'parentReviewCount':len(rows)})
