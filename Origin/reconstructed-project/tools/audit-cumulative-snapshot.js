#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.resolve(__dirname, '..');
const ANALYSIS = path.join(ROOT, 'analysis');
const AUDIT_PATH = path.join(ANALYSIS, 'cumulative-snapshot-audit.json');
const FILE_LIST_JSON = path.join(ANALYSIS, 'complete-file-list.json');
const FILE_LIST_TEXT = path.join(ANALYSIS, 'complete-file-list.txt');
const CUMULATIVE_MANIFEST = path.join(ANALYSIS, 'deliverable-manifest-cumulative-round-06.json');
const LATEST_MANIFEST = path.join(ANALYSIS, 'deliverable-manifest.json');

const EXPECTED_HASHES = Object.freeze({
  'original/bundle.js': '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595',
  'work/bundle.strings-decoded.js': 'f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b',
  'original/index.js': '4024b47ce0f6832a4aa969a6d7329385f6df4c0bbbfb6f26047fd22920574d1b',
  'src/network/HttpClient.js': 'bfb3ffeff499ba0077dfbfcf94c3a94215ff09166a84bbb522f698943ed89189',
  'vendor/typescript-5.8.3.tgz': '4b867378e82e75791492505f2ed1b986a6c04b2196f78179d01670862444550a',
});

const ROUND_FILES = Object.freeze({
  round01: [
    'tools/decode-strings.js',
    'tests/behavior/decode-strings.test.js',
    'work/bundle.formatted.js',
    'work/bundle.strings-decoded.js',
    'analysis/round-01-report.md',
    'analysis/string-decoding-report.json',
    'analysis/string-decoding-map.jsonl',
    'analysis/string-decoding-unresolved.jsonl',
  ],
  round02: [
    'src/core/SingletonBase.js',
    'src/network/HttpClient.js',
    'src/network/index.js',
    'tests/unit/HttpClient.test.js',
    'tests/behavior/NET-01.behavior.test.js',
    'analysis/round-02-report.md',
    'analysis/round-02-artifacts.json',
    'analysis/static-checks-round-02.json',
    'analysis/modules/NET-01.json',
    'analysis/modules/NET-01-method-coverage.json',
    'analysis/mappings/NET-01-symbol-map.json',
    'work/extracts/NET-01-requested-5087-6037.js',
  ],
  round03: [
    'src/bootstrap/GameBootstrap.js',
    'src/bootstrap/DevelopmentBootstrap.js',
    'src/core/SceneManager.js',
    'src/core/FixedUpdateManager.js',
    'src/scenes/LoadSceneController.js',
    'src/scenes/MainSceneController.js',
    'src/scenes/MatchSceneController.js',
    'src/scenes/BattleSceneController.js',
    'src/battle/BattleManager.js',
    'tests/behavior/BootToMainScene.test.js',
    'tests/behavior/MatchToBattleScene.test.js',
    'tests/behavior/BattleFirstFrame.test.js',
    'analysis/round-03-report.md',
    'analysis/critical-path/boot-to-battle-callgraph.json',
  ],
  round04: [
    'src/core/ObjectPool.js',
    'src/battle/MapData.js',
    'src/battle/EnemyFactory.js',
    'src/battle/EnemyManager.js',
    'src/entities/EnemyBase.js',
    'src/entities/NormalEnemyBase.js',
    'src/entities/Mob0Enemy.js',
    'tests/unit/EnemyMovement.test.js',
    'tests/unit/EnemyDeath.test.js',
    'tests/behavior/Mob0PathToTarget.test.js',
    'analysis/round-04-report.md',
    'analysis/mappings/ENEMY-RUNTIME-01-symbol-map.json',
  ],
  round05: [
    'src/units/UnitConfig.js',
    'src/units/UnitDragBase.js',
    'src/units/UnitBase.js',
    'src/units/SoldierBase.js',
    'src/units/KnifeSoldier.js',
    'src/units/UnitFactory.js',
    'src/units/UnitRegistry.js',
    'src/combat/KnifeAttackTimeline.js',
    'tests/unit/FriendlyUnitFactory.test.js',
    'tests/unit/FriendlyUnitAttackTiming.test.js',
    'tests/behavior/FriendlyUnitKillsMob0.test.js',
    'tests/behavior/FullMicroBattle.test.js',
    'tests/behavior/MicroBattleCli.test.js',
    'analysis/round-05-report.md',
    'analysis/mappings/FRIENDLY-UNIT-COMBAT-01-symbol-map.json',
    'analysis/behavior/unit-registry.md',
    'tools/run-micro-battle.js',
  ],
  round06: [
    'src/units/BowSoldier.js',
    'src/projectiles/ProjectileBase.js',
    'src/projectiles/SimpleDynamicArrow.js',
    'src/projectiles/ProjectileFactory.js',
    'src/projectiles/ProjectileManager.js',
    'src/projectiles/TargetEnemyBezierMovement.js',
    'src/combat/dev/DevelopmentAnimationDriver.js',
    'tests/unit/BowSoldierFactory.test.js',
    'tests/unit/SimpleDynamicArrowTrajectory.test.js',
    'tests/behavior/BowSoldierKillsMob0.test.js',
    'tests/behavior/FullRangedMicroBattle.test.js',
    'analysis/round-06-report.md',
    'analysis/mappings/BOW-PROJECTILE-COMBAT-01-symbol-map.json',
    'analysis/modules/BOW-PROJECTILE-COMBAT-01-method-coverage.json',
    'tools/run-ranged-battle.js',
  ],
  historicalPreservation: [
    'history/README.md',
    'history/historical-file-preservation.json',
    'history/superseded/round-02/src/package.json',
    'history/superseded/round-05/src/combat/KnifeHitAreaAttack.js',
  ],
});

