'use strict';

const assert = require('node:assert/strict');
const { test, beforeEach, afterEach } = require('node:test');

const {
  HttpClient,
  configureHttpClientDependencies,
  resetHttpClientDependenciesForTests,
} = require('../../src/network');
const { createLayaHttpMock } = require('../mocks/LayaHttpMock');
const { createNetworkMock } = require('../mocks/NetworkMock');

let originalConsole;
let consoleOutput;

beforeEach(() => {
  HttpClient.resetInstanceForTests();
  resetHttpClientDependenciesForTests();

  originalConsole = {
    log: console.log,
    warn: console.warn,
    error: console.error,
  };
  consoleOutput = {
    log: [],
    warn: [],
    error: [],
  };
  console.log = (...args) => consoleOutput.log.push(args);
  console.warn = (...args) => consoleOutput.warn.push(args);
  console.error = (...args) => consoleOutput.error.push(args);
});

afterEach(() => {
  console.log = originalConsole.log;
  console.warn = originalConsole.warn;
  console.error = originalConsole.error;
  HttpClient.resetInstanceForTests();
  resetHttpClientDependenciesForTests();
});

function setup(options = {}) {
  HttpClient.resetInstanceForTests();
  resetHttpClientDependenciesForTests();
  const layaMock = createLayaHttpMock();
  const networkMock = createNetworkMock(layaMock.Laya, options);
  configureHttpClientDependencies(networkMock.dependencies);
  return {
    client: HttpClient.instance(),
    layaMock,
    networkMock,
  };
}

function callbackRecorder() {
  const state = {
    successes: [],
    failures: [],
  };
  return {
    state,
    callbacks: {
      success: (value) => state.successes.push(value),
      fail: (error) => state.failures.push(error),
    },
  };
}

test('所有直接回调式 GET 上报/排行/时间接口保持 URL、方法、数据和回调', async (t) => {
  const cases = [
    {
      name: 'reportGameStart',
      invoke: (client, callbacks) => client.reportGameStart(callbacks),
      expectedPath: 'zyyad/game/start',
      expectedData: null,
      expectedMethod: 'get',
    },
    {
      name: 'reportGameEnd(win)',
      invoke: (client, callbacks) => client.reportGameEnd(true, callbacks),
      expectedPath: 'zyyad/game/end?star=12&win=1',
      expectedData: { skin: 1 },
      expectedMethod: 'get',
    },
    {
      name: 'requestCountryRanking',
      invoke: (client, callbacks) => client.requestCountryRanking(callbacks),
      expectedPath: 'zyyad/game/country/list?type=3',
      expectedData: null,
      expectedMethod: 'get',
    },
    {
      name: 'requestProvinceRanking',
      invoke: (client, callbacks) => client.requestProvinceRanking(callbacks),
      expectedPath: 'zyyad/game/province/detail/list?type=3',
      expectedData: null,
      expectedMethod: 'get',
    },
    {
      name: 'requestCountryRankingAlias',
      invoke: (client, callbacks) => client.requestCountryRankingAlias(callbacks),
      expectedPath: 'zyyad/game/country/list?type=3',
      expectedData: null,
      expectedMethod: 'get',
    },
    {
      name: 'requestServerTime',
      invoke: (client, callbacks) => client.requestServerTime(callbacks),
      expectedPath: 'sys/server/time',
      expectedData: null,
      expectedMethod: 'get',
    },
  ];

  for (const testCase of cases) {
    await t.test(`${testCase.name} success`, () => {
      const { client, layaMock } = setup();
      const recorder = callbackRecorder();
      testCase.invoke(client, recorder.callbacks);

      const request = layaMock.lastRequest;
      assert.equal(
        request.sent.url,
        `https://api01.mihuangame.com/api/v2/${testCase.expectedPath}`,
      );
      assert.deepEqual(request.sent.data, testCase.expectedData);
      assert.equal(request.sent.method, testCase.expectedMethod);
      assert.equal(request.sent.responseType, 'json');

      const response = { code: 1203, data: { accepted: false } };
      request.complete(response);
      // NET-01 不检查业务 code，COMPLETE 仍进入 success。
      assert.deepEqual(recorder.state.successes, [response]);
      assert.deepEqual(recorder.state.failures, []);
    });

    await t.test(`${testCase.name} failure`, () => {
      const { client, layaMock } = setup();
      const recorder = callbackRecorder();
      testCase.invoke(client, recorder.callbacks);
      layaMock.lastRequest.fail('network-down');
      assert.deepEqual(recorder.state.successes, []);
      assert.deepEqual(recorder.state.failures, ['network-down']);
    });
  }
});

