'use strict';

const { SceneControllerBase } = require('./SceneControllerBase');

/**
 * 重建模块：SCENE-LOAD-01
 * 原始范围：bundle.strings-decoded.js:50996-51270
 * 原始主要符号：s3
 * UUID：nFCDlT3GRD-9N62vwVVE4Q
 * 重建状态：PARTIAL_CRITICAL_PATH_IMPLEMENTATION
 *
 * 平台绕过由注入的 startupPolicy 决定；本类不读取 DevelopmentConfig，
 * 也不包含 directBattle 分支。开发直达战斗由 DevelopmentBootstrap 的
 * 临时 $_main_ 入口执行。
 */
class LoadSceneController extends SceneControllerBase {
  constructor(...args) {
    super(...args);
    this.unusedText = '';           // 原 rQ
    this.resourceProgress = 0;      // 原 oQ
    this.startupProgress = 0;       // 原 lQ
    this.statusText = '资源加载中'; // 原 cQ
    this.statusDotCount = 0;        // 原 uQ
    this.startupPromise = null;
    this.completed = false;
    this._statusDotTick = this._advanceStatusDots.bind(this);
    this._startupProgressTick = this._advanceStartupProgress.bind(this);
  }

  /** 原始方法：onAwake，bundle.strings-decoded.js:51036-51054。 */
  onAwake() {
    const laya = this.requireDependency('laya');
    const platform = this.requireDependency('platform');
    const gameLoop = this.requireDependency('gameLoop');
    const networkData = this.requireDependency('networkData');
    const progressBar = this.requireNode('progressBar');
    this.requireNode('loadingTxt');
    const zhao = this.requireNode('zhao');

    platform.initialize();
    gameLoop.init();
    this.requireDependency('loadingEffects').init();
    networkData.init(platform.getChannelAppId());

    this.progressMask = new laya.Sprite();
    progressBar.mask = this.progressMask;
    this.progressMask.graphics.drawRect(0, 0, 0, progressBar.height, '#fff');
    this.deps.loadingEffects.animateLoading(zhao, [
      'resources/loading/zhao0.png',
      'resources/loading/zhao1.png',
      'resources/loading/zhao2.png',
    ], 100);

    this.updateStatusText();
    this.statusText = '分包加载中';
    this.statusDotCount = 0;
    this.updateStatusText();
    this.startStatusDots();
    this.startupPromise = this.startLoadFlow();
  }

  /** 原始方法：gQ，bundle.strings-decoded.js:51188-51216。 */
  async startLoadFlow() {
    this.startupProgress = 0;
    this.drawProgress();
    try {
      await this.deps.platform.preload((loaded, total) => this.updatePlatformProgress(loaded, total));
    } catch (error) {
      // CONFIRMED：平台预加载失败只记录，继续进入资源与数据初始化。
      this.deps.logger.warn('[LoadScene] preload platform tasks failed', error);
    }

    this.startupProgress = 1;
    this.drawProgress();
    this.statusText = '资源加载中';
    this.statusDotCount = 0;
    this.updateStatusText();
    this.resourceProgress = 0;
    this.startupProgress = 0;
    this.drawProgress();

    await this.deps.resourceLoader.load(this.deps.resourceManifest || [], progress => {
      this.updateResourceProgress(progress);
    });
    return this.initializePlatformAndData();
  }

  /** 原始方法：mQ，bundle.strings-decoded.js:51136-51160。 */
  async initializePlatformAndData() {
    const laya = this.requireDependency('laya');
    const policy = this.requireDependency('startupPolicy');
    this.resourceProgress = 1;
    this.startupProgress = 0;
    this.statusText = '平台初始化中';
    this.statusDotCount = 0;
    this.updateStatusText();
    this.drawProgress();
    this.deps.gameData.init();
    laya.timer.loop(80, this, this._startupProgressTick);

    try {
      if (policy.shouldSkipPlatformLogin()) this.deps.networkData.recordSkippedLogin();
      else await this.loginOrContinue();

      if (policy.shouldUseLocalPlayerData()) this.deps.networkData.recordSkippedCloudSync();
      else this.synchronizeGameData();

      this.deps.networkData.finalizeLoadedPlayerData();
    } catch (error) {
      // CONFIRMED：登录/启动平台任务失败后继续使用本地数据。
      this.deps.logger.warn('[LoadScene] startup platform tasks failed', error);
    } finally {
      laya.timer.clear(this, this._startupProgressTick);
    }

    this.startupProgress = 1;
    this.drawProgress();
    return this.onComplete();
  }

