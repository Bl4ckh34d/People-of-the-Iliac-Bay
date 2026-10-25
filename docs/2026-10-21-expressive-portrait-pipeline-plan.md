# Expressive NPC portraits and future speech animation

Status: planning document from a read-only source and asset inventory, 21 October 2026. Expressive portrait assets and portrait replacement are not implemented. TTS and audio generation remain future work and are not enabled by this plan.

## Verified native inventory

DFU ships two native dialogue portrait banks. `TFAC00I0.RCI` contains 503 common-face records and `FACES.CIF` contains 61 special-face records. Both use 64×64 pixel records. DFU's `CifRciFile` treats `FACES.CIF` as an RCI-style fixed-size bank. These counts come from the local `GameFiles/arena2` files and DFU's parser; counts do not promise that every record is a unique human face.

`TalkManager` selects the portrait record. Mobile NPCs use `MobilePersonNPC.PersonFaceRecordId` from `TFAC00I0.RCI`. The mobile face-record table covers Breton, Nord and Redguard men and women. Static NPCs use a story face from `FACES.CIF` when the special-faction face index is at most 60; higher indices use the common bank. Other static NPCs resolve a common face from the faction's gender-specific flat, then prefer the current NPC billboard flat's `faceIndex`, with common record 410 as fallback.

`DaggerfallTalkWindow.SetNPCPortrait` is the render seam. It first calls `TextureReplacement.TryImportCifRci`; if no replacement is found, it decodes the selected record from the native bank. It stores the result in `texturePortrait` and assigns `panelPortrait.BackgroundTexture`. The method is `virtual`. People of the Iliac Bay currently has no portrait replacement or `SetNPCPortrait` override; its speaker-switch regression verifies that a fresh native portrait follows the selected NPC.

People of the Iliac Bay creates adult residents with persisted race, sex and appearance seed; actor setup derives outfit from that seed and DFU assigns the corresponding face record. Its child actor swaps the body billboard to native flat `182/43` (boy) or `182/38` (girl); it does not replace the dialogue portrait. Unsupported race enum values fall back to Breton in `ResidentActor`. DFU's native face-record arrays also cover only Breton, Nord and Redguard. Custom-race portrait mapping therefore needs an explicit adapter or stable custom portrait key; do not assume a custom body texture supplies a dialogue face.

Story and quest NPCs stay on DFU's static-NPC selection path. Preserve the chosen native bank and record as identity fallback, including their authored dialogue and quest ownership. Do not key a portrait solely by display name or faction: distinct static actors can share either.

Repository inventory: `mod/Textures` contains 53 PNG payloads and zero portrait-named images. `art/` contains 1,540 PNGs, mostly sleep and movement explorations, and zero portrait/expression/face/mugshot-named files. Existing runtime texture names identify dialogue backgrounds and body/sleep assets, not facial expressions. This is a filename and code inventory; I did not visually classify every art PNG. See `work/portrait-inventory-next.json` for counts and source paths.

## Recommended portrait identity and asset layout

Use one stable portrait identity key per actor, resolved from the existing persistent person ID where available. Keep a native fallback key `{bank, recordId}` and record the appearance source that selected it. Use a separate custom portrait key only when a supported race or actor adapter supplies one. Re-evaluate the key on speaker change, but never regenerate or reroll identity when mood changes, a conversation reopens, or a save loads.

Keep the native 64×64 source as identity base. Store authored expression assets as transparent, pixel-aligned layers over that base where composition preserves the source art. Separate brows/eyes from mouth shapes. A small set of reusable eye and mouth layers can form expressions without storing every eye × mouth combination as a full image. For faces that cannot support clean overlays, allow complete authored expression images linked to the same identity. Match native palette, pixel edges, lighting and facial proportions; validate each composed result at the Talk window's native display scale and the game's configured filter mode.

Candidate asset manifest fields:

```json
{
  "portraitKey": "person:<stable-person-id>",
  "nativeFallback": { "bank": "TFAC00I0.RCI", "recordId": 0 },
  "sourceKind": "native|texture-replacement|custom",
  "sourceRevision": "content-hash-or-version",
  "canvas": { "width": 64, "height": 64 },
  "layers": {
    "base": "portraits/<key>/base.png",
    "expressions": {
      "neutral": { "eyes": "eyes/neutral.png", "mouth": "mouth/closed.png" },
      "guarded": { "eyes": "eyes/guarded.png", "mouth": "mouth/closed.png" }
    },
    "visemes": {
      "closed": "mouth/closed.png",
      "open": "mouth/open.png"
    }
  },
  "anchors": { "eyes": [0, 0], "mouth": [0, 0] },
  "palette": "native-or-declared-palette",
  "fallbackPortraitKey": "native:<bank>:<recordId>",
  "qa": { "identity": "pending", "composite": "pending", "ui": "pending" }
}
```

