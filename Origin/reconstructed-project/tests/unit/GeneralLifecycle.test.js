'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { GeneralPart, GeneralPartState } = require('../../src/generals/GeneralPart');
const { GeneralFactory } = require('../../src/generals/GeneralFactory');
const { UnitRegistry } = require('../../src/units/UnitRegistry');

test('UnitRegistry recycles a general, unbinds both parts and releases the weapon', () => {
  const registry = new UnitRegistry();
  registry.generalFactory = new GeneralFactory({ nextId: 100 });
  const first = new GeneralPart({ id: 1, word: '赵', side: true });
  const second = new GeneralPart({ id: 2, word: '云', side: true });
  registry.secondaryUnits.set(first.id, first);
  registry.secondaryUnits.set(second.id, second);
  let weaponGameOverCount = 0;
  const weapon = { attach() {}, detach() {}, gameOver() { weaponGameOverCount += 1; } };

  const general = registry.mergeGeneralParts([first.id, second.id], { weapon });
  assert.equal(first.ownerId, general.id);
  assert.equal(second.state, GeneralPartState.MERGE);
  assert.throws(() => registry.mergeGeneralParts([first.id, second.id]), /already assigned/);

  assert.equal(registry.removeUnit(general.id), true);
  assert.equal(general.isDead, true);
  assert.equal(general.inPool, true);
  assert.equal(weaponGameOverCount, 1);
  assert.equal(first.ownerId, -1);
  assert.equal(first.state, GeneralPartState.NONE);
  assert.equal(first.active, true);
  assert.equal(second.ownerId, -1);
  assert.equal(registry.generals.has(general.id), false);
  assert.equal(registry.generalComponents.has(general.id), false);
  assert.equal(registry.removeGeneral(general.id), false);
});

test('GeneralUnit death stops activity before registry recycling', () => {
  const registry = new UnitRegistry();
  registry.generalFactory = new GeneralFactory({ nextId: 200 });
  const first = new GeneralPart({ id: 11, word: '张', side: true });
  const second = new GeneralPart({ id: 12, word: '飞', side: true });
  registry.secondaryUnits.set(first.id, first);
  registry.secondaryUnits.set(second.id, second);
  const general = registry.mergeGeneralParts([first.id, second.id]);

  assert.equal(general.die('combat'), true);
  assert.equal(general.isActive, false);
  assert.equal(general.deathReason, 'combat');
  assert.equal(general.die('duplicate'), false);
  assert.equal(registry.removeGeneral(general.id), true);
  assert.equal(general.inPool, true);
  assert.equal(first.ownerId, -1);
  assert.equal(second.ownerId, -1);
});
