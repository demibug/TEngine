'use strict';

const { SingletonBase } = require('../core/SingletonBase');
const { GameEvents } = require('../core/EventBus');
const { ENEMY_TYPE_KEYS, BOSS_TYPE_KEYS } = require('./EnemyFactory');

function circleIntersectsRect(radius, centerX, centerY, rectX, rectY, rectWidth, rectHeight) {
  // CONFIRMED：np.Es 先将半径减 1，再做 circle-vs-AABB 最近点测试。
  radius -= 1;
  const rectRight = rectX + rectWidth;
  const rectBottom = rectY + rectHeight;
  const dx = centerX - Math.max(rectX, Math.min(centerX, rectRight));
  const dy = centerY - Math.max(rectY, Math.min(centerY, rectBottom));
  return dx * dx + dy * dy <= radius * radius;
}

/**
 * 重建模块：ENEMY-RUNTIME-01 / EnemyManager
 * 原始范围：bundle.strings-decoded.js:32939-33696
 * 原始主要符号：vi
 * 重建状态：COMPLETE_FOR_MOB0_RUNTIME_AND_SPATIAL_QUERY
 */
class EnemyManager extends SingletonBase {
  constructor() {
    super();
    this.enemies = new Map();          // JS
    this.updateBuffer = [];            // vB
    this.queryBuffer = [];             // rp
    this.cellToEnemyIds = new Map();   // mB
    this.enemyIdToCell = new Map();    // wB
    this.deferredBuffs = [];           // DA
    this.gridSize = 80;
    this.initialized = false;
    this.spawnCalls = [];
    this.spawnLog = this.spawnCalls;
    this.prepareWaveCalls = 0;
    this.prepareWaveCount = 0;
  }

  configure({ gameLoop, gameData, eventBus, factory, laya, buffManager = null, logger = console, randomSource = Math.random } = {}) {
    if (!gameLoop || !gameData || !eventBus || !factory) {
      throw new TypeError('EnemyManager requires gameLoop, gameData, eventBus and factory');
    }
    Object.assign(this, { gameLoop, gameData, eventBus, factory, laya, buffManager, logger, randomSource });
    return this;
  }

  init() {
    this.gridSize = this.gameData.map.gridWidth;
    this.gameLoop.register('enemyMgr', this, this.update);
    this.addEvent();
    this.initialized = true;
  }

  /** 原 vi.startGame 明确为空。 */
  startGame() {}

  addEvent() {
    this.eventBus.on(GameEvents.ENEMY_REGISTERED, this, this._onEnemyRegistered);
    this.eventBus.on(GameEvents.ENEMY_REMOVED, this, this._onEnemyRemoved);
    this.eventBus.on(GameEvents.ENEMY_GRID_LEFT, this, this._onEnemyMovedCell);
  }

  update(deltaMs) {
    this._updateEnemies(deltaMs);
    // 原 vi.MB 为临近终点提示；提示表现不影响 Mob0 逻辑，事件在 EnemyBase 路径索引变化时保留。
  }

  _updateEnemies(deltaMs) {
    this.updateBuffer.length = 0;
    for (const enemy of this.enemies.values()) this.updateBuffer.push(enemy);
    for (const enemy of this.updateBuffer) {
      if (enemy.currentState !== 4 && enemy.currentState !== 0) enemy.update(deltaMs);
    }
  }

  /** 原 jL。 */
  spawn(typeIndex, isPlayerSide, isSpecial = false) {
    if (!this.initialized) throw new Error('EnemyManager.init() must run before spawn()');
    const typeKey = ENEMY_TYPE_KEYS[typeIndex];
    if (!typeKey) throw new Error(`Unknown enemy type index: ${typeIndex}`);
    const enemy = this.factory.create(typeKey);
    enemy.typeIndex = typeIndex;
    enemy.isSpecial = Boolean(isSpecial);
    enemy.init(Boolean(isPlayerSide));
    this._applyDeferredBuffContracts(enemy);
    this.spawnCalls.push({ enemy, typeIndex, typeName: typeKey, isPlayerSide: Boolean(isPlayerSide), playerSide: Boolean(isPlayerSide), isSpecial: Boolean(isSpecial) });
    this.eventBus.event(GameEvents.ENEMY_CREATED, Boolean(isPlayerSide));
    return enemy;
  }


  spawnByKey(typeKey, isPlayerSide, isSpecial = false, configure = null) {
    if (!this.initialized) throw new Error('EnemyManager.init() must run before spawnByKey()');
    const typeIndex = ENEMY_TYPE_KEYS.indexOf(typeKey);
    if (typeIndex < 0) throw new Error(`Unknown enemy type key: ${typeKey}`);
    const enemy = this.factory.create(typeKey);
    enemy.typeIndex = typeIndex;
    enemy.isSpecial = Boolean(isSpecial);
    if (typeof configure === 'function') configure(enemy);
    enemy.init(Boolean(isPlayerSide));
    this._applyDeferredBuffContracts(enemy);
    this.spawnCalls.push({ enemy, typeIndex, typeName: typeKey, isPlayerSide: Boolean(isPlayerSide), playerSide: Boolean(isPlayerSide), isSpecial: Boolean(isSpecial) });
    this.eventBus.event(GameEvents.ENEMY_CREATED, Boolean(isPlayerSide));
    return enemy;
  }

