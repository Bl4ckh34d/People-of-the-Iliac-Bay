"""Assemble hash-bound matching feature bytes onto each target's immutable original.

This does not synthesize or repaint pixels. Every copied feature pixel comes from
an existing selected donor frame and has an exact matching original plus guard.
"""
import hashlib
import json
import os
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
OUTPUT = HERE / "portraits/exact-feature-reuse"
MOODS = ["neutral", "happy", "angry", "sad", "afraid", "guarded"]
SHAPES = list("ABCDEFGH")+["X"]
GAZES = ["front", "left", "right"]
PHASES = ["open", "half", "closed"]
SELECTION = {"common-452": ["eyes", "mouths"], "common-445": ["mouths"], "common-434": ["mouths"]}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def relative(path, root):
    return Path(os.path.relpath(path, root)).as_posix()


def frame_path(feature, state):
    if feature == "eyes":
        return Path("eyes") / state["mood"] / state["gaze"] / (state["phase"]+".png")
    return Path("mouth") / state["mood"] / (state["shape"]+".png")


def make_contact(folder, feature, frames, original):
    cell_width, cell_height = 192, 216
    sheet = Image.new("RGB", (9*cell_width,6*cell_height), (35,39,43))
    draw = ImageDraw.Draw(sheet)
    lookup = {tuple(frame["state"].values()):frame for frame in frames}
    for row, mood in enumerate(MOODS):
        columns = [(gaze,phase) for gaze in GAZES for phase in PHASES] if feature == "eyes" else [(shape,) for shape in SHAPES]
        for column, parts in enumerate(columns):
            state = {"mood": mood, "gaze": parts[0], "phase": parts[1]} if feature == "eyes" else {"mood": mood, "shape": parts[0]}
            frame = lookup[tuple(state.values())]
            composite = original.copy()
            half = Image.open(folder / frame["file"]).convert("RGBA")
            composite.paste(half, (0,0 if feature == "eyes" else 35))
            composite = composite.resize((192,192),Image.Resampling.NEAREST)
            sheet.paste(composite,(column*cell_width,row*cell_height+24),composite)
            draw.text((column*cell_width+3,row*cell_height+4)," ".join(state.values()),fill="white")
    sheet.save(folder / f"{feature}-54-contact.png")


