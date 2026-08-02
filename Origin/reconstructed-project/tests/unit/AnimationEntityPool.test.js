'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createLayaSceneMock } = require('../mocks/LayaSceneMock');
const { AnimationEntityPool } = require('../../src/core/AnimationEntityPool');

function createSkeletonNode(Laya, resourcePath, animationId) {
  const node = new Laya.Sprite();
  node.resourcePath = resourcePath;
  node.animationId = animationId;
  node.fastMode = null;
  node.resetCount = 0;
  node.setIsFastMode = value => { node.fastMode = Boolean(value); };
  node.Td = () => { node.resetCount += 1; node.removeSelf(); };
  return node;
}

test('AnimationEntityPool preserves aDou pool key, resource path, fast-mode and recovery order', () => {
  AnimationEntityPool.resetInstanceForTests();
  const Laya = createLayaSceneMock();
  const pool = AnimationEntityPool.instance().configure({
    laya: Laya,
    isFrameAnimation: () => false,
    createFrameAnimation: () => { throw new Error('unexpected frame branch'); },
    createSkeletonAnimation: (path, id) => createSkeletonNode(Laya, path, id),
  });

  const first = pool.create('aDou');
  assert.equal(first.resourcePath, 'resources/anim/aDou/skeleton.json');
  assert.equal(first.fastMode, false);
  assert.equal(pool.createLog[0].poolKey, 'sk_aDou');

  pool.recover(first, 'aDou');
  assert.equal(first.resetCount, 1);
  assert.equal(pool.recoverLog[0].poolKey, 'sk_aDou');

  const reused = pool.create('aDou');
  assert.equal(reused, first);
  assert.equal(reused.fastMode, false);
});
