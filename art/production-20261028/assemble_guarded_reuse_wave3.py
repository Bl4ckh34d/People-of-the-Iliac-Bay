"""Prepare and optionally assemble five newly exact-compatible220 mouth families."""
import json
from pathlib import Path
from PIL import Image,ImageDraw
from assemble_exact_portrait_features import HERE,digest,main as assemble

TARGETS={'common-236','common-276','common-285','common-300','common-308'}

def main(assemble_now=False):
    scanPath=HERE/'portraits/all-selected-feature-compatibility-through046.json'
    scan=json.loads(scanPath.read_text());pairs=[]
    match=next(m for m in scan['matches'] if m['sourceId']=='common-220' and m['feature']=='mouths')
    assert {t['targetId'] for t in match['exactGuardedTargets']}==TARGETS
    sourcePath=Path(match['sourceOriginal']);assert digest(sourcePath)==match['sourceOriginalSha256'];source=Image.open(sourcePath).convert('RGBA')
    positions=[tuple(p) for p in match['changedPixelUnion']]
    guarded={(x+dx,y+dy) for x,y in positions for dx in (-1,0,1) for dy in (-1,0,1) if 0<=x+dx<64 and 35<=y+dy<64}
    for target in match['exactGuardedTargets']:
        path=Path(target['targetOriginal']);assert digest(path)==target['targetOriginalSha256'];original=Image.open(path).convert('RGBA')
        assert all(source.getpixel(p)==original.getpixel(p) for p in guarded)
        pair={key:match[key] for key in ['sourceId','sourceOriginal','sourceOriginalSha256','sourceManifest','sourceManifestSha256']};pair.update(target)
        pair['features']={'mouths':{'frames':54,'changedPixelUnion':match['changedPixelUnion'],'comparedPixels':len(positions),'guardedPixels':len(guarded),'exactActualChangeMask':True,'exactOnePixelGuard':True,'frameReceipts':match['frameReceipts']}}
        pair['visualReuseAccepted']=False;pairs.append(pair)
    evidence={'scope':'Explicit five newly discovered220 mouth matches; no overwrite of prior reuse series.','scanEvidence':str(scanPath),'scanEvidenceSha256':digest(scanPath),'pairs':pairs,'selectionRule':'Only the five exact220 mouth targets. No eye transfer or voice inheritance.'}
    evidencePath=HERE/'portraits/wave3-selected-mask-compatibility.json';evidencePath.write_text(json.dumps(evidence,indent=2)+'\n')
    output=HERE/'portraits/exact-feature-reuse-wave3';output.mkdir(exist_ok=True)
    page=Image.new('RGB',(5*320,180),(35,39,43));draw=ImageDraw.Draw(page)
    for col,pair in enumerate(pairs):
        draw.text((col*320+4,4),pair['sourceId']+' / '+pair['targetId'],fill='white')
        for offset,key in [(0,'sourceOriginal'),(160,'targetOriginal')]:
            picture=Image.open(pair[key]).convert('RGBA').resize((144,144),Image.Resampling.NEAREST);page.paste(picture,(col*320+offset,28))
    page.save(output/'source-pair-contact.png')
    if assemble_now:assemble(evidencePath,output,{target:['mouths'] for target in TARGETS})
    print(json.dumps({'targets':sorted(TARGETS),'exactGuardPixels':len(guarded),'assemblyPerformed':assemble_now}))

if __name__=='__main__':main()
