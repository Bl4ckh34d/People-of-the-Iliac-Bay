import {createBlink, sampleBlink, nextBlinkDelay} from './blink-policy.mjs';
import {createEyeHold, sampleEyeHold, nextEyeHoldDelay} from './eye-hold-policy.mjs';
import {sharedVoiceManifestUrl} from './shared-voice-policy.mjs?v=portrait-seed-test-1';
import {blendEyeEdgePixels} from './eye-edge-blend-policy.mjs?v=1';
import {createIdleThinkingState, advanceIdleThinking} from './idle-thinking-policy.mjs';
import {
  THINKING_DIRECTIONS,
  advanceThinkingGaze,
  createThinkingGazeState,
  resolveThinkingEyeFrameForEmotion,
  selectThinkingGaze,
  validateThinkingEyeSetsByEmotion,
} from './thinking-gaze-policy.mjs';

const $ = (id) => document.getElementById(id);
const audio = $('speech');
const loopControl = $('loop');
const playButton = $('play');
const mouthEmotionControl = $('mouth-emotionoverride') || $('mouth-emotion');
const catalogUrl = new URL('./portrait-catalog.json', import.meta.url);
const legacyTimingUrl = new URL('../sample.visemes.json', import.meta.url);
const legacyAudioUrl = new URL('../sample.wav', import.meta.url);
const phaseNames = ['open', 'half', 'closed'];
const emotionNames = {
  neutral: 'Neutral', happy: 'Fröhlich', angry: 'Wütend',
  sad: 'Traurig', afraid: 'Ängstlich', guarded: 'Skeptisch',
};

let catalog;
let selectedEntry;
let selectedManifest;
let selectedManifestUrl;
let timeline;
let currentOriginal;
let eyeSets = new Map();
let thinkingEyeSetsByEmotion = new Map();
let thinkingGazeState = createThinkingGazeState();
let idleThinkingState = createIdleThinkingState();
let renderedThinkingDirection = '';
let thinkingGazeNotice = '';
let loadedStatus = '';
let mouthSets = new Map();
let audioSets = new Map();
let defaultAudioBundle = null;
let activeAudioUrl = null;
let activeAudioEmotion = '';
let programmaticPlayRequests = [];
let eyeEmotion = 'neutral';
let mouthOverride = null;
let eyeMode = 'auto';
let mouthShape = 'X';
let cycling = false;
let cycleStart = 0;
let animationReady = false;
let portraitEpoch = 0;
let audioEpoch = 0;
let playbackCommandEpoch = 0;
let playbackWanted = false;
let ignoredPauseEvents = 0;
let audioMetadataHandler = null;
let frameLoopStarted = false;
const eyeEdgeBlendCache = new Map();

let activeBlink = null;
let activeHold = null;
let queuedEyeEvents = [];
let nextBlinkStart = 0;
let nextHoldStart = 0;
let lastRenderedPhase = 'open';
let phaseTransition = null;

async function image(src, width, height) {
  // Portrait repairs replace bytes at stable URLs. Do not reuse old browser pixels.
  const response = await fetch(src, {cache: 'no-store'});
  if (!response.ok) throw Error(`Datei fehlt (${response.status}): ${src}`);
  const objectUrl = URL.createObjectURL(await response.blob());
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => {
      URL.revokeObjectURL(objectUrl);
      if (img.naturalWidth !== width || img.naturalHeight !== height) {
        reject(Error(`Bildgröße falsch (${img.naturalWidth}×${img.naturalHeight}, erwartet ${width}×${height}): ${src}`));
        return;
      }
      resolve(img);
    };
    img.onerror = () => {
      URL.revokeObjectURL(objectUrl);
      reject(Error(`Bild kann nicht geladen werden: ${src}`));
    };
    img.src = objectUrl;
  });
}

async function json(src) {
  const response = await fetch(src, {cache: 'no-store'});
  if (!response.ok) throw Error(`Datei fehlt (${response.status}): ${src}`);
  return response.json();
}

function readNativeEyePixels(imageLike) {
  const canvas = document.createElement('canvas');
  canvas.width = 64;
  canvas.height = 35;
  const context = canvas.getContext('2d', {willReadFrequently: true});
  context.drawImage(imageLike, 0, 0, 64, 35, 0, 0, 64, 35);
  const pixels = context.getImageData(0, 0, 64, 35);
  return {width: 64, height: 35, data: pixels.data};
}

