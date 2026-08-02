'use strict';

/**
 * Laya 场景控制器公共基类。
 * 生产环境要求 LayaAir 在业务脚本之前加载；测试在 require 前安装 Laya mock。
 */
const LayaSceneBase = globalThis.Laya && globalThis.Laya.Scene
  ? globalThis.Laya.Scene
  : class MissingLayaSceneBase {
      constructor() {
        throw new Error('Laya.Scene is not available while loading reconstructed scene controllers');
      }
    };

class SceneControllerBase extends LayaSceneBase {
  constructor(...args) {
    super(...args);
    this.deps = this.constructor.dependencies || {};
  }
  requireDependency(name) {
    const value = this.deps[name];
    if (value === null || value === undefined) throw new Error(`${this.constructor.name} dependency is not configured: ${name}`);
    return value;
  }
  requireNode(name) {
    const node = this[name];
    if (!node) throw new Error(`${this.constructor.name}.${name} is required; scene .ls binding is missing`);
    return node;
  }
  static configureDependencies(dependencies = {}) {
    this.dependencies = { ...(this.dependencies || {}), ...dependencies };
  }
}

module.exports = { SceneControllerBase };
