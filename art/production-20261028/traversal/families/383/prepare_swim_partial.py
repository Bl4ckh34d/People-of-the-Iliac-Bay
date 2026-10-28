from pathlib import Path
ROOT=Path(__file__).resolve().parent
script=(ROOT.parent/'382/pack_swim_eight.py').read_text(encoding='utf-8').replace('382','383').replace('swim-eight-views-v1','swim-five-views-v1')
script=script.replace('for v in range(8):','for v in [0,1,2,3,6]:').replace('version=2 if v==4 and p==0 else 1','version=1')
script=script.replace("for p,name in enumerate(names):\n            x=100+p*W", "for p,name in enumerate(names):\n            if (v,p) not in frames:\n                d.text((100+p*W+8,y+50),'MISSING / NOT SELECTED',font=font,fill=(220,170,100));continue\n            x=100+p*W")
script=script.replace('383 Swim | all8 authored','383 Swim | 5 views authored; 3 open')
script=script.replace("'status':'eight-bearing-static-candidate','frameCount':32,'authoredFrames':32","'status':'five-bearing-static-candidate','frameCount':20,'authoredFrames':20")
script=script.replace('Rear Glide camera repaired only, other3 original phases retained.','Bearing4 blocked by tool and intentionally unselected; bearings5/7 not produced. No excluded retry counted.')
script=script.replace("m['visuallyReviewed']=True","m['visuallyReviewed']=False")
script=script.replace('Both contact pages inspected at native pixel size: eight bearings and four breaststroke phases distinct, long profile anatomy and camera foreshortening plausible. Quarter camera elevation and proportions vary. Static candidate, not temporal/waterline acceptance.','Pending383 five-bearing contact review; original profile reviewed separately.')
script=script.replace("(OUT/'manifest.json').write_text", "m['availableBearings']=[0,1,2,3,6]\nm['missingBearings']=[4,5,7]\nm['blockedEvidence']='../unselected-evidence/swim-rear-tool-block.json'\n(OUT/'manifest.json').write_text")
(ROOT/'pack_swim_partial.py').write_text(script,encoding='utf-8')
