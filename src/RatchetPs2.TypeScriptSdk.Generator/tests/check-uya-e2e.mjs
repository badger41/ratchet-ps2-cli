import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const dotnet = process.argv[2] ?? 'dotnet';
const wadPath = process.argv[3] ?? '../../test-assets/extractions_uya/level03.wad';
const zipPath = process.argv[4] ?? '../../test-assets/custom_maps_uya/Rushers_Ravine.ntsc.zip';
const sdkDir = `${cwd}/bin/frontend-uya`;
assert.ok(existsSync(new URL(wadPath, pathToFileURL(cwd))), `Missing UYA WAD: ${wadPath}`);
assert.ok(existsSync(new URL(zipPath, pathToFileURL(cwd))), `Missing UYA custom ZIP: ${zipPath}`);

const native = spawnSync(dotnet, [
  'run', '--no-restore', '--',
  '--output', sdkDir,
  '--type', 'RatchetPs2.Games.UYA.Builders.UyaFrontendMapPackageBuilder',
  '--type', 'RatchetPs2.Games.UYA.Gameplay.UyaGameplayBlockReader',
  '--verify-uya', wadPath, zipPath
], { cwd, encoding: 'utf8', stdio: 'inherit' });
assert.ifError(native.error);
assert.equal(native.status, 0, `.NET UYA reference exited with ${native.status ?? native.signal}`);
const report = JSON.parse(readFileSync(`${sdkDir}/report.json`, 'utf8'));
assert.ok(report.compiledFiles.every((path) => !/RatchetPs2\.Games\.(?:RC1|GC)\//.test(path)),
  'UYA frontend dependency closure included RC1 or GC sources.');

const {
  RatchetPs2_Games_UYA_Gameplay_UyaGameplayBlockReader: gameplayReader,
  RatchetPs2_Games_UYA_Builders_UyaFrontendMapPackageBuilder: maps
} = await import(`${pathToFileURL(`${sdkDir}/index.js`)}?v=${Date.now()}`);
const expected = JSON.parse(readFileSync(`${sdkDir}/uya-e2e.json`, 'utf8'));
const wad = readFileSync(new URL(wadPath, pathToFileURL(cwd)));
const zip = readFileSync(new URL(zipPath, pathToFileURL(cwd)));
const gameplayByteFields = new Set([
  'headerBytes', 'payloadBytes', 'mobyLinksBytes', 'tableBytes',
  'dataBytes', 'relativePointerBytes', 'data'
]);
const sha256 = (bytes) => createHash('sha256').update(bytes).digest('hex');

const standardPackage = maps.buildLevelWad(wad);
const actualStandard = describePackage(standardPackage);
writeFileSync(`${sdkDir}/uya-e2e-actual-standard.bin`, standardPackage.packedBytes);
writeFileSync(`${sdkDir}/uya-e2e-actual-standard.json`, JSON.stringify(actualStandard));
comparePackage('standard WAD', actualStandard, expected.standard, standardPackage.packedBytes,
  readFileSync(`${sdkDir}/uya-e2e-standard.bin`));
const customPackage = maps.buildCustomMapZip(zip);
const actualCustom = describePackage(customPackage);
comparePackage('custom ZIP', actualCustom, expected.custom, customPackage.packedBytes,
  readFileSync(`${sdkDir}/uya-e2e-custom.bin`));
console.log(`UYA end-to-end parity passed: ${actualStandard.entries.length} WAD entries, ${actualCustom.entries.length} custom-map entries.`);

function describePackage(packageResult) {
  const entries = packageResult.entries.map((entry) => {
    const bytes = packageResult.packedBytes.subarray(entry.offset, entry.offset + entry.length);
    const isJson = entry.path.endsWith('.json') || entry.path.endsWith('.gltf');
    const comparableBytes = comparableBinary(entry.path, bytes);
    return {
      path: entry.path,
      contentType: entry.contentType,
      offset: entry.offset,
      length: entry.length,
      prefix: isJson ? null : Buffer.from(comparableBytes.subarray(0, 64)).toString('base64'),
      sha256: isJson ? null : sha256(comparableBytes),
      json: isJson
        ? normalizeJson(JSON.parse(new TextDecoder().decode(bytes)))
        : null
    };
  });
  const gameplayEntry = packageResult.entries.find((entry) => entry.path === 'gameplay/gameplay_core.bin');
  assert.ok(gameplayEntry, 'Render package is missing gameplay/gameplay_core.bin.');
  const gameplayBytes = packageResult.packedBytes.subarray(
    gameplayEntry.offset,
    gameplayEntry.offset + gameplayEntry.length);
  const gameplay = gameplayReader.readCore__ReadOnlySpanOfByte(gameplayBytes);
  return { entries, gameplay: normalizeGameplay(gameplay) };
}

function comparableBinary(path, bytes) {
  if (!path.endsWith('.buffer.bin')) return bytes;
  const result = Uint8Array.from(bytes);
  for (let offset = 0; offset + 4 <= result.length; offset += 4)
    if (result[offset] === 0 && result[offset + 1] === 0 && result[offset + 2] === 0 && result[offset + 3] === 0x80)
      result[offset + 3] = 0;
  return result;
}

function comparePackage(label, actual, expected, actualPackedBytes, expectedPackedBytes) {
  assert.equal(actual.entries.length, expected.entries.length, `${label} entry count`);
  for (let index = 0; index < actual.entries.length; index++) {
    const actualEntry = actual.entries[index];
    const expectedEntry = expected.entries[index];
    assert.equal(actualEntry.path, expectedEntry.path, `${label} entry ${index} path`);
    assert.equal(actualEntry.contentType, expectedEntry.contentType, `${label} ${actualEntry.path} content type`);
    if (actualEntry.json !== null) compareJson(
      actualEntry.path.includes('/tie/') ? withoutGltfBufferLayout(actualEntry.json) : actualEntry.json,
      actualEntry.path.includes('/tie/') ? withoutGltfBufferLayout(expectedEntry.json) : expectedEntry.json,
      `${label} ${actualEntry.path}`);
    else {
      if (actualEntry.sha256 !== expectedEntry.sha256 && actualEntry.path.endsWith('.buffer.bin'))
        compareGltfBuffer(label, actual, expected, actualEntry, expectedEntry, actualPackedBytes, expectedPackedBytes);
      else {
        assert.equal(actualEntry.length, expectedEntry.length, `${label} ${actualEntry.path} length`);
        assert.equal(actualEntry.sha256, expectedEntry.sha256,
          `${label} ${actualEntry.path} SHA-256; prefixes ${actualEntry.prefix} != ${expectedEntry.prefix}`);
      }
    }
  }
  assert.deepEqual(normalizeGameplay(actual.gameplay), normalizeGameplay(expected.gameplay), `${label} gameplay`);
}

function compareGltfBuffer(label, actual, expected, actualEntry, expectedEntry, actualPackedBytes, expectedPackedBytes) {
  const actualBytes = actualPackedBytes.subarray(actualEntry.offset, actualEntry.offset + actualEntry.length);
  const expectedBytes = expectedPackedBytes.subarray(expectedEntry.offset, expectedEntry.offset + expectedEntry.length);
  const gltfPath = actualEntry.path.replace(/\.buffer\.bin$/, '.gltf');
  const gltf = actual.entries.find((entry) => entry.path === gltfPath)?.json;
  const expectedGltf = expected.entries.find((entry) => entry.path === gltfPath)?.json;
  assert.ok(gltf && expectedGltf, `${label} ${actualEntry.path} has no matching GLTF`);
  if (actualEntry.path.includes('/tie/')) {
    compareRenderedPrimitives(label, actualEntry.path, gltf, expectedGltf, actualBytes, expectedBytes);
    return;
  }
  assert.equal(actualBytes.length, expectedBytes.length, `${label} ${actualEntry.path} length`);
  const comparable = Uint8Array.from(actualBytes);
  const components = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT2: 4, MAT3: 9, MAT4: 16 };
  for (const accessor of gltf.accessors ?? []) {
    if (accessor.componentType !== 5126) continue;
    const view = gltf.bufferViews[accessor.bufferView];
    const componentCount = components[accessor.type];
    const stride = view.byteStride ?? componentCount * 4;
    for (let item = 0; item < accessor.count; item++)
      for (let component = 0; component < componentCount; component++) {
        const offset = view.byteOffset + (accessor.byteOffset ?? 0) + item * stride + component * 4;
        const actualValue = new DataView(actualBytes.buffer, actualBytes.byteOffset + offset, 4).getFloat32(0, true);
        const expectedValue = new DataView(expectedBytes.buffer, expectedBytes.byteOffset + offset, 4).getFloat32(0, true);
        const close = numbersClose(actualValue, expectedValue);
        assert.ok(close, `${label} ${actualEntry.path} float mismatch at byte ${offset}: ${actualValue} != ${expectedValue}`);
        comparable.set(expectedBytes.subarray(offset, offset + 4), offset);
      }
  }
  assert.equal(sha256(comparable), sha256(expectedBytes), `${label} ${actualEntry.path} non-float bytes`);
}

