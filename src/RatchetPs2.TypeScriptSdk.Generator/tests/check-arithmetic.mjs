import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, unlinkSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const path = new URL(`../../../src/RatchetPs2.Core/SdkArithmeticCheck${process.pid}.cs`, import.meta.url);
const source = `
namespace RatchetPs2;

public static class SdkArithmeticCheck {
    public static string[] Cases() {
        var results = new List<string>();
        var one = 1;
        var minusOne = -1;
        var intMax = int.MaxValue;
        var intMin = int.MinValue;
        results.Add(Capture(() => checked(intMax + 0).ToString()));
        results.Add(Capture(() => checked(intMax + one).ToString()));
        results.Add(Capture(() => checked(intMin - one).ToString()));
        var intProduct = 46340;
        results.Add(Capture(() => checked(intProduct * intProduct).ToString()));
        intProduct++;
        results.Add(Capture(() => checked(intProduct * intProduct).ToString()));
        results.Add(Capture(() => checked(intMax * minusOne).ToString()));
        results.Add(Capture(() => checked(intMin * minusOne).ToString()));
        results.Add(Capture(() => (intMin / minusOne).ToString()));
        results.Add(Capture(() => (one / (one - one)).ToString()));
        results.Add(unchecked(intMax + one).ToString());

        var uintMax = uint.MaxValue;
        var uintZero = 0u;
        var uintOne = 1u;
        results.Add(Capture(() => checked(uintMax + uintZero).ToString()));
        results.Add(Capture(() => checked(uintMax + uintOne).ToString()));
        results.Add(Capture(() => checked(uintZero - uintOne).ToString()));
        var uintProduct = 65535u;
        results.Add(Capture(() => checked(uintProduct * uintProduct).ToString()));
        uintProduct++;
        results.Add(Capture(() => checked(uintProduct * uintProduct).ToString()));
        results.Add(Capture(() => checked(uintMax * uintOne).ToString()));
        results.Add(Capture(() => checked(uintMax * 2u).ToString()));
        results.Add(unchecked(uintMax + uintOne).ToString());
        results.Add((uintMax >> 1).ToString());

        var longMax = long.MaxValue;
        var longMin = long.MinValue;
        var longOne = 1L;
        results.Add(Capture(() => checked(longMax + 0L).ToString()));
        results.Add(Capture(() => checked(longMax + longOne).ToString()));
        results.Add(Capture(() => checked(longMin - longOne).ToString()));
        var longProduct = 3037000499L;
        results.Add(Capture(() => checked(longProduct * longProduct).ToString()));
        longProduct++;
        results.Add(Capture(() => checked(longProduct * longProduct).ToString()));
        results.Add(Capture(() => checked(longMax * -1L).ToString()));
        results.Add(Capture(() => checked(longMin * 0L).ToString()));
        results.Add(Capture(() => checked(longMin * -1L).ToString()));
        results.Add(Capture(() => (longMin / -1L).ToString()));
        results.Add(unchecked(longMax + longOne).ToString());
        results.Add((longMin >> 63).ToString());

        var ulongMax = ulong.MaxValue;
        var ulongZero = 0UL;
        var ulongOne = 1UL;
        results.Add(Capture(() => checked(ulongMax + ulongZero).ToString()));
        results.Add(Capture(() => checked(ulongMax + ulongOne).ToString()));
        results.Add(Capture(() => checked(ulongZero - ulongOne).ToString()));
        var ulongProduct = 4294967295UL;
        results.Add(Capture(() => checked(ulongProduct * ulongProduct).ToString()));
        ulongProduct++;
        results.Add(Capture(() => checked(ulongProduct * ulongProduct).ToString()));
        results.Add(Capture(() => checked(ulongMax * ulongOne).ToString()));
        results.Add(Capture(() => checked(ulongMax * ulongZero).ToString()));
        results.Add(unchecked(ulongMax + ulongOne).ToString());
        results.Add((ulongMax >> 63).ToString());

        long conversion = -1;
        results.Add(Capture(() => checked((byte)conversion).ToString()));
        conversion = 255;
        results.Add(Capture(() => checked((byte)conversion).ToString()));
        conversion = 256;
        results.Add(Capture(() => checked((byte)conversion).ToString()));
        conversion = -129;
        results.Add(Capture(() => checked((sbyte)conversion).ToString()));
        conversion = 127;
        results.Add(Capture(() => checked((sbyte)conversion).ToString()));
        conversion = 32768;
        results.Add(Capture(() => checked((short)conversion).ToString()));
        conversion = 65535;
        results.Add(Capture(() => checked((ushort)conversion).ToString()));
        conversion = 65536;
        results.Add(Capture(() => checked((ushort)conversion).ToString()));
        conversion = 2147483647;
        results.Add(Capture(() => checked((int)conversion).ToString()));
        conversion++;
        results.Add(Capture(() => checked((int)conversion).ToString()));
        conversion = 4294967295;
        results.Add(Capture(() => checked((uint)conversion).ToString()));
        conversion++;
        results.Add(Capture(() => checked((uint)conversion).ToString()));

        var signed = -1;
        results.Add(Capture(() => checked((uint)signed).ToString()));
        signed = int.MaxValue;
        results.Add(Capture(() => checked((uint)signed).ToString()));
        var unsigned = 2147483647u;
        results.Add(Capture(() => checked((int)unsigned).ToString()));
        unsigned++;
        results.Add(Capture(() => checked((int)unsigned).ToString()));
        unsigned = 255;
        results.Add(Capture(() => checked((byte)unsigned).ToString()));
        unsigned++;
        results.Add(Capture(() => checked((byte)unsigned).ToString()));
        var wideHalf = (ushort)255;
        results.Add(Capture(() => checked((byte)wideHalf).ToString()));
        wideHalf++;
        results.Add(Capture(() => checked((byte)wideHalf).ToString()));

        var single = -1.9f;
        results.Add(Capture(() => checked((int)single).ToString()));
        single = 2147483520f;
        results.Add(Capture(() => checked((int)single).ToString()));
        single = 2147483648f;
        results.Add(Capture(() => checked((int)single).ToString()));
        single = -32768f;
        results.Add(Capture(() => checked((short)single).ToString()));
        single = 32768f;
        results.Add(Capture(() => checked((short)single).ToString()));
        single = float.NaN;
        results.Add(Capture(() => checked((int)single).ToString()));
        single = float.PositiveInfinity;
        results.Add(Capture(() => checked((short)single).ToString()));

        var order = 0;
        int Mark(int digit, int value) { order = order * 10 + digit; return value; }
        results.Add(Capture(() => checked(Mark(1, intMax) + Mark(2, one)).ToString()));
        results.Add(order.ToString());
        order = 0;
        results.Add(Capture(() => checked(Mark(1, one) + Mark(2, intMax) * Mark(3, 2)).ToString()));
        results.Add(order.ToString());
        return results.ToArray();
    }

    private static string Capture(Func<string> action) {
        try { return action(); }
        catch (Exception error) { return error.GetType().Name; }
    }
}
`;

writeFileSync(path, source, { flag: 'wx' });
try {
  const result = spawnSync(process.argv[2] ?? 'dotnet', ['run', '--no-restore', '--', '--type', 'RatchetPs2.SdkArithmeticCheck', '--verify'], { cwd, encoding: 'utf8' });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  const check = spawnSync(process.execPath, ['--input-type=module', '-e', `
    import assert from 'node:assert/strict';
    import { readFileSync } from 'node:fs';
    import { RatchetPs2_SdkArithmeticCheck as api } from './bin/probe/index.js';
    const expected = JSON.parse(readFileSync('./bin/probe/arithmetic.json', 'utf8'));
    assert.deepEqual(api.cases(), expected);
    process.stdout.write('Arithmetic parity passed: ' + expected.length + ' checked/unchecked, conversion, 64-bit and evaluation-order results.\\n');
  `], { cwd, encoding: 'utf8' });
  assert.ifError(check.error);
  assert.equal(check.status, 0, check.stdout + check.stderr);
  process.stdout.write(check.stdout);
} finally {
  unlinkSync(path);
}
