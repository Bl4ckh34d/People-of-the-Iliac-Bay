import test from 'node:test';
import assert from 'node:assert/strict';
import {THINKING_DIRECTIONS} from './thinking-gaze-policy.mjs';
import {advanceIdleThinking, createIdleThinkingState} from './idle-thinking-policy.mjs';

function sequence(values) {
  let index = 0;
  return () => values[Math.min(index++, values.length - 1)];
}

function horizontalPair() {
  return [...THINKING_DIRECTIONS];
}

test('idle enable waits for a scheduled left/right period and restores emotion eyes between periods', () => {
  let result = advanceIdleThinking(createIdleThinkingState(), 10_000, true, horizontalPair(), sequence([0, 0, 0]));
  assert.equal(result.gazeState, null);
  assert.equal(result.state.bursts.length, 1);
  const burst = result.state.bursts[0];
  assert.equal(result.state.windowStartAt, 10_000);
  assert.equal(burst.startAt, 10_001);
  assert.equal(burst.endAt - burst.startAt, 1_800);
  assert.equal(burst.directionId, 'left');
  assert.deepEqual(burst.segments, [{startAt: burst.startAt, endAt: burst.endAt, directionId: 'left'}]);

  result = advanceIdleThinking(result.state, burst.startAt, true, horizontalPair(), () => 0);
  assert.deepEqual(result.gazeState, {mode: 'fixed', directionId: 'left', nextChangeAt: null});
  result = advanceIdleThinking(result.state, burst.endAt, true, horizontalPair(), () => 0);
  assert.equal(result.gazeState, null);
});

test('each minute schedules one to five left/right periods lasting 1.8–6.5 seconds', () => {
  for (const [sample, expectedCount] of [[0, 1], [1, 5]]) {
    const {state} = advanceIdleThinking(createIdleThinkingState(), 0, true, horizontalPair(), () => sample);
    assert.equal(state.bursts.length, expectedCount);
    const slotSize = 60_000 / expectedCount;
    for (let index = 0; index < state.bursts.length; index++) {
      const burst = state.bursts[index];
      assert.ok(burst.startAt > 0);
      assert.ok(burst.startAt >= index * slotSize);
      assert.ok(burst.endAt <= (index + 1) * slotSize);
      assert.ok(burst.endAt - burst.startAt >= 1_800);
      assert.ok(burst.endAt - burst.startAt <= 6_500);
      assert.ok(['left', 'right'].includes(burst.directionId));
      assert.deepEqual(burst.segments, [{startAt: burst.startAt, endAt: burst.endAt, directionId: burst.directionId}]);
      if (index) assert.ok(state.bursts[index - 1].endAt <= burst.startAt);
    }
  }
  const longest = advanceIdleThinking(createIdleThinkingState(), 0, true, horizontalPair(), sequence([0, 1, 1]));
  assert.equal(longest.state.bursts[0].endAt - longest.state.bursts[0].startAt, 6_500);
});

test('direction remains stable throughout a period and may alternate only at the next period', () => {
  const {state} = advanceIdleThinking(createIdleThinkingState(), 0, true, horizontalPair(), () => 1);
  assert.equal(state.bursts.length, 5);
  for (let index = 0; index < state.bursts.length; index++) {
    const burst = state.bursts[index];
    assert.ok(['left', 'right'].includes(burst.directionId));
    assert.equal(burst.segments.length, 1);
    for (const now of [burst.startAt, Math.floor((burst.startAt + burst.endAt) / 2), burst.endAt - 1]) {
      const active = advanceIdleThinking(state, now, true, horizontalPair(), () => 0).gazeState;
      assert.equal(active.directionId, burst.directionId);
    }
    if (index > 0) assert.notEqual(burst.directionId, state.bursts[index - 1].directionId);
  }
});

test('next eligible minute alternates from the previous period without vertical or diagonal directions', () => {
  const first = advanceIdleThinking(createIdleThinkingState(), 0, true, horizontalPair(), () => 0);
  const next = advanceIdleThinking(first.state, 60_000, true, horizontalPair(), () => 0);
  assert.equal(next.state.windowStartAt, 60_000);
  assert.equal(next.state.bursts.length, 1);
  assert.equal(next.gazeState, null);
  assert.ok(next.state.bursts[0].startAt > 60_000);
  assert.notEqual(next.state.bursts[0].directionId, first.state.bursts[0].directionId);
  assert.ok(['left', 'right'].includes(next.state.bursts[0].directionId));
});

test('manual or audio eligibility cancellation clears active gaze and re-enable waits fresh', () => {
  let result = advanceIdleThinking(createIdleThinkingState(), 50_000, true, horizontalPair(), sequence([0, 0, 0]));
  const active = result.state.bursts[0];
  result = advanceIdleThinking(result.state, active.startAt, true, horizontalPair(), () => 0);
  assert.ok(result.gazeState);
  result = advanceIdleThinking(result.state, active.startAt + 1, false, horizontalPair(), () => 0);
  assert.equal(result.gazeState, null);
  assert.deepEqual(result.state, createIdleThinkingState());
  result = advanceIdleThinking(result.state, active.endAt + 20_000, true, horizontalPair(), sequence([0, 0, 0]));
  assert.equal(result.gazeState, null);
  assert.equal(result.state.windowStartAt, active.endAt + 20_000);
});

test('incomplete gaze set fails closed and stale monotonic timestamps do nothing', () => {
  const unavailable = advanceIdleThinking(createIdleThinkingState(), 1_000, true, ['left'], () => 0);
  assert.equal(unavailable.gazeState, null);
  assert.deepEqual(unavailable.state, createIdleThinkingState());

  let active = advanceIdleThinking(createIdleThinkingState(), 10_000, true, horizontalPair(), sequence([0, 0, 0]));
  active = advanceIdleThinking(active.state, active.state.bursts[0].startAt, true, horizontalPair(), () => 0);
  const stale = advanceIdleThinking(active.state, 9_000, true, horizontalPair(), () => 1);
  assert.strictEqual(stale.state, active.state);
  assert.deepEqual(stale.gazeState, active.gazeState);
});