test('reportGameEnd 的失败局仍保留 win=0 和固定 {skin:1} 请求体', () => {
  const { client, layaMock } = setup({ curStar: 33 });
  const recorder = callbackRecorder();

  client.reportGameEnd(false, recorder.callbacks);

  assert.equal(
    layaMock.lastRequest.sent.url,
    'https://api01.mihuangame.com/api/v2/zyyad/game/end?star=33&win=0',
  );
  assert.deepEqual(layaMock.lastRequest.sent.data, { skin: 1 });
  layaMock.lastRequest.fail('end-report-error');
  assert.deepEqual(recorder.state.failures, ['end-report-error']);
});

test('uploadUserInfo 成功和失败都只记录原始日志，不改变响应判定', () => {
  const { client, layaMock } = setup();
  const payload = { nick: 'tester', avatarUrl: 'avatar.png', province: '四川' };

  client.uploadUserInfo(payload);
  assert.equal(
    layaMock.lastRequest.sent.url,
    'https://api01.mihuangame.com/api/v2/sys/user/info',
  );
  assert.equal(layaMock.lastRequest.sent.method, 'post');
  assert.strictEqual(layaMock.lastRequest.sent.data, payload);
  layaMock.lastRequest.complete({ code: 500, data: null });
  assert.equal(consoleOutput.log.some((entry) => entry[0] === '上传用户数据成功'), true);

  client.uploadUserInfo(payload);
  layaMock.lastRequest.fail('user-info-failed');
  assert.equal(consoleOutput.log.some((entry) => entry[0] === '上传用户数据失败'), true);
});

test('track 保留非空判断、POST 路径和成功回调无参数行为', () => {
  const { client, layaMock } = setup();
  const callbackArguments = [];
  const failures = [];
  const payload = [{ point: 'battle_start', value: 1 }];

  client.track(payload, {
    success: (...args) => callbackArguments.push(args),
    fail: (error) => failures.push(error),
  });
  assert.equal(
    layaMock.lastRequest.sent.url,
    'https://api01.mihuangame.com/api/v2/sys/oa/point/add/new',
  );
  assert.equal(layaMock.lastRequest.sent.method, 'post');
  assert.strictEqual(layaMock.lastRequest.sent.data, payload);
  layaMock.lastRequest.complete({ data: 'ignored-by-track' });
  assert.deepEqual(callbackArguments, [[]]);
  assert.deepEqual(failures, []);

  client.track(payload, {
    fail: (error) => failures.push(error),
  });
  layaMock.lastRequest.fail('track-failed');
  assert.deepEqual(failures, ['track-failed']);

  const requestCount = layaMock.requests.length;
  client.track(null, {});
  client.track([], {});
  client.track('', {});
  assert.equal(layaMock.requests.length, requestCount);

  // 原条件对没有 length 字段的真值对象仍会发请求。
  client.track({ point: 'object-without-length' }, {});
  assert.equal(layaMock.requests.length, requestCount + 1);
});

test('uploadErrorLog 成功和失败均使用固定 POST 接口', () => {
  const { client, layaMock } = setup();
  const payload = { message: 'boom', stack: 'stack' };

  client.uploadErrorLog(payload);
  assert.equal(
    layaMock.lastRequest.sent.url,
    'https://api01.mihuangame.com/api/v2/sys/oa/errorUpload/add',
  );
  assert.equal(layaMock.lastRequest.sent.method, 'post');
  assert.strictEqual(layaMock.lastRequest.sent.data, payload);
  layaMock.lastRequest.complete({ ok: true });
  assert.equal(consoleOutput.log.some((entry) => entry[0] === '上传错误日志成功'), true);

  client.uploadErrorLog(payload);
  layaMock.lastRequest.fail('upload-error-failed');
  assert.equal(consoleOutput.log.some((entry) => entry[0] === '上传错误日志失败'), true);
});

