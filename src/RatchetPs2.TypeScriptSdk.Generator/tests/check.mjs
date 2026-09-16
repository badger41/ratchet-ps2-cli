import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  RatchetPs2_Games_UYA_Gameplay_UyaLevelSettingsReader as uya,
  RatchetPs2_Games_GC_Gameplay_GcLevelSettingsReader as gc,
  RatchetPs2_Core_IO_BinarySpanReader as reader,
  RatchetPs2_Core_IO_BinaryMagic as magic
} from '../bin/probe/index.js';
const parseUyaLevelSettings = uya.read;

const Header = globalThis.RatchetPs2.Core.Textures.Pif.PifHeader;
const swizzled = new Header(0x50494632, 32, 16, 16, 0x93, 0, 0, 1);
assert.equal(swizzled.HasValidMagic, true);
assert.equal(swizzled.BaseTextureFormat, 0x13);
assert.equal(swizzled.TextureFormatId, 0x13);
assert.equal(swizzled.IsSwizzled, true);
const plain = new Header(0, 32, 16, 16, 0x13, 0, 0, 1);
assert.equal(plain.HasValidMagic, false);
assert.equal(plain.IsSwizzled, false);
assert.equal(plain.BaseTextureFormat, 0x13);

const load = name => JSON.parse(readFileSync(new URL(`../bin/probe/${name}.json`, import.meta.url), 'utf8'));
const exceptionName = error => globalThis.Transpose.getTypeName(error).split('.').at(-1);
const cases = load('parity');
for (const { input, expected, error } of cases) {
  const bytes = Uint8Array.from(Buffer.from(input, 'base64'));
  // Exercise views whose backing buffer contains bytes outside the parser input.
  const backing = new Uint8Array(bytes.length + 19).fill(0xff);
  backing.set(bytes, 7);
  for (const data of [bytes, bytes.buffer, backing.subarray(7, 7 + bytes.length)]) {
    if (error) {
      assert.throws(() => parseUyaLevelSettings(data), ex => exceptionName(ex) === error);
    } else {
      const actual = parseUyaLevelSettings(data);
      assert.deepEqual(actual, { ...expected, trailingBytes: Uint8Array.from(Buffer.from(expected.trailingBytes, 'base64')) });
    }
  }
}
assert.throws(() => parseUyaLevelSettings([0]), TypeError);
assert.throws(() => parseUyaLevelSettings(null), TypeError);

for (const { input, int16, uint16, int32, uint32, uint64 } of load('integers')) {
  const bytes = Uint8Array.from(Buffer.from(input, 'base64'));
  assert.equal(reader.readInt16LittleEndian(bytes, 0), int16);
  assert.equal(reader.readUInt16LittleEndian(bytes, 0), uint16);
  assert.equal(reader.readInt32LittleEndian(bytes, 0), int32);
  assert.equal(reader.readUInt32LittleEndian(bytes, 0), uint32);
  assert.equal(reader.readUInt64LittleEndian(bytes, 0).toString(), uint64);
}
for (const value of [0, -0, 1.25, -12.5, Infinity, -Infinity, NaN]) {
  const bytes = new Uint8Array(4);
  new DataView(bytes.buffer).setFloat32(0, value, true);
  assert.ok(Object.is(reader.readSingleLittleEndian(bytes, 0), value));
}
const bytes = new Uint8Array([1, 2, 3, 4]);
const view = reader.slice(bytes, 1, 2, 'test');
const copy = reader.sliceToArray(bytes, 1, 2, 'test');
bytes[1] = 99;
assert.equal(view[0], 99);
assert.equal(copy[0], 2);
for (const offset of [-1, 1, 0x7fffffff])
  assert.throws(() => reader.readInt32LittleEndian(bytes, offset), ex => exceptionName(ex) === 'InvalidDataException');
assert.equal(reader.checkedOffset(0x7fffffff, 'test'), 0x7fffffff);
assert.throws(() => reader.checkedOffset(0x80000000, 'test'), ex => exceptionName(ex) === 'InvalidDataException');
console.log(`C# → JavaScript parity passed: ${cases.length} parser cases, integer/float boundaries, span views/copies and validation.`);

assert.equal(uya.tryRead(new Uint8Array(0)), null);
assert.deepEqual(uya.tryRead(new Uint8Array(0x84)), uya.read(new Uint8Array(0x84)));
assert.equal(gc.tryRead(new Uint8Array(0x27)), null);
assert.equal(gc.read(new Uint8Array(0x28)).trailingByteLength, 0);
assert.equal(gc.read(new Uint8Array(0x30)).trailingByteLength, 8);

for (const [input, expected] of [[[], 0], [[87, 65], 0], [[87, 65, 68], 2], [[0x32, 0x46, 0x49, 0x50], 1]]) {
  const bytes = Uint8Array.from([255, ...input, 255]);
  assert.equal(magic.detect(bytes.subarray(1, bytes.length - 1)), expected);
}
console.log('Binary magic detection passed: WAD, PIF, truncated input and byte offsets.');
