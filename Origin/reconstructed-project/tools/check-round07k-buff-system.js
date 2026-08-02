'use strict';
const assert = require('assert');
const path = require('path');

const root = path.resolve(__dirname, '..');
const { EventBus, GameEvents } = require(path.join(root, 'src/core/EventBus'));
const { ObjectPool } = require(path.join(root, 'src/core/ObjectPool'));
const {
  BuffManager,
  BuffHandlerFactory,
  BuffRegistry,
  BuffType,
  BuffTimeMode,
} = require(path.join(root, 'src/buffs'));

class SmokeTarget {
  constructor(id) {
    this.id = id;
    this.base = { 0: 10, 1: 1, 2: 2, 3: 50, 4: 100, 6: 1 };
    this.mods = {};
    this.states = new Map();
    this.events = [];
    this.hp = 100;
  }
  am() { return { event: (name, ...args) => this.events.push([name, ...args]) }; }
  jw(type) { return this.base[type]; }
  zw(type, delta, removing = false) {
    this.mods[type] = (this.mods[type] || 0) + delta;
    if (type === BuffType.MAX_HP && !removing && delta > 0) this.hp += delta;
    if (type === BuffType.MAX_HP) this.hp = Math.min(this.hp, (this.base[type] || 0) + (this.mods[type] || 0));
  }
  setState(channel, enabled, data) {
    if (channel === 4 && enabled) { this.hp -= data; return; }
    const count = Math.max(0, (this.states.get(channel) || 0) + (enabled ? 1 : -1));
    if (count) this.states.set(channel, count); else this.states.delete(channel);
  }
  onBuffDataChanged(type) { this.events.push(['data', type]); }
  onBuffTypeChanged(type) { this.events.push(['type', type]); }
}

function run() {
  const eventBus = new EventBus();
  const objectPool = new ObjectPool();
  const target = new SmokeTarget(1);
  const enemyManager = { enemies: new Map([[1, target]]) };
  const unitRegistry = { PA: new Map(), BM: new Map(), AA: new Map() };
  const manager = new BuffManager({
    enemyManager,
    unitRegistry,
    eventBus,
    objectPool,
    logger: { log() {}, warn() {} },
  }).init();

  assert.strictEqual(BuffRegistry.entries().length, 20);
  assert.deepStrictEqual(new BuffHandlerFactory({ objectPool }).keys(), Array.from({ length: 20 }, (_, i) => i));
  assert.throws(() => manager.applyBuff(1, 99, 1), /Unknown Buff type|not implemented/);

  let id = manager.applyBuff(1, BuffType.ATTACK_POWER, 0.5, true);
  assert.strictEqual(target.mods[BuffType.ATTACK_POWER], 5);
  manager.Jw(1, BuffType.ATTACK_POWER, id);
  assert.strictEqual(target.mods[BuffType.ATTACK_POWER], 0);

  id = manager.applyBuff(1, BuffType.MAX_HP, 0.5, true);
  assert.strictEqual(target.mods[BuffType.MAX_HP], 50);
  assert.strictEqual(target.hp, 150);
  manager.Jw(1, BuffType.MAX_HP, id);
  assert.strictEqual(target.mods[BuffType.MAX_HP], 0);
  assert.strictEqual(target.hp, 100);

  manager.applyBuff(1, BuffType.STUN, 0, false, 100);
  assert.strictEqual(target.states.get(1), 1);
  manager.update(100);
  assert.strictEqual(target.states.has(1), false);

  manager.applyBuff(1, BuffType.LOCK, 0, false, BuffTimeMode.ROUND);
  assert.strictEqual(manager.has(1, BuffType.LOCK), true);
  eventBus.event(GameEvents.ROUND_STARTED);
  assert.strictEqual(manager.has(1, BuffType.LOCK), false);

  manager.applyBuff(1, BuffType.KNOCKBACK, 0, false, BuffTimeMode.PERMANENT, { x: 2, y: 0 });
  assert.strictEqual(manager.has(1, BuffType.KNOCKBACK), true);
  const limitId = manager.applyBuff(1, BuffType.LIMIT, 0, false, BuffTimeMode.PERMANENT);
  assert.strictEqual(manager.has(1, BuffType.KNOCKBACK), false);
  assert.strictEqual(manager.has(1, BuffType.LIMIT), true);
  manager.Jw(1, BuffType.LIMIT, limitId);

  let started = 0;
  let ended = 0;
  manager.applyCustom(1, 50, {
    Bv: 'smoke-custom',
    onStart() { started += 1; },
    onEnd() { ended += 1; },
  });
  assert.strictEqual(started, 1);
  manager.update(50);
  assert.strictEqual(ended, 1);

  manager.gameOver();
  assert.strictEqual(manager.activeHandlerCount, 0);
  assert.strictEqual(manager.activeTargetCount, 0);

  console.log(JSON.stringify({ registeredTypes: BuffRegistry.entries().length, status: 'PASS' }));
}

run();
