"""Read-only exact pixel discovery across native and enabled mod portrait sources."""
import hashlib
import importlib.util
import json
from collections import defaultdict
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
CATALOG = ROOT / "art/portraits/mobile-mouths/split-mask-prototype/portrait-catalog.json"


def main():
    helper = ROOT / "tools/InventoryPortraitFamilies.py"
    spec = importlib.util.spec_from_file_location("portrait_family_discovery", helper)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    records = module.load_records(ROOT, CATALOG)
    mods = json.loads((HERE / "portraits/mod-portrait-source-reconciliation.json").read_text())
    for entry in mods["entries"]:
        path = Path(entry["original"])
        picture = Image.open(path).convert("RGBA")
        assert hashlib.sha256(path.read_bytes()).hexdigest() == entry["fileSha256"]
        regions = {}
        for name, box in module.REGIONS.items():
            crop = picture.crop(box)
            data = crop.tobytes()
            regions[name] = {"boxXYXY": list(box), "width": crop.width, "height": crop.height,
                             "pixelSha256": hashlib.sha256(data).hexdigest(), "pixels": data}
        records.append({"id": "mod:"+entry["id"], "label": entry["id"], "bank": entry["bank"],
                        "record": entry["recordFromFilename"], "sourcePath": path.relative_to(ROOT).as_posix(),
                        "originalFileSha256": entry["fileSha256"], "originalRgbaSha256": entry["rgbaSha256"],
                        "width": picture.width, "height": picture.height, "regions": regions})
    report = module.analyze(records, CATALOG.relative_to(ROOT).as_posix())
    report["visualReview"] = {"method": "No pair-specific visual reuse decision in this read-only inventory.", "canonicalAssignments": "none"}
    report["modSourceEvidence"] = "mod-portrait-source-reconciliation.json"
    report["discoveryHelperSha256"] = hashlib.sha256(helper.read_bytes()).hexdigest()
    buckets = defaultdict(list)
    for record in records:
        buckets[(record["width"], record["height"], record["originalRgbaSha256"])].append(record["id"])
    report["exactFullOriginalGroups"] = [ids for ids in buckets.values() if len(ids) > 1]
    catalog = json.loads(CATALOG.read_text())
    selected = {row["id"]: row["animationManifest"] for row in catalog["entries"] if row.get("animationManifest")}
    selected["common-195"] = str(HERE / "portraits/npc195-complete-candidate/manifest.json")
    report["selectedSourceCandidatePairs"] = [
        pair for pair in report["candidatePairwiseVerification"]
        if (pair["leftId"] in selected) != (pair["rightId"] in selected)
    ]
    report["selectedSourceManifests"] = selected
    report["summary"]["exactFullOriginalGroups"] = len(report["exactFullOriginalGroups"])
    report["summary"]["selectedToUnselectedCandidatePairs"] = len(report["selectedSourceCandidatePairs"])
    report["limitations"] = ["Fixed discovery rectangles do not establish full production-mask compatibility.",
                             "Existing selected art has its own visual limitations; exact source matches do not upgrade acceptance.",
                             "No animation files are generated, copied or registered by this script."]
    output = HERE / "portraits/face-family-reuse-discovery.json"
    output.write_text(json.dumps(report, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"sourceRecords": len(records), **report["summary"],
                      "selectedCandidateIds": [[pair["leftId"], pair["rightId"], pair["sharedExactRegions"]] for pair in report["selectedSourceCandidatePairs"]]}))


if __name__ == "__main__":
    main()
