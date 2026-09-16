import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, unlinkSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const path = new URL(`../../../src/RatchetPs2.Core/SdkNumericsCheck${process.pid}.cs`, import.meta.url);
const source = `
using System.Numerics;

namespace RatchetPs2;

public static class SdkNumericsCheck {
    public static string[] Cases() {
        var results = new List<string>();
        var v2 = new Vector2(1.25f, -2.5f);
        var w2 = new Vector2(-4.75f, 8.125f);
        results.Add(V(v2 + w2));
        results.Add(V(v2 - w2));
        results.Add(V(v2 * w2));
        var compound2 = v2;
        compound2 *= w2;
        results.Add(V(compound2));
        compound2 = v2;
        compound2 *= v2 + w2;
        results.Add(V(compound2));
        compound2 -= v2 - w2;
        results.Add(V(compound2));
        results.Add(V(v2 * 0.2f));
        results.Add(V(v2 / -3f));
        results.Add(V(Vector2.Min(v2, w2)));
        results.Add(V(Vector2.Max(v2, w2)));
        results.Add(V(Vector2.Clamp(new Vector2(-10f, 10f), v2, w2)));
        results.Add(F(Vector2.DistanceSquared(v2, w2)));
        results.Add(V(Vector2.Zero));
        results.Add(V(Vector2.One));

        var v3 = new Vector3(1.25f, -2.5f, 3.75f);
        var w3 = new Vector3(-4.75f, 8.125f, -0.0625f);
        results.Add(V(v3 + w3));
        var compound3 = new[] { v3 };
        compound3[0] += w3;
        results.Add(V(compound3[0]));
        results.Add(V(v3 - w3));
        results.Add(V(-v3));
        results.Add(V(v3 * 0.2f));
        results.Add(V(v3 / -3f));
        results.Add(V(Vector3.Cross(v3, w3)));
        results.Add(F(Vector3.Dot(v3, w3)));
        results.Add(F(Vector3.Distance(v3, w3)));
        results.Add(F(Vector3.DistanceSquared(v3, w3)));
        results.Add(V(Vector3.Min(v3, w3)));
        results.Add(V(Vector3.Max(v3, w3)));
        results.Add(V(Vector3.Normalize(new Vector3(3f, 4f, 12f))));
        results.Add(V(Vector3.Normalize(Vector3.Zero)));
        results.Add(V(Vector3.One));
        results.Add(V(Vector3.UnitY));
        results.Add(V(Vector3.UnitZ));
        results.Add((v3 == v3).ToString());
        results.Add((v3 != w3).ToString());
        results.Add(V(new Vector4(1f, -2f, 3f, -4f) / 3f));
        results.Add(V(Vector4.Zero));
        results.Add(V(Vector4.One));

        var q = new Quaternion(0.25f, -0.5f, 0.75f, 1f);
        var r = new Quaternion(-0.125f, 0.375f, 0.625f, -0.875f);
        results.Add(Q(q * r));
        results.Add(F(Quaternion.Dot(q, r)));
        results.Add(F(q.LengthSquared()));
        results.Add(Q(Quaternion.Normalize(q)));
        results.Add(Q(Quaternion.Inverse(q)));
        results.Add(Q(Quaternion.Normalize(new Quaternion())));
        results.Add(Q(Quaternion.Inverse(new Quaternion())));
        results.Add(Q(Quaternion.Identity));
        results.Add((q == q).ToString());
        results.Add((q != r).ToString());

        var rotation = Quaternion.Normalize(q);
        var rotationMatrix = Matrix4x4.CreateFromQuaternion(rotation);
        results.Add(M(rotationMatrix));
        results.Add(Q(Quaternion.CreateFromRotationMatrix(rotationMatrix)));
        results.Add(Q(Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationX(MathF.PI))));
        results.Add(Q(Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationY(MathF.PI))));
        results.Add(M(Matrix4x4.CreateRotationX(0.37f)));
        results.Add(M(Matrix4x4.CreateRotationY(-0.91f)));
        results.Add(V(Vector3.Transform(v3, rotation)));
        results.Add(V(Vector3.Transform(Vector3.UnitY, Matrix4x4.CreateRotationX(MathF.PI / 2f))));
        results.Add(V(Vector3.Transform(Vector3.UnitZ, Matrix4x4.CreateRotationY(MathF.PI / 2f))));

        var transform = Matrix4x4.CreateScale(new Vector3(2f, 3f, -4f))
            * rotationMatrix * Matrix4x4.CreateTranslation(new Vector3(10f, -20f, 30f));
        results.Add(M(transform));
        results.Add(V(Vector3.Transform(v3, transform)));
        results.Add(M(Matrix4x4.Transpose(transform)));
        results.Add((transform == transform).ToString());
        results.Add((transform != Matrix4x4.Identity).ToString());
        results.Add(Matrix4x4.Invert(transform, out var inverse).ToString());
        results.Add(M(inverse));
        results.Add(M(transform * inverse));

        var arbitrary = new Matrix4x4(
            1.25f, -2f, 3.5f, 0.25f,
            4f, 5.125f, -6f, 0.5f,
            -7.25f, 8f, 9.5f, -0.75f,
            10f, -11.5f, 12f, 1f);
        results.Add(M(arbitrary * Matrix4x4.Transpose(arbitrary)));
        results.Add(Matrix4x4.Invert(arbitrary, out var arbitraryInverse).ToString());
        results.Add(M(arbitraryInverse));

        var singular = Matrix4x4.CreateScale(new Vector3(1f, 0f, 2f));
        results.Add(Matrix4x4.Invert(singular, out var noInverse).ToString());
        results.Add(M(noInverse));

        var nan = float.NaN;
        results.Add(V(Vector2.Min(new Vector2(nan, 0f), new Vector2(1f, -0f))));
        results.Add(V(Vector2.Max(new Vector2(nan, -0f), new Vector2(1f, 0f))));
        results.Add((new Matrix4x4(nan, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            == new Matrix4x4(nan, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)).ToString());

        foreach (var value in new uint[] { 0, 1, 2, 3, 4, 255, 256, 65535, 65536, uint.MaxValue })
            results.Add(BitOperations.Log2(value).ToString());
        return results.ToArray();
    }

    private static string F(float value) => BitConverter.SingleToInt32Bits(value).ToString();
    private static string V(Vector2 value) => F(value.X) + "," + F(value.Y);
    private static string V(Vector3 value) => F(value.X) + "," + F(value.Y) + "," + F(value.Z);
    private static string V(Vector4 value) => F(value.X) + "," + F(value.Y) + "," + F(value.Z) + "," + F(value.W);
    private static string Q(Quaternion value) => F(value.X) + "," + F(value.Y) + "," + F(value.Z) + "," + F(value.W);
    private static string M(Matrix4x4 value) =>
        F(value.M11) + "," + F(value.M12) + "," + F(value.M13) + "," + F(value.M14) + "," +
        F(value.M21) + "," + F(value.M22) + "," + F(value.M23) + "," + F(value.M24) + "," +
        F(value.M31) + "," + F(value.M32) + "," + F(value.M33) + "," + F(value.M34) + "," +
        F(value.M41) + "," + F(value.M42) + "," + F(value.M43) + "," + F(value.M44);
}
`;

