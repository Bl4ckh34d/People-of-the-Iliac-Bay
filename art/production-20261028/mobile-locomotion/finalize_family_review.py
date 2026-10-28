"""Finalize evidence only after the operator inspected both local review pages."""
from pathlib import Path
from PIL import Image
import hashlib
import json
import sys

assert len(sys.argv) in (3,4) and sys.argv[2]=='--visually-reviewed'
ROOT=Path(__file__).resolve().parent
FAMILY=ROOT/'families'/sys.argv[1]
REV=sys.argv[3] if len(sys.argv)>3 else 'v1'
OUT=FAMILY/f'candidate-{REV}'
PROVENANCE=FAMILY/('generation-provenance.json' if REV=='v1' else f'generation-provenance-{REV}.json')
manifest=json.loads((OUT/'manifest.json').read_text())
native=json.loads((FAMILY/'native-reference.json').read_text())
provenance=json.loads(PROVENANCE.read_text())
for row in provenance['calls']:
    local=FAMILY/row['file'];raw=Path(row['raw_tool_output'])
    expected=hashlib.sha256(local.read_bytes()).hexdigest()
    assert hashlib.sha256(raw.read_bytes()).hexdigest()==expected
    assert row['sha256']==expected
    assert row['prompt_sha256']==hashlib.sha256((FAMILY/row['prompt']).read_bytes()).hexdigest()
manifest['native_world_sizes_by_bearing']=[{'bearing':v['bearing'],'pixel_dimensions':v['frames'][0]['dimensions'],
    'native_record_scale_xy':v['frames'][0]['nativeRecordScaleXY'],'scaled_before_global':v['frames'][0]['scaledSizeBeforeGlobalScaleAndOverrides'],
    'world_size':[x*.025 for x in v['frames'][0]['scaledSizeBeforeGlobalScaleAndOverrides']]} for v in native['references']]
manifest['generation_provenance_sha256']=hashlib.sha256(PROVENANCE.read_bytes()).hexdigest()
manifest['visual_reviewed']=True
manifest['visual_review_method']='Both local review pages inspected after packing: all8rendered views, limb alternation, contact/flight silhouettes, identity details and derived-mirror labels.'
manifest['known_limits'].append('Anatomical left/right phase wording reverses in mirrored views; frame sequence follows native renderer reflection.')
atlas=Image.open(OUT/'atlas-8x4.png').convert('RGBA')
for entry in manifest['frames']:
    image=Image.open(OUT/entry['file']).convert('RGBA');b,p=entry['bearing'],entry['phase']
    assert hashlib.sha256(image.tobytes()).hexdigest()==entry['frame_pixel_sha256']
    assert image.tobytes()==atlas.crop((p*160,b*176,(p+1)*160,(b+1)*176)).tobytes()
    alpha=image.getchannel('A');hist=alpha.histogram()
    entry['alpha_summary']={'zero_pixels':hist[0],'cutout_pixels':sum(hist[128:]),'fully_opaque_pixels':hist[255],'max_alpha':alpha.getextrema()[1]}
    entry['visual_reviewed']=True
    if entry['derived_mirrored']:
        source=entry['derived_from']
        original=Image.open(OUT/f'frames/view-{source["bearing"]}-frame-{source["phase"]}.png').convert('RGBA')
        assert original.transpose(Image.Transpose.FLIP_LEFT_RIGHT).tobytes()==image.tobytes()
(OUT/'manifest-reviewed.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(f'{manifest["archive"]}:32frames;20generated poses;12verified mirror derivatives;{len(provenance["calls"])}generation hashes verified;all8views visually reviewed;not integrated/approved.')