  /** 原始方法：kQ。 */
  async loginOrContinue() {
    this.statusText = '登录中';
    this.statusDotCount = 0;
    this.updateStatusText();
    const success = await this.deps.networkData.waitForLogin(this.deps.platform.login());
    if (!success) this.deps.logger.warn('[LoadScene] 登录超时或失败，将使用本地数据继续');
    return success;
  }

  /** 原始方法：SQ。 */
  synchronizeGameData() {
    this.statusText = '同步游戏数据';
    this.statusDotCount = 0;
    this.updateStatusText();
    return this.deps.networkData.synchronizeCloudSaveAfterLogin();
  }

  /** 原始方法：onComplete，bundle.strings-decoded.js:51057-51072。 */
  async onComplete() {
    const policy = this.requireDependency('startupPolicy');
    if (policy.shouldSkipRemoteShareConfig()) this.deps.networkData.recordSkippedRemoteShareConfig();
    else await this.deps.networkData.loadRemoteShareConfig();

    this.deps.battleFlow.init();

    const directMatch = Boolean(
      policy.shouldForceMatchLaunch() || this.deps.platform.shouldEnterMatchDirectly(),
    );
    if (directMatch) this.deps.matchPreparation.prepareBeforeMatch();
    const scene = await this.deps.sceneManager.openSceneAndWait(
      directMatch ? 'MatchScene' : 'MainScene',
      true,
      null,
    );
    this._finishLoadingScene();
    return scene;
  }

  updatePlatformProgress(loaded, total) {
    this.startupProgress = total <= 0 ? 1 : Math.min(1, loaded / total);
    this.drawProgress();
  }

  updateResourceProgress(progress) {
    this.resourceProgress = Number.isFinite(progress) ? Math.max(0, Math.min(1, progress)) : 0;
    this.drawProgress();
  }

  startStatusDots() { this.deps.laya.timer.loop(500, this, this._statusDotTick); }
  _advanceStatusDots() { this.statusDotCount = (this.statusDotCount + 1) % 4; this.updateStatusText(); }
  _advanceStartupProgress() { this.startupProgress = Math.min(0.95, this.startupProgress + 0.02); this.drawProgress(); }

  updateStatusText() {
    if (this.loadingTxt) this.loadingTxt.text = `${this.statusText}${'.'.repeat(this.statusDotCount)}`;
  }

  drawProgress() {
    if (!this.progressBar || !this.progressMask) return;
    const value = 0.85 * this.resourceProgress + 0.15 * this.startupProgress;
    this.progressMask.graphics.clear();
    this.progressMask.graphics.drawRect(0, 0, this.progressBar.width * value, this.progressBar.height, '#fff');
    if (this.zhao) this.zhao.x = this.progressBar.width * value;
  }

  onClosed() {
    this.deps.laya.timer.clear(this, this._statusDotTick);
    this.deps.laya.timer.clear(this, this._startupProgressTick);
  }

  _finishLoadingScene() {
    this.completed = true;
    this.onClosed();
    // index.js 直接打开 LoadScene，通常未进入 SceneManager 缓存。
    if (!this.destroyed && typeof this.destroy === 'function') this.destroy(true);
  }
}

LoadSceneController.dependencies = {
  laya: null,
  platform: null,
  gameLoop: null,
  loadingEffects: null,
  networkData: null,
  gameData: null,
  battleFlow: null,
  sceneManager: null,
  resourceLoader: null,
  resourceManifest: null,
  startupPolicy: null,
  matchPreparation: null,
  logger: console,
};

module.exports = { LoadSceneController };
