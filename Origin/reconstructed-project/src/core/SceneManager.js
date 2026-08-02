'use strict';

const { SingletonBase } = require('./SingletonBase');

/**
 * 重建模块：SCENE-MGR-01
 * 原始范围：bundle.strings-decoded.js:5715-5869；别名绑定 13165
 * 原始主要符号：q3；闭包别名 sF
 * 重建状态：COMPLETE_FOR_CRITICAL_PATH
 */
class SceneManager extends SingletonBase {
  constructor() {
    super();
    this.laya = null;
    this.init();
    this.lastOpenPromise = Promise.resolve(null);
    this.sceneDialogNames = new Map([
      ['MainScene', ['GetStaminaDialog', 'SidebarDialog']],
      ['BattleScene', ['DeckDialog', 'PauseDialog', 'BossTipDialog']],
      ['GameOverScene', ['ShareLpDialog']],
    ]);
  }

  configure({ laya, Laya } = {}) {
    const runtime = laya || Laya;
    if (!runtime || !runtime.Scene || !runtime.Dialog || !runtime.stage) {
      throw new TypeError('SceneManager requires Laya.Scene, Laya.Dialog and Laya.stage');
    }
    this.laya = runtime;
    return this;
  }

  init() {
    this.scenes = new Map();
    this.dialogs = new Map();
    this.dialogParams = new Map();
  }

  /** 原 xn。 */
  closeOtherMajorScenes(exceptName) {
    for (const [name, scene] of this.scenes) {
      if (name !== exceptName && scene.parent && typeof scene.close === 'function') scene.close();
    }
  }

  /** 原 bn：刻意不返回 Promise。 */
  openScene(name, closeOther = false, params, onComplete) {
    const laya = this._requireLaya();
    const major = SceneManager.MAJOR_SCENES.has(name);
    const cached = this.scenes.get(name);
    if (cached) {
      if (major) this.closeOtherMajorScenes(name);
      const result = typeof cached.open === 'function' ? cached.open(closeOther, params) : cached;
      this.lastOpenPromise = Promise.resolve(result).then(() => {
        if (onComplete) onComplete(cached);
        return cached;
      });
      return;
    }

    this.lastOpenPromise = Promise.resolve(
      laya.Scene.open(`scene/${name}.ls`, closeOther, params),
    ).then(scene => {
      this.scenes.set(name, scene);
      if (major) this.closeOtherMajorScenes(name);
      if (onComplete) onComplete(scene);
      return scene;
    });
  }

  openSceneAndWait(name, closeOther = false, params) {
    return new Promise((resolve, reject) => {
      try {
        this.openScene(name, closeOther, params, resolve);
        this.lastOpenPromise.catch(reject);
      } catch (error) { reject(error); }
    });
  }

  whenLastOpenCompletes() { return this.lastOpenPromise; }

  closeScene(name, closeScene = true) {
    this.closeSceneDialogs(name);
    const scene = this.scenes.get(name);
    if (closeScene && scene && typeof scene.close === 'function') scene.close();
  }

  getScene(name) { return this.scenes.get(name); }

  openDialog(name, closeOther = true, params) {
    const laya = this._requireLaya();
    if (params !== undefined) this.dialogParams.set(name, params);
    return Promise.resolve(laya.Dialog.open(`dialog/${name}.lh`, closeOther, params)).then(dialog => {
      this.dialogs.set(name, dialog);
      return dialog;
    });
  }

  closeDialog(name) {
    const dialog = this.dialogs.get(name);
    if (dialog && typeof dialog.close === 'function') dialog.close();
  }

  closeSceneDialogs(sceneName) {
    const names = this.sceneDialogNames.get(sceneName);
    if (!names) return;
    for (const name of names) this.closeDialog(name);
  }

  getDialogParams(name) { return this.dialogParams.get(name); }
  clearDialogParams(name) { this.dialogParams.delete(name); }

  _requireLaya() {
    if (!this.laya) throw new Error('SceneManager.configure() must run first');
    return this.laya;
  }
}

SceneManager.MAJOR_SCENES = new Set(['MainScene', 'MatchScene', 'BattleScene', 'GameOverScene']);
SceneManager.DESIGN_HEIGHT = 1386;

module.exports = { SceneManager };
