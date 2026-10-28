"""Pin root static reviews after direct contact inspection; never infer temporal acceptance."""
import hashlib,json
from pathlib import Path
HERE=Path(__file__).resolve().parent
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    rows=[]
    def add(name,manifest,contacts,limits):
        p=HERE/manifest
        rows.append({'candidate':name,'manifest':str(p),'manifestSha256':digest(p),'contacts':[{'file':str(HERE/f),'sha256':digest(HERE/f)} for f in contacts],'review':'root-direct-static-contact','limitations':limits,'temporalAcceptance':False,'runtimeAcceptance':False})
    for n in range(391,396):
        base=f'mobile-locomotion/families/{n}/candidate-v1'
        add(f'run-{n}',base+'/manifest-reviewed.json',[base+'/review-0.png',base+'/review-1.png'],['Native front/profile/rear compared separately. Cloth, face and limb depth vary;168 mirrors in whole381-395 checkpoint are explicit derivatives.'])
    for n in range(389,394):
        add(f'melee-{n}',f'combat/candidate-melee-{n}-eight-directions/manifest.json',[f'combat/melee-{n}-directions-0-3-review.png',f'combat/melee-{n}-directions-4-7-review.png'],['392 initial missing blue headwear rejected; current eight strips v2 restore source headwear.' if n==392 else 'Native costume and pouch placement checked; rear-left grip remains partly occluded.','Palette, hair and proportion differences remain.'])
    for n in [46,47]:
        add(f'portrait-{n:03}',f'portraits/npc{n:03}-complete-candidate/manifest.json',[f'portraits/npc{n:03}-eyes-v1/selected54-contact.png',f'portraits/npc{n:03}-complete-candidate/mouths54-contact.png'],['Own eyes plus54 previously counted exact mouth derivatives. Narrow HALF, brighter lateral eyes, subtle mood differences and donor mouth phoneme limits remain. Individual voice and AV review open.'])
    (HERE/'parent-review-expansion.json').write_text(json.dumps({'scope':'Static reviewed expansion after earlier root checkpoint. Idle and traversal reviews additionally pinned by their agents.','reviews':rows},indent=2)+'\n')
    folder=HERE/'portraits/exact-feature-reuse-wave3';reviewed=[]
    for n in [236,276,285,300,308]:
        p=folder/f'common-{n}/manifest.json';contact=p.parent/'mouths-54-contact.png'
        reviewed.append({'targetId':f'common-{n}','manifestSha256':digest(p),'contacts':[{'file':str(contact),'sha256':digest(contact),'feature':'mouths','statesInspected':54}],'scope':'Root inspected source pair and all54 states. Exact220 mouth transfer; inherited C/E and G/FV limits, no new poses, no temporal/voice acceptance.'})
    (folder/'parent-static-review.json').write_text(json.dumps({'reviewed':reviewed,'newlyGeneratedPoses':0},indent=2)+'\n')
    p=HERE/'portraits/npc236-masters-v1/masters-manifest.json';m=json.loads(p.read_text());m['visualAcceptance']={'scope':'Root direct native composite comparison; masters only','reviewContactSha256':digest(p.parent/'masters-review.png'),'limitations':['Happy restrained; sad/fear both raised-brow but fear outer brow higher. Eyes remain native rows21/22. No blink/lateral validation yet.']};p.write_text(json.dumps(m,indent=2)+'\n')
    print(json.dumps({'rootGroups':len(rows),'wave3FullContacts':len(reviewed),'masters236Reviewed':6}))
if __name__=='__main__':main()
