# Native split portrait runtime plan

## Goal

Use reviewed per-face split art in native DFU talk portraits. Keep portrait identity tied to the native face record. Keep facial emotion and blinking independent of text generation and audio playback. Build no gallery-only runtime dependency.

## Verified baseline

- Mobile NPC talk calls `SetNPCPortrait(CommonFaces, targetMobileNPC.PersonFaceRecordId)` (`daggerfall-unity/Assets/Scripts/Game/TalkManager.cs:705-715,783-795`).
- `DaggerfallTalkWindow.SetNPCPortrait()` passes `recordId` directly to `GetDFBitmap(recordId, 0)` (`daggerfall-unity/Assets/Scripts/Game/UserInterfaceWindows/DaggerfallTalkWindow.cs:359-367`). `CifRciFile.GetDFBitmap()` returns `records[record]` after bounds checks (`daggerfall-unity/Assets/Scripts/API/CifRciFile.cs:252-262`). Indices are zero-based.
- `MobilePersonNPC.SetPerson()` selects the record as outfit base plus a face variant (`MobilePersonNPC.cs:29-39,198-224`).
- Approved prototype art has record 77, bank `TFAC00I0.RCI`, and catalog ID `common-077`. Gwen proof allowlists face index 76. Treat these as separate records. Do not copy Gwen's allowlist into the production registry.
- Existing runtime overlay loads one Gwen full-face atlas and has no split-set loader, eye blink, per-face emotion selector, or registry. `Deploy.ps1` copies loose texture `.bytes` and emits those paths into `.dfmod` `Files`; it has no generic portrait manifest path.
- Split manifests already describe `schemaVersion: 2`, `width: 64`, `height: 64`, `splitY: 35`, eye phases and A–H/X mouth paths. These are browser/prototype inputs, not a native runtime API.

## Stages and gates

Parent accepts this implementation sequence within the authorized shared checkout.
Stage0 is now verified for configured common-bank records76–87 (22 October).
Stage1 is implemented and package-verified for the approved77 pilot (22 October).
Stage2 pure selection/composition now passes actual-art acceptance (22 October).
The common77 native TalkWindow pilot now passes an owned no-model/no-audio game
probe (22 October). Stage3 has a typed state projector and independent blink
clock; Stage4's optional full-WAV adapter is implemented but live speech/cue
acceptance is still open. Stage5 has bounded mobile lifecycle evidence, not
exhaustive audio/static-NPC/invalid-source gameplay acceptance.

### Current native pilot evidence, 22 October

`SplitPortraitRuntimeOverlay` binds the current mobile speaker, TalkWindow,
zero-based record, panel and native texture. Its callback runs after binding, so
previous-speaker presentation cannot leak through script update ordering. Whole
registry metadata is validated once; selected assets are decoded per binding and
temporary textures are destroyed. The compositor reuses one output/upload buffer
and texture. Repeated unchanged output skips upload. Unsupported art returns
before state/audio work. `EnableAnimatedPortraits` is independent of voice/LLM/D3.

`NpcPresentationState` samples all detached state at one game time without
creating profiles or recovery anchors. Full-WAV queue callsites save that exact
actor/session/request snapshot for both delivery context and playback expression;
conversation close invalidates the snapshots. Missing identity selects neutral.
The initial shared-state implementation incorrectly used raw resentment; a
regression now requires decayed `ProjectedResentment`. The optional speech source
scopes asynchronous sidecar attempts to request/person/session/clip/generation,
allowing replay while dropping stale results. Streaming cues remain unsupported
in this new renderer and select X; no live audio acceptance is claimed.

The pure visual policy prioritizes fear >=60, resentment >=60, known disposition
or trust <=25, then mood >=25 or <=-25. These are presentation cutoffs, not changes
to relationship rules. Blink uses presentation seconds, ordinary230ms phases,
partial-reopening double blinks and follow-ups, and scheduled15–60s holds.
Half holds last1.5–8s; speech-only closed holds last1.5–3s and release through half
frames when speech ends. Closed holds also enter/release through half frames.

