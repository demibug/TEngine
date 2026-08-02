#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..');
const REPORT_PATH = path.join(ROOT, 'analysis', 'test-results-round-04.json');
const STATIC_REPORT_PATH = path.join(ROOT, 'analysis', 'static-checks-round-04.json');

const ROUND04_TEST_FILES = Object.freeze([
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
]);

const ROUND03_TEST_FILES = Object.freeze([
  'tests/unit/BootToBattleCore.test.js',
  'tests/unit/AnimationEntityPool.test.js',
  'tests/behavior/BootToMainScene.test.js',
  'tests/behavior/MainToMatchScene.test.js',
  'tests/behavior/MatchToBattleScene.test.js',
  'tests/behavior/DirectBattleDevelopmentMode.test.js',
  'tests/behavior/BattleFirstFrame.test.js',
  'tests/behavior/BattleCleanup.test.js',
]);

const NET01_TEST_FILES = Object.freeze([
  'tests/unit/HttpClient.test.js',
  'tests/behavior/NET-01.behavior.test.js',
]);

const EXPECTED_HASHES = Object.freeze({
  originalBundle: ['original/bundle.js', '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595'],
  decodedBundle: ['work/bundle.strings-decoded.js', 'f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b'],
  originalIndex: ['original/index.js', '4024b47ce0f6832a4aa969a6d7329385f6df4c0bbbfb6f26047fd22920574d1b'],
  httpClient: ['src/network/HttpClient.js', 'bfb3ffeff499ba0077dfbfcf94c3a94215ff09166a84bbb522f698943ed89189'],
});

function sha256(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
}

function walk(dir) {
  const output = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) output.push(...walk(full));
    else output.push(full);
  }
  return output;
}

function treeHash(directories) {
  const files = directories.flatMap(directory => walk(path.join(ROOT, directory))).sort();
  const hash = crypto.createHash('sha256');
  for (const file of files) {
    hash.update(path.relative(ROOT, file).split(path.sep).join('/'));
    hash.update('\0');
    hash.update(fs.readFileSync(file));
    hash.update('\0');
  }
  return hash.digest('hex');
}

