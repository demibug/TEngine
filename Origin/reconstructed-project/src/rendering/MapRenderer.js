'use strict';

/**
 * 重建模块：BATTLE-SCENE-VISUAL 地图视觉生成器（纯逻辑层）
 * 原始范围：bundle.strings-decoded.js:57189-57214（Zq）、57815-57843（$q mapBg）、
 *           57319-57329（dq 瓦片池）、57955-57988（Qq bound 边界）、
 *           58307-58324（Kq pathTip 变换）、58750-58790（Vq 瓦片铺图）、58770-58782（divide）
 * 原始符号：r5（BattleSceneController）的 $q/Vq/Qq/Zq/Kq 方法
 * 重建状态：PURE_LOGIC_COMPLETE
 *
 * 本模块是引擎中立的纯数据生成器：输入 MapData，输出瓦片 skin 映射、边界线段、
 * 分界线尺寸、pathTip 方向变换、mapBg 切换指令。不依赖 Laya/Unity，可单测。
 * Laya 适配见 LayaMapRenderer.js，Unity 侧照本模块 + bundle 行号注释实现 C# Presentation。
 */

// ── hu 解码常量（obfuscation-runtime.original.js:355-386 ht(hs) 解密结果） ──
// bundle:58770-58782 divide pos/size 表依赖的 hu 数组索引。
const DIVIDE_TABLE = Object.freeze({
  // case 0: divide.pos(0, hu[305]) divide.size(hu[221], hu[306])
  0: Object.freeze({ x: 0, y: 301, width: 633, height: 182 }),
  // case 1: divide.pos(0, hu[307]) divide.size(hu[308], hu[62])
  1: Object.freeze({ x: 0, y: 361, width: 652, height: 44 }),
  // case 2: divide.pos(0, hu[309]) divide.size(hu[221], hu[8])
  2: Object.freeze({ x: 0, y: 380, width: 633, height: 23 }),
  // case 3: divide.pos(0, hu[310]) divide.size(hu[311], hu[45])
  3: Object.freeze({ x: 0, y: 364, width: 637, height: 50 }),
});

// ── 瓦片编码 → skin 映射（bundle:58785 Vq 的 5 分支） ──
// code 格式 "kind_lane"：0=路径、1=建造格、2=草地；lane 0/1 区分阵营侧。
const TILE_SKIN_MAP = Object.freeze({
  // 路径格 → road 层
  '0_0': Object.freeze({ layer: 'road',   skin: (i) => `resources/img/map/road_${i}.png` }),
  '0_1': Object.freeze({ layer: 'road',   skin: (i) => `resources/img/map/road_${i}.png` }),
  // 建造格 → highGround 层（space 贴图）
  '1_0': Object.freeze({ layer: 'highGround', skin: (i) => `resources/img/map/space_${i}.png` }),
  '1_1': Object.freeze({ layer: 'highGround', skin: (i) => `resources/img/map/space_${i}.png` }),
  // 草地格 → highGround 层（grass 贴图，lane 区分 0/1 变体）
  '2_0': Object.freeze({ layer: 'highGround', skin: (i) => `resources/img/map/grass_${i}_0.png` }),
  '2_1': Object.freeze({ layer: 'highGround', skin: (i) => `resources/img/map/grass_${i}_1.png` }),
});

const WALKABLE_CODES = Object.freeze(new Set(['0_0', '0_1']));
const BUILDABLE_CODES = Object.freeze(new Set(['1_0', '1_1', '2_0', '2_1']));
const BOUND_COLOR = '#000000';
const BOUND_INNER_WIDTH = 3;  // 建造格与路径格相邻边线宽
const BOUND_FRAME_WIDTH = 6;  // 地图外框线宽
// bundle:58526 Laya.timer.once(hu[123]=989, ...) pathTip 延迟显示
const PATH_TIP_DELAY_MS = 989;

/**
 * 生成瓦片铺图数据（bundle Vq，bundle:58750-58790）。
 * 遍历 MapData.map 格网，按 code 分配 skin 与目标层（road / highGround）。
 * @param {MapData} mapData
 * @returns {{tiles:Array<{gridX,gridY,code,layer,skin,pixelX,pixelY,width,height}>, layers:{road:number,highGround:number}}}
 */
function composeTiles(mapData) {
  const mapIndex = mapData.mapIndex;
  const gw = mapData.gridWidth;
  const gh = mapData.gridHeight;
  const tiles = [];
  let roadCount = 0, highGroundCount = 0;
  for (let x = 0; x < mapData.width; x += 1) {
    for (let y = 0; y < mapData.height; y += 1) {
      const code = mapData.blockAt(x, y);
      if (typeof code !== 'string') continue;
      const entry = TILE_SKIN_MAP[code];
      if (!entry) continue;
      const tile = {
        gridX: x, gridY: y, code,
        layer: entry.layer,
        skin: entry.skin(mapIndex),
        pixelX: x * gw, pixelY: y * gh,
        width: gw, height: gh,
      };
      tiles.push(tile);
      if (entry.layer === 'road') roadCount += 1; else highGroundCount += 1;
    }
  }
  return { tiles, layers: { road: roadCount, highGround: highGroundCount } };
}

