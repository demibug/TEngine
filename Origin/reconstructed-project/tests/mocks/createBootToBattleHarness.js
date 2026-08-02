'use strict';

const path = require('node:path');
const { createLayaSceneMock } = require('./LayaSceneMock');

function purgeSceneModules() {
  const marker = `${path.sep}src${path.sep}scenes${path.sep}`;
  for (const id of Object.keys(require.cache)) if (id.includes(marker)) delete require.cache[id];
}

async function flushMicrotasks() {
  await Promise.resolve();
  await Promise.resolve();
  await new Promise(resolve => setImmediate(resolve));
}

async function advanceTimer(Laya, totalMs, stepMs = 16) {
  let remaining = totalMs;
  while (remaining > 0) {
    const step = Math.min(stepMs, remaining);
    Laya.timer.tick(step);
    remaining -= step;
    await flushMicrotasks();
  }
}

async function createBootToBattleHarness(options = {}) {
  purgeSceneModules();
  delete globalThis.wx;
  delete globalThis.tt;
  const { DevelopmentBootstrap } = require('../../src/bootstrap/DevelopmentBootstrap');
  DevelopmentBootstrap.resetSingletonsForTests();
  const Laya = createLayaSceneMock();
  globalThis.Laya = Laya;
  const windowRef = options.windowRef || {
    splashHidden: 0,
    hideSplashScreen() { this.splashHidden += 1; },
  };
  const warnings = [];
  const logs = [];
  const logger = options.logger || {
    warn: (...args) => warnings.push(args),
    log: (...args) => logs.push(args),
    error: (...args) => logs.push(['error', ...args]),
  };
  const bootstrap = new DevelopmentBootstrap({
    Laya,
    windowRef,
    config: options.config || {},
    platformOptions: options.platformOptions || {},
    networkOptions: options.networkOptions || {},
    resourceLoaderOptions: options.resourceLoaderOptions || {},
    random: options.random || (() => 0),
    logger,
  });
  const context = await bootstrap.start();
  await flushMicrotasks();
  return { Laya, context, bootstrap, windowRef, warnings, logs, flushMicrotasks };
}

const createDevelopmentHarness = createBootToBattleHarness;
module.exports = { createBootToBattleHarness, createDevelopmentHarness, advanceTimer, flushMicrotasks };
