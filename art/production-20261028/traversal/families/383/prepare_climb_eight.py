from pathlib import Path
ROOT=Path(__file__).resolve().parent
script=(ROOT.parent/'382/pack_climb_eight.py').read_text(encoding='utf-8').replace('382','383')
old="heads={0:[(294,100),(748,104),(302,875),(778,867)],1:[(320,150),(774,175),(332,916),(798,898)],2:[(295,100),(750,104),(302,850),(784,848)],3:[(265,125),(741,140),(291,903),(770,876)],4:[(295,150),(732,155),(294,897),(762,888)],5:[(287,125),(748,143),(288,880),(754,875)],7:[(337,138),(749,152),(335,897),(784,888)]}"
new="heads={1:[(331,150),(764,160),(337,915),(790,878)],2:[(333,135),(744,140),(340,875),(778,872)],3:[(285,136),(741,140),(291,903),(728,894)],4:[(295,150),(732,155),(294,897),(762,888)],5:[(287,125),(748,143),(288,880),(754,875)],6:[(264,90),(800,108),(295,864),(775,812)],7:[(343,140),(750,151),(332,896),(766,885)]}"
assert old in script;script=script.replace(old,new)
script=script.replace('headwidth={0:110,1:100,2:100,3:95,4:100,5:95,7:110};rightcut={0:720,1:768,2:704,3:736,4:736,5:736,7:736}','headwidth={1:95,2:95,3:95,4:100,5:95,6:110,7:100};rightcut={1:744,2:720,3:740,4:736,5:736,6:720,7:736}')
script=script.replace('if v==6:','if v==0:').replace("ROOT/'climb-profile-v1'","ROOT/'climb-front-v1'").replace("'pixelScale':21/110","'pixelScale':21/90")
script=script.replace('version=2 if v==4 and p!=2 else 1','version=2 if v==3 and p in [1,2] else 1')
script=script.replace('Rear pouch was source-conditioned repaired in reach/pull/push only; tuck original kept.','Rear-left pull/tuck arm identity repaired from correct382 motion source; reach/push originals retained.')
script=script.replace("m['visuallyReviewed']=True","m['visuallyReviewed']=False")
script=script.replace("m['visualReview']='Both32-frame contact pages inspected. Right-arm identity readable across front/rear and opposed profiles; four phases distinct. Right profile leans more than other views, far limbs occluded. No temporal, physical-contact or palette acceptance.'","m['visualReview']='Pending383 combined contact review. Right profile stronger lean and repaired rear-left sleeve-edge drift remain known limits.'")
(ROOT/'pack_climb_eight.py').write_text(script,encoding='utf-8')
