"""Export generated NPC bearings; preserve original at its actual bearing or separately."""
from pathlib import Path
import hashlib,json
from PIL import Image,ImageDraw
OUT=Path(__file__).resolve().parent
SOURCES=OUT.parent/'sources/native'
INVENTORY=json.loads((OUT.parent/'source-inventory.json').read_text())
REVIEWS=json.loads((OUT/'native-idle-visual-review-records.json').read_text())
NAMES=['front-native','front-left','left-profile','rear-left','back','rear-right','right-profile','front-right']
results=[]
for archive,record,version in [(183,3,1),(183,2,2),(182,54,2),(182,9,2),(182,35,2),(182,27,2),(182,28,1),(182,42,1),(182,45,3),(182,19,1),(182,20,1),(182,23,1),(182,25,1),(182,4,1),(182,26,1),(182,15,1),(182,17,1),(182,22,1),(182,40,1),(182,39,1),(182,47,1),(182,56,1),(183,5,1),(183,4,1),(183,0,1),(183,1,1),(182,46,1),(182,37,1),(182,38,1),(183,6,1),(183,13,1),(183,7,1),(183,10,1),(183,8,1),(183,11,1),(183,9,1),(183,12,1),(183,14,1),(183,16,1),(183,15,1),(183,17,1)]:
    source=SOURCES/f'{archive}_{record}-0.png'
    native=Image.open(source).convert('RGBA')
    export_height=128 if (archive,record)==(182,38) else native.height
    pixelhash=hashlib.sha256(native.tobytes()).hexdigest()
    aliases=[row['id'] for row in INVENTORY['identities'] if row.get('sourcePixelHash')==pixelhash and row.get('sourceDimensions')==list(native.size)]
    sheetpath=OUT/f'native-{archive}-{record}-idle-eight-v{version}.png'
    sheet=Image.open(sheetpath).convert('RGBA')
    folder=OUT/f'idle-native-{archive}-{record}'
    folder.mkdir(exist_ok=True)
    native_view=7 if (archive,record) in [(182,27),(182,42),(183,13),(183,10),(183,8),(183,11),(183,9),(183,14),(183,16),(183,15)] else 0
    if (archive,record) in [(182,26),(182,47),(183,5),(183,4),(183,7)]:native_view=1
    if (archive,record) in [(182,37),(182,38)]:native_view=None
    names=NAMES.copy()
    if native_view is None:names[0]='front-generated'
    if native_view==7:
        names[0]='front-generated'
        names[7]='front-right-native'
        for obsolete in ['view-0-front-native.png','view-7-front-right.png']:
            stale=folder/obsolete
            if stale.exists():stale.unlink()
    if native_view==1:
        names[0]='front-generated'
        names[1]='front-left-native'
    frames=[]
    rows=[]
    for i,name in enumerate(names):
        source_cell={2:6,6:2}.get(i,i) if (archive,record)==(183,2) else i
        if (archive,record) in [(182,19),(182,38)]:source_cell={1:7,7:1}.get(i,i)
        if (archive,record)==(183,13):source_cell={2:6,6:2,5:3}.get(i,i)
        if (archive,record) in [(183,10),(183,9),(183,16)]:source_cell={2:6,6:2}.get(i,i)
        bounds=[round(source_cell%4*sheet.width/4),round(source_cell//4*sheet.height/2),round((source_cell%4+1)*sheet.width/4),round((source_cell//4+1)*sheet.height/2)]
        if (archive,record)==(182,28):
            bounds=[[14,24,357,494],[399,22,744,494],[793,22,1060,494],[1119,22,1426,494],[11,552,352,1039],[372,552,701,1039],[733,552,1007,1039],[1065,552,1426,1039]][source_cell]
        if (archive,record)==(182,4):
            bounds=[[66,12,306,589],[395,12,607,589],[739,12,870,588],[1012,12,1223,588],[53,606,289,1176],[378,606,596,1180],[728,606,855,1184],[1009,606,1220,1183]][source_cell]
        if (archive,record)==(182,26):
            bounds=[[17,11,273,639],[311,6,565,644],[663,5,849,644],[920,5,1151,643],[35,694,295,1329],[338,694,582,1329],[636,694,813,1329],[904,694,1151,1329]][source_cell]
        if (archive,record)==(182,47):
            bounds=[[33,8,327,504],[417,8,726,504],[783,4,1115,504],[1186,4,1504,506],[19,510,352,1015],[386,510,711,1015],[741,513,1056,1010],[1138,513,1477,1010]][source_cell]
        if (archive,record)==(182,56):
            bounds=[[46,15,364,496],[421,15,769,496],[831,15,1124,500],[1193,15,1507,500],[46,524,364,1008],[422,524,737,1005],[786,524,1064,1008],[1148,524,1492,1008]][source_cell]
        if (archive,record)==(183,0):
            bounds=[[62,5,359,482],[431,5,740,490],[862,5,1082,482],[1208,5,1484,479],[39,507,351,990],[416,507,709,988],[789,505,1019,994],[1164,505,1478,994]][source_cell]
        if (archive,record)==(183,1):
            bounds=[[37,8,349,575],[387,8,677,579],[772,8,973,575],[1035,8,1300,575],[33,594,347,1172],[383,596,655,1172],[719,594,912,1168],[1013,594,1292,1176]][source_cell]
        if (archive,record)==(183,13):
            bounds=[[17,13,321,583],[334,13,601,584],[624,13,878,586],[913,12,1202,593],[14,655,338,1226],[335,652,618,1233],[656,652,907,1231],[922,655,1205,1238]][source_cell]
        if (archive,record)==(183,11):
            bounds=[[16,49,252,691],[278,49,486,691],[511,49,720,690],[776,49,1009,689],[16,764,258,1408],[271,764,507,1409],[526,763,739,1408],[767,764,1007,1407]][source_cell]
        if (archive,record)==(183,16):
            bounds=[[16,51,401,457],[427,51,752,460],[800,51,1073,460],[1224,51,1524,460],[34,535,393,944],[429,534,752,944],[906,537,1162,942],[1188,534,1500,946]][source_cell]
        if (archive,record)==(183,17):
            bounds=[[34,13,349,482],[432,14,754,483],[858,13,1143,483],[1186,11,1498,486],[24,498,363,985],[428,498,737,989],[783,498,1085,983],[1172,498,1491,990]][source_cell]
        if (archive,record)==(182,9):
            bounds[1]=0 if source_cell<4 else 537
            bounds[3]=537 if source_cell<4 else sheet.height
        if (archive,record)==(182,27):
            bounds[1]=0 if source_cell<4 else 485
            bounds[3]=485 if source_cell<4 else sheet.height
        used_sheet=sheetpath
        if (archive,record)==(183,2) and i in [3,7]:
            used_sheet=OUT/'native-183-2-idle-rearleft-frontright-v2.png'
            pair=Image.open(used_sheet).convert('RGBA')
            side=0 if i==3 else 1
            bounds=[round(side*pair.width/2),0,round((side+1)*pair.width/2),pair.height]
            cell=pair.crop(bounds)
        elif (archive,record)==(182,23) and i==1:
            used_sheet=OUT/'native-182-23-front-left-v3.png'
            cell=Image.open(used_sheet).convert('RGBA')
            bounds=[0,0,cell.width,cell.height]
        elif (archive,record)==(182,26) and i==7:
            used_sheet=OUT/'native-182-26-angle-pair-v2.png'
            pair=Image.open(used_sheet).convert('RGBA')
            bounds=[pair.width//2,0,pair.width,pair.height]
            cell=pair.crop(bounds)
        elif (archive,record)==(182,17) and i in [1,5]:
            used_sheet=OUT/'native-182-17-angle-pair-v2.png'
            pair=Image.open(used_sheet).convert('RGBA')
            side=0 if i==1 else 1
            bounds=[side*pair.width//2,0,(side+1)*pair.width//2,pair.height]
            cell=pair.crop(bounds)
        elif (archive,record,i) in [(182,22,1),(182,40,3),(183,4,7),(183,0,7),(182,46,1),(183,13,4),(183,10,1)]:
            filename={(182,22):'native-182-22-front-left-v3.png',(182,40):'native-182-40-rear-left-v2.png',(183,4):'native-183-4-front-right-v2.png',(183,0):'native-183-0-front-right-v2.png',(182,46):'native-182-46-front-left-v3.png',(183,13):'native-183-13-back-v4.png',(183,10):'native-183-10-front-left-v2.png'}[(archive,record)]
            used_sheet=OUT/filename
            cell=Image.open(used_sheet).convert('RGBA')
            bounds=[0,0,cell.width,cell.height]
        elif (archive,record,i) in [(183,15,5),(183,17,3)]:
            used_sheet=OUT/('native-183-15-rear-right-v2.png' if record==15 else 'native-183-17-rear-left-v2.png')
            cell=Image.open(used_sheet).convert('RGBA')
            bounds=[0,0,cell.width,cell.height]
        elif (archive,record) in [(183,11),(183,14),(183,16)] and i==1:
            used_sheet=OUT/f'native-183-{record}-front-left-v3.png'
            cell=Image.open(used_sheet).convert('RGBA')
            bounds=[0,0,cell.width,cell.height]
        elif (archive,record)==(183,14) and i in [0,5]:
            used_sheet=OUT/'native-183-14-angle-triple-v2.png'
            triple=Image.open(used_sheet).convert('RGBA')
            side=0 if i==0 else 2
            bounds=[side*triple.width//3,0,(side+1)*triple.width//3,triple.height]
            cell=triple.crop(bounds)
        elif (archive,record)==(183,11) and i in [2,6]:
            used_sheet=OUT/'native-183-11-angle-triple-v2.png'
            triple=Image.open(used_sheet).convert('RGBA')
            side=1 if i==2 else 2
            bounds=[side*triple.width//3,0,(side+1)*triple.width//3,triple.height]
            cell=triple.crop(bounds)
        elif (archive,record)==(183,6) and i in [1,5,6]:
            used_sheet=OUT/('native-183-6-angle-pair-v2.png' if i==6 else 'native-183-6-angle-pair-v3.png')
            pair=Image.open(used_sheet).convert('RGBA')
            side=0 if i==1 else 1
            bounds=[side*pair.width//2,0,(side+1)*pair.width//2,pair.height]
            cell=pair.crop(bounds)
        elif (archive,record) in [(183,13),(183,9)] and i in [1,3]:
            used_sheet=OUT/f'native-183-{record}-angle-pair-v2.png'
            pair=Image.open(used_sheet).convert('RGBA')
            side=0 if i==1 else 1
            bounds=[side*pair.width//2,0,(side+1)*pair.width//2,pair.height]
            cell=pair.crop(bounds)
        elif (archive,record)==(182,37) and i in [1,5]:
            used_sheet=OUT/'native-182-37-angle-pair-v2.png'
            pair=Image.open(used_sheet).convert('RGBA')
            side=0 if i==1 else 1
            bounds=[side*pair.width//2,0,(side+1)*pair.width//2,pair.height]
            cell=pair.crop(bounds)
        elif (archive,record)==(182,46) and i==6:
            used_sheet=OUT/'native-182-46-angle-pair-v2.png'
            pair=Image.open(used_sheet).convert('RGBA')
            bounds=[pair.width//2,0,pair.width,pair.height]
            cell=pair.crop(bounds)
        elif (archive,record)==(182,39) and i in [3,7]:
            used_sheet=OUT/'native-182-39-angle-pair-v2.png'
            pair=Image.open(used_sheet).convert('RGBA')
            side=0 if i==3 else 1
            bounds=[side*pair.width//2,0,(side+1)*pair.width//2,pair.height]
            cell=pair.crop(bounds)
        else:cell=sheet.crop(bounds)
        bbox=cell.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
        assert bbox
        target=folder/f'view-{i}-{name}.png'
        if i==native_view:
            target.write_bytes(source.read_bytes())
            frame=native.copy()
        else:
            cut=cell.crop(bbox)
            frame=cut.resize((round(cut.width*export_height/cut.height),export_height),Image.Resampling.NEAREST)
            frame.save(target)
        frames.append(frame)
        rows.append({'view':i,'bearing':name,'path':str(target),'size':list(frame.size),'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'usedSheet':str(used_sheet),'sheetCell':bounds,'alpha128Bounds':bbox})
        rows[-1]['usedSheetSha256']=hashlib.sha256(used_sheet.read_bytes()).hexdigest()
    cw,ch=max(f.width for f in frames),export_height
    atlas=Image.new('RGBA',(cw*4,ch*2))
    review_width=max(160,2*max(f.width for f in [native]+frames)+16)
    preview=Image.new('RGB',(review_width*9,max(370,2*max(f.height for f in [native]+frames)+50)),'#555960')
    d=ImageDraw.Draw(preview)
    for i,frame in enumerate([native]+frames):
        p=frame.copy()
        p.putalpha(p.getchannel('A').point(lambda a:255 if a>=128 else 0))
        p=p.resize((p.width*2,p.height*2),Image.Resampling.NEAREST)
        preview.paste(p,(i*review_width+(review_width-p.width)//2,35),p)
        d.text((i*review_width+6,10),'SOURCE' if i==0 else names[i-1],fill='white')
        if i:
            atlas.paste(frame,((i-1)%4*cw+(cw-frame.width)//2,(i-1)//4*ch))
    atlas.save(folder/'atlas.png')
    preview.save(OUT/f'native-{archive}-{record}-idle-native-review.png')
    if native_view is not None:assert source.read_bytes()==Path(rows[native_view]['path']).read_bytes()
    result={'archive':archive,'record':record,'provider':'vanilla','aliases':aliases,'source':str(source),'sourcePixelSha256':pixelhash,'sourcePngSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'rawAtlas':str(sheetpath),'rawAtlasSha256':hashlib.sha256(sheetpath.read_bytes()).hexdigest(),'nativeSourceDimensions':list(native.size),'atlasCellSize':[cw,ch],'frames':rows,'generated':True,'technicalChecked':True,'visualChecked':False,'integrated':False,'userApproved':False,'limits':['Generated views are visual reconstructions, not pixel-identical native source.','All eight bearings and right-hand spear continuity require visual review; this manifest does not claim user acceptance.'],'processing':'Native front exact PNG bytes. Generated7 cells alpha128-bounded and proportionally NN-reduced to original total sprite height, including spear. No retouch, mirroring, repainting, palette substitution or alpha cleanup.'}
    review=REVIEWS.get(f'{archive}-{record}',{})
    result['visualChecked']=review.get('rawAtlasSha256')==result['rawAtlasSha256'] and review.get('frameSha256')==[f['sha256'] for f in rows]
    result['visualReviewEvidence']=review if result['visualChecked'] else None
    result['nativeAssignedView']=native_view
    result['candidateStatus']='reviewed-with-rendering-limits'
    result['usableAsCompleteEightViewCandidate']=True
    if (archive,record)==(183,13):
        result['candidateStatus']='needs-proportion-repair'
        result['usableAsCompleteEightViewCandidate']=False
    result['sourcePosture']='standing'
    if (archive,record)==(182,37):result['sourcePosture']='standing-playing-recorder; converted to quiet idle with lowered instrument'
    if (archive,record)==(182,38):result['sourcePosture']='seated cradling ambiguous orange-brown object; reconstructed standing'
    result['exportHeight']=export_height
    result['exportHeightBasis']='128px working standing scale derived from comparable native humans; source seated height61px is not standing stature' if (archive,record)==(182,38) else 'original total sprite height'
    result['originalRetainedAsSeparateSource']=True
    result['processing']='Original source exact PNG bytes assigned to verified native bearing (0 front, 1 front-left or 7 front-right). Other seven cells alpha128-bounded and aspect-preserving NN-reduced to original total height. No retouch, mirroring, repainting or alpha cleanup.'
    if native_view is None:result['processing']='Original non-idle source retained separately with PNG and pixel hashes. All eight idle cells generated, alpha128-bounded and aspect-preserving NN-reduced to explicit exportHeight; no original frame reused. No retouch, mirroring, repainting or alpha cleanup.'
    result['visualReview']='Source paired beside eight native-height frames; checked review requires exact atlas and output hashes in native-idle-visual-review-records.json. Candidate only.'
    result['limits']=['Generated views reconstruct unseen anatomy; palette and proportions show smoother, broader rendering than native front. Not integrated or user approved.']
    if (archive,record)==(183,2):result['limits'].append('Left-hand spear checked. Beard detail weaker and chainmail/plate treatment nonuniform across views; armor consistency remains a candidate limitation.')
    if (archive,record)==(183,3):result['visualReview']+=' Right-hand spear continuity checked.'
    if (archive,record)==(182,35):result['limits'].append('Small source gold ear hoop is omitted in generated views; face is smoother and broader. Identity-detail repair still needed.')
    if (archive,record)==(182,23):result['limits'].append('Corrected isolated view1 is coarser and narrower than the other generated views; style consistency remains a candidate limitation.')
    if (archive,record)==(182,26):result['limits'].append('Corrected right-front view7 preserves right-hand pail but has a fuller body and more detailed face than other views; source original at front-left1 retained.')
    if (archive,record)==(182,17):result['limits'].append('Corrected views1/5 preserve left-hand purse and far-side occlusion; their coarse texture and proportions differ mildly from initial atlas views.')
    if (archive,record) in [(182,22),(182,40)]:result['limits'].append('Isolated corrected angle uses coarser pixel blocks than remaining atlas; texture consistency remains a candidate limitation.')
    if (archive,record)==(182,39):result['limits'].append('Corrected views3/7 preserve right-hand broom but are slightly broader/coarser than initial atlas views.')
    if (archive,record)==(183,4):result['limits'].append('Corrected front-right7 is coarser with weaker torso-angle distinction than strict 45 degrees; chainmail and beard detail reconstructed, not original pixels.')
    if (archive,record)==(183,0):result['limits'].append('Corrected front-right7 restores anatomical right arm down and left hand at hip; its coarser texture and fuller figure differ from other generated angles.')
    if (archive,record)==(182,46):result['limits'].append('Corrected views1/6 restore leftward body bearing and right-hand rapier in profile; their texture is coarser and blade length in profile6 shorter than source.')
    if (archive,record)==(182,37):result['limits'].append('Active playing pose replaced by quiet lowered-recorder idle. Corrected views1/5 have coarser texture and different feather/recorder details; source posture and original asset remain separate.')
    if (archive,record)==(182,38):result['limits'].append('Standing anatomy and128px height reconstructed from seated56x61 source. Orange-brown cradled object remains semantically ambiguous; generated version resembles a small doll/animal and is not certified exact identity. No original standing direction exists.')
    if (archive,record)==(183,6):result['limits'].append('Corrected views1/5/6 restore leftward quarter, far-side left staff and right profile. Their crown/beard and cape proportions vary from initial atlas; front-left1 torso distinction remains weak, not certified exact45degrees. Rope-end positioning reconstructed.')
    if (archive,record)==(183,13):result['limits'].append('Original oblique view7 retained; corrected views1/3, isolated back4 and remapped profiles restore bearings. Ornate serpent/bird embroidery is reconstructed, coarser and simplified; exact emblem geometry not certified. Isolated back4 has smaller head/narrower body and longer cape proportions than other views; consistency repair remains.')
    if (archive,record)==(183,7):result['limits'].append('Generated figure is broader and coat longer than original front-left source; gold border ornament and goatee reconstructed.')
    if (archive,record)==(183,10):result['limits'].append('Corrected front-left1 is coarser and fuller than other views; right hand at chest, left hanging arm preserved. Gold border ornament simplified; original sourcefront-right7 retained.')
    if (archive,record)==(183,8):result['limits'].append('Silver hair back arrangement and ermine spots reconstructed; source red ring is tiny and not consistently legible in generated angles. Original front-right7 preserved.')
    if (archive,record)==(183,11):result['limits'].append('Corrected1/2/6 retain pale cream right-hand fan-like object, whose exact kind is ambiguous in source. Corrected angles have different head/body proportions and simpler gold ornament; originalfront-right7 preserved.')
    if (archive,record)==(183,9):result['limits'].append('Corrected1/3 preserve leftward body and right-hand pale object with far-side occlusion. Object kind ambiguous; corrected figures are fuller and hood/drapery simplified. Originalfront-right7 preserved.')
    if (archive,record)==(183,12):result['limits'].append('Left-hand book continuity checked; pageedge shape and brown cloth texture reconstructed. Generated face narrower than original and robe folds smoother.')
    if (archive,record)==(183,14):result['limits'].append('Source right arm occluded; behind-torso pose inferred. Corrected0/1/5 restore genuinefront/leftquarter and left-side slit/sleeve. Corrected views have fuller anatomy and neckline/hem differences; originalfront-right7 retained.')
    if (archive,record)==(183,16):result['limits'].append('Short stocky anatomy retained; correctedfront-left1 is coarser and slightly taller in body proportions. Source cloak-train and epaulet details reconstructed, originalfront-right7 retained.')
    if (archive,record)==(183,15):result['limits'].append('Hunched posture and left-hand staff retained, correctedrear-right5 coarser with thicker hair and changed robe folds. Originalfront-right7 preserved; exact unseen anatomy reconstructed.')
    if (archive,record)==(183,17):result['limits'].append('Correctedrear-left3 coarser with slimmer robe/body than atlas views; generated alternate heads exaggerate long nose, chin and bald patch relative to nativefront. Dirtyblond hair, raggedrobe and rope retained, exact facial identity limited.')
    if (archive,record) in [(182,45),(182,20),(182,23),(182,25)]:result['limits'].append('Quarter-front views 1/7 show weak torso-angle distinction from front; directional candidates, not certified exact 45-degree geometry.')
    (folder/'manifest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    results.append(result)
(OUT/'native-idle-candidate-coverage.json').write_text(json.dumps({'sets':results,'setCount':len(results),'identityCount':sum(len(r['aliases']) for r in results),'viewCount':sum(len(r['frames']) for r in results)},indent=2),encoding='utf-8')
print(json.dumps({'sets':len(results),'identities':sum(len(r['aliases']) for r in results),'views':sum(len(r['frames']) for r in results)}))