Parent verification:1631/1631 tests, RuntimeCompile0warnings/errors. Actual Unity
build/reopen/install passed. Owned Gothway Garden probe compares324 production
renders across two real mobile actors, preserves native texture objects/pixels,
checks same-face actor revision, record76 fallback/77 return, unchanged-upload
invariance and owned-composite destruction on disable. Production LateUpdate
then shows open/half/closed phases during6.25s without LLM/D3/prewarm/voice.
Original portable settings restore byte-exactly. Installed candidate is70,137,641
bytes, SHA256 `d3278604f99a3bc61c7fdf1ce00f3ece267af02eae1edc0c5ce04a8f67d8fa52`.
Evidence: `work/portrait-runtime-stage3-native-20261022T032251Z`, parent test/compile
logs, current real-art receipt linked by `portrait-runtime-stage3-real-art-final.log`,
and `portrait-runtime-stage3-integration-review.md`. The first wrapper mistakenly
required the package-only success marker for an install build and rolled back;
the actual editor build itself succeeded. DFU gameplay logs use its own
PortableAppdata logger, independently of Unity's -logFile; both logs are retained.

Remaining gates: real full-WAV playback/replay/stop/stale-sidecar integration,
streaming support, actor-bound delivery across all supported conversation paths,
static/special portrait banks, expanded approved art and binding-load profiling.
These results do not complete the broader personality/model/action roadmap.

### 0. Prove source and record identity

Resolve all keys as `(face archive/bank, zero-based record)`. For each pilot face, compare current `TFAC00I0.RCI` decoded pixels for that record with its catalog original, source dimensions, and recorded hash. Keep record 76 and 77 as distinct test cases. Do not enable a portrait entry if current archive, source pixels, or declared record disagree.

**Gate:** automated native-bank fixture proves `common-077` equals `CommonFaces` record 77 and differs from record 76. The approved art status does not substitute for this proof.

**Verified result:** `work/Export-PortraitNativeBank.ps1` invokes the installed
`Assembly-CSharp.dll` decoder directly, with `UseDisk` and readOnly=true.
`DFPalette.Read` loads the776-byte native COL after its8-byte header.
This avoids `FileProxy.LoadMemory` calling engine-only `Resources.Load` outside
Unity. Native `GetColor32(...,0)` rows are flipped once from Unity bottom-up to
PNG top-down RGBA. No TextureReplacement, editor, game or model was started.
The acceptance fixture failed before export and passes afterwards:12 records,
49,152 RGBA pixels, zero mismatches with catalog076–087. Records76/77 differ in
3,092 pixels. Bank, palette, installed decoder and configuration hashes remain
unchanged during export. Evidence: `work/portrait-native-bank-export/manifest.json`,
`work/test_portrait_native_bank_identity.py` and
`work/portrait-native-bank-identity-verification.json`. This proves source identity
for these records, not runtime loading/composition or other face banks.

### 1. Define native manifest and deployment

Create a small runtime manifest separate from the browser catalog. Each entry records archive/bank, zero-based record, source hash, dimensions, `splitY`, supported emotion IDs, eye `open/half/closed` paths, and mouth A–H/X paths. Validate schema, path containment, hash, dimensions, phase/shape completeness, and split before loading.

Package manifest and selected PNG `.bytes` under the mod's loose-file deployment pattern. Add every runtime path to generated `.dfmod` `Files`. Do not add an AssetBundle requirement. Exclude browser previews, voice-set metadata, and galleries from runtime package.

**Gate:** packaging fixture lists every required texture and manifest exactly once; missing, duplicate, out-of-root, wrong-hash, wrong-size, and unsupported-split entries fail closed.

