#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..');
const REPORT = path.join(ROOT, 'analysis', 'test-results-round-06.json');
const STATIC_REPORT = path.join(ROOT, 'analysis', 'static-checks-round-06.json');
const EXPECTED = Object.freeze({
  'original/bundle.js': '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595',
  'work/bundle.strings-decoded.js': 'f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b',
  'original/index.js': '4024b47ce0f6832a4aa969a6d7329385f6df4c0bbbfb6f26047fd22920574d1b',
  'src/network/HttpClient.js': 'bfb3ffeff499ba0077dfbfcf94c3a94215ff09166a84bbb522f698943ed89189',
});
const ROUND06 = Object.freeze([
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

function sha(relative) {
  return crypto.createHash('sha256').update(fs.readFileSync(path.join(ROOT, relative))).digest('hex');
}
function run(command, args) {
  const result = spawnSync(command, args, {
    cwd: ROOT,
    encoding: 'utf8',
    maxBuffer: 128 * 1024 * 1024,
    env: { ...process.env, NO_COLOR: '1' },
  });
  process.stdout.write(result.stdout || '');
  process.stderr.write(result.stderr || '');
  return { command, args, status: result.status, stdout: result.stdout || '', stderr: result.stderr || '' };
}
function runNode(args) { return run(process.execPath, args); }
function runNpm(args) {
  const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
  return run(npm, args);
}
function parseTap(result) {
  const text = `${result.stdout}\n${result.stderr}`;
  const number = label => {
    const matches = [...text.matchAll(new RegExp(`^# ${label} (\\d+)$`, 'gm'))];
    return matches.length ? Number(matches.at(-1)[1]) : null;
  };
  return { tests: number('tests'), pass: number('pass'), fail: number('fail'), skipped: number('skipped') };
}
function parseJsonOutput(text) {
  const start = text.indexOf('{');
  if (start < 0) return { parseError: 'missing JSON output' };
  return JSON.parse(text.slice(start));
}
function stableSimulationSummary(value) {
  return {
    mode: value.mode,
    bow: value.bow,
    arrow: value.arrow,
    timing: value.timing,
    arrowsCreated: (value.arrowsCreated || []).map(item => ({ elapsedMs: item.elapsedMs, projectileId: item.projectileId, typeKey: item.typeKey, poolKey: item.poolKey })),
    trajectoryMidpoint: value.trajectoryMidpoint,
    hitRecords: value.hitRecords,
    enemies: value.enemies,
    retargetResult: value.retargetResult,
    beforeCleanup: value.beforeCleanup,
    afterCleanup: value.afterCleanup,
    rewards: value.rewards,
    gameLoopOrder: value.gameLoopOrder,
    realNetworkRequests: value.realNetworkRequests,
    nativePlatformCalls: value.nativePlatformCalls,
  };
}

const immutableBefore = Object.fromEntries(Object.entries(EXPECTED).map(([file, expected]) => [file, { expected, actual: sha(file) }]));
const staticFirst = runNode(['tools/check-round06.js']);
const staticHashFirst = fs.existsSync(STATIC_REPORT) ? sha('analysis/static-checks-round-06.json') : null;
const testsFirst = runNode(['--test', ...ROUND06]);
const rangedFirstRun = runNode(['tools/run-ranged-battle.js']);
const rangedFirst = stableSimulationSummary(parseJsonOutput(rangedFirstRun.stdout));

const staticSecond = runNode(['tools/check-round06.js']);
const staticHashSecond = fs.existsSync(STATIC_REPORT) ? sha('analysis/static-checks-round-06.json') : null;
const testsSecond = runNode(['--test', ...ROUND06]);
const rangedSecondRun = runNode(['tools/run-ranged-battle.js']);
const rangedSecond = stableSimulationSummary(parseJsonOutput(rangedSecondRun.stdout));

// The existing Round 05 verifier includes NET-01, Round 03, Round 04 and Round 05.
const previousRounds = runNode(['tools/verify-round05.js']);
let previousReport = null;
try { previousReport = JSON.parse(fs.readFileSync(path.join(ROOT, 'analysis/test-results-round-05.json'), 'utf8')); } catch (_) { /* reported below */ }

const immutableAfter = Object.fromEntries(Object.entries(EXPECTED).map(([file, expected]) => [file, { expected, actual: sha(file) }]));
const counts = { first: parseTap(testsFirst), second: parseTap(testsSecond) };
const deterministic = JSON.stringify(rangedFirst) === JSON.stringify(rangedSecond);
const immutable = Object.values(immutableBefore).every(entry => entry.actual === entry.expected)
  && Object.values(immutableAfter).every(entry => entry.actual === entry.expected);
const timing = rangedFirst.timing || {};
const pass = [staticFirst, testsFirst, rangedFirstRun, staticSecond, testsSecond, rangedSecondRun, previousRounds]
  .every(result => result.status === 0)
  && counts.first.tests === 33 && counts.first.pass === 33 && counts.first.fail === 0
  && counts.second.tests === 33 && counts.second.pass === 33 && counts.second.fail === 0
  && staticHashFirst === staticHashSecond
  && deterministic
  && timing.firstTargetAt === 800
  && timing.firstAttackAnimationAt === 880
  && timing.firstStoppedAt === 1440
  && timing.firstArrowCreatedAt === 1440
  && timing.firstArrowHitAt === 1840
  && rangedFirst.hitRecords && rangedFirst.hitRecords.length === 6
  && rangedFirst.enemies && rangedFirst.enemies.first.finalHealth === 0
  && rangedFirst.enemies && rangedFirst.enemies.second.finalHealth === 0
  && rangedFirst.beforeCleanup && rangedFirst.beforeCleanup.activeProjectiles === 0
  && rangedFirst.afterCleanup && rangedFirst.afterCleanup.unitRegistryCount === 0
  && rangedFirst.realNetworkRequests === 0
  && rangedFirst.nativePlatformCalls === 0
  && previousReport && previousReport.status === 'PASS'
  && immutable;

const staticData = fs.existsSync(STATIC_REPORT) ? JSON.parse(fs.readFileSync(STATIC_REPORT, 'utf8')) : null;
const report = {
  round: 6,
  name: 'BOW-PROJECTILE-COMBAT-01',
  status: pass ? 'PASS' : 'FAIL',
  round06Tests: counts,
  staticChecks: {
    firstStatus: staticFirst.status,
    secondStatus: staticSecond.status,
    deterministic: staticHashFirst === staticHashSecond,
    hash: staticHashSecond,
    checksPassed: staticData && staticData.checksPassed,
    checksFailed: staticData && staticData.checksFailed,
  },
  rangedBattle: {
    first: rangedFirst,
    second: rangedSecond,
    deterministic,
  },
  previousRounds: previousReport ? {
    status: previousReport.status,
    net01: previousReport.tests && previousReport.tests.net01,
    round03: previousReport.tests && previousReport.tests.round03,
    round04: previousReport.tests && previousReport.tests.round04,
    round05First: previousReport.tests && previousReport.tests.round05First,
    round05Second: previousReport.tests && previousReport.tests.round05Second,
  } : null,
  immutableHashesBefore: immutableBefore,
  immutableHashesAfter: immutableAfter,
  realNetworkRequests: 0,
  nativePlatformCalls: 0,
};
fs.mkdirSync(path.dirname(REPORT), { recursive: true });
fs.writeFileSync(REPORT, `${JSON.stringify(report, null, 2)}\n`);
if (!pass) {
  console.error('Round 06 verification FAIL. See analysis/test-results-round-06.json');
  process.exit(1);
}
console.log(`Round 06 verification PASS (${counts.first.pass} tests × 2; previous cumulative verifier PASS).`);
