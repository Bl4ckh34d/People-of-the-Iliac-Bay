"""Assemble additional exact-compatible features, preserving each target original."""
import json
from pathlib import Path

from PIL import Image, ImageDraw

from assemble_exact_portrait_features import HERE, digest, main as assemble


def main():
    scan_path = HERE / "portraits/all-selected-feature-compatibility.json"
    scan = json.loads(scan_path.read_text())
    already = {row["targetId"] for row in json.loads((HERE / "portraits/exact-feature-reuse/summary.json").read_text())["candidates"]}
    pairs = {}
    selections = {}
    for match in scan["matches"]:
        for target in match["exactGuardedTargets"]:
            identity = target["targetId"]
            if identity in already:
                continue
            feature = match["feature"]
            if identity in selections:
                # Existing selected donor remains explicit; never silently mix donors.
                continue
            source_path, target_path = Path(match["sourceOriginal"]), Path(target["targetOriginal"])
            assert digest(source_path) == match["sourceOriginalSha256"]
            assert digest(target_path) == target["targetOriginalSha256"]
            source, original = Image.open(source_path).convert("RGBA"), Image.open(target_path).convert("RGBA")
            offset,height = (0,35) if feature == "eyes" else (35,29)
            positions = [tuple(p) for p in match["changedPixelUnion"]]
            guarded = {(x+dx,y+dy) for x,y in positions for dx in (-1,0,1) for dy in (-1,0,1)
                       if 0<=x+dx<64 and offset<=y+dy<offset+height}
            assert all(source.getpixel(p) == original.getpixel(p) for p in guarded)
            proof = {"frames":54,"changedPixelUnion":match["changedPixelUnion"],"comparedPixels":len(positions),
                     "guardedPixels":len(guarded),"exactActualChangeMask":True,"exactOnePixelGuard":True,
                     "frameReceipts":match["frameReceipts"]}
            pairs[identity] = {key:match[key] for key in ("sourceId","sourceOriginal","sourceOriginalSha256","sourceManifest","sourceManifestSha256")}
            pairs[identity].update(target)
            pairs[identity]["features"] = {feature:proof}
            pairs[identity]["visualReuseAccepted"] = False
            selections[identity] = [feature]
    evidence = {"scope":"Selected exact guarded features from all-source comparison; no fixed discovery ROI.",
                "scanEvidence":str(scan_path),"scanEvidenceSha256":digest(scan_path),"pairs":list(pairs.values()),
                "selectionRule":"First exact donor in existing catalog order; all outputs remain partial candidates with donor quality limits."}
    evidence_path = HERE / "portraits/wave2-selected-mask-compatibility.json"
    evidence_path.write_text(json.dumps(evidence,indent=2)+"\n")
    output = HERE / "portraits/exact-feature-reuse-wave2"
    assemble(evidence_path,output,selections)
    entries = list(pairs.values())
    for start in range(0,len(entries),12):
        page = Image.new("RGB",(1024,6*160),(35,39,43))
        draw = ImageDraw.Draw(page)
        for index,pair in enumerate(entries[start:start+12]):
            x,y = (index%2)*512,(index//2)*160
            for offset,key in ((0,"sourceOriginal"),(160,"targetOriginal")):
                picture = Image.open(pair[key]).convert("RGBA").resize((128,128),Image.Resampling.NEAREST)
                page.paste(picture,(x+offset,y+26),picture)
            draw.text((x+3,y+4),pair["sourceId"]+" / "+pair["targetId"]+" / "+"+".join(pair["features"]),fill="white")
        page.save(output / f"source-pair-contact-{start//12}.png")


if __name__ == "__main__":
    main()
