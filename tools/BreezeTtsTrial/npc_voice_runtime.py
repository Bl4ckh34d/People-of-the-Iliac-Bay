"""Create a neutral, persistent personal voice before applying utterance delivery."""
from pathlib import Path
import tempfile

from npc_voice_profiles import ANCHOR_TEXT, NpcVoiceProfileStore, VoiceProfileError
from audio_loudness import normalize_pcm16_wave


class PersonalVoiceCancelled(RuntimeError):
    pass


def resolve_personal_voice(request, voice_root, synthesize_anchor, is_cancelled, remaining_seconds):
    """Callbacks share the caller's cancellation and whole-presentation deadline."""
    def check_budget():
        if is_cancelled():
            raise PersonalVoiceCancelled("personal voice preparation was cancelled")
        remaining = remaining_seconds()
        if remaining <= 0:
            raise TimeoutError("personal voice preparation deadline expired")
        return remaining

    check_budget()
    identity = request.voice_identity or {}
    sex_field = identity.get("sex", {})
    native_gender = (sex_field.get("value") if isinstance(sex_field, dict) and
                     sex_field.get("known") is True else None)
    bridge_gender = request.voice_gender or "unknown"
    if native_gender is not None:
        if bridge_gender not in ("unknown", native_gender):
            raise VoiceProfileError("native gender conflict between voiceIdentity.sex and voiceGender")
        perceived_gender = native_gender
    else:
        perceived_gender = bridge_gender
    race_field = identity.get("race", {})
    race = (race_field.get("value") if isinstance(race_field, dict) and
            race_field.get("known") is True else "Unknown")
    age_field = identity.get("age", {})
    age = age_field.get("value", "unknown") if isinstance(age_field, dict) else "unknown"
    if (not isinstance(age_field, dict) or age_field.get("confidence") not in ("high", "medium")
            or age not in ("adult", "child")):
        age = "unknown"
    store = NpcVoiceProfileStore(Path(voice_root))
    profile = store.get_or_create(request.person_id, perceived_gender, age=age, race=race)
    created = False
    if profile.anchor_required:
        baseline = profile.to_coordinator_profile()
        Path(voice_root).mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix=".neutral-anchor-", dir=str(voice_root)) as temporary:
            output = Path(temporary) / "anchor.wav"
            synthesize_anchor(baseline, ANCHOR_TEXT, output, check_budget())
            check_budget()
            normalize_pcm16_wave(output)
            check_budget()
            profile = store.record_neutral_anchor(profile, output.read_bytes(), {
                "requestId": request.request_id,
                "profileId": profile.profile_id,
                "seed": baseline["seed"], "instruction": baseline["instruction"],
                "guidanceScale": 4.0, "text": ANCHOR_TEXT,
                "referenceAbsent": True, "voiceRef": None, "referenceText": None,
            })
            created = True
    check_budget()
    return profile.profile_id, profile.to_coordinator_profile(), {
        "anchorCreated": created,
        "race": race,
        "agePresentation": profile.age,
        "profileSchemaVersion": 2,
        "legacyProfileMigrated": profile.legacy_migrated,
        "legacyAnchorReused": False,
    }
