import assert from 'node:assert/strict';
import { existsSync, readFileSync, writeFileSync, unlinkSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const path = new URL(`../../../src/RatchetPs2.Core/SdkBufferCheck${process.pid}.cs`, import.meta.url);
const consumer = new URL('../bin/probe/buffer-consumer.ts', import.meta.url);
const source = `
using System.Buffers.Binary;

namespace RatchetPs2;
public static class SdkBufferCheck {
    public static byte[] Allocate(int length) => new byte[length];
    public static Span<byte> View(byte[] bytes, int start, int length) => bytes.AsSpan(start, length);
    public static ReadOnlySpan<byte> ReadOnlyView(ReadOnlySpan<byte> bytes, int start, int end) => bytes[start..^end];
    public static Span<byte> OwnedView() => new byte[] { 1, 2, 3, 4 }.AsSpan(1, 2);
    public static ReadOnlySpan<byte> ImplicitView() => new byte[] { 5, 6 };
    public static void Mutate(Span<byte> bytes, int index, byte value) { bytes[index] = value; }
    public static bool CopyWithin(Span<byte> bytes, int source, int length, int destination, bool tryCopy) {
        var input = bytes.Slice(source, length);
        var output = bytes.Slice(destination);
        if (tryCopy) return input.TryCopyTo(output);
        input.CopyTo(output);
        return true;
    }
    public static void FillView(Span<byte> bytes, int start, int length, byte value) => bytes.Slice(start, length).Fill(value);
    public static byte[] Copy(ReadOnlySpan<byte> bytes) => bytes.ToArray();
    public static byte[] Utf8() => "\\0é🙂"u8.ToArray();
    public static string[] Cases() {
        var results = new List<string>();
        foreach (var size in new[] { 0, 1, 4 })
        foreach (var start in new[] { -1, 0, 1, 4, int.MaxValue })
        foreach (var length in new[] { -1, 0, 1, 4, int.MaxValue }) {
            var bytes = new byte[size];
            for (var i = 0; i < size; i++) bytes[i] = (byte)(i + 10);
            try { results.Add(string.Join(",", bytes.AsSpan(start, length).ToArray())); }
            catch (Exception error) { results.Add(error.GetType().Name); }
            try { ReadOnlySpan<byte> span = bytes; results.Add(string.Join(",", span[start..^length].ToArray())); }
            catch (Exception error) { results.Add(error.GetType().Name); }
        }
        byte[]? missing = null;
        results.Add(missing.AsSpan().Length.ToString());
        results.Add(missing.AsSpan(0).Length.ToString());
        results.Add(missing.AsSpan(0, 0).Length.ToString());
        try { missing.AsSpan(1); results.Add("missing guard"); }
        catch (Exception error) { results.Add(error.GetType().Name); }
        var data = new byte[] { 1, 2, 3, 4 };
        var view = data.AsSpan(length: 2, start: 1);
        var copy = view.ToArray();
        view[0] = 99;
        results.Add(string.Join(",", data));
        results.Add(string.Join(",", copy));
        results.Add(string.Join(",", view.Slice(1).ToArray()));
        results.Add(string.Join(",", view.Slice(length: 1, start: 0).ToArray()));
        results.Add(string.Join(",", view[..].ToArray()));
        results.Add(string.Join(",", data.AsSpan()[1..^1].ToArray()));
        results.Add(string.Join(",", System.MemoryExtensions.AsSpan(array: data, start: 1, length: 2).ToArray()));
        ReadOnlySpan<byte> readonlyData = data;
        results.Add(readonlyData[..2].SequenceEqual(data.AsSpan(0, 2)).ToString());
        results.Add(readonlyData[..1].SequenceEqual(readonlyData[..2]).ToString());
        results.Add(readonlyData[..1].SequenceEqual(readonlyData[1..2]).ToString());
        var calls = 0;
        byte[] Receiver() { calls++; return data; }
        results.Add(string.Join(",", Receiver().AsSpan()[calls++..(calls++ + 1)].ToArray()));
        results.Add(calls.ToString());
        foreach (var index in new[] { -1, 0, 3, 4, int.MaxValue }) {
            try { results.Add(readonlyData[index].ToString()); }
            catch (Exception error) { results.Add(error.GetType().Name); }
            try { results.Add(readonlyData[^index].ToString()); }
            catch (Exception error) { results.Add(error.GetType().Name); }
            try { view[index] = 7; results.Add("write"); }
            catch (Exception error) { results.Add(error.GetType().Name); }
        }
        var overlap = new byte[] { 0, 1, 2, 3, 4 };
        overlap.AsSpan(0, 4).CopyTo(overlap.AsSpan(1));
        results.Add(string.Join(",", overlap));
        overlap.AsSpan(1, 4).CopyTo(overlap);
        results.Add(string.Join(",", overlap));
        var shortDestination = new byte[] { 8, 9 };
        try { overlap.AsSpan(0, 3).CopyTo(shortDestination); results.Add("short copy"); }
        catch (Exception error) { results.Add(error.GetType().Name); }
        results.Add(string.Join(",", shortDestination));
        results.Add(overlap.AsSpan(0, 3).TryCopyTo(shortDestination).ToString());
        results.Add(string.Join(",", shortDestination));
        var arrayDestination = new byte[3];
        new byte[] { 6, 7, 8 }.CopyTo(arrayDestination.AsSpan());
        results.Add(string.Join(",", arrayDestination));
        byte[]? optionalSource = new byte[] { 9, 10 };
        optionalSource?.CopyTo(arrayDestination.AsSpan(1));
        optionalSource = null;
        optionalSource?.CopyTo(arrayDestination.AsSpan());
        results.Add(string.Join(",", arrayDestination));
        var filled = new byte[] { 1, 2, 3, 4 };
        filled.AsSpan(1, 2).Clear();
        filled.AsSpan(2).Fill(0xfe);
        results.Add(string.Join(",", filled));
        Array.Fill(filled, (byte)7);
        results.Add(string.Join(",", filled));
        Span<byte> defaultSpan = default;
        ReadOnlySpan<byte> defaultReadOnly = default(ReadOnlySpan<byte>);
        results.Add((defaultSpan.IsEmpty && defaultReadOnly.IsEmpty && ReadOnlySpan<byte>.Empty.IsEmpty).ToString());
        byte[] converted = [1, 2, 3];
        Span<byte> convertedSpan = converted;
        ReadOnlySpan<byte> convertedReadOnly = converted;
        convertedSpan[1] = 9;
        results.Add(string.Join(",", convertedReadOnly.ToArray()));
        Index fromEnd = ^2;
        Range middle = 1..^1;
        results.Add(convertedReadOnly[fromEnd].ToString());
        results.Add(string.Join(",", convertedReadOnly[middle].ToArray()));
        foreach (var indexValue in new[] { new Index(0), new Index(1, true), new Index(0, true), new Index(4) }) {
            try { results.Add(convertedReadOnly[indexValue].ToString()); }
            catch (Exception error) { results.Add(error.GetType().Name); }
        }
        foreach (var rangeValue in new[] { Range.All, new Range(1, ^0), new Range(2, 1), new Range(^4, ^0) }) {
            try { results.Add(string.Join(",", convertedReadOnly[rangeValue].ToArray())); }
            catch (Exception error) { results.Add(error.GetType().Name); }
        }
        convertedSpan[fromEnd] += 250;
        convertedSpan[new Index(0)] |= 0x80;
        results.Add(string.Join(",", converted));
        Increment(ref convertedSpan[2]);
        results.Add(string.Join(",", converted));
        Span<byte> stackBytes = stackalloc byte[4];
        stackBytes.Fill(3);
        results.Add(string.Join(",", stackBytes.ToArray()));
        Span<int> stackInts = stackalloc int[] { 4, 5, 6 };
        stackInts.Clear();
        results.Add($"{stackInts[0]},{stackInts[1]},{stackInts[2]}");
        // DL compact animation deltas must shift before adding to the base value.
        foreach (var delta in new byte[] { 0, 1, 127, 128, 255 }) {
            Span<int> scale = stackalloc int[] { 4096 };
            scale[0] += unchecked((sbyte)delta) << 2;
            results.Add(scale[0].ToString());
            scale[0] -= unchecked((sbyte)delta) + 7;
            results.Add(scale[0].ToString());
        }
        var primitiveInputs = new[] {
            new byte[8],
            Enumerable.Repeat((byte)0xff, 8).ToArray(),
            new byte[] { 0x01, 0x80, 0x23, 0xff, 0x45, 0x67, 0x89, 0xab }
        };
        foreach (var bytes in primitiveInputs) {
            results.Add(BinaryPrimitives.ReadInt16LittleEndian(bytes).ToString());
            results.Add(BinaryPrimitives.ReadUInt16LittleEndian(bytes).ToString());
            results.Add(BinaryPrimitives.ReadUInt16BigEndian(bytes).ToString());
            results.Add(BinaryPrimitives.ReadInt32LittleEndian(bytes).ToString());
            results.Add(BinaryPrimitives.ReadUInt32LittleEndian(bytes).ToString());
            results.Add(BinaryPrimitives.ReadInt32BigEndian(bytes).ToString());
            results.Add(BinaryPrimitives.ReadUInt32BigEndian(bytes).ToString());
            results.Add(BinaryPrimitives.ReadUInt64LittleEndian(bytes).ToString());
            results.Add(BitConverter.SingleToInt32Bits(BinaryPrimitives.ReadSingleLittleEndian(bytes)).ToString());
            results.Add(BitConverter.ToInt16(bytes, 1).ToString());
            results.Add(BitConverter.ToUInt16(bytes, 1).ToString());
            results.Add(BitConverter.ToInt32(bytes, 1).ToString());
            results.Add(BitConverter.ToUInt32(bytes, 1).ToString());
            results.Add(BitConverter.SingleToInt32Bits(BitConverter.ToSingle(bytes, 1)).ToString());
            ReadOnlySpan<byte> bitSpan = bytes.AsSpan(1);
            results.Add(BitConverter.ToUInt16(bitSpan).ToString());
            results.Add(BitConverter.ToUInt32(bitSpan).ToString());
        }
        foreach (var value in new short[] { short.MinValue, -1, 0, 1, short.MaxValue }) {
            var bytes = Enumerable.Repeat((byte)0xcc, 8).ToArray();
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), value);
            results.Add(string.Join(",", bytes));
            results.Add(string.Join(",", BitConverter.GetBytes(value)));
        }
        foreach (var value in new ushort[] { 0, 1, ushort.MaxValue }) {
            var bytes = Enumerable.Repeat((byte)0xcc, 8).ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), value);
            results.Add(string.Join(",", bytes));
            results.Add(string.Join(",", BitConverter.GetBytes(value)));
        }
        foreach (var value in new[] { int.MinValue, -1, 0, 1, int.MaxValue }) {
            var bytes = Enumerable.Repeat((byte)0xcc, 8).ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), value);
            results.Add(string.Join(",", bytes));
            results.Add(string.Join(",", BitConverter.GetBytes(value)));
        }
        foreach (var value in new uint[] { 0, 1, 0x80000000u, uint.MaxValue }) {
            var little = Enumerable.Repeat((byte)0xcc, 8).ToArray();
            var big = Enumerable.Repeat((byte)0xcc, 8).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(little.AsSpan(2), value);
            BinaryPrimitives.WriteUInt32BigEndian(big.AsSpan(2), value);
            results.Add(string.Join(",", little));
            results.Add(string.Join(",", big));
            results.Add(string.Join(",", BitConverter.GetBytes(value)));
        }
        foreach (var bits in new[] { 0, int.MinValue, 1, unchecked((int)0x80000001), 0x3f800000,
                     unchecked((int)0xbf800000), 0x7f7fffff, unchecked((int)0xff7fffff), 0x7f800000,
                     unchecked((int)0xff800000), 0x7fc00000, unchecked((int)0xffc00000) }) {
            var value = BitConverter.Int32BitsToSingle(bits);
            var bytes = Enumerable.Repeat((byte)0xcc, 8).ToArray();
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(2), value);
            results.Add(string.Join(",", bytes));
            results.Add(BitConverter.SingleToInt32Bits(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(2))).ToString());
            results.Add(string.Join(",", BitConverter.GetBytes(value)));
        }
        for (var size = 0; size < 8; size++) {
            var bytes = Enumerable.Repeat((byte)0xcc, size).ToArray();
            try { results.Add(BinaryPrimitives.ReadUInt16BigEndian(bytes).ToString()); }
            catch (Exception error) { results.Add(error.GetType().Name); }
            try { results.Add(BinaryPrimitives.ReadInt32BigEndian(bytes).ToString()); }
            catch (Exception error) { results.Add(error.GetType().Name); }
            try { results.Add(BinaryPrimitives.ReadUInt32LittleEndian(bytes).ToString()); }
            catch (Exception error) { results.Add(error.GetType().Name); }
            try { results.Add(BinaryPrimitives.ReadUInt64LittleEndian(bytes).ToString()); }
            catch (Exception error) { results.Add(error.GetType().Name); }
            if (size < 4) {
                try { BinaryPrimitives.WriteUInt32LittleEndian(bytes, uint.MaxValue); results.Add("short write"); }
                catch (Exception error) { results.Add(error.GetType().Name); }
                results.Add(string.Join(",", bytes));
                try { BinaryPrimitives.WriteSingleLittleEndian(bytes, -1f); results.Add("short float write"); }
                catch (Exception error) { results.Add(error.GetType().Name); }
                results.Add(string.Join(",", bytes));
            }
            if (size < 2) {
                try { BinaryPrimitives.WriteUInt16LittleEndian(bytes, ushort.MaxValue); results.Add("short half write"); }
                catch (Exception error) { results.Add(error.GetType().Name); }
                results.Add(string.Join(",", bytes));
            }
        }
        foreach (var offset in new[] { -1, 5, 8, int.MaxValue }) {
            try { results.Add(BitConverter.ToUInt32(primitiveInputs[2], offset).ToString()); }
            catch (Exception error) { results.Add(error.GetType().Name); }
        }
        return results.ToArray();
    }
    private static void Increment(ref byte value) => value++;
}
`;
writeFileSync(path, source, { flag: 'wx' });
try {
  const result = spawnSync(process.argv[2] ?? 'dotnet', ['run', '--no-restore', '--', '--type', 'RatchetPs2.SdkBufferCheck', '--verify'], { cwd, encoding: 'utf8' });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  const check = spawnSync(process.execPath, ['--input-type=module', '-e', `
    import assert from 'node:assert/strict';
    import { readFileSync } from 'node:fs';
    import { RatchetPs2_SdkBufferCheck as api } from './bin/probe/index.js';
    const expected = JSON.parse(readFileSync('./bin/probe/buffers.json', 'utf8'));
    assert.deepEqual(api.cases(), expected);
    assert.deepEqual(api.utf8(), new TextEncoder().encode('\\0é🙂'));
    assert.deepEqual(api.allocate(4), new Uint8Array(4));
    const bytes = new Uint8Array([0, 1, 2, 3, 4, 5]);
    const view = api.view(bytes.subarray(1, 5), 1, 2);
    bytes[2] = 99;
    assert.equal(view[0], 99);
    view[1] = 88;
    assert.equal(bytes[3], 88);
    assert.deepEqual(api.copy(view), new Uint8Array([99, 88]));
    const owned = api.ownedView();
    assert.deepEqual(Uint8Array.from(owned), new Uint8Array([2, 3]));
    const nested = api.readOnlyView(owned, 1, 0);
    owned[1] = 77;
    assert.equal(nested[0], 77);
    const copy = api.copy(owned);
    owned[0] = 42;
    assert.deepEqual(copy, new Uint8Array([2, 77]));
    api.mutate(owned, 1, 33);
    assert.equal(nested[0], 33);
    api.mutate(view, 0, 44);
    assert.equal(bytes[2], 44);
    const forward = new Uint8Array([0, 1, 2, 3, 4]);
    assert.equal(api.copyWithin(forward, 0, 4, 1, false), true);
    assert.deepEqual(forward, new Uint8Array([0, 0, 1, 2, 3]));
    assert.equal(api.copyWithin(forward, 0, 4, 1, true), true);
    assert.deepEqual(forward, new Uint8Array([0, 0, 0, 1, 2]));
    const tooShort = new Uint8Array([1, 2, 3]);
    assert.equal(api.copyWithin(tooShort, 0, 3, 1, true), false);
    assert.deepEqual(tooShort, new Uint8Array([1, 2, 3]));
    assert.throws(() => api.copyWithin(tooShort, 0, 3, 1, false));
    assert.deepEqual(tooShort, new Uint8Array([1, 2, 3]));
    api.fillView(bytes, 1, 3, 0xaa);
    assert.deepEqual(bytes, new Uint8Array([0, 0xaa, 0xaa, 0xaa, 4, 5]));
    assert.deepEqual(api.copy(api.implicitView()), new Uint8Array([5, 6]));
    assert.throws(() => api.copy([1, 2]), TypeError);
    assert.throws(() => api.copy({length: 1, 0: 2}), TypeError);
    process.stdout.write('Byte buffer parity passed: ' + expected.length + ' .NET results, including views, copies and endian primitives.\\n');
  `], { cwd, encoding: 'utf8' });
  assert.ifError(check.error);
  assert.equal(check.status, 0, check.stdout + check.stderr);
  assert.match(check.stdout, /Byte buffer parity passed:/);
  writeFileSync(consumer, `
    import { RatchetPs2_SdkBufferCheck as api } from './index.js';
    const mutable = api.ownedView();
    mutable[0] = 1;
    api.mutate(mutable, 0, 2);
    const readonly = api.readOnlyView(mutable, 0, 0);
    api.copy(readonly);
    api.copyWithin(mutable, 0, 1, 1, true);
    api.fillView(mutable, 0, 1, 255);
    // @ts-expect-error ReadOnlySpan is read-only.
    readonly[0] = 1;
    // @ts-expect-error Array-like objects cannot impersonate SDK views.
    api.mutate({length: 1, 0: 2}, 0, 3);
  `);
  const types = spawnSync(process.execPath, [fileURLToPath(new URL('../node_modules/typescript/bin/tsc', import.meta.url)),
    '--noEmit', '--strict', '--target', 'es2022', '--module', 'esnext', '--moduleResolution', 'bundler', fileURLToPath(consumer)], { cwd, encoding: 'utf8' });
  assert.ifError(types.error);
  assert.equal(types.status, 0, types.stdout + types.stderr);
  process.stdout.write(check.stdout);
} finally {
  unlinkSync(path);
  if (existsSync(consumer)) unlinkSync(consumer);
}
