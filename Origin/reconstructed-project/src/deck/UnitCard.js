'use strict';
class UnitCard {
  constructor({ id, text, level = 1, cost = level, source = 'deck' } = {}) {
    this.id = id; this.text = text; this.level = level; this.cost = cost; this.source = source; this.locked = false;
  }
  clone() { return new UnitCard({ id: this.id, text: this.text, level: this.level, cost: this.cost, source: this.source }); }
  toJSON() { return { id: this.id, text: this.text, level: this.level, cost: this.cost, source: this.source, locked: this.locked }; }
}
module.exports = { UnitCard };
