'use strict';

const { SceneControllerBase } = require('./SceneControllerBase');
const { GameEvents } = require('../core/EventBus');

/**
 * 重建模块：SCENE-BATTLE-01
 * 原始范围：bundle.strings-decoded.js:57007-59129
 * 原始主要符号：r5
 * UUID：a1VsRozfQfKce35jblVR3w
 * 重建状态：PARTIAL_CRITICAL_PATH_IMPLEMENTATION
 */
class BattleSceneController extends SceneControllerBase {
  constructor(...args) {
    super(...args);
    this.closed = false;
    this.gameEnded = false;
    this.battleStarted = false;
    this.shovelPulseDirection = 1; // 原 Iq
    this.firstFrameExecuted = false;
    this.fixedUpdateCount = 0;
    this.enemyCreatedCount = 0; // DEVELOPMENT_OBSERVABILITY；由原敌人创建事件计数。
  }

  /** 原 onAwake 的进入战斗必要子集。 */
  onAwake() {
    const laya = this.requireDependency('laya');
    this.requireNode('map');
    this.requireNode('gameObjectBox');
    this.requireNode('effectBox');
    this.requireNode('round');
    this.requireNode('goldNumTxt');
    this.requireNode('end1');
    this.requireNode('end2');
    this._ensurePresentationLayers();

    this._createBattleTargets(); // 原 Gq

    if (this.shovelAd) this.shovelAd.on(laya.Event.CLICK, this, this.onShovelClick);
    if (this.refreshBtn) this.refreshBtn.on(laya.Event.CLICK, this, this.onRefreshClick);
    if (this.deckBtn) this.deckBtn.on(laya.Event.CLICK, this, this.onDeckClick);
    if (this.xBtn) this.xBtn.on(laya.Event.CLICK, this, this.pause);

    const eventBus = this.requireDependency('eventBus');
    eventBus.on(GameEvents.BATTLE_SCENE_GAME_OVER, this, this.gameOver);
    eventBus.on(GameEvents.ROUND_STARTED, this, this.onRoundStarted);
    eventBus.on(GameEvents.ENEMY_CREATED, this, this.onEnemyCreated);
  }

  _ensurePresentationLayers() {
    const laya = this.requireDependency('laya');
    const ensure = (name, parent, zIndex) => {
      let node = this[name] || (parent && parent.getChildByName && parent.getChildByName(name));
      if (!node) { node = new laya.Sprite(); node.name = name; node.zIndex = zIndex; if (parent) parent.addChild(node); }
      node.size((parent && parent.width) || laya.stage.width || 640, (parent && parent.height) || laya.stage.height || 1386);
      this[name] = node; return node;
    };
    this.battleWorldLayer = ensure('battleWorldLayer', this.gameObjectBox, 0);
    this.skillVfxLayer = ensure('skillVfxLayer', this.effectBox, 200);
    this.weatherLayer = ensure('weatherLayer', this.effectBox, 500);
    this.overlayLayer = ensure('overlayLayer', this.effectBox, 900);
    this.uiLayer = ensure('uiLayer', this, 1000);
  }

  getPresentationLayers() { return { battleWorldLayer:this.battleWorldLayer, skillVfxLayer:this.skillVfxLayer, weatherLayer:this.weatherLayer, overlayLayer:this.overlayLayer, uiLayer:this.uiLayer }; }

  /**
   * 原始方法符号：Gq
   * 原始源码范围：bundle.strings-decoded.js:58444-58490
   * 行为可信度：HIGH（阿斗对象、名称、锚点、坐标、父节点及初始隐藏）
   */
  _createBattleTargets() {
    const laya = this.requireDependency('laya');
    const animationEntityPool = this.requireDependency('animationEntityPool');

    this.eatIndicator = new laya.Image();
    this.eatIndicator.name = 'eat';
    this.eatIndicator.skin = 'resources/img/battleUI/eat1.png';
    this.eatIndicator.anchorX = 0.5;
    this.eatIndicator.anchorY = 0.5;
    this.eatIndicator.alpha = 0;
    this.end1.addChild(this.eatIndicator);

    for (const end of [this.end1, this.end2]) {
      const shadow = new laya.Image('resources/img/battleUI/deckBtn2.png');
      shadow.name = 'shadow';
      shadow.size(44, 13);
      shadow.anchorX = 0.5;
      shadow.anchorY = 1;
      shadow.alpha = 0.5;
      shadow.pos(end.width / 2, end.height);
      end.addChild(shadow);
    }

    // CONFIRMED：原工厂调用只传类型键，不传阵营参数；阵营由父节点/后续 tm 组件确定。
    this.playerTarget = animationEntityPool.create('aDou');
    this.opponentTarget = animationEntityPool.create('aDou');
    if (typeof this.playerTarget.bindBattleTarget !== 'function' || typeof this.opponentTarget.bindBattleTarget !== 'function') {
      throw new Error('aDou targets must implement bindBattleTarget() for enemy contact damage');
    }
    this.playerTarget.bindBattleTarget({ battleState: this.deps.gameData.battle, playerLaneTarget: true });
    this.opponentTarget.bindBattleTarget({ battleState: this.deps.gameData.battle, playerLaneTarget: false });
    for (const target of [this.playerTarget, this.opponentTarget]) {
      target.name = 'sk';
      target.anchorX = 0.5;
      target.anchorY = 1;
      target.pos(45, 70); // hu[59], hu[24]
    }
    this.end1.addChild(this.playerTarget);
    this.end2.addChild(this.opponentTarget);
    this.end1.visible = false;
    this.end2.visible = false;

    // TODO_UNVERIFIED：原 tm 目标控制组件尚未恢复；不创建无依据替代组件。
  }


