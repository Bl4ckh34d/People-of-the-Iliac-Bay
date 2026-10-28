from pathlib import Path
p=Path(__file__).resolve().parent
s=(p/'package_npc220_complete.py').read_text().replace('220','425')
s=s.replace("folder=HERE/f'npc425-mouths-v1/{mood}';mp=","folder=HERE/'npc425-mouths-v1'/('guarded-v2' if mood=='guarded' else mood);mp=")
s=s.replace('[22,35,43,48]','[20,39,45,52]').replace('allowed[35:48,22:43]','allowed[39:52,20:45]').replace('[:5,22:43]','[4:9,20:45]')
s=s.replace('Worker inspected all six native mouth contacts and bounded E/atlas-edge repairs. Parent reviewed derived eyes and neutral pilot. Full54 mouth parent review pending.','Worker inspected all six native mouth contacts. Parent reviewed derived eyes. Full54 mouth and bounded anchor selection parent review pending.')
s=s.replace('Eye source compatibility preserves donor limitations: dark native aperture, darker HALF as highlight is occluded; Sad/Afraid expressions close.','Derived eyes from075 retain lighter and softer lids than original, weak Happy/Guarded separation; parent static inspection only.')
s=s.replace('G/FV upper-teeth to lower-lip contact is not clearly readable; no blanket phoneme pass.','G/FV contact uses own mood upper teeth anchored to B and a generated lowerlip. Happy/Sad use compatible same-face neutral G lowerlip. Semantic and temporal review remains open.')
s=s.replace('Neutral and Happy E have independent corrections preserving own round upper contour. C/E distinction remains subtle at native size.','Neutral E-v3 is independently generated from B and preserves its own round narrower contour. Sad E uses that same-face upper contour; other E poses retain generated contour. Some E are tall.')
s=s.replace('Angry E/F atlas border contamination at full y35 was replaced only with immutable original skin row.','Guarded atlas v1 shifted framing and is rejected. Whole-grid v2 preserves source-centered framing and is selected.')
s=s.replace('unassigned; no voice generated or inherited','individual female voice required but unassigned; no voice generated or inherited; art-only candidate is not complete delivery')
s=s.replace('Independent generated contour, no B-width copy. Static row4 inspection only.','Own rounded E contour; neutral/Sad selected toothrow8. Other emotional upper lip exposure differs; no B-width copy to E.')
(p/'package_npc425_complete.py').write_text(s)
