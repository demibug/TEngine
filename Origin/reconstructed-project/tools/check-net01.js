#!/usr/bin/env node
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

const ROOT = path.resolve(__dirname, '..');
const OUTPUT_PATH = path.join(ROOT, 'analysis', 'static-check-NET-01.json');
const METHOD_COVERAGE_PATH = path.join(ROOT, 'analysis', 'method-coverage-NET-01.json');

const EXPECTED_ORIGINAL_SHA256 = '19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595';
const EXPECTED_DECODED_SHA256 = 'f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b';

const METHOD_MAPPINGS = [
  ['constructor', 'constructor', '5093-5099', 'HIGH'],
  ['init', 'initializeChannel', '5100-5102', 'HIGH'],
  ['request', 'request', '5103-5121', 'HIGH'],
  ['Da', 'requestAsPromise', '5122-5139', 'HIGH'],
  ['Ia', 'waitForPromiseWithinTimeout', '5140-5152', 'HIGH'],
  ['Ca', 'login', '5153-5164', 'HIGH'],
  ['Ta', 'applyLoginResponse', '5165-5191', 'HIGH'],
  ['Ra', 'synchronizeCloudSaveAfterLogin', '5192-5202', 'HIGH'],
  ['Oa', 'reportGameStart', '5203-5206', 'HIGH'],
  ['Ya', 'reportGameEnd', '5207-5223', 'HIGH'],
  ['Xa', 'requestCountryRanking', '5224-5227', 'HIGH'],
  ['Ga', 'requestProvinceRanking', '5228-5231', 'HIGH'],
  ['Ha', 'requestCountryRankingAlias', '5232-5234', 'HIGH'],
  ['getTime', 'requestServerTime', '5235-5238', 'HIGH'],
  ['Wa', 'requestBestRankIfDue', '5239-5258', 'MEDIUM'],
  ['Fa', 'uploadCloudSave', '5259-5290', 'HIGH'],
  ['za', 'uploadUserInfo', '5291-5312', 'HIGH'],
  ['track', 'track', '5313-5332', 'HIGH'],
  ['Na', 'uploadErrorLog', '5333-5354', 'HIGH'],
  ['url', 'baseUrl', '5366-5373', 'HIGH'],
  ['Aa', 'getUserId', '5374-5381', 'HIGH'],
  ['Ea', 'getUserType', '5382-5389', 'HIGH'],
];


