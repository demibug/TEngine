'use strict';

/**
 * Engine-independent battle economy reconstructed from uo/r0.
 * Player and opponent refresh costs start at 10 and increase by 2 per refresh.
 */
class BattleEconomy {
  constructor({ battleState, eventBus = null, logger = console } = {}) {
    if (!battleState) throw new TypeError('BattleEconomy requires BattleState');
    Object.assign(this, { battleState, eventBus, logger });
    this.killGold = 0;
    this.spentGold = 0;
    this.refreshCount = { player: 0, opponent: 0 };
  }
  startGame() {
    this.killGold = 0;
    this.spentGold = 0;
    this.refreshCount.player = 0;
    this.refreshCount.opponent = 0;
  }
  balance(side = true) { return side ? this.battleState.gold : this.battleState.opponentGold; }
  setBalance(side, value) { if (side) this.battleState.gold = value; else this.battleState.opponentGold = value; return value; }
  refreshCost(side = true) { return side ? this.battleState.playerRecruitCost : this.battleState.opponentRecruitCost; }
  canAfford(side, amount) { return this.balance(side) >= Number(amount || 0); }
  spend(side, amount, reason = 'battle') {
    const value = Math.max(0, Number(amount) || 0);
    if (!this.canAfford(side, value)) return { success: false, reason: '馒头不足', amount: value };
    this.setBalance(side, this.balance(side) - value);
    this.spentGold += value;
    return { success: true, amount: value, reason };
  }
  payRefresh(side = true) {
    const cost = this.refreshCost(side);
    const result = this.spend(side, cost, 'refresh');
    if (!result.success) return result;
    if (side) this.battleState.playerRecruitCost += 2;
    else this.battleState.opponentRecruitCost += 2;
    this.refreshCount[side ? 'player' : 'opponent'] += 1;
    return { ...result, nextCost: this.refreshCost(side) };
  }
  recruitCost(card) { return Math.max(1, Number(card && card.cost) || Math.max(1, Number(card && card.level) || 1)); }
  payRecruit(side, card) { return this.spend(side, this.recruitCost(card), 'recruit'); }
  award(side, amount, reason = 'kill') {
    const value = Math.max(0, Number(amount) || 0);
    this.setBalance(side, this.balance(side) + value);
    if (reason === 'kill') this.killGold += value;
    return value;
  }
  gameOver() {}
  snapshot() { return { playerGold: this.battleState.gold, opponentGold: this.battleState.opponentGold, spentGold: this.spentGold, killGold: this.killGold, refreshCount: { ...this.refreshCount } }; }
}
module.exports = { BattleEconomy };
