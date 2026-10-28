"""Reconcile current inventory against existing sleep mappings and this candidate output."""
from pathlib import Path
from collections import Counter
import json
OUT=Path(__file__).resolve().parent
SLEEP=OUT.parents[1]/'sleep'
inventory=json.loads((OUT.parent/'source-inventory.json').read_text())
existing={}
for file in SLEEP.glob('topdown-batch-*.json'):
    data=json.loads(file.read_text())
    for cell,item in enumerate(data['batch']['items']):
        kind='mobile' if item['kind']=='mobile' else 'stationary'
        record=None if kind=='mobile' else item['record']
        existing[('vanilla',kind,item['archive'],record)]={'manifest':str(file),'cell':cell}
        for alias in item.get('aliases',[]):
            bits=alias.split('-')
            if bits[0]=='flat':existing[('vanilla','stationary',int(bits[1]),int(bits[2]))]={'manifest':str(file),'cell':cell,'aliasOf':item['id']}
            elif bits[0]=='mobile':existing[('vanilla','mobile',int(bits[1]),None)]={'manifest':str(file),'cell':cell,'aliasOf':item['id']}
missing=json.loads((SLEEP/'missing-coverage.json').read_text())
for batch in missing['batches']:
    for item in batch['items']:
        existing[(item['provider'],'stationary',item['archive'],item['record'])]={'manifest':str(SLEEP/'missing-coverage.json'),'file':batch['file'],'cell':item['cell'],'sourcePixelHash':item['sourcePixelHash']}
new=json.loads((OUT/'sleep-candidate-coverage.json').read_text())
newidx={(r['provider'],'stationary',r['archive'],r['record']):r for r in new['entries']}
rows=[]
native_idle=json.loads((OUT/'native-idle-candidate-coverage.json').read_text())
native_idle_ids={alias for item in native_idle['sets'] for alias in item['aliases']}
native_idle_status={alias:item.get('candidateStatus','reviewed-with-rendering-limits') for item in native_idle['sets'] for alias in item['aliases']}
for row in inventory['identities']:
    kind='mobile' if row['kind'] in ['mobile','guard'] else row['kind']
    key=(row['provider'],kind,row['archive'],None if kind=='mobile' else row['record'])
    state='not-mapped'
    detail=None
    if key in existing:state='existing-mapping';detail=existing[key]
    if key in newidx:state=newidx[key]['candidateStatus'];detail={'manifest':str(OUT/'sleep-candidate-coverage.json'),'file':newidx[key]['file'],'issues':newidx[key]['issues']}
    idle='eight-views-not-certified-here'
    if row['provider']=='vanilla' and kind=='mobile' and row['archive'] in list(range(381,399))+list(range(451,457)):idle='existing-eight-views-technically-verified'
    if key==('daggerfall expanded textures','stationary',10015,0):idle='eight-view-candidate-with-identity-limits'
    if row['id'] in native_idle_ids:idle='eight-view-native-candidate-reviewed-with-rendering-limits'
    if native_idle_status.get(row['id'])=='needs-proportion-repair':idle='native-idle-needs-proportion-repair-not-complete'
    rows.append({'id':row['id'],'provider':row['provider'],'kind':kind,'archive':row['archive'],'record':row.get('record'),'applicability':row.get('applicability'),'sleepStatus':state,'sleepEvidence':detail,'idleStatus':idle})
report={'counts':{'identities':len(rows),'sleep':dict(Counter(r['sleepStatus'] for r in rows)),'idle':dict(Counter(r['idleStatus'] for r in rows))},'limits':['Existing mapping means source catalog evidence, not new user acceptance or full runtime verification.','Unmapped entries include excluded/nonstandard anatomy and resource families; inspect applicability before classifying as ordinary NPC gaps.','Stationary native frontal frames can be seated or performing work; they do not establish eight-direction idle standing coverage.'],'identities':rows}
(OUT/'scope-reconciliation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report['counts']))
