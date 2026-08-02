'use strict';
const { SCENE_CATALOG } = require('./SceneCatalog');
const { PREFAB_CATALOG } = require('./PrefabCatalog');
const { SPINE_CATALOG } = require('./SpineCatalog');
const { hasImage } = require('./ImageCatalog');
const { createLayaSpineAnimation } = require('../presentation/LayaSpineAnimation');
const { LayaPrefabFactory } = require('../presentation/LayaPrefabFactory');

const CRITICAL_SCENES = Object.freeze(['LoadScene','MainScene','MatchScene','BattleScene','GameOverScene']);
const CRITICAL_PREFABS = Object.freeze(['mob','boss','mapItem','heart','trail','knifeHit','bowHit','pikeHit','cavalryHit','lvlUpEff','lvlDownEff','loveHeart']);
const CRITICAL_SPINES = Object.freeze(['aDou','boss0','boss1','boss2','huaXiong','lvBu','dongZhuo','dancer']);

class OriginProjectRuntime {
  constructor({ Laya, assetPrefix = '', logger = console } = {}) {
    if (!Laya || !Laya.loader) throw new TypeError('OriginProjectRuntime requires Laya.loader');
    this.Laya = Laya;
    this.assetPrefix = assetPrefix;
    this.logger = logger;
    this.prefabs = new LayaPrefabFactory({ Laya, pathPrefix: assetPrefix });
  }
  get sceneCatalog() { return SCENE_CATALOG; }
  get prefabCatalog() { return PREFAB_CATALOG; }
  get spineCatalog() { return SPINE_CATALOG; }
  has(runtimePath) {
    if (!runtimePath) return false;
    if (hasImage(runtimePath)) return true;
    if (Object.values(SPINE_CATALOG).some(entry => entry.path === runtimePath || entry.atlasPath === runtimePath || entry.texturePath === runtimePath)) return true;
    if (Object.values(PREFAB_CATALOG).some(entry => entry.path === runtimePath)) return true;
    if (Object.values(SCENE_CATALOG).some(entry => entry.path === runtimePath)) return true;
    return false;
  }
  resolvePath(path) { return `${this.assetPrefix}${path}`; }
  resolveSpine(key) {
    const entry = SPINE_CATALOG[key];
    if (!entry) throw new Error(`Unknown Spine resource: ${key}`);
    return entry;
  }
  createSpine(keyOrPath) {
    const entry = SPINE_CATALOG[keyOrPath];
    return createLayaSpineAnimation(this.Laya, this.resolvePath(entry ? entry.path : keyOrPath));
  }
  validateAnimation(key, animationName) {
    const entry = this.resolveSpine(key);
    if (!entry.animations.includes(animationName)) {
      throw new Error(`Spine ${key} does not contain animation ${animationName}`);
    }
    return true;
  }
  async preloadCritical() {
    const paths = [
      ...CRITICAL_SCENES.map(key => SCENE_CATALOG[key].path),
      ...CRITICAL_PREFABS.map(key => PREFAB_CATALOG[key].path),
      ...CRITICAL_SPINES.map(key => SPINE_CATALOG[key].path),
      'data/weapon.json','data/weaponTxt.json','data/rank.json','data/rankData.json'
    ];
    return this.Laya.loader.load(paths.map(path => this.resolvePath(path)));
  }
  configureAnimationEntityPool(pool, { isFrameAnimation = () => false, createFrameAnimation } = {}) {
    return pool.configure({
      laya: this.Laya,
      isFrameAnimation,
      createFrameAnimation: createFrameAnimation || (id => { throw new Error(`Frame animation ${id} has no configured factory`); }),
      createSkeletonAnimation: (resourcePath, animationId) => {
        const entry = SPINE_CATALOG[animationId];
        return createLayaSpineAnimation(this.Laya, this.resolvePath(entry ? entry.path : resourcePath));
      },
    });
  }
}
module.exports = { OriginProjectRuntime, CRITICAL_SCENES, CRITICAL_PREFABS, CRITICAL_SPINES };