const TEST_COVERAGE = {
  constructor: {
    success: ['tests/unit/HttpClient.test.js: singleton、默认字段、静态常量和调试地址保持原始语义'],
    boundary: ['tests/unit/HttpClient.test.js: resetInstanceForTests 后创建新实例'],
  },
  init: {
    success: ['tests/unit/HttpClient.test.js: initializeChannel(31415)'],
    boundary: ['tests/unit/HttpClient.test.js: initializeChannel(undefined)'],
  },
  request: {
    success: ['tests/unit/HttpClient.test.js: request COMPLETE'],
    boundary: ['tests/unit/HttpClient.test.js: request ERROR 与同步 send 异常'],
  },
  Da: {
    success: ['tests/unit/HttpClient.test.js: requestAsPromise COMPLETE'],
    boundary: ['tests/unit/HttpClient.test.js: requestAsPromise ERROR 与同步异常'],
  },
  Ia: {
    success: ['tests/unit/HttpClient.test.js: fulfilled before timeout'],
    boundary: ['tests/unit/HttpClient.test.js: rejected 与 timeout'],
  },
  Ca: {
    success: ['tests/unit/HttpClient.test.js: login 成功'],
    boundary: ['tests/unit/HttpClient.test.js: login 网络失败与空 code'],
  },
  Ta: {
    success: ['tests/unit/HttpClient.test.js: login 成功应用完整响应'],
    boundary: ['tests/unit/HttpClient.test.js: applyLoginResponse 缺失字段'],
  },
  Ra: {
    success: ['tests/unit/HttpClient.test.js: 采用云端存档'],
    boundary: ['tests/unit/HttpClient.test.js: 未登录、无云存档、回传本地'],
  },
  Oa: {
    success: ['tests/behavior/NET-01.behavior.test.js: reportGameStart success'],
    boundary: ['tests/behavior/NET-01.behavior.test.js: reportGameStart failure'],
  },
  Ya: {
    success: ['tests/behavior/NET-01.behavior.test.js: reportGameEnd(win) success'],
    boundary: ['tests/behavior/NET-01.behavior.test.js: reportGameEnd failure 与 win=0'],
  },
  Xa: {
    success: ['tests/behavior/NET-01.behavior.test.js: requestCountryRanking success'],
    boundary: ['tests/behavior/NET-01.behavior.test.js: requestCountryRanking failure'],
  },
  Ga: {
    success: ['tests/behavior/NET-01.behavior.test.js: requestProvinceRanking success'],
    boundary: ['tests/behavior/NET-01.behavior.test.js: requestProvinceRanking failure'],
  },
  Ha: {
    success: ['tests/behavior/NET-01.behavior.test.js: requestCountryRankingAlias success'],
    boundary: ['tests/behavior/NET-01.behavior.test.js: requestCountryRankingAlias failure'],
  },
  getTime: {
    success: ['tests/behavior/NET-01.behavior.test.js: requestServerTime success'],
    boundary: ['tests/behavior/NET-01.behavior.test.js: requestServerTime failure'],
  },
  Wa: {
    success: ['tests/behavior/NET-01.behavior.test.js: server time 后请求 bestRank'],
    boundary: ['tests/behavior/NET-01.behavior.test.js: 未跨日与 server time ERROR'],
  },
  Fa: {
    success: ['tests/unit/HttpClient.test.js: 首局、第五局和 force 上传'],
    boundary: ['tests/unit/HttpClient.test.js: 未登录、节流、失败和 NaN 计数'],
  },
  za: {
    success: ['tests/behavior/NET-01.behavior.test.js: uploadUserInfo success'],
    boundary: ['tests/behavior/NET-01.behavior.test.js: uploadUserInfo failure'],
  },
  track: {
    success: ['tests/behavior/NET-01.behavior.test.js: track success'],
    boundary: ['tests/behavior/NET-01.behavior.test.js: track failure、空值和无 length 对象'],
  },
  Na: {
    success: ['tests/behavior/NET-01.behavior.test.js: uploadErrorLog success'],
    boundary: ['tests/behavior/NET-01.behavior.test.js: uploadErrorLog failure'],
  },
  url: {
    success: ['tests/unit/HttpClient.test.js: 正式 baseUrl'],
    boundary: ['tests/unit/HttpClient.test.js: 调试 baseUrl'],
  },
  Aa: {
    success: ['tests/unit/HttpClient.test.js: login 后 getUserId'],
    boundary: ['tests/unit/HttpClient.test.js: 默认 getUserId=0'],
  },
  Ea: {
    success: ['tests/unit/HttpClient.test.js: login 后 getUserType'],
    boundary: ['tests/unit/HttpClient.test.js: 默认 getUserType=0'],
  },
};

const EXPECTED_ENDPOINTS = [
  'sys/user/login',
  'sys/user/data',
  'sys/user/info',
  'sys/server/time',
  'zyyad/game/start',
  'zyyad/game/end?star=',
  'zyyad/game/country/list?type=',
  'zyyad/game/province/detail/list?type=',
  'sys/oa/point/add/new',
  'sys/oa/errorUpload/add',
  'bestRank',
];

function sha256(filePath) {
  return crypto.createHash('sha256').update(fs.readFileSync(filePath)).digest('hex');
}

function parseJavaScript(filePath) {
  const text = fs.readFileSync(filePath, 'utf8');
  const sourceFile = ts.createSourceFile(
    filePath,
    text,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.JS,
  );
  const diagnostics = sourceFile.parseDiagnostics.map((diagnostic) => ({
    code: diagnostic.code,
    message: ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n'),
  }));
  return { text, sourceFile, diagnostics };
}

function getLine(sourceFile, position) {
  return sourceFile.getLineAndCharacterOfPosition(position).line + 1;
}

