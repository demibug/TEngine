"use strict";

const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const ts = require("typescript");

const root = path.resolve(__dirname, "..");
const originalPath = path.join(root, "original/bundle.js");
const decodedPath = path.join(root, "work/bundle.strings-decoded.js");
const sourcePath = path.join(root, "src/network/HttpClient.js");
const symbolMapPath = path.join(root, "analysis/mappings/NET-01-symbol-map.json");
const coveragePath = path.join(root, "analysis/modules/NET-01-method-coverage.json");
const reportPath = path.join(root, "analysis/static-checks-round-02.json");
const unitTestPath = path.join(root, "tests/unit/HttpClient.test.js");
const behaviorTestPath = path.join(root, "tests/behavior/NET-01.behavior.test.js");

const EXPECTED_ORIGINAL_HASH = "19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595";
const EXPECTED_DECODED_HASH = "f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b";
const expectedOriginalMembers = [
  "constructor",
  "init",
  "request",
  "Da",
  "Ia",
  "Ca",
  "Ta",
  "Ra",
  "Oa",
  "Ya",
  "Xa",
  "Ga",
  "Ha",
  "getTime",
  "Wa",
  "Fa",
  "za",
  "track",
  "Na",
  "url",
  "Aa",
  "Ea"
];
const expectedReconstructedMembers = [
  "constructor",
  "init",
  "request",
  "requestAsync",
  "didResolveWithinTimeout",
  "login",
  "applyLoginResponse",
  "synchronizeCloudSaveAfterLogin",
  "reportGameStart",
  "reportGameEnd",
  "fetchCountryLeaderboard",
  "fetchProvinceLeaderboard",
  "fetchDefaultLeaderboard",
  "getServerTime",
  "requestBestRankIfDue",
  "uploadCloudSave",
  "uploadUserInfo",
  "track",
  "uploadErrorLog",
  "baseUrl",
  "getUserId",
  "getUserType"
];
const expectedEndpoints = [
  "https://api01.mihuangame.com/api/v2/",
  "https://debug.mihuangame.com/api/v2/",
  "sys/user/login",
  "zyyad/game/start",
  "zyyad/game/end?star=",
  "zyyad/game/country/list?type=",
  "zyyad/game/province/detail/list?type=",
  "sys/server/time",
  "bestRank",
  "sys/user/data",
  "sys/user/info",
  "sys/oa/point/add/new",
  "sys/oa/errorUpload/add"
];

function sha256(filePath) {
  return crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex");
}

function lineOf(sourceFile, position) {
  return sourceFile.getLineAndCharacterOfPosition(position).line + 1;
}

