"""Independently verify every derived feature pixel and every retained target pixel."""
import hashlib
import json
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
FOLDER = HERE / "portraits/exact-feature-reuse"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main(folder=FOLDER):
    summary = json.loads((folder / "summary.json").read_text())
    total = 0
    for candidate in summary["candidates"]:
        path = Path(candidate["manifest"])
        manifest = json.loads(path.read_text())
        original_path = (path.parent / manifest["original"]).resolve()
        assert digest(original_path) == manifest["sourceSha256"]
        original = Image.open(original_path).convert("RGBA")
        reuse = manifest["featureReuse"]
        assert digest(Path(reuse["donorManifest"])) == reuse["donorManifestSha256"]
        assert digest(Path(reuse["maskEvidence"])) == reuse["maskEvidenceSha256"]
        evidence = json.loads(Path(reuse["maskEvidence"]).read_text())
        pair = next(pair for pair in evidence["pairs"] if pair["targetId"] == candidate["targetId"] and pair["sourceId"] == candidate["donorId"])
        donor_original = Image.open(pair["sourceOriginal"]).convert("RGBA")
        assert digest(Path(pair["sourceOriginal"])) == pair["sourceOriginalSha256"]
        checked_states = []
        for feature, frames in reuse["frames"].items():
            assert len(frames) == 54
            proof = pair["features"][feature]
            assert proof["exactActualChangeMask"] and proof["exactOnePixelGuard"]
            positions = {tuple(point) for point in reuse["changedPixelUnions"][feature]}
            assert positions == {tuple(point) for point in proof["changedPixelUnion"]}
            offset,height = (0,35) if feature == "eyes" else (35,29)
            guarded = {(x+dx,y+dy) for x,y in positions for dx in (-1,0,1) for dy in (-1,0,1)
                       if 0 <= x+dx < 64 and offset <= y+dy < offset+height}
            assert all(original.getpixel(point) == donor_original.getpixel(point) for point in guarded)
            neutral = original.crop((0,offset,64,offset+height))
            assert len({tuple(frame["state"].items()) for frame in frames}) == 54
            for frame in frames:
                output_path = path.parent / frame["file"]
                donor_path = Path(frame["sourceFile"])
                assert digest(output_path) == frame["sha256"]
                assert digest(donor_path) == frame["sourceSha256"]
                output = Image.open(output_path).convert("RGBA")
                donor = Image.open(donor_path).convert("RGBA")
                assert output.size == donor.size == (64,height)
                for y in range(height):
                    for x in range(64):
                        expected = donor.getpixel((x,y)) if (x,y+offset) in positions else original.getpixel((x,y+offset))
                        assert output.getpixel((x,y)) == expected
                state = frame["state"]
                if state["mood"] == "neutral" and ((feature == "eyes" and state["gaze"] == "front" and state["phase"] == "open") or (feature == "mouths" and state["shape"] in ("A","X"))):
                    assert output.tobytes() == neutral.tobytes()
                    checked_states.append(state)
                total += 1
        gate = {"manifestSha256":digest(path),"derivedParts":manifest["derivedPartCount"],"sourceMaskAndGuardExact":True,
                "targetOutsideMaskExact":True,"selectedDonorPixelsExact":True,"neutralOriginalExactStates":checked_states,
                "status":"technical-assembly-pass-not-visual-acceptance"}
        (path.parent / "independent-verification.json").write_text(json.dumps(gate,indent=2)+"\n")
    assert total == summary["derivedParts"]
    result = {"candidates":len(summary["candidates"]),"derivedPartsVerified":total,"newlyGeneratedPoses":0,"allPixelChecksPass":True,"goalComplete":False}
    (folder / "independent-verification.json").write_text(json.dumps(result,indent=2)+"\n")
    print(json.dumps(result))


if __name__ == "__main__":
    main()
