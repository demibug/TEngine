'use strict';

/** DEVELOPMENT_ONLY：资源清单由调用方显式提供，不访问网络。 */
class DevelopmentResourceLoader {
  constructor(options = {}) {
    this.options = { fail: false, ...options };
    this.calls = [];
  }

  async load(resources, onProgress) {
    const manifest = Array.isArray(resources) ? resources.slice() : resources;
    this.calls.push(['load', manifest]);
    if (this.options.fail) throw new Error('DevelopmentResourceLoader configured failure');
    if (onProgress) {
      onProgress(0);
      onProgress(0.5);
      onProgress(1);
    }
    return manifest;
  }
}

/** DEVELOPMENT_ONLY：只记录真实场景控制器前后的转场边界。 */
class DevelopmentSceneTransition {
  constructor() { this.calls = []; }
  async mainToMatch(scene) { this.calls.push(['mainToMatch', scene && scene.name]); }
  async matchToBattle(matchScene, battleScene) {
    this.calls.push(['matchToBattle', matchScene && matchScene.name, battleScene && battleScene.name]);
    return battleScene;
  }
}

module.exports = { DevelopmentResourceLoader, DevelopmentSceneTransition };
