import assert from 'node:assert/strict';
import { deflateSync } from 'node:zlib';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const dotnet = process.argv[2] ?? 'dotnet';
const build = spawnSync(dotnet, [
  'run', '--no-restore', '--', '--type', '*StaticModelGltfImporter', '--verify',
], { cwd, encoding: 'utf8' });
assert.ifError(build.error);
assert.equal(build.status, 0, build.stdout + build.stderr);
const { RatchetPs2_Core_Gltf_StaticModelGltfImporter: importer } =
  await import(`../bin/probe/index.js?${Date.now()}`);

const crcTable = Array.from({ length: 256 }, (_, index) => {
  let value = index;
  for (let bit = 0; bit < 8; bit++)
    value = (value & 1) ? 0xedb88320 ^ (value >>> 1) : value >>> 1;
  return value >>> 0;
});
const crc32 = bytes => {
  let value = 0xffffffff;
  for (const byte of bytes) value = crcTable[(value ^ byte) & 0xff] ^ (value >>> 8);
  return (value ^ 0xffffffff) >>> 0;
};
const pngChunk = (name, data) => {
  const type = Buffer.from(name, 'ascii');
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(Buffer.concat([type, data])));
  return Buffer.concat([length, type, data, crc]);
};
const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(1, 0);
ihdr.writeUInt32BE(1, 4);
ihdr[8] = 8;
ihdr[9] = 6;
const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  pngChunk('IHDR', ihdr),
  pngChunk('IDAT', deflateSync(Buffer.from([0, 12, 34, 56, 128]))),
  pngChunk('IEND', Buffer.alloc(0)),
]);

const geometry = Buffer.alloc(114);
[
  0, 0, 0, 1, 0, 0, 0, 1, 0,
  0, 0, 1, 0, 0, 1, 0, 0, 1,
  0, 0, 1, 0, 0, 1,
].forEach((value, index) => geometry.writeFloatLE(value, index * 4));
Buffer.from([255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 255]).copy(geometry, 96);
geometry.writeUInt16LE(0, 108);
geometry.writeUInt16LE(1, 110);
geometry.writeUInt16LE(2, 112);

const document = {
  asset: { version: '2.0' },
  buffers: [{ uri: 'buffers/model.bin', byteLength: 114 }],
  bufferViews: [
    { buffer: 0, byteOffset: 0, byteLength: 36 },
    { buffer: 0, byteOffset: 36, byteLength: 36 },
    { buffer: 0, byteOffset: 72, byteLength: 24 },
    { buffer: 0, byteOffset: 96, byteLength: 12 },
    { buffer: 0, byteOffset: 108, byteLength: 6 },
  ],
  accessors: [
    { bufferView: 0, componentType: 5126, count: 3, type: 'VEC3' },
    { bufferView: 1, componentType: 5126, count: 3, type: 'VEC3' },
    { bufferView: 2, componentType: 5126, count: 3, type: 'VEC2' },
    { bufferView: 3, componentType: 5121, normalized: true, count: 3, type: 'VEC4' },
    { bufferView: 4, componentType: 5123, count: 3, type: 'SCALAR' },
  ],
  images: [{ name: 'Albedo', uri: 'textures/albedo.png' }],
  samplers: [{ magFilter: 9729, minFilter: 9987, wrapS: 33071, wrapT: 10497 }],
  textures: [{ name: 'Albedo', source: 0, sampler: 0 }],
  materials: [{
    name: 'Paint',
    pbrMetallicRoughness: {
      baseColorFactor: [0.5, 0.75, 1, 0.5],
      baseColorTexture: { index: 0 },
      metallicFactor: 0,
      roughnessFactor: 0.8,
    },
    alphaMode: 'BLEND',
    doubleSided: true,
  }],
  meshes: [{ name: 'Triangle', primitives: [{
    attributes: { POSITION: 0, NORMAL: 1, TEXCOORD_0: 2, COLOR_0: 3 },
    indices: 4,
    material: 0,
    mode: 4,
  }] }],
  nodes: [{ translation: [2, 3, 4], children: [1] }, { mesh: 0, scale: [-1, 2, 1] }],
  scenes: [{ nodes: [0] }],
  scene: 0,
};
const source = new TextEncoder().encode(JSON.stringify(document));
assert.deepEqual(importer.discoverDependencies(source, 'model.gltf').map(item => item.uri), [
  'buffers/model.bin',
  'textures/albedo.png',
]);
const result = importer.import(source, 'model.gltf', [
  { uri: 'textures/albedo.png', bytes: png },
  { uri: 'buffers/model.bin', bytes: geometry },
]);
assert.equal(
  result.contentSha256,
  'a053be57678ce6ca4954d1e3b0e6741aa1afff85fc62b13728301c2f0c5a26eb',
);
const excessPng = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  pngChunk('IHDR', ihdr),
  pngChunk('IDAT', deflateSync(Buffer.alloc(1024 * 1024))),
  pngChunk('IEND', Buffer.alloc(0)),
]);
assert.throws(
  () => importer.import(source, 'model.gltf', [
    { uri: 'textures/albedo.png', bytes: excessPng },
    { uri: 'buffers/model.bin', bytes: geometry },
  ]),
  /model\.gltf image 0/,
);
console.log(`Static-model browser parity passed (${result.contentSha256}).`);
