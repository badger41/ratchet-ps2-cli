import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, unlinkSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const sourcePath = new URL(`../../../src/RatchetPs2.Core/SdkValueBindingCheck${process.pid}.cs`, import.meta.url);
const consumerPath = new URL('../bin/probe/value-consumer.ts', import.meta.url);
const source = `
using System.Numerics;

namespace RatchetPs2;

public sealed record SdkEnvelope(string Name, Vector3 Origin, IReadOnlyList<uint> Indices, byte[] Payload);

public sealed class SdkOptions {
    public bool Enabled { get; init; } = true;
    public int Limit { get; init; } = 7;
    public Vector2 Offset { get; init; } = Vector2.One;
    public string? Label { get; init; }
}

public enum SdkMode : byte { First = 1, Second = 2 }

public sealed class SdkCounter {
    public SdkCounter(int value) => Value = value;
    public int Value { get; private set; }
    public int Add(int delta = 1) => Value += delta;
    public SdkCounter Reset(int value) { Value = value; return this; }
}

public sealed class SdkOpaque {
    internal SdkOpaque(int value) => Value = value;
    public int Value { get; private set; }
    public int Increment() => ++Value;
}

public static class SdkValueBindingCheck {
    public static Vector3 Origin => new(1f, 2f, 3f);
    public static IReadOnlyList<Vector2> Axes => new[] { Vector2.One, new Vector2(-1f) };
    public static Vector2 EchoVector2(Vector2 value) => value;
    public static Vector3 ScaleVector3(Vector3 value, float scale) => value * scale;
    public static Vector4 EchoVector4(Vector4 value) => value;
    public static Quaternion EchoQuaternion(Quaternion value) => value;
    public static Matrix4x4 EchoMatrix(Matrix4x4 value) => value;
    public static Vector2? MaybeVector2(Vector2? value, bool keep) => keep ? value : null;
    public static IReadOnlyList<Vector3> Points() => new[] { Vector3.Zero, new Vector3(1f, 2f, 3f) };
    public static uint SumIndices(IReadOnlyList<uint> values) {
        var result = 0u;
        for (var i = 0; i < values.Count; i++) result += values[i];
        return result;
    }
    public static int[] CheckedUIntIndices(IReadOnlyList<uint> values) {
        var result = new int[values.Count];
        for (var i = 0; i < values.Count; i++) result[i] = checked((int)values[i]);
        return result;
    }
    public static int SumValues(IEnumerable<int> values) {
        var result = 0;
        foreach (var value in values) result += value;
        return result;
    }
    public static int LastArray(int[] values) => values[^1];
    public static int IncrementLast(List<int> values) {
        values[^1] = values[^1] + 1;
        return values[^1];
    }
    public static bool[] BooleanCompounds() {
        var and = true; and &= false;
        var or = false; or |= true;
        var xor = true; xor ^= true;
        return [and, or, xor];
    }
    public static IReadOnlyList<Vector3> EchoPoints(IReadOnlyList<Vector3> values) => values;
    public static IReadOnlyList<Vector2> DoubleUvs(List<Vector2> values) {
        for (var i = 0; i < values.Count; i++) values[i] *= new Vector2(2f);
        return values;
    }
    public static IReadOnlyList<byte[]> EchoBuffers(IReadOnlyList<byte[]> values) => values;
    public static SdkEnvelope EchoEnvelope(SdkEnvelope value) => value;
    public static SdkEnvelope? MaybeEnvelope(SdkEnvelope? value) => value;
    public static IReadOnlyList<SdkEnvelope> EchoEnvelopes(IReadOnlyList<SdkEnvelope> values) => values;
    public static SdkOptions EchoOptions(SdkOptions value) => value;
    public static IReadOnlyDictionary<int, Vector2> EchoMap(IReadOnlyDictionary<int, Vector2> value) => value;
    public static IReadOnlyDictionary<int, Quaternion[]> EchoTracks(IReadOnlyDictionary<int, Quaternion[]> value) => value;
    public static IReadOnlySet<int> EchoSet(IReadOnlySet<int> value) => value;
    public static (byte[] Bytes, Vector2 Offset) EchoTuple((byte[] Bytes, Vector2 Offset) value) => value;
    public static ReadOnlyMemory<byte> EchoMemory(ReadOnlyMemory<byte> value) => value;
    public static byte FirstMemory(ReadOnlyMemory<byte> value) => value.Span[0];
    public static SdkMode EchoMode(SdkMode value) => value;
    public static long EchoInt64(long value) => value;
    public static bool TryPair(int value, out int doubled, out string error) {
        doubled = value * 2;
        error = value >= 0 ? string.Empty : "negative";
        return value >= 0;
    }
    public static SdkOpaque CreateOpaque(int value) => new(value);
    public static int ReadOpaque(SdkOpaque value) => value.Value;
}
`;
const consumer = `
import { RatchetPs2_SdkValueBindingCheck as api, RatchetPs2_SdkCounter as counters, RatchetPs2_Core_Geometry_Bounds3 as bounds, RatchetPs2_Core_IO_Vif_Ps2VifTopology as topology, RatchetPs2_Core_Wad_Models_PackedFilePackageBuilder as packer, RatchetPs2_Games_GC_Skyboxes_GcSkyRotationReader as skyRotations, type System_Numerics_Quaternion, type System_Numerics_Vector2, type System_Numerics_Vector3, type RatchetPs2_SdkCounter, type RatchetPs2_SdkEnvelopeInput, type RatchetPs2_Core_Geometry_Bounds3, type RatchetPs2_Core_Wad_Models_PackedFileInput } from './index.js';
const point: System_Numerics_Vector3 = { x: 1, y: 2, z: 3 };
const scaled: System_Numerics_Vector3 = api.scaleVector3(point, 2);
const maybe = api.maybeVector2({ x: 1, y: 2 }, true);
const points: ReadonlyArray<System_Numerics_Vector3> = api.points();
const echoed: ReadonlyArray<System_Numerics_Vector3> = api.echoPoints(points);
const sum: number = api.sumIndices([1, 2, 3]);
const origin: System_Numerics_Vector3 = api.origin;
const envelope: RatchetPs2_SdkEnvelopeInput = { name: 'map', origin: point, indices: [1, 2], payload: new Uint8Array() };
const envelopeName: string = api.echoEnvelope(envelope).name;
const enabled: boolean = api.echoOptions({}).enabled;
const files: ReadonlyArray<RatchetPs2_Core_Wad_Models_PackedFileInput> = [{ path: 'a', bytes: new Uint8Array(), contentType: 'x' }];
const packed: Uint8Array = packer.pack(files).packedBytes;
const mapped: ReadonlyMap<number, System_Numerics_Vector2> = api.echoMap(new Map([[1, { x: 2, y: 3 }]]));
const tracks: ReadonlyMap<number, ReadonlyArray<System_Numerics_Quaternion>> = api.echoTracks(new Map([[1, [{ x: 0, y: 0, z: 0, w: 1 }]]]));
const set: ReadonlySet<number> = api.echoSet(new Set([1, 2]));
const tuple: readonly [Uint8Array, { readonly x: number; readonly y: number }] = api.echoTuple([new Uint8Array(), { x: 1, y: 2 }]);
const memory: Uint8Array = api.echoMemory(new Uint8Array());
const large: bigint = api.echoInt64(9007199254740993n);
const rotations: ReadonlyMap<number, System_Numerics_Vector3> = skyRotations.readRadiansPerFrame(new Uint8Array());
const pair: readonly [success: boolean, doubled: number, error: string | null] = api.tryPair(2);
const appended = topology.tryFindAppendableTriangleRotation([], { a: 2, b: 1, c: 3 });
const counter: RatchetPs2_SdkCounter = counters.createInstance({ value: 2 });
const normalized: System_Numerics_Vector3 = bounds.createInstance({ min: { x: 0, y: 0, z: 0 }, max: { x: 2, y: 4, z: 8 } }).normalize({ x: 1, y: 2, z: 4 });
const box: RatchetPs2_Core_Geometry_Bounds3 = bounds.from__IReadOnlyListOfVector3([{ x: 0, y: 1, z: 2 }]);
const opaqueValue: number = api.readOpaque(api.createOpaque(2));
void [scaled, maybe, points, echoed, sum, origin, envelopeName, enabled, packed, mapped, tracks, set, tuple, memory, large, rotations, pair, appended, counter, normalized, box, opaqueValue, api.axes];
`;

