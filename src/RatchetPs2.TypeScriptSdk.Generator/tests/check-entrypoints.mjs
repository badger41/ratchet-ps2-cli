import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  RatchetPs2_Core_Games_GameId as gameId,
  RatchetPs2_Sdk_FrontendMapPackageBuilder as maps,
  RatchetPs2_Games_UYA_Builders_UyaFrontendMapPackageBuilder as uyaMaps
} from '../bin/probe/index.js';

const runtimeSource = readFileSync(new URL('../bin/probe/ratchetps2.js', import.meta.url), 'utf8');
assert.doesNotMatch(runtimeSource, /globals = global;/, 'Browser SDK must not reference Node global.');
assert.match(runtimeSource, /globals = globalThis;/);

assert.throws(() => maps.buildLevelWad(new Uint8Array(), gameId.uya));
assert.throws(() => maps.buildLevelWad(new Uint8Array(), 0), /Unsupported map game/);
assert.throws(() => uyaMaps.buildCustomMapZip(new Uint8Array()));
assert.throws(() => maps.buildLevelWad([], gameId.uya), /binary buffer/);

console.log('Frontend SDK entry points passed.');