function prepareEyeFrame(imageLike, imageIdentity, manifest, originalPortrait, portraitId) {
  const profile = manifest.eyeEdgeBlend;
  if (profile == null) return imageLike;
  const regions = manifest.eyeRegions;
  const originalIdentity = originalPortrait.currentSrc || originalPortrait.src || portraitId;
  const profileKey = JSON.stringify([portraitId, imageIdentity, originalIdentity, profile, regions]);
  if (eyeEdgeBlendCache.has(profileKey)) return eyeEdgeBlendCache.get(profileKey);

  const originalKey = JSON.stringify([portraitId, 'original-upper', originalIdentity]);
  let originalPixels = eyeEdgeBlendCache.get(originalKey);
  if (!originalPixels) {
    originalPixels = readNativeEyePixels(originalPortrait);
    eyeEdgeBlendCache.set(originalKey, originalPixels);
  }
  const blended = blendEyeEdgePixels(readNativeEyePixels(imageLike), originalPixels, regions, profile);
  const canvas = document.createElement('canvas');
  canvas.width = 64;
  canvas.height = 35;
  const context = canvas.getContext('2d');
  const imageData = context.createImageData(64, 35);
  imageData.data.set(blended.data);
  context.putImageData(imageData, 0, 0);
  eyeEdgeBlendCache.set(profileKey, canvas);
  return canvas;
}

function draw(id, img) {
  const canvas = $(id);
  const context = canvas.getContext('2d');
  context.imageSmoothingEnabled = false;
  context.clearRect(0, 0, canvas.width, canvas.height);
  if (img) context.drawImage(img, 0, 0);
}

function drawUnavailablePortrait(original, statusText) {
  animationReady = false;
  if (original) {
    draw('original', original);
    draw('live', original);
  } else {
    for (const id of ['original', 'live']) draw(id, null);
  }
  setControlsEnabled(false);
  setStatus(statusText);
  updateLiveData('original', 'X', 'disabled');
}

function setStatus(message, portraitMessage = message) {
  $('status').textContent = message;
  $('portrait-status').textContent = portraitMessage;
}

function setPlayLabel() {
  playButton.textContent = playbackWanted ? 'Sprache pausieren' : 'Sprache abspielen';
}

function pauseAudioPreservingIntent() {
  if (!audio.paused) {
    ignoredPauseEvents += 1;
    audio.pause();
  }
}

function clearAudioForPortrait() {
  audioEpoch += 1;
  playbackCommandEpoch += 1;
  playbackWanted = false;
  activeAudioUrl = null;
  activeAudioEmotion = '';
  timeline = null;
  programmaticPlayRequests = [];
  if (audioMetadataHandler) {
    audio.removeEventListener('loadedmetadata', audioMetadataHandler);
    audioMetadataHandler = null;
  }
  pauseAudioPreservingIntent();
  audio.removeAttribute('src');
  audio.load();
  setPlayLabel();
}

function stopPlaybackForManualAction() {
  playbackWanted = false;
  playbackCommandEpoch += 1;
  pauseAudioPreservingIntent();
  setPlayLabel();
}

async function requestPlayback(expectedAudioEpoch = audioEpoch) {
  if (!playbackWanted || expectedAudioEpoch !== audioEpoch || !activeAudioUrl) return;
  const commandEpoch = ++playbackCommandEpoch;
  const expectedUrl = activeAudioUrl.href;
  const request = {audioEpoch: expectedAudioEpoch, commandEpoch, url: expectedUrl};
  if (audio.paused) programmaticPlayRequests.push(request);
  try {
    await audio.play();
    if (expectedAudioEpoch !== audioEpoch || commandEpoch !== playbackCommandEpoch || expectedUrl !== activeAudioUrl?.href || !playbackWanted) {
      if (expectedAudioEpoch === audioEpoch && !playbackWanted && !audio.paused) pauseAudioPreservingIntent();
      return;
    }
  } catch (error) {
    programmaticPlayRequests = programmaticPlayRequests.filter((item) => item !== request);
    if (expectedAudioEpoch !== audioEpoch || commandEpoch !== playbackCommandEpoch || !playbackWanted) return;
    playbackWanted = false;
    playbackCommandEpoch += 1;
    setStatus(`Audio konnte nicht abgespielt werden: ${error.message}`, 'Audiofehler');
  }
  setPlayLabel();
}