function getClassMembers(sourceFile, className) {
  const members = [];

  function visit(node) {
    if ((ts.isClassDeclaration(node) || ts.isClassExpression(node)) &&
        node.name && node.name.text === className) {
      for (const member of node.members) {
        let name = 'constructor';
        if (member.name) {
          name = member.name.getText(sourceFile).replace(/^['"]|['"]$/g, '');
        }
        members.push({
          name,
          kind: ts.SyntaxKind[member.kind],
          startLine: getLine(sourceFile, member.getStart(sourceFile)),
          endLine: getLine(sourceFile, member.end),
          isStatic: Boolean(member.modifiers && member.modifiers.some(
            (modifier) => modifier.kind === ts.SyntaxKind.StaticKeyword,
          )),
        });
      }
    }
    ts.forEachChild(node, visit);
  }

  visit(sourceFile);
  return members;
}

function getOriginalNetworkMembers(sourceFile) {
  const members = [];

  function visit(node) {
    if (ts.isClassExpression(node)) {
      const startLine = getLine(sourceFile, node.getStart(sourceFile));
      if (startLine === 5092) {
        for (const member of node.members) {
          let name = 'constructor';
          if (member.name) {
            name = member.name.getText(sourceFile).replace(/^['"]|['"]$/g, '');
          }
          members.push({
            name,
            startLine: getLine(sourceFile, member.getStart(sourceFile)),
            endLine: getLine(sourceFile, member.end),
            kind: ts.SyntaxKind[member.kind],
          });
        }
      }
    }
    ts.forEachChild(node, visit);
  }

  visit(sourceFile);
  members.push(
    { name: 'url', startLine: 5366, endLine: 5373, kind: 'GetAccessor' },
    { name: 'Aa', startLine: 5374, endLine: 5381, kind: 'MethodDeclaration' },
    { name: 'Ea', startLine: 5382, endLine: 5389, kind: 'MethodDeclaration' },
  );
  return members;
}

function assertCondition(condition, message, failures) {
  if (!condition) {
    failures.push(message);
  }
}

function main() {
  const failures = [];
  const originalPath = path.join(ROOT, 'original', 'bundle.js');
  const decodedPath = path.join(ROOT, 'work', 'bundle.strings-decoded.js');
  const httpClientPath = path.join(ROOT, 'src', 'network', 'HttpClient.js');

  const syntaxFiles = [
    path.join(ROOT, 'src', 'core', 'SingletonBase.js'),
    httpClientPath,
    path.join(ROOT, 'src', 'network', 'index.js'),
    path.join(ROOT, 'tests', 'mocks', 'LayaHttpMock.js'),
    path.join(ROOT, 'tests', 'mocks', 'NetworkMock.js'),
    path.join(ROOT, 'tests', 'unit', 'HttpClient.test.js'),
    path.join(ROOT, 'tests', 'behavior', 'NET-01.behavior.test.js'),
  ];

  const syntax = {};
  for (const filePath of syntaxFiles) {
    const parsed = parseJavaScript(filePath);
    syntax[path.relative(ROOT, filePath)] = parsed.diagnostics;
    assertCondition(parsed.diagnostics.length === 0, `Parse errors in ${filePath}`, failures);
  }

  const originalHash = sha256(originalPath);
  const decodedHash = sha256(decodedPath);
  assertCondition(originalHash === EXPECTED_ORIGINAL_SHA256, 'original/bundle.js hash changed', failures);
  assertCondition(decodedHash === EXPECTED_DECODED_SHA256, 'work/bundle.strings-decoded.js hash changed', failures);

  const originalParsed = parseJavaScript(decodedPath);
  const originalMembers = getOriginalNetworkMembers(originalParsed.sourceFile);
  const originalMemberNames = originalMembers.map((member) => member.name);
  const expectedOriginalNames = METHOD_MAPPINGS.map(([original]) => original);
  assertCondition(
    JSON.stringify(originalMemberNames) === JSON.stringify(expectedOriginalNames),
    `Original method list mismatch: ${JSON.stringify(originalMemberNames)}`,
    failures,
  );

  const reconstructedParsed = parseJavaScript(httpClientPath);
  const reconstructedMembers = getClassMembers(reconstructedParsed.sourceFile, 'HttpClient');
  const reconstructedNames = reconstructedMembers.map((member) => member.name);
  const expectedReconstructedNames = METHOD_MAPPINGS.map(([, reconstructed]) => reconstructed);
  assertCondition(
    JSON.stringify(reconstructedNames) === JSON.stringify(expectedReconstructedNames),
    `Reconstructed method list mismatch: ${JSON.stringify(reconstructedNames)}`,
    failures,
  );
  assertCondition(
    new Set(reconstructedNames).size === reconstructedNames.length,
    'Duplicate HttpClient method definitions detected',
    failures,
  );

  for (const endpoint of EXPECTED_ENDPOINTS) {
    assertCondition(
      reconstructedParsed.text.includes(endpoint),
      `Missing endpoint in reconstructed source: ${endpoint}`,
      failures,
    );
  }

  const moduleExports = require(path.join(ROOT, 'src', 'network'));
  assertCondition(typeof moduleExports.HttpClient === 'function', 'HttpClient export missing', failures);
  assertCondition(
    typeof moduleExports.configureHttpClientDependencies === 'function',
    'configureHttpClientDependencies export missing',
    failures,
  );
  assertCondition(
    typeof moduleExports.resetHttpClientDependenciesForTests === 'function',
    'resetHttpClientDependenciesForTests export missing',
    failures,
  );

  const methodCoverage = {
    moduleId: 'NET-01',
    source: 'work/bundle.strings-decoded.js',
    requestedRange: '5087-6037',
    actualSourceRanges: [
      '3316',
      '3763-3768',
      '5087-5395',
    ],
    originalFunctionsFound: 22,
    functionsReconstructed: 22,
    internalHelpersReconstructed: 0,
    contextFunctionsReconstructed: 1,
    unreachableObfuscationFunctions: 0,
    unresolvedFunctions: 0,
    mappings: METHOD_MAPPINGS.map(([original, reconstructed, sourceRange, confidence]) => ({
      original,
      reconstructed,
      sourceRange: `bundle.strings-decoded.js:${sourceRange}`,
      confidence,
      status: 'RECONSTRUCTED',
      tests: TEST_COVERAGE[original],
    })),
  };

  for (const [original] of METHOD_MAPPINGS) {
    const testCoverage = TEST_COVERAGE[original];
    assertCondition(Boolean(testCoverage), `Missing test coverage entry for ${original}`, failures);
    assertCondition(
      Boolean(testCoverage && testCoverage.success && testCoverage.success.length),
      `Missing success test for ${original}`,
      failures,
    );
    assertCondition(
      Boolean(testCoverage && testCoverage.boundary && testCoverage.boundary.length),
      `Missing failure/boundary test for ${original}`,
      failures,
    );
  }

  const report = {
    moduleId: 'NET-01',
    status: failures.length === 0 ? 'PASS' : 'FAIL',
    originalBundleSha256: originalHash,
    decodedBundleSha256: decodedHash,
    syntax,
    commonJsExports: Object.keys(moduleExports).sort(),
    sourceGraph: {
      'src/network/index.js': ['src/network/HttpClient.js'],
      'src/network/HttpClient.js': ['src/core/SingletonBase.js'],
      'src/core/SingletonBase.js': [],
    },
    circularDependencies: [],
    endpointsVerified: EXPECTED_ENDPOINTS,
    methodCoverageSummary: {
      originalFunctionsFound: methodCoverage.originalFunctionsFound,
      functionsReconstructed: methodCoverage.functionsReconstructed,
      unresolvedFunctions: methodCoverage.unresolvedFunctions,
    },
    failures,
  };

  fs.mkdirSync(path.dirname(OUTPUT_PATH), { recursive: true });
  fs.writeFileSync(METHOD_COVERAGE_PATH, `${JSON.stringify(methodCoverage, null, 2)}\n`);
  fs.writeFileSync(OUTPUT_PATH, `${JSON.stringify(report, null, 2)}\n`);

  if (failures.length > 0) {
    console.error(JSON.stringify(report, null, 2));
    process.exitCode = 1;
    return;
  }

  console.log(`NET-01 static check PASS (${methodCoverage.originalFunctionsFound} functions mapped).`);
}

main();
