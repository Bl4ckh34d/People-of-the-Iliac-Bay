"""Build a manual review sheet from complete appraisal JSONL artifacts.

Core acceptance is reported separately from human judgment. No semantic score is automated.
"""
from __future__ import annotations

import argparse
import json
from collections import Counter
from pathlib import Path
from typing import Any

EXPECTED_CASES = 32
DELTA_FIELDS = ("moodDelta", "affinityDelta", "trustDelta", "resentmentDelta", "fearDelta")


def _read_jsonl(path: Path, label: str) -> list[dict[str, Any]]:
    rows = []
    for line_no, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError as exc:
            raise ValueError(f"{label} JSONL invalid at line {line_no}: {exc}") from exc
        if not isinstance(row, dict):
            raise ValueError(f"{label} row {line_no} is not an object")
        rows.append(row)
    if len(rows) != EXPECTED_CASES:
        raise ValueError(f"{label} must contain exactly 32 complete rows; found {len(rows)}")
    return rows


def _index(rows: list[dict[str, Any]], label: str) -> dict[str, dict[str, Any]]:
    indexed = {}
    for row in rows:
        case_id = row.get("caseId")
        if not isinstance(case_id, str) or not case_id or case_id in indexed:
            raise ValueError(f"{label} has a missing or duplicate caseId")
        indexed[case_id] = row
    return indexed


def _cell(value: Any, limit: int = 420) -> str:
    if value is None:
        return "—"
    text = " ".join(str(value).replace("|", "\\|").split())
    return text if len(text) <= limit else text[: limit - 1] + "…"


def _prompt_evidence(prompt: str) -> tuple[str, str, str]:
    lines = prompt.splitlines()
    event = next((x for x in lines if x.startswith("event={")), "")
    utterance = next((x.partition("=")[2] for x in lines if x.startswith("observedUtterance=")), "")
    relationship = next((x.partition("=")[2] for x in lines if x.startswith("relationship=")), "")
    _, separator, tail = event.partition("responsibleActorAttribution=")
    value = tail.split(",",1)[0].split("}",1)[0].strip()
    return (value if separator and value else "not found"), utterance, relationship


def _proposal(raw_response: Any) -> str:
    if not isinstance(raw_response, str) or not raw_response:
        return "no response text"
    try:
        obj = json.loads(raw_response)
    except json.JSONDecodeError:
        return "unparsed raw response: " + _cell(raw_response, 320)
    if not isinstance(obj, dict):
        return "non-object raw response: " + _cell(raw_response, 320)
    deltas = ",".join(str(obj.get(field, "?")) for field in DELTA_FIELDS)
    return f"({deltas}); act={obj.get('speechActKind', '?')}; target={obj.get('speechTargetPersonId') or '—'}; option={obj.get('conversationOption', '?')}"


