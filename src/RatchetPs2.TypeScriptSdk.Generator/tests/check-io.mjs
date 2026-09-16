import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, unlinkSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const sourcePath = new URL(`../../../src/RatchetPs2.Core/SdkIoCheck${process.pid}.cs`, import.meta.url);
const consumerPath = new URL('../bin/probe/io-consumer.ts', import.meta.url);
const source = `
namespace RatchetPs2;

public static class SdkIoCheck {
    public static void Write(BinaryWriter writer, int number, ushort small, float value) {
        writer.Write(number);
        writer.Write(small);
        writer.Write(value);
    }
    public static (int Number, ushort Small, float Value) Read(BinaryReader reader) =>
        (reader.ReadInt32(), reader.ReadUInt16(), reader.ReadSingle());
    public static (byte First, byte Second) ReadBytes(BinaryReader reader) =>
        (reader.ReadByte(), reader.ReadByte());
    public static byte[] ReadExactly(Stream stream, int count) {
        var bytes = new byte[count];
        stream.ReadExactly(bytes);
        return bytes;
    }
    public static byte[] Cases() {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        Write(writer, -123456789, 0xabcd, -3.5f);
        return stream.ToArray();
    }
}
`;
const consumer = `
import { createMemoryStream, RatchetPs2_SdkIoCheck as io, RatchetPs2_Core_IO_Vif_Ps2VifPacket as vif, RatchetPs2_Core_Textures_Pif_PifReader as reader, RatchetPs2_Core_Textures_Pif_PifWriter as writer, RatchetPs2_Core_Textures_Png_PngTextureMetadataReader as png, RatchetPs2_Games_UYA_Level_UyaCustomMapZipUnpacker as maps, type MemoryStream, type StreamInput } from './index.js';
const stream: MemoryStream = createMemoryStream(new Uint8Array());
const input: StreamInput = stream;
io.write(input, 1, 2, 3);
const values: readonly [number, number, number] = io.read(stream);
const bytes: Uint8Array = io.readExactly(new ArrayBuffer(4), 4);
const header = reader.readHeader(new Uint8Array(32));
const texture = writer.createIndexed8(1, 1, new Uint8Array(1024), new Uint8Array(1));
writer.write__Stream_PifTextureData(stream, texture);
vif.writeHeader(stream, 1, 2, 3);
const image = png.readRgba32(new Uint8Array());
const map = maps.unpack__ByteArray(new Uint8Array());
void [values, bytes, header, image, map, stream.position, stream.length];
`;

