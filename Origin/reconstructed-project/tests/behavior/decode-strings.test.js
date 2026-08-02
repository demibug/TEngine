#!/usr/bin/env node
'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const childProcess = require('child_process');

const root = path.resolve(__dirname, '../..');

function sha256(data) {
  return crypto.createHash('sha256').update(data).digest('hex');
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function loadTypeScript() {
  try { return require('typescript'); } catch (_) {
    const globalRoot = childProcess.execFileSync('npm', ['root', '-g'], { encoding: 'utf8' }).trim();
    return require(path.join(globalRoot, 'typescript'));
  }
}

function lineOffset(text, oneBasedLine) {
  if (oneBasedLine <= 1) return 0;
  let line = 1;
  for (let i = 0; i < text.length; i += 1) {
    if (text.charCodeAt(i) === 10 && ++line === oneBasedLine) return i + 1;
  }
  return text.length;
}

function countJsonl(filePath) {
  const text = fs.readFileSync(filePath, 'utf8');
  if (!text) return 0;
  return text.endsWith('\n') ? text.split('\n').length - 1 : text.split('\n').length;
}

function parseIsValid(fileName, source) {
  const ts = loadTypeScript();
  const sf = ts.createSourceFile(fileName, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.JS);
  return sf.parseDiagnostics.length === 0;
}

function main() {
  const originalPath = path.join(root, 'original/bundle.js');
  const formattedPath = path.join(root, 'work/bundle.formatted.js');
  const decodedPath = path.join(root, 'work/bundle.strings-decoded.js');
  const reportPath = path.join(root, 'analysis/string-decoding-report.json');
  const mapPath = path.join(root, 'analysis/string-decoding-map.jsonl');
  const unresolvedPath = path.join(root, 'analysis/string-decoding-unresolved.jsonl');
  const toolPath = path.join(root, 'tools/decode-strings.js');

  const original = fs.readFileSync(originalPath);
  const formatted = fs.readFileSync(formattedPath);
  const decoded = fs.readFileSync(decodedPath);
  const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));

  assert(sha256(original) === '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595', 'Original bundle hash changed');
  assert(Buffer.compare(original, formatted) === 0, 'Formatted stage is not the recorded identity copy');
  assert(sha256(decoded) === report.output.sha256, 'Decoded output hash does not match report');
  assert(report.input.sha256 === sha256(formatted), 'Report input hash does not match formatted stage');
  assert(report.replacements.count === countJsonl(mapPath), 'Replacement map count mismatch');
  assert(report.unresolved.count === countJsonl(unresolvedPath), 'Unresolved list count mismatch');
  assert(report.safety.antiTamperPreservedByteForByte === true, 'Report does not confirm anti-tamper preservation');
  assert(report.safety.postRuntimeStringTableImmutable === true, 'Post-runtime string table mutation was not ruled out');

  const originalText = original.toString('utf8');
  const decodedText = decoded.toString('utf8');
  const replacementStart = lineOffset(originalText, 1015);
  assert(originalText.slice(0, replacementStart) === decodedText.slice(0, replacementStart), 'Lines 1-1014 changed');
  assert((originalText.match(/\n/g) || []).length === (decodedText.match(/\n/g) || []).length, 'Line count changed');
  assert(parseIsValid(decodedPath, decodedText), 'Decoded JavaScript has syntax diagnostics');

  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'zhaoyun-decode-test-'));
  try {
    const tempOutput = path.join(tempRoot, 'bundle.strings-decoded.js');
    const tempReport = path.join(tempRoot, 'report.json');
    const tempMap = path.join(tempRoot, 'map.jsonl');
    const tempUnresolved = path.join(tempRoot, 'unresolved.jsonl');
    const tempRuntime = path.join(tempRoot, 'runtime.json');

    childProcess.execFileSync(process.execPath, [
      toolPath,
      '--input', formattedPath,
      '--output', tempOutput,
      '--report', tempReport,
      '--map', tempMap,
      '--unresolved', tempUnresolved,
      '--runtime-values', tempRuntime,
    ], {
      cwd: root,
      stdio: ['ignore', 'pipe', 'pipe'],
      maxBuffer: 16 * 1024 * 1024,
    });

    const reproduced = fs.readFileSync(tempOutput);
    const reproducedReport = JSON.parse(fs.readFileSync(tempReport, 'utf8'));
    assert(sha256(reproduced) === sha256(decoded), 'Decoder is not reproducible');
    assert(reproducedReport.replacements.count === report.replacements.count, 'Reproduced replacement count differs');
    assert(reproducedReport.unresolved.count === report.unresolved.count, 'Reproduced unresolved count differs');
  } finally {
    fs.rmSync(tempRoot, { recursive: true, force: true });
  }

  const result = {
    status: 'PASS',
    tests: {
      originalHashUnchanged: true,
      identityFormattedStage: true,
      protectedPrefixByteIdentical: true,
      logicalLineCountPreserved: true,
      outputSyntaxValid: true,
      replacementMapCountMatches: true,
      unresolvedCountMatches: true,
      noPostRuntimeStringTableMutation: true,
      deterministicReproduction: true,
    },
    inputSha256: sha256(original),
    outputSha256: sha256(decoded),
    replacements: report.replacements.count,
    unresolved: report.unresolved.count,
  };
  process.stdout.write(JSON.stringify(result, null, 2) + '\n');
}

try {
  main();
} catch (error) {
  console.error(error && error.stack ? error.stack : String(error));
  process.exitCode = 1;
}
