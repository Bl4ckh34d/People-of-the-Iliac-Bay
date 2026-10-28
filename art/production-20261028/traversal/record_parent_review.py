"""Persist parent visual-review evidence without claiming user acceptance."""
from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parent
items=[('families/382/jump-eight-views-v1',['review-bearings-0-3.png','review-bearings-4-7.png'],'Parent inspected all32: four clear phases, front0 noticeably larger than some bearings; proportional limit confirmed.'),('families/382/climb-eight-views-v1',['review-bearings-0-3.png','review-bearings-4-7.png'],'Parent inspected all32: anatomical right-hand contract more consistent than381; side-body lean and far-limb occlusion remain.'),('families/382/swim-profile-v1',['native-comparison.png'],'Parent inspected4phase pilot: body length/phases readable, shaded muscles more detailed than original.')]
items.append(('families/382/swim-eight-views-v1',['review-bearings-0-3.png','review-bearings-4-7.png'],'Parent inspected all32 authored phases: long profiles and foreshortened front/rear clear, quarter-camera/proportion differences visible. Replaces profile coverage count; no waterline or temporal acceptance.'))
rows=[]
for folder,files,result in items:
    paths=['manifest.json']+files
    rows.append({'folder':folder,'reviewer':'parent agent /root','reviewKind':'static visual review','result':result,'files':[{'file':name,'sha256':hashlib.sha256((ROOT/folder/name).read_bytes()).hexdigest()} for name in paths]})
report={'date':'2026-10-28','reviews':rows,'temporalAccepted':False,'physicalWallOrWaterAccepted':False,'integrated':False,'userApproved':False}
(ROOT/'parent-review-evidence.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print('Parent-reviewed manifests and contact-image hashes recorded.')
