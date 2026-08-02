'use strict';

const { GameConfig } = require('../config/GameConfig');

/**
 * 重建模块：BOOT-INDEX-01
 * 原始范围：original/index.js:1-68
 * 原始主要符号：顶层匿名 IIFE
 * 重建状态：COMPLETE
 */
class GameBootstrap {
  constructor({ Laya, config = GameConfig, windowRef = globalThis, documentRef = globalThis.document } = {}) {
    if (!Laya) throw new TypeError('GameBootstrap requires Laya');
    this.Laya = Laya;
    this.config = config;
    this.windowRef = windowRef;
    this.documentRef = documentRef;
  }

  applyConfiguration() {
    const { Laya, config } = this;
    Object.assign(Laya.PlayerConfig, config);
    Object.assign(Laya.Config, config['2D']);
    Object.assign(Laya.Config3D, config['3D']);
    if (Laya.UIConfig2) Object.assign(Laya.UIConfig2, config.UI);

    const cluster = Laya.Config3D.lightClusterCount;
    Laya.Config3D.lightClusterCount = new Laya.Vector3(cluster.x, cluster.y, cluster.z);
    if (config.useSafeFileExtensions) Laya.URL.initMiniGameExtensionOverrides();
  }

  configurePackages() {
    const { Laya, config } = this;
    const autoLoadPackages = [];
    for (const pkg of config.pkgs) {
      const path = pkg.path.length > 0 ? `${pkg.path}/` : pkg.path;
      if (pkg.hash != null) Laya.URL.version[`${path}fileconfig.json`] = pkg.hash;
      if (pkg.remoteUrl) {
        const remoteUrl = pkg.remoteUrl.endsWith('/') ? pkg.remoteUrl : `${pkg.remoteUrl}/`;
        if (path.length > 0) Laya.URL.basePaths[path] = remoteUrl;
        else Laya.URL.basePath = remoteUrl;
      }
      if (pkg.autoLoad) autoLoadPackages.push(pkg);
    }

    Laya.addBeforeInitCallback(() => {
      if (config.vConsole && Laya.Browser.onMobile && Laya.Browser.isDomSupported) {
        if (!this.documentRef) throw new Error('document is required when vConsole is enabled');
        const script = this.documentRef.createElement('script');
        script.src = 'js/vConsole.min.js';
        script.onload = () => {
          this.windowRef.vConsole = new this.windowRef.VConsole();
        };
        this.documentRef.body.appendChild(script);
      }
      if (config.alertGlobalError) Laya.alertGlobalError(true);
      return Promise.all(autoLoadPackages.map(pkg => Laya.loader.loadPackage(pkg.path, pkg.remoteUrl)));
    });
  }

  /**
   * 原始 index.js Promise 链：init → $_main_ 或 startupScene → hide splash。
   * 初始化异常只记录，不重新抛出，保持原链行为。
   */
  async start() {
    const { Laya, config } = this;
    this.applyConfiguration();
    this.configurePackages();
    let opened = null;
    try {
      await Laya.init(config.resolution);
      if (config.stat) Laya.Stat.show();
      if (this.windowRef.$_main_) {
        opened = await this.windowRef.$_main_();
      } else if (config.startupScene) {
        opened = await Laya.Scene.open(config.startupScene, true, null, progress => {
          if (this.windowRef.onSplashProgress) this.windowRef.onSplashProgress(progress);
        });
      }
    } catch (error) {
      this.initializationError = error;
      console.error('Initialization failed:\n', error);
    }
    if (this.windowRef.hideSplashScreen) this.windowRef.hideSplashScreen();
    return opened;
  }
}

module.exports = { GameBootstrap };