**Verified result:** `art/portraits/mobile-mouths/runtime-selection.json` explicitly
selects the approved77 split source and Stage0 PNG/RGBA identity. The standalone
`tools/BuildPortraitRuntimePackage.ps1` derives a schema1 native registry with
archive/bank, zero-based record, source hashes, fixed dimensions/split, all six
emotions and3/9 eye/mouth references. Each flat uniquely named `.png.bytes` binds
encoded SHA-256 and decoded top-down RGBA SHA-256. Source/selection/manifest hashes
are rechecked before promotion; source overlap and reparse-point escapes fail.
`tools/Deploy.ps1` adds these72 textures and one `.json.bytes` registry to `Files`.
No gallery, audio, raw-generation or preview metadata is included.

`SplitPortraitManifestValidator` rejects malformed identities, unsupported
archive/bank pairs, incomplete/duplicate phases, unsafe/duplicate asset names,
wrong hashes and decoded dimensions. Actual FullSerializer nested arrays pass.
The parent reran1598/1598 tests, the real-PNG package fixture with12 rejected cases,
and RuntimeCompile with zero warnings/errors. An actual Unity VerifyAndPackage
run passes72 texture decodes/hashes, exact asset listing, bundle build and reopen.
First Unity attempt exposed a List<string>-to-string[] editor call mismatch;
the preserved failure log precedes the corrected `info.Files.ToArray()` call.
Batch licensing failure is separate; the owned non-batch run completed successfully.
Evidence: `work/portrait-runtime-stage1-parent-unity.json`, parent test logs,
`portrait-runtime-stage1-package-report.json` and code-review reports. Installed
153b SHA remains `4219f2f1cbb0bfae734803c6218cad23f15378743c7a21c0766f2b38fd1488ad`.
The split registry is packaged but has no renderer yet; no animation activation,
new in-game acceptance or installation is implied.

### 2. Add face selector and compositor

Resolve active mobile actor and `PersonFaceRecordId` from `TalkManager`. Use `(CommonFaces, recordId)` to select an entry. Compose selected 64×35 eye half at y0 and selected 64×29 mouth half at y35 into a reusable 64×64 talk portrait. Preserve point filtering and per-face mask/source boundaries. Never reuse another face's geometry or pixels.

If requested emotion is absent, use that same face's neutral set. If face record, source, manifest, or neutral assets are missing or invalid, restore full native portrait. Do not require a stable NPC allowlist when face art is record-addressed.

**Gate:** pure tests cover record 76/77 resolution, six emotion lookups, neutral fallback, invalid/unknown face fallback, pixel dimensions, split placement, and unchanged pixels outside approved feature masks.

**Verified foundation:** `SplitPortraitComposition.TryCreateFace` takes the full
registry, requested archive/bank/zero-based record, current native top-down RGBA,
and selected decoded PNG assets. It verifies the entire registry's metadata using
resolver-free `SplitPortraitManifestValidator.TryValidateStructure`, then checks
only the selected face's encoded/decoded hashes and freezes its RGBA planes once.
This rejects malformed unselected metadata without eagerly decoding every face.
`SplitPortraitFace.TryCompose` accepts typed eye/mouth indices and reuses a
caller-owned64x64 buffer. Unknown emotion selects the same face's neutral set;
X retains the selected eye phase and emotion. No per-compose allocation or asset
resolution appears in the production path; this is source inspection, not a GPU
frame-time measurement.

Parent verification passes1605/1605 tests and RuntimeCompile with zero warnings/
errors. `tools/TestSplitPortraitComposition.ps1` invokes the compiled production
core through reflection and actual FullSerializer, decodes the72 approved77 PNGs,
and uses direct native77/76 exported RGBA as identity input. All162 supported paired
emotion/eye/cue combinations match their actual halves byte-for-byte;530,550
outside-mask pixel comparisons have zero mismatches. Invalid identities, sources,
assets and enum/destination inputs fail. The final post-deployment receipt is bound
through `work/portrait-runtime-stage2-parent-real-art-final.log`; prior failed
PowerShell wrapper attempts remain separate evidence. Typed List return enumeration
needed a unary comma; no production validation was weakened to fix the wrapper.
The updated package also builds/reopens in actual Unity, with820 manifest entries
and599 matching deployed C# sources; installed153b remains unchanged.

