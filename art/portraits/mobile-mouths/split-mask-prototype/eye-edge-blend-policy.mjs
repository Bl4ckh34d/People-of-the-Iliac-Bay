/** Blend only the bottom edge rows of configured eye rectangles. Inputs are immutable. */
export function blendEyeEdgePixels(generated, original, eyeRegions, profile) {
  if (profile == null) return generated;
  validateFrame(generated, 'generated');
  validateFrame(original, 'original');
  if (generated.width !== original.width || generated.height !== original.height) {
    throw new RangeError('Eye edge blend frames must have matching dimensions');
  }
  const {bottomRows} = profile;
  if (!Number.isInteger(bottomRows) || bottomRows < 1) {
    throw new RangeError('eyeEdgeBlend.bottomRows must be a positive integer');
  }
  if (!Array.isArray(eyeRegions) || eyeRegions.length === 0) {
    throw new RangeError('eyeEdgeBlend requires non-empty eyeRegions');
  }

  const normalized = eyeRegions.map((region, index) => {
    if (!region || !['x', 'y', 'width', 'height'].every((key) => Number.isInteger(region[key]))) {
      throw new RangeError(`eyeRegions[${index}] must have integer bounds`);
    }
    const {x, y, width, height} = region;
    if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > generated.width || y + height > generated.height) {
      throw new RangeError(`eyeRegions[${index}] is outside frame bounds`);
    }
    if (bottomRows > height) throw new RangeError('eyeEdgeBlend.bottomRows exceeds eye region height');
    return {x, y, width, height};
  });
  for (let i = 0; i < normalized.length; i++) {
    for (let j = 0; j < i; j++) {
      const a = normalized[i], b = normalized[j];
      if (a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height) {
        throw new RangeError('eyeRegions must not overlap');
      }
    }
  }

  const data = new Uint8ClampedArray(generated.data);
  for (const region of normalized) {
    for (let row = 0; row < bottomRows; row++) {
      const y = region.y + region.height - bottomRows + row;
      const generatedWeight = (bottomRows - row) / (bottomRows + 1);
      const originalWeight = 1 - generatedWeight;
      for (let x = region.x; x < region.x + region.width; x++) {
        const offset = (y * generated.width + x) * 4;
        for (let channel = 0; channel < 3; channel++) {
          data[offset + channel] = Math.round(
            generated.data[offset + channel] * generatedWeight + original.data[offset + channel] * originalWeight,
          );
        }
        data[offset + 3] = generated.data[offset + 3];
      }
    }
  }
  return {width: generated.width, height: generated.height, data};
}

function validateFrame(frame, name) {
  if (!frame || !Number.isInteger(frame.width) || !Number.isInteger(frame.height)
      || frame.width <= 0 || frame.height <= 0
      || !(frame.data instanceof Uint8ClampedArray)
      || frame.data.length !== frame.width * frame.height * 4) {
    throw new TypeError(`${name} eye frame must contain valid RGBA pixel data`);
  }
}