test('requestBestRankIfDue 严格保持“服务器时间完成后再请求 bestRank”的顺序', () => {
  const { client, layaMock, networkMock } = setup({
    calendarDayDifference: 1,
    isGetLastRankReward: 1234,
  });
  const recorder = callbackRecorder();

  client.requestBestRankIfDue(recorder.callbacks);
  assert.equal(layaMock.requests.length, 1);
  assert.equal(
    layaMock.lastRequest.sent.url,
    'https://api01.mihuangame.com/api/v2/sys/server/time',
  );

  layaMock.lastRequest.complete({ data: 9876 });
  assert.deepEqual(networkMock.state.calendarCalls, [[9876, 1234]]);
  assert.equal(layaMock.requests.length, 2);
  assert.equal(
    layaMock.lastRequest.sent.url,
    'https://api01.mihuangame.com/api/v2/bestRank',
  );

  const rankResponse = { data: [{ rank: 1 }] };
  layaMock.lastRequest.complete(rankResponse);
  assert.deepEqual(recorder.state.successes, [rankResponse]);

  const failureSetup = setup({ calendarDayDifference: 2 });
  const failureRecorder = callbackRecorder();
  failureSetup.client.requestBestRankIfDue(failureRecorder.callbacks);
  failureSetup.layaMock.lastRequest.complete({ data: 2222 });
  failureSetup.layaMock.lastRequest.fail('best-rank-failed');
  assert.deepEqual(failureRecorder.state.failures, ['best-rank-failed']);
});

test('requestBestRankIfDue 未跨日或服务器时间 ERROR 时不请求 bestRank 且不转发回调', () => {
  const notDue = setup({ calendarDayDifference: 0 });
  const notDueRecorder = callbackRecorder();
  notDue.client.requestBestRankIfDue(notDueRecorder.callbacks);
  notDue.layaMock.lastRequest.complete({ data: 5000 });
  assert.equal(notDue.layaMock.requests.length, 1);
  assert.deepEqual(notDueRecorder.state, { successes: [], failures: [] });

  const timeFailure = setup({ calendarDayDifference: 5 });
  const timeFailureRecorder = callbackRecorder();
  timeFailure.client.requestBestRankIfDue(timeFailureRecorder.callbacks);
  timeFailure.layaMock.lastRequest.fail('server-time-failed');
  assert.equal(timeFailure.layaMock.requests.length, 1);
  assert.deepEqual(timeFailureRecorder.state, { successes: [], failures: [] });
});

test('空响应、业务错误响应和非法 JSON 所对应的 ERROR 均按 Laya 事件结果处理', () => {
  const { client, layaMock } = setup();
  const recorder = callbackRecorder();

  client.request('empty-response', null, recorder.callbacks);
  layaMock.lastRequest.complete(null);
  assert.deepEqual(recorder.state.successes, [null]);

  client.request('business-error', null, recorder.callbacks);
  const businessError = { code: 1201, message: 'business error' };
  layaMock.lastRequest.complete(businessError);
  assert.deepEqual(recorder.state.successes, [null, businessError]);

  client.request('invalid-json', null, recorder.callbacks);
  // Laya.HttpRequest 在 JSON.parse 失败时发出 ERROR；mock 直接模拟该事件。
  layaMock.lastRequest.fail('Unexpected token in JSON');
  assert.deepEqual(recorder.state.failures, ['Unexpected token in JSON']);
});

test('公开方法集合与 NET-01 映射表预期一致，没有静默遗漏或重复定义', () => {
  const prototypeMethods = Object.getOwnPropertyNames(HttpClient.prototype)
    .filter((name) => name !== 'constructor')
    .sort();
  const expectedPrototypeMethods = [
    'applyLoginResponse',
    'baseUrl',
    'getUserId',
    'getUserType',
    'initializeChannel',
    'login',
    'reportGameEnd',
    'reportGameStart',
    'request',
    'requestAsPromise',
    'requestBestRankIfDue',
    'requestCountryRanking',
    'requestCountryRankingAlias',
    'requestProvinceRanking',
    'requestServerTime',
    'synchronizeCloudSaveAfterLogin',
    'track',
    'uploadCloudSave',
    'uploadErrorLog',
    'uploadUserInfo',
  ].sort();

  assert.deepEqual(prototypeMethods, expectedPrototypeMethods);
  assert.equal(typeof HttpClient.waitForPromiseWithinTimeout, 'function');
  assert.equal(typeof HttpClient.instance, 'function');
});