That pure fixture alone did not resolve a live TalkManager actor, create/reuse a
Unity composite texture or take ownership of a TalkWindow panel. The later native
pilot above adds those bounded common77 adapter/lifecycle checks.

### 3. Add structured emotion and independent blink

Define a typed emotion input from existing structured NPC state or an immutable current actor snapshot. Map only documented values to supported portrait emotion IDs. Do not infer emotion from dialogue/TTS prose, regex, LLM, or D3. If structured input is absent or unsupported, select neutral.

Run blink from an independent animation clock. Keep blink active while idle, speaking, or audio is unavailable. Keep selected mouth emotion and eye emotion paired unless a later explicit product decision separates them. Stop/reset blink on renderer detach, actor change, face change, or talk close.

**Gate:** tests feed structured values and absent/invalid snapshots; assert deterministic emotion selection and independent blink timing without dialogue, TTS, LLM, D3, social-memory state, or model server.

**Implementation preparation:** `work/portrait-runtime-stage3-state-review.md`
locates detached native mood, directed trust/fear/resentment and acute-fear reads,
plus the existing Talk actor resolver. Sample the owner game clock once for state
projection; blink/voice playback uses presentation time. Do not mutate recovery
anchors or create personality/relationship rows from portrait polling. Missing
stable social identity means neutral expression, not loss of valid record-addressed
portrait animation. Native fallback is for invalid/missing face data or detachment.

The preparation review found that TTS delivery context required managed conversationActor and
memoryPersonId; static and no-model paths can receive null context. It also samples
state at queue time while a live face could read later state. Integrate a shared
actor/utterance-owned presentation snapshot so face emotion and speech direction
use the same authoritative values. Do not inherit the old memoryWindow dependency
or assume contrast-only controlledDirection is a production emotion projector.
The later shared capture and full-WAV queue integration above address that gap.
Actual live speech/cue acceptance and streaming parity remain open.

### 4. Connect optional speech cues

Consume the active audio owner's sample cursor and validated Rhubarb timeline when available. Preserve request/audio/person ownership checks against stale sidecars, but do not make the portrait renderer depend on `memoryWindow` or a social conversation session. TTS and speech cues remain optional; idle emotion and blinking work without them.

Map A–H to mouth frames from the currently selected emotion. Cue `X` selects that emotion's closed-mouth frame and leaves eye emotion and blink phase unchanged. Missing or invalid cues select current-emotion `X`, or use a separately tested amplitude fallback. Restore native portrait only when detaching or when face/assets are missing or invalid.

**Gate:** test all nine cues, X during open/half/closed eye phases, audio pause/stop/end, invalid/missing timeline, speaker switch, and stale timeline after owner switch. Verify X never resets eyes or changes emotion.

### 5. Integrate ownership and texture lifecycle

Keep `DaggerfallTalkWindow` as owner of native source portrait. Renderer owns only loaded atlas/half textures and its composite. Detach and restore current native portrait before actor/window switch; destroy owned textures only after they are no longer assigned. Avoid repeated pixel readback, allocations, and texture uploads when selected output is unchanged. Decide eager versus lazy loading from measured asset memory and update cost.

**Gate:** bounded Unity probe covers open/close, actor and face switch, missing/invalid face, audio stop, stale async cue, renderer disable, re-enable, and repeated identical frame. Confirm no source texture is destroyed or overwritten and all renderer-owned textures are released.

## Definition of ready

Native integration begins only after Stage 0 passes and the parent approves this plan. Shipping requires all gates above, a package manifest with verified source mappings, stable native fallback, correct selected-emotion X behavior, and a no-LLM/no-D3 idle animation probe. Art review, browser playback, and sample audio do not prove native installation or runtime ownership.
