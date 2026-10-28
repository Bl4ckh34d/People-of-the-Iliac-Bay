import {ANIMATION_POLICY} from './portrait-behavior-policy.mjs';

export {ANIMATION_POLICY};

export const THINKING_DIRECTIONS = Object.freeze(['left', 'right']);

const PHASES = Object.freeze(['open', 'half', 'closed']);
const EMOTIONS = Object.freeze(['neutral', 'happy', 'angry', 'sad', 'afraid', 'guarded']);
const LEGACY_THINKING_DIRECTIONS = Object.freeze([
  'up-left', 'up', 'up-right', 'left', 'center', 'right', 'down-left', 'down', 'down-right',
]);

export function validateThinkingEyeSets(entries) {
  if (entries === undefined || entries === null) return null;
  if (!Array.isArray(entries) || ![THINKING_DIRECTIONS.length, LEGACY_THINKING_DIRECTIONS.length].includes(entries.length)) {
    throw new Error('Thinking eye sets must contain exactly left and right, or a legacy nine-direction map.');
  }

  const byId = new Map();
  for (const entry of entries) {
    if (!entry || typeof entry !== 'object' || Array.isArray(entry)) {
      throw new Error('Thinking eye set must be an object.');
    }
    if (!LEGACY_THINKING_DIRECTIONS.includes(entry.id)) {
      throw new Error(`Unknown thinking gaze direction: ${entry.id ?? ''}.`);
    }
    if (byId.has(entry.id)) throw new Error(`Duplicate thinking gaze direction: ${entry.id}.`);
    if (typeof entry.label !== 'string' || !entry.label.trim()) {
      throw new Error(`Thinking gaze label missing: ${entry.id}.`);
    }
    if (!entry.phases || typeof entry.phases !== 'object' || Array.isArray(entry.phases)) {
      throw new Error(`Thinking gaze phases missing: ${entry.id}.`);
    }
    for (const phase of PHASES) {
      const file = entry.phases[phase]?.file;
      if (typeof file !== 'string' || !file.trim()) {
        throw new Error(`Thinking gaze phase file missing: ${entry.id}/${phase}.`);
      }
    }
    byId.set(entry.id, {
      id: entry.id,
      label: entry.label.trim(),
      phases: Object.fromEntries(PHASES.map((phase) => [phase, {file: entry.phases[phase].file.trim()}])),
    });
  }

  const expected = entries.length === LEGACY_THINKING_DIRECTIONS.length
    ? LEGACY_THINKING_DIRECTIONS
    : THINKING_DIRECTIONS;
  const missing = expected.find((id) => !byId.has(id));
  if (missing) throw new Error(`Thinking gaze direction missing: ${missing}.`);
  return THINKING_DIRECTIONS.map((id) => byId.get(id));
}

export function validateThinkingEyeSetsByEmotion(manifest) {
  if (!manifest || typeof manifest !== 'object' || Array.isArray(manifest)) {
    throw new Error('Portrait manifest must be an object.');
  }
  const result = new Map();
  const mapping = manifest.thinkingEyeSetsByEmotion;
  if (mapping !== undefined && mapping !== null) {
    if (typeof mapping !== 'object' || Array.isArray(mapping)) {
      throw new Error('Emotion thinking eye sets must be an object.');
    }
    for (const [emotion, entries] of Object.entries(mapping)) {
      if (!EMOTIONS.includes(emotion)) throw new Error(`Unknown thinking eye emotion: ${emotion}.`);
      if (!Array.isArray(entries)) throw new Error(`Thinking eye sets missing for emotion: ${emotion}.`);
      result.set(emotion, new Map(validateThinkingEyeSets(entries).map((set) => [set.id, set])));
    }
  }

  if (!result.has('neutral') && manifest.thinkingEyeSets !== undefined && manifest.thinkingEyeSets !== null) {
    const legacy = validateThinkingEyeSets(manifest.thinkingEyeSets);
    if (legacy) result.set('neutral', new Map(legacy.map((set) => [set.id, set])));
  }
  return result;
}

export function createThinkingGazeState() {
  return {mode: 'off', directionId: null, nextChangeAt: null};
}

function unitRandom(random) {
  if (typeof random !== 'function') return 0;
  const value = Number(random());
  return Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 0;
}

function randomIndex(length, random) {
  return Math.min(length - 1, Math.floor(unitRandom(random) * length));
}

function nextInterval(random) {
  return ANIMATION_POLICY.gazeAutoMinMs
    + unitRandom(random) * (ANIMATION_POLICY.gazeAutoMaxMs - ANIMATION_POLICY.gazeAutoMinMs);
}

function fullSetAvailable(availableIds) {
  return Array.isArray(availableIds)
    && availableIds.length === THINKING_DIRECTIONS.length
    && new Set(availableIds).size === THINKING_DIRECTIONS.length
    && THINKING_DIRECTIONS.every((id) => availableIds.includes(id));
}

function pickDirection(exceptId, random) {
  const choices = THINKING_DIRECTIONS.filter((id) => id !== exceptId);
  return choices[randomIndex(choices.length, random)];
}

export function selectThinkingGaze(_state, selection, now, random = Math.random, availableIds = THINKING_DIRECTIONS) {
  if (selection === 'off') return createThinkingGazeState();
  if (!Number.isFinite(now) || !fullSetAvailable(availableIds)) return createThinkingGazeState();

  if (selection === 'auto') {
    return {
      mode: 'auto',
      directionId: THINKING_DIRECTIONS[randomIndex(THINKING_DIRECTIONS.length, random)],
      nextChangeAt: now + nextInterval(random),
    };
  }
  if (!THINKING_DIRECTIONS.includes(selection)) return createThinkingGazeState();
  return {mode: 'fixed', directionId: selection, nextChangeAt: null};
}

export function advanceThinkingGaze(state, now, random = Math.random) {
  if (!state || !['auto', 'fixed', 'off'].includes(state.mode)) return createThinkingGazeState();
  if (state.mode !== 'auto') return state;
  if (!THINKING_DIRECTIONS.includes(state.directionId) || !Number.isFinite(state.nextChangeAt)) {
    return createThinkingGazeState();
  }
  if (!Number.isFinite(now) || now < state.nextChangeAt) return state;

  return {
    mode: 'auto',
    directionId: pickDirection(state.directionId, random),
    nextChangeAt: now + nextInterval(random),
  };
}

export function resolveThinkingEyeFrame(sets, state, phase) {
  if (!state || !['auto', 'fixed'].includes(state.mode) || !THINKING_DIRECTIONS.includes(state.directionId)) return null;
  const set = sets instanceof Map ? sets.get(state.directionId) : null;
  const frame = set?.phases?.[phase];
  return frame && typeof frame === 'object' ? {directionId: set.id, label: set.label, frame} : null;
}

export function resolveThinkingEyeFrameForEmotion(setsByEmotion, emotion, state, phase) {
  if (!(setsByEmotion instanceof Map)) return null;
  return resolveThinkingEyeFrame(setsByEmotion.get(emotion), state, phase);
}
