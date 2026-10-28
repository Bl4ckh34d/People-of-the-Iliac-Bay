"""Read-only diagnosis of hood occlusion versus genuinely different eye pixels."""
import json
from pathlib import Path

from PIL import Image,ImageDraw

from check_portrait_reuse_masks import CATALOG,HERE,digest,feature_frames


def main():
    entries = {item["id"]:item for item in json.loads(CATALOG.read_text())["entries"]}
    pairs = [("common-072","common-046"),("common-073","common-066"),("common-075","common-012")]
    output = HERE / "portraits/hood-occlusion-audit"
    output.mkdir(exist_ok=True)
    page = Image.new("RGB",(1600,len(pairs)*332),(35,39,43))
    draw = ImageDraw.Draw(page)
    results=[]
    for row,(donor_id,target_id) in enumerate(pairs):
        donor_path=(CATALOG.parent/entries[donor_id]["original"]).resolve()
        target_path=(CATALOG.parent/entries[target_id]["original"]).resolve()
        manifest_path=(CATALOG.parent/entries[donor_id]["animationManifest"]).resolve()
        donor,target=Image.open(donor_path).convert("RGBA"),Image.open(target_path).convert("RGBA")
        original=donor.crop((0,0,64,35))
        positions=set()
        for frame in feature_frames(json.loads(manifest_path.read_text()))["eyes"]:
            path=manifest_path.parent/frame["file"]
            assert digest(path).lower()==frame["sha256"].lower()
            picture=Image.open(path).convert("RGBA")
            assert picture.size==(64,35)
            positions.update((i%64,i//64) for i,(a,b) in enumerate(zip(original.getdata(),picture.getdata())) if a!=b)
        guard={(x+dx,y+dy) for x,y in positions for dx in (-1,0,1) for dy in (-1,0,1) if 0<=x+dx<64 and 0<=y+dy<35}
        different=[p for p in sorted(positions) if donor.getpixel(p)!=target.getpixel(p)]
        guard_different=[p for p in sorted(guard) if donor.getpixel(p)!=target.getpixel(p)]
        diagram=Image.new("RGB",(64,35),(12,15,18))
        for p in guard: diagram.putpixel(p,(80,80,80))
        for p in positions: diagram.putpixel(p,(40,170,100))
        for p in different: diagram.putpixel(p,(230,70,70))
        for p in guard_different:
            if p not in positions: diagram.putpixel(p,(240,180,50))
        y=row*332
        for x,picture,label in ((0,original,donor_id),(530,target.crop((0,0,64,35)),target_id),(1060,diagram,"green exact / red differs / yellow guard differs")):
            draw.text((x+4,y+4),label,fill="white")
            page.paste(picture.resize((512,280),Image.Resampling.NEAREST),(x,y+28))
        draw.text((4,y+312),f"changed union {len(positions)}; original differences {len(different)}; guarded differences {len(guard_different)}",fill="white")
        results.append({"donorId":donor_id,"targetId":target_id,"donorOriginal":str(donor_path),"targetOriginal":str(target_path),
                        "donorOriginalSha256":digest(donor_path),"targetOriginalSha256":digest(target_path),
                        "changedUnion":sorted(positions),"differentChangedPixels":different,"differentGuardPixels":guard_different,
                        "status":"diagnostic-only-no-occlusion-mask-selected-no-art-copied"})
    page.save(output/"native-upper-comparison.png")
    (output/"compatibility.json").write_text(json.dumps({"pairs":results,"automaticReuseAllowed":False},indent=2)+"\n")
    print(json.dumps([{k:r[k] for k in ("donorId","targetId","status")}|{"differentPixels":len(r["differentChangedPixels"])} for r in results]))


if __name__=="__main__": main()
