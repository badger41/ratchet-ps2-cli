import assert from 'node:assert/strict';
import { existsSync, readFileSync, writeFileSync, unlinkSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const core = new URL('../../../src/RatchetPs2.Core/', import.meta.url);
const token = `SdkDiscoveryCheck${process.pid}`;
const paths = [new URL(`${token}.cs`, core), new URL(`${token}.Helper.cs`, core), new URL(`${token}.Partial.cs`, core), new URL(`obj/${token}.Excluded.cs`, core)];
const namespace = `RatchetPs2.${token}`;
const exportName = `${namespace.replaceAll('.', '_')}_NewFeature`;
const created = [];
const dotnet = process.argv[2] ?? 'dotnet';
const compile = () => {
  const result = spawnSync(dotnet, ['run', '--no-restore', '--', '--type', `${namespace}.NewFeature`], { cwd, encoding: 'utf8' });
  assert.ifError(result.error);
  return result;
};
const evaluate = body => {
  const result = spawnSync(process.execPath, ['--input-type=module', '-e', `import assert from 'node:assert/strict'; import { ${exportName} as api } from './bin/probe/index.js'; ${body}`], { cwd, encoding: 'utf8' });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stdout + result.stderr);
};
const source = `namespace ${namespace};
public static class NewFeature {
  public static Payload Read(byte[] bytes, int extra = 7) {
    ArgumentNullException.ThrowIfNull(bytes);
    return new Payload(Helper.Add(bytes.Length, extra), null, ulong.MaxValue, new[] { new Part(3) });
  }
  public static int Echo(int value) => value;
  public static string Echo(string value) => value!;
  public static string? NullableText(string? value = null) => value;
  public static string NullArgumentName() {
    object? missing = null;
    try { ArgumentNullException.ThrowIfNull(missing); return "guard failed"; }
    catch (ArgumentNullException error) { return error.ParamName!; }
  }
  // ADDED_METHODS
}
public sealed record Payload(int Sum, string? Note, ulong Counter, Part[] Parts);
public sealed record Part(int Value);
`;
try {
  for (const [i, contents] of [source,
    `namespace ${namespace}; internal static partial class Helper { public static int Add(int a, int b) => Finish(a + b); }`,
    `namespace ${namespace}; internal static partial class Helper { private static int Finish(int value) => value + sizeof(int); }`,
    'This deliberately invalid C# must be excluded by MSBuild.'
  ].entries()) {
    writeFileSync(paths[i], contents, { flag: 'wx' });
    created.push(paths[i]);
  }
  let result = compile();
  assert.equal(result.status, 0, result.stdout + result.stderr);
  const report = JSON.parse(readFileSync(new URL('../bin/probe/report.json', import.meta.url), 'utf8'));
  assert.equal(report.compiledFiles.length, 3, 'Follow both partial helper files without pulling in unrelated namespaces.');
  assert.ok(report.compiledFiles.every(file => !file.includes('Excluded')));
  evaluate(`assert.deepEqual(api.read(new Uint8Array(2)), {sum:13, note:null, counter:18446744073709551615n, parts:[{value:3}]}); assert.equal(api.echo__Int32(42),42); assert.equal(api.echo__String('hello'),'hello'); assert.throws(()=>api.read(null),TypeError);`);
  evaluate(`assert.equal(api.nullableText(),null); assert.equal(api.nullableText('hello'),'hello'); assert.equal(api.nullArgumentName(),'missing');`);
  const declarations = readFileSync(new URL('../bin/probe/index.d.ts', import.meta.url), 'utf8');
  assert.match(declarations, /readonly note: string \| null/);
  assert.match(declarations, /readonly counter: bigint/);

  writeFileSync(paths[0], source.replace('// ADDED_METHODS', 'public static int AddedLater() => 42;'));
  result = compile();
  assert.equal(result.status, 0, result.stdout + result.stderr);
  evaluate('assert.equal(api.addedLater(),42);');

  const invalidRuntime = new URL(`../Runtime/${token}.ts`, import.meta.url);
  writeFileSync(invalidRuntime, 'export const invalid: number = "not a number";', { flag: 'wx' });
  created.push(invalidRuntime);
  result = compile();
  assert.equal(result.status, 1, result.stdout + result.stderr);
  assert.match(result.stderr, /TypeScript compilation failed/);
  assert.match(result.stderr, /TS2322/);
  assert.equal(existsSync(new URL('../bin/probe/index.js', import.meta.url)), false,
    'A runtime type error must remove the previously importable SDK.');
  unlinkSync(invalidRuntime);
  created.pop();

  writeFileSync(paths[0], source.replace('// ADDED_METHODS', 'public static System.IO.FileInfo Unsupported() => new("host-only");'));
  result = compile();
  assert.equal(result.status, 1, result.stdout + result.stderr);
  const failed = JSON.parse(readFileSync(new URL('../bin/probe/report.json', import.meta.url), 'utf8'));
  assert.equal(failed.success, false);
  assert.ok(failed.apiErrors.some(error => error.includes('Unsupported')));
  assert.equal(existsSync(new URL('../bin/probe/index.js', import.meta.url)), false, 'A failed build must remove stale SDK output.');
  console.log('Dynamic discovery passed: new files/methods/models, partial helpers, MSBuild exclusions, overloads, normalization, runtime type errors and failed-build cleanup.');
} finally {
  for (const path of created) unlinkSync(path);
}
