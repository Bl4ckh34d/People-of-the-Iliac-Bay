import {THINKING_DIRECTIONS} from './thinking-gaze-policy.mjs';
import {ANIMATION_POLICY} from './portrait-behavior-policy.mjs';

export {ANIMATION_POLICY};

export function createIdleThinkingState() {
  return {windowStartAt: null, lastNow: null, bursts: [], lastDirectionId: null};
}

function unitRandom(random) {
  if (typeof random !== 'function') return 0;
  const value = Number(random());
  return Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 0;
}

function fullSetAvailable(availableIds) {
  return Array.isArray(availableIds)
    && availableIds.length === THINKING_DIRECTIONS.length
    && new Set(availableIds).size === THINKING_DIRECTIONS.length
    && THINKING_DIRECTIONS.every((id) => availableIds.includes(id));
}

function chooseDirection(previousId, random) {
  const choices = THINKING_DIRECTIONS.filter((id) => id !== previousId);
  return choices[Math.min(choices.length - 1, Math.floor(unitRandom(random) * choices.length))];
}

function scheduleWindow(startAt, previousId, random) {
  const countRange = ANIMATION_POLICY.gazeBurstsMax - ANIMATION_POLICY.gazeBurstsMin + 1;
  const count = ANIMATION_POLICY.gazeBurstsMin
    + Math.min(countRange - 1, Math.floor(unitRandom(random) * countRange));
  const slotSize = ANIMATION_POLICY.gazeWindowMs / count;
  const bursts = [];
  let lastDirectionId = previousId;
  for (let index = 0; index < count; index++) {
    const duration = ANIMATION_POLICY.gazeDurationMinMs
      + unitRandom(random) * (ANIMATION_POLICY.gazeDurationMaxMs - ANIMATION_POLICY.gazeDurationMinMs);
    const slotStart = index * slotSize;
    const slotEnd = (index + 1) * slotSize;
    const maxOffset = Math.max(0, slotSize - duration - 1);
    let offset = unitRandom(random) * maxOffset;
    if (index === 0 && slotStart + offset < 1) offset = 1;
    const directionId = chooseDirection(lastDirectionId, random);
    const burstStart = startAt + slotStart + offset;
    const burstEnd = Math.min(burstStart + duration, startAt + slotEnd - 1);
    bursts.push({startAt: burstStart, endAt: burstEnd, directionId,
      segments: [{startAt: burstStart, endAt: burstEnd, directionId}]});
    lastDirectionId = directionId;
  }
  return {bursts, lastDirectionId};
}

function currentGaze(state, now) {
  const burst = state.bursts.find((entry) => now >= entry.startAt && now < entry.endAt);
  const segment = burst?.segments?.find((entry) => now >= entry.startAt && now < entry.endAt);
  return segment ? {mode: 'fixed', directionId: segment.directionId, nextChangeAt: null} : null;
}

export function advanceIdleThinking(state, now, eligible, availableIds, random = Math.random) {
  // `eligible` means the gallery's automatic blink mode with no manual gaze override.
  // Gallery speech/cycling does not suppress these review movements; game timing is separate.
  // `now` must come from the browser UI animation clock, in monotonic milliseconds.
  if (!eligible || !fullSetAvailable(availableIds) || !Number.isFinite(now)) {
    return {state: createIdleThinkingState(), gazeState: null};
  }
  const validState = state && Array.isArray(state.bursts) && Number.isFinite(state.windowStartAt)
    && Number.isFinite(state.lastNow);
  if (validState && now < state.lastNow) return {state, gazeState: currentGaze(state, state.lastNow)};

  let next = state;
  if (!validState || now >= state.windowStartAt + ANIMATION_POLICY.gazeWindowMs) {
    const priorDirection = validState ? state.lastDirectionId : null;
    const scheduled = scheduleWindow(now, priorDirection, random);
    next = {...createIdleThinkingState(), windowStartAt: now, bursts: scheduled.bursts,
      lastDirectionId: scheduled.lastDirectionId};
  } else {
    next = {...state};
  }
  next.lastNow = now;
  return {state: next, gazeState: currentGaze(next, now)};
}
