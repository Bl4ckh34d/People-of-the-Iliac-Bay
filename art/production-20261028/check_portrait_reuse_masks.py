"""Compare originals over every actually changed feature pixel plus a one-pixel guard."""
import hashlib
import json
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
CATALOG = ROOT / "art/portraits/mobile-mouths/split-mask-prototype/portrait-catalog.json"
PAIRS = [(87, 433), (87, 445), (93, 452), (148, 434)]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def feature_frames(manifest):
    eyes = []
    for mood in manifest["eyeSets"]:
        eyes.extend({"mood": mood["id"], "gaze": "front", "phase": phase, **frame} for phase, frame in mood["phases"].items())
    for emotion, gazes in manifest["thinkingEyeSetsByEmotion"].items():
        for gaze in gazes:
            eyes.extend({"mood": emotion, "gaze": gaze["id"], "phase": phase, **frame} for phase, frame in gaze["phases"].items())
    mouths = [{"mood": mood["id"], **frame} for mood in manifest["mouthSets"] for frame in mood["frames"]]
    assert len(eyes) == 54 and len(mouths) == 54
    return {"eyes": eyes, "mouths": mouths}


def main():
    catalog = json.loads(CATALOG.read_text())
    by_id = {row["record"]: row for row in catalog["entries"] if row["id"].startswith("common-")}
    results = []
    for source_id, target_id in PAIRS:
        source_entry, target_entry = by_id[source_id], by_id[target_id]
        source_path = (CATALOG.parent / source_entry["original"]).resolve()
        target_path = (CATALOG.parent / target_entry["original"]).resolve()
        source = Image.open(source_path).convert("RGBA")
        target = Image.open(target_path).convert("RGBA")
        manifest_path = (CATALOG.parent / source_entry["animationManifest"]).resolve()
        manifest = json.loads(manifest_path.read_text())
        assert manifest["portraitRecord"] == source_id
        features = {}
        for feature, frames in feature_frames(manifest).items():
            offset, height = (0,35) if feature == "eyes" else (35,29)
            original = source.crop((0,offset,64,offset+height))
            positions = set()
            frame_receipts = []
            for frame in frames:
                path = manifest_path.parent / frame["file"]
                assert digest(path).lower() == frame["sha256"].lower()
                picture = Image.open(path).convert("RGBA")
                assert picture.size == (64,height)
                for index, (before, after) in enumerate(zip(original.getdata(), picture.getdata())):
                    if before != after:
                        positions.add((index % 64,index // 64+offset))
                frame_receipts.append({"state": {key: frame[key] for key in ("mood", "gaze", "phase", "shape") if key in frame},
                                       "file": str(path.resolve()), "sha256": digest(path)})
            guarded = {(x+dx,y+dy) for x,y in positions for dx in (-1,0,1) for dy in (-1,0,1)
                       if 0 <= x+dx < 64 and offset <= y+dy < offset+height}
            ordered = sorted(positions, key=lambda point:(point[1],point[0]))
            ordered_guard = sorted(guarded, key=lambda point:(point[1],point[0]))
            mismatch = [list(point) for point in ordered if source.getpixel(point) != target.getpixel(point)]
            guard_mismatch = [list(point) for point in ordered_guard if source.getpixel(point) != target.getpixel(point)]
            features[feature] = {"frames": 54, "changedPixelUnion": [list(point) for point in ordered],
                                 "comparedPixels": len(ordered), "guardedPixels": len(guarded),
                                 "differentOriginalPixels": mismatch, "differentGuardedPixels": guard_mismatch,
                                 "exactActualChangeMask": not mismatch, "exactOnePixelGuard": not guard_mismatch,
                                 "frameReceipts": frame_receipts,
                                 "sourceOriginalMaskSha256": hashlib.sha256(bytes(channel for point in ordered for channel in source.getpixel(point))).hexdigest(),
                                 "targetOriginalMaskSha256": hashlib.sha256(bytes(channel for point in ordered for channel in target.getpixel(point))).hexdigest()}
        results.append({"sourceId": source_entry["id"], "targetId": target_entry["id"],
                        "sourceOriginal": str(source_path), "sourceOriginalSha256": digest(source_path),
                        "targetOriginal": str(target_path), "targetOriginalSha256": digest(target_path),
                        "sourceManifest": str(manifest_path), "sourceManifestSha256": digest(manifest_path),
                        "features": features, "visualReuseAccepted": False})
    output = {"scope": "Exact feature compatibility from all 108 selected source frames; no outputs copied or registered.",
              "pairs": results, "limitations": ["Pixel compatibility does not establish visual, temporal or phoneme quality of source art.",
                                                  "All target pixels outside selected changed-pixel unions must remain original."]}
    (HERE / "portraits/selected-family-mask-compatibility.json").write_text(json.dumps(output,indent=2)+"\n")
    print(json.dumps([{ "source":row["sourceId"], "target":row["targetId"],
                      "features": {name:{key:value for key,value in feature.items() if key in ("comparedPixels","guardedPixels","exactActualChangeMask","exactOnePixelGuard")} for name,feature in row["features"].items()}}
                     for row in results]))


if __name__ == "__main__":
    main()
