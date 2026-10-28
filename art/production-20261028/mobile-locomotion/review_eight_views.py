"""Fresh source, atlas, geometry and status evidence for the 385 candidate."""
from pathlib import Path
from PIL import Image
import hashlib
import json
import struct

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'eight-view-candidate-v1'
REPO=ROOT.parents[2]
RAW=Path('C:/Users/Daniel/.codex/generated_images/01a1807c-774d-79c1-9bfd-2639419d2660')
PROVENANCE={
 '385-front-cycle-v1-rejected.png':('prompt-385-front-cycle-v1.txt','exec-47a374d3-89cd-4674-bff8-e08312333c34.png',['../../run/mobile/385/native-view-0-four-frames.png','left-cycle-v2/contact-sheet.png']),
 '385-front-far-contact-v1.png':('prompt-385-front-far-contact-v1.txt','exec-9797967f-42bd-4003-84e5-3c4d346977f0.png',['385-front-near-contact-source.png']),
 '385-front-far-flight-v1.png':('prompt-385-front-far-flight-v1.txt','exec-d4758408-645b-4f5f-9a8e-29d87302a144.png',['385-front-far-contact-v1.png']),
 '385-front-left-cycle-v2.png':('prompt-385-front-left-cycle-v2.txt','exec-6de4172f-c654-4c91-a8ea-a63690d3a8c8.png',['front-cycle-v1/contact-sheet.png','../../run/mobile/385/native-view-1-four-frames.png']),
 '385-left-near-contact-v1.png':('prompt-385-left-near-contact-v1.txt','exec-3c55f32b-ca34-4cb6-81eb-b031d10ef34b.png',['385-run-opposite-keyframe-v1.png','../../run/mobile/385/native-view-2-four-frames.png']),
 '385-left-far-contact-v1.png':('prompt-385-left-far-contact-v1.txt','exec-a87ee6a9-0cb8-4856-9b64-79a2ee0dfe20.png',['385-run-opposite-keyframe-v1.png','../../run/mobile/385/native-view-2-four-frames.png']),
 '385-left-far-flight-v1.png':('prompt-385-left-far-flight-v1.txt','exec-395da6f6-aad8-4b10-b455-7fa97867f14e.png',['385-left-far-contact-v1.png']),
 '385-left-near-flight-v2.png':('prompt-385-left-near-flight-v2.txt','exec-a82d2830-cf24-43ad-8461-420de1020462.png',['385-run-opposite-keyframe-v1.png','385-left-far-flight-v1.png']),
 '385-rear-left-cycle-v1.png':('prompt-385-rear-left-cycle-v1.txt','exec-22d2363d-42c7-4cef-aaca-b4cfe1b3b6dc.png',['385-front-left-cycle-v2.png','../../run/mobile/385/native-view-3-four-frames.png']),
 '385-back-cycle-v1.png':('prompt-385-back-cycle-v1.txt','exec-55625bfe-99d0-48a4-835c-75df8c58a33a.png',['../../run/mobile/385/native-view-4-four-frames.png','front-cycle-v1/contact-sheet.png']),
 '385-back-right-contact-v1.png':('prompt-385-back-right-contact-v1.txt','exec-e5665b3a-be2a-4254-9db8-93dd180571fe.png',['385-back-left-contact-source.png']),
 '385-back-left-flight-v1.png':('prompt-385-back-left-flight-v1.txt','exec-94e2c96d-efad-41e9-a5a9-d964c81480aa.png',['385-back-left-contact-source.png']),
 '385-rear-right-cycle-v1.png':('prompt-385-rear-right-cycle-v1.txt','exec-6d06e82e-049d-471b-9609-08342c424147.png',['385-front-left-cycle-v2.png','../../run/mobile/385/native-view-5-four-frames.png']),
 '385-rear-right-right-contact-v3.png':('prompt-385-rear-right-right-contact-v3.txt','exec-b9e5d621-5ed1-4549-87b3-2246e860ca08.png',['385-rear-right-phase2-source.png']),
 '385-rear-right-right-flight-v3.png':('prompt-385-rear-right-right-flight-v3.txt','exec-998039ae-5e66-405b-b0bb-6854c7082be4.png',['385-rear-right-right-contact-v3.png']),
 '385-right-profile-cycle-v1.png':('prompt-385-right-profile-cycle-v1.txt','exec-83456a0a-3de4-46f2-a45c-e97cf85f9f27.png',['left-cycle-v2/contact-sheet.png','../../run/mobile/385/native-view-6-four-frames.png']),
 '385-front-right-cycle-v1.png':('prompt-385-front-right-cycle-v1.txt','exec-9ba08012-0c22-4842-b939-2b0e3cef0581.png',['385-front-left-cycle-v2.png','../../run/mobile/385/native-view-7-four-frames.png']),
 '385-front-right-left-contact-v2.png':('prompt-385-front-right-left-contact-v2.txt','exec-bda1a812-d446-4dea-968b-f15bbc7b842f.png',['385-front-right-phase0-source.png']),
 '385-front-right-left-flight-v2.png':('prompt-385-front-right-left-flight-v2.txt','exec-7df797da-fabf-4d66-907a-fb9ca7bcb8ff.png',['385-front-right-left-contact-v2.png'])}
