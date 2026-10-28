"""Read source and pilot evidence; write this scope's report without changing images."""
from collections import Counter
from hashlib import sha256
import json
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
ROOT = REPO.parent


def file_hash(path):
    return sha256(path.read_bytes()).hexdigest()


def image_evidence(path):
    with Image.open(path) as source:
        image = source.convert('RGBA')
        pixels = list(image.getdata())
        alpha = Counter(pixel[3] for pixel in pixels)
        return {
            'path': path.relative_to(REPO).as_posix(),
            'fileSha256': file_hash(path),
            'dimensions': list(image.size),
            'opaquePixelCount': alpha[255],
            'transparentPixelCount': alpha[0],
            'partialAlphaPixelCount': sum(count for value, count in alpha.items() if 0 < value < 255),
            'mostCommonAlphaValues': alpha.most_common(10),
            'distinctFullyOpaqueRgbColors': len({pixel[:3] for pixel in pixels if pixel[3] == 255}),
        }


inventory_path = HERE.parent / 'source-inventory.json'
inventory = json.loads(inventory_path.read_text(encoding='utf-8'))
identities = [row for row in inventory['identities'] if row['kind'] == 'stationary']
reference = REPO / 'art/standing/keepers/prototypes/shopkeeper-182-0-native-reference-nn8x.png'
v1 = HERE / 'flat-182-0-walk-left-pilot-v1.png'
v2 = HERE / 'flat-182-0-walk-left-pilot-v2-alpha-repair.png'
with Image.open(v1) as first, Image.open(v2) as second:
    a, b = list(first.convert('RGBA').getdata()), list(second.convert('RGBA').getdata())
    changed_rgb = sum(x[:3] != y[:3] for x, y in zip(a, b))
    changed_shared_foreground = sum(x[:3] != y[:3] for x, y in zip(a, b) if x[3] >= 128 and y[3] >= 128)

