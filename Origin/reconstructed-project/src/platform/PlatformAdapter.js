'use strict';

class PlatformMethodNotImplementedError extends Error {
  constructor(method) {
    super(`PlatformAdapter method is not implemented: ${method}`);
    this.name = 'PlatformMethodNotImplementedError';
    this.method = method;
  }
}

/** LoadScene 与 BattleFlow 在本轮实际调用到的平台契约。 */
class PlatformAdapter {
  initialize() { throw new PlatformMethodNotImplementedError('initialize'); }
  preload(_onProgress) { throw new PlatformMethodNotImplementedError('preload'); }
  login() { throw new PlatformMethodNotImplementedError('login'); }
  getChannelAppId() { throw new PlatformMethodNotImplementedError('getChannelAppId'); }
  shouldEnterMatchDirectly() { throw new PlatformMethodNotImplementedError('shouldEnterMatchDirectly'); }
  startGame() { throw new PlatformMethodNotImplementedError('startGame'); }
}

module.exports = { PlatformAdapter, PlatformMethodNotImplementedError };
