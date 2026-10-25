"""Validation, leakage-safe replay, and descriptive scoring for NPC fixtures.

This module deliberately does not make a semantic pass/fail decision.  The
scenario gold labels stay evaluator-side; only the explicit input whitelist is
presented to a model adapter.
"""

from __future__ import annotations

import copy
import math
from dataclasses import dataclass
from typing import Any, Iterable, Mapping


MODEL_INPUT_FIELDS = ("npc", "world", "history", "playerText", "availableTools", "options")
SEVERITIES = {"critical", "major", "minor"}
DEFAULT_WEIGHTS = {"critical": 3, "major": 2, "minor": 1}


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def _deep_merge(target: dict[str, Any], update: Mapping[str, Any]) -> None:
    for key, value in update.items():
        if key == "historyAppend":
            # It is a replay instruction, not an input field to be merged.
            continue
        if isinstance(value, Mapping) and isinstance(target.get(key), dict):
            _deep_merge(target[key], value)
        else:
            target[key] = copy.deepcopy(value)


def apply_input_updates(source: Mapping[str, Any], updates: Mapping[str, Any] | None) -> dict[str, Any]:
    """Return a copied input with nested merge and dotted-path updates applied."""
    result = copy.deepcopy(dict(source))
    if not updates:
        return result

    # Apply ordinary nested updates first, then dotted paths in their declared
    # order. This makes a dotted path unambiguous even beside a nested object.
    nested: dict[str, Any] = {}
    dotted: list[tuple[str, Any]] = []
    for key, value in updates.items():
        if key == "historyAppend":
            continue
        if isinstance(key, str) and "." in key:
            dotted.append((key, value))
        else:
            nested[key] = value
    _deep_merge(result, nested)
    for path, value in dotted:
        parts = path.split(".")
        _require(all(parts), f"invalid empty segment in update path {path!r}")
        cursor = result
        for part in parts[:-1]:
            current = cursor.get(part)
            if current is None:
                current = {}
                cursor[part] = current
            _require(isinstance(current, dict), f"cannot apply dotted path {path!r} through non-object {part!r}")
            cursor = current
        cursor[parts[-1]] = copy.deepcopy(value)

    history_append = updates.get("historyAppend", [])
    if history_append:
        _require(isinstance(history_append, list), "historyAppend must be a list")
        _require(isinstance(result.get("history"), list), "input.history must be a list when historyAppend is used")
        result["history"].extend(copy.deepcopy(history_append))
    return result


def _validate_expected(expected: Any, options: list[dict[str, Any]], where: str) -> None:
    _require(isinstance(expected, dict), f"{where}: expected must be an object")
    ids = {option["id"] for option in options}
    allowed = expected.get("allowedChoices")
    forbidden = expected.get("forbiddenChoices", [])
    _require(isinstance(allowed, list) and len(allowed) > 0, f"{where}: allowedChoices must be a non-empty list")
    _require(isinstance(forbidden, list), f"{where}: forbiddenChoices must be a list")
    _require(all(isinstance(item, str) and item for item in allowed + forbidden), f"{where}: choice references must be non-empty strings")
    _require(len(allowed) == len(set(allowed)), f"{where}: duplicate allowed choice reference")
    _require(len(forbidden) == len(set(forbidden)), f"{where}: duplicate forbidden choice reference")
    _require(not (set(allowed) & set(forbidden)), f"{where}: allowed and forbidden choices overlap")
    _require(set(allowed + forbidden) <= ids, f"{where}: expected choice reference is not offered")
    _require(expected.get("severity") in SEVERITIES, f"{where}: invalid severity")


def _validate_input(case_input: Any, where: str) -> list[dict[str, Any]]:
    _require(isinstance(case_input, dict), f"{where}: input must be an object")
    for field in MODEL_INPUT_FIELDS:
        _require(field in case_input, f"{where}: missing input field {field!r}")
    options = case_input["options"]
    _require(isinstance(options, list), f"{where}: options must be a list")
    _require(3 <= len(options) <= 6, f"{where}: expected 3–6 options")
    option_ids: list[str] = []
    for index, option in enumerate(options):
        _require(isinstance(option, dict), f"{where}: option {index} must be an object")
        option_id = option.get("id")
        _require(isinstance(option_id, str) and option_id, f"{where}: option {index} has invalid id")
        option_ids.append(option_id)
    _require(len(option_ids) == len(set(option_ids)), f"{where}: duplicate option ids")
    _require(isinstance(case_input["history"], list), f"{where}: history must be a list")
    return options