function bindAudioBundle(bundle) {
  if (!bundle) throw Error('Audio-/Viseme-Bundle fehlt.');
  const nextEpoch = ++audioEpoch;
  playbackCommandEpoch += 1;
  pauseAudioPreservingIntent();
  programmaticPlayRequests = [];
  timeline = bundle.timeline;
  activeAudioUrl = bundle.audioUrl;
  activeAudioEmotion = bundle.id;
  audio.src = activeAudioUrl.href;
  audio.loop = loopControl.checked;
  if (audioMetadataHandler) audio.removeEventListener('loadedmetadata', audioMetadataHandler);
  audioMetadataHandler = () => {
    audioMetadataHandler = null;
    if (nextEpoch !== audioEpoch || audio.currentSrc !== activeAudioUrl?.href) return;
    try { audio.currentTime = 0; } catch { /* Media metadata can become unavailable during a source change. */ }
  };
  audio.addEventListener('loadedmetadata', audioMetadataHandler, {once: true});
  audio.load();
  try { audio.currentTime = 0; } catch { /* New media may not expose a seek position until metadata loads. */ }
  setPlayLabel();
  if (playbackWanted) void requestPlayback(nextEpoch);
}

function setControlsEnabled(enabled) {
  for (const button of $('emotions').querySelectorAll('button')) button.disabled = !enabled;
  for (const id of ['eyes', 'thinking-gaze', 'mouth', 'blink', 'double-blink', 'hold-test', 'cycle', 'rest', 'mouth-emotionoverride', 'mouth-emotion', 'play', 'loop']) {
    const control = $(id);
    if (control) control.disabled = !enabled;
  }
  audio.controls = enabled;
  updateThinkingGazeControl(enabled);
  if (!enabled) clearAudioForPortrait();
  setPlayLabel();
}

function updateLiveData(phase, shape, eyeAnimation) {
  Object.assign($('live').dataset, {
    portrait: selectedEntry?.id ?? '',
    emotion: eyeEmotion,
    mouthEmotion: getMouthEmotionId(),
    eyes: phase,
    mouth: shape,
    eyeAnimation,
    audioEmotion: activeAudioEmotion,
    audioHash: timeline?.provenance?.audioSha256 ?? '',
    thinkingGaze: renderedThinkingDirection,
    thinkingGazeMode: $('thinking-gaze').value,
  });
}

function getMouthEmotionId() {
  if (mouthOverride) return mouthOverride;
  return eyeEmotion;
}

function currentMouthSet() {
  return mouthSets.get(getMouthEmotionId()) || mouthSets.get('neutral') || mouthSets.values().next().value;
}

function currentEyeSet() {
  return eyeSets.get(eyeEmotion) || eyeSets.values().next().value;
}

function currentThinkingEyeSets() {
  return thinkingEyeSetsByEmotion.get(eyeEmotion) || new Map();
}

function currentThinkingAvailabilityNotice() {
  if (currentThinkingEyeSets().size === THINKING_DIRECTIONS.length) return thinkingGazeNotice;
  const label = eyeSets.get(eyeEmotion)?.label ?? eyeEmotion;
  return `Nachdenkblick für ${label} nicht verfügbar; normale Ausdrucksaugen werden verwendet.`;
}

function updateThinkingGazeControl(enabled = animationReady) {
  const control = $('thinking-gaze');
  if (control) control.disabled = !enabled || currentThinkingEyeSets().size !== THINKING_DIRECTIONS.length;
}

function updateEmotionButtons() {
  for (const button of $('emotions').querySelectorAll('button')) {
    button.setAttribute('aria-pressed', String(button.dataset.emotion === eyeEmotion));
  }
}

function populateMouthShapes() {
  const control = $('mouth');
  const set = currentMouthSet();
  if (!set) {
    control.replaceChildren();
    return;
  }
  const oldShape = mouthShape;
  control.replaceChildren();
  for (const frame of set.frames) {
    const option = document.createElement('option');
    option.value = frame.shape;
    option.textContent = frame.shape === 'X' ? 'X – Ruhe' : frame.shape;
    control.append(option);
  }
  mouthShape = set.frames.some((frame) => frame.shape === oldShape)
    ? oldShape
    : set.frames.some((frame) => frame.shape === 'X') ? 'X' : set.frames[0]?.shape ?? 'X';
  control.value = mouthShape;
}

function populateEmotions() {
  const host = $('emotions');
  host.replaceChildren();
  for (const set of eyeSets.values()) {
    const button = document.createElement('button');
    button.textContent = emotionNames[set.id] || set.label;
    button.dataset.emotion = set.id;
    button.setAttribute('aria-pressed', String(set.id === eyeEmotion));
    button.addEventListener('click', () => {
      const emotionChanged = eyeEmotion !== set.id;
      eyeEmotion = set.id;
      if (emotionChanged) idleThinkingState = createIdleThinkingState();
      updateEmotionButtons();
      populateMouthShapes();
      updateThinkingGazeControl();
      if (emotionChanged) {
        const notice = currentThinkingAvailabilityNotice();
        $('status').textContent = notice || loadedStatus;
      }
      if (emotionChanged && audioSets.size) bindAudioBundle(audioSets.get(set.id));
    });
    host.append(button);
  }

  if (mouthEmotionControl) {
    mouthEmotionControl.replaceChildren();
    const followEyes = document.createElement('option');
    followEyes.value = 'same-as-eyes';
    followEyes.textContent = 'Wie Augen';
    mouthEmotionControl.append(followEyes);
    for (const set of mouthSets.values()) {
      const option = document.createElement('option');
      option.value = set.id;
      option.textContent = emotionNames[set.id] || set.label;
      mouthEmotionControl.append(option);
    }
    mouthEmotionControl.value = mouthOverride ?? 'same-as-eyes';
  }
  populateMouthShapes();
}

