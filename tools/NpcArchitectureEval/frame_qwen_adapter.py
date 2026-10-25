"""Offline Qwen selector adapter for matched decision-frame diagnostics."""
from __future__ import annotations

import json
from typing import Any

import run_native_route_replay as route_replay


_strict_json_loads = route_replay._strict_json_loads


def _selection_questions(frame_payload: dict[str, Any]) -> tuple[str, dict[str, dict[str, Any]]]:
    if not isinstance(frame_payload, dict) or set(frame_payload) != {"state", "questions"}:
        raise ValueError("Frame payload must contain only state and questions")
    state = frame_payload["state"]
    questions = frame_payload["questions"]
    if not isinstance(state, str) or not isinstance(questions, dict) or not questions:
        raise ValueError("Frame payload needs string state and non-empty questions object")
    for question_id, question in questions.items():
        if not isinstance(question_id, str) or not question_id:
            raise ValueError("Frame question IDs must be non-empty strings")
        if not isinstance(question, dict) or set(question) != {"type", "instructions", "criteria"}:
            raise ValueError("Frame question must contain only type, instructions, and criteria")
        if question["type"] != "choice":
            raise ValueError("Frame questions must use choice type")
        if not isinstance(question["instructions"], str) or not question["instructions"]:
            raise ValueError("Frame question instructions must be a non-empty string")
        criteria = question["criteria"]
        if not isinstance(criteria, dict) or not criteria:
            raise ValueError("Frame criteria must be a non-empty mapping")
        if any(not isinstance(option_id, str) or not option_id or
               not isinstance(description, str) or not description
               for option_id, description in criteria.items()):
            raise ValueError("Frame criteria must map non-empty IDs to descriptions")
    return state, questions


def build_payload(frame_payload: dict, model: str) -> dict:
    """Build one strict OpenAI chat request from one exact frame payload."""
    if not isinstance(model, str) or not model:
        raise ValueError("model must be a non-empty string")
    state, questions = _selection_questions(frame_payload)
    question_ids = list(questions)
    properties = {
        question_id: {"type": "string", "enum": list(question["criteria"])}
        for question_id, question in questions.items()
    }
    schema = {"type": "object", "properties": properties, "required": question_ids,
              "additionalProperties": False}
    visible_input = {
        "state": state,
        "questions": {
            question_id: {"instructions": question["instructions"],
                          "criteria": question["criteria"]}
            for question_id, question in questions.items()
        },
    }
    return {
        "model": model,
        "messages": [
            {"role": "system", "content": (
                "Select one exact offered choice for each supplied question. "
                "Return only the required JSON object. Do not add explanations or fields."
            )},
            {"role": "user", "content": json.dumps(visible_input, ensure_ascii=False,
                                                    separators=(",", ":"))},
        ],
        "temperature": 0,
        "stream": False,
        "response_format": {"type": "json_schema", "json_schema": {
            "name": "decision_frame_selection_v1", "strict": True, "schema": schema,
        }},
    }


def convert_response(frame_payload: dict, raw_chat_response: str, expected_model: str) -> dict:
    """Convert one strict Qwen response to the canonical decision-frame envelope."""
    _, questions = _selection_questions(frame_payload)
    if not isinstance(expected_model, str) or not expected_model:
        raise ValueError("expected_model must be a non-empty string")
    if not isinstance(raw_chat_response, str):
        raise ValueError("Qwen response must be a JSON string")
    try:
        response = _strict_json_loads(raw_chat_response)
    except (json.JSONDecodeError, ValueError) as exc:
        raise ValueError("Qwen response must be valid strict JSON") from exc
    if not isinstance(response, dict):
        raise ValueError("Qwen response must be a JSON object")
    model = response.get("model")
    if not isinstance(model, str) or not model:
        raise ValueError("Qwen response model must be a non-empty string")
    if model != expected_model:
        raise ValueError("Qwen response model differs from requested model identity")
    choices = response.get("choices")
    if not isinstance(choices, list) or len(choices) != 1 or not isinstance(choices[0], dict):
        raise ValueError("Qwen response must contain exactly one completion choice")
    completion = choices[0]
    if completion.get("finish_reason") != "stop":
        raise ValueError("Qwen completion finish_reason must be stop")
    message = completion.get("message")
    content = message.get("content") if isinstance(message, dict) else None
    if not isinstance(content, str):
        raise ValueError("Qwen completion content must be a JSON string")
    try:
        selected = _strict_json_loads(content)
    except (json.JSONDecodeError, ValueError) as exc:
        raise ValueError("Qwen completion content must be valid strict JSON") from exc
    expected_ids = set(questions)
    if not isinstance(selected, dict) or set(selected) != expected_ids:
        raise ValueError("Qwen completion question keys differ from frame payload")

    answers = {}
    for question_id, question in questions.items():
        choice = selected[question_id]
        criteria = question["criteria"]
        if not isinstance(choice, str) or choice not in criteria:
            raise ValueError(f"Qwen choice for {question_id} is missing or was not offered")
        # Synthetic one-hot values satisfy the existing D3 validator. They are not confidence.
        answers[question_id] = {
            "type": "choice",
            "choice": choice,
            "probabilities": {option_id: 1.0 if option_id == choice else 0.0
                              for option_id in criteria},
        }
    return {"model": model, "answers": answers}
