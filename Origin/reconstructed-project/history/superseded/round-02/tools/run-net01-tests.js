"use strict";

const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const root = path.resolve(__dirname, "..");
const testFiles = [
  "tests/unit/HttpClient.test.js",
  "tests/behavior/NET-01.behavior.test.js"
];

function runOnce(printOutput) {
  const result = spawnSync(
    process.execPath,
    ["--test", ...testFiles],
    {
      cwd: root,
      encoding: "utf8",
      env: { ...process.env, NO_COLOR: "1" }
    }
  );

  const output = `${result.stdout || ""}${result.stderr || ""}`;
  if (printOutput) {
    process.stdout.write(output);
  }

  const readCount = (label) => {
    const match = output.match(new RegExp(`^# ${label} (\\d+)$`, "m"));
    return match ? Number(match[1]) : null;
  };

  return {
    status: result.status,
    signal: result.signal,
    tests: readCount("tests"),
    suites: readCount("suites"),
    pass: readCount("pass"),
    fail: readCount("fail"),
    cancelled: readCount("cancelled"),
    skipped: readCount("skipped"),
    todo: readCount("todo"),
    outputSha256: crypto.createHash("sha256").update(output).digest("hex"),
    output
  };
}

const first = runOnce(true);
const second = runOnce(false);

const comparableKeys = [
  "status",
  "tests",
  "suites",
  "pass",
  "fail",
  "cancelled",
  "skipped",
  "todo"
];
const deterministic = comparableKeys.every((key) => first[key] === second[key]);

const report = {
  schemaVersion: 1,
  moduleId: "NET-01",
  command: `${process.execPath} --test ${testFiles.join(" ")}`,
  realNetworkAccessed: false,
  firstRun: Object.fromEntries(
    comparableKeys.map((key) => [key, first[key]])
  ),
  repeatedRun: Object.fromEntries(
    comparableKeys.map((key) => [key, second[key]])
  ),
  deterministic,
  testFiles,
  generatedAt: new Date().toISOString()
};

const reportPath = path.join(root, "analysis/test-results-round-02.json");
fs.mkdirSync(path.dirname(reportPath), { recursive: true });
fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`);

if (first.status !== 0 || second.status !== 0 || !deterministic) {
  process.exitCode = 1;
}