writeFileSync(sourcePath, source, { flag: 'wx' });
try {
  const build = spawnSync(process.argv[2] ?? 'dotnet', ['run', '--no-restore', '--', '--type', 'RatchetPs2.SdkValueBindingCheck', '--type', 'RatchetPs2.SdkCounter', '--type', '*Bounds3', '--type', '*PackedFilePackageBuilder', '--type', '*GcSkyRotationReader', '--type', '*Ps2VifTopology'], { cwd, encoding: 'utf8' });
  assert.ifError(build.error);
  assert.equal(build.status, 0, build.stdout + build.stderr);

  const { RatchetPs2_SdkValueBindingCheck: api, RatchetPs2_SdkCounter: counters, RatchetPs2_Core_Geometry_Bounds3: bounds, RatchetPs2_Core_IO_Vif_Ps2VifTopology: topology, RatchetPs2_Core_Wad_Models_PackedFilePackageBuilder: packer, RatchetPs2_Games_GC_Skyboxes_GcSkyRotationReader: skyRotations } = await import(`../bin/probe/index.js?${Date.now()}`);
  const vector2 = { x: 1 / 3, y: -0 };
  assert.deepEqual(api.echoVector2(vector2), { x: Math.fround(1 / 3), y: -0 });
  assert.notEqual(api.echoVector2(vector2), vector2);
  assert.deepEqual(api.scaleVector3({ x: 1.25, y: -2.5, z: 3.75 }, 2), { x: 2.5, y: -5, z: 7.5 });
  assert.deepEqual(api.echoVector4({ x: 1, y: 2, z: 3, w: 4 }), { x: 1, y: 2, z: 3, w: 4 });
  assert.deepEqual(api.echoQuaternion({ x: 0, y: 0, z: 0, w: 1 }), { x: 0, y: 0, z: 0, w: 1 });

  const matrix = Object.fromEntries(Array.from({ length: 16 }, (_, index) => {
    const row = Math.floor(index / 4) + 1;
    const column = index % 4 + 1;
    return ['m' + row + column, index + 0.25];
  }));
  assert.deepEqual(api.echoMatrix(matrix), matrix);
  assert.deepEqual(api.maybeVector2({ x: 4, y: 5 }, true), { x: 4, y: 5 });
  assert.equal(api.maybeVector2({ x: 4, y: 5 }, false), null);
  assert.equal(api.maybeVector2(null, true), null);
  assert.deepEqual(api.points(), [{ x: 0, y: 0, z: 0 }, { x: 1, y: 2, z: 3 }]);
  assert.deepEqual(api.origin, { x: 1, y: 2, z: 3 });
  assert.deepEqual(api.axes, [{ x: 1, y: 1 }, { x: -1, y: -1 }]);
  assert.equal(api.sumIndices([1, 2, 3, 4]), 10);
  assert.deepEqual(api.checkedUIntIndices([0, 1, 2, 3]), [0, 1, 2, 3]);
  assert.throws(() => api.checkedUIntIndices([0xffffffff]), /overflow/i);
  assert.equal(api.sumValues([-3, 5, 9]), 11);
  assert.equal(api.lastArray([1, 2, 3]), 3);
  assert.equal(api.incrementLast([1, 2, 3]), 4);
  assert.deepEqual(api.booleanCompounds(), [false, true, false]);
  assert.deepEqual(api.echoPoints([{ x: 1, y: 2, z: 3 }]), [{ x: 1, y: 2, z: 3 }]);
  const uvs = [{ x: 2, y: 3 }];
  assert.deepEqual(api.doubleUvs(uvs), [{ x: 4, y: 6 }]);
  assert.deepEqual(uvs, [{ x: 2, y: 3 }]);
  const buffer = new Uint8Array([1, 2, 3]);
  const buffers = api.echoBuffers([buffer, buffer.buffer.slice(1)]);
  assert.deepEqual(buffers.map(value => Array.from(value)), [[1, 2, 3], [2, 3]]);
  assert.notEqual(buffers[0], buffer);
  const envelope = { name: 'map', origin: { x: 1, y: 2, z: 3 }, indices: [4, 5], payload: new Uint8Array([6, 7]) };
  assert.deepEqual(api.echoEnvelope(envelope), envelope);
  assert.deepEqual(api.echoEnvelopes([envelope]), [envelope]);
  assert.equal(api.maybeEnvelope(null), null);
  assert.deepEqual(api.echoOptions({}), { enabled: true, limit: 7, offset: { x: 1, y: 1 }, label: null });
  assert.deepEqual(api.echoOptions({ enabled: false, limit: 2, offset: { x: 3, y: 4 }, label: 'custom' }),
    { enabled: false, limit: 2, offset: { x: 3, y: 4 }, label: 'custom' });
  assert.deepEqual(packer.pack([
    { path: 'a', bytes: new Uint8Array([1, 2]), contentType: 'x' },
    { path: 'b', bytes: new Uint8Array([3]), contentType: 'y' }
  ]), {
    packedBytes: new Uint8Array([1, 2, 3]),
    entries: [
      { path: 'a', offset: 0, length: 2, contentType: 'x' },
      { path: 'b', offset: 2, length: 1, contentType: 'y' }
    ]
  });
  assert.deepEqual([...api.echoMap(new Map([[2, { x: 3, y: 4 }], [1, { x: -1, y: 0 }]]))],
    [[2, { x: 3, y: 4 }], [1, { x: -1, y: 0 }]]);
  assert.deepEqual([...api.echoTracks(new Map([[4, [{ x: 0, y: 0, z: 0, w: 1 }]]]))],
    [[4, [{ x: 0, y: 0, z: 0, w: 1 }]]]);
  assert.deepEqual([...api.echoSet(new Set([3, 1, 3]))], [3, 1]);
  assert.deepEqual(api.echoTuple([new Uint8Array([8, 9]), { x: 2, y: 4 }]),
    [new Uint8Array([8, 9]), { x: 2, y: 4 }]);
  assert.deepEqual(api.echoMemory(new Uint8Array([5, 6])), new Uint8Array([5, 6]));
  assert.equal(api.firstMemory(new Uint8Array([5, 6])), 5);
  assert.equal(api.echoMode(2), 2);
  assert.equal(api.echoInt64(9007199254740993n), 9007199254740993n);
  assert.deepEqual(api.tryPair(3), [true, 6, '']);
  assert.deepEqual(api.tryPair(-2), [false, -4, 'negative']);
  assert.deepEqual(topology.tryFindAppendableTriangleRotation([], { a: 2, b: 1, c: 3 }),
    [false, { a: 0, b: 0, c: 0 }, 0]);
  const counter = counters.createInstance({ value: 2 });
  assert.equal(counter.value, 2);
  assert.equal(counter.add(), 3);
  assert.equal(counter.add(4), 7);
  const reset = counter.reset(5);
  assert.equal(reset.value, 5);
  assert.equal(counter.value, 5);
  assert.deepEqual(bounds.createInstance({ min: { x: 0, y: 0, z: 0 }, max: { x: 2, y: 4, z: 8 } }).normalize({ x: 1, y: 2, z: 4 }),
    { x: 0.5, y: 0.5, z: 0.5 });
  const box = bounds.from__IReadOnlyListOfVector3([{ x: -1, y: 1, z: 2 }, { x: 3, y: 5, z: 6 }]);
  assert.deepEqual(box.center, { x: 1, y: 3, z: 4 });
  assert.deepEqual(box.lerp({ x: 0.5, y: 0.5, z: 0.5 }), { x: 1, y: 3, z: 4 });
  const opaque = api.createOpaque(4);
  assert.equal(opaque.increment(), 5);
  assert.equal(api.readOpaque(opaque), 5);
  assert.throws(() => api.readOpaque({ value: 5 }), TypeError);
  assert.deepEqual([...skyRotations.readRadiansPerFrame(new Uint8Array())], []);
  assert.throws(() => api.scaleVector3({ x: 1, y: 2 }, 1), TypeError);
  assert.throws(() => api.sumIndices([1, -1]), TypeError);
  assert.throws(() => api.echoPoints([{ x: 1, y: 2 }]), TypeError);
  assert.throws(() => api.echoBuffers([{}]), TypeError);
  assert.throws(() => api.echoQuaternion(null), TypeError);
  assert.throws(() => api.echoMatrix({}), TypeError);
  assert.throws(() => api.echoEnvelope({ name: 'bad' }), TypeError);
  assert.throws(() => api.echoOptions({ limit: 1.5 }), TypeError);
  assert.throws(() => api.echoMap({}), TypeError);
  assert.throws(() => api.echoSet([]), TypeError);
  assert.throws(() => api.echoTuple([new Uint8Array()]), TypeError);
  assert.throws(() => api.echoMode(256), TypeError);
  assert.throws(() => api.echoInt64(1), TypeError);

  writeFileSync(consumerPath, consumer);
  const tsc = spawnSync(process.execPath, ['node_modules/typescript/bin/tsc', '--noEmit', '--strict', '--target', 'es2022', '--module', 'esnext', '--moduleResolution', 'bundler', fileURLToPath(consumerPath)], { cwd, encoding: 'utf8' });
  assert.ifError(tsc.error);
  assert.equal(tsc.status, 0, tsc.stdout + tsc.stderr);
  assert.match(readFileSync(new URL('../bin/probe/index.d.ts', import.meta.url), 'utf8'), /ReadonlyArray<System_Numerics_Vector3>/);
  process.stdout.write('Value binding parity passed: domain values, collections, memory, bigint, lifetimes, multi-out contracts, validation and strict TypeScript.\n');
} finally {
  try { unlinkSync(consumerPath); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  unlinkSync(sourcePath);
}
