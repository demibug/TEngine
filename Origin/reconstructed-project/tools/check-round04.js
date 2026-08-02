#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

const ROOT = path.resolve(__dirname, '..');
const OUTPUT = path.join(ROOT, 'analysis', 'static-checks-round-04.json');

const EXPECTED_HASHES = Object.freeze({
  'original/bundle.js': '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595',
  'work/bundle.strings-decoded.js': 'f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b',
  'original/index.js': '4024b47ce0f6832a4aa969a6d7329385f6df4c0bbbfb6f26047fd22920574d1b',
  'src/network/HttpClient.js': 'bfb3ffeff499ba0077dfbfcf94c3a94215ff09166a84bbb522f698943ed89189',
});

const REQUIRED_FILES = Object.freeze([
  'src/core/ObjectPool.js',
  'src/battle/MapData.js',
  'src/battle/EnemyFactory.js',
  'src/battle/EnemyManager.js',
  'src/battle/BattleManager.js',
  'src/battle/BattleState.js',
  'src/battle/dev/DevelopmentCombatServices.js',
  'src/entities/EnemyEventProxy.js',
  'src/entities/EnemyBase.js',
  'src/entities/NormalEnemyBase.js',
  'src/entities/Mob0Enemy.js',
  'src/entities/BattleTarget.js',
  'analysis/critical-path/enemy-runtime-classgraph.md',
  'analysis/critical-path/enemy-runtime-classgraph.json',
  'analysis/critical-path/enemy-runtime-dependency-closure.json',
  'analysis/mappings/ENEMY-RUNTIME-01-symbol-map.json',
  'analysis/modules/ENEMY-RUNTIME-01.json',
  'analysis/modules/ENEMY-RUNTIME-01-method-coverage.json',
  'analysis/behavior/ENEMY-RUNTIME-01.md',
  'analysis/behavior/enemy-state-machine.md',
  'analysis/behavior/enemy-lifecycle.md',
  'analysis/behavior/enemy-pool-reset-contract.md',
  'analysis/behavior/enemy-target-selection.md',
  'analysis/behavior/enemy-spatial-index.md',
  'analysis/behavior-diff-round-04.md',
  'analysis/round-04-report.md',
  'tests/mocks/createEnemyRuntimeHarness.js',
  'tests/unit/MapPath.test.js',
  'tests/unit/EnemyMovement.test.js',
  'tests/unit/EnemySpatialIndex.test.js',
  'tests/unit/EnemyTargetSelection.test.js',
  'tests/unit/EnemyAttack.test.js',
  'tests/unit/EnemyDamage.test.js',
  'tests/unit/EnemyDeath.test.js',
  'tests/unit/EnemyPoolReset.test.js',
  'tests/behavior/Mob0PathToTarget.test.js',
  'tests/behavior/Mob0AttackBattleTarget.test.js',
  'tests/behavior/Mob0KilledBeforeArrival.test.js',
  'tests/behavior/Mob0PairBattle.test.js',
  'tests/behavior/BattleTargetDestroyed.test.js',
  'tests/behavior/EnemyCleanupAfterBattle.test.js',
  'tests/behavior/ReusedMob0HasCleanState.test.js',
  'tools/run-mob0-simulation.js',
]);

