'use strict';
const { PREFAB_CATALOG } = require('../resources/PrefabCatalog');

class LayaPrefabFactory {
  constructor({ Laya, catalog = PREFAB_CATALOG, pathPrefix = '' } = {}) {
    if (!Laya || !Laya.loader) throw new TypeError('LayaPrefabFactory requires Laya.loader');
    this.Laya = Laya;
    this.catalog = catalog;
    this.pathPrefix = pathPrefix;
  }
  resolve(key) {
    const entry = this.catalog[key] || this.catalog[`dialog:${key}`];
    if (!entry) throw new Error(`Unknown origin prefab: ${key}`);
    return { ...entry, resolvedPath: `${this.pathPrefix}${entry.path}` };
  }
  async preload(keys = Object.keys(this.catalog)) {
    const paths = keys.map(key => this.resolve(key).path);
    return this.Laya.loader.load(paths.map(path => `${this.pathPrefix}${path}`));
  }
  getResource(key) {
    const entry = this.resolve(key);
    return this.Laya.loader.getRes(entry.resolvedPath);
  }
  createSync(key) {
    const entry = this.resolve(key);
    const prefab = this.Laya.loader.getRes(entry.resolvedPath);
    if (!prefab || typeof prefab.create !== 'function') {
      throw new Error(`Prefab ${entry.resolvedPath} is not preloaded or has no create()`);
    }
    const node = prefab.create();
    node.originPrefabKey = key;
    node.originPrefabPath = entry.resolvedPath;
    return node;
  }
  async create(key) {
    const entry = this.resolve(key);
    let prefab = this.Laya.loader.getRes(entry.resolvedPath);
    if (!prefab) prefab = await this.Laya.loader.load(entry.resolvedPath);
    if (!prefab || typeof prefab.create !== 'function') throw new Error(`Failed to load prefab ${entry.resolvedPath}`);
    const node = prefab.create();
    node.originPrefabKey = key;
    node.originPrefabPath = entry.resolvedPath;
    return node;
  }
}
module.exports = { LayaPrefabFactory };
