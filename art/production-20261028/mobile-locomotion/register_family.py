"""Save bounded image-tool outputs and exact provenance from an explicit plan."""
from pathlib import Path
import hashlib
import json
import shutil
import sys

ROOT=Path(__file__).resolve().parent
FAMILY=ROOT/'families'/sys.argv[1]
plan=json.loads((FAMILY/'production-inputs.json').read_text())
assert plan['archive']==int(sys.argv[1])
raw_root=Path(plan['raw_output_directory'])
rows=[]
selected=set(plan['selected_strips'])
selected.update(v['file'].removesuffix('.png') for v in plan.get('source_overrides',[]))
for call in plan['calls']:
    name=call['name'];source=raw_root/f'exec-{call["id"]}.png';target=FAMILY/f'{name}.png'
    expected=hashlib.sha256(source.read_bytes()).hexdigest()
    if target.exists():assert hashlib.sha256(target.read_bytes()).hexdigest()==expected
    else:shutil.copyfile(source,target)
    prompt=FAMILY/call.get('prompt',name+'.prompt.txt')
    bearing=int(name.split('-')[1])
    refs=call.get('references',[f'../../motion-reference-{plan["motion_template"]}/view-{bearing}.png',f'native-reference/view-{bearing}.png'])
    for reference in refs:assert (FAMILY/reference).is_file(),reference
    rows.append({'file':target.name,'sha256':expected,'tool':'built-in image_gen','raw_tool_output':str(source),
        'prompt':prompt.name,'prompt_sha256':hashlib.sha256(prompt.read_bytes()).hexdigest(),'references':refs,'selected':name in selected})
views=[[{'file':name+'.png','column':phase} for phase in range(4)] for name in plan['selected_strips']]
for override in plan.get('source_overrides',[]):
    views[override['bearing']][override['phase']]={'file':override['file']}
    if 'resolution_ratio' in override:
        views[override['bearing']][override['phase']]['resolution_ratio']=override['resolution_ratio']
selection={'archive':plan['archive'],'contact_pixel_height':plan.get('contact_pixel_height',110),'generated_views':views,'known_limits':plan['known_limits']}
provenance={'archive':plan['archive'],'date':'2026-10-28','calls':rows,'reference_policy':plan['reference_policy']}
for filename,data in [('source-selection.json',selection),('generation-provenance.json',provenance)]:
    target=FAMILY/filename
    assert not target.exists(),f'Append-only safeguard: {target}'
    target.write_text(json.dumps(data,indent=2)+'\n')
print(f'{plan["archive"]}: saved {len(rows)} image outputs with hashes, ordered references and prompts.')