const REQUIRED_SCRIPTS = Object.freeze([
  'test:decode',
  'verify:round02',
  'verify:round03',
  'verify:round04',
  'verify:round05',
  'verify:round06',
  'verify:all',
  'dev:boot',
  'dev:battle',
  'dev:mob0-simulation',
  'dev:friendly-unit',
  'dev:friendly-unit-simulation',
  'dev:micro-battle',
  'test:friendly-unit',
  'test:round06',
  'test:projectile',
  'test:bow-soldier',
  'dev:bow-soldier',
  'dev:projectile',
  'dev:ranged-battle',
  'dev:all',
]);

function rel(file) {
  return path.relative(ROOT, file).split(path.sep).join('/');
}
function sha256File(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
}
function walk(dir) {
  if (!fs.existsSync(dir)) return [];
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
    if (entry.name === 'node_modules' || entry.name === '.git') continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walk(full));
    else out.push(full);
  }
  return out;
}
function fileInventory(exclusions = new Set()) {
  return walk(ROOT)
    .filter(file => !exclusions.has(rel(file)))
    .map(file => ({ path: rel(file), sizeBytes: fs.statSync(file).size, sha256: sha256File(file) }))
    .sort((a, b) => a.path.localeCompare(b.path));
}

const checks = [];
function check(name, pass, details = null) {
  checks.push({ name, pass: Boolean(pass), details });
}

for (const [round, files] of Object.entries(ROUND_FILES)) {
  const missing = files.filter(file => !fs.existsSync(path.join(ROOT, file)));
  check(`historical-files:${round}`, missing.length === 0, { expected: files.length, missing });
}

for (const [file, expected] of Object.entries(EXPECTED_HASHES)) {
  const full = path.join(ROOT, file);
  const actual = fs.existsSync(full) ? sha256File(full) : null;
  check(`immutable-hash:${file}`, actual === expected, { expected, actual });
}

const pkg = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8'));
const missingScripts = REQUIRED_SCRIPTS.filter(name => !(name in (pkg.scripts || {})));
check('package-scripts', missingScripts.length === 0, { required: REQUIRED_SCRIPTS, missing: missingScripts });
check('package-lock-present', fs.existsSync(path.join(ROOT, 'package-lock.json')));
check('local-typescript-dependency', pkg.devDependencies && pkg.devDependencies.typescript === 'file:vendor/typescript-5.8.3.tgz', {
  configured: pkg.devDependencies && pkg.devDependencies.typescript,
});
check('offline-npm-config-present', fs.existsSync(path.join(ROOT, '.npmrc')));

