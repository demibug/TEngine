#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.resolve(__dirname, '..');
const MANIFEST_REL = 'analysis/cumulative-file-manifest.json';
const LIST_REL = 'analysis/cumulative-file-list.txt';
const DELIVERABLE_REL = 'analysis/deliverable-manifest.json';
const SELF_EXCLUDED = new Set([MANIFEST_REL, LIST_REL, DELIVERABLE_REL]);
const EXCLUDED_DIRS = new Set(['node_modules', '.git']);

const REQUIRED_HISTORY = Object.freeze({
  round01: ['analysis/round-01-report.md', 'tools/decode-strings.js', 'work/bundle.strings-decoded.js'],
  round02: ['analysis/round-02-report.md', 'src/network/HttpClient.js', 'tests/unit/HttpClient.test.js'],
  round03: ['analysis/round-03-report.md', 'src/bootstrap/GameBootstrap.js', 'src/scenes/BattleSceneController.js'],
  round04: ['analysis/round-04-report.md', 'src/entities/Mob0Enemy.js', 'src/battle/MapData.js'],
  round05: ['analysis/round-05-report.md', 'src/units/KnifeSoldier.js', 'src/units/UnitRegistry.js'],
  round06: ['analysis/round-06-report.md', 'src/units/BowSoldier.js', 'src/projectiles/SimpleDynamicArrow.js', 'src/projectiles/ProjectileManager.js'],
});

function toPosix(value) { return value.split(path.sep).join('/'); }
function sha256File(file) { return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex'); }
function walk(dir) {
  const output = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
    if (entry.isDirectory() && EXCLUDED_DIRS.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) output.push(...walk(full));
    else output.push(full);
  }
  return output;
}
function treeHash(entries) {
  const hash = crypto.createHash('sha256');
  for (const entry of entries) {
    hash.update(entry.path); hash.update('\0'); hash.update(entry.sha256); hash.update('\0');
  }
  return hash.digest('hex');
}

fs.mkdirSync(path.join(ROOT, 'analysis'), { recursive: true });
const allBefore = walk(ROOT).map(file => toPosix(path.relative(ROOT, file))).sort();
const contentPaths = allBefore.filter(relativePath => !SELF_EXCLUDED.has(relativePath));
const entries = contentPaths.map(relativePath => {
  const absolutePath = path.join(ROOT, relativePath);
  return { path: relativePath, sizeBytes: fs.statSync(absolutePath).size, sha256: sha256File(absolutePath) };
});
const history = {};
for (const [round, files] of Object.entries(REQUIRED_HISTORY)) {
  const missing = files.filter(file => !fs.existsSync(path.join(ROOT, file)));
  history[round] = { requiredFiles: files, missing, complete: missing.length === 0 };
}
const directoryCounts = {};
for (const entry of entries) {
  const top = entry.path.split('/')[0];
  directoryCounts[top] = (directoryCounts[top] || 0) + 1;
}
const manifest = {
  schemaVersion: 3,
  round: 6,
  snapshot: 'cumulative-rounds-01-06',
  snapshotType: 'FULL_CUMULATIVE_PROJECT',
  containsRounds: [1, 2, 3, 4, 5, 6],
  generatedAt: new Date().toISOString(),
  containsAllHistoricalResults: Object.values(history).every(item => item.complete),
  containsAllHistoricalRounds: Object.values(history).every(item => item.complete),
  status: Object.values(history).every(item => item.complete) ? 'CUMULATIVE_VERIFIED' : 'CUMULATIVE_HISTORY_INCOMPLETE',
  historicalRounds: history,
  immutableInputs: {
    'original/bundle.js': sha256File(path.join(ROOT, 'original/bundle.js')),
    'work/bundle.strings-decoded.js': sha256File(path.join(ROOT, 'work/bundle.strings-decoded.js')),
    'original/index.js': sha256File(path.join(ROOT, 'original/index.js')),
    'src/network/HttpClient.js': sha256File(path.join(ROOT, 'src/network/HttpClient.js')),
  },
  fileCount: entries.length,
  totalBytes: entries.reduce((sum, entry) => sum + entry.sizeBytes, 0),
  projectFileCountExcludingManifestArtifacts: entries.length,
  totalBytesExcludingManifestArtifacts: entries.reduce((sum, entry) => sum + entry.sizeBytes, 0),
  directoryCounts,
  treeSha256: treeHash(entries),
  manifestArtifacts: [MANIFEST_REL, LIST_REL, DELIVERABLE_REL],
  selfHashPolicy: 'Manifest artifacts are listed separately to avoid recursive self-hashing.',
  files: entries,
};
fs.writeFileSync(path.join(ROOT, MANIFEST_REL), `${JSON.stringify(manifest, null, 2)}\n`);
fs.writeFileSync(path.join(ROOT, DELIVERABLE_REL), `${JSON.stringify(manifest, null, 2)}\n`);

const finalPaths = walk(ROOT).map(file => toPosix(path.relative(ROOT, file))).sort();
if (!finalPaths.includes(LIST_REL)) finalPaths.push(LIST_REL);
finalPaths.sort();
fs.writeFileSync(path.join(ROOT, LIST_REL), `${finalPaths.join('\n')}\n`);
console.log(`Cumulative manifest written: ${entries.length} hashed files; ${finalPaths.length} total listed paths.`);
