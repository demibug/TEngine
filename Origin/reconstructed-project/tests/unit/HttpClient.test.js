'use strict';

const assert = require('node:assert/strict');
const {
  test,
  beforeEach,
  afterEach,
} = require('node:test');

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
  const layaMock = createLayaHttpMock();
  const networkMock = createNetworkMock(layaMock.Laya, options);
  configureHttpClientDependencies(networkMock.dependencies);
  const client = HttpClient.instance();
  return {
    client,
    layaMock,
    networkMock,
  };
}

test('singleton、默认字段、静态常量和调试地址保持原始语义', () => {
  const { client } = setup();

  assert.strictEqual(client, HttpClient.instance());
  assert.equal(client.loginCloudSaveRaw, null);
  assert.equal(client.productionBaseUrl, 'https://api01.mihuangame.com/api/v2/');
  assert.equal(client.baseUrl, 'https://api01.mihuangame.com/api/v2/');
  assert.equal(client.useDebugServer, false);
  assert.equal(client.authentication, '');
  assert.equal(client.getUserId(), 0);
  assert.equal(client.getUserType(), 0);
  assert.equal(client.rankingType, 3);
  assert.equal(client.channelAppId, 0);

  client.initializeChannel(31415);
  assert.equal(client.channelAppId, 31415);
  client.initializeChannel(undefined);
  assert.equal(client.channelAppId, undefined);

  client.useDebugServer = true;
  assert.equal(client.baseUrl, 'https://debug.mihuangame.com/api/v2/');

  assert.equal(HttpClient.DEFAULT_TIMEOUT_MS, 5000);
  assert.equal(HttpClient.CLOUD_SAVE_UPLOAD_COUNT_KEY, 'playGameCount');
  assert.equal(HttpClient.LEGACY_CODE_1201, 1201);
  assert.equal(HttpClient.LEGACY_CODE_1203, 1203);

  HttpClient.resetInstanceForTests();
  assert.notStrictEqual(client, HttpClient.instance());
});

test('request 按原顺序设置超时、发送，再注册 COMPLETE/ERROR 监听', () => {
  const { client, layaMock } = setup();
  const successes = [];
  const failures = [];

  client.authentication = 'auth-token';
  client.request('/example', { value: 7 }, {
    success: (response) => successes.push(response),
    fail: (error) => failures.push(error),
  }, 'post', 4321);

  assert.deepEqual(
    layaMock.operations.map((operation) => operation.type),
    [
      'construct-request',
      'set-timeout',
      'send',
      'once-complete',
      'once-error',
    ],
  );

  const request = layaMock.lastRequest;
  assert.equal(request.http.timeout, 4321);
  assert.deepEqual(request.sent, {
    url: 'https://api01.mihuangame.com/api/v2//example',
    data: { value: 7 },
    method: 'post',
    responseType: 'json',
    headers: [
      'Content-Type',
      'application/json',
      'authentication',
      'auth-token',
    ],
  });

  const response = { code: 0, data: { ok: true } };
  assert.equal(request.complete(response), true);
  assert.deepEqual(successes, [response]);
  assert.deepEqual(failures, []);
});

test('request ERROR 只调用 fail；send 同步异常发生时不会注册监听', () => {
  const { client, layaMock } = setup();
  const successes = [];
  const failures = [];

  client.request('error-case', null, {
    success: (response) => successes.push(response),
    fail: (error) => failures.push(error),
  });

  layaMock.lastRequest.fail('offline');
  assert.deepEqual(successes, []);
  assert.deepEqual(failures, ['offline']);

  layaMock.resetOperations();
  layaMock.throwOnNextSend(new Error('send exploded'));
  assert.throws(
    () => client.request('throws', null, {}),
    /send exploded/,
  );
  assert.deepEqual(
    layaMock.operations.map((operation) => operation.type),
    ['construct-request', 'set-timeout', 'send'],
  );
});

