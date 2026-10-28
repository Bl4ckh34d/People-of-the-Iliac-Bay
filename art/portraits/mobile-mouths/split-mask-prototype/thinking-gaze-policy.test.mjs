import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {
  THINKING_DIRECTIONS,
  advanceThinkingGaze,
  createThinkingGazeState,
  resolveThinkingEyeFrame,
  resolveThinkingEyeFrameForEmotion,
  selectThinkingGaze,
  validateThinkingEyeSets,
  validateThinkingEyeSetsByEmotion,
} from './thinking-gaze-policy.mjs';

const LEGACY_DIRECTIONS = [
  'up-left', 'up', 'up-right', 'left', 'center', 'right', 'down-left', 'down', 'down-right',
];

function definitions(directions = THINKING_DIRECTIONS) {
  return directions.map((id) => ({
    id,
    label: id,
    phases: {
      open: {file: `${id}-open.png`},
      half: {file: `${id}-half.png`},
      closed: {file: `${id}-closed.png`},
    },
  }));
}

test('new maps require exactly left and right with all three frame files', () => {
  assert.deepEqual(THINKING_DIRECTIONS, ['left', 'right']);
  assert.deepEqual(validateThinkingEyeSets(definitions()).map((set) => set.id), ['left', 'right']);
  assert.equal(validateThinkingEyeSets(undefined), null);
  assert.throws(() => validateThinkingEyeSets(definitions(['left'])), /two|left.*right/i);

  const unknown = definitions();
  unknown[0] = {...unknown[0], id: 'upside-down'};
  assert.throws(() => validateThinkingEyeSets(unknown), /unknown/i);

  const missingPhase = definitions();
  delete missingPhase[0].phases.half;
  assert.throws(() => validateThinkingEyeSets(missingPhase), /half/i);

  const missingFile = definitions();
  missingFile[0].phases.closed.file = '';
  assert.throws(() => validateThinkingEyeSets(missingFile), /closed/i);
});

test('legacy nine-direction maps load only left/right phases', () => {
  const expected = new Map(definitions(['left', 'right']).map((set) => [set.id, set]));
  const legacy = validateThinkingEyeSets(definitions(LEGACY_DIRECTIONS));
  assert.deepEqual(legacy, ['left', 'right'].map((id) => expected.get(id)));

  const byEmotion = validateThinkingEyeSetsByEmotion({
    thinkingEyeSetsByEmotion: {neutral: definitions(LEGACY_DIRECTIONS)},
    thinkingEyeSets: definitions(LEGACY_DIRECTIONS),
  });
  assert.deepEqual([...byEmotion.get('neutral').keys()], ['left', 'right']);
  assert.equal(byEmotion.get('neutral').has('up'), false);
});

test('unknown or unavailable fixed gaze fails closed to off', () => {
  const initial = createThinkingGazeState();
  assert.deepEqual(selectThinkingGaze(initial, 'up', 10, () => 0), createThinkingGazeState());
  assert.deepEqual(selectThinkingGaze(initial, 'left', 10, () => 0, ['left']), createThinkingGazeState());
});

test('automatic gaze uses only left/right, alternates, and respects its deadline', () => {
  let state = selectThinkingGaze(createThinkingGazeState(), 'auto', 100, () => 0);
  assert.equal(state.mode, 'auto');
  assert.equal(state.directionId, 'left');
  assert.equal(state.nextChangeAt, 1600);
  assert.strictEqual(advanceThinkingGaze(state, 1599, () => 1), state);
  state = advanceThinkingGaze(state, 1600, () => 0);
  assert.equal(state.directionId, 'right');
  assert.equal(state.nextChangeAt, 3100);
  assert.equal(advanceThinkingGaze(state, 3100, () => 0).directionId, 'left');

  const maxInterval = selectThinkingGaze(createThinkingGazeState(), 'auto', 0, () => 1);
  assert.equal(maxInterval.nextChangeAt, 4500);
});

test('manual gaze accepts only left/right and reset clears state', () => {
  const fixed = selectThinkingGaze(createThinkingGazeState(), 'right', 42);
  assert.equal(fixed.directionId, 'right');
  assert.deepEqual(selectThinkingGaze(fixed, 'down-left', 42), createThinkingGazeState());
  assert.deepEqual(createThinkingGazeState(), {mode: 'off', directionId: null, nextChangeAt: null});
  assert.deepEqual(advanceThinkingGaze(createThinkingGazeState(), 100_000, () => 0), createThinkingGazeState());
});

test('missing thinking phase resolves to null so caller can use standard emotion eyes', () => {
  const frames = new Map([['left', {id: 'left', phases: {open: {file: 'open.png'}}}]]);
  const selected = selectThinkingGaze(createThinkingGazeState(), 'left', 0);
  assert.equal(resolveThinkingEyeFrame(frames, selected, 'open')?.frame.file, 'open.png');
  assert.equal(resolveThinkingEyeFrame(frames, selected, 'closed'), null);
});

test('legacy thinking eyes map only to neutral and never leak into another emotion', () => {
  const setsByEmotion = validateThinkingEyeSetsByEmotion({thinkingEyeSets: definitions(LEGACY_DIRECTIONS)});
  const gaze = selectThinkingGaze(createThinkingGazeState(), 'left', 0);
  assert.equal(resolveThinkingEyeFrameForEmotion(setsByEmotion, 'neutral', gaze, 'open')?.frame.file, 'left-open.png');
  assert.equal(resolveThinkingEyeFrameForEmotion(setsByEmotion, 'happy', gaze, 'open'), null);
});

test('emotion thinking gaze uses only the matching complete mood map', () => {
  const neutral = definitions();
  const happy = definitions().map((entry) => ({...entry, phases: Object.fromEntries(
    Object.entries(entry.phases).map(([phase]) => [phase, {file: `happy-${entry.id}-${phase}.png`}])),
  }));
  const setsByEmotion = validateThinkingEyeSetsByEmotion({thinkingEyeSetsByEmotion: {neutral, happy}});
  const gaze = selectThinkingGaze(createThinkingGazeState(), 'right', 0);
  assert.equal(resolveThinkingEyeFrameForEmotion(setsByEmotion, 'happy', gaze, 'open')?.frame.file,
    'happy-right-open.png');
  assert.equal(resolveThinkingEyeFrameForEmotion(setsByEmotion, 'sad', gaze, 'open'), null);
});

test('emotion maps reject unknown keys and malformed phases', () => {
  assert.throws(() => validateThinkingEyeSetsByEmotion({thinkingEyeSetsByEmotion: {surprised: definitions()}}), /unknown/i);
  const malformed = definitions();
  delete malformed[0].phases.half;
  assert.throws(() => validateThinkingEyeSetsByEmotion({thinkingEyeSetsByEmotion: {afraid: malformed}}), /half/i);
});

test('gallery dropdown contains only the five approved thinking-gaze options', () => {
  const html = readFileSync(new URL('./preview.html', import.meta.url), 'utf8');
  const select = html.match(/<select id="thinking-gaze">([\s\S]*?)<\/select>/)?.[1] ?? '';
  const options = [...select.matchAll(/<option value="([^"]+)">([^<]*)<\/option>/g)]
    .map(([, value, label]) => [value, label]);
  assert.deepEqual(options, [
    ['idle', 'Mit Blinzeln'], ['off', 'Aus'], ['auto', 'Wechselnd'], ['left', 'Links'], ['right', 'Rechts'],
  ]);
});
