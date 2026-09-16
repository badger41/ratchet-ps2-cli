import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, unlinkSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const dotnet = process.argv[2] ?? 'dotnet';
const build = spawnSync(dotnet, ['run', '--no-restore', '--', '--type', '*GltfJsonReader', '--type', '*GltfModelInspector', '--verify'], { cwd, encoding: 'utf8' });
assert.ifError(build.error);
assert.equal(build.status, 0, build.stdout + build.stderr);
const {
  RatchetPs2_Core_Gltf_GltfJsonReader: reader,
  RatchetPs2_Core_Gltf_GltfModelInspector: inspector
} = await import(`../bin/probe/index.js?${Date.now()}`);

const cases = JSON.parse(readFileSync(new URL('../bin/probe/json.json', import.meta.url)));
for (const test of cases) {
  const input = Uint8Array.from(Buffer.from(test.input, 'base64'));
  let actual;
  let error = false;
  try { actual = inspector.inspect__Stream(input); }
  catch { error = true; }
  assert.equal(error, test.error);
  if (!error) {
    assert.deepEqual(actual, test.expected);
    const value = reader.read(input);
    assert.deepEqual(value, JSON.parse(new TextDecoder().decode(input)));
    assert.deepEqual(inspector.inspect__JsonElement(value), test.expected);
  }
}

for (const invalid of [null, undefined, NaN, Infinity, new Date(), () => {}, [,]])
  assert.throws(() => inspector.inspect__JsonElement(invalid));
const cyclic = {};
cyclic.self = cyclic;
assert.throws(() => inspector.inspect__JsonElement(cyclic));

const consumerPath = new URL('../bin/probe/json-consumer.ts', import.meta.url);
writeFileSync(consumerPath, `
import { RatchetPs2_Core_Gltf_GltfJsonReader as reader, RatchetPs2_Core_Gltf_GltfModelInspector as inspector, type JsonValue } from './index.js';
const source: JsonValue = { meshes: [], custom: [null, true, 1, 'value'] };
const parsed: JsonValue = reader.read(new TextEncoder().encode(JSON.stringify(source)));
const count: number = inspector.inspect__JsonElement(parsed).meshCount;
void count;
`);
try {
  const tsc = spawnSync(process.execPath, ['node_modules/typescript/bin/tsc', '--noEmit', '--strict', '--target', 'es2022', '--module', 'esnext', '--moduleResolution', 'bundler', fileURLToPath(consumerPath)], { cwd, encoding: 'utf8' });
  assert.ifError(tsc.error);
  assert.equal(tsc.status, 0, tsc.stdout + tsc.stderr);
} finally {
  unlinkSync(consumerPath);
}

console.log(`JSON parity passed ${cases.length} .NET cases plus SDK boundary validation and strict TypeScript.`);