report = {
    'schemaVersion': 1,
    'date': '2026-10-28',
    'status': 'pilot-unaccepted-not-production',
    'sourceInventory': {'path': '../source-inventory.json', 'fileSha256': file_hash(inventory_path)},
    'scope': {
        'knownStationarySourceIdentities': len(identities),
        'knownStationaryIdentities': len(identities),
        'populationMeaning': 'Provider-qualified source catalogue entries including special and unclassified records; not an exact count of eligible humanoid NPC appearances.',
        'applicabilityCounts': dict(sorted(Counter(row.get('applicability', 'not-classified') for row in identities).items())),
        'sourceEntriesAfterEditorMarkerExclusion': sum(row.get('applicability') != 'excluded-editor-marker-not-npc' for row in identities),
        'visuallyReviewedHumanoidSourceEntries': sum(row.get('applicability') == 'humanoid-source-visually-reviewed' for row in identities),
        'providerCounts': dict(sorted(Counter(row['provider'] for row in identities).items())),
        'actions': ['walk', 'run'],
        'views': ['front', 'front-left', 'left-profile', 'rear-left', 'back', 'rear-right', 'right-profile', 'front-right'],
        'catalogueActionViewSlotsBeforeEligibility': len(identities) * 16,
        'requiredKnownActionViewSlots': None,
        'acceptedNewActionViewSlots': 0,
        'remainingKnownActionViewSlots': None,
        'generatedCandidateActionViewSlots': 1,
        'candidateFrames': 4,
        'candidateIdentity': 'vanilla:stationary:182:0',
        'candidateAction': 'walk',
        'candidateView': 'left-profile',
        'completenessLimit': 'The stationary count is the current known inventory, not proven universal mod coverage. Additional billboard identities and source applicability remain under the parent inventory audit. Creature/marker/nonhuman eligibility is not inferred from record count.',
    },
    'source': {
        'nativeArchive': {'path': '../GameFiles/arena2/TEXTURE.182', 'fileSha256': file_hash(ROOT / 'GameFiles/arena2/TEXTURE.182')},
        'reference': image_evidence(reference),
        'referenceStatus': 'Existing nearest-neighbor x8 native 182/0 reference; 47x76 logical extent. Source pixels are not a generated keeper prototype.',
        'promptDimensionCorrection': 'The historical prompt described a 38x77 silhouette; reference file is 376x608 (47x76 at x8). No native-size compliance is claimed.',
    },
    'tool': 'built-in image_gen',
    'pilots': [
        {'version': 1, 'promptFile': 'prompt-v1.txt', 'status': 'review-candidate-not-accepted', 'evidence': image_evidence(v1), 'generatedSource': 'C:/Users/Daniel/.codex/generated_images/01a1807c-c563-7922-9e98-f606d781bfb8/exec-6f10c45d-ebe7-4d99-9a91-adfbfac77224.png'},
        {'version': 2, 'promptFile': 'prompt-v2-alpha-repair.txt', 'status': 'alpha-only-edit-failed', 'evidence': image_evidence(v2), 'generatedSource': 'C:/Users/Daniel/.codex/generated_images/01a1807c-c563-7922-9e98-f606d781bfb8/exec-46824e84-7692-4952-a65a-5b354afd0ff7.png'},
    ],
    'repairComparison': {'rgbChangedPixels': changed_rgb, 'rgbChangedSharedForegroundPixels': changed_shared_foreground},
    'visualReview': {
        'reviewedLocalFiles': [v1.name, v2.name, reference.relative_to(REPO).as_posix()],
        'method': 'Built-in generation preview and local view_image; actual PNG alpha/RGB analysis independently checked.',
        'positive': 'Four complete bodies, left-facing view, visible opposing arm movement at contact phases, two narrower passing silhouettes.',
        'unresolved': ['Initial strip leg ownership is not securely demonstrated; subsequent individual keyframe cycle is recorded separately.', 'Head, torso proportions, coarse native pixel structure and palette are not preserved exactly.', 'This historical initial strip covers only left walking; current eight-view walk/run candidate coverage is recorded in directionalWalk/directionalRun.', 'No gameplay integration acceptance.'],
        'failedRepair': 'The alpha-only edit changes substantial foreground RGB and leaves extensive partial alpha. It cannot be called an alpha-only fix.',
        'alphaRuntimeContract': 'Existing billboard rendering uses a0.5 alpha cutoff. Body alpha252/253 is not itself a rejection reason; source fidelity, contour at cutoff, anatomy and temporal consistency are the material criteria.',
        'correctedObservation': 'The local view_image rendering looked nearly black, but this is not proof of transparent bodies: dominant body alpha is252/253, not near zero. The original alpha-repair prompt contains the earlier mistaken interpretation; retain it as exact historical prompt, not fact.',
    },
    'existingArtAssessment': {
        'walk': 'art/walk/rejected/vanilla-182-0-walk-v1-rejected.json explicitly rejects the older cycle for repeated leading limbs and missing passing poses.',
        'run': 'Existing art/run is mobile385 evidence; it is not stationary coverage.',
        'keepers': 'art/standing/keepers contains standing/offer/wiping prototypes. Neither stationary poses nor mirrored views establish gait animation.',
    },
    'changes': 'Only this own production folder. Shared manifests, queues, runtime code, original sources and existing assets unchanged.',
}
report['keyframePilots'] = [
    {
        'version': 1,
        'promptFile': 'prompt-keyframe-v1.txt',
        'evidence': image_evidence(HERE / 'flat-182-0-walk-left-passing-keyframe-v1.png'),
        'generatedSource': 'C:/Users/Daniel/.codex/generated_images/01a1807c-c563-7922-9e98-f606d781bfb8/exec-f6f2aace-d50a-4e9e-8a0d-30b41bdbb662.png',
        'status': 'passing-pose-not-met',
        'visualReview': 'Lifted boot remains behind supporting leg, and visible arm remains behind hip. Requested forward passing pose was not achieved.',
    },
    {
        'version': 2,
        'promptFile': 'prompt-keyframe-v2.txt',
        'evidence': image_evidence(HERE / 'flat-182-0-walk-left-passing-keyframe-v2.png'),
        'generatedSource': 'C:/Users/Daniel/.codex/generated_images/01a1807c-c563-7922-9e98-f606d781bfb8/exec-ad94c847-dc67-44a0-9df3-13303f05e6d3.png',
        'status': 'isolated-passing-keyframe-review-candidate',
        'visualReview': 'The bent knee and lifted boot now pass in front, to image-left of the supporting leg. Near arm hangs more vertically. This meets the requested isolated pose more closely; supporting foot and head pixel placement shift, so temporal stability and native fidelity are not established. No complete cycle is claimed.',
    },
]
cycle_path = HERE / 'cycle-review.json'
if cycle_path.exists():
    report['assembledCycle'] = json.loads(cycle_path.read_text(encoding='utf-8'))
    report['assembledCycle']['evidenceFiles'] = {
        name: file_hash(HERE / name)
        for name in ['cycle-review-atlas-native.png', 'cycle-review-sheet-4x.png', 'cycle-review-4fps.gif']
    }
    report['assembledCycle']['visualReview'] = {
        'reviewed': 'Native-scale sheet at4x inspected locally with view_image. A4fps GIF is delivered for actual playback; no user playback approval is claimed.',
        'observed': 'Four distinct authored phases; alternating near-arm contact positions; near-support and far-support passing poses show distinct foreground leg occlusion. Head and strict left-profile camera remain stable in the assembled native-sized sheet. Body height76–77pixels; baseline variation at most1pixel.',
        'remaining': 'Trousers are more saturated red than the subdued rusty source. Contact-phase near/far thigh depth remains weaker than the passing poses and needs visual review. Production/native gameplay acceptance remains open.',
        'status': 'first-complete-left-walk-review-candidate',
    }