function compareRenderedPrimitives(label, path, actualGltf, expectedGltf, actualBytes, expectedBytes) {
  assert.equal(actualGltf.meshes.length, expectedGltf.meshes.length, `${label} ${path} mesh count`);
  for (let meshIndex = 0; meshIndex < actualGltf.meshes.length; meshIndex++) {
    const actualPrimitives = actualGltf.meshes[meshIndex].primitives;
    const expectedPrimitives = expectedGltf.meshes[meshIndex].primitives;
    assert.equal(actualPrimitives.length, expectedPrimitives.length, `${label} ${path} primitive count`);
    for (let primitiveIndex = 0; primitiveIndex < actualPrimitives.length; primitiveIndex++) {
      const actualPrimitive = actualPrimitives[primitiveIndex];
      const expectedPrimitive = expectedPrimitives[primitiveIndex];
      const actualIndices = readAccessor(actualGltf, actualBytes, actualPrimitive.indices);
      const expectedIndices = readAccessor(expectedGltf, expectedBytes, expectedPrimitive.indices);
      assert.equal(actualIndices.length, expectedIndices.length, `${label} ${path} index count`);
      assert.deepEqual(Object.keys(actualPrimitive.attributes), Object.keys(expectedPrimitive.attributes), `${label} ${path} attributes`);
      for (let drawIndex = 0; drawIndex < actualIndices.length; drawIndex++)
        for (const attribute of Object.keys(actualPrimitive.attributes)) {
          const actualValue = readAccessor(actualGltf, actualBytes, actualPrimitive.attributes[attribute], actualIndices[drawIndex]);
          const expectedValue = readAccessor(expectedGltf, expectedBytes, expectedPrimitive.attributes[attribute], expectedIndices[drawIndex]);
          assert.equal(actualValue.length, expectedValue.length, `${label} ${path} ${attribute} component count`);
          for (let component = 0; component < actualValue.length; component++)
            assert.ok(numbersClose(actualValue[component], expectedValue[component]),
              `${label} ${path} ${attribute} draw ${drawIndex} component ${component}`);
        }
    }
  }
}