DERIVATIVES={
 '385-front-near-contact-source.png':('385-front-cycle-v1-rejected.png',[0,0,537,732]),
 '385-front-near-flight-source.png':('385-front-cycle-v1-rejected.png',[537,0,1074,732]),
 '385-back-left-contact-source.png':('385-back-cycle-v1.png',[0,0,543,724]),
 '385-back-right-flight-source.png':('385-back-cycle-v1.png',[543,0,1086,724]),
 '385-front-right-phase0-source.png':('385-front-right-cycle-v1.png',[0,0,537,732]),
 '385-rear-right-phase2-source.png':('385-rear-right-cycle-v1.png',[1074,0,1611,732])}
provenance=[]
for name,(prompt,rawname,references) in PROVENANCE.items():
    assert (ROOT/name).read_bytes()==(RAW/rawname).read_bytes(),name
    assert (ROOT/prompt).exists()
    assert all((ROOT/reference).exists() for reference in references)
    provenance.append({'file':name,'method':'built-in imagegen','prompt':prompt,'references':references,
                       'sha256':hashlib.sha256((ROOT/name).read_bytes()).hexdigest(),'raw_tool_output':str(RAW/rawname),'raw_copy_unchanged':True})
for name,(source,crop) in DERIVATIVES.items():
    a=Image.open(ROOT/name).convert('RGBA')
    b=Image.open(ROOT/source).convert('RGBA').crop(crop)
    assert a.tobytes()==b.tobytes(),name
    provenance.append({'file':name,'method':'exact mechanical source crop','source':source,'crop':crop,'no_redraw_or_mirror':True})
native_path=REPO.parent/'GameFiles/arena2/TEXTURE.385'
native_data=native_path.read_bytes()
native_records=[]
for record in range(6):
    offset=struct.unpack_from('<i',native_data,26+record*20+2)[0]
    width,height=struct.unpack_from('<hh',native_data,offset+4)
    sx,sy=struct.unpack_from('<hh',native_data,offset+24)
    native_records.append({'record':record,'pixel_size':[width,height],'record_scale':[sx,sy],
                           'native_adjusted_size':[width+int(width*sx/256),height+int(height*sy/256)]})
assert [r['native_adjusted_size'][1] for r in native_records]==[80,80,80,80,80,78]
manifest=json.loads((OUT/'manifest.json').read_text())
atlas=Image.open(OUT/'atlas-8x4.png').convert('RGBA')
checks=[]
for e in manifest['frames']:
    im=Image.open(OUT/e['file']).convert('RGBA')
    b,p=e['bearing'],e['phase'];w,h=manifest['canvas']
    assert im.tobytes()==atlas.crop((p*w,b*h,(p+1)*w,(b+1)*h)).tobytes()
    cutout=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    assert cutout[0]>0 and cutout[1]>0 and cutout[2]<w and cutout[3]<h
    if p in (1,3): assert e['lowest_cutout_clearance']>=3
    else: assert abs(e['lowest_cutout_clearance'])<=1
    e['visual_reviewed']=True
    checks.append({'bearing':b,'phase':p,'atlas_equal':True,'cutout_not_clipped':True,'ground_or_flight_passed':True})