test('requestAsPromise 分别映射 COMPLETE、ERROR 和同步 send 异常', async () => {
  const { client, layaMock } = setup();

  const resolvedPromise = client.requestAsPromise('promise-ok', { a: 1 }, 'post', 88);
  const resolvedRequest = layaMock.lastRequest;
  resolvedRequest.complete({ data: 10 });
  assert.deepEqual(await resolvedPromise, { data: 10 });

  const rejectedPromise = client.requestAsPromise('promise-fail', null);
  const rejectedRequest = layaMock.lastRequest;
  rejectedRequest.fail('network-error');
  await assert.rejects(rejectedPromise, (error) => error === 'network-error');

  layaMock.throwOnNextSend(new Error('sync-send-error'));
  await assert.rejects(
    client.requestAsPromise('promise-throws', null),
    /sync-send-error/,
  );
});

test('waitForPromiseWithinTimeout 对 fulfilled/rejected/timeout 返回布尔值且不取消源 Promise', async () => {
  const { layaMock } = setup();

  let resolveSource;
  const fulfilledSource = new Promise((resolve) => {
    resolveSource = resolve;
  });
  const fulfilledResult = HttpClient.waitForPromiseWithinTimeout(fulfilledSource, 50);
  assert.equal(layaMock.Laya.timer.pendingCount(), 1);
  resolveSource('ignored-value');
  assert.equal(await fulfilledResult, true);
  assert.equal(layaMock.Laya.timer.pendingCount(), 0);

  const rejectedResult = HttpClient.waitForPromiseWithinTimeout(
    Promise.reject(new Error('rejected')),
    60,
  );
  assert.equal(await rejectedResult, false);

  let lateResolve;
  const pendingSource = new Promise((resolve) => {
    lateResolve = resolve;
  });
  const timeoutResult = HttpClient.waitForPromiseWithinTimeout(pendingSource, 70);
  assert.equal(layaMock.Laya.timer.runNext(), true);
  assert.equal(await timeoutResult, false);
  lateResolve('late');
  await Promise.resolve();
  assert.equal(layaMock.Laya.timer.pendingCount(), 0);
});

test('login 成功时应用响应并保持 requestPayload 原样', async () => {
  const { client, layaMock, networkMock } = setup();
  const callbackState = { success: [], fail: [] };
  const payload = { channelAppId: 9, js_code: 'wx-code' };
  const response = {
    code: 999,
    data: {
      authentication: 'new-auth',
      userId: 77,
      userType: 1,
      userData: '{"wn":3}',
      attach: { province: '广东' },
    },
  };

  const loginPromise = client.login('wx-code', payload, {
    success: (value) => callbackState.success.push(value),
    fail: (error) => callbackState.fail.push(error),
  }, 3456);

  assert.deepEqual(layaMock.lastRequest.sent, {
    url: 'https://api01.mihuangame.com/api/v2/sys/user/login',
    data: payload,
    method: 'post',
    responseType: 'json',
    headers: [
      'Content-Type',
      'application/json',
      'authentication',
      '',
    ],
  });
  assert.equal(layaMock.lastRequest.http.timeout, 3456);

  layaMock.lastRequest.complete(response);
  assert.strictEqual(await loginPromise, response);
  assert.equal(client.authentication, 'new-auth');
  assert.equal(client.getUserId(), 77);
  assert.equal(client.getUserType(), 1);
  assert.equal(client.loginCloudSaveRaw, '{"wn":3}');
  assert.equal(networkMock.playerData.province, '广东');
  assert.deepEqual(networkMock.state.emittedUserIds, [77]);
  assert.deepEqual(callbackState.success, [response]);
  assert.deepEqual(callbackState.fail, []);
});

test('login 网络失败返回 null；空 code 同步触发 fail 且不创建请求', async () => {
  const { client, layaMock } = setup();
  const failures = [];

  const failedLogin = client.login('code', { code: 'code' }, {
    fail: (error) => failures.push(error),
  });
  layaMock.lastRequest.fail('timeout');
  assert.equal(await failedLogin, null);
  assert.deepEqual(failures, ['timeout']);
  assert.equal(consoleOutput.warn.length, 1);

  const requestCount = layaMock.requests.length;
  let emptyCodeFailure;
  const emptyResultPromise = client.login('', { code: '' }, {
    fail: (error) => {
      emptyCodeFailure = error;
    },
  });
  assert.equal(emptyCodeFailure, 'login code is empty');
  assert.equal(await emptyResultPromise, null);
  assert.equal(layaMock.requests.length, requestCount);
});

