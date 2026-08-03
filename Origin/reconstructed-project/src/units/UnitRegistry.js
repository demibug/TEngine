'use strict';

const { SingletonBase } = require('../core/SingletonBase');
const { SoldierBase } = require('./SoldierBase');
const { UnitContainerType } = require('./UnitBase');
const { GeneralFactory } = require('../generals/GeneralFactory');

/**
 * 重建模块：FRIENDLY-UNIT-COMBAT-01 / UnitRegistry
 * 原始范围：bundle.strings-decoded.js:29460-30476
 * 原始符号：vc
 * 重建状态：COMPLETE_FOR_BASE_SOLDIER_COMBAT
 */
class UnitRegistry extends SingletonBase {
  constructor() {
    super();
    this.scratchIds = [];             // rp
    this.soldiers = new Map();        // PA
    this.secondaryUnits = new Map();  // AA
    this.generals = new Map();        // BM
    this.farmers = new Map();         // EA
    this.generalComponents = new Map(); // BA
    this.deferredBuffs = [];          // DA
    this.initialized = false;
    this._configured = false;
    this.generalFactory = null;
  }

  configure({
    unitFactory,
    gameData,
    eventBus,
    placementReservations,
    parentResolver,
    buffManager = null,
    logger = console,
      mapTileManager = null,
      weaponManager = null,
      skillManager = null,
      generalFactory = null,
      attackEffectManager = null,
      projectileManager = null,
      enemyManager = null,
  } = {}) {
    if (!unitFactory || !gameData || !eventBus || !placementReservations) {
      throw new TypeError('UnitRegistry requires unitFactory, gameData, eventBus and placementReservations');
    }
    if (typeof parentResolver !== 'function') throw new TypeError('UnitRegistry requires parentResolver()');
    Object.assign(this, {
      unitFactory, gameData, eventBus, placementReservations, parentResolver,
      buffManager, logger, mapTileManager, weaponManager, skillManager,
      attackEffectManager, projectileManager, enemyManager,
      generalFactory: generalFactory || new GeneralFactory(),
    });
    this._configured = true;
    return this;
  }

  init() {
    this._requireConfigured();
    this.initialized = true;
  }

  /** 原 vc.startGame 明确为空。 */
  startGame() {}

  /** 原 gP。 */
  createUnit(containerType, text, side, gridX, gridY, level = 1, buffData = null) {
    return this.createFromDescriptor({ containerType, text, side, gridX, gridY, level, buffData });
  }

  /** 原 GA 的核心顺序：分类 → 工厂 → placement/init → register → placement → level/buff。 */
  createFromDescriptor({
    containerType,
    text,
    side,
    gridX,
    gridY,
    level = 1,
    buffData = null,
  } = {}) {
    this._requireInitialized();
    if (this.gameData.battle.isGameOver) return null;
    const category = this.classifyText(text);
    if (category === 'Farmer') throw new Error(`Unit category ${category} is outside FRIENDLY-UNIT-COMBAT-01`);
    if (containerType === UnitContainerType.BATTLE && this.mapTileManager && !this.mapTileManager.canPlace(side, gridX, gridY)) {
      throw new Error(`Battle grid ${side ? 'player' : 'opponent'}:${gridX},${gridY} is unavailable`);
    }
    if (containerType === UnitContainerType.BATTLE && this.hasBattleOccupant(side, gridX, gridY)) {
      throw new Error(`Battle grid ${side ? 'player' : 'opponent'}:${gridX},${gridY} is already occupied`);
    }

    const unit = category === 'GeneralPart'
      ? this.generalFactory.createPart({ word: text, side, level })
      : this.unitFactory.createByText(text);
    unit.setPlacement(containerType, gridX, gridY);
    if (typeof unit.initialize === 'function') unit.initialize(text, side);
    this.register(unit, category);
    if (buffData != null) this.applyInitialBuffContract(unit, buffData);
    this.place(unit, containerType, side, gridX, gridY);
    if (level > 1 && typeof unit.levelUp === 'function') unit.levelUp(level - unit.level, false);
    this.applyDeferredBuffContracts(unit);
    if (category === 'Soldier' && this.weaponManager && typeof this.weaponManager.equipDefault === 'function') {
      this.weaponManager.equipDefault(unit);
    }
    return unit;
  }

