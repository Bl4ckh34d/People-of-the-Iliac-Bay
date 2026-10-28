import {ANIMATION_POLICY} from './portrait-behavior-policy.mjs';

export {ANIMATION_POLICY};

function unitRandom(random) {
  const value = Number(random());
  return Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 0;
}

function randomMs(min, max, random) {
  return Math.round(min + unitRandom(random) * (max - min));
}

function segment(phase, start, duration) {
  return {phase, start, end: start + duration};
}

export function createEyeHold(now, speaking, random = Math.random) {
  if (!Number.isFinite(now)) throw new TypeError('now must be a finite number');
  if (typeof random !== 'function') throw new TypeError('random must be a function');

  const segments = [];

  if (speaking && unitRandom(random) < ANIMATION_POLICY.holdSpeakingClosedChance) {
    const halfStart = now;
    const closedStart = halfStart + ANIMATION_POLICY.holdReleaseMs;
    const closedEnd = closedStart + randomMs(ANIMATION_POLICY.holdClosedMinMs, ANIMATION_POLICY.holdClosedMaxMs, random);
    segments.push(
      segment('half', halfStart, ANIMATION_POLICY.holdReleaseMs),
      segment('closed', closedStart, closedEnd - closedStart),
      segment('half', closedEnd, ANIMATION_POLICY.holdReleaseMs),
    );
  } else {
    segments.push(segment('half', now,
      randomMs(ANIMATION_POLICY.holdHalfMinMs, ANIMATION_POLICY.holdHalfMaxMs, random)));
  }

  return {start: now, end: segments.at(-1).end, phase: 'half', segments};
}

export function sampleEyeHold(plan, now) {
  if (!plan || !Array.isArray(plan.segments) || !Number.isFinite(now)) {
    return {phase: 'open', completed: false};
  }
  if (now < plan.start) return {phase: 'open', completed: false};
  if (now >= plan.end) return {phase: 'open', completed: true};

  for (const item of plan.segments) {
    if (now < item.end) return {phase: item.phase, completed: false};
  }
  return {phase: 'open', completed: true};
}

export function nextEyeHoldDelay(speaking, random = Math.random) {
  if (typeof random !== 'function') throw new TypeError('random must be a function');
  return ANIMATION_POLICY.holdIntervalMinMs
    + unitRandom(random) * (ANIMATION_POLICY.holdIntervalMaxMs - ANIMATION_POLICY.holdIntervalMinMs);
}