Manifest coordinates and filenames above are proposed schema examples, not files or APIs that exist today. Add source attribution and asset provenance for every authored or generated image. Keep any existing texture replacement precedence intact; an expressive portrait layer should compose over the resolved replacement texture when compatible, then fall back to the original native record if loading or composition fails.

## Expression policy

Show a legible interpersonal demeanor, with current mood as a temporary influence. Candidate states include neutral, warm/open, guarded, amused, concerned and upset. Final set, relationship-to-expression weighting and transition timing remain undecided. Derive expression from approved public-facing relationship and mood projections; do not expose exact disposition, trust, fear, resentment, loyalty or hidden motives through a one-to-one visual code.

Allow behavior and expression to diverge. A manipulative or socially skilled NPC may smile while distrustful; a trustworthy NPC may look guarded because of temporary stress. Never select facial assets directly from hidden numeric stats. Mood recovery may relax temporary expression over game time, while lasting interpersonal stance remains distinct. Reopening Talk should reconstruct the same current expression from saved identity and current approved state, not advance an animation or relationship value.

## Future audio mouth composition

TTS is outside current implementation. When separately authorized, drive mouth frames from viseme or phoneme timestamps attached to the actual audio playback timeline. Do not animate from typed letters, token arrival, or text chunks. Preserve the selected eyes/brow expression while swapping only the mouth layer, except where an authored face requires a complete matching frame.

Timeline rules: mouth closes during pauses and after playback; animation follows audio time rather than request time; late audio starts with its correct current viseme; interruption or cancellation stops mouth motion immediately; speaker change, Talk close, scene change, save/load or audio failure invalidates stale callbacks. Text-only fallback uses a static closed-mouth expression. Missing viseme metadata or an incompatible portrait uses the same fallback. A model outage or cancelled TTS request must never leave a talking mouth or replace the speaker's portrait.

## Runtime integration plan

1. Capture the portrait identity after DFU selects the speaker and native bank/record. Preserve a native fallback texture.
2. Resolve an optional manifest entry and expression layers. Cache by portrait identity, source revision, expression and renderer state. Avoid cache keys based on NPC name alone.
3. Apply the composite to the current Talk portrait panel after native selection. The lowest-risk integration seam is a Harmony postfix on `DaggerfallTalkWindow.SetNPCPortrait`; the method is virtual, but the current DFU `DaggerfallUI` factory creates its native Talk window, so a subclass is not currently installed by People of the Iliac Bay. An alternative is an owned window factory hook if DFU later exposes one. Keep native portrait loading authoritative when the adapter is unavailable.
4. Cancel pending asset/audio work when the selected speaker changes or the Talk window closes. Bind each asynchronous result to the window instance, speaker identity and generation token. Discard late callbacks after cancellation, scene change, reload or destruction.
5. On cache/load/compose error, restore the selected native or existing texture-replacement portrait. Do not show another NPC's cached texture. Re-resolve expression after save/load from stable identity and persisted inputs.

The existing Harmony and reflection UI work in `PeopleOfTheIliacBayMod.Dialogue.cs` provides an integration precedent, not an existing portrait hook. The portrait implementation should own a small adapter and cache rather than alter `TalkManager`'s quest selection or replace native actor identity.

## Validation before implementation claims

- Verify bank and record resolution for mobile, generated adult, child, ordinary static, story static and quest NPC speakers.
- Confirm texture replacements remain selected as expression bases and unknown custom races use an explicit fallback.
- Compare native and composite portrait pixels for identity, palette, crop and filtering; review at native UI scale.
- Switch speakers rapidly, reopen Talk, close during load/playback, cancel audio, unload the scene and reload a save. No stale face, stuck animation or leaked Unity texture may remain.
- Verify a friendly manipulator can smile without signaling trust, a trusted but reserved NPC can remain guarded, and temporary mood changes do not reveal exact hidden values.
- Add TTS playback/viseme acceptance only after a separate TTS request. Confirm mouth timing against real playback, pause closure, drift, interruption, cancellation and text-only fallback.

## Current limits

No expressive portrait assets, portrait manifest, portrait replacement hook, custom-race portrait adapter, child portrait bank, TTS provider or viseme playback exists in People of the Iliac Bay. The native portrait banks and DFU UI hook are verified locally. This plan records a future path; it does not enable TTS, alter game files, establish asset ownership rights, or claim legal review.

