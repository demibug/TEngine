#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..');
const REPORT = path.join(ROOT, 'analysis', 'test-results-round-05.json');
const STATIC_REPORT = path.join(ROOT, 'analysis', 'static-checks-round-05.json');
const EXPECTED = {
  'original/bundle.js': '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595',
  'work/bundle.strings-decoded.js': 'f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b',
  'original/index.js': '4024b47ce0f6832a4aa969a6d7329385f6df4c0bbbfb6f26047fd22920574d1b',
  'src/network/HttpClient.js': 'bfb3ffeff499ba0077dfbfcf94c3a94215ff09166a84bbb522f698943ed89189',
};
const ROUND05 = [
  'tests/unit/FriendlyUnitFactory.test.js',
  'tests/unit/FriendlyUnitInitialization.test.js',
  'tests/unit/FriendlyUnitTargetSelection.test.js',
  'tests/unit/FriendlyUnitAttackTiming.test.js',
  'tests/unit/FriendlyUnitDamage.test.js',
  'tests/unit/FriendlyUnitDeath.test.js',
  'tests/unit/FriendlyUnitPoolReset.test.js',
  'tests/unit/UnitRegistry.test.js',
  'tests/behavior/FriendlyUnitKillsMob0.test.js',
  'tests/behavior/FriendlyUnitRetargetsAfterKill.test.js',
  'tests/behavior/MultipleFriendlyUnitsTargetMob0.test.js',
  'tests/behavior/Mob0LeavesRange.test.js',
  'tests/behavior/FriendlyUnitRemovedDuringAttack.test.js',
  'tests/behavior/BattleCleanupWithFriendlyUnits.test.js',
  'tests/behavior/ReusedFriendlyUnitHasCleanState.test.js',
  'tests/behavior/FullMicroBattle.test.js',
  'tests/behavior/MicroBattleCli.test.js',
];
const ROUND03 = [
  'tests/unit/BootToBattleCore.test.js', 'tests/unit/AnimationEntityPool.test.js',
  'tests/behavior/BootToMainScene.test.js', 'tests/behavior/MainToMatchScene.test.js',
  'tests/behavior/MatchToBattleScene.test.js', 'tests/behavior/DirectBattleDevelopmentMode.test.js',
  'tests/behavior/BattleFirstFrame.test.js', 'tests/behavior/BattleCleanup.test.js',
];
const ROUND04 = [
  'tests/unit/MapPath.test.js','tests/unit/EnemyMovement.test.js','tests/unit/EnemySpatialIndex.test.js',
  'tests/unit/EnemyTargetSelection.test.js','tests/unit/EnemyAttack.test.js','tests/unit/EnemyDamage.test.js',
  'tests/unit/EnemyDeath.test.js','tests/unit/EnemyPoolReset.test.js','tests/behavior/Mob0PathToTarget.test.js',
  'tests/behavior/Mob0AttackBattleTarget.test.js','tests/behavior/Mob0KilledBeforeArrival.test.js',
  'tests/behavior/Mob0PairBattle.test.js','tests/behavior/BattleTargetDestroyed.test.js',
  'tests/behavior/EnemyCleanupAfterBattle.test.js','tests/behavior/ReusedMob0HasCleanState.test.js',
];
const NET01 = ['tests/unit/HttpClient.test.js', 'tests/behavior/NET-01.behavior.test.js'];
function sha(file) { return crypto.createHash('sha256').update(fs.readFileSync(path.join(ROOT, file))).digest('hex'); }
function run(args) {
  const result = spawnSync(process.execPath, args, { cwd: ROOT, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  process.stdout.write(result.stdout || ''); process.stderr.write(result.stderr || '');
  return { status: result.status, stdout: result.stdout || '', stderr: result.stderr || '' };
}
function tap(result) {
  const text = `${result.stdout}\n${result.stderr}`;
  const number = label => { const values = [...text.matchAll(new RegExp(`^# ${label} (\\d+)$`, 'gm'))]; return values.length ? Number(values.at(-1)[1]) : null; };
  return { tests: number('tests'), pass: number('pass'), fail: number('fail'), skipped: number('skipped') };
}
function parseJsonOutput(text) {
  const start = text.indexOf('{');
  return start < 0 ? { parseError: 'missing JSON' } : JSON.parse(text.slice(start));
}
const immutableBefore = Object.fromEntries(Object.entries(EXPECTED).map(([file, expected]) => [file, { expected, actual: sha(file) }]));
const static1 = run(['tools/check-round05.js']);
const staticHash1 = fs.existsSync(STATIC_REPORT) ? sha('analysis/static-checks-round-05.json') : null;
const tests1 = run(['--test', '--test-reporter=tap', ...ROUND05]);
const sim1run = run(['tools/run-friendly-unit-simulation.js']);
const sim1 = parseJsonOutput(sim1run.stdout);
const static2 = run(['tools/check-round05.js']);
const staticHash2 = fs.existsSync(STATIC_REPORT) ? sha('analysis/static-checks-round-05.json') : null;
const tests2 = run(['--test', '--test-reporter=tap', ...ROUND05]);
const sim2run = run(['tools/run-friendly-unit-simulation.js']);
const sim2 = parseJsonOutput(sim2run.stdout);
const round03 = run(['--test', '--test-reporter=tap', ...ROUND03]);
const round04 = run(['--test', '--test-reporter=tap', ...ROUND04]);
const net01 = run(['--test', '--test-reporter=tap', ...NET01]);
const immutableAfter = Object.fromEntries(Object.entries(EXPECTED).map(([file, expected]) => [file, { expected, actual: sha(file) }]));
const counts = { round05First: tap(tests1), round05Second: tap(tests2), round03: tap(round03), round04: tap(round04), net01: tap(net01) };
const simSummary = value => ({ completed: value.completed, elapsedMs: value.elapsedMs, type: value.unit && value.unit.type, damage: value.unit && value.unit.attackDamage, range: value.unit && value.unit.attackRange, interval: value.unit && value.unit.attackIntervalSeconds, attacks: value.attacks && value.attacks.started && value.attacks.started.length, settled: value.attacks && value.attacks.settled, rewards: value.rewardCalls, logicPool: value.mobPools && value.mobPools.logic, visualPool: value.mobPools && value.mobPools.visual, networkRequests: value.networkRequests, nativePlatformCalls: value.nativePlatformCalls });
const simA = simSummary(sim1), simB = simSummary(sim2);
const immutable = Object.values(immutableAfter).every(value => value.actual === value.expected) && Object.values(immutableBefore).every(value => value.actual === value.expected);
const pass = [static1, tests1, sim1run, static2, tests2, sim2run, round03, round04, net01].every(value => value.status === 0) &&
  counts.round05First.tests === 25 && counts.round05First.pass === 25 && counts.round05First.fail === 0 &&
  counts.round05Second.tests === 25 && counts.round05Second.pass === 25 && counts.round05Second.fail === 0 &&
  counts.round03.pass === 15 && counts.round03.fail === 0 && counts.round04.pass === 24 && counts.round04.fail === 0 && counts.net01.pass === 32 && counts.net01.fail === 0 &&
  simA.completed && simA.attacks === 4 && simA.settled === 4 && simA.rewards === 2 && simA.networkRequests === 0 && simA.nativePlatformCalls === 0 &&
  JSON.stringify(simA) === JSON.stringify(simB) && staticHash1 === staticHash2 && immutable;
const report = {
  round: 5,
  name: 'FRIENDLY-UNIT-COMBAT-01',
  status: pass ? 'PASS' : 'FAIL',
  tests: counts,
  staticChecks: { firstStatus: static1.status, secondStatus: static2.status, deterministic: staticHash1 === staticHash2, hash: staticHash2 },
  simulation: { first: simA, second: simB, deterministic: JSON.stringify(simA) === JSON.stringify(simB) },
  immutableHashesBefore: immutableBefore,
  immutableHashesAfter: immutableAfter,
  realNetworkRequests: 0,
  nativePlatformCalls: 0,
};
fs.writeFileSync(REPORT, `${JSON.stringify(report, null, 2)}\n`);
if (!pass) { console.error('Round 05 verification FAIL. See analysis/test-results-round-05.json'); process.exit(1); }
console.log(`Round 05 verification PASS (${counts.round05First.pass} tests × 2; Round03 ${counts.round03.pass}; Round04 ${counts.round04.pass}; NET-01 ${counts.net01.pass}).`);
