'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { EventBus } = require('../../src/core/EventBus');
const { BattleManager } = require('../../src/battle/BattleManager');
const { GeneralUnit } = require('../../src/generals/GeneralUnit');
const { UnitRegistry } = require('../../src/units/UnitRegistry');
const { SkillFactory } = require('../../src/skills/SkillFactory');
const { SkillManager } = require('../../src/skills/SkillManager');

test('BattleManager triggers an injected general skill through SkillManager and cleans it on recycle', () => {
  const effectCalls = [];
  const skillManager = new SkillManager().configure({
    gameLoop: { register() {}, unregister() {} },
    factory: new SkillFactory(),
    effectPort: {
      execute(key, context) {
        effectCalls.push({ key, ownerId: context.owner.id });
        return { key, ownerId: context.owner.id };
      },
      update() {},
      clearOwner() {},
      gameOver() {},
    },
  });
  skillManager.init();

  const general = new GeneralUnit({ id: 21, name: '赵云' });
  const registry = new UnitRegistry();
  registry.generals.set(general.id, general);
  const battleManager = new BattleManager().configure({ eventBus: new EventBus(), unitManager: registry, skillManager });
  general.configureSkill({ skillManager, skillKey: 'BattleShout' });

  const result = battleManager.triggerGeneralSkill(general.id, { source: 'test' });

  assert.equal(result.activated, true);
  assert.deepEqual(effectCalls, [{ key: 'BattleShout', ownerId: general.id }]);
  assert.equal(skillManager.count, 1);

  general.gameOver();
  assert.equal(skillManager.count, 0);
  assert.equal(general.skill, null);
});