/**
 * 生成地图边界线段（bundle Qq，bundle:57955-57988）。
 * 规则：对每个建造/草地格，检查四向邻居；若邻居是路径格（0_0/0_1），
 *       在两者交界处画一条黑色线段（宽 3）。最后画地图外框（宽 6）。
 * @param {MapData} mapData
 * @returns {{lines:Array<{x1,y1,x2,y2,width,color}>, frameWidth, frameColor}}
 */
function composeBound(mapData) {
  const map = mapData.map;
  const gw = mapData.gridWidth;
  const gh = mapData.gridHeight;
  const width = mapData.width;
  const height = mapData.height;
  const lines = [];
  for (let x = 0; x < width; x += 1) {
    for (let y = 0; y < height; y += 1) {
      const code = map[x][y];
      if (!BUILDABLE_CODES.has(code)) continue;
      // 左邻居是路径格 → 画左边界竖线
      if (x - 1 >= 0 && WALKABLE_CODES.has(map[x - 1][y]))
        lines.push({ x1: x * gw, y1: y * gh, x2: x * gw, y2: (y + 1) * gh, width: BOUND_INNER_WIDTH, color: BOUND_COLOR });
      // 右邻居是路径格 → 画右边界竖线
      if (x + 1 < width && WALKABLE_CODES.has(map[x + 1][y]))
        lines.push({ x1: (x + 1) * gw, y1: y * gh, x2: (x + 1) * gw, y2: (y + 1) * gh, width: BOUND_INNER_WIDTH, color: BOUND_COLOR });
      // 上邻居是路径格 → 画上边界横线
      if (y - 1 >= 0 && WALKABLE_CODES.has(map[x][y - 1]))
        lines.push({ x1: x * gw, y1: y * gh, x2: (x + 1) * gw, y2: y * gh, width: BOUND_INNER_WIDTH, color: BOUND_COLOR });
      // 下邻居是路径格 → 画下边界横线
      if (y + 1 < height && WALKABLE_CODES.has(map[x][y + 1]))
        lines.push({ x1: x * gw, y1: (y + 1) * gh, x2: (x + 1) * gw, y2: (y + 1) * gh, width: BOUND_INNER_WIDTH, color: BOUND_COLOR });
    }
  }
  // 地图外框（bundle:57983）
  const fw = width * gw, fh = height * gh;
  lines.push({ x1: 0, y1: 0, x2: fw, y2: 0, width: BOUND_FRAME_WIDTH, color: BOUND_COLOR });
  lines.push({ x1: 0, y1: fh, x2: fw, y2: fh, width: BOUND_FRAME_WIDTH, color: BOUND_COLOR });
  lines.push({ x1: 0, y1: 0, x2: 0, y2: fh, width: BOUND_FRAME_WIDTH, color: BOUND_COLOR });
  lines.push({ x1: fw, y1: 0, x2: fw, y2: fh, width: BOUND_FRAME_WIDTH, color: BOUND_COLOR });
  return { lines, frameWidth: BOUND_FRAME_WIDTH, frameColor: BOUND_COLOR };
}

/**
 * 生成分界线尺寸（bundle:58770-58782，4 张地图 divide pos/size）。
 * @param {number} mapIndex
 * @returns {{x,y,width,height,skin:string}}
 */
function composeDivide(mapIndex) {
  const entry = DIVIDE_TABLE[mapIndex];
  if (!entry) throw new Error(`MapRenderer.composeDivide: invalid mapIndex ${mapIndex}`);
  return { ...entry, skin: `resources/img/map/divide_${mapIndex}.png` };
}

/**
 * 生成 mapBg 切换指令（bundle $q，bundle:57815-57843）。
 * mapIndex=0 用 mapBgImg（静态 .ls 节点）；其余用 mapBgImgNew + 动态 skin。
 * mapTitle.skin 按 mapIndex 切换。mapBg prefab（mapBg0-3.lh）按 mapIndex 实例化。
 * @param {number} mapIndex
 * @returns {{mapBgImgVisible:boolean, mapBgImgNewVisible:boolean, mapBgImgNewSkin:string|null, mapTitleSkin:string, mapBgPrefabKey:string}}
 */
function composeMapBg(mapIndex) {
  const useOld = mapIndex === 0;
  return {
    mapBgImgVisible: useOld,
    mapBgImgNewVisible: !useOld,
    mapBgImgNewSkin: useOld ? null : `resources/img/map/mapBg_${mapIndex}.png`,
    mapTitleSkin: `resources/img/map/mapBg/mapBg${mapIndex}/title.png`,
    mapBgPrefabKey: `mapBg${mapIndex}`,
  };
}

/**
 * 生成 bg 背景贴图指令（bundle:58770 Vq 内 this.bg.skin）。
 * @param {number} mapIndex
 * @returns {{skin:string}}
 */
function composeBackground(mapIndex) {
  return { skin: `resources/img/map/bg_${mapIndex}.png` };
}

