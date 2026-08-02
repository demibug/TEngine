'use strict';

/**
 * DEVELOPMENT_ONLY
 *
 * LoadScene 只依赖这些显式策略方法，不读取 DevelopmentConfig，避免把
 * 平台绕过逻辑写入正式场景实现。缺失方法不会被 Proxy 或空对象吞掉。
 */
class DevelopmentStartupPolicy {
  constructor(config) {
    if (!config) throw new TypeError('DevelopmentStartupPolicy requires config');
    this.config = config;
  }

  shouldSkipPlatformLogin() {
    return Boolean(this.config.skipPlatformLogin);
  }

  shouldUseLocalPlayerData() {
    return Boolean(this.config.useLocalPlayerData);
  }

  shouldSkipRemoteShareConfig() {
    return Boolean(this.config.skipRemoteShareConfig);
  }

  shouldForceMatchLaunch() {
    return Boolean(this.config.forceMatchLaunch);
  }
}

module.exports = { DevelopmentStartupPolicy };