const ROUND04_RUNTIME_FILES = Object.freeze([
  'src/core/ObjectPool.js',
  'src/battle/MapData.js',
  'src/battle/EnemyFactory.js',
  'src/battle/EnemyManager.js',
  'src/battle/dev/DevelopmentCombatServices.js',
  'src/entities/EnemyEventProxy.js',
  'src/entities/EnemyBase.js',
  'src/entities/NormalEnemyBase.js',
  'src/entities/Mob0Enemy.js',
  'src/entities/BattleTarget.js',
  'tests/mocks/createEnemyRuntimeHarness.js',
  'tests/unit/MapPath.test.js',
  'tests/unit/EnemyMovement.test.js',
  'tests/unit/EnemySpatialIndex.test.js',
  'tests/unit/EnemyTargetSelection.test.js',
  'tests/unit/EnemyAttack.test.js',
  'tests/unit/EnemyDamage.test.js',
  'tests/unit/EnemyDeath.test.js',
  'tests/unit/EnemyPoolReset.test.js',
  'tests/behavior/Mob0PathToTarget.test.js',
  'tests/behavior/Mob0AttackBattleTarget.test.js',
  'tests/behavior/Mob0KilledBeforeArrival.test.js',
  'tests/behavior/Mob0PairBattle.test.js',
  'tests/behavior/BattleTargetDestroyed.test.js',
  'tests/behavior/EnemyCleanupAfterBattle.test.js',
  'tests/behavior/ReusedMob0HasCleanState.test.js',
  'tools/run-mob0-simulation.js',
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

function relative(file) {
  return path.relative(ROOT, file).split(path.sep).join('/');
}

function add(checks, name, pass, details = null) {
  checks.push({ name, pass: Boolean(pass), details });
}

function parse(file) {
  const source = fs.readFileSync(file, 'utf8');
  return ts.createSourceFile(file, source, ts.ScriptTarget.ES2022, true, ts.ScriptKind.JS);
}

function localRequires(sourceFile) {
  const values = [];
  function visit(node) {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'require' && node.arguments.length === 1) {
      const arg = node.arguments[0];
      if (ts.isStringLiteral(arg) && arg.text.startsWith('.')) values.push(arg.text);
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  return values;
}

function resolveLocalRequire(fromFile, request) {
  const base = path.resolve(path.dirname(fromFile), request);
  const candidates = [base, `${base}.js`, path.join(base, 'index.js')];
  return candidates.find(candidate => fs.existsSync(candidate) && fs.statSync(candidate).isFile()) || null;
}

function directNativeApiReferences(sourceFile) {
  const hits = [];
  function visit(node) {
    if (ts.isPropertyAccessExpression(node) && ts.isIdentifier(node.expression) && (node.expression.text === 'wx' || node.expression.text === 'tt')) {
      hits.push(`${node.expression.text}.${node.name.text}`);
    }
    if (ts.isElementAccessExpression(node) && ts.isIdentifier(node.expression) && (node.expression.text === 'wx' || node.expression.text === 'tt')) {
      hits.push(`${node.expression.text}[...]`);
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  return hits;
}

function findRequireCycles(graph) {
  const state = new Map();
  const stack = [];
  const cycles = [];
  const seenCycleKeys = new Set();

  function visit(node) {
    const current = state.get(node) || 0;
    if (current === 2) return;
    if (current === 1) {
      const start = stack.indexOf(node);
      const cycle = [...stack.slice(start), node].map(relative);
      const rotations = cycle.slice(0, -1).map((_, index) => {
        const body = cycle.slice(0, -1);
        const rotated = [...body.slice(index), ...body.slice(0, index)];
        return rotated.join(' -> ');
      });
      rotations.sort();
      const key = rotations[0];
      if (!seenCycleKeys.has(key)) {
        seenCycleKeys.add(key);
        cycles.push(cycle);
      }
      return;
    }
    state.set(node, 1);
    stack.push(node);
    for (const dep of graph.get(node) || []) visit(dep);
    stack.pop();
    state.set(node, 2);
  }

  for (const node of [...graph.keys()].sort()) visit(node);
  cycles.sort((a, b) => a.join('|').localeCompare(b.join('|')));
  return cycles;
}

function hasAll(text, fragments) {
  return fragments.every(fragment => text.includes(fragment));
}

const checks = [];

for (const [file, expected] of Object.entries(EXPECTED_HASHES)) {
  const full = path.join(ROOT, file);
  const actual = fs.existsSync(full) ? sha256(full) : null;
  add(checks, `immutable-hash:${file}`, actual === expected, { expected, actual });
}

for (const file of REQUIRED_FILES) {
  add(checks, `required-file:${file}`, fs.existsSync(path.join(ROOT, file)));
}

const jsFiles = ['src', 'tests', 'tools']
  .flatMap(dir => walk(path.join(ROOT, dir)))
  .filter(file => file.endsWith('.js'))
  .sort();

const syntaxErrors = [];
const missingRequires = [];
const requireGraph = new Map();
for (const file of jsFiles) {
  const sourceFile = parse(file);
  for (const diagnostic of sourceFile.parseDiagnostics) {
    syntaxErrors.push({
      file: relative(file),
      message: ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n'),
      position: diagnostic.start == null ? null : sourceFile.getLineAndCharacterOfPosition(diagnostic.start),
    });
  }
  const deps = [];
  for (const request of localRequires(sourceFile)) {
    const resolved = resolveLocalRequire(file, request);
    if (!resolved) missingRequires.push({ file: relative(file), request });
    else if (resolved.startsWith(path.join(ROOT, 'src'))) deps.push(resolved);
  }
  if (file.startsWith(path.join(ROOT, 'src'))) requireGraph.set(file, [...new Set(deps)].sort());
}
syntaxErrors.sort((a, b) => `${a.file}:${a.position && a.position.line}`.localeCompare(`${b.file}:${b.position && b.position.line}`));
missingRequires.sort((a, b) => `${a.file}:${a.request}`.localeCompare(`${b.file}:${b.request}`));
add(checks, 'javascript-syntax', syntaxErrors.length === 0, syntaxErrors);
add(checks, 'local-require-resolution', missingRequires.length === 0, missingRequires);

const cycles = findRequireCycles(requireGraph);
add(checks, 'src-commonjs-require-cycles', cycles.length === 0, cycles);

const nativeHits = [];
const httpHits = [];
const proxyHits = [];
for (const name of ROUND04_RUNTIME_FILES) {
  const file = path.join(ROOT, name);
  if (!fs.existsSync(file)) continue;
  const sourceFile = parse(file);
  for (const hit of directNativeApiReferences(sourceFile)) nativeHits.push({ file: name, hit });
  const text = fs.readFileSync(file, 'utf8');
  const urls = text.match(/https?:\/\/[^\s'"`]+/g) || [];
  for (const url of urls) httpHits.push({ file: name, url });
  if (/\bnew\s+Proxy\b|\bProxy\s*\(/.test(text)) proxyHits.push(name);
}
nativeHits.sort((a, b) => `${a.file}:${a.hit}`.localeCompare(`${b.file}:${b.hit}`));
httpHits.sort((a, b) => `${a.file}:${a.url}`.localeCompare(`${b.file}:${b.url}`));
proxyHits.sort();
add(checks, 'round04-no-direct-wx-tt-call', nativeHits.length === 0, nativeHits);
add(checks, 'round04-no-real-http-url', httpHits.length === 0, httpHits);
add(checks, 'round04-no-proxy-fallback', proxyHits.length === 0, proxyHits);

const enemyBaseText = fs.readFileSync(path.join(ROOT, 'src/entities/EnemyBase.js'), 'utf8');
const normalEnemyText = fs.readFileSync(path.join(ROOT, 'src/entities/NormalEnemyBase.js'), 'utf8');
const mob0Text = fs.readFileSync(path.join(ROOT, 'src/entities/Mob0Enemy.js'), 'utf8');
const managerText = fs.readFileSync(path.join(ROOT, 'src/battle/EnemyManager.js'), 'utf8');
const mapText = fs.readFileSync(path.join(ROOT, 'src/battle/MapData.js'), 'utf8');
const poolText = fs.readFileSync(path.join(ROOT, 'src/core/ObjectPool.js'), 'utf8');
const gameLoopText = fs.readFileSync(path.join(ROOT, 'src/core/GameLoop.js'), 'utf8');
const targetText = fs.readFileSync(path.join(ROOT, 'src/entities/BattleTarget.js'), 'utf8');
const devCombatText = fs.readFileSync(path.join(ROOT, 'src/battle/dev/DevelopmentCombatServices.js'), 'utf8');

add(checks, 'enemy-state-values', hasAll(enemyBaseText, [
  'SPAWNING: 0', 'MOVING: 1', 'SKILL: 2', 'STUNNED: 3', 'DEAD: 4',
]));
add(checks, 'enemy-movement-time-unit', hasAll(enemyBaseText, [
  'const ENEMY_BASE_SPEED = 50',
  'const TIME_UNIT_MS = 1000',
  'dirX * this.moveSpeed * deltaMs / TIME_UNIT_MS',
  'dirY * this.moveSpeed * deltaMs / TIME_UNIT_MS',
]));
add(checks, 'enemy-contact-timing-and-damage', hasAll(enemyBaseText, [
  'const CONTACT_ATTACK_COOLDOWN_MS = 500',
  'const CONTACT_DAMAGE_DELAY_MS = 50',
  'target.receiveEnemyContact(1, this)',
]));
add(checks, 'enemy-death-completion-boundary', normalEnemyText.includes('this.presentation.playDeath(this') && devCombatText.includes('deathDurationMs = 100'));
add(checks, 'fixed-update-semantics', hasAll(gameLoopText, [
  'GameLoop.MAX_FRAME_DELTA_MS = 500',
  'GameLoop.LOGIC_STEP_MS = 80',
]));
add(checks, 'map-grid-size', hasAll(mapText, [
  'this.gridWidth = 80', 'this.gridHeight = 80', 'this.cellWidth = 80', 'this.cellHeight = 80',
]));
add(checks, 'mob0-resource-and-pool-key', hasAll(mob0Text, [
  "this.resourcePath = 'resources/img/gameObject/enemy/mob_0.png'",
  "this.visualPoolKey = 'mob'",
  'this.objectPool.takeByKey(this.visualPoolKey',
  'this.objectPool.recoverByKey(this.visualPoolKey, visual)',
]));
add(checks, 'dual-object-pool-semantics', hasAll(poolText, [
  'takeByKey(', 'recoverByKey(', 'takeByClass(', 'recoverByClass(', '__InPool',
]));
add(checks, 'enemy-spatial-index-structures', hasAll(managerText, [
  'this.enemies = new Map()',
  'this.cellToEnemyIds = new Map()',
  'this.enemyIdToCell = new Map()',
  'this.gridSize = 80',
  '_indexEnemy(', '_unindexEnemy(', '_candidateIds(',
]));
add(checks, 'enemy-spatial-query-contract', hasAll(managerText, [
  'queryTargets(centerX, centerY, radius, playerSide)',
  'enemy.isTargetableBy(Boolean(playerSide))',
  'circleIntersectsRect(',
  'results.push({ id, x: enemy.visual.x, y: enemy.visual.y, Bm: enemy.remainingPathDistance })',
]));
add(checks, 'enemy-lifecycle-events', hasAll(enemyBaseText, [
  'GameEvents.ENEMY_REGISTERED',
  'GameEvents.ENEMY_GRID_LEFT',
  'GameEvents.ENEMY_GRID_ENTERED',
  'GameEvents.ENEMY_REMOVED',
]));
add(checks, 'battle-target-contact-contract', hasAll(targetText, [
  'receiveEnemyContact',
  'this.battleState.playerHealth',
  'this.battleState.opponentHealth',
]));
add(checks, 'pool-reuse-generation-guard', hasAll(enemyBaseText, [
  'this._lifecycleGeneration += 1',
  'if (generation !== this._lifecycleGeneration)',
  'this.laya.timer.clearAll(this)',
]));
add(checks, 'mob0-preserves-same-frame-visual-reference', mob0Text.includes('const visual = this.visual') && !mob0Text.includes('this.visual = null'));

let pathCheck = null;
try {
  const { MapData } = require(path.join(ROOT, 'src/battle/MapData.js'));
  const map = new MapData();
  map.changeMap(0);
  const player = map.pathForSide(true).map(point => [point.x, point.y]);
  const opponent = map.pathForSide(false).map(point => [point.x, point.y]);
  const expectedPlayer = [[0,8],[0,7],[0,6],[1,6],[2,6],[3,6],[4,6],[4,5],[4,4],[5,4],[6,4],[7,4],[7,5],[7,6],[7,7],[7,8],[7,9]];
  const expectedOpponent = [[7,1],[7,2],[7,3],[6,3],[5,3],[4,3],[3,3],[3,4],[3,5],[2,5],[1,5],[0,5],[0,4],[0,3],[0,2],[0,1],[0,0]];
  const fourDirection = [...player, ...opponent].every((point, index, all) => {
    if (index === 0 || index === player.length) return true;
    const previous = all[index - 1];
    return Math.abs(point[0] - previous[0]) + Math.abs(point[1] - previous[1]) === 1;
  });
  pathCheck = {
    player,
    opponent,
    playerMatches: JSON.stringify(player) === JSON.stringify(expectedPlayer),
    opponentMatches: JSON.stringify(opponent) === JSON.stringify(expectedOpponent),
    fourDirection,
  };
} catch (error) {
  pathCheck = { error: error && error.stack ? error.stack : String(error) };
}
add(checks, 'map0-confirmed-paths', Boolean(pathCheck && pathCheck.playerMatches && pathCheck.opponentMatches && pathCheck.fourDirection), pathCheck);

let exportedState = null;
try {
  const { EnemyRuntimeState } = require(path.join(ROOT, 'src/entities/EnemyBase.js'));
  exportedState = EnemyRuntimeState;
} catch (error) {
  exportedState = { error: String(error) };
}
add(checks, 'enemy-state-export', exportedState && exportedState.SPAWNING === 0 && exportedState.MOVING === 1 && exportedState.DEAD === 4, exportedState);

const methodCoveragePath = path.join(ROOT, 'analysis/modules/ENEMY-RUNTIME-01-method-coverage.json');
let methodCoverage = null;
try { methodCoverage = JSON.parse(fs.readFileSync(methodCoveragePath, 'utf8')); } catch (error) { methodCoverage = { parseError: String(error) }; }
const methodAccounted = methodCoverage && Number.isFinite(methodCoverage.originalMethodsFound)
  ? methodCoverage.constructorsReconstructed + methodCoverage.methodsReconstructed + methodCoverage.gettersSettersReconstructed + methodCoverage.poolLifecycleMethodsRecovered + methodCoverage.unreachableObfuscationMethods + methodCoverage.deferredOutOfScopeMethods
  : null;
add(checks, 'method-coverage-accounting', methodCoverage && methodAccounted === methodCoverage.originalMethodsFound, {
  originalMethodsFound: methodCoverage && methodCoverage.originalMethodsFound,
  accounted: methodAccounted,
});
add(checks, 'method-coverage-unresolved-zero', methodCoverage && methodCoverage.unresolvedMethods === 0, { unresolvedMethods: methodCoverage && methodCoverage.unresolvedMethods });
add(checks, 'method-coverage-critical-methods', methodCoverage && Array.isArray(methodCoverage.criticalMethods) && methodCoverage.criticalMethods.length >= 10 && methodCoverage.criticalMethods.every(item => item.status === 'MIGRATED'), methodCoverage && methodCoverage.criticalMethods);

let moduleReport = null;
try { moduleReport = JSON.parse(fs.readFileSync(path.join(ROOT, 'analysis/modules/ENEMY-RUNTIME-01.json'), 'utf8')); } catch (error) { moduleReport = { parseError: String(error) }; }
add(checks, 'module-range-recording', moduleReport && Array.isArray(moduleReport.requestedRanges) && moduleReport.requestedRanges.length === 5 && Array.isArray(moduleReport.actualRanges) && moduleReport.actualRanges.length >= 8, moduleReport && { requestedRanges: moduleReport.requestedRanges, actualRanges: moduleReport.actualRanges });
add(checks, 'module-status-mob0-complete', moduleReport && moduleReport.status === 'COMPLETE_FOR_MOB0_RUNTIME');

let symbolMap = null;
try { symbolMap = JSON.parse(fs.readFileSync(path.join(ROOT, 'analysis/mappings/ENEMY-RUNTIME-01-symbol-map.json'), 'utf8')); } catch (error) { symbolMap = { parseError: String(error) }; }
const symbolEntries = Array.isArray(symbolMap) ? symbolMap : symbolMap && (symbolMap.symbols || symbolMap.entries || []);
const originalSymbols = new Set((symbolEntries || []).map(item => item.originalSymbol || item.original).filter(Boolean));
const requiredSymbols = ['qE', 'ro', 'pe', 'st', 'vi', 's4', 'tl', 'ru', 'oS', 's0'];
add(checks, 'symbol-map-critical-coverage', requiredSymbols.every(symbol => originalSymbols.has(symbol) || [...originalSymbols].some(value => String(value).split(/\s*\/\s*/).includes(symbol))), {
  requiredSymbols,
  found: [...originalSymbols].sort(),
});
add(checks, 'symbol-map-confidence-values', (symbolEntries || []).length > 0 && (symbolEntries || []).every(item => ['HIGH','MEDIUM','LOW'].includes(item.confidence)), {
  invalid: (symbolEntries || []).filter(item => !['HIGH','MEDIUM','LOW'].includes(item.confidence)).map(item => item.originalSymbol || item.original),
});

const packageJson = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8'));
const requiredScripts = ['test:enemy-runtime','test:mob0','test:round04','dev:mob0-simulation','check:round04','verify:round04'];
add(checks, 'package-scripts-round04', requiredScripts.every(name => packageJson.scripts && packageJson.scripts[name]), {
  required: requiredScripts,
  found: Object.keys(packageJson.scripts || {}).sort(),
});

const extractDir = path.join(ROOT, 'work', 'round-04-extracts');
const extractFiles = walk(extractDir).filter(file => file.endsWith('.js')).map(relative).sort();
add(checks, 'round04-source-extracts', extractFiles.length >= 9, { count: extractFiles.length, files: extractFiles });

const failures = checks.filter(check => !check.pass);
const report = {
  round: 4,
  name: 'ENEMY-RUNTIME-01',
  status: failures.length === 0 ? 'PASS' : 'FAIL',
  checksPassed: checks.length - failures.length,
  checksFailed: failures.length,
  sourceFilesChecked: jsFiles.length,
  requireGraphNodes: requireGraph.size,
  checks,
};

fs.mkdirSync(path.dirname(OUTPUT), { recursive: true });
fs.writeFileSync(OUTPUT, `${JSON.stringify(report, null, 2)}\n`);

if (failures.length > 0) {
  console.error(`Round 04 static check FAIL (${failures.length}/${checks.length})`);
  for (const failure of failures) console.error(`- ${failure.name}`);
  process.exit(1);
}

console.log(`Round 04 static check PASS (${checks.length} checks, ${jsFiles.length} JS files).`);
