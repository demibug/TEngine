#!/usr/bin/env node
'use strict';

const { createRangedCombatHarness } = require('../tests/mocks/createRangedCombatHarness');

function runBowSoldierDevelopment() {
  const h = createRangedCombatHarness();
  try {
    const bow = h.spawnBow({ gridX: 0, gridY: 6, level: 1 });
    const mob = h.spawnMobInRange(bow, { offsetX: 160, remainingPathDistance: 10 });
    const timeline = [];
    let acquiredAt = null;
    let attackStartedAt = null;
    let stoppedAt = null;
    let arrowCreatedAt = null;
    let lastAnimationEvents = 0;
    let lastCreations = 0;
    for (let elapsed = 80; elapsed <= 2400; elapsed += 80) {
      h.tick(80, 80);
      if (acquiredAt == null && bow.currentState === 'UnitAttack') acquiredAt = elapsed;
      const events = h.animationDriver.eventLog.slice(lastAnimationEvents);
      lastAnimationEvents = h.animationDriver.eventLog.length;
      for (const event of events) {
        if (event.type === 'play' && event.startMs === 0 && attackStartedAt == null) attackStartedAt = elapsed;
        if (event.type === 'stopped' && stoppedAt == null) stoppedAt = elapsed;
      }
      if (lastCreations !== h.projectileFactory.creationLog.length) {
        arrowCreatedAt = elapsed;
        lastCreations = h.projectileFactory.creationLog.length;
      }
      timeline.push({ elapsedMs: elapsed, state: bow.currentState, activeProjectiles: h.projectileManager.activeCount, mobHealth: mob.health });
      if (mob.health < 6) break;
    }
    return {
      mode: 'DEVELOPMENT_BOW_SOLDIER',
      originalSymbol: 'ok',
      formalKey: '弓',
      factoryIndex: 1,
      config: {
        attackDamage: bow.attackDamage,
        attackRangePx: bow.attackRange,
        attackIntervalMs: bow.attackIntervalSeconds * 1000,
        animationKey: bow.animationKey,
        initialPlaybackRate: bow.initialAnimationPlaybackRate,
        releaseSegment: [0, bow.attackReleaseEventMs],
        releaseDurationMs: (bow.attackReleaseEventMs - 0) / bow.initialAnimationPlaybackRate,
        projectileType: 'SimpleDynamicArrow',
        projectileSpeedScale: bow.projectileSpeedScale,
      },
      position: { grid: { ...bow.gridPosition }, pixel: { x: bow.displayObject.x, y: bow.displayObject.y } },
      target: { id: mob.id, initialHealth: 6, position: { x: mob.visual.x, y: mob.visual.y } },
      timing: { acquiredAt, attackStartedAt, stoppedAt, arrowCreatedAt },
      timeline,
      realNetworkRequests: 0,
      nativePlatformCalls: Number(Boolean(globalThis.wx)) + Number(Boolean(globalThis.tt)),
    };
  } finally { h.cleanup(); }
}

function main() {
  const output = runBowSoldierDevelopment();
  if (output.timing.acquiredAt !== 800 || output.timing.attackStartedAt !== 880 || output.timing.stoppedAt !== 1440 || output.timing.arrowCreatedAt !== 1440 || output.realNetworkRequests !== 0 || output.nativePlatformCalls !== 0) {
    const error = new Error('BowSoldier development simulation did not reach the confirmed deterministic boundary');
    error.output = output;
    throw error;
  }
  process.stdout.write(`${JSON.stringify(output, null, 2)}\n`);
}

if (require.main === module) {
  try { main(); } catch (error) {
    console.error(error && error.stack ? error.stack : error);
    if (error && error.output) console.error(JSON.stringify(error.output, null, 2));
    process.exitCode = 1;
  }
}

module.exports = { runBowSoldierDevelopment, main };
