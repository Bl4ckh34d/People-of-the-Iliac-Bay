import hashlib
import json
import unittest

from validate_appraisal_preparation import (
    APPRAISAL_V3_MANIFEST_PATH,
    APPRAISAL_FIXTURE_PATH,
    PreparationContractError,
    validate_rows,
)


def fixture_rows(version="v1"):
    fixture_path = APPRAISAL_FIXTURE_PATH.with_name(f"appraisal-live-scenarios-{version}.json")
    fixture = json.loads(fixture_path.read_text(encoding="utf-8"))
    rows = []
    for profile in fixture["profiles"]:
        for scenario in fixture["scenarios"]:
            attribution = scenario.get("responsibleActorAttribution")
            evidence = (f"responsibleActorAttribution={attribution},responsibleActor=unknown"
                        if attribution == "Unknown" else "supplied event evidence")
            prompt = f"Production Core prompt for {profile['personId']} with {evidence}."
            completion_body = json.dumps({
                "prompt": prompt,
                "grammar": "root ::= \"{}\"",
                "n_predict": 1024,
                "temperature": 0,
                "stop": ["<|im_end|>", "<|im_start|>"],
            }, separators=(",", ":"))
            rows.append({
                "recordType": "prepared-production-core-appraisal",
                "fixtureId": fixture["fixtureId"],
                "fixtureSha256": hashlib.sha256(fixture_path.read_bytes()).hexdigest(),
                "caseId": f"{profile['personId']}/{scenario['id']}",
                "humanReview": {"label": scenario["reviewLabel"]},
                "requestPrompt": prompt,
                "requestPromptSha256": hashlib.sha256(prompt.encode("utf-8")).hexdigest(),
                "completionRequestBody": completion_body,
                "completionRequestSha256": hashlib.sha256(completion_body.encode("utf-8")).hexdigest(),
                "syntheticFixture": True,
                "nativeCommitAvailable": False,
            })
    return rows


def fixture_rows_v3():
    fixture_dir = APPRAISAL_V3_MANIFEST_PATH.parent
    manifest = json.loads(APPRAISAL_V3_MANIFEST_PATH.read_text(encoding="utf-8"))
    base_path = fixture_dir / manifest["baseFixtureFile"]
    pairs_path = fixture_dir / manifest["contextPairFixtureFile"]
    base = json.loads(base_path.read_text(encoding="utf-8"))
    pairs = json.loads(pairs_path.read_text(encoding="utf-8"))
    manifest_hash = hashlib.sha256(APPRAISAL_V3_MANIFEST_PATH.read_bytes()).hexdigest()
    rows = []

    def add(case_id, label, scenario, context_text=""):
        attribution = scenario.get("responsibleActorAttribution")
        evidence = ("responsibleActorAttribution=Unknown,responsibleActor=unknown"
                    if attribution == "Unknown" else "supplied event evidence")
        prompt = f"Production Core prompt with {evidence}. {context_text}".strip()
        completion_body = json.dumps({
            "prompt": prompt,
            "grammar": "root ::= \"{}\"",
            "n_predict": 1024,
            "temperature": 0,
            "stop": ["<|im_end|>", "<|im_start|>"],
        }, separators=(",", ":"))
        rows.append({
            "recordType": "prepared-production-core-appraisal",
            "fixtureId": manifest["fixtureId"],
            "fixtureSha256": manifest_hash,
            "caseId": case_id,
            "humanReview": {"label": label},
            "requestPrompt": prompt,
            "requestPromptSha256": hashlib.sha256(prompt.encode("utf-8")).hexdigest(),
            "completionRequestBody": completion_body,
            "completionRequestSha256": hashlib.sha256(completion_body.encode("utf-8")).hexdigest(),
            "syntheticFixture": True,
            "nativeCommitAvailable": False,
        })

    for profile in base["profiles"]:
        for scenario in base["scenarios"]:
            add(f"{profile['personId']}/{scenario['id']}", scenario["reviewLabel"], scenario)
    scenarios_by_id = {scenario["id"]: scenario for scenario in base["scenarios"]}
    for pair in pairs["pairs"]:
        scenario = scenarios_by_id[pair["scenarioId"]]
        public_topic = pair.get("publicTopicId")
        for arm in pair["arms"]:
            projection = ""
            if "needIntensity" in arm:
                projection = f"Known Health intensity={arm['needIntensity']}"
            elif "dutyPresent" in arm and arm["dutyPresent"]:
                projection = "Active Appointment Schedule duty"
            elif "epistemicKind" in arm:
                projection = f"Public topic {public_topic}:{arm['epistemicKind']}"
            add(arm["caseId"], arm["reviewLabel"], scenario, projection)
    return rows


