#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

const ROOT = path.resolve(__dirname, '..');
const OUTPUT = path.join(ROOT, 'analysis', 'static-checks-round-03.json');
const EXPECTED = Object.freeze({
  'original/bundle.js': '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595',
  'work/bundle.strings-decoded.js': 'f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b',
  'original/index.js': '4024b47ce0f6832a4aa969a6d7329385f6df4c0bbbfb6f26047fd22920574d1b',
});

const REQUIRED = [
  'src/bootstrap/GameBootstrap.js',
  'src/bootstrap/DevelopmentBootstrap.js',
  'src/core/GameLoop.js',
  'src/core/SceneManager.js',
  'src/core/AnimationEntityPool.js',
  'src/data/PlayerDataCore.js',
  'src/data/BattleDataCore.js',
  'src/data/CriticalGameState.js',
  'src/platform/PlatformAdapter.js',
  'src/platform/dev/DevelopmentPlatform.js',
  'src/network/dev/DevelopmentNetworkData.js',
  'src/scenes/LoadSceneController.js',
  'src/scenes/MainSceneController.js',
  'src/scenes/MatchSceneController.js',
  'src/scenes/BattleSceneController.js',
  'src/battle/BattleState.js',
  'src/battle/BattleFlowCoordinator.js',
  'src/battle/BattleManager.js',
  'src/battle/EnemyManager.js',
  'src/battle/EnemyFactory.js',
  'src/battle/UnitRegistry.js',
  'src/entities/BattleTarget.js',
  'src/entities/Mob0Enemy.js',
  'analysis/critical-path/boot-to-battle-callgraph.md',
  'analysis/critical-path/boot-to-battle-callgraph.json',
  'analysis/critical-path/boot-to-battle-symbols.json',
  'analysis/critical-path/minimum-player-state.json',
  'analysis/critical-path/battle-entry-definition.md',
  'analysis/critical-path/deferred-modules.md',
  'tests/behavior/BootToMainScene.test.js',
  'tests/behavior/MainToMatchScene.test.js',
  'tests/behavior/MatchToBattleScene.test.js',
  'tests/behavior/DirectBattleDevelopmentMode.test.js',
  'tests/behavior/BattleFirstFrame.test.js',
  'tests/behavior/BattleCleanup.test.js',
];

