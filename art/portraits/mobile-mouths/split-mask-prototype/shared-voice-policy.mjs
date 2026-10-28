export function sharedVoiceManifestUrl(item, catalog, catalogUrl) {
  if (item.voiceSet !== 'female' && item.voiceSet !== 'male') {
    throw Error(`${item.id || 'Portrait'}: voiceSet muss female oder male sein.`);
  }
  if (item.voiceManifest !== undefined) {
    const override = item.voiceManifest;
    if (typeof override !== 'string' || !override.trim()) {
      throw Error(`${item.id || 'Portrait'}: voiceManifest muss ein relativer Pfad sein.`);
    }
    if (override !== override.trim() || override.startsWith('/') || override.startsWith('\\') || /^[a-z][a-z0-9+.-]*:/i.test(override)) {
      throw Error(`${item.id || 'Portrait'}: voiceManifest muss ein relativer Pfad sein.`);
    }
    return new URL(override, catalogUrl);
  }
  const path = catalog.voiceSets?.[item.voiceSet];
  if (typeof path !== 'string' || !path.trim()) {
    throw Error(`Gemeinsames Stimmset fehlt: ${item.voiceSet}.`);
  }
  return new URL(path, catalogUrl);
}
