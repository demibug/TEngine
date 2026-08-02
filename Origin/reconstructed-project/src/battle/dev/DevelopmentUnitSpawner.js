'use strict';

/**
 * DEVELOPMENT_ONLY：使用正式 UnitRegistry/UnitFactory 创建单位。
 * 不直接 new，不绕过正式初始化、注册和放置合法性。
 */
class DevelopmentUnitSpawner {
  constructor({ unitRegistry, placementReservations, gameData } = {}) {
    if (!unitRegistry || !placementReservations || !gameData) {
      throw new TypeError('DevelopmentUnitSpawner requires unitRegistry, placementReservations and gameData');
    }
    Object.assign(this, { unitRegistry, placementReservations, gameData });
    this.calls = [];
  }

  spawnKnife(options = {}) { return this._spawn('刀', options); }
  spawnBow(options = {}) { return this._spawn('弓', options); }
  spawnSpear(options = {}) { return this._spawn('枪', options); }
  spawnCavalry(options = {}) { return this._spawn('骑', options); }

  _spawn(text, { side = true, gridX, gridY, level = 1 } = {}) {
    this._validateGrid(gridX, gridY);
    if (this.unitRegistry.hasBattleOccupant(side, gridX, gridY)) {
      throw new Error(`Development placement is occupied: ${side ? 'player' : 'opponent'}:${gridX},${gridY}`);
    }
    const key = this.unitRegistry.reservationKey(side, gridX, gridY);
    if (this.placementReservations.items.has(key)) throw new Error(`Development placement is already reserved: ${key}`);
    this.placementReservations.add(key);
    try {
      const unit = this.unitRegistry.createUnit(1, text, side, gridX, gridY, level, null);
      this.calls.push({ mode: 'DEVELOPMENT_SAMPLE', text, side: Boolean(side), gridX, gridY, level, unitId: unit.id });
      return unit;
    } finally {
      this.placementReservations.delete(key);
    }
  }

  _validateGrid(x, y) {
    if (!Number.isInteger(x) || !Number.isInteger(y)) throw new TypeError('Development unit grid coordinates must be integers');
    const map = this.gameData.map.map;
    if (!map || x < 0 || y < 0 || x >= map.length || y >= map[0].length) {
      throw new RangeError(`Development unit grid coordinate is outside the current map: ${x},${y}`);
    }
  }
}

module.exports = { DevelopmentUnitSpawner };
