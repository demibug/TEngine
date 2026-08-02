'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createLayaSceneMock } = require('../mocks/LayaSceneMock');
const { FixedUpdateManager } = require('../../src/core/FixedUpdateManager');
const { SceneManager } = require('../../src/core/SceneManager');

test('FixedUpdateManager preserves 500ms clamp and 80ms substeps', () => {
  FixedUpdateManager.resetInstanceForTests();
  const Laya = createLayaSceneMock();
  const fixed = FixedUpdateManager.instance().configure({ Laya });
  const steps = [];
  fixed.register('probe', null, delta => steps.push(delta));
  fixed.init();
  Laya.timer.tick(1000);
  assert.deepEqual(steps, [80,80,80,80,80,80,20]);
  assert.equal(fixed.delta, 500);
  assert.equal(fixed.elapsed, 500);
});

test('SceneManager original openScene method remains non-Promise while observation helper resolves', async () => {
  SceneManager.resetInstanceForTests();
  const Laya = createLayaSceneMock();
  class TestScene extends Laya.Scene {}
  Laya.Scene.registerFactory('scene/TestScene.ls', () => new TestScene());
  const manager = SceneManager.instance().configure({ Laya });
  manager.init();
  const returnValue = manager.openScene('TestScene');
  assert.equal(returnValue, undefined);
  const scene = await manager.whenLastOpenCompletes();
  assert.equal(scene.url, 'scene/TestScene.ls');
});