function run(command, args) {
  const result = spawnSync(command, args, { cwd: ROOT, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const stdout = result.stdout || '';
  const stderr = result.stderr || '';
  process.stdout.write(stdout);
  process.stderr.write(stderr);
  return { status: result.status, signal: result.signal, stdout, stderr };
}

function parseTap(output) {
  function lastNumber(label) {
    const matches = [...output.matchAll(new RegExp(`^# ${label} (\\d+)$`, 'gm'))];
    return matches.length ? Number(matches[matches.length - 1][1]) : null;
  }
  return {
    tests: lastNumber('tests'),
    suites: lastNumber('suites'),
    pass: lastNumber('pass'),
    fail: lastNumber('fail'),
    cancelled: lastNumber('cancelled'),
    skipped: lastNumber('skipped'),
    todo: lastNumber('todo'),
  };
}

function parseSimulation(stdout) {
  const start = stdout.indexOf('{');
  if (start < 0) return { parseError: 'No JSON object in simulation output' };
  try { return JSON.parse(stdout.slice(start)); }
  catch (error) { return { parseError: error.message }; }
}

function immutableSnapshot() {
  const output = {};
  for (const [name, [file, expected]] of Object.entries(EXPECTED_HASHES)) {
    const actual = fs.existsSync(path.join(ROOT, file)) ? sha256(path.join(ROOT, file)) : null;
    output[name] = { file, expected, actual, unchanged: actual === expected };
  }
  return output;
}

const sourceTreeBefore = treeHash(['src', 'tests', 'tools']);
const immutableBefore = immutableSnapshot();

const staticFirst = run(process.execPath, ['tools/check-round04.js']);
const staticReportHashFirst = fs.existsSync(STATIC_REPORT_PATH) ? sha256(STATIC_REPORT_PATH) : null;
const staticReportFirst = fs.existsSync(STATIC_REPORT_PATH) ? JSON.parse(fs.readFileSync(STATIC_REPORT_PATH, 'utf8')) : null;

const round04First = run(process.execPath, ['--test', ...ROUND04_TEST_FILES]);
const simulationFirstRun = run(process.execPath, ['tools/run-mob0-simulation.js']);
const simulationFirst = parseSimulation(simulationFirstRun.stdout);

const staticSecond = run(process.execPath, ['tools/check-round04.js']);
const staticReportHashSecond = fs.existsSync(STATIC_REPORT_PATH) ? sha256(STATIC_REPORT_PATH) : null;
const staticReportSecond = fs.existsSync(STATIC_REPORT_PATH) ? JSON.parse(fs.readFileSync(STATIC_REPORT_PATH, 'utf8')) : null;

const round04Second = run(process.execPath, ['--test', ...ROUND04_TEST_FILES]);
const simulationSecondRun = run(process.execPath, ['tools/run-mob0-simulation.js']);
const simulationSecond = parseSimulation(simulationSecondRun.stdout);

const round03Regression = run(process.execPath, ['--test', ...ROUND03_TEST_FILES]);
const net01Regression = run(process.execPath, ['--test', ...NET01_TEST_FILES]);

const sourceTreeAfter = treeHash(['src', 'tests', 'tools']);
const immutableAfter = immutableSnapshot();

const round04FirstCounts = parseTap(`${round04First.stdout}\n${round04First.stderr}`);
const round04SecondCounts = parseTap(`${round04Second.stdout}\n${round04Second.stderr}`);
const round03Counts = parseTap(`${round03Regression.stdout}\n${round03Regression.stderr}`);
const net01Counts = parseTap(`${net01Regression.stdout}\n${net01Regression.stderr}`);

function simulationSummary(value) {
  return {
    parseError: value.parseError || null,
    mode: value.mode || null,
    elapsedMs: value.elapsedMs == null ? null : value.elapsedMs,
    completed: value.completed === true,
    pathTransitionCount: Array.isArray(value.pathTransitions) ? value.pathTransitions.length : null,
    playerHealth: value.targets && value.targets.playerHealth,
    opponentHealth: value.targets && value.targets.opponentHealth,
    mob0ClassPool: value.pools && value.pools.Mob0Class,
    mobVisualPool: value.pools && value.pools.mobVisual,
    networkRequests: value.networkRequests,
    nativePlatformCalls: value.nativePlatformCalls,
  };
}

const simulationFirstSummary = simulationSummary(simulationFirst);
const simulationSecondSummary = simulationSummary(simulationSecond);
const simulationDeterministic = JSON.stringify(simulationFirstSummary) === JSON.stringify(simulationSecondSummary);
const immutableStable = Object.values(immutableBefore).every(item => item.unchanged) &&
  Object.values(immutableAfter).every(item => item.unchanged);
const allCommandsPassed = [
  staticFirst,
  round04First,
  simulationFirstRun,
  staticSecond,
  round04Second,
  simulationSecondRun,
  round03Regression,
  net01Regression,
].every(result => result.status === 0);
const countChecksPassed = round04FirstCounts.tests === 24 && round04FirstCounts.pass === 24 && round04FirstCounts.fail === 0 &&
  round04SecondCounts.tests === 24 && round04SecondCounts.pass === 24 && round04SecondCounts.fail === 0 &&
  round03Counts.tests === 15 && round03Counts.pass === 15 && round03Counts.fail === 0 &&
  net01Counts.tests === 32 && net01Counts.pass === 32 && net01Counts.fail === 0;
const simulationChecksPassed = [simulationFirstSummary, simulationSecondSummary].every(summary =>
  !summary.parseError && summary.completed && summary.pathTransitionCount === 32 &&
  summary.playerHealth === 2 && summary.opponentHealth === 2 &&
  summary.mob0ClassPool === 2 && summary.mobVisualPool === 2 &&
  summary.networkRequests === 0 && summary.nativePlatformCalls === 0
);
const staticDeterministic = staticReportHashFirst != null && staticReportHashFirst === staticReportHashSecond;
const sourceStable = sourceTreeBefore === sourceTreeAfter;
const pass = allCommandsPassed && countChecksPassed && simulationChecksPassed && simulationDeterministic &&
  staticDeterministic && sourceStable && immutableStable &&
  staticReportFirst && staticReportSecond && staticReportFirst.status === 'PASS' && staticReportSecond.status === 'PASS';

const report = {
  round: 4,
  name: 'ENEMY-RUNTIME-01',
  status: pass ? 'PASS' : 'FAIL',
  round04: {
    firstRun: { commandStatus: round04First.status, counts: round04FirstCounts },
    secondRun: { commandStatus: round04Second.status, counts: round04SecondCounts },
    testFiles: ROUND04_TEST_FILES,
  },
  staticChecks: {
    firstRunStatus: staticFirst.status,
    secondRunStatus: staticSecond.status,
    checksPassed: staticReportSecond && staticReportSecond.checksPassed,
    checksFailed: staticReportSecond && staticReportSecond.checksFailed,
    sourceFilesChecked: staticReportSecond && staticReportSecond.sourceFilesChecked,
    reportHashFirst: staticReportHashFirst,
    reportHashSecond: staticReportHashSecond,
    deterministic: staticDeterministic,
  },
  mob0Simulation: {
    firstRun: simulationFirstSummary,
    secondRun: simulationSecondSummary,
    deterministic: simulationDeterministic,
  },
  regressions: {
    round03: { commandStatus: round03Regression.status, counts: round03Counts, testFiles: ROUND03_TEST_FILES },
    net01: { commandStatus: net01Regression.status, counts: net01Counts, testFiles: NET01_TEST_FILES },
  },
  determinism: {
    sourceTreeHashBefore: sourceTreeBefore,
    sourceTreeHashAfter: sourceTreeAfter,
    sourceTestsToolsUnchanged: sourceStable,
  },
  immutableHashesBefore: immutableBefore,
  immutableHashesAfter: immutableAfter,
  realNetworkRequests: 0,
  nativePlatformCalls: 0,
};

fs.mkdirSync(path.dirname(REPORT_PATH), { recursive: true });
fs.writeFileSync(REPORT_PATH, `${JSON.stringify(report, null, 2)}\n`);

if (!pass) {
  console.error('Round 04 verification FAIL. See analysis/test-results-round-04.json');
  process.exit(1);
}

console.log(`Round 04 verification PASS (${round04FirstCounts.pass} tests × 2, Round 03 ${round03Counts.pass}, NET-01 ${net01Counts.pass}).`);
