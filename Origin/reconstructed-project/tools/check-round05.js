#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

const ROOT = path.resolve(__dirname, '..');
const OUTPUT = path.join(ROOT, 'analysis', 'static-checks-round-05.json');
const EXPECTED_HASHES = Object.freeze({
  'original/bundle.js': '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595',
  'work/bundle.strings-decoded.js': 'f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b',
  'original/index.js': '4024b47ce0f6832a4aa969a6d7329385f6df4c0bbbfb6f26047fd22920574d1b',
  'src/network/HttpClient.js': 'bfb3ffeff499ba0077dfbfcf94c3a94215ff09166a84bbb522f698943ed89189',
});

const REQUIRED_FILES = [
  'src/core/GameObjectEventProxy.js',
  'src/units/UnitConfig.js',
  'src/units/UnitDragBase.js',
  'src/units/UnitBase.js',
  'src/units/SoldierBase.js',
  'src/units/KnifeSoldier.js',
  'src/units/UnitFactory.js',
  'src/units/UnitRegistry.js',
  'src/units/index.js',
  'src/combat/KnifeAttackTimeline.js',
  'src/battle/dev/DevelopmentUnitSpawner.js',
  'src/battle/dev/DevelopmentUnitServices.js',
  'tests/mocks/createFriendlyUnitCombatHarness.js',
  'tools/run-friendly-unit-simulation.js',
  'tools/run-friendly-unit.js',
  'tools/run-micro-battle.js',
  'tests/behavior/MicroBattleCli.test.js',
  'analysis/critical-path/friendly-unit-classgraph.md',
  'analysis/critical-path/friendly-unit-classgraph.json',
  'analysis/critical-path/friendly-unit-dependency-closure.json',
  'analysis/critical-path/first-friendly-unit-selection.md',
  'analysis/critical-path/first-friendly-unit-stats.json',
  'analysis/behavior/friendly-unit-lifecycle.md',
  'analysis/behavior/friendly-unit-pool-reset-contract.md',
  'analysis/behavior/friendly-target-selection.md',
  'analysis/behavior/unit-registry.md',
  'analysis/behavior/FRIENDLY-UNIT-COMBAT-01.md',
  'analysis/mappings/FRIENDLY-UNIT-COMBAT-01-symbol-map.json',
  'analysis/modules/FRIENDLY-UNIT-COMBAT-01.json',
  'analysis/modules/FRIENDLY-UNIT-COMBAT-01-method-coverage.json',
  'analysis/behavior-diff-round-05.md',
  'analysis/round-05-report.md',
];

const ROUND05_RUNTIME_FILES = [
  'src/core/GameObjectEventProxy.js',
  'src/units/UnitConfig.js',
  'src/units/UnitDragBase.js',
  'src/units/UnitBase.js',
  'src/units/SoldierBase.js',
  'src/units/KnifeSoldier.js',
  'src/units/UnitFactory.js',
  'src/units/UnitRegistry.js',
  'src/combat/KnifeAttackTimeline.js',
  'src/battle/dev/DevelopmentUnitSpawner.js',
  'src/battle/dev/DevelopmentUnitServices.js',
  'tests/mocks/createFriendlyUnitCombatHarness.js',
  'tools/run-friendly-unit-simulation.js',
  'tools/run-friendly-unit.js',
  'tools/run-micro-battle.js',
  'tests/behavior/MicroBattleCli.test.js',
];

