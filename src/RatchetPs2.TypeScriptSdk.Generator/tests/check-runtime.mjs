import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import ts from 'typescript';

const cwd = fileURLToPath(new URL('../', import.meta.url));
const config = ts.readConfigFile(`${cwd}Runtime/tsconfig.json`, ts.sys.readFile);
assert.equal(config.error, undefined);
const parsed = ts.parseJsonConfigFileContent(config.config, ts.sys, `${cwd}Runtime`);
assert.equal(parsed.errors.length, 0);
mkdirSync(`${cwd}bin`, { recursive: true });
const output = mkdtempSync(`${cwd}bin/runtime-check-`);
try {
  const program = ts.createProgram(parsed.fileNames, { ...parsed.options, outDir: output });
  const diagnostics = ts.getPreEmitDiagnostics(program);
  assert.equal(diagnostics.length, 0, ts.formatDiagnosticsWithColorAndContext(diagnostics, {
    getCurrentDirectory: () => cwd, getCanonicalFileName: name => name, getNewLine: () => '\n',
  }));
  assert.equal(program.emit().emitSkipped, false);
  const runtime = await import(pathToFileURL(`${output}/index.js`));
  const compatibility = readFileSync(`${cwd}Transpilation/BrowserCompatibility.cs`, 'utf8');
  const templates = [...compatibility.matchAll(/\[Transpose\.Template\("([^"\n]*)"\)\]/g)];
  assert.equal(templates.length, [...compatibility.matchAll(/\[Transpose\.Template\(/g)].length,
    'Keep implementations in Runtime/*.ts; templates must be single-line call bindings.');
  for (const [, template] of templates) {
    const binding = /^Browser\.(\w+)\.(\w+)\((.*)\)$/.exec(template);
    assert.ok(binding, `Inline implementation in template: ${template}`);
    const [, owner, method, args] = binding;
    assert.equal(typeof runtime[owner]?.[method], 'function', `Missing runtime binding: ${template}`);
    const parameters = args ? args.split(', ') : [];
    assert.ok(parameters.every(parameter => /^\{\w+\}$/.test(parameter)), `Inline argument logic: ${template}`);
    assert.equal(runtime[owner][method].length, parameters.length, `Binding arity mismatch: ${template}`);
  }
  assert.equal(runtime.StringCompatibility.IndexOf('a/b', '/'.charCodeAt(0), 4), 1);
  assert.equal(runtime.StringCompatibility.IndexOf('abc', '/'.charCodeAt(0), 4), -1);
  const { normalizeJson, jsonValue } = await import(pathToFileURL(`${output}/json.js`));
  assert.deepEqual(jsonValue(normalizeJson({ nested: { value: 1 } }), false), { nested: { value: 1 } });
  console.log(`Browser runtime strict TypeScript and ${templates.length} transpiler bindings passed.`);
} finally {
  rmSync(output, { recursive: true, force: true });
}