function drawComposed(eyeFrame, mouthFrame) {
  const context = $('live').getContext('2d');
  context.imageSmoothingEnabled = false;
  context.clearRect(0, 0, 64, 64);
  context.drawImage(eyeFrame.image, 0, 0);
  context.drawImage(mouthFrame.image, 0, 35);
}

function cueShape() {
  if (audio.paused || audio.ended || !timeline?.audioSampleRate || !Array.isArray(timeline.cues)) return null;
  const sample = Math.floor(audio.currentTime * timeline.audioSampleRate);
  return timeline.cues.find((cue) => sample >= cue.startSample && sample < cue.endSample)?.shape ?? 'X';
}

function getMouthFrame(now) {
  const set = currentMouthSet();
  if (!set) return null;
  const audioShape = cueShape();
  let wantedShape = audioShape;
  if (wantedShape === null && cycling) {
    const frameIndex = Math.floor((now - cycleStart) / 650) % set.frames.length;
    wantedShape = set.frames[frameIndex]?.shape;
  }
  if (wantedShape === null || wantedShape === undefined) wantedShape = mouthShape;
  const selected = set.frameMap.get(wantedShape) || set.frameMap.get('X') || set.frames[0];
  return {frame: selected, shape: selected?.shape ?? 'X', emotion: set.id};
}

function startBlink(now, forceDouble = false) {
  activeBlink = createBlink(now, Math.random, {forceDouble});
  phaseTransition = null;
}

function startHold(now, speaking, forced = false) {
  activeHold = forced
    ? createEyeHold(now, speaking, () => 0)
    : createEyeHold(now, speaking);
  nextHoldStart = now + nextEyeHoldDelay(speaking);
  phaseTransition = null;
}

function sampleActiveEvent(now) {
  if (activeBlink) {
    const sample = sampleBlink(activeBlink, now);
    if (!sample.completed) return {phase: sample.phase, animation: 'blink'};
    nextBlinkStart = now + nextBlinkDelay(activeBlink);
    activeBlink = null;
  }
  if (activeHold) {
    const sample = sampleEyeHold(activeHold, now);
    const speaking = !audio.paused && !audio.ended;
    const containsLongClose = activeHold.segments.some((item) => item.phase === 'closed');
    if (!speaking && containsLongClose && !sample.completed) {
      activeHold = {
        start: now,
        end: now + 65,
        phase: 'half',
        segments: [{phase: 'half', start: now, end: now + 65}],
      };
      return {phase: 'half', animation: 'hold-release'};
    }
    if (!sample.completed) return {phase: sample.phase, animation: 'hold'};
    activeHold = null;
  }
  return null;
}

function smoothManualPhase(target, now) {
  if (phaseTransition) {
    if (phaseTransition.target === target && now < phaseTransition.until) return 'half';
    if (now >= phaseTransition.until) phaseTransition = null;
  }
  const jumpsBetweenOpenAndClosed = (lastRenderedPhase === 'open' && target === 'closed')
    || (lastRenderedPhase === 'closed' && target === 'open');
  if (jumpsBetweenOpenAndClosed) {
    phaseTransition = {target, until: now + 65};
    return 'half';
  }
  return target;
}

function resolveEyePhase(now) {
  const active = sampleActiveEvent(now);
  if (active) return active;

  if (queuedEyeEvents.length) {
    const event = queuedEyeEvents.shift();
    if (event.type === 'hold') startHold(now, !audio.paused && !audio.ended, true);
    else startBlink(now, event.forceDouble);
    return sampleActiveEvent(now) || {phase: 'open', animation: 'auto'};
  }

  if (eyeMode !== 'auto') return {phase: smoothManualPhase(eyeMode, now), animation: 'manual'};

  const speaking = !audio.paused && !audio.ended;
  if (now >= nextHoldStart) {
    startHold(now, speaking);
    return sampleActiveEvent(now) || {phase: 'open', animation: 'auto'};
  }
  if (now >= nextBlinkStart) {
    startBlink(now);
    return sampleActiveEvent(now) || {phase: 'open', animation: 'auto'};
  }
  return {phase: smoothManualPhase('open', now), animation: 'auto'};
}

