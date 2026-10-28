import test from 'node:test';
import assert from 'node:assert/strict';
import {sharedVoiceManifestUrl} from './shared-voice-policy.mjs';

const url = 'http://127.0.0.1:8096/mobile-mouths/split-mask-prototype/portrait-catalog.json';
const catalog = {voiceSets: {female: '../shared-voices/female.json', male: '../shared-voices/male.json'}};

test('different female portraits can select distinct per-entry manifests', () => {
  const a = sharedVoiceManifestUrl({id: 'common-077', voiceSet: 'female', voiceManifest: '../women/077.json'}, catalog, url);
  const b = sharedVoiceManifestUrl({id: 'common-078', voiceSet: 'female', voiceManifest: '../women/078.json'}, catalog, url);
  assert.equal(a.href, 'http://127.0.0.1:8096/mobile-mouths/women/077.json');
  assert.equal(b.href, 'http://127.0.0.1:8096/mobile-mouths/women/078.json');
});

test('entries without an override keep the shared female and male manifests', () => {
  const female = sharedVoiceManifestUrl({id: 'common-077', voiceSet: 'female'}, catalog, url);
  const male = sharedVoiceManifestUrl({id: 'common-000', voiceSet: 'male'}, catalog, url);
  assert.equal(female.href, 'http://127.0.0.1:8096/mobile-mouths/shared-voices/female.json');
  assert.equal(male.href, 'http://127.0.0.1:8096/mobile-mouths/shared-voices/male.json');
});

test('an empty or non-string per-entry manifest override is rejected', () => {
  for (const voiceManifest of ['', '   ', 17, null]) {
    assert.throws(
      () => sharedVoiceManifestUrl({id: 'common-077', voiceSet: 'female', voiceManifest}, catalog, url),
      /voiceManifest/,
    );
  }
});

test('an absolute URL is rejected because overrides are catalog-relative paths', () => {
  assert.throws(
    () => sharedVoiceManifestUrl({id: 'common-077', voiceSet: 'female', voiceManifest: 'https://example.test/077.json'}, catalog, url),
    /relativer Pfad/,
  );
});

test('missing or unsupported speaker sex never silently selects the female prototype voice', () => {
  assert.throws(() => sharedVoiceManifestUrl({id: 'common-077'}, catalog, url), /voiceSet/);
  assert.throws(() => sharedVoiceManifestUrl({voiceSet: 'unknown'}, catalog, url), /voiceSet/);
  assert.throws(() => sharedVoiceManifestUrl({voiceSet: 'unknown', voiceManifest: '../women/077.json'}, catalog, url), /voiceSet/);
  assert.throws(() => sharedVoiceManifestUrl({voiceSet: 'male'}, {voiceSets: {}}, url), /male/);
});
