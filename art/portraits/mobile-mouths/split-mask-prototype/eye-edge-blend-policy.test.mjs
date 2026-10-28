import assert from 'node:assert/strict';
import test from 'node:test';
import {blendEyeEdgePixels} from './eye-edge-blend-policy.mjs';

function frame(width, height, rgb, alpha = 255) {
  const data = new Uint8ClampedArray(width * height * 4);
  for (let i = 0; i < width * height; i++) data.set([...rgb, alpha], i * 4);
  return {width, height, data};
}

function pixel(image, x, y) {
  const i = (y * image.width + x) * 4;
  return [...image.data.slice(i, i + 4)];
}

test('missing profile returns the original generated frame unchanged', () => {
  const generated = frame(4, 4, [90, 60, 30], 173);
  const before = generated.data.slice();
  const result = blendEyeEdgePixels(generated, frame(4, 4, [0, 0, 0]), [{x:1,y:0,width:2,height:4}], undefined);
  assert.equal(result, generated);
  assert.deepEqual(generated.data, before);
});

test('blends only the two bottom eye rows with the specified inner and outer weights', () => {
  const generated = frame(5, 5, [90, 60, 30], 173);
  const original = frame(5, 5, [0, 0, 0], 255);
  const generatedBefore = generated.data.slice();
  const originalBefore = original.data.slice();
  const regions = [{x:1,y:0,width:3,height:5}];
  const result = blendEyeEdgePixels(generated, original, regions, {bottomRows:2});

  // Region bottom rows: inner row keeps 2/3 generated, outer row keeps 1/3.
  assert.deepEqual(pixel(result, 2, 3), [60, 40, 20, 173]);
  assert.deepEqual(pixel(result, 2, 4), [30, 20, 10, 173]);
  // Top brows, upper eye rows, and every exterior pixel stay exact.
  assert.deepEqual(pixel(result, 2, 2), pixel(generated, 2, 2));
  assert.deepEqual(pixel(result, 0, 4), pixel(generated, 0, 4));
  assert.deepEqual(pixel(result, 4, 3), pixel(generated, 4, 3));
  // Inputs and alpha are preserved.
  assert.deepEqual(generated.data, generatedBefore);
  assert.deepEqual(original.data, originalBefore);
  for (let i = 3; i < result.data.length; i += 4) assert.equal(result.data[i], generated.data[i]);
});

test('refuses invalid eye bounds and profiles', () => {
  const generated = frame(4, 4, [90, 60, 30]);
  const original = frame(4, 4, [0, 0, 0]);
  assert.throws(() => blendEyeEdgePixels(generated, original, [{x:3,y:0,width:2,height:4}], {bottomRows:2}), /bounds/i);
  assert.throws(() => blendEyeEdgePixels(generated, original, [{x:0,y:0,width:2,height:1}], {bottomRows:2}), /bottomRows/i);
  assert.throws(() => blendEyeEdgePixels(generated, original, [{x:0,y:0,width:2,height:4}], {bottomRows:0}), /bottomRows/i);
});