/**
 * 生成 pathTip 方向变换（bundle Kq，bundle:58307-58324）。
 * 原始逻辑：比较 entry 与 start 坐标，按方向差翻转 pathTip 的 scale/pos。
 *   - x 方向不同：pos 到 entry 旁，x>则 scaleX=1+右移一格，x<则 scaleY=-1（镜像）
 *   - y 方向不同：pos 到 entry 旁，y<则 scaleY=1，y>则 scaleY=-1+下移一格
 * @param {{x,y}} entry  路径入口坐标（playerEntry / opponentEntry）
 * @param {{x,y}} start  路径起点坐标（playerStart / opponentStart）
 * @param {number} gridWidth   map.ye
 * @param {number} gridHeight  map.gridHei
 * @param {number} tipWidth    pathTip 节点宽
 * @param {number} tipHeight   pathTip 节点高
 * @returns {{posX,posY,scaleX,scaleY}}
 */
function composePathTip(entry, start, gridWidth, gridHeight, tipWidth, tipHeight) {
  let posX = 0, posY = 0, scaleX = 1, scaleY = 1;
  if (start.x !== entry.x) {
    posX = entry.x * gridWidth + (start.x - entry.x) * (tipWidth / 2);
    posY = entry.y * gridHeight + gridHeight / 2;
    if (start.x > entry.x) { scaleX = 1; posX += gridWidth; }
    else { scaleY = -1; }
  }
  if (start.y !== entry.y) {
    posX = entry.x * gridWidth + gridWidth / 2;
    posY = entry.y * gridHeight + (start.y - entry.y) * (tipHeight / 2);
    if (start.y < entry.y) { scaleY = 1; }
    else { scaleY = -1; posY += gridHeight; }
  }
  return { posX, posY, scaleX, scaleY };
}

/**
 * 生成 end1/end2 终点位置（bundle Zq，bundle:57189-57214）。
 * end1 = 玩家终点（playerEnd/he），end2 = 对手终点（opponentEnd/ne）。
 * @param {MapData} mapData
 * @returns {{end1:{x,y}, end2:{x,y}}}
 */
function composeEndPositions(mapData) {
  const gw = mapData.gridWidth, gh = mapData.gridHeight;
  return {
    end1: { x: mapData.playerEnd.x * gw, y: mapData.playerEnd.y * gh },
    end2: { x: mapData.opponentEnd.x * gw, y: mapData.opponentEnd.y * gh },
  };
}

/**
 * 生成完整地图视觉数据（onOpened 调用链 $q→Vq→Qq→Zq→Kq，bundle:58526）。
 * 一次调用产出所有子指令，适配器按字段落节点。
 * @param {MapData} mapData
 * @param {{pathTipWidth?:number, pathTipHeight?:number}} [options]
 * @returns {MapVisualData}
 */
function compose(mapData, options = {}) {
  const mapIndex = mapData.mapIndex;
  const gw = mapData.gridWidth, gh = mapData.gridHeight;
  const tipW = options.pathTipWidth != null ? options.pathTipWidth : 80;
  const tipH = options.pathTipHeight != null ? options.pathTipHeight : 150;
  const tiles = composeTiles(mapData);
  const bound = composeBound(mapData);
  const divide = composeDivide(mapIndex);
  const mapBg = composeMapBg(mapIndex);
  const background = composeBackground(mapIndex);
  const ends = composeEndPositions(mapData);
  const pathTip0 = composePathTip(mapData.playerEntry, mapData.playerStart, gw, gh, tipW, tipH);
  const pathTip1 = composePathTip(mapData.opponentEntry, mapData.opponentStart, gw, gh, tipW, tipH);
  return {
    mapIndex,
    grid: { width: mapData.width, height: mapData.height, cellWidth: gw, cellHeight: gh },
    background, tiles, bound, divide, mapBg, ends,
    pathTips: { pathTip0, pathTip1 },
    pathTipDelayMs: PATH_TIP_DELAY_MS,
  };
}

/**
 * @typedef {Object} MapVisualData
 * @property {number} mapIndex
 * @property {{width,height,cellWidth,cellHeight}} grid
 * @property {{skin:string}} background
 * @property {{tiles:Array, layers:{road:number,highGround:number}}} tiles
 * @property {{lines:Array, frameWidth, frameColor}} bound
 * @property {{x,y,width,height,skin}} divide
 * @property {{mapBgImgVisible,mapBgImgNewVisible,mapBgImgNewSkin,mapTitleSkin,mapBgPrefabKey}} mapBg
 * @property {{end1:{x,y}, end2:{x,y}}} ends
 * @property {{pathTip0:{posX,posY,scaleX,scaleY}, pathTip1:{posX,posY,scaleX,scaleY}}} pathTips
 * @property {number} pathTipDelayMs
 */

module.exports = {
  DIVIDE_TABLE, TILE_SKIN_MAP, WALKABLE_CODES, BUILDABLE_CODES,
  BOUND_COLOR, BOUND_INNER_WIDTH, BOUND_FRAME_WIDTH, PATH_TIP_DELAY_MS,
  composeTiles, composeBound, composeDivide, composeMapBg, composeBackground,
  composePathTip, composeEndPositions, compose,
};
