'use strict';
const MapRenderer = require('../rendering/MapRenderer');

/**
 * 重建模块：BATTLE-SCENE-VISUAL 地图视觉 Laya 适配器
 * 原始范围：bundle.strings-decoded.js:57319-57329（dq 瓦片池）、57815-57843（$q mapBg）、
 *           57955-57988（Qq bound 边界）、58307-58324（Kq pathTip）、58750-58790（Vq 瓦片铺图）
 * 原始符号：r5（BattleSceneController）$q/Vq/Qq/Zq/Kq 方法的 Laya 落地部分
 * 重建状态：LAYA_ADAPTER_COMPLETE
 *
 * 依赖注入 Laya 运行时 + BattleScene 节点引用（map/road/highGround/bound/divide/
 * mapBgImg/mapBgImgNew/mapTitle/bg/pathTip0/pathTip1），将 MapRenderer 纯数据落到节点。
 * 节点缺失时抛错，与 BattleSceneController.requireNode 一致（不静默吞节点）。
 */
class LayaMapRenderer {
  /**
   * @param {object} options
   * @param {object} options.Laya          Laya 运行时（Laya.Image/Sprite/Timer/Tween）
   * @param {object} [options.itemPool]    rw.getItem("mapImg") 对象池；缺省自建 Image
   * @param {object} [options.logger]      日志器
   */
  constructor({ Laya, itemPool = null, logger = console } = {}) {
    if (!Laya) throw new TypeError('LayaMapRenderer requires Laya runtime');
    Object.assign(this, { Laya, itemPool, logger });
    this._tiles = [];      // dq 瓦片池：dq[x][y]
    this._tileIndex = new Map(); // Lq：name → tile
    this._lastMapIndex = null;
  }

  /**
   * 绑定 BattleScene 节点引用（由 BattleSceneController.onAwake 传入）。
   * @param {object} nodes  { map, road, highGround, bound, divide, bg, mapBgImg, mapBgImgNew, mapTitle, pathTip0, pathTip1 }
   */
  bindNodes(nodes) {
    const required = ['map', 'road', 'highGround', 'bound', 'divide', 'bg', 'mapTitle', 'pathTip0', 'pathTip1'];
    for (const name of required) {
      if (!nodes[name]) throw new Error(`LayaMapRenderer.bindNodes: missing required node '${name}'`);
    }
    this.nodes = nodes;
    return this;
  }

  /**
   * 预建瓦片池（bundle $q grid 布局，bundle:57319-57329）。
   * dq[x][y] = Image(80×80)，pos(x*80, y*80)，Lq.set(`${x}_${y}`)。
   * 幂等：重复调用先清理旧池。
   * @param {MapData} mapData
   */
  buildTilePool(mapData) {
    this.clearTilePool();
    const Laya = this.Laya;
    const gw = mapData.gridWidth, gh = mapData.gridHeight;
    for (let x = 0; x < mapData.width; x += 1) {
      const column = [];
      for (let y = 0; y < mapData.height; y += 1) {
        let tile;
        if (this.itemPool && typeof this.itemPool.getItem === 'function') {
          tile = this.itemPool.getItem('mapImg', this);
        } else {
          tile = new Laya.Image();
        }
        tile.name = `${x}_${y}`;
        tile.size(gw, gh);
        tile.pos(x * gw, y * gh);
        column.push(tile);
        this._tileIndex.set(tile.name, tile);
      }
      this._tiles.push(column);
    }
    return this._tiles;
  }

  /**
   * 按瓦片数据铺图（bundle Vq，bundle:58750-58790）。
   * 遍历 composeTiles 输出，把每个 tile 的 skin 设到对应 dq 瓦片，
   * 并 addChild 到 road 或 highGround 层。
   * @param {MapVisualData['tiles']} tilesData  composeTiles() 的返回值
   */
  layTiles(tilesData) {
    const { road, highGround } = this.nodes;
    for (const t of tilesData.tiles) {
      const tile = this._tileIndex.get(`${t.gridX}_${t.gridY}`);
      if (!tile) continue; // 瓦片池未建则跳过；buildTilePool 后不应到达
      tile.skin = t.skin;
      const layer = t.layer === 'road' ? road : highGround;
      if (tile.parent !== layer) layer.addChild(tile);
    }
  }

  /**
   * 绘制边界线（bundle Qq，bundle:57955-57988）。
   * bound.graphics.clear() → 逐线 drawLine → 外框 drawLine。
   * graphics 或 drawLine 缺失时跳过（dev mock 无 drawLine，不阻塞规则层）。
   * @param {MapVisualData['bound']} boundData  composeBound() 的返回值
   */
  drawBound(boundData) {
    const g = this.nodes.bound.graphics;
    if (!g) return;
    if (typeof g.clear === 'function') g.clear();
    if (typeof g.drawLine !== 'function') return; // dev mock 无 drawLine
    for (const line of boundData.lines) {
      g.drawLine(line.x1, line.y1, line.x2, line.y2, line.color, line.width);
    }
  }

  /**
   * 设置分界线（bundle:58770-58782）。
   * divide.skin/pos/size。
   * @param {MapVisualData['divide']} divideData
   */
  applyDivide(divideData) {
    const d = this.nodes.divide;
    d.skin = divideData.skin;
    d.pos(divideData.x, divideData.y);
    d.size(divideData.width, divideData.height);
  }