function renderFrame(now) {
  if (!animationReady) return;
  const eyeSet = currentEyeSet();
  const mouthSelection = getMouthFrame(now);
  if (!eyeSet || !mouthSelection) return;

  const eyeState = resolveEyePhase(now);
  thinkingGazeState = advanceThinkingGaze(thinkingGazeState, now);
  const availableThinkingSets = currentThinkingEyeSets();
  const idle = advanceIdleThinking(idleThinkingState, now,
    $('thinking-gaze').value === 'idle' && eyeMode === 'auto', [...availableThinkingSets.keys()]);
  idleThinkingState = idle.state;
  const effectiveGaze = idle.gazeState || thinkingGazeState;
  const thinkingFrame = resolveThinkingEyeFrameForEmotion(thinkingEyeSetsByEmotion, eyeEmotion, effectiveGaze, eyeState.phase);
  const eyeFrame = thinkingFrame?.frame || eyeSet.phases[eyeState.phase] || eyeSet.phases.open;
  renderedThinkingDirection = thinkingFrame?.directionId ?? '';
  drawComposed(eyeFrame, mouthSelection.frame);
  lastRenderedPhase = eyeState.phase;

  const mouthSet = currentMouthSet();
  const gazeLabel = thinkingFrame ? ` · Nachdenkblick ${thinkingFrame.label}` : '';
  $('state').textContent = `${eyeSet.label}${gazeLabel} · ${eyeState.phase} · Mund ${mouthSelection.shape} (${mouthSet.label}) · geänderte Pixel oben ${eyeFrame.changed ?? 0}, unten ${mouthSelection.frame.changed}`;
  updateLiveData(eyeState.phase, mouthSelection.shape, eyeState.animation);
}

function frame(now) {
  renderFrame(now);
  requestAnimationFrame(frame);
}

function startFrameLoop() {
  if (frameLoopStarted) return;
  frameLoopStarted = true;
  requestAnimationFrame(frame);
}

function setPortraitOptions(entries) {
  const control = $('portrait');
  control.replaceChildren();
  for (const item of entries) {
    const option = document.createElement('option');
    option.value = item.id;
    option.textContent = item.label;
    control.append(option);
  }
  control.value = entries.some((item) => item.id === 'common-077') ? 'common-077' : entries[0]?.id ?? '';
}

function buildEyeSets(manifest, manifestUrl, originalPortrait, portraitId) {
  if (!Array.isArray(manifest.eyeSets)) throw Error('Portraitmanifest enthält keine eyeSets.');
  return Promise.all(manifest.eyeSets.map(async (set) => {
    if (!set.id || !set.label || !set.phases) throw Error('Augenset unvollständig.');
    const phases = {};
    await Promise.all(phaseNames.map(async (phase) => {
      const item = set.phases[phase];
      if (!item?.file) throw Error(`Augenphase fehlt: ${set.id}/${phase}`);
      const src = new URL(item.file, manifestUrl).href;
      const loaded = await image(src, 64, 35);
      const frame = prepareEyeFrame(loaded, src, manifest, originalPortrait, portraitId);
      phases[phase] = {image: frame, changed: Number(item.changedPixels) || 0};
    }));
    return [set.id, {id: set.id, label: set.label, phases}];
  }));
}

async function buildThinkingEyeSets(manifest, manifestUrl, originalPortrait, portraitId) {
  const definitions = validateThinkingEyeSetsByEmotion(manifest);
  if (!definitions.size) return {setsByEmotion: new Map(), notice: 'Nachdenkblick für dieses Portrait nicht verfügbar.'};
  const emotions = await Promise.all([...definitions.entries()].map(async ([emotion, sets]) => {
    const entries = await Promise.all([...sets.values()].map(async (set) => {
      const phases = {};
      await Promise.all(phaseNames.map(async (phase) => {
        const src = new URL(set.phases[phase].file, manifestUrl).href;
        const loaded = await image(src, 64, 35);
        const frame = prepareEyeFrame(loaded, src, manifest, originalPortrait, portraitId);
        phases[phase] = {image: frame, changed: 0};
      }));
      return [set.id, {id: set.id, label: set.label, phases}];
    }));
    return [emotion, new Map(entries)];
  }));
  return {setsByEmotion: new Map(emotions), notice: ''};
}

function legacyMouthSets(manifest) {
  if (!Array.isArray(manifest.mouths)) return [];
  return [{id: 'neutral', label: 'Neutral', frames: manifest.mouths}];
}

