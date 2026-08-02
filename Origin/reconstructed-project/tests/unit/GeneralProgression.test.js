'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { EventBus, GameEvents } = require('../../src/core/EventBus');
const { BattleManager } = require('../../src/battle/BattleManager');
const { GeneralUnit } = require('../../src/generals/GeneralUnit');
const { UnitRegistry } = require('../../src/units/UnitRegistry');

test('GeneralUnit accumulates experience, upgrades and refreshes level stats', () => {
  const general = new GeneralUnit({ name: '赵云', experienceThresholds: [0, 5, 10, 20, 35] });

  assert.equal(general.addExperience(4).level, 1);
  assert.equal(general.experience, 4);
  assert.equal(general.getExperienceToNextLevel(), 1);

  const upgrade = general.gainExperience(6);
  assert.equal(upgrade.level, 3);
  assert.equal(upgrade.levelsGained, 2);
  assert.equal(general.stats.attackSpeedMultiplier, 1.56);
  assert.equal(general.stats.damageMultiplier, 2.1);
  assert.equal(general.getExperienceToNextLevel(), 10);

  general.setExperience(100);
  assert.equal(general.level, 5);
  assert.equal(general.stats.damageMultiplier, 3.276);
  assert.equal(general.getExperienceToNextLevel(), null);
});

test('BattleManager distributes enemy kill experience to general contributors', () => {
  const general = new GeneralUnit({ id: 101, name: '赵云', experienceThresholds: [0, 2, 4, 6, 8] });
  const registry = new UnitRegistry();
  registry.generals.set(general.id, general);
  const eventBus = new EventBus();
  new BattleManager().configure({ eventBus, unitManager: registry });

  eventBus.event(GameEvents.ENEMY_KILLED_BY, general.id, [general.id], 2);

  assert.equal(general.experience, 2);
  assert.equal(general.level, 2);
  assert.equal(general.stats.attackSpeedMultiplier, 1.3);
});