def build_review_report(prepared_path: Path, raw_path: Path, validated_path: Path) -> tuple[str, dict[str, Any]]:
    prepared = _index(_read_jsonl(prepared_path, "prepared"), "prepared")
    raw = _index(_read_jsonl(raw_path, "raw results"), "raw results")
    validated = _index(_read_jsonl(validated_path, "Core-validated results"), "Core-validated results")
    ids = set(prepared)
    if set(raw) != ids or set(validated) != ids:
        raise ValueError("prepared, raw, and validated caseId sets do not match")
    core_counts: Counter[str] = Counter()
    failures: Counter[str] = Counter()
    fixture_ids, fixture_hashes = set(), set()
    records = []
    for case_id in sorted(ids):
        p, r, v = prepared[case_id], raw[case_id], validated[case_id]
        digest = p.get("requestPromptSha256")
        if not digest or r.get("requestPromptSha256") != digest or v.get("requestPromptSha256") != digest:
            raise ValueError(f"{case_id}: prompt hash mismatch between prepared/raw/validated rows")
        fixture_ids.add(str(p.get("fixtureId", "")))
        fixture_hashes.add(str(p.get("fixtureSha256", "")))
        if v.get("fixtureId") != p.get("fixtureId") or v.get("fixtureSha256") != p.get("fixtureSha256"):
            raise ValueError(f"{case_id}: fixture binding mismatch in Core output")
        accepted = v.get("coreValidated") is True
        state = "accepted" if accepted else "rejected"
        core_counts[state] += 1
        failure = v.get("failureKind")
        if failure:
            failures[str(failure)] += 1
        prompt = p.get("requestPrompt")
        if not isinstance(prompt, str):
            raise ValueError(f"{case_id}: prepared requestPrompt missing")
        attribution, utterance, relationship = _prompt_evidence(prompt)
        appraisal = v.get("appraisal") if accepted else None
        if accepted and not isinstance(appraisal, dict):
            raise ValueError(f"{case_id}: accepted row has no parsed appraisal")
        proposal = _proposal(r.get("responseText"))
        if accepted:
            deltas = ",".join(str(appraisal.get(k, "?")) for k in DELTA_FIELDS)
            proposal = f"({deltas}); act={appraisal.get('speechActKind', '?')}; target={appraisal.get('speechTargetPersonId') or '—'}; option={appraisal.get('conversationOption', '?')}"
        records.append({
            "scenario": case_id.rsplit("/", 1)[-1],
            "label": (p.get("humanReview") or {}).get("label", ""),
            "core": state + (": " + str(failure) if failure else ""),
            "attribution": attribution,
            "utterance": utterance,
            "relationship": relationship,
            "proposal": proposal,
        })
    if len(fixture_ids) != 1 or len(fixture_hashes) != 1:
        raise ValueError("prepared rows do not share one fixture ID and hash")
    summary = {
        "fixtureId": next(iter(fixture_ids)),
        "fixtureSha256": next(iter(fixture_hashes)),
        "caseCount": len(records),
        "coreAccepted": core_counts["accepted"],
        "coreRejected": core_counts["rejected"],
        "failureKinds": dict(sorted(failures.items())),
        "semanticReviewCompleted": False,
    }
    lines = [
        "# Manual appraisal semantic review", "",
        f"Fixture: {summary['fixtureId']}",
        f"Fixture SHA-256: {summary['fixtureSha256']}",
        f"Rows: {summary['caseCount']} | Core accepted: {summary['coreAccepted']} | Core rejected: {summary['coreRejected']}", "",
        "Core acceptance is separate from semantic review. Accepted means the current Core parser accepted the structured proposal; it does not mean the appraisal is correct or committed. Rejected proposals remain visible as raw model outputs. No semantic score is automated.", "",
        "Review quote and hypothetical cases by comparing the exact observed utterance, reported speechActKind, and target. Review injury/status cases with Unknown attribution as unknown cause; do not infer actor blame or poisoning. Numeric values are proposals, not applied state.", "",
        "| Case | Reviewer label | Core | Attribution | Observed utterance | Relationship | Proposal (M,A,T,R,F); act/target/option | Reviewer judgment |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for row in records:
        values = [row["scenario"], row["label"], row["core"], row["attribution"], row["utterance"], row["relationship"], row["proposal"], ""]
        lines.append("| " + " | ".join(_cell(value) for value in values) + " |")
    lines += [
        "", "## Reviewer checklist", "",
        "- Did the model distinguish a direct threat from quoted, reported, or hypothetical wording? Parser acceptance alone does not answer this.",
        "- For injury/status rows with Unknown attribution, did the proposal avoid assigning cause or actor? Do not treat illness/status as proof of poisoning or player intent.",
        "- Are mood, affinity, trust, resentment, and fear grounded in separate supplied evidence and ledger state? Record structural validity separately from plausibility and calibration.",
        "- Does nonzero trust rely on a relationship ledger and an allowlisted native event? Does a transfer alone improperly establish reliability?", "",
    ]
    return "\n".join(lines), summary


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepared", type=Path, required=True)
    parser.add_argument("--raw", type=Path, required=True)
    parser.add_argument("--validated", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--summary-output", type=Path)
    args = parser.parse_args()
    report, summary = build_review_report(args.prepared, args.raw, args.validated)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(report, encoding="utf-8")
    if args.summary_output:
        args.summary_output.parent.mkdir(parents=True, exist_ok=True)
        args.summary_output.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
