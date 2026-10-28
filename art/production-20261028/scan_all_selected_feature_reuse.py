"""Find exact guarded feature matches without restricting discovery to fixed ROIs.

This read-only audit compares real selected animation change unions to every
native and enabled-mod original. It neither copies art nor registers coverage.
"""
import hashlib
import json
from pathlib import Path

from PIL import Image

from check_portrait_reuse_masks import CATALOG, HERE, digest, feature_frames


def main(extra_numbers=(), output_name="all-selected-feature-compatibility.json"):
    catalog = json.loads(CATALOG.read_text())
    originals = []
    selected = {}
    for entry in catalog["entries"]:
        path = (CATALOG.parent / entry["original"]).resolve()
        picture = Image.open(path).convert("RGBA")
        assert picture.size == (64,64)
        originals.append({"id":entry["id"], "path":str(path), "sha256":digest(path), "pixels":list(picture.getdata())})
        if entry.get("animationManifest"):
            selected[entry["id"]] = (CATALOG.parent / entry["animationManifest"]).resolve()
    for number in (195,196,*extra_numbers):
        selected[f"common-{number:03}"] = HERE / f"portraits/npc{number:03}-complete-candidate/manifest.json"
    for entry in json.loads((HERE / "portraits/mod-portrait-source-reconciliation.json").read_text())["entries"]:
        path = Path(entry["original"])
        picture = Image.open(path).convert("RGBA")
        assert picture.size == (64,64) and digest(path) == entry["fileSha256"]
        originals.append({"id":"mod:"+entry["id"], "path":str(path), "sha256":digest(path), "pixels":list(picture.getdata())})
    by_id = {item["id"]:item for item in originals}
    features = []
    for identity, manifest_path in selected.items():
        manifest = json.loads(manifest_path.read_text())
        source = by_id[identity]
        for feature, frames in feature_frames(manifest).items():
            offset,height = (0,35) if feature == "eyes" else (35,29)
            positions = set()
            receipts = []
            original_pixels = source["pixels"][offset*64:(offset+height)*64]
            for frame in frames:
                path = (manifest_path.parent / frame["file"]).resolve()
                assert digest(path).lower() == frame["sha256"].lower()
                picture = Image.open(path).convert("RGBA")
                assert picture.size == (64,height)
                state = {key:frame[key] for key in ("mood","gaze","phase","shape") if key in frame}
                if state["mood"] == "neutral" and ((feature == "eyes" and state["gaze"] == "front" and state["phase"] == "open") or (feature == "mouths" and state["shape"] in ("A","X"))):
                    assert list(picture.getdata()) == original_pixels
                for i,(before,after) in enumerate(zip(original_pixels,picture.getdata())):
                    if before != after:
                        positions.add((i%64,i//64+offset))
                receipts.append({"state":state,"file":str(path),"sha256":digest(path)})
            assert positions
            guard = {(x+dx,y+dy) for x,y in positions for dx in (-1,0,1) for dy in (-1,0,1)
                     if 0<=x+dx<64 and offset<=y+dy<offset+height}
            indices = sorted(y*64+x for x,y in guard)
            matches = []
            for target in originals:
                if target["id"] in selected:
                    continue
                if all(source["pixels"][i] == target["pixels"][i] for i in indices):
                    matches.append({"targetId":target["id"],"targetOriginal":target["path"],"targetOriginalSha256":target["sha256"]})
            if matches:
                features.append({"sourceId":identity,"feature":feature,"sourceOriginal":source["path"],
                                 "sourceOriginalSha256":source["sha256"],"sourceManifest":str(manifest_path),
                                 "sourceManifestSha256":digest(manifest_path),"changedPixelUnion":[list(p) for p in sorted(positions)],
                                 "guardedPixelCount":len(guard),"frameReceipts":receipts,"exactGuardedTargets":matches})
    result = {"sourceOriginalCount":len(originals),"selectedDonorCount":len(selected),"matches":features,
              "method":"All actually changed pixels across all 54 feature frames, plus a one-pixel exact-original guard; no fixed-ROI prefilter.",
              "visualAcceptance":False,"newlyGeneratedPoses":0,"assemblyPerformed":False}
    (HERE / "portraits" / output_name).write_text(json.dumps(result,indent=2)+"\n")
    print(json.dumps({"sources":len(originals),"donors":len(selected),"matches":[{"source":f["sourceId"],"feature":f["feature"],"targets":[t["targetId"] for t in f["exactGuardedTargets"]]} for f in features]}))


if __name__ == "__main__":
    main()
