"""Reconcile current source identities without implying animation acceptance."""
import hashlib
import json
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
ROOT = REPO.parent


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def flat_blocks(path):
    """Match FlatsFile's blank-line blocks and first-definition precedence."""
    rows, block = {}, []
    for line in path.read_text(encoding="utf-8-sig").splitlines() + [""]:
        if line:
            block.append(line.strip())
            continue
        if not block:
            continue
        archive, record = [int(value) for value in block[0].split(" ")]
        rows.setdefault((archive, record), {
            "archive": archive, "record": record,
            "caption": block[1], "genderCode": block[2],
            "portraitFaceIndex": int(block[5]),
        })
        block = []
    return rows


def main():
    catalog_path = REPO / "tools/BreezeTtsTrial/stationary_voice_origin_sex_catalog.json"
    mods_path = ROOT / "Player/PortableAppdata/Mods/GameData/Mods.json"
    flats_path = ROOT / "GameFiles/arena2/FLATS.CFG"
    catalog = read_json(catalog_path)
    mods = read_json(mods_path)
    flats = flat_blocks(flats_path)
    bundles = ROOT / "Player/DaggerfallUnity_Data/StreamingAssets/Mods"
    checked_sources = {
        "nativeFlatsCfg": flats_path,
        "activeModsConfig": mods_path,
        "elvesAndBeastfolkResourcesBundle": bundles / "elves & beastfolk - resources.dfmod",
        "elvesAndBeastfolkReplacesBundle": bundles / "elves & beastfolk - replaces.dfmod",
        "activeFlatReplacementConfig": ROOT / "Player/DaggerfallUnity_Data/StreamingAssets/FlatReplacements/LivelyCitiesFlatReplacements.json",
    }
    evidence = []
    for key, path in checked_sources.items():
        actual = sha256(path)
        prior = catalog["sources"]["hashes"].get(key)
        evidence.append({"source": key, "path": str(path), "sha256": actual,
                         "matchesHistoricalEvidence": actual == prior})

    identities = {}

    def add(provider, archive, record, kind, source, **extra):
        key = f"{provider}:{kind}:{archive}:{record}"
        entry = identities.setdefault(key, {
            "id": key, "provider": provider, "kind": kind,
            "archive": archive, "record": record,
            "sourceEvidence": [], "coverageStatus": "reconciliation-pending",
        })
        if source not in entry["sourceEvidence"]:
            entry["sourceEvidence"].append(source)
        entry.update(extra)
        return entry

    for row in catalog["nativeStationaryFlats"]:
        add("vanilla", row["archive"], row["record"], "stationary",
            "historical-visual-catalog", inVisualCatalog=True,
            applicability="requires-current-humanoid-review")
    for row in flats.values():
        add("vanilla", row["archive"], row["record"], "stationary",
            "current-FLATS.CFG", inCurrentFlatsCfg=True,
            caption=row["caption"], portraitFaceIndex=row["portraitFaceIndex"],
            applicability="requires-current-humanoid-review")

    for archive in list(range(381, 399)) + list(range(451, 457)):
        add("vanilla", archive, None, "mobile", "MobilePersonNPC.cs",
            applicability="civilian", nativeWalk="five-native-views-engine-mirrors-eight-directions",
            nativeIdle="frontal-record-5")
    add("vanilla", 399, None, "guard", "native-guard-separate-from-civilian-family",
        applicability="guard", nativeCoverage="verify-per-action")

    for row in catalog["modNpcResources"]:
        add("elves & beastfolk - resources", row["archive"], row["record"],
            "stationary", "hash-checked-E&B-resource-catalog",
            sourceContainerPath=row["bundleContainerPath"],
            sourcePixelHash=row["pixelSha256"],
            applicability="requires-humanoid-review-contains-Alfiq-cats")

    sleep = read_json(REPO / "art/sleep/missing-coverage.json")
    for batch in sleep["batches"]:
        for row in batch["items"]:
            entry = add(row["provider"], row["archive"], row["record"], "stationary",
                        "existing-sleep-generation-provenance",
                        sourcePixelHash=row["sourcePixelHash"])
            source_path = Path(row["sourceReference"])
            entry["historicalExtractedSource"] = str(source_path)
            entry["historicalExtractedSourceExists"] = source_path.is_file()
            entry["existingSleeper"] = {
                "sheet": batch["file"], "cell": row["cell"],
                "status": "existing-mapping-not-new-visual-acceptance",
            }

    additional_path = HERE / "additional-mod-sources.json"
    if additional_path.exists():
        for row in read_json(additional_path)["sources"]:
            special = (row["archive"], row["record"]) in {(10014, 1), (10020, 1)}
            classification = "special-anatomy-separate-action-design" if special else "humanoid-source-visually-reviewed"
            if (row["archive"], row["record"]) in {(1200, 20), (1200, 24)}:
                classification = "source-statue-or-partial-body-needs-individual-review"
            add(row["provider"], row["archive"], row["record"], "stationary",
                "current-enabled-bundle-source-extraction",
                sourceContainerPath=row["container"], sourcePixelHash=row["pixelSha256"],
                sourceDimensions=row["dimensions"],
                availableOriginalSource=row["source"], applicability=classification,
                sourceContact=row["inspectionContact"],
                sourceVisualReviewDate="2026-10-28")

    remaining_path = HERE / "remaining-mod-source-scan.json"
    if remaining_path.exists():
        for row in read_json(remaining_path)["sources"]:
            provider = row["provider"]
            is_npc = provider in {"elves & beastfolk - resources", "elves & beastfolk - replaces"} or (provider == "lively cities" and row["archive"] == 1200)
            if not is_npc:
                continue
            add(provider, row["archive"], row["record"], "stationary",
                "current-enabled-bundle-source-extraction",
                sourceContainerPath=row["container"], sourcePixelHash=row["pixelSha256"],
                sourceDimensions=row["dimensions"], availableOriginalSource=row["source"],
                applicability="special-anatomy-cat-bodied-Alfiq" if row["archive"] == 1306 else "humanoid-source-visually-reviewed",
                sourceVisualReviewDate="2026-10-28")

    native_sources_path = HERE / "native-sources.json"
    if native_sources_path.exists():
        for row in read_json(native_sources_path)["sources"]:
            classification = "humanoid-source-visually-reviewed"
            if row["archive"] == 199:
                classification = "excluded-editor-marker-not-npc"
            elif row["archive"] == 175:
                classification = "authored-special-character-individual-anatomy-review"
            elif (row["archive"], row["record"]) == (184, 34):
                classification = "mounted-rider-needs-dismounted-body-source"
            add("vanilla", row["archive"], row["record"], "stationary",
                "current-native-archive-decoding-reference-parity-checked",
                sourcePixelHash=row["pixelSha256"], sourceDimensions=row["dimensions"],
                availableOriginalSource=row["source"], nativeSourceFrameCount=row["recordFrameCount"],
                applicability=classification, sourceVisualReviewDate="2026-10-28")

    det_mobile_path = HERE / "mobile-locomotion/det-mobile-orc-coverage.json"
    det_mobile = read_json(det_mobile_path) if det_mobile_path.exists() else None
    if det_mobile:
        for family in det_mobile["filename_families"]:
            add("daggerfall expanded textures", family["filename_archive"], None, "mobile",
                "current-bundle-filename-family-inventory",
                applicability="humanoid-mobile-resource-runtime-selection-unverified",
                nativeWalk="five-records-four-frames-existing-resource-conditional-mirroring",
                sourceBundleSha256=det_mobile["sha256"],
                runtimeArchiveMapping="unverified-do-not-use-folder-label-as-six-identities")

    source_entries = sorted(identities.values(), key=lambda entry: entry["id"])
    hash_groups = defaultdict(list)
    for entry in source_entries:
        if entry.get("sourcePixelHash") and entry.get("sourceDimensions"):
            key = (entry["sourcePixelHash"], tuple(entry["sourceDimensions"]))
            hash_groups[key].append(entry["id"])
    equivalence = [{"pixelSha256": key[0], "dimensions": list(key[1]), "identities": ids}
                   for key, ids in hash_groups.items() if len(ids) > 1]
    visual_keys = {(r["archive"], r["record"]) for r in catalog["nativeStationaryFlats"]}
    result = {
        "schemaVersion": 1, "date": "2026-10-28", "timezone": "Asia/Taipei",
        "status": "known-source-union-incomplete-mod-classification",
        "sourceChecks": evidence,
        "counts": {
            "currentFlatRecords": len(flats), "historicalVisualStationaryRecords": len(visual_keys),
            "stationaryUnion": len(set(flats) | visual_keys),
            "civilianMobileFamilies": 24, "guardFamilies": 1,
            "knownProviderQualifiedIdentities": len(source_entries),
            "enabledMods": sum(bool(row["Enabled"]) for row in mods),
            "additionalExtractedModReferences": len(read_json(additional_path)["sources"]) if additional_path.exists() else 0,
            "modMobileResourceFamilies": len(det_mobile["filename_families"]) if det_mobile else 0,
            "excludedEditorMarkers": sum(e.get("applicability") == "excluded-editor-marker-not-npc" for e in source_entries),
            "exactSourceEquivalenceGroups": len(equivalence),
        },
        "enabledMods": [row for row in mods if row["Enabled"]],
        "requiredActions": ["walk", "run", "idle-standing", "melee", "bow", "climb", "jump", "swim", "sleep-in-bed"],
        "requiredViews": ["front", "front-right", "right", "back-right", "back", "back-left", "left", "front-left"],
        "limitations": [
            "Known-source union is not a final inventory of every mod NPC.",
            "FLATS.CFG contains special figures and markers; a config record alone is not humanoid applicability.",
            "Historical source PNG paths may have been removed; recover from pinned native archives/bundles.",
            "DET billboards and other non-textures paths require container classification.",
            "Resource availability does not establish that a live NPC selects the resource.",
            "Existing sleeper mapping is not a fresh visual or gameplay acceptance.",
        ],
        "identities": source_entries,
        "exactSourceEquivalenceGroups": equivalence,
    }
    output = HERE / "source-inventory.json"
    output.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(output), "counts": result["counts"],
                      "historicalHashChecksPassed": all(r["matchesHistoricalEvidence"] for r in evidence)}))


if __name__ == "__main__":
    main()
