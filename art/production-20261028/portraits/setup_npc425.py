from pathlib import Path
import json,shutil
p=Path(__file__).resolve().parent
for m in ['happy','angry','sad','afraid','guarded']:
 shutil.copyfile(json.loads((p/'npc425-mouth-masters-v1'/f'{m}-receipt.json').read_text())['rawSource'],p/'npc425-mouth-masters-v1'/f'{m}-raw.png')
s=(p/'build_npc220_mouth_masters.py').read_text().replace('220','425').replace('BOX=((22,35,43,47),)','BOX=((20,39,45,49),)').replace("'x':22,'y':35,'width':21,'height':17","'x':20,'y':39,'width':25,'height':13").replace('no196 mouth art','no075 mouth art')
(p/'build_npc425_mouth_masters.py').write_text(s)
