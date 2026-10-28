import {ANIMATION_POLICY} from './portrait-behavior-policy.mjs';

export {ANIMATION_POLICY};

export const BLINK_DURATION_MS = ANIMATION_POLICY.blinkDurationMs;

export function createBlink(now, random = Math.random, {forceDouble = false} = {}) {
  const double = forceDouble || random() < ANIMATION_POLICY.blinkDoubleChance;
  const doubleStartRange = ANIMATION_POLICY.blinkDoubleStartMaxFraction - ANIMATION_POLICY.blinkDoubleStartMinFraction;
  const secondStart = double
    ? now + BLINK_DURATION_MS * (ANIMATION_POLICY.blinkDoubleStartMinFraction + random() * doubleStartRange)
    : null;
  return {start: now, secondStart, end: (secondStart ?? now) + BLINK_DURATION_MS};
}

export function sampleBlink(blink, now) {
  if (now >= blink.end) return {phase: 'open', completed: true, leg: blink.secondStart === null ? 1 : 2};
  const second = blink.secondStart !== null && now >= blink.secondStart;
  const elapsed = now - (second ? blink.secondStart : blink.start);
  return {
    phase: elapsed < ANIMATION_POLICY.blinkHalfMs ? 'half'
      : elapsed < ANIMATION_POLICY.blinkClosedEndMs ? 'closed' : 'half',
    completed: false,
    leg: second ? 2 : 1,
  };
}

export function nextBlinkDelay(blink, random = Math.random) {
  const shortChance = blink.secondStart === null
    ? ANIMATION_POLICY.blinkSingleShortChance : ANIMATION_POLICY.blinkDoubleShortChance;
  return random() < shortChance
    ? ANIMATION_POLICY.blinkShortDelayMinMs + random() * (ANIMATION_POLICY.blinkShortDelayMaxMs - ANIMATION_POLICY.blinkShortDelayMinMs)
    : ANIMATION_POLICY.blinkLongDelayMinMs + random() * (ANIMATION_POLICY.blinkLongDelayMaxMs - ANIMATION_POLICY.blinkLongDelayMinMs);
}
