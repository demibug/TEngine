'use strict';

const { SceneControllerBase } = require('./SceneControllerBase');

/**
 * 重建模块：SCENE-MATCH-01
 * 原始范围：bundle.strings-decoded.js:60834-61284
 * 原始主要符号：nd
 * UUID：dxhrI-d-T2icEkklUGt-kQ
 * 重建状态：PARTIAL_CRITICAL_PATH_IMPLEMENTATION
 */
class MatchSceneController extends SceneControllerBase {
  constructor(...args) {
    super(...args);
    this.transitionDelayMs = 1500;      // 原 tK
    this.matchComplete = false;         // 原 aK
    this.elapsedAfterCompleteMs = 0;    // 原 nK
    this.enteringBattle = false;
    this.enteringBattlePromise = null;
    this._completeMatch = this.completeMatch.bind(this);
  }

  onAwake() {
    const laya = this.requireDependency('laya');
    if (this.xBtn) this.xBtn.on(laya.Event.CLICK, this, this.closeMatch);
  }

  onOpened() {
    this.resetVisualState();
    this.initializeMatchDisplay();
    this.deps.gameLoop.register('MatchScene', this, this.update);
  }

  initializeMatchDisplay() {
    const player = this.deps.gameData.player;
    const completedToday = player.roundDay - 1;
    this.viewModel = {
      playerRank: this.deps.gameData.rank.current.rank,
      playerWinRate: completedToday > 0 ? `${(player.winDay / completedToday * 100).toFixed(1)}%` : '0.0%',
      title: '匹配中',
    };
    this.requireNode('title').text = this.viewModel.title;
    this.deps.laya.timer.once(50, this, this._completeMatch);
  }

  /** 原 rK：无道具展示时将 nK 置为 -1000。 */
  completeMatch() {
    this.title.text = '匹配完成';
    this.matchComplete = true;
    this.elapsedAfterCompleteMs = this.deps.matchPreparation.hasAnyDisplayedProps() ? 0 : -1000;
  }

  update(deltaMs) {
    if (!this.matchComplete || this.enteringBattle) return;
    this.elapsedAfterCompleteMs += deltaMs;
    if (this.elapsedAfterCompleteMs > this.transitionDelayMs) {
      this.matchComplete = false;
      this.enterBattle();
    }
  }

  /** 原始方法符号：oK。 */
  enterBattle() {
    if (this.enteringBattle) return this.enteringBattlePromise;
    this.enteringBattle = true;
    if (this.title) this.title.visible = false;
    if (this.xBtn) this.xBtn.visible = false;
    this.deps.gameLoop.pause(false);

    const start = this.deps.gameData.player.round === 0 && this.deps.tutorialEnabled
      ? this.deps.battleFlow.startTutorialBattle()
      : this.deps.battleFlow.startBattle();

    this.enteringBattlePromise = Promise.resolve(start).then(async battleScene => {
      // CONFIRMED：原流程把 MatchScene 临时挂到 BattleScene 后完成转场。
      if (battleScene && typeof battleScene.addChild === 'function') battleScene.addChild(this);
      if (this.deps.sceneTransition) await this.deps.sceneTransition.matchToBattle(this, battleScene);
      this.closeMatch();
      this.deps.gameLoop.resume();
      return battleScene;
    }, error => {
      this.deps.gameLoop.resume();
      this.enteringBattle = false;
      throw error;
    });
    return this.enteringBattlePromise;
  }

  /** 原 Pn 使用错误键 "match"；onClosed 再移除正确键。 */
  closeMatch() {
    this.deps.gameLoop.unregister('match');
    this.resetVisualState();
    this.deps.sceneManager.closeScene('MatchScene');
  }

  resetVisualState() {
    this.matchComplete = false;
    this.elapsedAfterCompleteMs = 0;
    if (this.title) { this.title.text = '开始匹配'; this.title.visible = true; }
    if (this.xBtn) this.xBtn.visible = true;
  }

  onClosed() {
    this.deps.gameLoop.unregister('MatchScene');
    this.deps.laya.timer.clear(this, this._completeMatch);
    if (this.xBtn) this.xBtn.off(this.deps.laya.Event.CLICK, this, this.closeMatch);
  }
}

MatchSceneController.dependencies = {
  laya: null,
  gameLoop: null,
  sceneManager: null,
  battleFlow: null,
  gameData: null,
  matchPreparation: null,
  sceneTransition: null,
  tutorialEnabled: false,
};
module.exports = { MatchSceneController };
