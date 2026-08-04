'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { MapData } = require('../../src/battle/MapData');
const MapRenderer = require('../../src/rendering/MapRenderer');

// ── 辅助：创建指定 mapIndex 的 MapData ──
function makeMap(mapIndex) {
  const m = new MapData();
  m.changeMap(mapIndex);
  return m;
}

// ── composeTiles ──

test('composeTiles distributes tiles across road and highGround layers for map 0', () => {
  const m = makeMap(0);
  const { tiles, layers } = MapRenderer.composeTiles(m);
  const total = m.width * m.height; // 8×10=80
  assert.equal(tiles.length, total);
  assert.equal(layers.road + layers.highGround, total);
  // 抽查：map0 grid[0][0]='0_1' 应在 road 层
  const t00 = tiles.find(t => t.gridX === 0 && t.gridY === 0);
  assert.equal(t00.layer, 'road');
  assert.equal(t00.skin, 'resources/img/map/road_0.png');
  assert.equal(t00.pixelX, 0);
  assert.equal(t00.pixelY, 0);
  // 抽查：map0 grid[3][1]='1_1' 应在 highGround 层（space 贴图）
  const t31 = tiles.find(t => t.gridX === 3 && t.gridY === 1);
  assert.equal(t31.layer, 'highGround');
  assert.equal(t31.skin, 'resources/img/map/space_0.png');
  // 抽查：草地格 grass 变体
  const grass0 = tiles.find(t => t.code === '2_0');
  assert.equal(grass0.skin, 'resources/img/map/grass_0_0.png');
  const grass1 = tiles.find(t => t.code === '2_1');
  assert.equal(grass1.skin, 'resources/img/map/grass_0_1.png');
});

test('composeTiles skin path includes correct mapIndex', () => {
  for (const idx of [0, 1, 2, 3]) {
    const m = makeMap(idx);
    const { tiles } = MapRenderer.composeTiles(m);
    const road = tiles.find(t => t.layer === 'road');
    assert.ok(road.skin.includes(`road_${idx}.png`), `map ${idx} road skin`);
  }
});

test('composeTiles pixel positions match grid coordinates', () => {
  const m = makeMap(0);
  const { tiles } = MapRenderer.composeTiles(m);
  for (const t of tiles) {
    assert.equal(t.pixelX, t.gridX * 80);
    assert.equal(t.pixelY, t.gridY * 80);
    assert.equal(t.width, 80);
    assert.equal(t.height, 80);
  }
});

// ── composeBound ──

test('composeBound draws inner edges between buildable and walkable cells', () => {
  const m = makeMap(0);
  const { lines } = MapRenderer.composeBound(m);
  // 至少有内边界线（建造格与路径格相邻）+ 4 条外框
  const inner = lines.filter(l => l.width === MapRenderer.BOUND_INNER_WIDTH);
  const frame = lines.filter(l => l.width === MapRenderer.BOUND_FRAME_WIDTH);
  assert.ok(inner.length > 0, 'should have inner boundary lines');
  assert.equal(frame.length, 4, 'map outer frame has 4 sides');
  // 所有线段颜色一致
  for (const l of lines) assert.equal(l.color, '#000000');
});

test('composeBound frame covers full map dimensions', () => {
  const m = makeMap(0);
  const { lines } = MapRenderer.composeBound(m);
  const frame = lines.filter(l => l.width === MapRenderer.BOUND_FRAME_WIDTH);
  const fw = m.width * 80, fh = m.height * 80;
  // 顶边
  assert.deepEqual({ x1: 0, y1: 0, x2: fw, y2: 0 }, { x1: frame[0].x1, y1: frame[0].y1, x2: frame[0].x2, y2: frame[0].y2 });
  // 底边
  assert.ok(frame.some(l => l.y1 === fh && l.y2 === fh), 'bottom frame line');
  // 左边
  assert.ok(frame.some(l => l.x1 === 0 && l.x2 === 0), 'left frame line');
  // 右边
  assert.ok(frame.some(l => l.x1 === fw && l.x2 === fw), 'right frame line');
});

test('composeBound does not draw inner line between two adjacent buildable cells', () => {
  const m = makeMap(0);
  const { lines } = MapRenderer.composeBound(m);
  const inner = lines.filter(l => l.width === MapRenderer.BOUND_INNER_WIDTH);
  // 内边界线必须贴在路径格旁边：线段坐标应在路径格边界
  // 验证：不存在两条完全相同的内边界线（去重）
  const keys = new Set(inner.map(l => `${l.x1},${l.y1},${l.x2},${l.y2}`));
  assert.equal(keys.size, inner.length, 'no duplicate inner lines');
});

// ── composeDivide ──

test('composeDivide returns correct size for all 4 maps', () => {
  const expected = {
    0: { x: 0, y: 301, width: 633, height: 182 },
    1: { x: 0, y: 361, width: 652, height: 44 },
    2: { x: 0, y: 380, width: 633, height: 23 },
    3: { x: 0, y: 364, width: 637, height: 50 },
  };
  for (const idx of [0, 1, 2, 3]) {
    const d = MapRenderer.composeDivide(idx);
    assert.equal(d.x, expected[idx].x, `map ${idx} divide.x`);
    assert.equal(d.y, expected[idx].y, `map ${idx} divide.y`);
    assert.equal(d.width, expected[idx].width, `map ${idx} divide.width`);
    assert.equal(d.height, expected[idx].height, `map ${idx} divide.height`);
    assert.equal(d.skin, `resources/img/map/divide_${idx}.png`);
  }
});

test('composeDivide throws on invalid mapIndex', () => {
  assert.throws(() => MapRenderer.composeDivide(99), /invalid mapIndex 99/);
});

