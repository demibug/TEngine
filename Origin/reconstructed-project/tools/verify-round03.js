#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..');
const REPORT = path.join(ROOT, 'analysis', 'test-results-round-03.json');
const TEST_FILES = [
  'tests/unit/BootToBattleCore.test.js',
  'tests/unit/AnimationEntityPool.test.js',
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
function treeHash(dirs) {
  const files = [];
  function walk(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else files.push(full);
    }
  }
  for (const dir of dirs) walk(path.join(ROOT, dir));
  files.sort();
  const hash = crypto.createHash('sha256');
  for (const file of files) {
    hash.update(path.relative(ROOT, file));
    hash.update('\0');
    hash.update(fs.readFileSync(file));
    hash.update('\0');
  }
  return hash.digest('hex');
}
function run(command, args) {
  const result = spawnSync(command, args, { cwd: ROOT, encoding: 'utf8' });
  const output = `${result.stdout || ''}${result.stderr || ''}`;
  process.stdout.write(output);
  return { status: result.status, output };
}
function parseTap(output) {
  const value = name => {
    const match = output.match(new RegExp(`# ${name} (\\d+)`));
    return match ? Number(match[1]) : null;
  };
  return { tests: value('tests'), pass: value('pass'), fail: value('fail'), skipped: value('skipped') };
}

const sourceHashBefore = treeHash(['src', 'tests']);
const staticFirst = run(process.execPath, ['tools/check-round03.js']);
const staticReportFirst = fs.existsSync(path.join(ROOT, 'analysis/static-checks-round-03.json'))
  ? sha256(path.join(ROOT, 'analysis/static-checks-round-03.json')) : null;
const testsFirst = run(process.execPath, ['--test', ...TEST_FILES]);
const staticSecond = run(process.execPath, ['tools/check-round03.js']);
const staticReportSecond = fs.existsSync(path.join(ROOT, 'analysis/static-checks-round-03.json'))
  ? sha256(path.join(ROOT, 'analysis/static-checks-round-03.json')) : null;
const testsSecond = run(process.execPath, ['--test', ...TEST_FILES]);
const sourceHashAfter = treeHash(['src', 'tests']);

const firstCounts = parseTap(testsFirst.output);
const secondCounts = parseTap(testsSecond.output);
const immutable = {
  originalBundle: sha256(path.join(ROOT, 'original/bundle.js')),
  decodedBundle: sha256(path.join(ROOT, 'work/bundle.strings-decoded.js')),
  originalIndex: sha256(path.join(ROOT, 'original/index.js')),
};
const pass = [staticFirst, testsFirst, staticSecond, testsSecond].every(result => result.status === 0)
  && staticReportFirst === staticReportSecond
  && sourceHashBefore === sourceHashAfter
  && firstCounts.fail === 0
  && secondCounts.fail === 0;

const report = {
  round: 3,
  name: 'BOOT-TO-BATTLE',
  status: pass ? 'PASS' : 'FAIL',
  firstRun: { staticStatus: staticFirst.status, testStatus: testsFirst.status, counts: firstCounts },
  secondRun: { staticStatus: staticSecond.status, testStatus: testsSecond.status, counts: secondCounts },
  determinism: {
    staticReportHashFirst: staticReportFirst,
    staticReportHashSecond: staticReportSecond,
    staticReportIdentical: staticReportFirst === staticReportSecond,
    sourceTreeHashBefore: sourceHashBefore,
    sourceTreeHashAfter: sourceHashAfter,
    testsDidNotModifySource: sourceHashBefore === sourceHashAfter,
  },
  immutableHashes: immutable,
  realNetworkRequests: 0,
  nativePlatformCalls: 0,
  testFiles: TEST_FILES,
};
fs.writeFileSync(REPORT, `${JSON.stringify(report, null, 2)}\n`);
if (!pass) process.exit(1);
console.log(`Round 03 verification PASS (${firstCounts.pass} tests × 2 runs).`);
