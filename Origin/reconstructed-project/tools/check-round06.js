#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

const ROOT = path.resolve(__dirname, '..');
const OUTPUT = path.join(ROOT, 'analysis', 'static-checks-round-06.json');
const EXPECTED_HASHES = Object.freeze({
  'original/bundle.js': '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595',
  'work/bundle.strings-decoded.js': 'f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b',
  'original/index.js': '4024b47ce0f6832a4aa969a6d7329385f6df4c0bbbfb6f26047fd22920574d1b',
  'src/network/HttpClient.js': 'bfb3ffeff499ba0077dfbfcf94c3a94215ff09166a84bbb522f698943ed89189',
});

const REQUIRED_FILES = Object.freeze([
  'src/units/BowSoldier.js',
  'src/projectiles/ProjectileMath.js',
  'src/projectiles/HitEnemyStrategy.js',
  'src/projectiles/TargetEnemyBezierMovement.js',
  'src/projectiles/ProjectileBase.js',
  'src/projectiles/SimpleDynamicArrow.js',
  'src/projectiles/ProjectileFactory.js',
  'src/projectiles/ProjectileManager.js',
  'src/projectiles/index.js',
  'src/combat/dev/DevelopmentAnimationDriver.js',
  'src/battle/dev/DevelopmentRangedBattleServices.js',
  'tests/mocks/createRangedCombatHarness.js',
  'tools/run-bow-soldier.js',
  'tools/run-projectile.js',
  'tools/run-ranged-battle.js',
  'analysis/critical-path/bow-projectile-classgraph.md',
  'analysis/critical-path/bow-projectile-classgraph.json',
  'analysis/critical-path/bow-projectile-dependency-closure.json',
  'analysis/critical-path/bow-soldier-stats.json',
  'analysis/critical-path/simple-arrow-stats.json',
  'analysis/mappings/BOW-PROJECTILE-COMBAT-01-symbol-map.json',
  'analysis/modules/BOW-PROJECTILE-COMBAT-01.json',
  'analysis/modules/BOW-PROJECTILE-COMBAT-01-method-coverage.json',
  'analysis/behavior/BOW-PROJECTILE-COMBAT-01.md',
  'analysis/behavior/bow-target-selection.md',
  'analysis/behavior/bow-attack-animation-contract.md',
  'analysis/behavior/simple-dynamic-arrow-trajectory.md',
  'analysis/behavior/projectile-update-order.md',
  'analysis/behavior/projectile-lifecycle.md',
  'analysis/behavior/projectile-pool-reset-contract.md',
  'analysis/behavior-diff-round-06.md',
  'analysis/round-06-report.md',
]);

const ROUND06_TEST_FILES = Object.freeze([
  'tests/unit/BowSoldierFactory.test.js',
  'tests/unit/BowSoldierInitialization.test.js',
  'tests/unit/BowSoldierTargetSelection.test.js',
  'tests/unit/BowSoldierAttackEvent.test.js',
  'tests/unit/ProjectileFactory.test.js',
  'tests/unit/SimpleDynamicArrowTrajectory.test.js',
  'tests/unit/ProjectileHit.test.js',
  'tests/unit/ProjectileTargetInvalidation.test.js',
  'tests/unit/ProjectilePoolReset.test.js',
  'tests/unit/ProjectileManager.test.js',
  'tests/behavior/BowSoldierKillsMob0.test.js',
  'tests/behavior/BowAndKnifeAttackTogether.test.js',
  'tests/behavior/TargetDiesBeforeArrowCreation.test.js',
  'tests/behavior/TargetDiesDuringArrowFlight.test.js',
  'tests/behavior/MovingTargetArrowFlight.test.js',
  'tests/behavior/TwoArrowsSameTarget.test.js',
  'tests/behavior/BattleEndsWithArrowInFlight.test.js',
  'tests/behavior/ReusedArrowHasCleanState.test.js',
  'tests/behavior/FullRangedMicroBattle.test.js',
]);

const ROUND06_RUNTIME_FILES = Object.freeze([
  'src/units/BowSoldier.js',
  'src/projectiles/ProjectileMath.js',
  'src/projectiles/HitEnemyStrategy.js',
  'src/projectiles/TargetEnemyBezierMovement.js',
  'src/projectiles/ProjectileBase.js',
  'src/projectiles/SimpleDynamicArrow.js',
  'src/projectiles/ProjectileFactory.js',
  'src/projectiles/ProjectileManager.js',
  'src/combat/dev/DevelopmentAnimationDriver.js',
  'src/battle/dev/DevelopmentRangedBattleServices.js',
  'src/battle/dev/DevelopmentUnitSpawner.js',
  'src/battle/dev/DevelopmentUnitServices.js',
  'src/bootstrap/DevelopmentBootstrap.js',
  'src/battle/BattleFlowCoordinator.js',
]);

