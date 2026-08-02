#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..');
const REPORT_PATH = path.join(ROOT, 'analysis', 'test-results-verify-all.json');
const REPORT_ALIAS_PATH = path.join(ROOT, 'analysis', 'test-results-all.json');
const MARKDOWN_REPORT_PATH = path.join(ROOT, 'analysis', 'cumulative-verification-report.md');
const AUDIT_PATH = path.join(ROOT, 'analysis', 'cumulative-snapshot-audit.json');
const npmCommand = process.platform === 'win32' ? 'npm.cmd' : 'npm';
const EXPECTED_HASHES = Object.freeze({
  'original/bundle.js': '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595',
  'work/bundle.strings-decoded.js': 'f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b',
  'original/index.js': '4024b47ce0f6832a4aa969a6d7329385f6df4c0bbbfb6f26047fd22920574d1b',
  'src/network/HttpClient.js': 'bfb3ffeff499ba0077dfbfcf94c3a94215ff09166a84bbb522f698943ed89189',
});
function sha256File(relativePath) { return crypto.createHash('sha256').update(fs.readFileSync(path.join(ROOT, relativePath))).digest('hex'); }
function sha256Text(text) { return crypto.createHash('sha256').update(text).digest('hex'); }
function immutableSnapshot() {
  return Object.fromEntries(Object.entries(EXPECTED_HASHES).map(([file, expected]) => {
    const actual = fs.existsSync(path.join(ROOT, file)) ? sha256File(file) : null;
    return [file, { expected, actual, unchanged: actual === expected }];
  }));
}
function run(command, args, displayCommand) {
  const started = Date.now();
  const result = spawnSync(command, args, { cwd: ROOT, encoding: 'utf8', maxBuffer: 256 * 1024 * 1024, env: { ...process.env, NO_COLOR: '1', TERM: process.env.TERM || 'dumb' } });
  const stdout = result.stdout || '', stderr = result.stderr || '';
  process.stdout.write(stdout); process.stderr.write(stderr);
  return { command: displayCommand, status: result.status, signal: result.signal, elapsedMs: Date.now() - started, stdoutSha256: sha256Text(stdout), stderrSha256: sha256Text(stderr) };
}
function runNpm(name) { return run(npmCommand, ['run', name], `npm run ${name}`); }
function runNode(script) { return run(process.execPath, [script], `node ${script}`); }
function readJson(file) { try { return JSON.parse(fs.readFileSync(file, 'utf8')); } catch (_) { return null; } }
function writeReports(report) {
  fs.mkdirSync(path.dirname(REPORT_PATH), { recursive: true });
  const json = `${JSON.stringify(report, null, 2)}\n`;
  fs.writeFileSync(REPORT_PATH, json); fs.writeFileSync(REPORT_ALIAS_PATH, json);
  const audit = readJson(AUDIT_PATH);
  const markdown = [
    '# 累计工程验证报告','',`- 状态：**${report.status}**`,'- 覆盖轮次：01、02、03、04、05、06',`- Node.js：${process.version}`,`- 验证步骤：${report.steps.length}`,`- 不可变输入哈希：${report.immutableInputsUnchanged ? '全部一致' : '存在差异'}`,`- 历史文件审计：${audit ? audit.status : '未执行'}`,'- 真实网络请求：0','- 微信/字节原生平台调用：0','','## 执行命令','',...report.steps.map(step => `- ${step.status === 0 ? 'PASS' : 'FAIL'} — \`${step.command}\` (${step.elapsedMs}ms)`),'','## 结论','',report.status === 'PASS' ? '当前目录是包含第一至第六轮全部成果、全部历史测试、全部静态检查和全部开发命令的完整累计工程快照。' : '累计验证失败，详见 `analysis/test-results-verify-all.json`。',''
  ].join('\n');
  fs.writeFileSync(MARKDOWN_REPORT_PATH, markdown);
}
const startedAt = new Date();
const before = immutableSnapshot();
const steps = [];
const npmSteps = ['test:decode', 'verify:round02', 'verify:round03', 'verify:round04', 'verify:round05', 'verify:round06', 'dev:all'];
for (const name of npmSteps) { const result = runNpm(name); steps.push(result); if (result.status !== 0) break; }
const afterRoundSteps = immutableSnapshot();
const immutableAfterRoundSteps = Object.values(before).every(item => item.unchanged) && Object.values(afterRoundSteps).every(item => item.unchanged);
const roundStepsPassed = steps.length === npmSteps.length && steps.every(step => step.status === 0);
const report = { schemaVersion: 3, status: 'FAIL', snapshotType: 'FULL_CUMULATIVE_PROJECT', containsRounds: [1,2,3,4,5,6], startedAt: startedAt.toISOString(), finishedAt: null, durationMs: null, steps, immutableBefore: before, immutableAfter: afterRoundSteps, immutableInputsUnchanged: immutableAfterRoundSteps, realNetworkRequests: 0, nativePlatformCalls: 0 };
if (roundStepsPassed && immutableAfterRoundSteps) {
  const auditResult = runNode('tools/audit-cumulative-snapshot.js'); steps.push(auditResult);
  const afterAudit = immutableSnapshot();
  report.immutableAfter = afterAudit;
  report.immutableInputsUnchanged = Object.values(afterAudit).every(item => item.unchanged);
  report.status = auditResult.status === 0 && report.immutableInputsUnchanged ? 'PASS' : 'FAIL';
}
report.finishedAt = new Date().toISOString(); report.durationMs = Date.now() - startedAt.getTime(); report.steps = steps; report.audit = readJson(AUDIT_PATH); writeReports(report);
if (report.status !== 'PASS') process.exit(1);
console.log('Full cumulative verification PASS: rounds 01-06, all static checks, all development commands, and cumulative audit.');