def validate_suite(suite: Mapping[str, Any]) -> None:
    """Raise ValueError for malformed suite/case/turn references."""
    _require(isinstance(suite, Mapping), "suite must be an object")
    cases = suite.get("cases")
    _require(isinstance(cases, list), "suite.cases must be a list")
    seen_case_ids: set[str] = set()
    for index, case in enumerate(cases):
        where = f"case[{index}]"
        _require(isinstance(case, dict), f"{where} must be an object")
        case_id = case.get("id")
        _require(isinstance(case_id, str) and case_id, f"{where}: invalid id")
        _require(case_id not in seen_case_ids, f"duplicate case id {case_id!r}")
        seen_case_ids.add(case_id)
        base = copy.deepcopy(case.get("input"))
        options = _validate_input(base, where)
        turns = case.get("turns")
        if turns is None:
            _validate_expected(case.get("expected"), options, f"{where}.expected")
            continue
        _require(isinstance(turns, list) and len(turns) > 0, f"{where}: turns must be a non-empty list")
        _require("expected" in case, f"{where}: root expected is required for fixture compatibility")
        _validate_expected(case["expected"], options, f"{where}.expected")
        state = base
        for turn_index, turn in enumerate(turns):
            tw = f"{where}.turns[{turn_index}]"
            _require(isinstance(turn, dict), f"{tw} must be an object")
            _require(isinstance(turn.get("playerText"), (str, type(None))), f"{tw}: playerText must be string or null")
            updates = turn.get("inputUpdates", {})
            _require(isinstance(updates, dict), f"{tw}: inputUpdates must be an object")
            state = apply_input_updates(state, updates)
            options = _validate_input(state, tw)
            _validate_expected(turn.get("expected"), options, f"{tw}.expected")


@dataclass(frozen=True)
class PreparedTurn:
    model_input: dict[str, Any]
    expected: dict[str, Any]
    turn_index: int
    case_id: str
    capability_ids: tuple[str, ...]
    option_by_id: dict[str, dict[str, Any]]


class ScenarioReplay:
    """Sequential replay; only caller-supplied actual choices enter history."""

    def __init__(self, case: Mapping[str, Any]):
        self._case = copy.deepcopy(dict(case))
        validate_suite({"cases": [self._case]})
        self._state = copy.deepcopy(self._case["input"])
        self._turns = self._case.get("turns")
        self._next = 0
        self._pending: PreparedTurn | None = None
        self._finished = False

    def prepare_next(self) -> PreparedTurn | None:
        if self._pending is not None:
            raise RuntimeError("record the pending actual choice before preparing another turn")
        if self._finished:
            return None
        if self._turns is None:
            if self._next:
                self._finished = True
                return None
            turn = None
            expected = self._case["expected"]
            index = 0
            state = copy.deepcopy(self._state)
        else:
            if self._next >= len(self._turns):
                self._finished = True
                return None
            turn = self._turns[self._next]
            self._state = apply_input_updates(self._state, turn.get("inputUpdates", {}))
            expected = turn["expected"]
            index = self._next
            state = copy.deepcopy(self._state)

        model_input = {key: copy.deepcopy(state[key]) for key in MODEL_INPUT_FIELDS}
        model_input["playerText"] = turn.get("playerText") if turn is not None else state["playerText"]
        prepared = PreparedTurn(
            model_input=model_input,
            expected=copy.deepcopy(expected),
            turn_index=index,
            case_id=self._case["id"],
            capability_ids=tuple(self._case.get("capabilityIds", [])),
            option_by_id={option["id"]: copy.deepcopy(option) for option in model_input["options"]},
        )
        self._pending = prepared
        self._next += 1
        return prepared

    def record_choice(self, choice_id: str) -> None:
        if self._pending is None:
            raise RuntimeError("no prepared turn is awaiting a choice")
        _require(isinstance(choice_id, str) and choice_id, "choice_id must be a non-empty string")
        option = self._pending.option_by_id.get(choice_id)
        description = option.get("description") if option else None
        selected = f"{choice_id} — {description}" if description else choice_id
        history = self._state["history"]
        if self._turns is not None:
            player_text = self._pending.model_input.get("playerText")
            if player_text is not None:
                history.append({"speaker": "player", "text": player_text})
            history.append({
                "speaker": "decision_event",
                "text": f"Simulated proposed decision; no native effect confirmed: {selected}",
            })
        else:
            history.append({
                "speaker": "decision_event",
                "text": f"Simulated proposed decision; no native effect confirmed: {selected}",
            })
            self._finished = True
        self._pending = None