def main(evidence_path=None, output=None, selection=None):
    evidence_path = evidence_path or HERE / "portraits/selected-family-mask-compatibility.json"
    output = output or OUTPUT
    selection = selection or SELECTION
    evidence = json.loads(evidence_path.read_text())
    output.mkdir(exist_ok=True)
    summary = []
    for pair in evidence["pairs"]:
        target_id = pair["targetId"]
        if target_id not in selection:
            continue
        features = selection[target_id]
        target_original_path = Path(pair["targetOriginal"])
        source_original_path = Path(pair["sourceOriginal"])
        source_manifest_path = Path(pair["sourceManifest"])
        assert digest(target_original_path) == pair["targetOriginalSha256"]
        assert digest(source_original_path) == pair["sourceOriginalSha256"]
        assert digest(source_manifest_path) == pair["sourceManifestSha256"]
        original = Image.open(target_original_path).convert("RGBA")
        source_original = Image.open(source_original_path).convert("RGBA")
        donor = json.loads(source_manifest_path.read_text())
        folder = output / target_id
        folder.mkdir(exist_ok=False)
        emitted = {}
        neutral_exact = []
        for feature in features:
            proof = pair["features"][feature]
            assert proof["exactActualChangeMask"] and proof["exactOnePixelGuard"]
            positions = [tuple(point) for point in proof["changedPixelUnion"]]
            selected_positions = set(positions)
            assert all(original.getpixel(point) == source_original.getpixel(point) for point in positions)
            offset, height = (0,35) if feature == "eyes" else (35,29)
            base = original.crop((0,offset,64,offset+height))
            emitted[feature] = []
            for receipt in proof["frameReceipts"]:
                donor_path = Path(receipt["file"])
                assert digest(donor_path) == receipt["sha256"]
                donor_frame = Image.open(donor_path).convert("RGBA")
                frame = base.copy()
                for x,y in positions:
                    frame.putpixel((x,y-offset),donor_frame.getpixel((x,y-offset)))
                for index, (before, after) in enumerate(zip(base.getdata(),frame.getdata())):
                    point = (index%64,index//64+offset)
                    assert point in selected_positions or before == after
                state = receipt["state"]
                is_neutral_exact = state["mood"] == "neutral" and (
                    (feature == "eyes" and state["gaze"] == "front" and state["phase"] == "open") or
                    (feature == "mouths" and state["shape"] in ("A","X")))
                if is_neutral_exact:
                    assert frame.tobytes() == base.tobytes()
                    neutral_exact.append(state)
                relative_path = frame_path(feature,state)
                path = folder / relative_path
                path.parent.mkdir(exist_ok=True,parents=True)
                frame.save(path)
                assert Image.open(path).convert("RGBA").tobytes() == frame.tobytes()
                emitted[feature].append({"state":state,"file":relative_path.as_posix(),"sha256":digest(path),
                                          "sourceFile":str(donor_path),"sourceSha256":receipt["sha256"],
                                          "outsideChangedMaskDifferenceCount":0})
            make_contact(folder,feature,emitted[feature],original)

        manifest = {"schemaVersion":2,"portraitRecord":int(target_id.split("-")[1]),"bank":"TFAC00I0.RCI",
                    "width":64,"height":64,"splitY":35,"original":relative(target_original_path,folder),
                    "sourceSha256":pair["targetOriginalSha256"],"status":"exact-feature-derived-review-candidate",
                    "completeArtParts":set(features)=={"eyes","mouths"},"derivedPartCount":sum(map(len,emitted.values())),
                    "newlyGeneratedPoses":0,"voiceRoute":"unassigned; no donor voice inherited",
                    "visualAcceptance":None,"temporalAcceptance":None,"userApproved":False,"published":False,
                    "featureReuse":{"donorId":pair["sourceId"],"donorOriginalSha256":pair["sourceOriginalSha256"],
                                    "donorManifest":str(source_manifest_path),"donorManifestSha256":pair["sourceManifestSha256"],
                                    "maskEvidence":str(evidence_path),"maskEvidenceSha256":digest(evidence_path),
                                    "changedPixelUnions":{feature:pair["features"][feature]["changedPixelUnion"] for feature in features},
                                    "frames":emitted},
                    "limitations":["Exact source-mask compatibility does not improve donor animation quality.",
                                   "Target pixels outside actual changed-feature unions remain exactly original.",
                                   "No runtime, audiovisual, user or shared-gallery acceptance is claimed."]}
        manifest["donorVisualAcceptance"] = donor.get("visualAcceptance")
        if "eyes" in features:
            eyes = {tuple(frame["state"].values()):frame for frame in emitted["eyes"]}
            manifest["eyeSets"] = [{"id":mood,"phases":{phase:{key:eyes[(mood,"front",phase)][key] for key in ("file","sha256")} for phase in PHASES}} for mood in MOODS]
            manifest["thinkingEyeSetsByEmotion"] = {mood:[{"id":gaze,"phases":{phase:{key:eyes[(mood,gaze,phase)][key] for key in ("file","sha256")} for phase in PHASES}} for gaze in ("left","right")] for mood in MOODS}
        if "mouths" in features:
            mouths = {tuple(frame["state"].values()):frame for frame in emitted["mouths"]}
            manifest["mouthSets"] = [{"id":mood,"frames":[{"shape":shape,**{key:mouths[(mood,shape)][key] for key in ("file","sha256")}} for shape in SHAPES]} for mood in MOODS]
        output_manifest = folder / "manifest.json"
        output_manifest.write_text(json.dumps(manifest,indent=2)+"\n")
        gate = {"manifestSha256AtAssembly":digest(output_manifest),"scope":"Immutable assembly-time receipt; current manifest binding is in independent-verification.json.","targetSourceHashUnchanged":digest(target_original_path)==pair["targetOriginalSha256"],
                "donorSourceHashUnchanged":digest(source_original_path)==pair["sourceOriginalSha256"],
                "derivedParts":manifest["derivedPartCount"],"outsideActualChangeMaskDifferences":0,
                "neutralSourceExactStates":neutral_exact,"structuralPass":True,"visualPass":None}
        (folder / "technical-verification.json").write_text(json.dumps(gate,indent=2)+"\n")
        summary.append({"targetId":target_id,"donorId":pair["sourceId"],"manifest":str(output_manifest),
                        "parts":manifest["derivedPartCount"],"completeArtParts":manifest["completeArtParts"],"visualReview":"pending"})
    (output / "summary.json").write_text(json.dumps({"candidates":summary,"newlyGeneratedPoses":0,"derivedParts":sum(row["parts"] for row in summary)},indent=2)+"\n")
    print(json.dumps(summary))


if __name__ == "__main__":
    main()