function buildMouthSets(manifest) {
  const sourceSets = Array.isArray(manifest.mouthSets) ? manifest.mouthSets : legacyMouthSets(manifest);
  if (!sourceSets.length) throw Error('Portraitmanifest enthält keine mouthSets.');
  return Promise.all(sourceSets.map(async (set) => {
    if (!set.id || !set.label || !Array.isArray(set.frames) || !set.frames.length) throw Error('Mundset unvollständig.');
    const frames = await Promise.all(set.frames.map(async (item) => {
      if (!item.shape || !item.file) throw Error(`Mundform fehlt: ${set.id}`);
      const img = await image(new URL(item.file, selectedManifestUrl).href, 64, 29);
      return {shape: item.shape, image: img, changed: Number(item.changedPixels) || 0};
    }));
    return [set.id, {id: set.id, label: set.label, frames, frameMap: new Map(frames.map((item) => [item.shape, item]))}];
  }));
}

function validateTimeline(timing, expectedHash, id) {
  if (!Number.isFinite(Number(timing.audioSampleRate)) || !Array.isArray(timing.cues)) {
    throw Error(`Viseme-Timing ungültig: ${id}.`);
  }
  if (expectedHash) {
    const actualHash = timing.provenance?.audioSha256 ?? timing.audioSha256;
    if (typeof actualHash !== 'string' || actualHash.toLowerCase() !== String(expectedHash).toLowerCase()) {
      throw Error(`Audio-Hash passt nicht zum Viseme-Timing: ${id}.`);
    }
  }
}

function expectedAudioHash(manifest, set) {
  if (set.audioSha256) return set.audioSha256;
  if (typeof manifest.audioSha256 === 'string') return manifest.audioSha256;
  if (manifest.audioSha256 && typeof manifest.audioSha256 === 'object') return manifest.audioSha256[set.id];
  return null;
}

async function buildAudioBundles(manifest, manifestUrl) {
  if (!Array.isArray(manifest.audioSets)) return null;
  if (!manifest.audioSets.length) throw Error('Portraitmanifest enthält leere audioSets.');
  const bundles = await Promise.all(manifest.audioSets.map(async (set) => {
    if (!set.id || !set.audio || !set.timing) throw Error('Audio-/Viseme-Bundle unvollständig.');
    const audioUrl = new URL(set.audio, manifestUrl);
    const timingUrl = new URL(set.timing, manifestUrl);
    const timing = await json(timingUrl.href);
    validateTimeline(timing, expectedAudioHash(manifest, set), set.id);
    return [set.id, {id: set.id, audioUrl, timeline: timing}];
  }));
  const result = new Map(bundles);
  if (result.size !== manifest.audioSets.length) throw Error('Portraitmanifest enthält doppelte audioSet-IDs.');
  return result;
}

function resetEyeAnimationForPortrait() {
  activeBlink = null;
  activeHold = null;
  queuedEyeEvents = [];
  phaseTransition = null;
  lastRenderedPhase = 'open';
}