function sha256(file) { return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex'); }
function walk(dir) {
  if (!fs.existsSync(dir)) return [];
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walk(full)); else out.push(full);
  }
  return out;
}
function rel(file) { return path.relative(ROOT, file).split(path.sep).join('/'); }
function add(checks, name, pass, details = null) { checks.push({ name, pass: Boolean(pass), details }); }
function sourceFile(file) {
  return ts.createSourceFile(file, fs.readFileSync(file, 'utf8'), ts.ScriptTarget.ES2022, true, ts.ScriptKind.JS);
}
function localRequires(sf) {
  const out = [];
  (function visit(node) {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'require' && node.arguments.length === 1) {
      const arg = node.arguments[0];
      if (ts.isStringLiteral(arg) && arg.text.startsWith('.')) out.push(arg.text);
    }
    ts.forEachChild(node, visit);
  })(sf);
  return out;
}
function resolveRequire(from, request) {
  const base = path.resolve(path.dirname(from), request);
  return [base, `${base}.js`, path.join(base, 'index.js')].find(candidate => fs.existsSync(candidate) && fs.statSync(candidate).isFile()) || null;
}
function cycles(graph) {
  const state = new Map(), stack = [], found = [];
  function visit(node) {
    const value = state.get(node) || 0;
    if (value === 2) return;
    if (value === 1) { const index = stack.indexOf(node); found.push([...stack.slice(index), node].map(rel)); return; }
    state.set(node, 1); stack.push(node);
    for (const dep of graph.get(node) || []) visit(dep);
    stack.pop(); state.set(node, 2);
  }
  for (const node of graph.keys()) visit(node);
  return found;
}

const checks = [];
for (const [name, expected] of Object.entries(EXPECTED_HASHES)) {
  const file = path.join(ROOT, name);
  const actual = fs.existsSync(file) ? sha256(file) : null;
  add(checks, `immutable-hash:${name}`, actual === expected, { expected, actual });
}
for (const name of REQUIRED_FILES) add(checks, `required-file:${name}`, fs.existsSync(path.join(ROOT, name)));

