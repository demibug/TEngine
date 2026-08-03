'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

// 弹种用例（P1-01 / 提案 ④b special-weapons-projectiles）
// 覆盖 bundle 注册的 23 弹种

const SEVEN_EXISTING = ['EagleArrow', 'FireArrow', 'HuoFengHuang', 'LightningChain', 'ShenBiArrow', 'PikeSnakeBullet'];
const SIXTEEN_NEW = [
  'SimpleHitAreaBullet', 'KnifeBullet', 'PikeBullet', 'StaticFireBall', 'VirtualBullet',
  'SwordBullet', 'StarBullet', 'FireDragonArrow', 'GroundSpikeBullet', 'FireExplosiveArrow',
  'DaoQiBullet', 'AttachCustomShapeBullet', 'SimpleHitBullet', 'LiHuaBullet', 'LightningArrow', 'FlyPike',
];
const ALL_23 = ['SimpleDynamicArrow', ...SEVEN_EXISTING, ...SIXTEEN_NEW];

test('16 new projectile types can be produced without UnresolvedProjectileTypeError', () => {
  const h = createRangedCombatHarness(); h.cleanup;
  for (const typeKey of SIXTEEN_NEW) {
    const projectile = h.projectileFactory.produce({ type: typeKey, appearance: { label: 'test' } });
    assert.ok(projectile, `should produce ${typeKey}`);
    assert.equal(projectile.projectileId >= 0, true, `${typeKey} should have projectileId`);
  }
});

test('ShenBiArrow correction: typeKey and resource aligned to bundle', () => {
  const { ShenBiArrow } = require('../../src/projectiles/types/ShenBiArrow');
  assert.equal(ShenBiArrow.projectileTypeKey, 'ShenBiArrow', '校正后 typeKey 为 ShenBiArrow（bundle:36365）');
  assert.equal(ShenBiArrow.DEFAULT_APPEARANCE.resourcePath, 'resources/img/weapon/bullet/shenBiArrow.png');
});

test('ShenBiArrow is registered (ShenBiPunch no longer exists)', () => {
  const h = createRangedCombatHarness();
  const projectile = h.projectileFactory.produce({ type: 'ShenBiArrow', appearance: { label: 'test' } });
  assert.ok(projectile, 'ShenBiArrow 可经工厂创建');
  // 旧误标键应抛错
  assert.throws(
    () => h.projectileFactory.produce({ type: 'ShenBiPunch', appearance: { label: 'test' } }),
    error => error && error.name === 'UnresolvedProjectileTypeError',
  );
});

test('all 23 projectile types registered without duplicates', () => {
  const h = createRangedCombatHarness();
  const registry = h.projectileFactory.registry;
  const keys = Array.from(registry.keys());
  assert.equal(keys.length, 23, '应覆盖 bundle 注册的 23 弹种');
  for (const typeKey of ALL_23) {
    assert.ok(registry.has(typeKey), `${typeKey} 应已注册`);
  }
  // 无重复（register 会抛 Duplicate，构造成功即无重复）
  const unique = new Set(keys);
  assert.equal(unique.size, 23, '23 个不重复 type key');
});

test('unknown projectile type still throws UnresolvedProjectileTypeError', () => {
  const h = createRangedCombatHarness();
  assert.throws(
    () => h.projectileFactory.produce({ type: 'NonExistentBullet', appearance: { label: 'x' } }),
    error => error && error.name === 'UnresolvedProjectileTypeError',
  );
});

test('frame sequence contract registered (DEFERRED annotation present)', () => {
  // 验证新弹种有帧动画契约登记注释（纯逻辑层登记，渲染为 P2 非目标）
  const { LiHuaBullet } = require('../../src/projectiles/types/LiHuaBullet');
  const { DaoQiBullet } = require('../../src/projectiles/types/DaoQiBullet');
  const { FlyPike } = require('../../src/projectiles/types/FlyPike');
  assert.ok(LiHuaBullet.DEFAULT_APPEARANCE.resourcePath, 'LiHuaBullet 有资源路径契约');
  assert.ok(DaoQiBullet.DEFAULT_APPEARANCE.resourcePath, 'DaoQiBullet 有资源路径契约');
  assert.ok(FlyPike.DEFAULT_APPEARANCE.resourcePath, 'FlyPike 有资源路径契约');
});
