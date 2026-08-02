'use strict';

/**
 * DEVELOPMENT_ONLY
 *
 * 不导入 HttpClient，不访问真实服务器。返回值是显式开发样本或跳过标记，
 * 不能当作正式服务端数据。
 */
class DevelopmentNetworkData {
  constructor(options = {}) {
    this.options = {
      failAwaitLogin: false,
      loginResult: true,
      ...options,
    };
    this.calls = [];
    this.channelAppId = undefined;
  }

  init(channelAppId) {
    this.channelAppId = channelAppId;
    this.calls.push(['init', channelAppId]);
  }

  /** LoadScene 原登录等待契约的开发实现。 */
  async waitForLogin(loginPromise) {
    this.calls.push(['login', 'DEVELOPMENT_PLATFORM']);
    if (this.options.failAwaitLogin) {
      throw new Error('DevelopmentNetworkData configured login failure');
    }
    await loginPromise;
    return Boolean(this.options.loginResult);
  }

  // 兼容第二轮/早期第三轮测试命名；行为委托给同一实现。
  async awaitLogin(loginPromise) {
    return this.waitForLogin(loginPromise);
  }

  recordSkippedLogin() {
    this.calls.push(['login', 'SKIPPED_DEVELOPMENT_MODE']);
  }

  synchronizeCloudSaveAfterLogin() {
    this.calls.push(['synchronizeCloudSaveAfterLogin', 'LOCAL_DATA_ONLY']);
    return false;
  }

  recordSkippedCloudSync() {
    this.calls.push(['synchronizeCloudSaveAfterLogin', 'SKIPPED_DEVELOPMENT_MODE']);
    return false;
  }

  finalizeLoadedPlayerData() {
    this.calls.push(['finalizeLoadedPlayerData', 'LOCAL_DATA_ONLY']);
    return false;
  }

  async loadRemoteShareConfig() {
    this.calls.push(['loadRemoteShareConfig', 'DEVELOPMENT_SAMPLE']);
    return Object.freeze({ source: 'DEVELOPMENT_SAMPLE' });
  }

  recordSkippedRemoteShareConfig() {
    this.calls.push(['loadRemoteShareConfig', 'SKIPPED_DEVELOPMENT_MODE']);
  }

  reportGameStart() {
    this.calls.push(['reportGameStart', 'SKIPPED_DEVELOPMENT_MODE']);
  }

  reportGameEnd(isWin) {
    this.calls.push(['reportGameEnd', Boolean(isWin), 'SKIPPED_DEVELOPMENT_MODE']);
  }

  assertNoRealNetworkCalls() {
    return true;
  }
}

module.exports = { DevelopmentNetworkData };
