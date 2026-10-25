"""Offline Qwen selector adapter for matched D3 choice diagnostics.

This module maps one exact D3 choice request to one constrained Qwen request,
then maps one Qwen choice back to the canonical D3 response envelope. It is a
diagnostic selector substitution, not a production Qwen fallback path.
"""

from __future__ import annotations

import json
from typing import Any


def _decode_json_object(data: bytes, label: str) -> dict[str, Any]:
    if not isinstance(data, (bytes, bytearray)):
        raise ValueError(f"{label} must be bytes")
    try:
        value = json.loads(bytes(data).decode("utf-8", errors="strict"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"{label} must be valid UTF-8 JSON") from exc
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be a JSON object")
    return value


def _choice_question(d3_request: bytes) -> tuple[dict[str, Any], str, dict[str, str]]:
    envelope = _decode_json_object(d3_request, "D3 request")
    state = envelope.get("state")
    questions = envelope.get("questions")
    if not isinstance(state, str) or not isinstance(questions, dict):
        raise ValueError("D3 request must contain string state and questions object")
    question = questions.get("decision")
    if not isinstance(question, dict) or question.get("type") != "choice":
        raise ValueError("D3 request must contain questions.decision choice")
    criteria = question.get("criteria")
    if not isinstance(criteria, dict) or not criteria:
        raise ValueError("D3 decision criteria must be a non-empty mapping")
    if any(not isinstance(option_id, str) or not option_id or
           not isinstance(description, str) or not description
           for option_id, description in criteria.items()):
        raise ValueError("D3 decision criteria must map non-empty IDs to descriptions")
    instructions = question.get("instructions", "")
    if not isinstance(instructions, str):
        raise ValueError("D3 decision instructions must be a string")
    return question, state, criteria


def build_qwen_choice_request(d3_request: bytes, model: str) -> bytes:
    """Build one OpenAI-compatible constrained-choice chat completion request."""
    if not isinstance(model, str) or not model:
        raise ValueError("model must be a non-empty string")
    question, state, criteria = _choice_question(d3_request)
    schema = {
        "type": "object",
        "properties": {"choice": {"type": "string", "enum": list(criteria)}},
        "required": ["choice"],
        "additionalProperties": False,
    }
    visible_input = {
        "state": state,
        "instructions": question.get("instructions", ""),
        "criteria": criteria,
    }
    request = {
        "model": model,
        "messages": [
            {"role": "system", "content": (
                "Select exactly one offered choice using only the supplied state, instructions, "
                "and criteria. Return the selected choice ID in the required JSON schema. "
                "Do not add explanations or other fields."
            )},
            {"role": "user", "content": json.dumps(visible_input, ensure_ascii=False,
                                                      separators=(",", ":"))},
        ],
        "temperature": 0,
        "stream": False,
        "response_format": {
            "type": "json_schema",
            "json_schema": {"name": "npc_choice", "strict": True, "schema": schema},
        },
    }
    return json.dumps(request, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def convert_qwen_choice_response(d3_request: bytes, qwen_response: bytes, *, expected_model: str | None = None) -> bytes:
    """Convert one constrained Qwen choice into the canonical D3 response envelope.

    Reject every malformed or unoffered result. Never infer a choice from prose.
    """
    _, _, criteria = _choice_question(d3_request)
    response = _decode_json_object(qwen_response, "Qwen response")
    model = response.get("model")
    choices = response.get("choices")
    if not isinstance(model, str) or not model:
        raise ValueError("Qwen response must contain a non-empty model")
    if expected_model is not None:
        normalized_model = model.replace("\\", "/")
        normalized_expected = expected_model.replace("\\", "/")
        identity = normalized_model if "/" in normalized_expected else normalized_model.rsplit("/", 1)[-1]
        if identity != normalized_expected:
            raise ValueError("Qwen response model differs from requested model identity")
    if not isinstance(choices, list) or len(choices) != 1 or not isinstance(choices[0], dict):
        raise ValueError("Qwen response must contain exactly one completion choice")
    message = choices[0].get("message")
    content = message.get("content") if isinstance(message, dict) else None
    if not isinstance(content, str):
        raise ValueError("Qwen completion content must be a JSON string")
    try:
        parsed = json.loads(content)
    except json.JSONDecodeError as exc:
        raise ValueError("Qwen completion content must be valid choice JSON") from exc
    if not isinstance(parsed, dict) or set(parsed) != {"choice"}:
        raise ValueError("Qwen completion must contain only choice field")
    selected = parsed.get("choice")
    if not isinstance(selected, str) or selected not in criteria:
        raise ValueError("Qwen choice is missing or was not offered")
    canonical = {
        "model": model,
        # The production wire parser requires a probability mapping. This is a
        # deterministic encoding of the selected ID, not measured confidence.
        "answers": {"decision": {"type": "choice", "choice": selected,
                                 "probabilities": {key: 1.0 if key == selected else 0.0 for key in criteria}}},
    }
    return json.dumps(canonical, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
