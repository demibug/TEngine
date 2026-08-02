'use strict';

const { SceneControllerBase } = require('./SceneControllerBase');

/**
 * 重建模块：SCENE-MAIN-01（进入匹配关键路径）
 * 原始范围：bundle.strings-decoded.js:64782-65947
 * 原始主要符号：nA
 * UUID：dKvUsPTsTBGGfiZxHMSqtg
 * 重建状态：PARTIAL_CRITICAL_PATH_IMPLEMENTATION
 */
class MainSceneController extends SceneControllerBase {
  constructor(...args) {
    super(...args);
    this.startDebounceMs = 5000; // 原 PQ
    this.lastStartTime = 0;      // 原 AQ
    this.startPromise = null;
  }

  onAwake() {
    const laya = this.requireDependency('laya');
    this.requireNode('playBtn').on(laya.Event.CLICK, this, this.startGame);
  }

  onOpened() {
    if (this.deps.audio) this.deps.audio.playMusic('bg_mainScene');
  }

  /**
   * 原始方法符号：startGame
   * 原始源码范围：bundle.strings-decoded.js:65424-65443
   * 行为可信度：HIGH
   * 副作用：扣除 5 点体力，准备匹配数据，异步打开 MatchScene。
   */
  async startGame() {
    const stamina = this.deps.gameData.staminaManager;
    if (!stamina.canStartBattle()) {
      const message = '体力不足，无法开始游戏！';
      if (this.deps.tipService) this.deps.tipService.showTip(message);
      // 原代码 Promise.reject("体力不足")。
      throw new Error('体力不足');
    }

    const now = this.deps.now();
    // 原代码在 5000ms 窗口内无显式返回值、无副作用。
    if (now - this.lastStartTime < this.startDebounceMs) return undefined;

    stamina.consumeForBattle();
    this.lastStartTime = now;
    if (this.deps.sceneTransition) await this.deps.sceneTransition.mainToMatch(this);
    this.deps.matchPreparation.prepareBeforeMatch();
    this.deps.sceneManager.openScene('MatchScene', false, null);
    this.startPromise = this.deps.sceneManager.whenLastOpenCompletes();
    await this.startPromise;
    return undefined;
  }

  onClosed() {
    if (this.playBtn) this.playBtn.off(this.deps.laya.Event.CLICK, this, this.startGame);
  }
}

MainSceneController.dependencies = {
  laya: null,
  gameData: null,
  sceneManager: null,
  audio: null,
  tipService: null,
  sceneTransition: null,
  matchPreparation: null,
  now: () => Date.now(),
};
module.exports = { MainSceneController };