function readAccessor(gltf, bytes, accessorIndex, itemIndex) {
  const accessor = gltf.accessors[accessorIndex];
  const view = gltf.bufferViews[accessor.bufferView];
  const componentCounts = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT2: 4, MAT3: 9, MAT4: 16 };
  const formats = {
    5120: [1, 'getInt8'], 5121: [1, 'getUint8'], 5122: [2, 'getInt16'],
    5123: [2, 'getUint16'], 5125: [4, 'getUint32'], 5126: [4, 'getFloat32']
  };
  const [size, getter] = formats[accessor.componentType];
  const count = componentCounts[accessor.type];
  const stride = view.byteStride ?? count * size;
  const readItem = (index) => Array.from({ length: count }, (_, component) =>
    new DataView(bytes.buffer, bytes.byteOffset + view.byteOffset + (accessor.byteOffset ?? 0) + index * stride + component * size, size)[getter](0, true));
  return itemIndex === undefined
    ? Array.from({ length: accessor.count }, (_, index) => readItem(index)[0])
    : readItem(itemIndex);
}

function numbersClose(actual, expected) {
  return Object.is(actual, expected)
    || actual === 0 && expected === 0
    || Number.isNaN(actual) && Number.isNaN(expected)
    || Math.abs(actual - expected) <= 1e-5 * Math.max(1, Math.abs(actual), Math.abs(expected));
}

function compareJson(actual, expected, path) {
  if (typeof actual === 'number' && typeof expected === 'number') {
    assert.ok(Number.isInteger(actual) && Number.isInteger(expected) ? actual === expected : numbersClose(actual, expected), path);
    return;
  }
  if (Array.isArray(actual) && Array.isArray(expected)) {
    assert.equal(actual.length, expected.length, `${path} length`);
    for (let index = 0; index < actual.length; index++) compareJson(actual[index], expected[index], `${path}[${index}]`);
    return;
  }
  if (actual && expected && typeof actual === 'object' && typeof expected === 'object') {
    assert.deepEqual(Object.keys(actual), Object.keys(expected), `${path} keys`);
    for (const key of Object.keys(actual)) compareJson(actual[key], expected[key], `${path}.${key}`);
    return;
  }
  assert.deepEqual(actual, expected, path);
}

function withoutGltfBufferLayout(value) {
  const result = structuredClone(value);
  delete result.buffers;
  delete result.bufferViews;
  delete result.accessors;
  for (const mesh of result.meshes ?? [])
    for (const primitive of mesh.primitives ?? []) {
      primitive.attributes = Object.keys(primitive.attributes);
      primitive.indices = true;
    }
  return result;
}

function normalizeJson(value) {
  if (typeof value === 'number') return value === 0 ? 0 : Number.isInteger(value) ? value : Math.fround(value);
  if (Array.isArray(value)) return value.map(normalizeJson);
  if (value && typeof value === 'object') {
    const result = {};
    for (const [key, child] of Object.entries(value)) {
      if (key !== 'PerformanceTimings' && key !== 'performanceTimings') result[key] = normalizeJson(child);
    }
    return result;
  }
  return value;
}

function normalizeGameplay(value, key = '') {
  if (gameplayByteFields.has(key)) return undefined;
  if (typeof value === 'number' && !Number.isFinite(value)) return String(value);
  if (typeof value === 'number') return value === 0 ? 0 : Number.isInteger(value) ? value : Math.fround(value);
  if (value instanceof Uint8Array) return Buffer.from(value).toString('base64');
  if (Array.isArray(value)) return value.map((child) => normalizeGameplay(child));
  if (value && typeof value === 'object') {
    const result = {};
    for (const childKey of Object.keys(value)) {
      const child = normalizeGameplay(value[childKey], childKey);
      if (child !== undefined) result[childKey] = child;
    }
    return result;
  }
  return value;
}
