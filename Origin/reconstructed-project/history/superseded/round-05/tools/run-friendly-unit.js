#!/usr/bin/env node
'use strict';

const { createFriendlyCombatHarness } = require('../tests/mocks/createFriendlyCombatHarness');

const harness = createFriendlyCombatHarness({ deathDurationMs: 0 });
try {
  const unit = harness.spawnKnife({ side: true, gridX: 1, gridY: 6, level: 1 });
  process.stdout.write(`${JSON.stringify({
    mode: 'DEVELOPMENT_FRIENDLY_UNIT',
    unit: {
      id: unit.id,
      formalKey: unit.typeKey,
      className: unit.constructor.name,
      level: unit.level,
      side: unit.side,
      position: { x: unit.displayObject.x, y: unit.displayObject.y },
      attackDamage: unit.attackDamage,
      attackRange: unit.attackRange,
      attackIntervalScale: unit.attackIntervalScale,
      state: unit.currentState,
      active: unit.isActive,
    },
    registryCount: harness.unitRegistry.soldierCount,
    factoryKey: harness.unitFactory.createLog[0].key,
    networkRequests: 0,
    nativePlatformCalls: 0,
  }, null, 2)}\n`);
} finally {
  harness.cleanup();
}
