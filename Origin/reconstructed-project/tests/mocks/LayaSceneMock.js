'use strict';

const { LayaTimerMock } = require('./LayaTimerMock');

class GraphicsMock {
  constructor() { this.commands = []; }
  clear() { this.commands.length = 0; }
  drawRect(...args) { this.commands.push(['drawRect', ...args]); return this; }
}

class NodeMock {
  constructor() {
    this.name = '';
    this.parent = null;
    this.children = [];
    this.visible = true;
    this.alpha = 1;
    this.x = 0;
    this.y = 0;
    this.width = 0;
    this.height = 0;
    this.scaleX = 1;
    this.scaleY = 1;
    this.rotation = 0;
    this.graphics = new GraphicsMock();
    this._events = new Map();
    this.destroyed = false;
    this.closed = false;
    this.lifecycle = [];
  }
  get numChildren() { return this.children.length; }
  on(type, caller, method) {
    const list = this._events.get(type) || [];
    list.push({ caller, method });
    this._events.set(type, list);
    return this;
  }
  once(type, caller, method) {
    const wrapper = (...args) => { this.off(type, caller, wrapper); method.apply(caller, args); };
    return this.on(type, caller, wrapper);
  }
  off(type, caller, method) {
    const list = this._events.get(type) || [];
    this._events.set(type, list.filter(e => !(e.caller === caller && e.method === method)));
    return this;
  }
  offAll(type = null) {
    if (type == null) this._events.clear();
    else this._events.delete(type);
    return this;
  }
  offAllCaller(caller) {
    for (const [type, list] of this._events) this._events.set(type, list.filter(e => e.caller !== caller));
    return this;
  }
  event(type, ...args) {
    const listeners = (this._events.get(type) || []).slice();
    for (const e of listeners) e.method.apply(e.caller, args);
    return listeners.length > 0;
  }
  listenerCount(type = null) {
    if (type != null) return (this._events.get(type) || []).length;
    let total = 0;
    for (const list of this._events.values()) total += list.length;
    return total;
  }
  addChild(child) {
    if (!child) throw new Error('Cannot add an empty child');
    if (child.parent) child.removeSelf();
    child.parent = this;
    this.children.push(child);
    return child;
  }
  removeChild(child) {
    const index = this.children.indexOf(child);
    if (index >= 0) this.children.splice(index, 1);
    if (child) child.parent = null;
    return child;
  }
  removeSelf() { if (this.parent) this.parent.removeChild(this); return this; }
  getChildByName(name) { return this.children.find(c => c.name === name) || null; }
  getChildAt(index) { return this.children[index] || null; }
  size(width, height) { this.width = width; this.height = height; return this; }
  pos(x, y) { this.x = x; this.y = y; return this; }
  pivot(x, y) { this.pivotX = x; this.pivotY = y; return this; }
  anchor(x, y) { this.anchorX = x; this.anchorY = y; return this; }
  scale(x, y) { this.scaleX = x; this.scaleY = y; return this; }
  addComponent(Type) { const component = new Type(); component.owner = this; return component; }
  open(_closeOther, params) { this.closed = false; if (typeof this.onOpened === 'function') this.onOpened(params); return this; }
  close() {
    if (this.closed) return;
    this.closed = true;
    if (typeof this.onClosed === 'function') { this.lifecycle.push('onClosed'); this.onClosed(); }
    this.removeSelf();
  }
  destroy(destroyChildren = true) {
    if (this.destroyed) return;
    if (!this.closed && typeof this.onClosed === 'function') { this.lifecycle.push('onClosed'); this.onClosed(); }
    this.destroyed = true;
    this.closed = true;
    this.removeSelf();
    if (destroyChildren) {
      for (const child of this.children.slice()) if (child.destroy) child.destroy(true);
      this.children.length = 0;
    }
    this._events.clear();
  }
}