const jsFiles = ['src', 'tests', 'tools'].flatMap(name => walk(path.join(ROOT, name))).filter(file => file.endsWith('.js')).sort();
const syntaxErrors = [], missing = [], graph = new Map();
for (const file of jsFiles) {
  const sf = sourceFile(file);
  for (const diagnostic of sf.parseDiagnostics) syntaxErrors.push({ file: rel(file), message: ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n') });
  const deps = [];
  for (const request of localRequires(sf)) {
    const resolved = resolveRequire(file, request);
    if (!resolved) missing.push({ file: rel(file), request });
    else if (resolved.startsWith(path.join(ROOT, 'src'))) deps.push(resolved);
  }
  if (file.startsWith(path.join(ROOT, 'src'))) graph.set(file, [...new Set(deps)]);
}
add(checks, 'javascript-syntax', syntaxErrors.length === 0, syntaxErrors);
add(checks, 'local-require-resolution', missing.length === 0, missing);
const requireCycles = cycles(graph);
add(checks, 'src-commonjs-require-cycles', requireCycles.length === 0, requireCycles);

const nativeHits = [], urlHits = [], proxyHits = [], emptyTodoHits = [];
for (const name of ROUND05_RUNTIME_FILES) {
  const text = fs.readFileSync(path.join(ROOT, name), 'utf8');
  if (/\b(?:wx|tt)\s*[.[]/.test(text)) nativeHits.push(name);
  const urls = text.match(/https?:\/\/[^\s'"`]+/g) || [];
  for (const url of urls) urlHits.push({ file: name, url });
  if (/\bnew\s+Proxy\b|\bProxy\s*\(/.test(text)) proxyHits.push(name);
  if (/TODO\s*\n\s*}/.test(text)) emptyTodoHits.push(name);
}
add(checks, 'round05-no-direct-wx-tt-call', nativeHits.length === 0, nativeHits);
add(checks, 'round05-no-real-http-url', urlHits.length === 0, urlHits);
add(checks, 'round05-no-proxy-fallback', proxyHits.length === 0, proxyHits);
add(checks, 'round05-no-empty-todo-body', emptyTodoHits.length === 0, emptyTodoHits);


const packageJson = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8'));
for (const scriptName of ['test:friendly-unit', 'dev:friendly-unit', 'dev:micro-battle', 'verify:round05', 'verify:all']) {
  add(checks, `package-script:${scriptName}`, Boolean(packageJson.scripts && packageJson.scripts[scriptName]), packageJson.scripts && packageJson.scripts[scriptName]);
}
const simulationTool = fs.readFileSync(path.join(ROOT, 'tools/run-friendly-unit-simulation.js'), 'utf8');
for (const evidenceKey of ['firstTargetDetectedAt', 'firstAttackStateAt', 'firstDamageSettledAt', 'retargetedAt', 'spatialEnemyRecordCount', 'friendlyCleanup']) {
  add(checks, `micro-battle-output:${evidenceKey}`, simulationTool.includes(evidenceKey));
}

const decoded = fs.readFileSync(path.join(ROOT, 'work/bundle.strings-decoded.js'), 'utf8');
for (const [name, fragment] of Object.entries({
  'source-alias-ri-equals-rb': 'ri = rb',
  'source-rc-extends-ri': 'class extends ri',
  'source-td-extends-rc': 'class extends rc',
  'source-rb-extends-qE': 'class b extends qE',
  'source-knife-key': 'this["Q_"] = "knife"',
  'source-knife-effect-name': '["tS"]: "knifeSoliderAttack"',
  'source-knife-audio': 'playSound"]("knife_attack")',
  'source-config-knife-range': '["wp"]: 1.5',
  'source-config-knife-damage': '["_p"]: 3',
  'source-config-knife-interval': '["kp"]: .8',
  'source-factory-index-lookup': 'tb["zx"][i]',
  'source-battle-manager-unit-attack': 'a["attack"]()',
})) add(checks, name, decoded.includes(fragment), fragment);

const config = fs.readFileSync(path.join(ROOT, 'src/units/UnitConfig.js'), 'utf8');
const knife = fs.readFileSync(path.join(ROOT, 'src/units/KnifeSoldier.js'), 'utf8');
const timeline = fs.readFileSync(path.join(ROOT, 'src/combat/KnifeAttackTimeline.js'), 'utf8');
const registry = fs.readFileSync(path.join(ROOT, 'src/units/UnitRegistry.js'), 'utf8');
const factory = fs.readFileSync(path.join(ROOT, 'src/units/UnitFactory.js'), 'utf8');
add(checks, 'reconstructed-knife-values', ['rangeCells: 1.5', 'attackDamage: 3', 'attackIntervalSeconds: 0.8'].every(item => config.includes(item)));
add(checks, 'reconstructed-knife-fixed-target-query', knife.includes('this.enemyManager.queryTargets') && !knife.includes('moveToward'));
add(checks, 'reconstructed-knife-delay', timeline.includes('KNIFE_HIT_DELAY_BASE_MS = 500'));
add(checks, 'reconstructed-factory-registration', factory.includes("this.register(0, '刀', KnifeSoldier)"));
add(checks, 'reconstructed-registry-map-order', registry.includes('this.soldiers = new Map()'));
add(checks, 'friendly-health-not-invented', !/this\.(?:health|maxHealth)\s*=/.test(fs.readFileSync(path.join(ROOT, 'src/units/SoldierBase.js'), 'utf8')));

const coveragePath = path.join(ROOT, 'analysis/modules/FRIENDLY-UNIT-COMBAT-01-method-coverage.json');
let coverage = null;
try { coverage = JSON.parse(fs.readFileSync(coveragePath, 'utf8')); } catch (_) { /* reported below */ }
add(checks, 'method-coverage-unresolved-zero', coverage && coverage.unresolvedMethods === 0, coverage);
add(checks, 'method-coverage-has-classification', coverage && coverage.originalMethodsFound > 0 && Array.isArray(coverage.classifications), coverage && { originalMethodsFound: coverage.originalMethodsFound });

const failed = checks.filter(check => !check.pass);
const report = {
  round: 5,
  name: 'FRIENDLY-UNIT-COMBAT-01',
  status: failed.length ? 'FAIL' : 'PASS',
  checksPassed: checks.length - failed.length,
  checksFailed: failed.length,
  sourceFilesChecked: jsFiles.length,
  checks,
};
fs.mkdirSync(path.dirname(OUTPUT), { recursive: true });
fs.writeFileSync(OUTPUT, `${JSON.stringify(report, null, 2)}\n`);
if (failed.length) {
  console.error(`Round 05 static checks FAIL (${failed.length}/${checks.length}).`);
  for (const check of failed) console.error(`- ${check.name}`);
  process.exit(1);
}
console.log(`Round 05 static checks PASS (${checks.length}/${checks.length}; ${jsFiles.length} JavaScript files).`);