function sha256(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
}
function walk(dir) {
  if (!fs.existsSync(dir)) return [];
  const output = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) output.push(...walk(full));
    else output.push(full);
  }
  return output;
}
function rel(file) { return path.relative(ROOT, file).split(path.sep).join('/'); }
function add(checks, name, pass, details = null) { checks.push({ name, pass: Boolean(pass), details }); }
function sourceFile(file) {
  return ts.createSourceFile(file, fs.readFileSync(file, 'utf8'), ts.ScriptTarget.ES2022, true, ts.ScriptKind.JS);
}
function localRequires(sf) {
  const requests = [];
  (function visit(node) {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'require' && node.arguments.length === 1) {
      const arg = node.arguments[0];
      if (ts.isStringLiteral(arg) && arg.text.startsWith('.')) requests.push(arg.text);
    }
    ts.forEachChild(node, visit);
  }(sf));
  return requests;
}
function resolveRequire(from, request) {
  const base = path.resolve(path.dirname(from), request);
  return [base, `${base}.js`, path.join(base, 'index.js')]
    .find(candidate => fs.existsSync(candidate) && fs.statSync(candidate).isFile()) || null;
}
function findCycles(graph) {
  const state = new Map();
  const stack = [];
  const found = [];
  function visit(node) {
    const value = state.get(node) || 0;
    if (value === 2) return;
    if (value === 1) {
      const index = stack.indexOf(node);
      found.push([...stack.slice(index), node].map(rel));
      return;
    }
    state.set(node, 1);
    stack.push(node);
    for (const dependency of graph.get(node) || []) visit(dependency);
    stack.pop();
    state.set(node, 2);
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
for (const name of ROUND06_TEST_FILES) add(checks, `required-test:${name}`, fs.existsSync(path.join(ROOT, name)));

const jsFiles = ['src', 'tests', 'tools']
  .flatMap(directory => walk(path.join(ROOT, directory)))
  .filter(file => file.endsWith('.js'))
  .sort();
const syntaxErrors = [];
const missingRequires = [];
const graph = new Map();
for (const file of jsFiles) {
  const sf = sourceFile(file);
  for (const diagnostic of sf.parseDiagnostics) {
    syntaxErrors.push({ file: rel(file), message: ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n') });
  }
  const dependencies = [];
  for (const request of localRequires(sf)) {
    const resolved = resolveRequire(file, request);
    if (!resolved) missingRequires.push({ file: rel(file), request });
    else if (resolved.startsWith(path.join(ROOT, 'src'))) dependencies.push(resolved);
  }
  if (file.startsWith(path.join(ROOT, 'src'))) graph.set(file, [...new Set(dependencies)]);
}
add(checks, 'javascript-syntax', syntaxErrors.length === 0, syntaxErrors);
add(checks, 'local-require-resolution', missingRequires.length === 0, missingRequires);
const requireCycles = findCycles(graph);
add(checks, 'src-commonjs-require-cycles', requireCycles.length === 0, requireCycles);

const nativeCalls = [];
const urls = [];
const proxyFallbacks = [];
const emptyTodos = [];
const instantDamageBypass = [];
for (const name of ROUND06_RUNTIME_FILES) {
  const text = fs.readFileSync(path.join(ROOT, name), 'utf8');
  if (/\b(?:wx|tt)\s*[.[]/.test(text)) nativeCalls.push(name);
  for (const url of text.match(/https?:\/\/[^\s'"`]+/g) || []) urls.push({ file: name, url });
  if (/\bnew\s+Proxy\b|\bProxy\s*\(/.test(text)) proxyFallbacks.push(name);
  if (/TODO\s*\n\s*}/.test(text)) emptyTodos.push(name);
  if (name === 'src/units/BowSoldier.js' && /setTimeout\s*\(|\.hit\s*\(/.test(text)) instantDamageBypass.push(name);
}
add(checks, 'round06-no-direct-wx-tt-call', nativeCalls.length === 0, nativeCalls);
add(checks, 'round06-no-real-http-url', urls.length === 0, urls);
add(checks, 'round06-no-proxy-fallback', proxyFallbacks.length === 0, proxyFallbacks);
add(checks, 'round06-no-empty-todo-body', emptyTodos.length === 0, emptyTodos);
add(checks, 'bow-no-instant-damage-bypass', instantDamageBypass.length === 0, instantDamageBypass);

const packageJson = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8'));
for (const scriptName of [
  'test:projectile', 'test:bow-soldier', 'test:round06',
  'dev:bow-soldier', 'dev:projectile', 'dev:ranged-battle',
  'check:round06', 'verify:round06', 'verify:all',
]) {
  add(checks, `package-script:${scriptName}`, Boolean(packageJson.scripts && packageJson.scripts[scriptName]), packageJson.scripts && packageJson.scripts[scriptName]);
}
add(checks, 'package-version-0.6.0', packageJson.version === '0.6.0', packageJson.version);

const sourceEvidence = {
  bow: fs.readFileSync(path.join(ROOT, 'work/extracts/round06/bow-soldier-26093-26264.js'), 'utf8'),
  arrow: fs.readFileSync(path.join(ROOT, 'work/extracts/round06/simple-dynamic-arrow-26698-26845.js'), 'utf8'),
  movement: fs.readFileSync(path.join(ROOT, 'work/extracts/round06/movement-27558-27873.js'), 'utf8'),
  factory: fs.readFileSync(path.join(ROOT, 'work/extracts/round06/projectile-factory-33698-33780.js'), 'utf8'),
  manager: fs.readFileSync(path.join(ROOT, 'work/extracts/round06/projectile-manager-37209-37474.js'), 'utf8'),
  config: fs.readFileSync(path.join(ROOT, 'work/extracts/round06/unit-config-11069-11352.js'), 'utf8'),
};
const evidenceChecks = {
  'source-bow-extends-soldier': sourceEvidence.bow.includes('class extends td'),
  'source-bow-animation-key': sourceEvidence.bow.includes('this["Q_"] = "bow"'),
  'source-bow-projectile-label': sourceEvidence.bow.includes('"弓箭小兵箭矢"'),
  'source-bow-projectile-resource': sourceEvidence.bow.includes('resources/img/weapon/arrow_0.png'),
  'source-bow-speed-scale': sourceEvidence.bow.includes('["fS"]: 1.75'),
  'source-bow-stopped-event': sourceEvidence.bow.includes('Laya["Event"]["STOPPED"]'),
  'source-bow-stop-before-launch': sourceEvidence.bow.includes('offAll")(Laya["Event"]["STOPPED"]') || sourceEvidence.bow.includes('offAll"](Laya["Event"]["STOPPED"]'),
  'source-bow-target-policy-Bm': sourceEvidence.bow.includes('g["Bm"] < e["Bm"]'),
  'source-bow-damage-2': sourceEvidence.config.includes('["_p"]: 2'),
  'source-bow-range-3.5': sourceEvidence.config.includes('["wp"]: 3.5'),
  'source-bow-interval-.8': sourceEvidence.config.includes('["kp"]: .8'),
  'source-arrow-extends-projectile-base': sourceEvidence.arrow.includes('class extends qY'),
  'source-arrow-size': sourceEvidence.arrow.includes('this["eS"]["pos"](0, 0), this["eS"]["size"](b[2], b[34])'),
  'source-projectile-composite-pool': sourceEvidence.factory.includes('vZ["VB"] + "_" + j + "_" + i'),
  'source-projectile-pool-prefix': sourceEvidence.factory.includes('vj["VB"] = "bullet_pool"'),
  'source-manager-array': sourceEvidence.manager.includes('this["ZI"] = []'),
  'source-manager-reverse-iteration': sourceEvidence.manager.includes('for (let g = this["ZI"]["length"] - 1; g >= 0; g--)'),
  'source-manager-register-bulletMgr': sourceEvidence.manager.includes('La"]("bulletMgr"'),
  'source-manager-movement-before-update': sourceEvidence.manager.includes('h["gS"]["Tk"](a') && sourceEvidence.manager.includes('h["update"](a)'),
};
for (const [name, pass] of Object.entries(evidenceChecks)) add(checks, name, pass);

const unitFactory = fs.readFileSync(path.join(ROOT, 'src/units/UnitFactory.js'), 'utf8');
const bow = fs.readFileSync(path.join(ROOT, 'src/units/BowSoldier.js'), 'utf8');
const movement = fs.readFileSync(path.join(ROOT, 'src/projectiles/TargetEnemyBezierMovement.js'), 'utf8');
const projectileFactory = fs.readFileSync(path.join(ROOT, 'src/projectiles/ProjectileFactory.js'), 'utf8');
const projectileManager = fs.readFileSync(path.join(ROOT, 'src/projectiles/ProjectileManager.js'), 'utf8');
add(checks, 'reconstructed-unit-factory-knife-key-stable', unitFactory.includes("this.register(0, '刀', KnifeSoldier)"));
add(checks, 'reconstructed-unit-factory-bow-key', unitFactory.includes("this.register(1, '弓', BowSoldier)"));
add(checks, 'reconstructed-bow-stopped-before-arrow', bow.includes('this.animation.on(this.laya.Event.STOPPED') && bow.includes('this.animation.offAll(this.laya.Event.STOPPED)'));
add(checks, 'reconstructed-bow-uses-projectile-manager', bow.includes('this.projectileManager.create') && !bow.includes('new SimpleDynamicArrow'));
add(checks, 'reconstructed-bezier-formula-not-linear-only', movement.includes('quadraticBezier') && movement.includes('Math.sqrt(Math.max(0.1'));
add(checks, 'reconstructed-hit-enable-progress', movement.includes('this.progress >= 0.8'));
add(checks, 'reconstructed-formal-pool-key', projectileFactory.includes('ProjectileFactory.POOL_PREFIX') && projectileFactory.includes('appearance.label'));
add(checks, 'reconstructed-manager-array-order', projectileManager.includes('this.activeProjectiles = []') && projectileManager.includes('this.activeProjectiles.push(projectile)'));
add(checks, 'reconstructed-manager-reverse-delete', projectileManager.includes('for (let index = this.activeProjectiles.length - 1; index >= 0; index -= 1)'));

let coverage = null;
try { coverage = JSON.parse(fs.readFileSync(path.join(ROOT, 'analysis/modules/BOW-PROJECTILE-COMBAT-01-method-coverage.json'), 'utf8')); } catch (_) { /* handled below */ }
add(checks, 'method-coverage-present', Boolean(coverage));
add(checks, 'method-coverage-unresolved-zero', coverage && coverage.unresolvedMethods === 0, coverage && coverage.unresolvedMethods);
add(checks, 'method-coverage-classified', coverage && coverage.originalMethodsFound === coverage.constructorsReconstructed + coverage.methodsReconstructed + coverage.explicitlyDeferredMethods, coverage);
add(checks, 'method-coverage-nonzero', coverage && coverage.originalMethodsFound > 0 && coverage.methodsReconstructed > 0, coverage);

const stats = JSON.parse(fs.readFileSync(path.join(ROOT, 'analysis/critical-path/bow-soldier-stats.json'), 'utf8'));
const arrowStats = JSON.parse(fs.readFileSync(path.join(ROOT, 'analysis/critical-path/simple-arrow-stats.json'), 'utf8'));
add(checks, 'bow-stats-formal-key', stats.stats.some(entry => entry.field === 'formalUnitKey' && entry.value === '弓'));
add(checks, 'bow-stats-formal-damage', stats.stats.some(entry => entry.field === 'attackDamageLevel1' && entry.value === 2));
add(checks, 'arrow-stats-formal-key', arrowStats.stats.some(entry => entry.field === 'projectileTypeKey' && entry.value === 'SimpleDynamicArrow'));
add(checks, 'arrow-stats-progress-formula', arrowStats.stats.some(entry => entry.field === 'progressDelta' && String(entry.value).includes('/ 500')));

const failed = checks.filter(check => !check.pass);
const report = {
  round: 6,
  module: 'BOW-PROJECTILE-COMBAT-01',
  status: failed.length === 0 ? 'PASS' : 'FAIL',
  checksPassed: checks.length - failed.length,
  checksFailed: failed.length,
  javascriptFilesChecked: jsFiles.length,
  sourceRequireCycles: requireCycles,
  realNetworkUrlsInRound06Runtime: urls,
  nativePlatformCallsInRound06Runtime: nativeCalls,
  checks,
};
fs.mkdirSync(path.dirname(OUTPUT), { recursive: true });
fs.writeFileSync(OUTPUT, `${JSON.stringify(report, null, 2)}\n`);
if (failed.length) {
  console.error(`Round 06 static checks FAIL (${failed.length}/${checks.length}).`);
  for (const failure of failed) console.error(`- ${failure.name}`);
  process.exit(1);
}
console.log(`Round 06 static checks PASS (${checks.length}/${checks.length}; ${jsFiles.length} JavaScript files).`);