test('applyLoginResponse 对缺失字段使用原始默认值并保留旧 authentication', () => {
  const { client, networkMock } = setup();
  client.authentication = 'existing-auth';

  client.applyLoginResponse({ data: {} });

  assert.equal(client.authentication, 'existing-auth');
  assert.equal(client.getUserId(), 0);
  assert.equal(client.getUserType(), 0);
  assert.equal(client.loginCloudSaveRaw, undefined);
  assert.equal(networkMock.playerData.province, '未知');
  assert.deepEqual(networkMock.state.emittedUserIds, []);
});

test('synchronizeCloudSaveAfterLogin 覆盖未登录、无云存档、采用云端和回传本地四条分支', () => {
  const { client, layaMock, networkMock } = setup();

  client.synchronizeCloudSaveAfterLogin();
  assert.deepEqual(networkMock.state.parseCalls, []);
  assert.equal(layaMock.requests.length, 0);

  client.userId = 1;
  client.loginCloudSaveRaw = 'raw-cloud';
  networkMock.state.parsedCloudSave = null;
  client.synchronizeCloudSaveAfterLogin();
  assert.deepEqual(networkMock.state.parseCalls, ['raw-cloud']);
  assert.equal(networkMock.state.resolveCloudCalls.length, 0);

  networkMock.state.parsedCloudSave = { wn: 10, ls: 2 };
  networkMock.state.cloudSaveResolution = true;
  client.synchronizeCloudSaveAfterLogin();
  assert.deepEqual(networkMock.state.resolveCloudCalls, [{ wn: 10, ls: 2 }]);
  assert.equal(networkMock.state.cloudAppliedCalls, 1);
  assert.equal(layaMock.requests.length, 0);

  networkMock.state.cloudSaveResolution = false;
  client.synchronizeCloudSaveAfterLogin();
  assert.equal(networkMock.state.cloudPushCalls, 1);
  assert.equal(layaMock.requests.length, 1);
  assert.equal(layaMock.lastRequest.sent.url, 'https://api01.mihuangame.com/api/v2/sys/user/data');
  assert.equal(layaMock.lastRequest.sent.method, 'post');
  assert.deepEqual(layaMock.lastRequest.sent.data, networkMock.state.cloudPayload);
  assert.equal(layaMock.operations.some((operation) => operation.type === 'storage-get'), false);
});

test('uploadCloudSave 保留首局/每五局节流、force 绕过计数和非法计数行为', () => {
  const { client, layaMock, networkMock } = setup();

  client.uploadCloudSave();
  assert.equal(layaMock.requests.length, 0);
  assert.equal(layaMock.storage.size, 0);

  client.userId = 8;
  client.uploadCloudSave();
  assert.equal(layaMock.storage.get('playGameCount'), '1');
  assert.equal(layaMock.requests.length, 1);
  assert.deepEqual(layaMock.lastRequest.sent.data, networkMock.state.cloudPayload);
  layaMock.lastRequest.complete({ ok: true });

  client.uploadCloudSave();
  assert.equal(layaMock.storage.get('playGameCount'), '2');
  assert.equal(layaMock.requests.length, 1);

  layaMock.storage.set('playGameCount', '4');
  client.uploadCloudSave();
  assert.equal(layaMock.storage.get('playGameCount'), '5');
  assert.equal(layaMock.requests.length, 2);
  layaMock.lastRequest.fail('upload-failed');

  layaMock.resetOperations();
  client.uploadCloudSave(true);
  assert.equal(layaMock.requests.length, 3);
  assert.equal(layaMock.operations.some((operation) => operation.type === 'storage-get'), false);
  assert.equal(layaMock.operations.some((operation) => operation.type === 'storage-set'), false);

  layaMock.storage.set('playGameCount', 'not-a-number');
  client.uploadCloudSave();
  assert.equal(layaMock.storage.get('playGameCount'), 'NaN');
  assert.equal(layaMock.requests.length, 3);
});

test('configureHttpClientDependencies 拒绝未知或非函数绑定', () => {
  setup();
  assert.throws(
    () => configureHttpClientDependencies(null),
    /must be an object/,
  );
  assert.throws(
    () => configureHttpClientDependencies({ unknown: () => {} }),
    /Unknown HttpClient dependency/,
  );
  assert.throws(
    () => configureHttpClientDependencies({ getPlayerData: {} }),
    /must be a function/,
  );
});
