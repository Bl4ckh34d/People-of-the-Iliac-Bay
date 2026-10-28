import test from 'node:test';
import assert from 'node:assert/strict';
import behaviorPolicy from './portrait-behavior-policy.json' with {type: 'json'};
import * as blinkPolicy from './blink-policy.mjs';
import * as eyeHoldPolicy from './eye-hold-policy.mjs';
import * as idleThinkingPolicy from './idle-thinking-policy.mjs';
import * as thinkingGazePolicy from './thinking-gaze-policy.mjs';

function sequence(values) {
  let index = 0;
  return () => values[Math.min(index++, values.length - 1)];
}

test('all animation policy modules expose the shared JSON policy', () => {
  for (const module of [blinkPolicy, eyeHoldPolicy, idleThinkingPolicy, thinkingGazePolicy]) {
    assert.strictEqual(module.ANIMATION_POLICY, behaviorPolicy);
  }
});

test('blink phases and double timing use shared policy values', () => {
  const blink = blinkPolicy.createBlink(0, sequence([0, 0]));
  assert.equal(blink.secondStart, behaviorPolicy.blinkDurationMs * behaviorPolicy.blinkDoubleStartMinFraction);
  assert.equal(blink.end, blink.secondStart + behaviorPolicy.blinkDurationMs);

  const single = {start: 0, secondStart: null, end: behaviorPolicy.blinkDurationMs};
  assert.equal(blinkPolicy.sampleBlink(single, behaviorPolicy.blinkHalfMs).phase, 'closed');
  assert.equal(blinkPolicy.sampleBlink(single, behaviorPolicy.blinkClosedEndMs).phase, 'half');
  assert.equal(blinkPolicy.sampleBlink(single, behaviorPolicy.blinkDurationMs).completed, true);
});

test('eye holds use shared idle and speech timing bounds', () => {
  const idleMin = eyeHoldPolicy.createEyeHold(0, false, () => 0);
  const idleMax = eyeHoldPolicy.createEyeHold(0, false, () => 1);
  assert.equal(idleMin.end, behaviorPolicy.holdHalfMinMs);
  assert.equal(idleMax.end, behaviorPolicy.holdHalfMaxMs);
  assert.equal(eyeHoldPolicy.nextEyeHoldDelay(false, () => 0), behaviorPolicy.holdIntervalMinMs);
  assert.equal(eyeHoldPolicy.nextEyeHoldDelay(false, () => 1), behaviorPolicy.holdIntervalMaxMs);

  const speech = eyeHoldPolicy.createEyeHold(0, true, sequence([0, 0]));
  assert.deepEqual(speech.segments.map(({phase, start, end}) => [phase, start, end]), [
    ['half', 0, behaviorPolicy.holdReleaseMs],
    ['closed', behaviorPolicy.holdReleaseMs, behaviorPolicy.holdReleaseMs + behaviorPolicy.holdClosedMinMs],
    ['half', behaviorPolicy.holdReleaseMs + behaviorPolicy.holdClosedMinMs,
      behaviorPolicy.holdReleaseMs * 2 + behaviorPolicy.holdClosedMinMs],
  ]);
});

test('automatic gaze uses shared interval and idle-window bounds', () => {
  const autoMin = thinkingGazePolicy.selectThinkingGaze(
    thinkingGazePolicy.createThinkingGazeState(), 'auto', 0, () => 0,
  );
  const autoMax = thinkingGazePolicy.selectThinkingGaze(
    thinkingGazePolicy.createThinkingGazeState(), 'auto', 0, () => 1,
  );
  assert.equal(autoMin.nextChangeAt, behaviorPolicy.gazeAutoMinMs);
  assert.equal(autoMax.nextChangeAt, behaviorPolicy.gazeAutoMaxMs);

  const {state} = idleThinkingPolicy.advanceIdleThinking(
    idleThinkingPolicy.createIdleThinkingState(), 0, true, ['left', 'right'], sequence([0, 0, 0]),
  );
  assert.equal(state.windowStartAt, 0);
  assert.equal(state.bursts.length, behaviorPolicy.gazeBurstsMin);
  assert.equal(state.bursts[0].endAt - state.bursts[0].startAt, behaviorPolicy.gazeDurationMinMs);
  assert.equal(state.windowStartAt + behaviorPolicy.gazeWindowMs, 60_000);
});