const round04ManifestPath = path.join(ROOT, 'analysis/deliverable-manifest-round-04.json');
let round04ManifestFiles = [];
try {
  const round04Manifest = JSON.parse(fs.readFileSync(round04ManifestPath, 'utf8'));
  round04ManifestFiles = (round04Manifest.files || [])
    .map(entry => typeof entry === 'string' ? entry : entry.path)
    .filter(Boolean);
} catch (_) {
  round04ManifestFiles = [];
}
const missingRound04ManifestFiles = round04ManifestFiles.filter(file => !fs.existsSync(path.join(ROOT, file)));
check('round04-cumulative-manifest-preserved', round04ManifestFiles.length > 0 && missingRound04ManifestFiles.length === 0, {
  manifestFileCount: round04ManifestFiles.length,
  present: round04ManifestFiles.length - missingRound04ManifestFiles.length,
  missing: missingRound04ManifestFiles,
});

const EXPECTED_TEST_FILES = Object.freeze([
  'tests/behavior/BattleCleanup.test.js',
  'tests/behavior/BattleCleanupWithFriendlyUnits.test.js',
  'tests/behavior/BattleFirstFrame.test.js',
  'tests/behavior/BattleTargetDestroyed.test.js',
  'tests/behavior/BootToMainScene.test.js',
  'tests/behavior/DirectBattleDevelopmentMode.test.js',
  'tests/behavior/EnemyCleanupAfterBattle.test.js',
  'tests/behavior/FriendlyUnitKillsMob0.test.js',
  'tests/behavior/FriendlyUnitRemovedDuringAttack.test.js',
  'tests/behavior/FriendlyUnitRetargetsAfterKill.test.js',
  'tests/behavior/FullMicroBattle.test.js',
  'tests/behavior/MicroBattleCli.test.js',
  'tests/behavior/MainToMatchScene.test.js',
  'tests/behavior/MatchToBattleScene.test.js',
  'tests/behavior/Mob0AttackBattleTarget.test.js',
  'tests/behavior/Mob0KilledBeforeArrival.test.js',
  'tests/behavior/Mob0LeavesRange.test.js',
  'tests/behavior/Mob0PairBattle.test.js',
  'tests/behavior/Mob0PathToTarget.test.js',
  'tests/behavior/MultipleFriendlyUnitsTargetMob0.test.js',
  'tests/behavior/NET-01.behavior.test.js',
  'tests/behavior/ReusedFriendlyUnitHasCleanState.test.js',
  'tests/behavior/ReusedMob0HasCleanState.test.js',
  'tests/behavior/decode-strings.test.js',
  'tests/unit/AnimationEntityPool.test.js',
  'tests/unit/BootToBattleCore.test.js',
  'tests/unit/EnemyAttack.test.js',
  'tests/unit/EnemyDamage.test.js',
  'tests/unit/EnemyDeath.test.js',
  'tests/unit/EnemyMovement.test.js',
  'tests/unit/EnemyPoolReset.test.js',
  'tests/unit/EnemySpatialIndex.test.js',
  'tests/unit/EnemyTargetSelection.test.js',
  'tests/unit/FriendlyUnitAttackTiming.test.js',
  'tests/unit/FriendlyUnitDamage.test.js',
  'tests/unit/FriendlyUnitDeath.test.js',
  'tests/unit/FriendlyUnitFactory.test.js',
  'tests/unit/FriendlyUnitInitialization.test.js',
  'tests/unit/FriendlyUnitPoolReset.test.js',
  'tests/unit/FriendlyUnitTargetSelection.test.js',
  'tests/unit/HttpClient.test.js',
  'tests/unit/MapPath.test.js',
  'tests/unit/UnitRegistry.test.js',
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
const testFiles = walk(path.join(ROOT, 'tests')).filter(file => file.endsWith('.test.js'));
const actualTestPaths = testFiles.map(rel).sort();
const missingHistoricalTests = EXPECTED_TEST_FILES.filter(file => !actualTestPaths.includes(file));
check('historical-tests-present', missingHistoricalTests.length === 0, {
  expectedCount: EXPECTED_TEST_FILES.length,
  actualCount: actualTestPaths.length,
  missing: missingHistoricalTests,
});
const roundReports = [1, 2, 3, 4, 5, 6].map(round => `analysis/round-${String(round).padStart(2, '0')}-report.md`);
check('round-reports-complete', roundReports.every(file => fs.existsSync(path.join(ROOT, file))), roundReports);

const nodeModulesInsideProject = fs.existsSync(path.join(ROOT, 'node_modules'));
check('node-modules-not-required-in-snapshot', true, { presentInWorkingTree: nodeModulesInsideProject, excludedFromArchive: true });

const failed = checks.filter(item => !item.pass);
const audit = {
  schemaVersion: 1,
  status: failed.length === 0 ? 'PASS' : 'FAIL',
  cumulativeSnapshot: true,
  containsRounds: [1, 2, 3, 4, 5, 6],
  activeSourceFormat: 'CommonJS',
  historicalSupersededFilesPreservedUnder: 'history/superseded/',
  checksPassed: checks.length - failed.length,
  checksFailed: failed.length,
  testsDiscovered: testFiles.length,
  checks,
};
fs.mkdirSync(ANALYSIS, { recursive: true });
fs.writeFileSync(AUDIT_PATH, `${JSON.stringify(audit, null, 2)}\n`);

const manifestExclusions = new Set([
  'analysis/deliverable-manifest.json',
  'analysis/deliverable-manifest-cumulative-round-06.json',
  'analysis/complete-file-list.json',
  'analysis/complete-file-list.txt',
]);
const manifestFiles = fileInventory(manifestExclusions);
const manifest = {
  schemaVersion: 3,
  round: 6,
  snapshotType: 'FULL_CUMULATIVE_PROJECT',
  containsAllHistoricalRounds: true,
  containsRounds: [1, 2, 3, 4, 5, 6],
  status: audit.status === 'PASS' ? 'CUMULATIVE_VERIFIED' : 'CUMULATIVE_AUDIT_FAILED',
  fileCount: manifestFiles.length,
  totalBytes: manifestFiles.reduce((sum, file) => sum + file.sizeBytes, 0),
  excludedSelfReferentialFiles: [...manifestExclusions].sort(),
  immutableInputs: EXPECTED_HASHES,
  files: manifestFiles,
};
fs.writeFileSync(CUMULATIVE_MANIFEST, `${JSON.stringify(manifest, null, 2)}\n`);
fs.writeFileSync(LATEST_MANIFEST, `${JSON.stringify(manifest, null, 2)}\n`);

const listExclusions = new Set(['analysis/complete-file-list.json', 'analysis/complete-file-list.txt']);
const completeFiles = fileInventory(listExclusions);
const completeList = {
  schemaVersion: 1,
  snapshotType: 'FULL_CUMULATIVE_PROJECT',
  containsAllHistoricalRounds: true,
  containsRounds: [1, 2, 3, 4, 5, 6],
  fileCountExcludingSelfListingFiles: completeFiles.length,
  excludedSelfListingFiles: [...listExclusions].sort(),
  files: completeFiles,
};
fs.writeFileSync(FILE_LIST_JSON, `${JSON.stringify(completeList, null, 2)}\n`);
const text = [
  '# 完整累计工程文件清单',
  '# 包含第一至第六轮全部当前成果，并在 history/ 中保存被后续实现替代的历史文件。',
  `# 文件数（不含本清单自身两个文件）：${completeFiles.length}`,
  '# 格式：路径<TAB>字节数<TAB>SHA-256',
  ...completeFiles.map(file => `${file.path}\t${file.sizeBytes}\t${file.sha256}`),
  'analysis/complete-file-list.json\tSELF_LISTING_FILE\tNOT_HASHED',
  'analysis/complete-file-list.txt\tSELF_LISTING_FILE\tNOT_HASHED',
  '',
].join('\n');
fs.writeFileSync(FILE_LIST_TEXT, text);

if (failed.length) {
  console.error(`Cumulative snapshot audit FAIL (${failed.length}/${checks.length}).`);
  for (const item of failed) console.error(`- ${item.name}`);
  process.exit(1);
}
console.log(`Cumulative snapshot audit PASS (${checks.length}/${checks.length}; ${completeFiles.length} listed files).`);