writeFileSync(path, source, { flag: 'wx' });
try {
  const result = spawnSync(process.argv[2] ?? 'dotnet', ['run', '--no-restore', '--', '--type', 'RatchetPs2.SdkNumericsCheck', '--verify'], { cwd, encoding: 'utf8' });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  const check = spawnSync(process.execPath, ['--input-type=module', '-e', `
    import assert from 'node:assert/strict';
    import { readFileSync } from 'node:fs';
    import { RatchetPs2_SdkNumericsCheck as api } from './bin/probe/index.js';
    const expected = JSON.parse(readFileSync('./bin/probe/numerics.json', 'utf8'));
    const actual = api.cases();
    assert.equal(actual.length, expected.length);
    const decode = bits => new Float32Array(new Int32Array([Number(bits)]).buffer)[0];
    for (let i = 0; i < expected.length; i++) {
      if (!expected[i].includes(',')) {
        assert.equal(actual[i], expected[i], 'case ' + i);
        continue;
      }
      const wanted = expected[i].split(',').map(decode);
      const got = actual[i].split(',').map(decode);
      assert.equal(got.length, wanted.length, 'case ' + i);
      for (let j = 0; j < wanted.length; j++) {
        if (Number.isNaN(wanted[j])) assert(Number.isNaN(got[j]), 'case ' + i + '[' + j + ']');
        else assert(Math.abs(got[j] - wanted[j]) <= 2e-6 * Math.max(1, Math.abs(wanted[j])),
          'case ' + i + '[' + j + ']: ' + got[j] + ' != ' + wanted[j]);
      }
    }
    process.stdout.write('Numerics parity passed: ' + expected.length + ' vector, quaternion, matrix and bit-operation results.\\n');
  `], { cwd, encoding: 'utf8' });
  assert.ifError(check.error);
  assert.equal(check.status, 0, check.stdout + check.stderr);
  process.stdout.write(check.stdout);
} finally {
  unlinkSync(path);
}