  classifyText(text) {
    if (text === '农') return 'Farmer';
    return this.gameData.friendlyUnits.indexOf(text) >= 0 ? 'Soldier' : 'GeneralPart';
  }

  register(unit, category = 'Soldier') {
    if (category === 'Soldier' && unit instanceof SoldierBase) this.soldiers.set(unit.id, unit);
    else if (category === 'GeneralPart') this.secondaryUnits.set(unit.id, unit);
    else if (category === 'Farmer') this.farmers.set(unit.id, unit);
    else throw new Error(`Unsupported unit registration category: ${category}`);
    return unit;
  }

  place(unit, containerType, side, gridX, gridY) {
    if (containerType !== UnitContainerType.BATTLE) {
      throw new Error(`Container type ${containerType} placement is not reconstructed in FRIENDLY-UNIT-COMBAT-01`);
    }
    const parent = this.parentResolver(side);
    const pixelX = gridX * this.gameData.map.gridWidth;
    const pixelY = gridY * this.gameData.map.gridHeight;
    unit.activatePlacement({ parent, pixelX, pixelY, zIndex: gridY });
  }


  reposition(unit) {
    if (!unit || unit.containerType !== UnitContainerType.BATTLE) return false;
    const parent = this.parentResolver(unit.side);
    const pixelX = unit.gridPosition.x * this.gameData.map.gridWidth;
    const pixelY = unit.gridPosition.y * this.gameData.map.gridHeight;
    unit.activatePlacement({ parent, pixelX, pixelY, zIndex: unit.gridPosition.y });
    return true;
  }

  moveUnit(id, gridX, gridY) {
    const unit = this.getUnit(id); if (!unit) return false;
    if (this.mapTileManager && !this.mapTileManager.canPlace(unit.side, gridX, gridY)) return false;
    if (this.hasBattleOccupant(unit.side, gridX, gridY)) return false;
    unit.setPlacement(UnitContainerType.BATTLE, gridX, gridY);
    return this.reposition(unit);
  }

  hasBattleOccupant(side, gridX, gridY) {
    for (const map of [this.soldiers, this.secondaryUnits, this.farmers]) {
      for (const unit of map.values()) {
        if (unit.side === Boolean(side) && unit.containerType === UnitContainerType.BATTLE &&
            unit.gridPosition.x === gridX && unit.gridPosition.y === gridY) return true;
      }
    }
    return false;
  }

  getUnit(id) {
    return this.soldiers.get(id) || this.generals.get(id) || this.secondaryUnits.get(id) || this.farmers.get(id);
  }

  setSkillManager(skillManager) {
    if (skillManager != null && typeof skillManager.attach !== 'function') throw new TypeError('UnitRegistry skillManager requires attach()');
    this.skillManager = skillManager;
    return this;
  }

  awardGeneralExperience(contributorIds, amount = 0) {
    const value = Number(amount);
    if (!Number.isFinite(value) || value < 0) throw new TypeError('General experience reward must be a non-negative number');
    const ids = Array.isArray(contributorIds) ? contributorIds : [contributorIds];
    const results = [];
    for (const id of [...new Set(ids.filter(valueId => valueId != null))]) {
      const general = this.generals.get(id);
      if (!general || typeof general.addExperience !== 'function') continue;
      results.push({ id, result: general.addExperience(value), unit: general });
    }
    return results;
  }

  allUnits() { return [...this.soldiers.values(), ...this.secondaryUnits.values(), ...this.farmers.values()]; }
  unitsBySide(side) { return this.allUnits().filter(unit => unit.side === Boolean(side)); }
  unitsInRadius(x, y, radius, side) {
    const r2 = radius * radius;
    return this.unitsBySide(side).filter(unit => {
      const node = unit.displayObject || unit.Oc; if (!node) return false;
      const cx = node.x + (node.width || 0) / 2, cy = node.y + (node.height || 0) / 2;
      const dx = cx - x, dy = cy - y; return dx * dx + dy * dy <= r2;
    });
  }
  lowestLevel(side, count = 1) { return this.unitsBySide(side).sort((a,b)=>a.level-b.level||a.id-b.id).slice(0,count); }
  highestLevel(side, count = 1) { return this.unitsBySide(side).sort((a,b)=>b.level-a.level||a.id-b.id).slice(0,count); }
  removeUnit(id) { return this.removeSoldier(id) || this.removeGeneral(id) || this.removeSecondary(id) || this.removeFarmer(id); }

