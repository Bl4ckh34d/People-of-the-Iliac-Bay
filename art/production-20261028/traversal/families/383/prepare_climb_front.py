from pathlib import Path
ROOT=Path(__file__).resolve().parent
script=(ROOT.parent/'382/pack_climb_profile.py').read_text(encoding='utf-8').replace('382','383').replace('climb-profile-v1','climb-front-v1').replace('climb-profile-generated-v1.png','climb-view-0-generated-v1.png').replace('SCALE=21/110','SCALE=21/90')
script=script.replace('heads=[(274,90),(794,105),(294,865),(784,805)]','heads=[(317,107),(751,108),(318,876),(785,872)]')
script=script.replace("'bearing':6","'bearing':0").replace('right profile','front').replace("'motionReference':'../../../381-climb-paired-pilot-generated-v1.png'","'motionReference':'../../382/climb-view-0-generated-v1.png'")
script=script.replace('Hair/beard, musculature and wrap folds differ from native low-detail pixels.','Face, tunic folds, hem and yellow saturation differ from native low-detail pixels.')
script=script.replace('Native comparison inspected: four genuine joint poses preserve beard, bare torso, bracers and plain white wrap. Far hand/foot occlusion, head/body ratio and physical wall contact remain limits; static candidate only.','Pending383 front climb native comparison review.')
script=script.replace("m['visuallyReviewed']=True","m['visuallyReviewed']=False")
(ROOT/'pack_climb_front.py').write_text(script,encoding='utf-8')
