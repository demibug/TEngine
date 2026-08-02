'use strict';

const DEFAULT_DEVELOPMENT_CONFIG = Object.freeze({
  enabled: false,
  skipPlatformLogin: false,
  skipRemoteShareConfig: false,
  useLocalPlayerData: false,
  directBattle: false,
  forceMatchLaunch: false,
  developmentBattleStartDelayMs: null,
  enemySpawnDurationMs: 1100,
  enemyDeathDurationMs: 100,
  resourceManifest: Object.freeze([]),
});

const DEVELOPMENT_BOOTSTRAP_DEFAULTS = Object.freeze({
  enabled: true,
  skipPlatformLogin: true,
  skipRemoteShareConfig: true,
  useLocalPlayerData: true,
  directBattle: false,
  forceMatchLaunch: false,
  developmentBattleStartDelayMs: 0,
  enemySpawnDurationMs: 0,
  enemyDeathDurationMs: 100,
  resourceManifest: Object.freeze([]),
});

function createDevelopmentConfig(overrides = {}, useBootstrapDefaults = false) {
  const base = useBootstrapDefaults ? DEVELOPMENT_BOOTSTRAP_DEFAULTS : DEFAULT_DEVELOPMENT_CONFIG;
  const config = { ...base, ...overrides };
  if (config.enabled !== true && useBootstrapDefaults) throw new Error('DevelopmentBootstrap requires enabled=true');
  if (config.developmentBattleStartDelayMs !== null &&
      (!Number.isFinite(config.developmentBattleStartDelayMs) || config.developmentBattleStartDelayMs < 0)) {
    throw new RangeError('developmentBattleStartDelayMs must be null or a non-negative number');
  }
  for (const key of ['enemySpawnDurationMs', 'enemyDeathDurationMs']) {
    if (!Number.isFinite(config[key]) || config[key] < 0) throw new RangeError(`${key} must be a non-negative number`);
  }
  config.resourceManifest = Object.freeze(Array.isArray(config.resourceManifest)
    ? config.resourceManifest.slice()
    : []);
  return Object.freeze(config);
}

module.exports = {
  DEFAULT_DEVELOPMENT_CONFIG,
  DEVELOPMENT_BOOTSTRAP_DEFAULTS,
  createDevelopmentConfig,
};
