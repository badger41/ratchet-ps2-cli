import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const test = name => fileURLToPath(new URL(name, import.meta.url));
const dotnet = process.argv[2] ?? 'dotnet';
const run = (command, args) => {
  console.log(`\n> ${command} ${args.join(' ')}`);
  const result = spawnSync(command, args, { cwd, encoding: 'utf8', stdio: 'inherit' });
  assert.ifError(result.error);
  assert.equal(result.status, 0, `${command} exited with ${result.status ?? result.signal}`);
};

for (const check of [
  'check-runtime.mjs',
  'check-buffers.mjs',
  'check-arithmetic.mjs',
  'check-numerics.mjs',
  'check-value-bindings.mjs',
  'check-io.mjs',
  'check-json.mjs',
  'check-json-serialization.mjs',
  'check-bcl.mjs',
  'check-discovery.mjs'
]) run(process.execPath, [test(check), dotnet]);

run(dotnet, ['run', '--no-restore']);
run(process.execPath, [test('check-entrypoints.mjs')]);
run(process.execPath, ['node_modules/typescript/bin/tsc',
  '--noEmit', '--strict', '--target', 'es2022', '--module', 'esnext', '--moduleResolution', 'bundler', test('check.ts')]);
run(process.execPath, [test('check.mjs')]);
console.log('Full SDK acceptance passed.');
