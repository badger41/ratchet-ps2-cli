import assert from 'node:assert/strict';
import {
  RatchetPs2_Core_Games_GameId as gameId,
  RatchetPs2_Sdk_FrontendMapPackageBuilder as maps
} from '../bin/probe/index.js';

assert.throws(() => maps.buildLevelWad(new Uint8Array(), gameId.uya));
assert.throws(() => maps.buildLevelWad(new Uint8Array(), 0), /Unsupported map game/);
assert.throws(() => maps.buildUyaCustomMapZip(new Uint8Array()));
assert.throws(() => maps.buildLevelWad([], gameId.uya), /binary buffer/);

console.log('Frontend SDK entry points passed.');
