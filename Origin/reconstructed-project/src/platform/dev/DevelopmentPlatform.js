'use strict';

const { PlatformAdapter } = require('../PlatformAdapter');

/** DEVELOPMENT_ONLY：不调用 wx.*、tt.*、广告、分享或云存储。 */
class DevelopmentPlatform extends PlatformAdapter {
  constructor(options = {}) {
    super();
    this.options = {
      failInitialize: false,
      failPreload: false,
      failLogin: false,
      directMatch: false,
      channelAppId: 'development-local',
      loginResult: Object.freeze({ source: 'DEVELOPMENT_SAMPLE', code: 'LOCAL_DEV_CODE' }),
      ...options,
    };
    this.calls = [];
  }

  initialize() {
    this.calls.push(['initialize']);
    if (this.options.failInitialize) throw new Error('DevelopmentPlatform configured initialize failure');
  }

  preload(onProgress) {
    this.calls.push(['preload']);
    if (this.options.failPreload) return Promise.reject(new Error('DevelopmentPlatform configured preload failure'));
    if (onProgress) { onProgress(0, 1); onProgress(1, 1); }
    return Promise.resolve();
  }

  login() {
    this.calls.push(['login']);
    if (this.options.failLogin) return Promise.reject(new Error('DevelopmentPlatform configured login failure'));
    return Promise.resolve(this.options.loginResult);
  }

  getChannelAppId() {
    this.calls.push(['getChannelAppId']);
    return this.options.channelAppId;
  }

  shouldEnterMatchDirectly() {
    this.calls.push(['shouldEnterMatchDirectly']);
    return Boolean(this.options.directMatch);
  }

  startGame() {
    this.calls.push(['startGame']);
  }

  assertNoNativePlatformCalls() { return true; }
}

module.exports = { DevelopmentPlatform };
