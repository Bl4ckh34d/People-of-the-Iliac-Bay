"""Verify delivered file integrity separately from visual acceptance."""
import ast
import hashlib
import json
from pathlib import Path
from html.parser import HTMLParser

from PIL import Image, ImageOps

HERE = Path(__file__).resolve().parent


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    inventory = json.loads((HERE / "source-inventory.json").read_text())
    assert len({e["id"] for e in inventory["identities"]}) == len(inventory["identities"])
    assert inventory["counts"]["excludedEditorMarkers"] == 17
    assert all(digest(Path(row["path"])) == row["sha256"] for row in inventory["sourceChecks"])
    syntax_files = [path.name for path in HERE.glob("*.py")]
    for name in syntax_files:
        ast.parse((HERE / name).read_text())

    folder = HERE / "traversal/381-jump-eight-views-v1"
    jump = json.loads((folder / "manifest.json").read_text())
    assert len(jump["frames"]) == 32
    assert {(f["bearing"], f["phase"]) for f in jump["frames"]} == {(b, p) for b in range(8) for p in range(4)}
    assert digest(folder / jump["atlas"]) == jump["atlasSha256"]
    atlas = Image.open(folder / jump["atlas"]).convert("RGBA")
    width, height = jump["canvas"]
    for frame in jump["frames"]:
        path = folder / frame["file"]
        assert digest(path) == frame["sha256"]
        image = Image.open(path)
        assert image.mode == "RGBA" and image.size == (width, height)
        x, y = frame["phase"] * width, frame["bearing"] * height
        assert image.tobytes() == atlas.crop((x, y, x+width, y+height)).tobytes()

    melee_count = 0
    for archive in range(381, 394):
        melee_folder = HERE / f"combat/candidate-melee-{archive}-eight-directions"
        melee = json.loads((melee_folder / "manifest.json").read_text())
        melee_frames = [frame for direction in melee["directions"] for frame in direction["frames"]]
        assert len(melee_frames) == 32 and len(melee["directions"]) == 8
        assert digest(melee_folder / melee["atlas"]) == melee["atlasSha256"]
        for frame in melee_frames:
            path = melee_folder / frame["path"]
            assert digest(path) == frame["sha256"]
            picture = Image.open(path)
            assert picture.mode == "RGBA" and list(picture.size) == melee["cellSize"]
        melee_count += len(melee_frames)

    run_count = 0
    mirror_count = 0
    run_folders = ["mobile-locomotion/eight-view-candidate-v1"] + [f"mobile-locomotion/families/{archive}/candidate-v1" for archive in range(381,396) if archive != 385]
    for relative in run_folders:
        run_folder = HERE / relative
        run = json.loads((run_folder / "manifest-reviewed.json").read_text())
        assert len(run["frames"]) == 32
        assert {(f["bearing"], f["phase"]) for f in run["frames"]} == {(b, p) for b in range(8) for p in range(4)}
        run_atlas = Image.open(run_folder / "atlas-8x4.png").convert("RGBA")
        width, height = run["canvas"]
        for frame in run["frames"]:
            picture = Image.open(run_folder / frame["file"])
            assert picture.mode == "RGBA" and picture.size == (width, height)
            assert hashlib.sha256(picture.tobytes()).hexdigest() == frame["frame_pixel_sha256"]
            x, y = frame["phase"] * width, frame["bearing"] * height
            assert picture.tobytes() == run_atlas.crop((x, y, x+width, y+height)).tobytes()
            if frame.get("derived_mirrored"):
                mirror_count += 1
                original_bearing = {5: 3, 6: 2, 7: 1}[frame["bearing"]]
                original = next(f for f in run["frames"] if f["bearing"] == original_bearing and f["phase"] == frame["phase"])
                assert picture.tobytes() == ImageOps.mirror(Image.open(run_folder / original["file"])).tobytes()
        run_count += len(run["frames"])

    stationary_folder = HERE / "stationary-locomotion/run-182-0"
    stationary = json.loads((stationary_folder / "run-review-manifest.json").read_text())
    assert len(stationary["views"]) == 8 and stationary["candidateFrameCount"] == 32
    for view in stationary["views"]:
        strip = Image.open(stationary_folder / f"view-{view['view']}-review-native.png").convert("RGBA")
        assert strip.size == (384,96)
        for frame in view["frames"]:
            x = frame["phase"] * 96
            assert hashlib.sha256(strip.crop((x,0,x+96,96)).tobytes()).hexdigest() == frame["nativePixelSha256"]
            assert digest(stationary_folder / frame["source"]) == frame["sourceSha256"]

    swim_folder = HERE / "traversal/381-swim-eight-views-v2"
    jester_folder = HERE / "stationary-locomotion/family-182-5"
    jester = json.loads((jester_folder / "manifest.json").read_text())
    assert jester["actions"]["walk"]["candidateViews"] == list(range(8))
    for original in jester["originals"]:
        path = Path(original["path"])
        assert digest(path) == original["fileSha256"]
        assert hashlib.sha256(Image.open(path).convert("RGBA").tobytes()).hexdigest() == original["pixelSha256"]
    for view in jester["actions"]["walk"]["views"]:
        strip = Image.open(jester_folder / f"walk/view-{view['view']}-review-native.png").convert("RGBA")
        assert strip.size == (640,160)
        for frame in view["frames"]:
            x = frame["phase"]*160
            assert hashlib.sha256(strip.crop((x,0,x+160,160)).tobytes()).hexdigest() == frame["pixelSha256"]
            assert digest(jester_folder / frame["source"]) == frame["sha256"]
    swim = json.loads((swim_folder / "manifest.json").read_text())
    assert len(swim["frames"]) == 32
    assert {(f["bearing"], f["phase"]) for f in swim["frames"]} == {(b, p) for b in range(8) for p in range(4)}
    assert digest(swim_folder / "atlas.png") == swim["atlasSha256"]
    swim_atlas = Image.open(swim_folder / "atlas.png").convert("RGBA")
    width, height = swim["pixelCanvas"]
    for frame in swim["frames"]:
        path = swim_folder / frame["file"]
        assert digest(path) == frame["sha256"]
        picture = Image.open(path)
        assert picture.mode == "RGBA" and picture.size == (width, height)
        x, y = frame["phase"] * width, frame["bearing"] * height
        assert picture.tobytes() == swim_atlas.crop((x, y, x+width, y+height)).tobytes()

    eyes = json.loads((HERE / "portraits/npc195-eyes-v1/selected54-manifest.json").read_text())
    climb_folder = HERE / "traversal/381-climb-eight-views-v1"
    climb = json.loads((climb_folder / "manifest.json").read_text())
    assert len(climb["frames"]) == 32
    assert {(f["bearing"], f["phase"]) for f in climb["frames"]} == {(b,p) for b in range(8) for p in range(4)}
    assert digest(climb_folder / "atlas.png") == climb["atlasSha256"]
    climb_atlas = Image.open(climb_folder / "atlas.png").convert("RGBA")
    width,height = climb["pixelCanvas"]
    for frame in climb["frames"]:
        picture = Image.open(climb_folder / frame["file"]).convert("RGBA")
        assert picture.size == (width,height)
        assert digest(climb_folder / frame["file"]) == frame["sha256"]
        x,y = frame["phase"]*width,frame["bearing"]*height
        assert picture.tobytes() == climb_atlas.crop((x,y,x+width,y+height)).tobytes()
    assert len(eyes["frames"]) == 54
    assert len({(f["mood"], f["gaze"], f["phase"]) for f in eyes["frames"]}) == 54
    for frame in eyes["frames"]:
        path = Path(frame["file"])
        assert digest(path) == frame["sha256"]
        assert Image.open(path).size == (64, 35)

    portrait_count = 0
    for number in (195, 196, 197):
        portrait_folder = HERE / f"portraits/npc{number}-complete-candidate"
        portrait_gate = json.loads((portrait_folder / "structural-gate.json").read_text())
        assert portrait_gate["structuralPass"] and len(portrait_gate["assets"]) == 108
        assert digest(portrait_folder / "manifest.json") == portrait_gate["manifestSha256"]
        for frame in portrait_gate["assets"]:
            path = Path(frame["file"])
            assert digest(path) == frame["sha256"]
            assert list(Image.open(path).size) == frame["dimensions"]
        portrait_count += len(portrait_gate["assets"])

    from verify_exact_portrait_reuse import main as verify_reuse
    verify_reuse()
    verify_reuse(HERE / "portraits/exact-feature-reuse-wave2")
    verify_reuse(HERE / "portraits/exact-feature-reuse-wave3")

    mod_portraits = json.loads((HERE / "portraits/mod-portrait-source-reconciliation.json").read_text())
    for row in mod_portraits["entries"]:
        path = Path(row["original"])
        picture = Image.open(path).convert("RGBA")
        assert digest(path) == row["fileSha256"]
        assert picture.size == (64, 64)
        assert hashlib.sha256(picture.tobytes()).hexdigest() == row["rgbaSha256"]

    native_idle = json.loads((HERE / "idle-sleep/native-idle-candidate-coverage.json").read_text())
    idle_count = 0
    for candidate in native_idle["sets"]:
        if (candidate["archive"], candidate["record"]) not in {(183, 3), (183, 2), (182, 54), (182, 9), (182, 35), (182, 27), (182, 28), (182, 42), (182, 45), (182, 19), (182, 20), (182, 23), (182, 25), (182, 4), (182, 26), (182, 15), (182, 17), (182, 22), (182, 40), (182,39), (182,47), (182,56), (183,5), (183,4), (183,0), (183,1), (182,46), (182,37), (182,38), (183,6)}:
            continue
        assert len(candidate["frames"]) == 8
        assert digest(Path(candidate["source"])) == candidate["sourcePngSha256"]
        original = Image.open(candidate["source"]).convert("RGBA")
        assert hashlib.sha256(original.tobytes()).hexdigest() == candidate["sourcePixelSha256"]
        if (candidate["archive"], candidate["record"]) in {(182,37),(182,38)}:
            assert candidate["nativeAssignedView"] is None and candidate["originalRetainedAsSeparateSource"]
            assert candidate["exportHeight"] == 128
        else:
            assert any(digest(Path(frame["path"])) == candidate["sourcePngSha256"] for frame in candidate["frames"])
        for frame in candidate["frames"]:
            assert digest(Path(frame["path"])) == frame["sha256"]
            assert Image.open(frame["path"]).mode == "RGBA"
        idle_count += len(candidate["frames"])
    assert idle_count == 240

    ledger = json.loads((HERE / "coverage-ledger.json").read_text())
    assert {row["id"] for row in ledger["identities"]} == {row["id"] for row in inventory["identities"]}
    for row in ledger["identities"]:
        assert set(row["actions"]) == set(inventory["requiredActions"])
        for action in row["actions"].values():
            if "evidence" in action:
                assert Path(action["evidence"]).is_file()

    sleep = json.loads((HERE / "idle-sleep/sleep-candidate-coverage.json").read_text())
    sleep_files = []
    for value in sleep.values():
        if isinstance(value, list):
            sleep_files.extend(row for row in value if isinstance(row, dict) and "file" in row and "sha256" in row)
    for row in sleep_files:
        path = Path(row["file"])
        assert path.is_file() and digest(path) == row["sha256"]
        assert Image.open(path).mode == "RGBA"
    assert len(sleep_files) == sleep["counts"]["generated"]

    references = []
    class ReferenceParser(HTMLParser):
        def handle_starttag(self, tag, attributes):
            references.extend(value for key, value in attributes if key in ("src", "href"))
    ReferenceParser().feed((HERE / "review.html").read_text())
    assert all((HERE / reference).is_file() for reference in references)
    from verify_body_additions import main as verify_body_additions
    from verify_portrait_additions import main as verify_portrait_additions
    verify_body_additions()
    verify_portrait_additions()
    result = {
        "status": "technical-file-integrity-pass-not-visual-acceptance",
        "sourceIdentities": len(inventory["identities"]), "sourceHashChecks": len(inventory["sourceChecks"]),
        "rootHelpersParsed": len(syntax_files), "jumpFrames": 32,
        "jumpAtlasPixelParity": True, "meleeCandidateFrames": melee_count, "sleepCandidateFiles": len(sleep_files),
        "runCandidateFrames": run_count, "runDerivedMirrorFrames": mirror_count, "runAtlasPixelParity": True, "portrait195EyeCandidates": 54,
        "stationaryRunCandidateFrames":32,
        "jesterWalkCandidateFrames":32,"jesterSourceAliases":len(jester["aliases"]),
        "additionalBodyCandidatesSeparatelyVerified":128,
        "additionalPortraitManifestParts":648,"additionalPortraitFilesBeyondPriorReuse":324,
        "additionalPortraitAlreadyCountedDerivedParts":324,
        "portrait195Through197TotalCandidates": portrait_count, "exactReuseDerivedPortraitParts": 3078, "exactReuseNewlyGeneratedPoses": 0,
        "modPortraitSourceFiles": len(mod_portraits["entries"]), "nativeIdleCandidateSlots": idle_count,
        "swimCandidateFrames": 32, "swimAtlasPixelParity": True,
        "climbCandidateFrames":32,"climbAtlasPixelParity":True,
        "bodyCoverageLedgerIdentities": len(ledger["identities"]),
        "reviewLocalReferences": len(references), "runtimeIntegrated": False,
        "goalComplete": False,
    }
    (HERE / "checkpoint-verification.json").write_text(json.dumps(result, indent=2)+"\n")
    print(json.dumps(result))


if __name__ == "__main__":
    main()