async function loadPortrait(id) {
  const epoch = ++portraitEpoch;
  const item = catalog.entries.find((candidate) => candidate.id === id);
  if (!item) return;
  eyeEdgeBlendCache.clear();

  selectedEntry = item;
  selectedManifest = null;
  selectedManifestUrl = null;
  timeline = null;
  eyeSets = new Map();
  thinkingEyeSetsByEmotion = new Map();
  thinkingGazeState = createThinkingGazeState();
  idleThinkingState = createIdleThinkingState();
  loadedStatus = '';
  renderedThinkingDirection = '';
  thinkingGazeNotice = '';
  $('thinking-gaze').value = 'off';
  updateThinkingGazeControl(false);
  mouthSets = new Map();
  audioSets = new Map();
  defaultAudioBundle = null;
  eyeEmotion = 'neutral';
  mouthOverride = null;
  mouthShape = 'X';
  eyeMode = 'auto';
  $('eyes').value = 'auto';
  animationReady = false;
  currentOriginal = null;
  cycling = false;
  nextBlinkStart = 0;
  nextHoldStart = 0;
  resetEyeAnimationForPortrait();
  setControlsEnabled(false);
  setStatus('Portrait wird geladen …');
  for (const canvasId of ['original', 'live']) draw(canvasId, null);

  if (!item.original) {
    if (epoch === portraitEpoch) drawUnavailablePortrait(null, 'Quelle fehlt.');
    return;
  }

  let original;
  try {
    original = await image(new URL(item.original, catalogUrl).href, 64, 64);
  } catch (error) {
    if (epoch === portraitEpoch) drawUnavailablePortrait(null, error.message);
    return;
  }
  if (epoch !== portraitEpoch) return;
  currentOriginal = original;
  draw('original', original);

  if (!item.animationManifest) {
    drawUnavailablePortrait(original, `${item.status || 'original-only'} · Animation nicht vorbereitet.`);
    return;
  }

  try {
    const animationUrl = new URL(item.animationManifest, catalogUrl);
    const nextManifest = await json(animationUrl.href);
    if (epoch !== portraitEpoch) return;
    if (nextManifest.portraitRecord !== item.record) throw Error(`Portraitrecord passt nicht: Manifest ${nextManifest.portraitRecord}, Quelle ${item.record}.`);
    if (nextManifest.bank !== item.bank) throw Error(`Portraitbank passt nicht: Manifest ${nextManifest.bank}, Quelle ${item.bank}.`);
    if (nextManifest.width !== 64 || nextManifest.height !== 64 || nextManifest.splitY !== 35) throw Error('Unerwartetes Portraitlayout.');
    const voiceUrl = sharedVoiceManifestUrl(item, catalog, catalogUrl);
    const voiceManifest = await json(voiceUrl.href);
    if (epoch !== portraitEpoch) return;
    if (voiceManifest.voiceSet !== item.voiceSet || voiceManifest.audioSets?.length !== 6) {
      throw Error(`Gemeinsames Stimmset passt nicht: ${item.voiceSet}.`);
    }

    selectedManifest = nextManifest;
    selectedManifestUrl = animationUrl;
    const [eyeEntries, mouthEntries, audioBundles, thinkingResult] = await Promise.all([
      buildEyeSets(nextManifest, animationUrl, original, item.id),
      buildMouthSets(nextManifest),
      buildAudioBundles(voiceManifest, voiceUrl),
      buildThinkingEyeSets(nextManifest, animationUrl, original, item.id).catch((error) => ({
        setsByEmotion: new Map(),
        notice: `Nachdenkblick ungültig; normale Ausdrucksaugen werden verwendet (${error.message}).`,
      })),
    ]);
    if (epoch !== portraitEpoch) return;
    eyeSets = new Map(eyeEntries);
    thinkingEyeSetsByEmotion = thinkingResult.setsByEmotion;
    $('thinking-gaze').value = currentThinkingEyeSets().size === THINKING_DIRECTIONS.length ? 'idle' : 'off';
    thinkingGazeNotice = thinkingResult.notice;
    mouthSets = new Map(mouthEntries);
    audioSets = audioBundles || new Map();
    $('live').dataset.voiceSet = item.voiceSet;
    if (!eyeSets.size || !mouthSets.size) throw Error('Portraitmanifest enthält keine verwendbaren Sets.');
    if (audioSets.size && (audioSets.size !== eyeSets.size || [...eyeSets.keys()].some((id) => !audioSets.has(id)))) {
      throw Error('Audio-Sets passen nicht zu den Augensets.');
    }

    eyeEmotion = eyeSets.has('neutral') ? 'neutral' : eyeSets.keys().next().value;
    mouthOverride = null;
    mouthShape = 'X';
    populateEmotions();
    updateEmotionButtons();

    if (audioSets.size) {
      defaultAudioBundle = null;
    } else {
      const timingUrl = nextManifest.timing
        ? new URL(nextManifest.timing, animationUrl)
        : legacyTimingUrl;
      const audioUrl = nextManifest.audio
        ? new URL(nextManifest.audio, animationUrl)
        : legacyAudioUrl;
      const timingResult = await json(timingUrl.href);
      if (epoch !== portraitEpoch) return;
      validateTimeline(timingResult, expectedAudioHash(nextManifest, {id: 'legacy'}), 'legacy');
      defaultAudioBundle = {id: 'legacy', audioUrl, timeline: timingResult};
    }
    animationReady = true;
    setControlsEnabled(true);
    bindAudioBundle(audioSets.get(eyeEmotion) || defaultAudioBundle);
    const now = performance.now();
    if (!nextBlinkStart) nextBlinkStart = now + nextBlinkDelay({secondStart: null});
    if (!nextHoldStart) nextHoldStart = now + nextEyeHoldDelay(false);
    $('proof').textContent = 'Pixelprüfung: Oberhälfte 64×35 und Unterhälfte 64×29 werden ohne Streckung zusammengesetzt.';
    const speechCount = audioSets.size || 1;
    const status = `${eyeSets.size} Augensets · ${mouthSets.size} Mundsets · ${speechCount} Sprachclips · ${[...mouthSets.values()].reduce((sum, set) => sum + set.frames.length, 0)} Mundframes geladen.${thinkingGazeNotice ? ` ${thinkingGazeNotice}` : ''}`;
    loadedStatus = status;
    setStatus(status, `${item.label} · animiert${thinkingGazeNotice ? ` · ${thinkingGazeNotice}` : ''}`);
  } catch (error) {
    if (epoch !== portraitEpoch) return;
    drawUnavailablePortrait(original, error.message);
  }
}