  purchaseAndPlace(slot, gridX, gridY) {
    if (!this.deps.inputController) throw new Error('BattleInputController is not configured');
    const { BattleInputCommand, BattleInputCommandType } = require('../input/BattleInputCommand');
    const result = this.deps.inputController.execute(new BattleInputCommand(BattleInputCommandType.PURCHASE_AND_PLACE, { side: true, slot, gridX, gridY }));
    if (result && result.success) this.deps.gameData.battle.playerPlacementComplete = true;
    return result;
  }

  mergeUnits(sourceId, targetId) {
    if (!this.deps.inputController) throw new Error('BattleInputController is not configured');
    const { BattleInputCommand, BattleInputCommandType } = require('../input/BattleInputCommand');
    return this.deps.inputController.execute(new BattleInputCommand(BattleInputCommandType.MERGE_UNITS, { sourceId, targetId }));
  }

  syncGold() { if (this.goldNumTxt) this.goldNumTxt.text = String(this.deps.gameData.battle.gold); }
  /** 原 onOpened 的进入战斗必要子集。 */
  onOpened() {
    const gameData = this.requireDependency('gameData');
    this.closed = false;
    this.gameEnded = false;
    this.battleStarted = true;
    this.mapIndex = gameData.map.mapIndex;
    this.round.text = '第1波';
    this.goldNumTxt.text = String(gameData.battle.playerRecruitCost);
    this.end1.visible = true;
    this.end2.visible = true;

    if (this.deps.audio) {
      this.deps.audio.playMusic(
        this.mapIndex === 1 || this.mapIndex === 3 ? 'bg_battleScene_3' : 'bg_battleScene_0',
      );
    }
    this.requireDependency('gameLoop').register('BattleScene', this, this.update);
    this.deps.eventBus.on(require('../core/EventBus').GameEvents.GOLD_CHANGED, this, this.syncGold);
    this.syncGold();
    if (this.deps.matchPreparation) this.deps.matchPreparation.markBattleStarted(true);
    if (this.deps.onBattleSceneOpened) this.deps.onBattleSceneOpened(this);
  }

  /** 原 update → B$；恢复首帧直接执行的视觉循环。 */
  update(deltaMs) {
    this.fixedUpdateCount += 1;
    if (this.shovelAdBg) {
      if (this.shovelAdBg.alpha >= 1) this.shovelPulseDirection = -1;
      if (this.shovelAdBg.alpha <= 0) this.shovelPulseDirection = 1;
      this.shovelAdBg.alpha += this.shovelPulseDirection * deltaMs / 300;
    }
    if (this.adLight) {
      this.adLight.rotation += 1;
      if (this.adLight.rotation >= 360) this.adLight.rotation = 0;
    }
    this.firstFrameExecuted = true;
  }

  onRoundStarted() { this.round.text = `第${this.deps.gameData.battle.currentRound}波`; }
  onEnemyCreated() { this.enemyCreatedCount += 1; }
  pause() { return this.deps.sceneManager.openDialog('PauseDialog'); }

  /** 原 BattleScene.gameOver 的关键清理子集。 */
  gameOver() {
    if (this.gameEnded) return;
    this.gameEnded = true;
    this.battleStarted = false;
    if (this.deps.inputController && typeof this.deps.inputController.cancelDrag === 'function') this.deps.inputController.cancelDrag();
    if (this.deps.placementReservations) this.deps.placementReservations.clear();
    if (this.deps.skillPresentation) this.deps.skillPresentation.gameOver();
    if (this.deps.mapTileManager) this.deps.mapTileManager.gameOver();
    this.deps.gameLoop.unregister('BattleScene');
    this.deps.laya.timer.clearAll(this);
    if (this.deps.matchPreparation) this.deps.matchPreparation.markBattleStarted(false);
    this.deps.sceneManager.closeScene('BattleScene', false);
    this.end1.visible = false;
    this.end2.visible = false;
  }

  onClosed() {
    this.closed = true;
    this.battleStarted = false;
    this.deps.gameLoop.unregister('BattleScene');
    this.deps.laya.timer.clearAll(this);
    this.deps.eventBus.offAllCaller(this);
    if (this.playerTarget) this.playerTarget.offAllCaller(this);
    if (this.opponentTarget) this.opponentTarget.offAllCaller(this);
    if (this.deps.matchPreparation) this.deps.matchPreparation.markBattleStarted(false);

    for (const [node, handler] of [
      [this.shovelAd, this.onShovelClick],
      [this.refreshBtn, this.onRefreshClick],
      [this.deckBtn, this.onDeckClick],
      [this.xBtn, this.pause],
    ]) if (node) node.off(this.deps.laya.Event.CLICK, this, handler);
  }

  onShovelClick() {
    if (!this.deps.shovelAction) throw new Error('Battle shovel action is deferred and not configured');
    return this.deps.shovelAction();
  }
  onRefreshClick() {
    if (!this.deps.refreshAction) throw new Error('Battle refresh action is deferred and not configured');
    return this.deps.refreshAction();
  }
  onDeckClick() {
    if (!this.deps.deckAction) throw new Error('Battle deck action is deferred and not configured');
    return this.deps.deckAction();
  }
}

BattleSceneController.dependencies = {
  laya: null,
  gameLoop: null,
  sceneManager: null,
  eventBus: null,
  gameData: null,
  audio: null,
  animationEntityPool: null,
  placementReservations: null,
  matchPreparation: null,
  onBattleSceneOpened: null,
  shovelAction: null,
  refreshAction: null,
  deckAction: null,
  skillPresentation: null,
  mapTileManager: null,
  inputController: null,
  deckManager: null,
  economy: null,
};

module.exports = { BattleSceneController };
