'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('BowSoldier creates no arrow before STOPPED and creates one after the 650/1.25 segment', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 160, remainingPathDistance: 10 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  bow.attack();
  h.tick(480, 80);
  assert.equal(h.projectileFactory.creationLog.length, 0);
  assert.equal(h.animationDriver.activeCount, 1);
  h.tick(80, 80);
  assert.equal(h.projectileFactory.creationLog.length, 1);
  assert.equal(h.projectileManager.activeCount, 1);
  assert.equal(h.attackEffectManager.activeCount, 1);
  assert.equal(h.unitAudio.calls.at(-1), 'bow_attack');
  assert.equal(bow.animation.listenerCount(h.Laya.Event.STOPPED), 0);
  assert.equal(h.projectileManager.activeProjectiles[0].hitStrategy.targetIds[0], mob.id);
});

test('development animation timing pauses with GameLoop and does not emit STOPPED early', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  h.spawnMobInRange(bow, { offsetX: 160 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  h.gameLoop.unregister('BattleMgr');
  bow.attack();
  h.gameLoop.paused = true;
  h.tick(1000, 100);
  assert.equal(h.projectileFactory.creationLog.length, 0);
  h.gameLoop.paused = false;
  h.tick(560, 80);
  assert.equal(h.projectileFactory.creationLog.length, 1);
});

test('STOPPED listener is removed before launch so duplicate STOPPED cannot create a second arrow', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  h.spawnMobInRange(bow, { offsetX: 160 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  bow.attack();
  h.tick(560, 80);
  assert.equal(h.projectileFactory.creationLog.length, 1);
  bow.animation.event(h.Laya.Event.STOPPED);
  bow.animation.event(h.Laya.Event.STOPPED);
  assert.equal(h.projectileFactory.creationLog.length, 1);
});