function memberName(member, sourceFile) {
  if (ts.isConstructorDeclaration(member)) return "constructor";
  return member.name ? member.name.getText(sourceFile).replace(/^['"]|['"]$/g, "") : "";
}

function collectOriginalMembers(text) {
  const sourceFile = ts.createSourceFile(
    decodedPath,
    text,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.JS
  );
  let targetClass = null;

  function visit(node) {
    if (ts.isClassExpression(node) && lineOf(sourceFile, node.getStart(sourceFile)) === 5092) {
      targetClass = node;
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);

  if (!targetClass) throw new Error("Original NET-01 class at line 5092 was not found");

  const members = targetClass.members.map((member) => memberName(member, sourceFile));
  const tail = text.split(/\r?\n/).slice(5355, 5390).join("\n");
  for (const name of ["url", "Aa", "Ea"]) {
    if (tail.includes(`d["prototype"], "${name}"`)) {
      members.push(name);
    }
  }
  return members;
}

function collectReconstructedMembers(text) {
  const sourceFile = ts.createSourceFile(
    sourcePath,
    text,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.JS
  );
  let targetClass = null;

  function visit(node) {
    if (ts.isClassDeclaration(node) && node.name && node.name.text === "HttpClient") {
      targetClass = node;
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);

  if (!targetClass) throw new Error("Reconstructed HttpClient class was not found");
  return targetClass.members.map((member) => memberName(member, sourceFile));
}

function sameSet(actual, expected) {
  return actual.length === expected.length && expected.every((item) => actual.includes(item));
}

function duplicateItems(items) {
  return [...new Set(items.filter((item, index) => items.indexOf(item) !== index))];
}

(async () => {
  const originalHash = sha256(originalPath);
  const decodedHash = sha256(decodedPath);
  const decodedText = fs.readFileSync(decodedPath, "utf8");
  const sourceText = fs.readFileSync(sourcePath, "utf8");
  const symbolMap = JSON.parse(fs.readFileSync(symbolMapPath, "utf8"));
  const testText = `${fs.readFileSync(unitTestPath, "utf8")}
${fs.readFileSync(behaviorTestPath, "utf8")}`;
  const testTitles = new Set(
    [...testText.matchAll(/\btest\(\s*["'`]([^"'`]+)["'`]/g)].map((match) => match[1])
  );

  const originalMembers = collectOriginalMembers(decodedText);
  const reconstructedMembers = collectReconstructedMembers(sourceText);
  const mappedOriginalSymbols = symbolMap.symbols
    .filter((entry) => ["constructor", "method", "getter"].includes(entry.kind))
    .map((entry) => entry.originalSymbol);

  const checks = {
    originalHashUnchanged: originalHash === EXPECTED_ORIGINAL_HASH,
    decodedHashUnchanged: decodedHash === EXPECTED_DECODED_HASH,
    originalMemberInventoryComplete: sameSet(originalMembers, expectedOriginalMembers),
    reconstructedMemberInventoryComplete: sameSet(reconstructedMembers, expectedReconstructedMembers),
    symbolMapCoversOriginalMembers: expectedOriginalMembers.every((name) => mappedOriginalSymbols.includes(name)),
    noDuplicateReconstructedMembers: duplicateItems(reconstructedMembers).length === 0,
    endpointsPreserved: expectedEndpoints.every((endpoint) => sourceText.includes(endpoint)),
    authenticationHeaderPreserved:
      sourceText.includes('"authentication"') && sourceText.includes('"application/json"'),
    requestOrderPreserved:
      sourceText.indexOf("request.http.timeout = timeout") < sourceText.indexOf("request.send(") &&
      sourceText.indexOf("request.send(") < sourceText.indexOf("request.once(Laya.Event.COMPLETE") &&
      sourceText.indexOf("request.once(Laya.Event.COMPLETE") < sourceText.indexOf("request.once(Laya.Event.ERROR"),
    noDirectPlatformApi: !/\b(?:wx|tt)\s*\./.test(sourceText),
    onlyExpectedImport:
      /import\s+\{\s*SingletonBase\s*\}\s+from\s+"\.\.\/core\/SingletonBase\.js"/.test(sourceText),
    noCircularImport: !fs.readFileSync(path.join(root, "src/core/SingletonBase.js"), "utf8").includes("network/HttpClient")
  };

  const sourceFilesForCheck = [
    path.join(root, "src/core/SingletonBase.js"),
    sourcePath,
    path.join(root, "src/network/index.js")
  ];
  const compilerOptions = {
    allowJs: true,
    checkJs: true,
    noEmit: true,
    target: ts.ScriptTarget.ES2022,
    module: ts.ModuleKind.NodeNext,
    moduleResolution: ts.ModuleResolutionKind.NodeNext,
    lib: ["lib.es2022.d.ts", "lib.dom.d.ts"],
    skipLibCheck: true
  };
  const program = ts.createProgram(sourceFilesForCheck, compilerOptions);
  const diagnostics = ts.getPreEmitDiagnostics(program);
  const undefinedIdentifierCodes = new Set([2304, 2552, 2580]);
  checks.javascriptSyntaxValid = sourceFilesForCheck.every((filePath) => {
    const text = fs.readFileSync(filePath, "utf8");
    const parsed = ts.createSourceFile(filePath, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.JS);
    return parsed.parseDiagnostics.length === 0;
  });
  checks.noUndefinedIdentifiers = !diagnostics.some((diagnostic) =>
    undefinedIdentifierCodes.has(diagnostic.code)
  );
  // TS2556 is the sole permitted checkJs diagnostic and comes from preserving
  // the original `super(...arguments)` constructor forwarding exactly.
  checks.noUnexpectedTypeCheckDiagnostics = diagnostics.every(
    (diagnostic) => diagnostic.code === 2556
  );

  let importCheck = false;
  try {
    const moduleUrl = `${pathToFileURL(sourcePath).href}?verify=${Date.now()}`;
    const imported = await import(moduleUrl);
    importCheck = typeof imported.HttpClient === "function";
  } catch (error) {
    checks.importError = String(error && error.stack || error);
  }
  checks.importExportValid = importCheck;

  const coverage = {
    moduleId: "NET-01",
    requestedRange: "bundle.strings-decoded.js:5087-6037",
    actualRanges: [
      "bundle.strings-decoded.js:3316",
      "bundle.strings-decoded.js:3763-3768",
      "bundle.strings-decoded.js:5087-5395"
    ],
    originalFunctionsFound: 22,
    functionsReconstructed: 21,
    internalHelpersReconstructed: 1,
    unreachableObfuscationFunctions: 0,
    unresolvedFunctions: 0,
    originalMembers,
    reconstructedMembers,
    internalHelper: {
      originalSymbol: "Ta",
      reconstructedSymbol: "applyLoginResponse"
    },
    methodTests: {
      constructor: ["singleton, constructor defaults, init, base URL and accessors"],
      init: ["singleton, constructor defaults, init, base URL and accessors"],
      request: [
        "request preserves exact URL, headers, timeout, response type and original call order",
        "request forwards ERROR and does not invoke success",
        "Laya JSON completion reaches success with parsed object",
        "illegal JSON and empty JSON response follow Laya ERROR path"
      ],
      Da: ["requestAsync resolves on COMPLETE and rejects on ERROR"],
      Ia: [
        "didResolveWithinTimeout returns true for resolution and clears timer",
        "didResolveWithinTimeout returns false for rejection",
        "didResolveWithinTimeout returns false on timer and ignores later resolution"
      ],
      Ca: [
        "login rejects an empty code through callback without creating a request",
        "login posts payload, applies response before success callback, and emits user event",
        "login handles request rejection, warns, invokes fail and resolves null"
      ],
      Ta: [
        "login posts payload, applies response before success callback, and emits user event",
        "applyLoginResponse keeps prior authentication on falsy token and uses unknown province"
      ],
      Ra: [
        "synchronizeCloudSaveAfterLogin covers not logged in and absent cloud payload",
        "synchronizeCloudSaveAfterLogin applies accepted cloud data and refreshes dependent props",
        "synchronizeCloudSaveAfterLogin force-uploads local data when cloud is not selected"
      ],
      Oa: ["reporting and leaderboard wrappers preserve every endpoint and both callback paths"],
      Ya: [
        "reporting and leaderboard wrappers preserve every endpoint and both callback paths",
        "reportGameEnd supplies an empty callback object when none is given"
      ],
      Xa: ["reporting and leaderboard wrappers preserve every endpoint and both callback paths"],
      Ga: ["reporting and leaderboard wrappers preserve every endpoint and both callback paths"],
      Ha: ["reporting and leaderboard wrappers preserve every endpoint and both callback paths"],
      getTime: ["reporting and leaderboard wrappers preserve every endpoint and both callback paths"],
      Wa: ["requestBestRankIfDue requests bestRank only when day difference is at least one"],
      Fa: [
        "uploadCloudSave skips when logged out and skips non-first/non-fifth counts",
        "uploadCloudSave uploads on first and fifth counts and preserves success/fail logs",
        "uploadCloudSave force mode skips the counter and still uploads"
      ],
      za: ["uploadUserInfo preserves POST payload and logs both outcomes"],
      track: ["track ignores empty input and forwards success/failure through caller callbacks"],
      Na: ["uploadErrorLog preserves POST endpoint and logs both outcomes"],
      url: ["singleton, constructor defaults, init, base URL and accessors"],
      Aa: ["singleton, constructor defaults, init, base URL and accessors"],
      Ea: ["singleton, constructor defaults, init, base URL and accessors"]
    }
  };

  const referencedTestTitles = Object.values(coverage.methodTests).flat();
  checks.everyOriginalMemberHasTest = expectedOriginalMembers.every(
    (member) => Array.isArray(coverage.methodTests[member]) && coverage.methodTests[member].length > 0
  );
  checks.methodTestReferencesExist = referencedTestTitles.every((title) => testTitles.has(title));

  const report = {
    schemaVersion: 1,
    moduleId: "NET-01",
    checks,
    allPassed: Object.values(checks).every((value) => value === true),
    hashes: {
      originalBundleSha256: originalHash,
      decodedBundleSha256: decodedHash,
      reconstructedSourceSha256: sha256(sourcePath),
      symbolMapSha256: sha256(symbolMapPath)
    },
    generatedAt: new Date().toISOString()
  };

  fs.writeFileSync(coveragePath, `${JSON.stringify(coverage, null, 2)}\n`);
  fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`);
  process.stdout.write(`${JSON.stringify(report, null, 2)}\n`);

  if (!report.allPassed) {
    process.exitCode = 1;
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