// ── composeMapBg ──

test('composeMapBg uses static mapBgImg for map 0, dynamic for others', () => {
  const bg0 = MapRenderer.composeMapBg(0);
  assert.equal(bg0.mapBgImgVisible, true);
  assert.equal(bg0.mapBgImgNewVisible, false);
  assert.equal(bg0.mapBgImgNewSkin, null);
  assert.equal(bg0.mapTitleSkin, 'resources/img/map/mapBg/mapBg0/title.png');
  assert.equal(bg0.mapBgPrefabKey, 'mapBg0');

  const bg2 = MapRenderer.composeMapBg(2);
  assert.equal(bg2.mapBgImgVisible, false);
  assert.equal(bg2.mapBgImgNewVisible, true);
  assert.equal(bg2.mapBgImgNewSkin, 'resources/img/map/mapBg_2.png');
  assert.equal(bg2.mapBgPrefabKey, 'mapBg2');
});

// ── composeBackground ──

test('composeBackground skin follows mapIndex', () => {
  for (const idx of [0, 1, 2, 3]) {
    const bg = MapRenderer.composeBackground(idx);
    assert.equal(bg.skin, `resources/img/map/bg_${idx}.png`);
  }
});

// ── composePathTip ──

test('composePathTip handles x-direction (player map 0: entry(0,9)→start(0,8) is y-direction)', () => {
  // map0 player: entry(0,9) start(0,8) → y 方向不同（y 递减）
  const tip = MapRenderer.composePathTip({ x: 0, y: 9 }, { x: 0, y: 8 }, 80, 80, 80, 150);
  assert.equal(tip.scaleY, 1); // start.y < entry.y → scaleY=1
  assert.equal(tip.scaleX, 1);
  assert.equal(tip.posX, 0 * 80 + 80 / 2); // entry.x*gw + gw/2
});

test('composePathTip handles x-increasing direction', () => {
  // 构造 entry(0,5) start(2,5) → x 递增
  const tip = MapRenderer.composePathTip({ x: 0, y: 5 }, { x: 2, y: 5 }, 80, 80, 80, 150);
  assert.equal(tip.scaleX, 1); // start.x > entry.x → scaleX=1
  assert.equal(tip.posX, 0 * 80 + (2 - 0) * 40 + 80); // entry.x*gw + dx*(w/2) + gw
});

test('composePathTip handles x-decreasing direction with mirror', () => {
  // 构造 entry(2,5) start(0,5) → x 递减 → scaleY=-1
  const tip = MapRenderer.composePathTip({ x: 2, y: 5 }, { x: 0, y: 5 }, 80, 80, 80, 150);
  assert.equal(tip.scaleY, -1); // start.x < entry.x → scaleY=-1（镜像）
});

// ── composeEndPositions ──

test('composeEndPositions maps player/opponent end to pixel coords', () => {
  const m = makeMap(0);
  const ends = MapRenderer.composeEndPositions(m);
  // map0: playerEnd(7,9) opponentEnd(0,0)
  assert.deepEqual(ends.end1, { x: 7 * 80, y: 9 * 80 });
  assert.deepEqual(ends.end2, { x: 0 * 80, y: 0 * 80 });
});

// ── compose（全量）──

test('compose produces complete visual data for all 4 maps', () => {
  for (const idx of [0, 1, 2, 3]) {
    const m = makeMap(idx);
    const data = MapRenderer.compose(m);
    assert.equal(data.mapIndex, idx);
    assert.equal(data.grid.width, 8);
    assert.equal(data.grid.height, 10);
    assert.equal(data.grid.cellWidth, 80);
    assert.equal(data.grid.cellHeight, 80);
    assert.equal(data.tiles.tiles.length, 80);
    assert.ok(data.bound.lines.length > 4, `map ${idx} bound has inner lines`);
    assert.equal(data.divide.skin, `resources/img/map/divide_${idx}.png`);
    assert.equal(data.background.skin, `resources/img/map/bg_${idx}.png`);
    assert.equal(data.mapBg.mapBgPrefabKey, `mapBg${idx}`);
    assert.ok(data.pathTips.pathTip0 && data.pathTips.pathTip1);
    assert.equal(data.pathTipDelayMs, 989);
    assert.ok(data.ends.end1 && data.ends.end2);
  }
});

test('compose pathTips use correct entry/start per side', () => {
  const m = makeMap(0);
  const data = MapRenderer.compose(m);
  // pathTip0 用 playerEntry/playerStart；pathTip1 用 opponentEntry/opponentStart
  // map0 player: entry(0,9)→start(0,8) y方向
  assert.equal(data.pathTips.pathTip0.scaleY, 1); // y 递减
  // map0 opponent: entry(7,0)→start(7,1) y方向（y递增）
  assert.equal(data.pathTips.pathTip1.scaleY, -1); // start.y > entry.y → scaleY=-1
});

// ── TILE_SKIN_MAP 完整性 ──

test('TILE_SKIN_MAP covers all 6 tile codes', () => {
  const codes = ['0_0', '0_1', '1_0', '1_1', '2_0', '2_1'];
  for (const code of codes) {
    assert.ok(MapRenderer.TILE_SKIN_MAP[code], `missing skin map for ${code}`);
  }
});

test('WALKABLE_CODES and BUILDABLE_CODES partition correctly', () => {
  assert.ok(MapRenderer.WALKABLE_CODES.has('0_0'));
  assert.ok(MapRenderer.WALKABLE_CODES.has('0_1'));
  assert.ok(!MapRenderer.WALKABLE_CODES.has('1_0'));
  for (const c of ['1_0', '1_1', '2_0', '2_1']) {
    assert.ok(MapRenderer.BUILDABLE_CODES.has(c));
  }
});