def score_choice(prepared: PreparedTurn, choice_id: str) -> dict[str, Any]:
    offered = set(prepared.option_by_id)
    allowed = set(prepared.expected.get("allowedChoices", []))
    forbidden = set(prepared.expected.get("forbiddenChoices", []))
    invalid_reason = None
    if choice_id not in offered:
        classification, invalid_reason = "invalid", "not_offered"
    elif choice_id in allowed:
        classification = "allowed"
    elif choice_id in forbidden:
        classification = "forbidden"
    else:
        classification = "unexpected"
    categories = sorted({cap.split(".", 1)[0] for cap in prepared.capability_ids if cap})
    weight = DEFAULT_WEIGHTS[prepared.expected["severity"]]
    return {
        "case_id": prepared.case_id,
        "turn_index": prepared.turn_index,
        "choice_id": choice_id,
        "classification": classification,
        "invalid_reason": invalid_reason,
        "point": 1 if classification == "allowed" else 0,
        "severity": prepared.expected["severity"],
        "weight": weight,
        "categories": categories,
        "capability_ids": list(prepared.capability_ids),
    }


def aggregate_scores(scores: Iterable[Mapping[str, Any]], severity_weights: Mapping[str, int] | None = None) -> dict[str, Any]:
    """Summarize choices by class, severity, category, and capability."""
    weights = dict(DEFAULT_WEIGHTS)
    if severity_weights:
        weights.update(severity_weights)
    classifications = ("allowed", "forbidden", "unexpected", "invalid")
    counts = {name: 0 for name in classifications}
    per_severity: dict[str, dict[str, int]] = {}
    per_category: dict[str, dict[str, int]] = {}
    per_capability: dict[str, dict[str, int]] = {}
    weighted_points = weighted_total = 0
    for score in scores:
        classification = score.get("classification")
        if classification not in counts:
            raise ValueError(f"unknown score classification {classification!r}")
        counts[classification] += 1
        severity = score["severity"]
        if severity not in SEVERITIES:
            raise ValueError(f"unknown score severity {severity!r}")
        weight = int(weights[severity])
        weighted_total += weight
        weighted_points += weight * int(classification == "allowed")
        bucket = per_severity.setdefault(severity, {"count": 0, **{name: 0 for name in classifications}, "weighted_mistakes": 0})
        bucket["count"] += 1
        bucket[classification] += 1
        bucket["weighted_mistakes"] += weight * int(classification != "allowed")
        for category in score.get("categories", []):
            cat = per_category.setdefault(category, {"count": 0, **{name: 0 for name in classifications}})
            cat["count"] += 1
            cat[classification] += 1
        for capability in score.get("capability_ids", []):
            cap = per_capability.setdefault(capability, {"count": 0, **{name: 0 for name in classifications}})
            cap["count"] += 1
            cap[classification] += 1
    total = sum(counts.values())
    return {
        "counts": counts,
        "count": total,
        "raw_choice_accuracy": counts["allowed"] / total if total else None,
        "weighted_quality": weighted_points / weighted_total if weighted_total else None,
        "per_severity": per_severity,
        "per_category": per_category,
        "per_capability": per_capability,
        "weighted_mistakes": sum(bucket["weighted_mistakes"] for bucket in per_severity.values()),
    }

