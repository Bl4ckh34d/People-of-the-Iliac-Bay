from pathlib import Path
ROOT=Path(__file__).resolve().parent
script=(ROOT.parent/'382/pack_jump_eight.py').read_text(encoding='utf-8').replace('382','383')
script=script.replace("head_x={1:[225,745,1260,1780],2:[217,805,1300,1827],3:[237,720,1260,1780],4:[263,733,1314,1800]}","head_x={1:[240,735,1290,1787],2:[244,760,1253,1785],3:[248,750,1274,1788],4:[263,768,1282,1789]}")
script=script.replace('scales={1:21/95,2:21/100,3:21/95,4:21/95}','scales={1:21/95,2:21/96,3:21/94,4:21/90}')
script=script.replace('version=2 if v==2 and p==1 else 1','version=1')
script=script.replace('Artwork keeps generated muscle/face detail, not exact native palette.','Teal tunic/yellow trim retained, but face, hem folds and saturation differ from native palette.')
script=script.replace("m['visuallyReviewed']=True","m['visuallyReviewed']=False")
script=script.replace("m['visualReview']='Both native-scale contact pages inspected: four poses and five authored cameras distinct; three exact mirrors preserve symmetric limb timing. Front0 body visibly larger than other bearings; generated head/body ratios and native palette remain unaccepted.'","m['visualReview']='Awaiting383 combined contact review. Profile apex torso slightly turns toward camera; keep as explicit limit.'")
(ROOT/'pack_jump_eight.py').write_text(script,encoding='utf-8')
