'use strict';
const { DeckDefinitions } = require('./DeckDefinitions');
const { UnitCard } = require('./UnitCard');

/** Engine-independent reconstruction of vN + r0 refresh path. */
class DeckManager {
  constructor({ gameData, economy, randomSource = Math.random, logger = console, definitions = DeckDefinitions } = {}) {
    if (!gameData || !economy) throw new TypeError('DeckManager requires gameData and BattleEconomy');
    Object.assign(this, { gameData, economy, randomSource, logger, definitions });
    this.hands = { player: [], opponent: [] };
    this.nextCardId = 1;
    this.started = false;
  }
  init() {}
  startGame() { this.started = true; this.nextCardId = 1; this.hands.player = this.drawHand(true); this.hands.opponent = this.drawHand(false); }
  poolForSide(_side) { const available = this.gameData.friendlyUnits.texts || this.definitions.basePool; return available.length ? Array.from(available) : Array.from(this.definitions.basePool); }
  drawText(side) { const pool = this.poolForSide(side); const r = Math.max(0, Math.min(0.999999999, Number(this.randomSource()) || 0)); return pool[Math.floor(r * pool.length)] || '刀'; }
  createCard(text, level = 1, source = 'deck') { return new UnitCard({ id: this.nextCardId++, text, level, cost: Math.max(this.definitions.baseUnitCost, level), source }); }
  drawHand(side = true) { return Array.from({ length: this.definitions.handSize }, () => this.createCard(this.drawText(side), 1, side ? 'player' : 'opponent')); }
  hand(side = true) { return this.hands[side ? 'player' : 'opponent']; }
  getCard(side, slot) { return this.hand(side)[slot] || null; }
  setHand(side = true, cards = []) {
    const key = side ? 'player' : 'opponent';
    this.hands[key] = cards.slice(0, this.definitions.handSize).map(card => {
      if (card instanceof UnitCard) return card;
      if (typeof card === 'string') return this.createCard(card, 1, side ? 'player' : 'opponent');
      return this.createCard(card.text, card.level || 1, card.source || (side ? 'player' : 'opponent'));
    });
    while (this.hands[key].length < this.definitions.handSize) this.hands[key].push(this.createCard(this.drawText(side), 1, side ? 'player' : 'opponent'));
    return this.hands[key];
  }
  refresh(side = true) {
    if (!this.started) throw new Error('DeckManager.startGame() must run before refresh');
    const result = this.economy.payRefresh(side); if (!result.success) return result;
    const hand = this.hand(side);
    for (let i = 0; i < hand.length; i += 1) if (!hand[i] || !hand[i].locked) hand[i] = this.createCard(this.drawText(side), 1, side ? 'player' : 'opponent');
    return { success: true, cost: result.amount, nextCost: result.nextCost, hand: hand.map(c => c.toJSON()) };
  }
  consume(side, slot) { const hand = this.hand(side); const card = hand[slot]; if (!card) return null; hand[slot] = this.createCard(this.drawText(side), 1, side ? 'player' : 'opponent'); return card; }
  lock(side, slot, locked = true) { const card = this.getCard(side, slot); if (!card) return false; card.locked = Boolean(locked); return true; }
  gameOver() { this.started = false; this.hands.player.length = 0; this.hands.opponent.length = 0; }
  snapshot() { return { player: this.hands.player.map(c => c.toJSON()), opponent: this.hands.opponent.map(c => c.toJSON()) }; }
}
module.exports = { DeckManager };
