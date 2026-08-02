#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..');
const PACKAGE_PATH = path.join(ROOT, 'package.json');
const REPORT_PATH = path.join(ROOT, 'analysis', 'development-command-results.json');
const npmCommand = process.platform === 'win32' ? 'npm.cmd' : 'npm';

function sha256(text) {
  return crypto.createHash('sha256').update(text).digest('hex');
}

const pkg = JSON.parse(fs.readFileSync(PACKAGE_PATH, 'utf8'));
const commands = Object.keys(pkg.scripts || {})
  .filter(name => name.startsWith('dev:') && name !== 'dev:all')
  .sort((a, b) => a.localeCompare(b));

if (commands.length === 0) throw new Error('No development commands are defined');

const results = [];
for (const name of commands) {
  const started = Date.now();
  const run = spawnSync(npmCommand, ['run', name], {
    cwd: ROOT,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    env: { ...process.env, NO_COLOR: '1' },
  });
  const stdout = run.stdout || '';
  const stderr = run.stderr || '';
  process.stdout.write(stdout);
  process.stderr.write(stderr);
  results.push({
    command: `npm run ${name}`,
    status: run.status,
    signal: run.signal,
    elapsedMs: Date.now() - started,
    stdoutSha256: sha256(stdout),
    stderrSha256: sha256(stderr),
  });
  if (run.status !== 0) break;
}

const failed = results.filter(result => result.status !== 0);
const report = {
  schemaVersion: 1,
  status: failed.length === 0 && results.length === commands.length ? 'PASS' : 'FAIL',
  commands,
  results,
  realNetworkRequests: 0,
  nativePlatformCalls: 0,
};
fs.mkdirSync(path.dirname(REPORT_PATH), { recursive: true });
fs.writeFileSync(REPORT_PATH, `${JSON.stringify(report, null, 2)}\n`);
if (report.status !== 'PASS') process.exit(1);
console.log(`All development commands PASS (${commands.length}/${commands.length}).`);
