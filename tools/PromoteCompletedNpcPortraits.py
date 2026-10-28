"""Promote complete, source-checked portrait art into the existing runtime art root."""
import importlib.util
import json
from pathlib import Path
import hashlib
import os

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'art/production-20261028/portraits'
TARGET=ROOT/'art/portraits/mobile-mouths'
CATALOG=TARGET/'split-mask-prototype/portrait-catalog.json'
spec=importlib.util.spec_from_file_location('candidate_gate',SOURCE/'candidate_gate.py')
gate=importlib.util.module_from_spec(spec);spec.loader.exec_module(gate)


def main():
    catalog=json.loads(CATALOG.read_text(encoding='utf-8-sig'));receipts=[]
    candidates=[SOURCE/f'npc{n:03}-complete-candidate/manifest.json' for n in [12,13,14,46,47,195,196,197,220,236,425]]
    candidates.append(SOURCE/'exact-feature-reuse/common-452/manifest.json')
    pending=[]
    for path in candidates:
        checked=gate.gate(path)
        if not checked['structuralPass']:raise ValueError(checked['issues'])
        m=json.loads(path.read_text());record=m['portraitRecord']
        target=TARGET/f'completed-20261028/common-{record:03}'
        entry=next(e for e in catalog['entries'] if e['bank']==m['bank'] and e['record']==record)
        destination_manifest=os.path.relpath(target/'manifest.json',CATALOG.parent).replace('\\','/')
        if entry.get('animationManifest') not in (None,destination_manifest):
            raise ValueError(f'Existing portrait selection would be replaced: {record}')
        original=(path.parent/m['original']).resolve()
        catalog_original=(CATALOG.parent/entry['original']).resolve()
        if original.read_bytes()!=catalog_original.read_bytes():raise ValueError(f'Original identity mismatch: {record}')
        copies={}
        def copy_ref(relative):
            p=(path.parent/relative).resolve();data=p.read_bytes()
            name=hashlib.sha256(str(p).encode()).hexdigest()[:16]+'-'+p.name
            copies[target/name]=data
            return name
        def rewrite(obj):
            if isinstance(obj,dict):
                for k,v in list(obj.items()):
                    if k=='file' and isinstance(v,str):obj[k]=copy_ref(v)
                    elif k in ('original','originalUpper','originalLower') and isinstance(v,str):obj[k]=copy_ref(v)
                    else:rewrite(v)
            elif isinstance(obj,list):
                for v in obj:rewrite(v)
        # Only runtime fields are rewritten; historical evidence stays in the source manifest.
        runtime={k:m[k] for k in ['schemaVersion','portraitRecord','bank','width','height','splitY','original','eyeSets','mouthSets','thinkingEyeSetsByEmotion']}
        for k in ['originalUpper','originalLower','eyeEdgeBlend']:
            if k in m:runtime[k]=m[k]
        rewrite(runtime)
        runtime['status']='complete-art-runtime-selected'
        runtime['sourceProductionManifest']=os.path.relpath(path,target).replace('\\','/')
        runtime['sourceProductionManifestSha256']=checked['manifestSha256']
        copies[target/'manifest.json']=(json.dumps(runtime,indent=2)+'\n').encode()
        pending.extend(copies.items())
        entry['animationManifest']=destination_manifest;entry['status']='complete-art-runtime-selected'
        receipts.append(dict(record=record,parts=len(checked['assets']),sourceManifest=str(path.relative_to(ROOT)).replace('\\','/'),sourceSha256=checked['manifestSha256'],runtimeManifest=destination_manifest))
    for p,data in pending:
        p.parent.mkdir(parents=True,exist_ok=True)
        if not p.exists() or p.read_bytes()!=data:p.write_bytes(data)
    for r in receipts:
        result=gate.gate(CATALOG.parent/r['runtimeManifest'])
        if not result['structuralPass']:raise ValueError(result['issues'])
    CATALOG.write_text(json.dumps(catalog,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    receipt=dict(status='selected-for-existing-runtime-export',portraits=receipts,voiceAssignmentChanged=False,temporalAcceptance=False)
    (SOURCE/'runtime-integration.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print(f'Promoted {len(receipts)} complete portraits / {sum(r["parts"] for r in receipts)} parts; preserved voice assignments.')


if __name__=='__main__':main()