class AppraisalPreparationContractTests(unittest.TestCase):
    def setUp(self):
        self.rows = fixture_rows()

    def test_accepts_all_fixture_cases_with_label_separate_from_hashed_prompt(self):
        result = validate_rows(self.rows)
        self.assertEqual(result["status"], "pass")
        self.assertEqual(result["rowsVerified"], 32)
        self.assertEqual(result["labelsVerifiedOutsidePrompts"], 8)

    def test_rejects_review_label_leak_in_prompt(self):
        self.rows[0]["requestPrompt"] += " " + self.rows[0]["humanReview"]["label"]
        self.rows[0]["requestPromptSha256"] = hashlib.sha256(
            self.rows[0]["requestPrompt"].encode("utf-8")
        ).hexdigest()
        body = json.loads(self.rows[0]["completionRequestBody"])
        body["prompt"] = self.rows[0]["requestPrompt"]
        self.rows[0]["completionRequestBody"] = json.dumps(body, separators=(",", ":"))
        self.rows[0]["completionRequestSha256"] = hashlib.sha256(
            self.rows[0]["completionRequestBody"].encode("utf-8")
        ).hexdigest()
        with self.assertRaises(PreparationContractError) as _raised_122:
            validate_rows(self.rows)
        self.assertTrue(('review label leaked' in str(_raised_122.exception)), str(_raised_122.exception))

    def test_rejects_scenario_identifier_leak_through_prompt_metadata(self):
        self.rows[0]["requestPrompt"] += " canonicalId=receipt:npc:appraisal-commoner-steady/native-known-gift"
        self.rows[0]["requestPromptSha256"] = hashlib.sha256(
            self.rows[0]["requestPrompt"].encode("utf-8")
        ).hexdigest()
        body = json.loads(self.rows[0]["completionRequestBody"])
        body["prompt"] = self.rows[0]["requestPrompt"]
        self.rows[0]["completionRequestBody"] = json.dumps(body, separators=(",", ":"))
        self.rows[0]["completionRequestSha256"] = hashlib.sha256(
            self.rows[0]["completionRequestBody"].encode("utf-8")
        ).hexdigest()
        with self.assertRaises(PreparationContractError) as _raised_136:
            validate_rows(self.rows)
        self.assertTrue(('scenario identifier leaked' in str(_raised_136.exception)), str(_raised_136.exception))

    def test_rejects_prompt_hash_mismatch(self):
        self.rows[0]["requestPromptSha256"] = "0" * 64
        with self.assertRaises(PreparationContractError) as _raised_141:
            validate_rows(self.rows)
        self.assertTrue(('requestPrompt SHA-256' in str(_raised_141.exception)), str(_raised_141.exception))

    def test_requires_exact_core_completion_body_and_hash(self):
        result = validate_rows(self.rows)
        self.assertEqual(result["completionBodiesVerified"], 32)
        body = json.loads(self.rows[0]["completionRequestBody"])
        body["prompt"] = "different prompt"
        self.rows[0]["completionRequestBody"] = json.dumps(body, separators=(",", ":"))
        self.rows[0]["completionRequestSha256"] = hashlib.sha256(
            self.rows[0]["completionRequestBody"].encode("utf-8")
        ).hexdigest()
        with self.assertRaises(PreparationContractError) as _raised_153:
            validate_rows(self.rows)
        self.assertTrue(('completion body prompt' in str(_raised_153.exception)), str(_raised_153.exception))

    def test_rejects_missing_or_duplicate_case_ids(self):
        with self.assertRaises(PreparationContractError) as _raised_157:
            validate_rows(self.rows[:-1])
        self.assertTrue(('expected 32' in str(_raised_157.exception)), str(_raised_157.exception))
        self.rows[-1]["caseId"] = self.rows[0]["caseId"]
        with self.assertRaises(PreparationContractError) as _raised_160:
            validate_rows(self.rows)
        self.assertTrue(('duplicate caseId' in str(_raised_160.exception)), str(_raised_160.exception))

    def test_rejects_rows_that_can_commit_native_state_or_include_oracle(self):
        self.rows[0]["nativeCommitAvailable"] = True
        with self.assertRaises(PreparationContractError) as _raised_165:
            validate_rows(self.rows)
        self.assertTrue(('nativeCommitAvailable' in str(_raised_165.exception)), str(_raised_165.exception))
        self.rows = fixture_rows()
        self.rows[0]["oracle"] = {"truth": "hidden"}
        with self.assertRaises(PreparationContractError) as _raised_169:
            validate_rows(self.rows)
        self.assertTrue(('oracle data' in str(_raised_169.exception)), str(_raised_169.exception))

    def test_v2_fixture_marks_cause_unknown_native_events_as_unattributed(self):
        fixture_path = APPRAISAL_FIXTURE_PATH.with_name("appraisal-live-scenarios-v2.json")
        fixture = json.loads(fixture_path.read_text(encoding="utf-8"))
        self.assertEqual(fixture["fixtureId"], "npc-experience-appraisal-live-scenarios-v2")
        scenarios = {scenario["type"]: scenario for scenario in fixture["scenarios"]}
        for event_type in ("injury-health-episode", "status-onset"):
            self.assertEqual(scenarios[event_type]["responsibleActorAttribution"], "Unknown")
            self.assertNotIn("responsibleActorId", scenarios[event_type])
        for event_type in ("known-gift", "confirmed-help"):
            self.assertEqual(scenarios[event_type]["responsibleActorAttribution"], "Known")
            self.assertEqual(scenarios[event_type]["responsibleActorId"], "player")

        rows = fixture_rows("v2")
        result = validate_rows(rows, fixture_path=fixture_path)
        self.assertEqual(result["fixtureId"], fixture["fixtureId"])
        self.assertEqual(result["rowsVerified"], 32)

    def test_v2_validator_rejects_player_attribution_in_cause_unknown_prompt(self):
        fixture_path = APPRAISAL_FIXTURE_PATH.with_name("appraisal-live-scenarios-v2.json")
        rows = fixture_rows("v2")
        row = next(item for item in rows if item["caseId"].endswith("/native-injury-health-episode"))
        row["requestPrompt"] = row["requestPrompt"].replace(
            "responsibleActorAttribution=Unknown,responsibleActor=unknown",
            "responsibleActorAttribution=Known,responsibleActor=player",
        )
        row["requestPromptSha256"] = hashlib.sha256(row["requestPrompt"].encode("utf-8")).hexdigest()
        body = json.loads(row["completionRequestBody"])
        body["prompt"] = row["requestPrompt"]
        row["completionRequestBody"] = json.dumps(body, separators=(",", ":"))
        row["completionRequestSha256"] = hashlib.sha256(
            row["completionRequestBody"].encode("utf-8")
        ).hexdigest()
        with self.assertRaises(PreparationContractError) as _raised_204:
            validate_rows(rows, fixture_path=fixture_path)
        self.assertTrue(('does not preserve unknown attribution' in str(_raised_204.exception)), str(_raised_204.exception))

    def test_v3_manifest_pins_base_and_context_fixtures_and_validates_38_rows(self):
        rows = fixture_rows_v3()
        result = validate_rows(rows, manifest_path=APPRAISAL_V3_MANIFEST_PATH)
        self.assertEqual(result["fixtureId"], "npc-experience-appraisal-live-scenarios-v3")
        self.assertEqual(result["rowsVerified"], 38)
        self.assertEqual(len({row["caseId"] for row in rows}), 38)

    def test_v3_rejects_missing_or_duplicate_case_ids(self):
        rows = fixture_rows_v3()
        with self.assertRaises(PreparationContractError) as _raised_216:
            validate_rows(rows[:-1], manifest_path=APPRAISAL_V3_MANIFEST_PATH)
        self.assertTrue(('expected 38' in str(_raised_216.exception)), str(_raised_216.exception))
        rows[-1]["caseId"] = rows[0]["caseId"]
        with self.assertRaises(PreparationContractError) as _raised_219:
            validate_rows(rows, manifest_path=APPRAISAL_V3_MANIFEST_PATH)
        self.assertTrue(('duplicate caseId' in str(_raised_219.exception)), str(_raised_219.exception))

    def test_v3_rejects_context_case_or_hidden_value_leak(self):
        rows = fixture_rows_v3()
        row = next(item for item in rows if item["caseId"] == "context-topic-verified")
        row["requestPrompt"] += " " + "An unidentified status began after an event by the west quay."
        row["requestPromptSha256"] = hashlib.sha256(row["requestPrompt"].encode("utf-8")).hexdigest()
        body = json.loads(row["completionRequestBody"])
        body["prompt"] = row["requestPrompt"]
        row["completionRequestBody"] = json.dumps(body, separators=(",", ":"))
        row["completionRequestSha256"] = hashlib.sha256(row["completionRequestBody"].encode("utf-8")).hexdigest()
        with self.assertRaises(PreparationContractError) as _raised_231:
            validate_rows(rows, manifest_path=APPRAISAL_V3_MANIFEST_PATH)
        self.assertTrue(('review-only context metadata leaked' in str(_raised_231.exception)), str(_raised_231.exception))

    def test_v3_rejects_a_modified_child_fixture_even_when_manifest_is_unchanged(self):
        from pathlib import Path
        from tempfile import TemporaryDirectory

        source_dir = APPRAISAL_V3_MANIFEST_PATH.parent
        with TemporaryDirectory() as temporary:
            target = Path(temporary)
            for name in (APPRAISAL_V3_MANIFEST_PATH.name, "appraisal-live-scenarios-v2.json",
                         "appraisal-context-pairs-v3.json"):
                (target / name).write_bytes((source_dir / name).read_bytes())
            child = target / "appraisal-context-pairs-v3.json"
            child.write_bytes(child.read_bytes() + b"\n")
            with self.assertRaises(PreparationContractError) as _raised_246:
                validate_rows(fixture_rows_v3(), manifest_path=target / APPRAISAL_V3_MANIFEST_PATH.name)
            self.assertTrue(('context-pair fixture SHA-256 mismatch' in str(_raised_246.exception)), str(_raised_246.exception))


if __name__ == "__main__":
    unittest.main()
