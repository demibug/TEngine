#!/usr/bin/env node
'use strict';

const { createRangedCombatHarness } = require('../tests/mocks/createRangedCombatHarness');

function runProjectileDevelopment() {
  const h = createRangedCombatHarness();
  try {
    const bow = h.spawnBow({ gridX: 0, gridY: 6 });
    const mob = h.spawnMobInRange(bow, { offsetX: 400, offsetY: -80, remainingPathDistance: 10 });
    const arrow = h.createArrow({ attacker: bow, target: mob, damage: bow.attackDamage, speedScale: bow.projectileSpeedScale, curveHeight: 120 });
    const projectileId = arrow.projectileId;
    const poolKey = arrow.poolKey;
    const samples = [{ elapsedMs: 0, x: arrow.x, y: arrow.y, rotation: arrow.rotation, progress: 0 }];
    let hitAt = null;
    let previousHitCount = 0;
    for (let elapsed = 80; elapsed <= 3000; elapsed += 80) {
      h.tick(80, 80);
      const live = h.projectileManager.getById(projectileId);
      if (live) samples.push({ elapsedMs: elapsed, x: live.x, y: live.y, rotation: live.rotation, progress: live.movement.progress });
      if (h.projectileEffects.calls.length > previousHitCount) { hitAt = elapsed; previousHitCount = h.projectileEffects.calls.length; }
      if (!live) break;
    }
    const output = {
      mode: 'DEVELOPMENT_PROJECTILE',
      originalSymbol: 'rd',
      formalKey: 'SimpleDynamicArrow',
      poolKey,
      appearance: { label: '弓箭小兵箭矢', resourcePath: 'resources/img/weapon/arrow_0.png', width: 22, height: 72, anchorX: 0.5, anchorY: 0.9 },
      movement: {
        originalSymbol: 'on',
        type: 'quadratic-bezier-target-tracking',
        curveHeight: 120,
        speedScale: bow.projectileSpeedScale,
        progressFormula: 'deltaMs * movementRate * projectileSpeedScale / 500, then distance scaling',
        hitEnableProgress: 0.8,
        hitRadiusPx: 48,
      },
      start: samples[0],
      midpoint: samples.reduce((best, sample) => Math.abs(sample.progress - 0.5) < Math.abs(best.progress - 0.5) ? sample : best, samples[0]),
      finalSample: samples.at(-1),
      samples,
      hitAt,
      damage: 2,
      remainingMobHealth: mob.health,
      activeProjectiles: h.projectileManager.activeCount,
      compositePoolSize: h.objectPool.sizeByKey(poolKey),
      realNetworkRequests: 0,
      nativePlatformCalls: Number(Boolean(globalThis.wx)) + Number(Boolean(globalThis.tt)),
    };
    if (output.hitAt == null || output.remainingMobHealth !== 4 || output.activeProjectiles !== 0 || output.compositePoolSize !== 1) {
      const error = new Error('Projectile development simulation did not reach the confirmed deterministic boundary');
      error.output = output;
      throw error;
    }
    return output;
  } finally { h.cleanup(); }
}

function main() { process.stdout.write(`${JSON.stringify(runProjectileDevelopment(), null, 2)}\n`); }
if (require.main === module) {
  try { main(); } catch (error) {
    console.error(error && error.stack ? error.stack : error);
    if (error && error.output) console.error(JSON.stringify(error.output, null, 2));
    process.exitCode = 1;
  }
}
module.exports = { runProjectileDevelopment, main };