  /** 原 jB；非 Mob0 Boss 分支仍明确拒绝。 */
  prepareWave() {
    this.prepareWaveCalls += 1;
    this.prepareWaveCount += 1;
    const battle = this.gameData.battle;
    const round = battle.currentRound;
    if (battle.forceBossNextRound) throw new Error(`Forced boss creation for round ${round} is not reconstructed`);
    const index = this.gameData.enemy.bossWaveNumbers.indexOf(round);
    if (index < 0 || battle.bossDecisionByRound[round] !== undefined) return false;
    const shouldSpawn = this.randomSource() < this.gameData.enemy.bossSpawnChances[index];
    battle.bossDecisionByRound[round] = shouldSpawn;
    if (!shouldSpawn) return false;
    const bossIndex = this.gameData.map.mapIndex * 3 + this.gameData.enemy.bossRotationIndex;
    const bossType = BOSS_TYPE_KEYS[bossIndex];
    if (!this.factory.creators.has(bossType)) throw new Error(`Boss type ${bossType} for round ${round} is not reconstructed`);
    return true;
  }

  _onEnemyRegistered(id, enemy) {
    this.enemies.set(id, enemy);
    this._indexEnemy(id, enemy);
  }

  _onEnemyRemoved(id) {
    this._unindexEnemy(id);
    this.enemies.delete(id);
  }

  _onEnemyMovedCell(id, enemy) {
    const key = this._cellKeyForEnemy(enemy);
    if (this.enemyIdToCell.get(id) !== key) this._indexEnemy(id, enemy);
  }

  _cellKey(x, y) { return `${x}_${y}`; }

  _cellCoordinates(enemy) {
    const centerX = enemy.visual.x + enemy.visual.width / 2;
    const centerY = enemy.visual.y + enemy.visual.height / 2;
    return { x: Math.floor(centerX / this.gridSize), y: Math.floor(centerY / this.gridSize) };
  }

  _cellKeyForEnemy(enemy) {
    const cell = this._cellCoordinates(enemy);
    return this._cellKey(cell.x, cell.y);
  }

  _indexEnemy(id, enemy) {
    this._unindexEnemy(id);
    const key = this._cellKeyForEnemy(enemy);
    let ids = this.cellToEnemyIds.get(key);
    if (!ids) { ids = new Set(); this.cellToEnemyIds.set(key, ids); }
    ids.add(id);
    this.enemyIdToCell.set(id, key);
  }

  _unindexEnemy(id) {
    const key = this.enemyIdToCell.get(id);
    if (key !== undefined) {
      const ids = this.cellToEnemyIds.get(key);
      if (ids) {
        ids.delete(id);
        if (ids.size === 0) this.cellToEnemyIds.delete(key);
      }
      this.enemyIdToCell.delete(id);
    }
  }

  _candidateIds(centerX, centerY, radius) {
    const result = new Set();
    const minX = Math.floor((centerX - radius) / this.gridSize);
    const maxX = Math.floor((centerX + radius) / this.gridSize);
    const minY = Math.floor((centerY - radius) / this.gridSize);
    const maxY = Math.floor((centerY + radius) / this.gridSize);
    for (let x = minX; x <= maxX; x += 1) {
      for (let y = minY; y <= maxY; y += 1) {
        const ids = this.cellToEnemyIds.get(this._cellKey(x, y));
        if (ids) for (const id of ids) result.add(id);
      }
    }
    return result;
  }

  /**
   * 原始方法符号：qx
   * 原始源码范围：bundle.strings-decoded.js:33180-33220
   * 返回值不排序；顺序来自空间单元扫描和 Set 插入顺序。
   */
  queryTargets(centerX, centerY, radius, playerSide) {
    const results = [];
    const map = this.gameData.map;
    for (const id of this._candidateIds(centerX, centerY, radius)) {
      const enemy = this.enemies.get(id);
      if (!enemy || !enemy.isTargetableBy(Boolean(playerSide))) continue;
      if (!circleIntersectsRect(radius, centerX, centerY, enemy.visual.x, enemy.visual.y, map.gridWidth, map.gridHeight)) continue;
      results.push({ id, x: enemy.visual.x, y: enemy.visual.y, Bm: enemy.remainingPathDistance });
    }
    return results;
  }