function enqueueBlink(forceDouble = false) {
  queuedEyeEvents.push({type: 'blink', forceDouble});
}

function enqueueHold() {
  queuedEyeEvents.push({type: 'hold'});
}

$('eyes').addEventListener('change', (event) => {
  eyeMode = event.target.value;
  phaseTransition = null;
});

$('thinking-gaze').addEventListener('change', (event) => {
  const now = performance.now();
  const selection = event.target.value;
  idleThinkingState = createIdleThinkingState();
  if (selection === 'idle') {
    thinkingGazeState = createThinkingGazeState();
    return;
  }
  const nextState = selectThinkingGaze(thinkingGazeState, selection, now, Math.random, [...currentThinkingEyeSets().keys()]);
  if (selection !== 'off' && nextState.mode === 'off') {
    event.target.value = 'off';
    thinkingGazeNotice = 'Nachdenkblick unvollständig; normale Ausdrucksaugen werden verwendet.';
    $('status').textContent = thinkingGazeNotice;
    return;
  }
  thinkingGazeState = nextState;
});

$('mouth').addEventListener('change', (event) => {
  stopPlaybackForManualAction();
  cycling = false;
  mouthShape = event.target.value;
});

if (mouthEmotionControl) {
  mouthEmotionControl.addEventListener('change', (event) => {
    mouthOverride = event.target.value === 'same-as-eyes' ? null : event.target.value;
    populateMouthShapes();
  });
}

$('blink').addEventListener('click', () => enqueueBlink());
$('double-blink').addEventListener('click', () => enqueueBlink(true));
$('hold-test').addEventListener('click', enqueueHold);
$('cycle').addEventListener('click', () => {
  stopPlaybackForManualAction();
  cycling = true;
  cycleStart = performance.now();
});
$('rest').addEventListener('click', () => {
  stopPlaybackForManualAction();
  cycling = false;
  mouthShape = 'X';
  $('mouth').value = 'X';
});

playButton.addEventListener('click', async () => {
  if (!animationReady) return;
  if (playbackWanted) {
    playbackWanted = false;
    playbackCommandEpoch += 1;
    pauseAudioPreservingIntent();
    setPlayLabel();
  } else {
    playbackWanted = true;
    cycling = false;
    void requestPlayback();
  }
});

loopControl.addEventListener('change', () => {
  audio.loop = loopControl.checked;
});

audio.addEventListener('play', () => {
  const currentUrl = activeAudioUrl?.href;
  const matchIndex = programmaticPlayRequests.findIndex((item) => item.audioEpoch === audioEpoch && item.url === currentUrl);
  if (matchIndex !== -1) {
    const [request] = programmaticPlayRequests.splice(matchIndex, 1);
    if (request.commandEpoch !== playbackCommandEpoch || !playbackWanted) {
      pauseAudioPreservingIntent();
      return;
    }
  } else {
    if (audio.paused) return;
    playbackWanted = true;
    playbackCommandEpoch += 1;
  }
  cycling = false;
  setPlayLabel();
});
audio.addEventListener('pause', () => {
  if (ignoredPauseEvents > 0) ignoredPauseEvents -= 1;
  else {
    playbackWanted = false;
    playbackCommandEpoch += 1;
  }
  setPlayLabel();
});
audio.addEventListener('ended', () => {
  playbackWanted = false;
  playbackCommandEpoch += 1;
  mouthShape = 'X';
  $('mouth').value = 'X';
  setPlayLabel();
});
audio.addEventListener('error', () => {
  if (animationReady && activeAudioUrl && audio.currentSrc === activeAudioUrl.href) {
    playbackWanted = false;
    playbackCommandEpoch += 1;
    setPlayLabel();
    setStatus('Sprachclip fehlt oder kann nicht geladen werden.', 'Audioquelle fehlt');
  }
});

async function init() {
  try {
    catalog = await json(catalogUrl.href);
    if (!Array.isArray(catalog.entries)) throw Error('Portraitkatalog enthält keine Einträge.');
    setPortraitOptions(catalog.entries);
    $('portrait').addEventListener('change', (event) => loadPortrait(event.target.value));
    $('proof').textContent = 'Pixelprüfung: Original und Bildhälften werden direkt bei nativer Größe gezeichnet.';
    startFrameLoop();
    await loadPortrait($('portrait').value);
  } catch (error) {
    drawUnavailablePortrait(null, error.message);
  }
}

init();