function createLayaSceneMock() {
  const timer = new LayaTimerMock();
  const beforeInitCallbacks = [];
  const classRegistry = new Map();
  const factories = new Map();
  const activeScenes = new Map();
  const activeDialogs = new Map();
  const calls = [];

  class Sprite extends NodeMock {}
  class Image extends Sprite { constructor(skin = '') { super(); this.skin = skin; } }
  class Text extends Sprite { constructor() { super(); this.text = ''; } }
  class Point {
    constructor(x = 0, y = 0) { this.x = x; this.y = y; this.__InPool = false; }
    setTo(x, y) { this.x = x; this.y = y; return this; }
    copy(value) { this.x = value.x; this.y = value.y; return this; }
    clone() { return new Point(this.x, this.y); }
    recover() { this.x = 0; this.y = 0; this.__InPool = true; return this; }
    static create() { return new Point(); }
  }
  Point.TEMP = new Point();

  const tweenCalls = [];
  const Tween = {
    to(target, properties, duration, ease = null) {
      tweenCalls.push(['to', target, { ...properties }, duration, ease]);
      Object.assign(target, properties);
      return target;
    },
    create(target) {
      const state = { target, property: null, value: null, duration: 0, ease: null };
      return {
        to(property, value) { state.property = property; state.value = value; return this; },
        duration(value) { state.duration = value; return this; },
        ease(value) {
          state.ease = value;
          if (state.property != null) state.target[state.property] = state.value;
          tweenCalls.push(['create', state.target, state.property, state.value, state.duration, state.ease]);
          return this;
        },
      };
    },
    killAll(target) { tweenCalls.push(['killAll', target]); },
  };
  const stage = new Sprite();
  stage.name = 'stage'; stage.width = 640; stage.height = 1386;

  class Scene extends Sprite {
    static registerFactory(path, factory) { factories.set(path, factory); }
    static async open(path, closeOther = false, params = null, progress = null) {
      calls.push(['Scene.open', path, closeOther, params]);
      if (progress) progress(0);
      const factory = factories.get(path);
      if (!factory) throw new Error(`No mock scene factory registered for ${path}`);
      if (closeOther) for (const scene of Array.from(activeScenes.values())) scene.close();
      const scene = factory();
      scene.url = path;
      stage.addChild(scene);
      const name = path.split('/').pop().replace(/\.ls$/, '');
      activeScenes.set(name, scene);
      for (const lifecycleName of ['onAwake','onEnable','onStart','onOpened']) {
        if (typeof scene[lifecycleName] === 'function') {
          scene.lifecycle.push(lifecycleName);
          if (lifecycleName === 'onOpened') scene[lifecycleName](params);
          else scene[lifecycleName]();
        }
      }
      if (progress) progress(1);
      return scene;
    }
    close() {
      super.close();
      for (const [name, scene] of activeScenes) if (scene === this) activeScenes.delete(name);
    }
    destroy(destroyChildren = true) {
      super.destroy(destroyChildren);
      for (const [name, scene] of activeScenes) if (scene === this) activeScenes.delete(name);
    }
  }

  class Dialog extends Scene {
    static async open(path, closeOther = true, params = null) {
      calls.push(['Dialog.open', path, closeOther, params]);
      if (closeOther) for (const dialog of Array.from(activeDialogs.values())) dialog.close();
      const dialog = new Dialog();
      dialog.url = path;
      dialog.name = path.split('/').pop().replace(/\.lh$/, '');
      stage.addChild(dialog);
      activeDialogs.set(dialog.name, dialog);
      if (typeof dialog.onAwake === 'function') dialog.onAwake();
      if (typeof dialog.onOpened === 'function') dialog.onOpened(params);
      return dialog;
    }
    close() {
      super.close();
      for (const [name, dialog] of activeDialogs) if (dialog === this) activeDialogs.delete(name);
    }
  }

  const poolBuckets = new Map();
  const Pool = {
    getItemByCreateFun(sign, createFun, caller = null) {
      const bucket = poolBuckets.get(sign) || [];
      poolBuckets.set(sign, bucket);
      const value = bucket.length > 0 ? bucket.pop() : createFun.call(caller);
      value.__InPool = false;
      return value;
    },
    recover(sign, value) {
      if (!value || value.__InPool) return;
      const bucket = poolBuckets.get(sign) || [];
      poolBuckets.set(sign, bucket);
      value.__InPool = true;
      bucket.push(value);
    },
    getPoolBySign(sign) {
      const bucket = poolBuckets.get(sign) || [];
      poolBuckets.set(sign, bucket);
      return bucket;
    },
    clearBySign(sign) { poolBuckets.set(sign, []); },
  };

  const Laya = {
    PlayerConfig: {}, Config: {}, Config3D: { lightClusterCount: { x: 12, y: 12, z: 12 } }, UIConfig2: {},
    URL: { version: {}, basePaths: {}, basePath: '', initMiniGameExtensionOverrides() { calls.push(['URL.initMiniGameExtensionOverrides']); } },
    Browser: { onMobile: false, isDomSupported: false },
    Event: { CLICK: 'click', COMPLETE: 'complete', ERROR: 'error', STOPPED: 'stopped' },
    Sprite, Image, Text, Point, Scene, Dialog, stage, timer, Pool, Tween,
    Ease: { linearInOut: 'linearInOut' },
    MathUtil: { lerp(from, to, amount) { return from + (to - from) * amount; } },
    loader: { loading: false, loadPackage(path, remoteUrl) { calls.push(['loader.loadPackage', path, remoteUrl]); return Promise.resolve(); } },
    Handler: { create(caller, method, args = null, once = true) { return { once, run: () => method.apply(caller, args || []), runWith: value => method.apply(caller, args ? [...args, value] : [value]) }; } },
    Vector3: class Vector3 { constructor(x, y, z) { this.x = x; this.y = y; this.z = z; } },
    Stat: { show() { calls.push(['Stat.show']); } },
    addBeforeInitCallback(callback) { beforeInitCallbacks.push(callback); },
    async init(resolution) { calls.push(['Laya.init', { ...resolution }]); for (const cb of beforeInitCallbacks) await cb(); return Laya; },
    alertGlobalError(value) { calls.push(['alertGlobalError', value]); },
    regClass(uuid) { return ClassType => { classRegistry.set(uuid, ClassType); return ClassType; }; },
    __mock: {
      calls, factories, activeScenes, activeDialogs, classRegistry, poolBuckets, tweenCalls,
      getScene(name) { return activeScenes.get(name) || null; },
      getDialog(name) { return activeDialogs.get(name) || null; },
    },
  };
  return Laya;
}

const createLayaMock = createLayaSceneMock;
module.exports = { GraphicsMock, NodeMock, createLayaSceneMock, createLayaMock };