function sha256(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
}
function walk(dir) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walk(full));
    else out.push(full);
  }
  return out;
}
function rel(file) { return path.relative(ROOT, file).split(path.sep).join('/'); }
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
    if (ts.isCallExpression(node) && node.expression.getText(sourceFile) === 'require' && node.arguments.length === 1) {
      const arg = node.arguments[0];
      if (ts.isStringLiteral(arg) && arg.text.startsWith('.')) values.push(arg.text);
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  return values;
}
function nativeCalls(sourceFile) {
  const hits = [];
  function visit(node) {
    if (ts.isPropertyAccessExpression(node) && ts.isIdentifier(node.expression) && (node.expression.text === 'wx' || node.expression.text === 'tt')) {
      hits.push(`${node.expression.text}.${node.name.text}`);
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  return hits;
}

const checks = [];
for (const [file, expected] of Object.entries(EXPECTED)) {
  const full = path.join(ROOT, file);
  const actual = fs.existsSync(full) ? sha256(full) : null;
  add(checks, `immutable-hash:${file}`, actual === expected, { expected, actual });
}

for (const file of REQUIRED) add(checks, `required-file:${file}`, fs.existsSync(path.join(ROOT, file)));

const jsFiles = [path.join(ROOT, 'src'), path.join(ROOT, 'tests'), path.join(ROOT, 'tools')]
  .flatMap(dir => walk(dir)).filter(file => file.endsWith('.js')).sort();
let syntaxErrors = [];
let missingImports = [];
for (const file of jsFiles) {
  const sourceFile = parse(file);
  for (const diagnostic of sourceFile.parseDiagnostics) {
    syntaxErrors.push({ file: rel(file), message: ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n') });
  }
  for (const request of localRequires(sourceFile)) {
    const base = path.resolve(path.dirname(file), request);
    const candidates = [base, `${base}.js`, path.join(base, 'index.js')];
    if (!candidates.some(candidate => fs.existsSync(candidate))) missingImports.push({ file: rel(file), request });
  }
}
add(checks, 'javascript-syntax', syntaxErrors.length === 0, syntaxErrors);
add(checks, 'local-require-resolution', missingImports.length === 0, missingImports);

const devFiles = [
  ...walk(path.join(ROOT, 'src', 'platform', 'dev')),
  ...walk(path.join(ROOT, 'src', 'network', 'dev')),
  path.join(ROOT, 'src', 'bootstrap', 'DevelopmentBootstrap.js'),
].filter(file => file.endsWith('.js'));
const nativeHits = [];
for (const file of devFiles) {
  for (const hit of nativeCalls(parse(file))) nativeHits.push({ file: rel(file), hit });
}
add(checks, 'development-mode-no-wx-tt-call', nativeHits.length === 0, nativeHits);

const devNetworkText = fs.readFileSync(path.join(ROOT, 'src/network/dev/DevelopmentNetworkData.js'), 'utf8');
add(checks, 'development-network-no-http-url', !/https?:\/\//.test(devNetworkText));

const classRegistry = JSON.parse(fs.readFileSync(path.join(ROOT, 'analysis/critical-path/boot-to-battle-symbols.json'), 'utf8'));
const requiredUuids = [
  'nFCDlT3GRD-9N62vwVVE4Q',
  'dKvUsPTsTBGGfiZxHMSqtg',
  'dxhrI-d-T2icEkklUGt-kQ',
  'a1VsRozfQfKce35jblVR3w',
];
const uuids = new Set(classRegistry.filter(item => item.uuid).map(item => item.uuid));
add(checks, 'critical-scene-uuid-coverage', requiredUuids.every(uuid => uuids.has(uuid)), { requiredUuids, found: [...uuids].sort() });

const battleData = fs.readFileSync(path.join(ROOT, 'src/data/BattleDataCore.js'), 'utf8');
add(checks, 'wave-count-table', battleData.includes('[10, 11, 12, 13, 15, 16, 18, 19, 21, 24, 26, 29, 31, 35, 38, 42, 46, 51, 56, 61]'));
add(checks, 'boss-round-table', battleData.includes('[3, 6, 9, 12, 15, 20]'));

const battleManager = fs.readFileSync(path.join(ROOT, 'src/battle/BattleManager.js'), 'utf8');
add(checks, 'battle-timing-constants', battleManager.includes('this.interWaveDelayMs = 5000') && battleManager.includes('this.spawnIntervalMs = 1500'));
add(checks, 'battle-manager-loop-registration', battleManager.includes("this.gameLoop.register('BattleMgr', this, this.update)"));

const battleScene = fs.readFileSync(path.join(ROOT, 'src/scenes/BattleSceneController.js'), 'utf8');
add(checks, 'adou-created-twice', (battleScene.match(/animationEntityPool\.create\('aDou'\)/g) || []).length === 2);
add(checks, 'battle-scene-loop-registration', battleScene.includes("register('BattleScene', this, this.update)"));

const packageJson = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8'));
add(checks, 'package-scripts', ['test:boot','test:battle-entry','test:round03','check:round03','verify:round03'].every(name => packageJson.scripts && packageJson.scripts[name]));

const callgraph = JSON.parse(fs.readFileSync(path.join(ROOT, 'analysis/critical-path/boot-to-battle-callgraph.json'), 'utf8'));
add(checks, 'callgraph-node-count', Array.isArray(callgraph.nodes) && callgraph.nodes.length >= 15, { count: callgraph.nodes && callgraph.nodes.length });
add(checks, 'callgraph-required-boundary', callgraph.completionBoundary && callgraph.completionBoundary.firstFrameExecutable === true && callgraph.completionBoundary.firstEnemyPairObservable === true);

const failures = checks.filter(check => !check.pass);
const report = {
  round: 3,
  name: 'BOOT-TO-BATTLE',
  status: failures.length === 0 ? 'PASS' : 'FAIL',
  checksPassed: checks.length - failures.length,
  checksFailed: failures.length,
  sourceFilesChecked: jsFiles.length,
  checks,
};
fs.writeFileSync(OUTPUT, `${JSON.stringify(report, null, 2)}\n`);
if (failures.length) {
  console.error(`Round 03 static check FAIL (${failures.length}/${checks.length})`);
  for (const failure of failures) console.error(`- ${failure.name}`);
  process.exit(1);
}
console.log(`Round 03 static check PASS (${checks.length} checks, ${jsFiles.length} JS files).`);