  queryEnemyObjects(centerX, centerY, radius, playerSide, output = []) {
    const map = this.gameData.map;
    for (const id of this._candidateIds(centerX, centerY, radius)) {
      const enemy = this.enemies.get(id);
      if (enemy && enemy.isTargetableBy(Boolean(playerSide)) &&
          circleIntersectsRect(radius, centerX, centerY, enemy.visual.x, enemy.visual.y, map.gridWidth, map.gridHeight)) output.push(enemy);
    }
    return output;
  }

  queryAroundEnemy(source, radius, playerSide) {
    const results = [];
    const map = this.gameData.map;
    for (const id of this._candidateIds(source.x, source.y, radius)) {
      if (id === source.id) continue;
      const enemy = this.enemies.get(id);
      if (enemy && enemy.isTargetableBy(Boolean(playerSide)) &&
          circleIntersectsRect(radius, source.x, source.y, enemy.visual.x, enemy.visual.y, map.gridWidth, map.gridHeight)) {
        results.push({ id, x: enemy.visual.x, y: enemy.visual.y, Bm: enemy.remainingPathDistance });
      }
    }
    return results;
  }


  /** 原 vi.JS.get；供最小刀兵攻击时间线按目标 ID 解析。 */
  getById(id) { return this.enemies.get(id) || null; }

  applyDamage(damage, targetDtos, attacker) {
    for (const target of targetDtos) {
      const enemy = this.enemies.get(target.id);
      if (enemy) enemy.hit(damage, attacker);
    }
  }

  forceRemove(id) {
    const enemy = this.enemies.get(id);
    if (enemy) enemy.gameOver();
  }

  closestToEnd(count, playerSide) {
    const entries = [];
    for (const [id, enemy] of this.enemies) if (enemy.isTargetableBy(Boolean(playerSide))) entries.push([id, enemy.remainingPathDistance]);
    entries.sort((a, b) => a[1] - b[1]);
    return entries.slice(0, count).map(([id]) => this._toTargetDto(this.enemies.get(id)));
  }

  randomTarget(playerSide) {
    this.queryBuffer.length = 0;
    for (const enemy of this.enemies.values()) if (enemy.isTargetableBy(Boolean(playerSide))) this.queryBuffer.push(enemy);
    if (this.queryBuffer.length === 0) return { id: -1, x: 0, y: 0, Bm: Infinity };
    const index = Math.floor(this.randomSource() * this.queryBuffer.length);
    return this._toTargetDto(this.queryBuffer[index]);
  }

  lowestHealthTarget(playerSide) {
    let selected = null;
    for (const enemy of this.enemies.values()) {
      if (enemy.isTargetableBy(Boolean(playerSide)) && (!selected || enemy.health < selected.health)) selected = enemy;
    }
    return selected ? this._toTargetDto(selected) : null;
  }

  frontmostPathPosition(playerSide) {
    let selected = null;
    for (const enemy of this.enemies.values()) {
      if (enemy.isPlayerLane === Boolean(playerSide) && (!selected || enemy.currentPathIndex > selected.currentPathIndex)) selected = enemy;
    }
    return selected ? { index: selected.currentPathIndex, x: selected.visual.x, y: selected.visual.y } : null;
  }

  _toTargetDto(enemy) {
    return { id: enemy.id, x: enemy.visual.x, y: enemy.visual.y, Bm: enemy.remainingPathDistance };
  }

  _applyDeferredBuffContracts(enemy) {
    if (!this.deferredBuffs.length) return;
    if (!this.buffManager) throw new Error(`BuffManager is required to apply deferred enemy buffs to enemy ${enemy.id}`);
    for (const item of this.deferredBuffs) this.buffManager.applyData(enemy.id, item.type, item.data || item);
  }

  gameOver() {
    if (this.laya && this.laya.timer) this.laya.timer.clearAll(this);
    for (const enemy of [...this.enemies.values()]) enemy.gameOver();
    this.enemies.clear();
    this.updateBuffer.length = 0;
    this.queryBuffer.length = 0;
    this.cellToEnemyIds.clear();
    this.enemyIdToCell.clear();
    this.deferredBuffs.length = 0;
    this.spawnCalls.length = 0;
  }

  get count() { return this.enemies.size; }
  get spatialCellCount() { return this.cellToEnemyIds.size; }
  hasSpatialRegistration(id) { return this.enemyIdToCell.has(id); }
  spatialKeyFor(id) { return this.enemyIdToCell.get(id) || null; }

  setRandomSource(randomSource) { this.randomSource = randomSource || Math.random; }

  resetForTests() {
    if (this.eventBus) this.eventBus.offAllCaller(this);
    if (this.gameLoop) this.gameLoop.unregister('enemyMgr');
    this.gameOver();
    this.initialized = false;
    this.prepareWaveCalls = 0;
    this.prepareWaveCount = 0;
  }
}

module.exports = { EnemyManager, circleIntersectsRect };
