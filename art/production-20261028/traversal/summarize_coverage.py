"""Summarize selected traversal candidates without mutating the parent inventory."""
from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parent
inventory_path=ROOT.parent/'source-inventory.json'
inventory=json.loads(inventory_path.read_text(encoding='utf-8-sig'))
selected={'jumping':'381-jump-eight-views-v1','climbing':'381-climb-eight-views-v1','swimming':'381-swim-eight-views-v2'}
actions=[]
for action,folder in selected.items():
    path=ROOT/folder/'manifest.json'
    m=json.loads(path.read_text(encoding='utf-8'))
    frames=m['frames']
    for r in frames:
        assert hashlib.sha256((ROOT/folder/r['file']).read_bytes()).hexdigest()==r['sha256']
    actions.append({'action':action,'manifest':f'{folder}/manifest.json','manifestSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'selectedPackedFrames':len(frames),'generatedBearings':len(frames)//4,'targetBearings':8,'targetFramesPerBearing':4,'technicalChecks':m['technicalChecks'],'visuallyReviewed':True,'visualResult':m['visualReview'],'integrated':False,'userApproved':False,'acceptedProductionFrames':0})
report={'schemaVersion':1,'date':'2026-10-28','identity':'vanilla:mobile:381:None','sourceInventory':'../source-inventory.json','sourceInventorySha256':hashlib.sha256(inventory_path.read_bytes()).hexdigest(),'sourceInventoryCounts':inventory['counts'],'status':'pilot-in-progress-no-complete-traversal-coverage','actions':actions,'selectedPackedCandidateFrames':sum(a['selectedPackedFrames'] for a in actions),'targetFramesFor381ThreeActions':96,'missingPackedFramesFor381AtFourPhases':96-sum(a['selectedPackedFrames'] for a in actions),'acceptedProductionFrames':0,'otherIdentitiesTraversalProduction':'not-started','worldScaleContract':'native-scale-contract.json','notes':['Packed means technically valid candidate, not visual or runtime acceptance.','Climb pilot needs contact/leg-depth repairs; swim corrected profile awaits parent review before other views.','No package, game code, build, installation or shared queue changed.','Initial grids and superseded compact swim remain rejected evidence, excluded from current selected frame count.']}
report['status']='381-three-action-eight-bearing-candidates-generated-inventory-uncovered'
report['notes'][1]='Swimv2 is preferred after lower front/rear camera regeneration. Climb uses the new reach/pull/tuck/push sequence; old contralateral diagnostics are not counted. Temporal, cross-bearing and wall-contact acceptance remain open.'
report['additionalAuditCandidates']=[{'manifest':'381-climb-rear-cycle-v1/manifest.json','packedFrames':4,'selectedProduction':False,'reason':'Opposite arm/knee audit; pull leg transition sparse; physical depth contact unvalidated.'}]
family_actions=[]
for action,folder in [('jumping','families/382/jump-eight-views-v1'),('climbing','families/382/climb-eight-views-v1'),('swimming','families/382/swim-eight-views-v1'),('jumping','families/383/jump-eight-views-v1'),('climbing','families/383/climb-eight-views-v1'),('swimming','families/383/swim-five-views-v1')]:
    path=ROOT/folder/'manifest.json';m=json.loads(path.read_text(encoding='utf-8'))
    for r in m['frames']:assert hashlib.sha256((ROOT/folder/r['file']).read_bytes()).hexdigest()==r['sha256']
    family_actions.append({'identity':m['identity'],'action':action,'manifest':f'{folder}/manifest.json','manifestSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'selectedPackedFrames':len(m['frames']),'authoredFrames':m.get('authoredFrames',len(m['frames'])),'mirrorDerivedFrames':m.get('mirrorDerivedFrames',0),'technicalChecks':m['technicalChecks'],'visuallyReviewed':m['visuallyReviewed'],'visualResult':m.get('visualReview',''),'integrated':False,'userApproved':False})
report['additionalFamilies']=family_actions
report['otherIdentitiesTraversalProduction']='382 has96 candidates;383 has84 candidates, with swim bearings4/5/7 open. Bearing4 is blocked; its retry is excluded. Remaining inventory uncovered.'
report['selectedPackedCandidateFramesAllFamilies']=report['selectedPackedCandidateFrames']+sum(a['selectedPackedFrames'] for a in family_actions)
report['remaining382FramesAtFourPhases']=96-sum(a['selectedPackedFrames'] for a in family_actions if a['identity']=='vanilla:mobile:382:None')
report['remaining383FramesAtFourPhases']=96-sum(a['selectedPackedFrames'] for a in family_actions if a['identity']=='vanilla:mobile:383:None')
(ROOT/'coverage.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'selectedPackedCandidateFrames':report['selectedPackedCandidateFrames'],'missingPackedFramesFor381AtFourPhases':report['missingPackedFramesFor381AtFourPhases'],'acceptedProductionFrames':0,'frameHashesVerified':True},indent=2))
