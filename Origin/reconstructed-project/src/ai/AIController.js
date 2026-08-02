'use strict';
const { BattleInputCommand, BattleInputCommandType } = require('../input/BattleInputCommand');

/**
 * Engine-independent minimal reconstruction of vS opponent deployment logic.
 * It uses the same DeckManager, BattleInputController and UnitFactory path as the player.
 */
class AIController {
  constructor({ gameLoop, gameData, deckManager, inputController, randomSource = Math.random, logger = console, decisionIntervalMs = 800, initialUnitTarget = 1 } = {}) {
    if (!gameLoop || !gameData || !deckManager || !inputController) throw new TypeError('AIController requires gameLoop, gameData, deckManager and inputController');
    Object.assign(this, { gameLoop, gameData, deckManager, inputController, randomSource, logger, decisionIntervalMs, initialUnitTarget });
    this.started = false; this.elapsedMs = 0; this.deployed = 0; this.actions = [];
  }
  init() {}
  startGame() {
    this.started = true; this.elapsedMs = 0; this.deployed = 0; this.actions.length = 0;
    // Original AI adds a starting battle resource before its first refresh/deployment.
    if (this.gameData.battle.opponentGold < this.gameData.battle.initialGold) this.gameData.battle.opponentGold += this.gameData.battle.initialGold;
    this.gameLoop.register('AIController', this, this.update);
    this.deployUntilReady();
  }
  update(deltaMs) {
    if (!this.started || this.gameData.battle.isGameOver) return;
    this.elapsedMs += deltaMs; if (this.elapsedMs < this.decisionIntervalMs) return; this.elapsedMs = 0;
    if (!this.gameData.battle.opponentPlacementComplete) this.deployUntilReady();
  }
  deployUntilReady() {
    while (this.deployed < this.initialUnitTarget) {
      const placement = this.choosePlacement(false); if (!placement) break;
      let result = this.tryDeploy(placement);
      if (!result.success && result.reason === '馒头不足') {
        const refresh = this.inputController.execute(new BattleInputCommand(BattleInputCommandType.REFRESH, { side: false }));
        this.actions.push({ type: 'refresh', result });
        if (!refresh.success) break;
        result = this.tryDeploy(placement);
      }
      if (!result.success) break;
      this.deployed += 1; this.actions.push({ type: 'deploy', unitId: result.unit.id, text: result.unit.unitText, placement });
    }
    if (this.deployed >= this.initialUnitTarget) this.gameData.battle.opponentPlacementComplete = true;
  }
  tryDeploy(placement) {
    const hand = this.deckManager.hand(false);
    // Prefer a melee unit far from the route so the deterministic smoke can still reach GameOver.
    let slot = hand.findIndex(card => card && card.text === '刀'); if (slot < 0) slot = hand.findIndex(Boolean);
    return this.inputController.execute(new BattleInputCommand(BattleInputCommandType.PURCHASE_AND_PLACE, { side: false, slot, gridX: placement.x, gridY: placement.y }));
  }
  choosePlacement(side) {
    const map = this.gameData.map; const route = map.pathForSide(side) || [];
    const candidates = [];
    for (let x = 0; x < map.width; x += 1) for (let y = 0; y < map.height; y += 1) {
      const check = this.inputController.validatePlacement(side, x, y); if (!check.valid) continue;
      let minD2 = Infinity; for (const point of route) { const dx = x - point.x, dy = y - point.y; minD2 = Math.min(minD2, dx * dx + dy * dy); }
      candidates.push({ x, y, minD2 });
    }
    candidates.sort((a,b) => b.minD2 - a.minD2 || a.x - b.x || a.y - b.y);
    return candidates[0] || null;
  }
  gameOver() { this.started = false; this.gameLoop.unregister('AIController'); this.elapsedMs = 0; }
  snapshot() { return { started: this.started, deployed: this.deployed, actions: this.actions.slice() }; }
}
module.exports = { AIController };