writeFileSync(sourcePath, source, { flag: 'wx' });
try {
  const build = spawnSync(process.argv[2] ?? 'dotnet', ['run', '--no-restore', '--', '--type', 'RatchetPs2.SdkIoCheck', '--type', '*PifReader', '--type', '*PifWriter', '--type', '*Ps2VifPacket', '--type', '*PngTextureMetadataReader', '--type', '*UyaCustomMapZipUnpacker', '--verify'], { cwd, encoding: 'utf8' });
  assert.ifError(build.error);
  assert.equal(build.status, 0, build.stdout + build.stderr);

  const { createMemoryStream, RatchetPs2_SdkIoCheck: io, RatchetPs2_Core_IO_Vif_Ps2VifPacket: vif, RatchetPs2_Core_Textures_Pif_PifReader: reader, RatchetPs2_Core_Textures_Pif_PifWriter: writer, RatchetPs2_Core_Textures_Png_PngTextureMetadataReader: png, RatchetPs2_Games_UYA_Level_UyaCustomMapZipUnpacker: maps } = await import(`../bin/probe/index.js?${Date.now()}`);
  const expected = Uint8Array.from(Buffer.from(JSON.parse(readFileSync(new URL('../bin/probe/io.json', import.meta.url), 'utf8')), 'base64'));
  const output = createMemoryStream();
  io.write(output, -123456789, 0xabcd, -3.5);
  assert.deepEqual(output.toArray(), expected);
  assert.deepEqual(io.read(output.seek(0n) === 0n ? output : null), [-123456789, 0xabcd, -3.5]);
  assert.deepEqual(io.readBytes(new Uint8Array([0x12, 0x34])), [0x12, 0x34]);
  assert.equal(output.position, 10n);
  assert.equal(output.length, 10n);
  assert.equal(output.seek(-4n, 2), 6n);

  const sourceBytes = new Uint8Array([1, 2, 3, 4]);
  const copied = createMemoryStream(sourceBytes);
  vif.writeHeader(copied, 0x2211, 0x33, 0x44);
  assert.deepEqual(copied.toArray(), new Uint8Array([0x11, 0x22, 0x33, 0x44]));
  assert.deepEqual(sourceBytes, new Uint8Array([1, 2, 3, 4]));
  assert.deepEqual(io.readExactly(new Uint8Array([9, 8, 7]), 3), new Uint8Array([9, 8, 7]));
  assert.throws(() => io.readExactly(new Uint8Array([9]), 2));
  assert.throws(() => io.read(new Uint8Array([1])));
  assert.throws(() => createMemoryStream({}), TypeError);
  assert.throws(() => output.seek(0n, 3), TypeError);

  const palette = Uint8Array.from({ length: 1024 }, (_, index) => index & 255);
  const pixels = new Uint8Array([4, 3, 2, 1]);
  const texture = writer.createIndexed8(2, 2, palette, pixels);
  const pifBytes = writer.write__PifTextureData(texture);
  const pifStream = createMemoryStream();
  writer.write__Stream_PifTextureData(pifStream, texture);
  assert.deepEqual(pifStream.toArray(), pifBytes);
  pifStream.seek(0n);
  const header = reader.readHeader(pifStream);
  assert.deepEqual([header.uSize, header.vSize, header.fileSize], [2, 2, pifBytes.length]);
  assert.equal(pifStream.position, 32n);
  assert.deepEqual(reader.read__Stream(pifBytes).pixelData, pixels);

  const fixtures = JSON.parse(readFileSync(new URL('../bin/probe/io-compression.json', import.meta.url), 'utf8'));
  const decode = value => Uint8Array.from(Buffer.from(value, 'base64'));
  const uint32 = value => new Uint8Array([(value >>> 24) & 255, (value >>> 16) & 255, (value >>> 8) & 255, value & 255]);
  const join = (...values) => {
    const result = new Uint8Array(values.reduce((length, value) => length + value.length, 0));
    let offset = 0;
    for (const value of values) { result.set(value, offset); offset += value.length; }
    return result;
  };
  const chunk = (name, data) => join(uint32(data.length), new TextEncoder().encode(name), data, new Uint8Array(4));
  const pngBytes = zlib => {
    const ihdr = new Uint8Array(13);
    ihdr.set(uint32(1), 0);
    ihdr.set(uint32(1), 4);
    ihdr.set([8, 6, 0, 0, 0], 8);
    return join(new Uint8Array([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib), chunk('IEND', new Uint8Array()));
  };
  assert.deepEqual(png.readRgba32(pngBytes(decode(fixtures.zlib))).pixelData, decode(fixtures.raw).slice(1));
  assert.equal(fixtures.truncatedZlibError, null);
  assert.deepEqual(png.readRgba32(pngBytes(decode(fixtures.truncatedZlib))).pixelData, decode(fixtures.raw).slice(1));
  assert.ok(fixtures.malformedZlibError);
  assert.throws(() => png.readRgba32(pngBytes(decode(fixtures.malformedZlib))));

  const zip = decode(fixtures.zip);
  assert.equal(fixtures.validZipError, null);
  const map = maps.unpack__ByteArray(zip);
  assert.deepEqual([map.levelDataWadEntryName, map.worldEntryName, map.levelDataWadByteLength, map.worldByteLength],
    ['folder/map.wad', 'folder/map.world', 0x58, 0]);
  assert.deepEqual(map.files.map(file => [file.path, file.bytes.length]),
    [['level_wad/level_data.wad', 0x58], ['level_data/header.bin', 0x58]]);
  assert.deepEqual(maps.unpack__Stream(createMemoryStream(zip)).files.map(file => file.path), map.files.map(file => file.path));
  for (const [bytes, error] of [['missingZip', 'missingZipError'], ['duplicateZip', 'duplicateZipError'], ['truncatedZip', 'truncatedZipError']]) {
    assert.ok(fixtures[error]);
    assert.throws(() => maps.unpack__ByteArray(decode(fixtures[bytes])));
  }

  writeFileSync(consumerPath, consumer);
  const tsc = spawnSync(process.execPath, ['node_modules/typescript/bin/tsc', '--noEmit', '--strict', '--target', 'es2022', '--module', 'esnext', '--moduleResolution', 'bundler', fileURLToPath(consumerPath)], { cwd, encoding: 'utf8' });
  assert.ifError(tsc.error);
  assert.equal(tsc.status, 0, tsc.stdout + tsc.stderr);
  process.stdout.write('I/O parity passed: streams, binary I/O, PIF, zlib/PNG, ZIP failures and strict TypeScript.\n');
} finally {
  try { unlinkSync(consumerPath); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  unlinkSync(sourcePath);
}
