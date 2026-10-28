from pathlib import Path
ROOT=Path(__file__).resolve().parent
script=(ROOT.parent/'382/pack_swim_profile.py').read_text(encoding='utf-8').replace('382','383')
script=script.replace("'motionReference':'../../../381-swim-right-cycle-v2/atlas.png'","'motionReference':'../../382/swim-profile-generated-v1.png'")
script=script.replace("m['visuallyReviewed']=True","m['visuallyReviewed']=False")
script=script.replace('Native comparison inspected at2x: long glide/kick body, bent knees account for pull/recovery shortening; beard, bare chest, bracers, white wrap retained. Alpha128 cutout has no visible dark halo. Static candidate, not temporal acceptance.','Pending383 profile comparison review.')
(ROOT/'pack_swim_profile.py').write_text(script,encoding='utf-8')