gif=Image.open(OUT/'animated-preview.gif')
assert gif.n_frames==4
assert len({e['frame_pixel_sha256'] for e in manifest['frames']})==32
manifest.update({'visual_reviewed':True,'source_provenance':provenance,'technical_verification':checks,
 'visual_review':{'evidence':['review-0.png','review-1.png'],'method':'All32 packed frames inspected through local view_image contact sheets; latest clearance-adjusted right/back page re-inspected.',
  'observations':['Eight canonical visible bearings present; left/right profiles and front/rear quarters remain distinct.',
   'Visible arm arrangements alternate between half-cycles; front, back, front-right and rear-right needed separately generated correction phases.',
   'Pouch is preserved as an asymmetric costume feature. All generated bearings were authored independently, so no unreviewed whole-sprite mirror changed its side.',
   'All feet/hands fit cutout bounds; actual strip alpha gutters prevent the right-profile third pose foot being sliced by an assumed equal grid.'],
  'limits':['This is not native game playback or user approval. Anatomical phase names describe intended sequencing; obscured thighs do not establish perfect three-dimensional leg ownership.',
   'Palette saturation, facial detail, tunic folds and exact body widths still drift between generation calls.',
   'Four-phase cadence omits intermediate passing poses; movement smoothness requires review at intended playback and world scale.']},
 'export_geometry':{'status':'proposed contract, not integrated','native_source':str(native_path),
  'native_source_sha256':hashlib.sha256(native_data).hexdigest(),'native_records':native_records,
  'native_formula':'size + int(size * recordScale / 256); C# int truncates toward zero',
  'native_code_reference':'daggerfall-unity/Assets/Scripts/Internal/MobilePersonBillboard.cs:321-346',
  'global_scale':0.025,'global_scale_reference':'daggerfall-unity/Assets/Scripts/MeshReader.cs:58',
  'optional_texture_replacement_scale_checked':False,
  'pixel_canvas':[160,176],'mean_contact_content_height_px':110,'pixel_ground_pivot_top_left':[80,156],
  'ground_pivot_uv_bottom_left':[0.5,20/176],
  'target_native_walk_height_before_global_scale':80,'target_walk_height_unity_units':2.0,
  'native_idle_height_unity_units':1.95,'unity_units_per_content_pixel':2/110,
  'proposed_quad_size_unity_units':[160*2/110,176*2/110],
  'proposed_quad_center_above_ground_pivot_unity_units':(156-176/2)*2/110,
  'boundary':'These target sizes use native archive385 before optional TextureReplacement overrides. The176px padded canvas is not the intended figure height. No renderer was modified or tested.'}})
(OUT/'manifest-reviewed.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
supplement={'date':'2026-10-28','archive':385,'candidate':'eight-view-candidate-v1/manifest-reviewed.json','candidate_bearings':8,'candidate_frames':32,
 'generated':True,'technical_checked':True,'visual_reviewed':True,'integrated':False,'user_approved':False,
 'derived_mirrored_frames':0,'other_civilian_families_unfinished':23,'guard_family_unfinished':True,
 'det_orc_families_unfinished':True,'accepted_civilian_run_bearing_sequences':0,
 'note':'This completes one eight-bearing candidate only; it does not mark all-family production or final acceptance complete.'}
(ROOT/'coverage-eight-view-supplement.json').write_text(json.dumps(supplement,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'checked_source_outputs':len(PROVENANCE),'exact_source_crops':len(DERIVATIVES),'checked_frames':32,'gif_frames':4,
 'native385_walk_units':2.0,'native385_idle_units':1.95,'runtime_integrated':False,'user_approved':False},indent=2))
