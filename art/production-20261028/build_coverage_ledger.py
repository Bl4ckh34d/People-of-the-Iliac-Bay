"""Join explicit evidence by source identity; never infer completion from file names."""
import json
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent


def read(relative):
    return json.loads((HERE / relative).read_text(encoding="utf-8"))


def main():
    inventory = read("source-inventory.json")
    actions = inventory["requiredActions"]
    rows = {}
    for source in inventory["identities"]:
        excluded = source.get("applicability", "").startswith("excluded")
        rows[source["id"]] = {
            "id": source["id"], "applicability": source.get("applicability"),
            "actions": {action: {"status": "excluded-editor-marker" if excluded else "not-certified-in-this-ledger"} for action in actions},
        }

    def set_action(identity, action, status, evidence, views=None, frames=None, limits=None):
        assert identity in rows and action in actions
        path = Path(evidence)
        if not path.is_absolute():
            path = HERE / path
        assert path.is_file(), str(path)
        item = {"status": status, "evidence": str(path)}
        if views is not None:
            item["views"] = views
        if frames is not None:
            item["frames"] = frames
        if limits:
            item["limitations"] = limits
        rows[identity]["actions"][action] = item

    reconciliation = read("idle-sleep/scope-reconciliation.json")
    for row in reconciliation["identities"]:
        if row["sleepEvidence"]:
            set_action(row["id"], "sleep-in-bed", row["sleepStatus"], row["sleepEvidence"]["manifest"], frames=1,
                       limits=row["sleepEvidence"].get("issues"))
        if row["idleStatus"] == "existing-eight-views-technically-verified":
            set_action(row["id"], "idle-standing", row["idleStatus"], "idle-sleep/existing-mobile-idle-verification.json", views=list(range(8)), frames=8)
        elif row["idleStatus"] == "eight-view-candidate-with-identity-limits":
            set_action(row["id"], "idle-standing", row["idleStatus"], "idle-sleep/scope-reconciliation.json", views=list(range(8)), frames=8)

    native_walk = read("mobile-locomotion/coverage.json")
    for row in native_walk["native"] + [native_walk["guard_native"]]:
        kind = "guard" if row["archive"] == 399 else "mobile"
        identity = f"vanilla:{kind}:{row['archive']}:None"
        if row.get("walking"):
            set_action(identity, "walk", "native-existing-eight-directions-with-engine-mirroring", "mobile-locomotion/coverage.json",
                       views=[view["bearing"] for view in row["walking"]], frames=sum(view["frames"] for view in row["walking"]),
                       limits=["32 rendered slots use 20 authored native frames and engine mirroring."])

    source_by_id = {row["id"]: row for row in inventory["identities"]}
    native_idle = read("idle-sleep/native-idle-candidate-coverage.json")
    parent_reviewed = {(183, 3), (183, 2), (182, 54), (182, 9), (182, 35), (182, 27), (182, 28), (182, 42), (182, 45), (182, 19), (182, 20), (182, 23), (182, 25), (182, 4), (182, 26), (182, 15), (182, 17), (182, 22), (182, 40), (182,39), (182,47), (182,56), (183,5), (183,4), (183,0), (183,1), (182,46), (182,37), (182,38), (183,6)}
    for candidate in native_idle["sets"]:
        if (candidate["archive"], candidate["record"]) == (183,13):
            for identity in candidate["aliases"]:
                set_action(identity, "idle-standing", "needs-proportion-repair", "idle-sleep/native-idle-candidate-coverage.json", list(range(8)), 8,
                           ["Root found smaller head, narrower torso and longer cape in back view. Not a complete eight-view candidate."])
        if (candidate["archive"], candidate["record"]) not in parent_reviewed:
            continue
        assert candidate["visualChecked"] and len(candidate["frames"]) == 8
        for identity in candidate["aliases"]:
            source = source_by_id[identity]
            assert source["sourcePixelHash"] == candidate["sourcePixelSha256"]
            assert source["sourceDimensions"] == candidate["nativeSourceDimensions"]
            set_action(identity, "idle-standing", "generated-static-reviewed-candidate-with-limits", "idle-sleep/native-idle-candidate-coverage.json",
                       list(range(8)), 8, candidate["limits"])

    candidates = [
        ("vanilla:mobile:385:None", "run", "mobile-locomotion/eight-view-candidate-v1/manifest-reviewed.json", 8, 32,
         ["Palette, folds and facial details drift. Static parent inspection only."]),
        ("vanilla:mobile:381:None", "run", "mobile-locomotion/families/381/candidate-v1/manifest-reviewed.json", 8, 32,
         ["20 generated poses plus 12 explicitly native-rule-mirrored frames. Rear-left flight3 bulk drift."]),
        ("vanilla:stationary:182:0", "walk", "stationary-locomotion/walk-182-0/walk-review-manifest.json", 8, 32,
         ["Quarter-view heads are profile-like; trouser colour too saturated."]),
        ("vanilla:stationary:182:0", "run", "stationary-locomotion/run-182-0/run-review-manifest.json", 8, 32,
         ["Rear-left camera repaired in selected v4. Body-height, pouch and palette variation remain; no temporal acceptance."]),
        ("vanilla:mobile:381:None", "jump", "traversal/381-jump-eight-views-v1/manifest.json", 8, 32,
         ["Identity proportions and pouch position vary between views."]),
        ("vanilla:mobile:381:None", "melee", "combat/candidate-melee-381-eight-directions/manifest.json", 8, 32,
         ["Rear-left strike/recovery weapon-arm occlusion unresolved."]),
        ("vanilla:mobile:382:None", "melee", "combat/candidate-melee-382-eight-directions/manifest.json", 8, 32,
         ["Rear-left strike/recovery grip occluded; parent static inspection limited."]),
        ("vanilla:mobile:383:None", "melee", "combat/candidate-melee-383-eight-directions/manifest.json", 8, 32,
         ["Rear-left grip occluded. Rear/profile pouches corrected against actual native walk artwork."]),
        ("vanilla:mobile:385:None", "melee", "combat/candidate-melee-385-eight-directions/manifest.json", 8, 32,
         ["Rear-left strike/recovery weapon-arm occlusion unresolved."]),
    ]
    for archive in (382, 383, 384, 386, 387, 388, 389, 390, 391, 392, 393, 394, 395):
        candidates.append((f"vanilla:mobile:{archive}:None", "run", f"mobile-locomotion/families/{archive}/candidate-v1/manifest-reviewed.json", 8, 32,
                           ["20 generated poses plus 12 native-rule mirrors. Palette/detail drift and similar paired leg silhouettes remain static inspection limits."]))
    for archive in (384, 386, 387, 388, 389, 390, 391, 392, 393):
        candidates.append((f"vanilla:mobile:{archive}:None", "melee", f"combat/candidate-melee-{archive}-eight-directions/manifest.json", 8, 32,
                           ["Rear-left grip occluded. Identity details remain candidate limitations; source pouches checked against native walk."]))
    for identity, action, evidence, views, frames, limits in candidates:
        set_action(identity, action, "generated-static-reviewed-candidate-with-limits", evidence, list(range(views)), frames, limits)
    jester = read("stationary-locomotion/family-182-5/manifest.json")
    assert jester["actions"]["walk"]["candidateViews"] == list(range(8))
    for identity in jester["aliases"]:
        assert source_by_id[identity]["sourcePixelHash"] == jester["originals"][0]["pixelSha256"]
        set_action(identity, "walk", "generated-static-reviewed-candidate-with-limits", "stationary-locomotion/family-182-5/manifest.json", list(range(8)), 32,
                   ["Asymmetric red/gold clothing and right-hand marotte retained; face, prop size and palette vary. Three exact source aliases share one generated set."])
        assert jester["actions"]["run"]["candidateViews"] == list(range(8))
        set_action(identity, "run", "generated-static-reviewed-candidate-with-limits", "stationary-locomotion/family-182-5/manifest.json", list(range(8)), 32,
                   ["Parent inspected all32 run frames. Asymmetric costume and right-hand prop retained; face/prop/palette variation and temporal acceptance remain open."])
    for action in ("jump", "climb"):
        set_action("vanilla:mobile:382:None", action, "generated-static-reviewed-candidate-with-limits", f"traversal/families/382/{action}-eight-views-v1/manifest.json", list(range(8)), 32,
                   ["Parent inspected all32 static frames. Jump front size varies across views. Climb explicitly fixes anatomical right-hand reach; occlusion and wall contact remain limits. No temporal acceptance."])
    set_action("vanilla:mobile:382:None", "swim", "generated-static-reviewed-candidate-with-limits", "traversal/families/382/swim-eight-views-v1/manifest.json", list(range(8)), 32,
               ["All32 authored frames root-inspected. Full set replaces four-frame profile pilot; do not count both. Quarter-camera and proportion differences remain; no water or temporal acceptance."])
    set_action("vanilla:mobile:381:None", "swim", "generated-static-reviewed-candidate-with-limits", "traversal/381-swim-eight-views-v2/manifest.json", list(range(8)), 32,
               ["Front/rear camera repaired; quarter-camera, head and pouch variation remain. No temporal or runtime acceptance."])
    set_action("vanilla:mobile:381:None", "climb", "generated-static-reviewed-candidate-with-limits", "traversal/381-climb-eight-views-v1/manifest.json", list(range(8)), 32,
               ["Reach/pull/tuck/push movement. Front-right camera and front push hand phase repaired. Occluded far limbs, quarter camera variation and exact wall registration remain unaccepted."])
    set_action("vanilla:mobile:385:None", "bow", "incomplete-tool-blocked-nock-frame", "PROGRESS.md", [6], 2,
               ["Draw/release pair only. Nock generation blocked; do not bypass."])

    counts = {action: dict(Counter(row["actions"][action]["status"] for row in rows.values())) for action in actions}
    result = {
        "schemaVersion": 1, "date": "2026-10-28", "scope": "body source identities; portraits have a separate catalog",
        "goalComplete": False, "newRuntimeIntegration": False,
        "limitations": ["Snapshot of explicitly reconciled evidence, not a live file-discovery service.",
                       "Uncertified does not prove missing. Special anatomies require separate action design.",
                       "Candidate counts do not establish user acceptance, temporal quality or runtime integration.",
                       "Provider aliases remain distinct identities. Source equivalence alone does not assert generated coverage."],
        "countsByActionAndStatus": counts, "identities": list(rows.values()),
        "portraitEvidence": "portraits/coverage.json",
        "newPortraitCandidateEvidence": ["portraits/npc195-complete-candidate/manifest.json", "portraits/npc196-complete-candidate/manifest.json", "portraits/npc197-complete-candidate/manifest.json", "portraits/npc012-complete-candidate/manifest.json", "portraits/npc046-complete-candidate/manifest.json", "portraits/npc047-complete-candidate/manifest.json", "portraits/npc220-complete-candidate/manifest.json", "portraits/npc425-complete-candidate/manifest.json", "portraits/npc236-complete-candidate/manifest.json"],
        "supplementalParentReview": ["parent-review-additions.json", "parent-review-expansion.json"],
        "supplementalVerification": ["body-additions-verification.json", "portrait-additions-verification.json"],
        "derivedPortraitFeatureEvidence": ["portraits/exact-feature-reuse/summary.json", "portraits/exact-feature-reuse-wave2/summary.json", "portraits/exact-feature-reuse-wave3/summary.json"],
        "modPortraitSourceEvidence": "portraits/mod-portrait-source-reconciliation.json",
    }
    (HERE / "coverage-ledger.json").write_text(json.dumps(result, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"identities": len(rows), "countsByActionAndStatus": counts}))


if __name__ == "__main__":
    main()