  removeSoldier(id) {
    const unit = this.soldiers.get(id);
    if (!unit) return false;
    if (this.placementReservations) this.placementReservations.delete(this.reservationKey(unit.side, unit.gridPosition.x, unit.gridPosition.y));
    if (this.weaponManager && unit.weapon) this.weaponManager.remove(unit.weapon);
    unit.gameOver();
    this.soldiers.delete(id);
    return true;
  }

  removeSecondary(id) {
    const unit = this.secondaryUnits.get(id);
    if (!unit) return false;
    if (this.placementReservations) this.placementReservations.delete(this.reservationKey(unit.side, unit.gridPosition.x, unit.gridPosition.y));
    unit.gameOver();
    this.secondaryUnits.delete(id);
    return true;
  }

  /** Recovered sE/QA path: two GeneralPart objects become one GeneralUnit. */
  mergeGeneralParts(partIds, { side = true, isPlayer = true, weaponId = null, weapon = null, combat = null, experienceThresholds = null, experience = 0, skillManager = this.skillManager, skillKey = null, skill = null } = {}) {
    const parts = partIds.map(id => this.secondaryUnits.get(id));
    if (parts.some(part => !part)) throw new Error(`Unknown general part in merge: ${partIds.join(',')}`);
    if (parts.some(part => part.ownerId !== -1)) throw new Error(`General part is already assigned: ${parts.find(part => part.ownerId !== -1).id}`);
    // 调用方未显式传 combat 时,从 registry 的 enemyManager/效果管理器自动构造,使武将合成即参战。
    const sourceCombat = combat || this._buildAutoCombat(parts);
    const generalCombat = sourceCombat ? {
      ...sourceCombat,
      attackEffectManager: sourceCombat.attackEffectManager || this.attackEffectManager,
      projectileManager: sourceCombat.projectileManager || this.projectileManager,
    } : sourceCombat;
    const general = this.generalFactory.createGeneral(parts, { side, isPlayer, weaponId, weapon, combat: generalCombat, experienceThresholds, experience, skillManager, skillKey, skill });
    this.generals.set(general.id, general);
    this.generalComponents.set(general.id, parts.map(part => part.id));
    return general;
  }

  /** 由 registry 持有的运行时依赖构造武将战斗配置;enemyManager 缺失时返回 null(武将暂不参战)。 */
  _buildAutoCombat(parts) {
    if (!this.enemyManager || typeof this.enemyManager.queryTargets !== 'function') return null;
    const map = this.gameData && this.gameData.map ? this.gameData.map : null;
    const width = map ? map.gridWidth : 0;
    const height = map ? map.gridHeight : 0;
    const first = parts[0];
    let position = { x: 0, y: 0, width, height };
    if (first && first.placement) {
      position = { x: Number(first.placement.pixelX) || 0, y: Number(first.placement.pixelY) || 0, width, height };
    }
    return {
      enemyManager: this.enemyManager,
      attackEffectManager: this.attackEffectManager,
      projectileManager: this.projectileManager,
      position,
    };
  }

  removeGeneral(id) {
    const general = this.generals.get(id);
    if (!general) return false;
    const parts = Array.isArray(general.parts) ? general.parts.slice() : [];
    const result = typeof general.recycle === 'function' ? general.recycle('registry-remove') : general.gameOver();
    for (const part of parts) {
      if (part && typeof part.unbindFromGeneral === 'function') part.unbindFromGeneral(id);
    }
    this.generals.delete(id);
    this.generalComponents.delete(id);
    return result !== false;
  }