  /**
   * 设置背景与 mapBg（bundle $q + Vq bg，bundle:57815-57843/58770）。
   * bg.skin、mapTitle.skin、mapBgImg/mapBgImgNew 可见性与 skin 切换。
   * @param {MapVisualData} data  compose() 的返回值
   */
  applyBackground(data) {
    const { bg, mapTitle } = this.nodes;
    bg.skin = data.background.skin;
    mapTitle.skin = data.mapBg.mapTitleSkin;
    // mapBgImg/mapBgImgNew 在 mapIndex=0 时用静态 .ls 节点，其余动态切换
    if (this.nodes.mapBgImg) this.nodes.mapBgImg.visible = data.mapBg.mapBgImgVisible;
    if (this.nodes.mapBgImgNew) {
      this.nodes.mapBgImgNew.visible = data.mapBg.mapBgImgNewVisible;
      if (data.mapBg.mapBgImgNewSkin) this.nodes.mapBgImgNew.skin = data.mapBg.mapBgImgNewSkin;
    }
    // mapBg prefab（mapBg0-3.lh）由外部 prefabFactory 实例化；此处仅记 key。
    this._pendingMapBgPrefabKey = data.mapBg.mapBgPrefabKey;
  }

  /**
   * 定位 end1/end2（bundle Zq，bundle:57189-57214）。
   * 由 BattleSceneController 在创建 end 目标后调用。
   * @param {MapVisualData['ends']} endsData
   * @param {object} end1  玩家终点节点
   * @param {object} end2  对手终点节点
   */
  positionEnds(endsData, end1, end2) {
    if (end1) end1.pos(endsData.end1.x, endsData.end1.y);
    if (end2) end2.pos(endsData.end2.x, endsData.end2.y);
  }

  /**
   * 定位 pathTip0/pathTip1（bundle Kq，bundle:58307-58324）。
   * 按方向变换设置 pos/scaleX/scaleY，并延迟显示（bundle:58526 timer.once 989ms）。
   * @param {MapVisualData['pathTips']} pathTipsData
   */
  applyPathTips(pathTipsData) {
    const { pathTip0, pathTip1 } = this.nodes;
    this._applyPathTip(pathTip0, pathTipsData.pathTip0);
    this._applyPathTip(pathTip1, pathTipsData.pathTip1);
  }

  _applyPathTip(node, tip) {
    if (!node) return;
    node.pos(tip.posX, tip.posY);
    node.scaleX = tip.scaleX;
    node.scaleY = tip.scaleY;
    // bundle:58526 i$() 初始隐藏子节点，timer.once(989ms) 后由 t$() 显示
    this._hidePathTipChildren(node);
    this.Laya.timer.once(MapRenderer.PATH_TIP_DELAY_MS, this, () => {
      if (!node.destroyed) this._showPathTipChildren(node);
    });
  }

  _hidePathTipChildren(node) {
    // bundle:57946-57947 遍历子节点设 alpha=0
    for (let i = 0; i < node.numChildren; i += 1) {
      const child = node.getChildAt(i);
      if (child) child.alpha = 0;
    }
  }

  _showPathTipChildren(node) {
    for (let i = 0; i < node.numChildren; i += 1) {
      const child = node.getChildAt(i);
      if (child) child.alpha = 1;
    }
  }

  /**
   * 一次性生成并落地全部地图视觉（onOpened 调用链，bundle:58526）。
   * 顺序：buildTilePool → applyBackground → layTiles → drawBound → applyDivide → positionEnds → applyPathTips
   * 对标原始 $q()→Vq()→Qq()→Zq()→Kq() 调用链。
   * @param {MapData} mapData
   * @param {{end1?:object, end2?:object, pathTipWidth?:number, pathTipHeight?:number}} [extra]
   */
  render(mapData, extra = {}) {
    if (!this.nodes) throw new Error('LayaMapRenderer.render: call bindNodes() first');
    const data = MapRenderer.compose(mapData, {
      pathTipWidth: extra.pathTipWidth,
      pathTipHeight: extra.pathTipHeight,
    });
    // 地图切换时重建瓦片池
    if (this._lastMapIndex !== mapData.mapIndex) {
      this.buildTilePool(mapData);
      this._lastMapIndex = mapData.mapIndex;
    }
    // map 尺寸（bundle:57321 map.size(pe.length*ye, pe[0].length*gridHei) + map.x 居中）
    const mapW = mapData.width * mapData.gridWidth;
    const mapH = mapData.height * mapData.gridHeight;
    this.nodes.map.size(mapW, mapH);
    const parent = this.nodes.map.parent;
    if (parent && parent.width) this.nodes.map.x = (parent.width - mapW) / 2;

    this.applyBackground(data);
    this.layTiles(data.tiles);
    this.drawBound(data.bound);
    this.applyDivide(data.divide);
    if (extra.end1 || extra.end2) this.positionEnds(data.ends, extra.end1, extra.end2);
    this.applyPathTips(data.pathTips);
    return data;
  }

  /** 清理瓦片池（gameOver / 切地图）。 */
  clearTilePool() {
    for (const column of this._tiles) {
      for (const tile of column) {
        if (tile && tile.removeSelf) tile.removeSelf();
        if (this.itemPool && typeof this.itemPool.returnItem === 'function') this.itemPool.returnItem('mapImg', tile);
      }
    }
    this._tiles = [];
    this._tileIndex.clear();
  }

  gameOver() {
    this.clearTilePool();
    this._lastMapIndex = null;
    if (this.nodes && this.nodes.bound && this.nodes.bound.graphics) this.nodes.bound.graphics.clear();
    this.Laya.timer.clearAll(this);
  }
}

module.exports = { LayaMapRenderer };