walk_path = HERE / 'walk-182-0/walk-review-manifest.json'
if walk_path.exists():
    walk = json.loads(walk_path.read_text(encoding='utf-8'))
    report['status'] = 'directional-walk-review-candidate-running-in-progress'
    report['scope']['generatedCandidateActionViewSlots'] = len(walk['completeCandidateViews'])
    report['scope']['candidateFrames'] = walk['candidateFrameCount']
    report['scope']['candidateViews'] = walk['completeCandidateViews']
    report['scope'].pop('candidateView', None)
    report['directionalWalk'] = {
        'manifest': 'walk-182-0/walk-review-manifest.json', 'manifestSha256': file_hash(walk_path),
        'verification': 'walk-182-0/walk-verification.json',
        'sheet': 'walk-182-0/walk-eight-view-review-sheet-2x.png',
        'animation': 'walk-182-0/walk-eight-view-review.gif',
        'status': 'eight-body-views-four-phases-review-candidate-not-production',
        'review': 'Complete sheet reviewed: distinguishable body viewpoints and corrected contact-arm changes. Quarter heads remain profile-like, source trouser hue differs, and limb/pouch depth remains review-limited.',
    }
    report['source']['directNativeOriginal'] = walk['source']
run_path = HERE / 'run-182-0/run-review-manifest.json'
if run_path.exists():
    run = json.loads(run_path.read_text(encoding='utf-8'))
    report['scope']['generatedCandidateActionViewSlots'] += len(run['candidateViews'])
    report['scope']['candidateFrames'] += run['candidateFrameCount']
    report['scope']['candidateActions'] = ['walk', 'run']
    report['scope'].pop('candidateAction', None)
    report['scope']['candidateViewsByAction'] = {'walk': walk['completeCandidateViews'], 'run': run['candidateViews']}
    report['directionalRun'] = {
        'manifest': 'run-182-0/run-review-manifest.json', 'manifestSha256': file_hash(run_path),
        'verification': 'run-182-0/run-verification.json',
        'sheet': 'run-182-0/run-eight-view-review-sheet-2x.png',
        'animation': 'run-182-0/run-eight-view-review.gif',
        'candidateViews': run['candidateViews'], 'missingViews': run['missingViews'],
        'status': run['status'],
        'review': 'Eight body bearings have selected four-phase motion candidates after individual phase and camera corrections. Contact baselines registered by rigid translation, with a disclosed -3px rear-right phase3 flight registration. Front/front-left contact body heights differ6pixels; rear-right differs4pixels. Pouch-side, palette, texture and remaining limb-depth fidelity remain open.',
        'rearLeftCameraCorrection': 'Parent found yaw between phases0/3 and1/2. Bounded camera-match-v4 edits now align1/2 to0 while keeping opposite arm phases; local rear-left-camera-review-4x.png inspected. Earlier cells remain unselected.',
    }
    if len(run['candidateViews']) == 8:
        report['status'] = 'one-identity-walk-and-run-eight-view-review-candidates-not-production'
families = []
for family_path in sorted(HERE.glob('family-*/manifest.json')):
    family = json.loads(family_path.read_text(encoding='utf-8'))
    family_frames = sum(action['candidateFrames'] for action in family['actions'].values())
    family_slots = sum(len(action['candidateViews']) for action in family['actions'].values())
    report['status'] = 'multiple-source-families-locomotion-review-candidates-in-progress-not-production'
    report['scope']['countsReferTo'] = 'Original182/0 pilot only; see additionalFamilies and combinedCoverage for later source families.'
    families.append({
        'manifest': family_path.relative_to(HERE).as_posix(), 'manifestSha256': file_hash(family_path),
        'sourceIdentities': family['aliases'], 'uniqueAppearances': 1,
        'candidateViewsByAction': {action: data['candidateViews'] for action, data in family['actions'].items()},
        'candidateAuthoredFrames': family_frames, 'candidateActionViews': family_slots,
        'verification': (family_path.parent.relative_to(HERE) / 'verification.json').as_posix(), 'acceptedProductionFrames': 0,
    })
if families:
    report['additionalFamilies'] = families
    report['combinedCoverage'] = {
        'uniqueSourceAppearances': 1 + sum(family['candidateAuthoredFrames'] > 0 for family in families),
        'providerQualifiedSourceIdentitiesWithCandidates': 1 + sum(len(family['sourceIdentities']) for family in families if family['candidateAuthoredFrames'] > 0),
        'candidateAuthoredFrames': report['scope']['candidateFrames'] + sum(family['candidateAuthoredFrames'] for family in families),
        'candidateAuthoredActionViews': report['scope']['generatedCandidateActionViewSlots'] + sum(family['candidateActionViews'] for family in families),
        'providerQualifiedCandidateFrameSlots': report['scope']['candidateFrames'] + sum(family['candidateAuthoredFrames'] * len(family['sourceIdentities']) for family in families),
        'acceptedProductionFrames': 0,
        'limitation': 'Exact original aliases retain distinct provider identities. Each equivalent family reuses one authored appearance, rather than counting independent generated sets. No population-wide production pass is claimed.',
    }
(HERE / 'manifest.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'status': report['status'], 'stationarySourceCatalogueEntries': len(identities), 'catalogueSlotsBeforeEligibility': len(identities) * 16, 'v1Sha256': file_hash(v1), 'v2Sha256': file_hash(v2)}))