  removeFarmer(id) {
    const unit = this.farmers.get(id);
    if (!unit) return false;
    unit.gameOver();
    this.farmers.delete(id);
    return true;
  }

  applyInitialBuffContract(unit, buffData) {
    if (!this.buffManager) throw new Error('UnitRegistry requires BuffManager to apply initial buffs');
    if (!buffData) return;
    const list = Array.isArray(buffData) ? buffData : [buffData];
    for (const item of list) this.buffManager.applyData(unit.id, item.type, item.data || item);
  }

  applyDeferredBuffContracts(unit) {
    if (!this.deferredBuffs.length) return;
    if (!this.buffManager) throw new Error(`BuffManager is required to apply deferred buffs to unit ${unit.id}`);
    for (const item of this.deferredBuffs) this.buffManager.applyData(unit.id, item.type, item.data || item);
  }

  gameOver() {
    for (const general of this.generals.values()) {
      if (general && typeof general.gameOver === 'function') general.gameOver();
    }
    this.generals.clear();
    this.generalComponents.clear();

    this.scratchIds.length = 0;
    for (const id of this.soldiers.keys()) this.scratchIds.push(id);
    for (const id of this.scratchIds) this.removeSoldier(id);

    this.scratchIds.length = 0;
    for (const id of this.secondaryUnits.keys()) this.scratchIds.push(id);
    for (const id of this.scratchIds) this.removeSecondary(id);

    this.scratchIds.length = 0;
    for (const id of this.farmers.keys()) this.scratchIds.push(id);
    for (const id of this.scratchIds) this.removeFarmer(id);

    this.soldiers.clear();
    this.secondaryUnits.clear();
    this.farmers.clear();
    this.deferredBuffs.length = 0;
  }

  reservationKey(side, x, y) {
    return `${side ? 'player' : 'opponent'}:${x}:${y}`;
  }

  get count() { return this.soldiers.size + this.secondaryUnits.size + this.generals.size + this.farmers.size; }
  get playerSoldierCount() {
    let count = 0;
    for (const unit of this.soldiers.values()) if (unit.side) count += 1;
    return count;
  }

  // Original-symbol compatibility aliases used by reconstructed BattleManager and mapping tests.
  get PA() { return this.soldiers; }
  get AA() { return this.secondaryUnits; }
  get BM() { return this.generals; }
  get EA() { return this.farmers; }
  get BA() { return this.generalComponents; }
  get DA() { return this.deferredBuffs; }
  gP(...args) { return this.createUnit(...args); }
  GA(descriptor) {
    return this.createFromDescriptor({
      containerType: descriptor.containerType,
      text: descriptor.text,
      side: descriptor.nm,
      gridX: descriptor.x,
      gridY: descriptor.y,
      level: descriptor.We,
      buffData: descriptor.L_,
    });
  }
  WP(id) { return this.removeSoldier(id); }
  HP(id) { return this.removeSecondary(id); }
  QA(general, partIds = general.partIds) {
    this.generals.set(general.id, general);
    this.generalComponents.set(general.id, partIds.slice());
    return general;
  }
  sE(partIds, options) { return this.mergeGeneralParts(partIds, options); }
  TA(id) { return this.removeGeneral(id); }
  uM(id) { return this.getUnit(id); }
  mE(side, x, y) { return this.hasBattleOccupant(side, x, y); }

  resetForTests() {
    this.gameOver();
    this.scratchIds.length = 0;
    this.initialized = false;
    this._configured = false;
    this.unitFactory = null;
    this.gameData = null;
    this.eventBus = null;
    this.placementReservations = null;
    this.parentResolver = null;
    this.mapTileManager = null;
    this.weaponManager = null;
    this.skillManager = null;
    this.attackEffectManager = null;
    this.projectileManager = null;
    this.enemyManager = null;
    this.generalFactory = null;
  }

  _requireConfigured() {
    if (!this._configured) throw new Error('UnitRegistry.configure() must run before init()');
  }
  _requireInitialized() {
    this._requireConfigured();
    if (!this.initialized) throw new Error('UnitRegistry.init() must run before unit creation');
  }
}

module.exports = { UnitRegistry };
