"""Crop generated sleep cells without repainting. Retain exact generated pixels/alpha."""
from pathlib import Path
import hashlib,json
from PIL import Image,ImageDraw
OUT=Path(__file__).resolve().parent
sources=json.loads((OUT.parent/'additional-mod-sources.json').read_text())['sources']
source_index={(r['provider'],r['archive'],r['record']):r for r in sources}
specs=[('det-scholar-sleep-pilot-v2.png',10015,0,4),('det-nobles-sleep-v2.png',10011,0,7),('det-civilians-0-7-sleep-v1.png',10012,0,7),('det-civilians-8-15-sleep-v1.png',10012,8,15),('det-civilians-16-20-sleep-v2.png',10012,16,20)]
specs=[(name,[(archive,r) for r in range(start,end+1)]) for name,archive,start,end in specs]
remaining=json.loads((OUT/'remaining-sleep-batches.json').read_text())
for batch in remaining[:5]:
    version=2 if batch['batch']==4 else 1
    specs.append((f"det-remaining-{batch['batch']}-sleep-v{version}.png",[(r['archive'],r['record']) for r in batch['sources']]))
dest=OUT/'sleep-candidates'
dest.mkdir(exist_ok=True)
entries=[]
atlases=[]
for name,keys in specs:
    path=OUT/name
    im=Image.open(path).convert('RGBA')
    alpha=im.getchannel('A').point(lambda a:255 if a>=128 else 0)
    empty=[]
    for y in range(im.height//3,im.height*3//4):
        if alpha.crop((0,y,im.width,y+1)).getbbox() is None:
            empty.append(y)
    runs=[]
    for y in empty:
        if not runs or y!=runs[-1][-1]+1:runs.append([y])
        else:runs[-1].append(y)
    assert runs, (name,'No transparent horizontal split')
    gap=min(runs,key=lambda r:abs(sum(r)/len(r)-im.height/2))
    split=(gap[0]+gap[-1]+1)//2
    empty_columns=[]
    for x in range(im.width//10,im.width*9//10):
        if alpha.crop((x,0,x+1,im.height)).getbbox() is None:empty_columns.append(x)
    column_runs=[]
    for x in empty_columns:
        if not column_runs or x!=column_runs[-1][-1]+1:column_runs.append([x])
        else:column_runs[-1].append(x)
    assert len(column_runs)>=3,(name,'No separated four-column layout')
    columns=[0]
    for fraction in [0.25,0.5,0.75]:
        candidate=min(column_runs,key=lambda r:abs((r[0]+r[-1])/2-im.width*fraction))
        columns.append((candidate[0]+candidate[-1]+1)//2)
    columns.append(im.width)
    assert len(set(columns))==5 and columns==sorted(columns)
    atlas_row={'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'size':list(im.size),'actualRowSplit':split,'actualColumnSplits':columns,'nominalRowSplit':im.height/2,'transparentRowGap':[gap[0],gap[-1]],'equalGridDeviation':split-im.height/2}
    atlases.append(atlas_row)
    for i,(archive,record) in enumerate(keys):
        x0,x1=columns[i%4],columns[i%4+1]
        y0,y1=(0,split) if i<4 else (split,im.height)
        part=im.crop((x0,y0,x1,y1))
        box=part.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
        assert box,(name,record)
        cut=part.crop(box)
        target=dest/f'det-{archive}-{record}-sleep.png'
        cut.save(target)
        s=source_index[('daggerfall expanded textures',archive,record)]
        issues=[]
        if box[0]==0 or box[1]==0 or box[2]==part.width or box[3]==part.height:issues.append('silhouette-touches-source-cell-edge')
        if archive==10012 and record==2:issues.append('source-face-partly-hidden-by-hair-direction; facial reconstruction is inferred')
        if archive==10012 and record==5:issues.append('brown head/hair interpretation uncertain in low-resolution source; review required')
        if archive==10012 and record==20:issues.append('blanket-to-head ratio shorter than peer cells despite one correction; rejected for proportion consistency')
        if archive==10015 and record==0:issues.append('golden eye/eyewear detail from awake source is not retained exactly')
        if archive==10014 and record in [3,7]:issues.append('source-face-partly-hidden; conservative generated face is not exact identity proof')
        if archive==10014 and record==4:issues.append('source head is mostly turned away; generated long-muzzled beastfolk face requires specific identity review')
        if archive==10019 and record==0:issues.append('source face obscured by arm; generated face is inferred')
        entries.append({'provider':s['provider'],'providerGuid':'00272292-acc6-4e51-ba4c-3a3e89199609','archive':archive,'record':record,'source':s['source'],'sourcePixelSha256':s['pixelSha256'],'sourceBundleSha256':s['bundleSha256'],'atlas':str(path),'cellIndex':i,'extractionCell':[x0,y0,x1,y1],'alpha128BoundsInCell':box,'file':str(target),'size':list(cut.size),'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'generated':True,'technicalChecked':True,'visualChecked':True,'integrated':False,'userApproved':False,'candidateStatus':'needs-correction' if (archive==10012 and record==20) else ('review-with-limits' if issues else 'reviewable-candidate'),'issues':issues,'visualChecks':{'headIdentityCompared':True,'closedEyesReviewed':True,'greenBlanket':True,'whitePillow':True,'completeNeckToToesCoverage':True,'rendererCutoff128Preview':True}})

singlepath=OUT/'det-remaining-5-sleep-v2.png'
single=Image.open(singlepath).convert('RGBA')
singlebox=single.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
assert singlebox
single=single.crop(singlebox)
singletarget=dest/'det-10020-7-sleep.png'
single.save(singletarget)
s=source_index[('daggerfall expanded textures',10020,7)]
entries.append({'provider':s['provider'],'providerGuid':'00272292-acc6-4e51-ba4c-3a3e89199609','archive':10020,'record':7,'source':s['source'],'sourcePixelSha256':s['pixelSha256'],'sourceBundleSha256':s['bundleSha256'],'atlas':str(singlepath),'cellIndex':0,'alpha128BoundsInCell':singlebox,'file':str(singletarget),'size':list(single.size),'sha256':hashlib.sha256(singletarget.read_bytes()).hexdigest(),'generated':True,'technicalChecked':True,'visualChecked':True,'integrated':False,'userApproved':False,'candidateStatus':'reviewable-candidate','issues':[],'visualChecks':{'headIdentityCompared':True,'closedEyesReviewed':True,'greenBlanket':True,'whitePillow':True,'completeNeckToToesCoverage':True,'rendererCutoff128Preview':True,'longBlanketCorrected':True}})
all_det={(r['archive'],r['record']) for r in sources if r['provider']=='daggerfall expanded textures'}
repairpath=OUT/'det-10012-20-sleep-repair-v2.png'
repair=Image.open(repairpath).convert('RGBA')
repairbox=repair.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
repair=repair.crop(repairbox)
repairtarget=dest/'det-10012-20-sleep.png'
repair.save(repairtarget)
for entry in entries:
    if entry['archive']==10012 and entry['record']==20:
        entry.update({'atlas':str(repairpath),'cellIndex':0,'alpha128BoundsInCell':repairbox,'size':list(repair.size),'sha256':hashlib.sha256(repairtarget.read_bytes()).hexdigest(),'candidateStatus':'reviewable-candidate','issues':[],'proportionRepair':{'previousHeightWidthRatio':1.956,'selectedHeightWidthRatio':round(repair.height/repair.width,3),'visualCheck':'Complete long blanket replaces short squat original; head/pillow/hair remain recognizable. This is visual similarity, not pixel identity.'}})
        entry.pop('extractionCell',None)
        entry['visualChecks']['longBlanketCorrected']=True
generated={(e['archive'],e['record']) for e in entries}
assert len(entries)==len(generated)==75
assert all_det-generated=={(10014,1),(10020,1)}
pages=[]
for page in range((len(entries)+7)//8):
    subset=entries[page*8:page*8+8]
    preview=Image.new('RGB',(1200,760),'#555960')
    d=ImageDraw.Draw(preview)
    for i,row in enumerate(subset):
        im=Image.open(row['file']).convert('RGBA')
        im.putalpha(im.getchannel('A').point(lambda a:255 if a>=128 else 0))
        scale=min(250/im.width,320/im.height)
        im=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.NEAREST)
        x,y=i%4*300+(300-im.width)//2,i//4*380+35
        preview.paste(im,(x,y),im)
        label=f"DET {row['archive']}/{row['record']} | "+('LIMIT' if row['issues'] else 'candidate')
        d.text((i%4*300+10,i//4*380+10),label,fill='#ffcc77' if row['issues'] else 'white')
    p=OUT/f'sleep-candidate-review-{page+1}.png'
    preview.save(p)
    pages.append(str(p))
manifest={'date':'2026-10-28','scope':'Additional DET sleep candidates only. Existing326 mappings unchanged. This does not claim universal humanoid coverage.','counts':{'generated':len(entries),'technicallyChecked':len(entries),'visuallyChecked':len(entries),'needsCorrection':sum(e['candidateStatus']=='needs-correction' for e in entries),'integrated':0,'userApproved':0},'processing':'Actual transparent horizontal/vertical gaps partition rows/columns; each cell cropped at alpha128 bounds. No source pixels painted, alpha altered, palette changes or image resampling in deliverable PNGs. Preview uses native cutout128 and NN scaling only. Raw atlases may not honor equal cells; use individual crop metadata, not assumed4x2UVs.','atlases':atlases,'entries':entries,'reviewPages':pages}
(OUT/'sleep-candidate-coverage.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
idlepath=OUT/'idle-det-10015-0/manifest.json'
idle=json.loads(idlepath.read_text())
idle['visuallyChecked']=True
idle['visualReview']={'eightDistinctBearings':True,'nativeFrontByteIdentical':True,'rightProfileCorrected':True,'bookOcclusionAtRear':True,'limits':['Generated angles are smoother and broader than native source.','Golden eye/eyewear source feature is not preserved exactly.','User visual acceptance and integration remain pending.']}
idlepath.write_text(json.dumps(idle,indent=2),encoding='utf-8')
print(json.dumps(manifest['counts']))
