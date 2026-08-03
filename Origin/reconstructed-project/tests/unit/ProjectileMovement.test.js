'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');

// 移动策略用例（P1-01 / 提案 ④b special-weapons-projectiles）

// 最小伪 projectile：模拟 ProjectileBase 的 renderNode 与 movementConfig 字段
function makeFakeProjectile(opts = {}) {
  return {
    renderNode: { x: opts.x || 0, y: opts.y || 0 },
    movementConfig: opts.movementConfig || null,
    attacker: opts.attacker || null,
    enemyManager: opts.enemyManager || null,
  };
}

const STRATEGIES = [
  ['DefaultBulletMovement', require('../../src/projectiles/movement/DefaultBulletMovement').DefaultBulletMovement],
  ['TargetObjectInstantaneous', require('../../src/projectiles/movement/TargetObjectInstantaneous').TargetObjectInstantaneous],
  ['TargetDirectionWaveMovement', require('../../src/projectiles/movement/TargetDirectionWaveMovement').TargetDirectionWaveMovement],
  ['TargetDirectionLineMovement', require('../../src/projectiles/movement/TargetDirectionLineMovement').TargetDirectionLineMovement],
  ['ForwardMovement', require('../../src/projectiles/movement/ForwardMovement').ForwardMovement],
  ['TargetPositionBezierMovement', require('../../src/projectiles/movement/TargetPositionBezierMovement').TargetPositionBezierMovement],
  ['TargetEnemyMovement', require('../../src/projectiles/movement/TargetEnemyMovement').TargetEnemyMovement],
];

test('7 new movement strategies implement attach/onFire/update/recover and do not read p.config', () => {
  for (const [name, StrategyClass] of STRATEGIES) {
    const strategy = new StrategyClass();
    const projectile = makeFakeProjectile({ x: 10, y: 20, movementConfig: {} });
    assert.equal(typeof strategy.attach, 'function', `${name} has attach`);
    assert.equal(typeof strategy.onFire, 'function', `${name} has onFire`);
    assert.equal(typeof strategy.update, 'function', `${name} has update`);
    assert.equal(typeof strategy.recover, 'function', `${name} has recover`);
    // attach 不抛错且不读 p.config（p.config 不存在）
    strategy.attach(projectile);
    strategy.onFire();
    assert.doesNotThrow(() => strategy.update(16, 1), `${name} update should not throw`);
    strategy.recover();
    assert.equal(strategy.projectile, null, `${name} recover clears projectile`);
  }
});

test('DefaultBulletMovement moves projectile by direction', () => {
  const { DefaultBulletMovement } = require('../../src/projectiles/movement/DefaultBulletMovement');
  const s = new DefaultBulletMovement();
  const p = makeFakeProjectile({ x: 0, y: 100, movementConfig: { direction: { x: 0, y: -1 } } });
  s.attach(p);
  s.update(1000, 1); // 1s
  assert.equal(p.renderNode.y, 0, 'should move up 100 units in 1s');
});

test('TargetObjectInstantaneous snaps to target on first update', () => {
  const { TargetObjectInstantaneous } = require('../../src/projectiles/movement/TargetObjectInstantaneous');
  const s = new TargetObjectInstantaneous();
  const target = { x: 200, y: 300 };
  const p = makeFakeProjectile({ movementConfig: { target } });
  s.attach(p);
  s.update(16, 1);
  assert.equal(p.renderNode.x, 200);
  assert.equal(p.renderNode.y, 300);
});

test('TargetPositionBezierMovement interpolates along bezier curve', () => {
  const { TargetPositionBezierMovement } = require('../../src/projectiles/movement/TargetPositionBezierMovement');
  const s = new TargetPositionBezierMovement();
  const p = makeFakeProjectile({ x: 0, y: 0, movementConfig: { target: { x: 100, y: 0 }, control: { x: 50, y: 50 } } });
  s.attach(p);
  s.update(500, 1); // progress 0.5
  // t=0.5: 0.25*0 + 0.5*50 + 0.25*100 = 50; y: 0.25*0 + 0.5*50 + 0.25*0 = 25
  assert.equal(p.renderNode.x, 50);
  assert.equal(p.renderNode.y, 25);
});

test('TargetEnemyMovement chases enemy position', () => {
  const { TargetEnemyMovement } = require('../../src/projectiles/movement/TargetEnemyMovement');
  const s = new TargetEnemyMovement();
  const enemy = { id: 5, x: 100, y: 0 };
  const em = { getEnemy: id => id === 5 ? enemy : null };
  const p = makeFakeProjectile({ x: 0, y: 0, movementConfig: { enemyManager: em, targetId: 5, speed: 100 } });
  s.attach(p);
  s.update(1000, 1); // 1s at speed 100 → move 100 toward (100,0)
  assert.equal(p.renderNode.x, 100);
});

// ---- 3 占位骨架校正后接入 resetData（不读 p.config）----

test('corrected BezierMovement reads movementConfig not p.config', () => {
  const { BezierMovement } = require('../../src/projectiles/movement/BezierMovement');
  const s = new BezierMovement();
  const p = makeFakeProjectile({ x: 0, y: 0, movementConfig: { p0: { x: 0, y: 0 }, p1: { x: 50, y: 100 }, p2: { x: 100, y: 0 } } });
  s.attach(p); // 不抛错（p.config 不存在，改读 movementConfig）
  s.update(500, 1); // progress 0.5
  assert.equal(p.renderNode.x, 50);
});

test('corrected FixedTargetMovement reads movementConfig.target', () => {
  const { FixedTargetMovement } = require('../../src/projectiles/movement/FixedTargetMovement');
  const s = new FixedTargetMovement();
  const p = makeFakeProjectile({ x: 0, y: 0, movementConfig: { target: { x: 100, y: 100 } } });
  s.attach(p);
  s.update(500, 1);
  assert.equal(p.renderNode.x, 50);
  assert.equal(p.renderNode.y, 50);
});

test('corrected LineMovement reads movementConfig dx/dy', () => {
  const { LineMovement } = require('../../src/projectiles/movement/LineMovement');
  const s = new LineMovement();
  const p = makeFakeProjectile({ movementConfig: { dx: 10, dy: 0 } });
  s.attach(p);
  s.update(1000, 1); // t=(1000/1000)*1=1, x += dx*1 = 10
  assert.equal(p.renderNode.x, 10);
});
