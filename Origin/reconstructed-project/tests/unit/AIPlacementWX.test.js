'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');

/**
 * 任务 7.4：WX 分层放置用例（spec「AI 必须有难度分层放置策略」Requirement）
 *
 * 覆盖场景（对应 spec 的 3 个 Scenario）：
 *   - Si<2 随机洗牌：WX 返回候选经 _shuffle，不按评分排序（bundle:49912 np.Ys）。
 *   - Si=2 DX 降序→TX 升序 + 前5洗牌 unshift（bundle:49949-49952）。
 *   - Si=3 OG→DX→TX：OG 经 bX DEFERRED 退化为 0，排序退化为 DX/TX（bundle:49945/49949）。
 *
 * 测试策略：用 mock 构造 AIController（绕过 startGame 的状态机初始化，直接设 Si/
 * templateResolver/GX 字段）+ mock MapData（map.pe='1_1' 可放置格、map.me 路线点、
 * '0_1' 路线格）+ mock UnitRegistry（空棋盘，hasBattleOccupant 全 false）+ 可控
 * randomSource.shuffle（按预定顺序重排）+ mock AITemplateResolver（wG/vG/_G 可控、
 * bX 退化 0）。直接调 WX(terrainKey, unit) 断言 this.zX 候选顺序。
 */

const { AIController } = require('../../src/ai/AIController');

/**
 * 构造 mock MapData（仅暴露 WX 用到的接口：width/height/blockAt/me）。
 * @param {string[][]} grid tile 字符矩阵，grid[x][y]（与 MapData.map 同构：列优先）
 * @param {{x,y}[]} me 对手路线点数组（等价 map.me）
 * @returns {{width:number,height:number,blockAt:Function,me:Array}}
 */
function makeMockMap(grid, me) {
  return {
    width: grid.length,
    height: grid[0] ? grid[0].length : 0,
    blockAt(x, y) { return grid[x] ? grid[x][y] : undefined; },
    me: me || [],
  };
}

/**
 * 构造 mock UnitRegistry（空棋盘，hasBattleOccupant 恒 false → 所有格视为空格）。
 * @returns {{hasBattleOccupant:Function}}
 */
function makeMockUnitRegistry() {
  return { hasBattleOccupant() { return false; } };
}

/**
 * 构造可控 randomSource：shuffle 按预定重排顺序打乱入参数组（原地），
 * 用于断言 Si<2 / Si=2 前5 洗牌后的顺序与 shuffle 一致。
 * @param {number[]} orderMap shuffle(arr) 时把 arr 重排成 orderMap 索引顺序，
 *   即 result[i] = arr[orderMap[i]]。长度须与被洗牌数组一致。
 * @returns {{shuffle:Function,random:Function,randomInt:Function}}
 */
function makeControlledRandom(orderMap) {
  return {
    shuffle(arr) {
      // 原地按 orderMap 重排（深拷贝源值避免覆盖）
      const src = arr.slice();
      for (let i = 0; i < arr.length && i < orderMap.length; i += 1) arr[i] = src[orderMap[i]];
      return arr;
    },
    random() { return 0.5; },     // 固定值（WX Si<2 / Si=2 前5 不依赖 random()，仅依赖 shuffle）
    randomInt(min, max) { return min; },
  };
}

/**
 * 构造 mock AITemplateResolver：wG/vG/_G 可控，bX 退化 0（DEFERRED_PATHFINDING）。
 * @param {number} wG 路线点总数
 * @param {number} vG 近带边界
 * @param {number} _G 远带边界
 * @returns {{wG:number,vG:number,_G:number,bX:Function}}
 */
function makeMockTemplateResolver(wG, vG, _G) {
  return {
    wG, vG, _G,
    bX() { return 0; }, // DEFERRED_PATHFINDING 退化为 0
  };
}

/**
 * 构造一个已配置 WX 所需字段的 AIController（绕过 startGame）。
 * 仅 setSi/templateResolver/GX/zX/randomSource/mapData/unitRegistry。
 * @param {object} opts
 * @param {number} opts.Si 难度档
 * @param {object} opts.mapData mock MapData
 * @param {object} opts.unitRegistry mock UnitRegistry
 * @param {object} opts.randomSource 可控随机源
 * @param {object} opts.templateResolver mock AITemplateResolver
 * @param {object} [opts.GX] 模板 Map（Si=3 OG 用，可空）
 * @returns {AIController}
 */
function makeAIControllerForWX({ Si, mapData, unitRegistry, randomSource, templateResolver, GX = null }) {
  // 构造仅需 4 个必传依赖（gameLoop/gameData/deckManager/inputController），用最小桩。
  const ai = new AIController({
    gameLoop: { register() {}, unregister() {} },
    gameData: { battle: {} },
    deckManager: { hand() { return []; } },
    inputController: { execute() { return { success: false }; } },
    randomSource,
    mapData,
    unitRegistry,
  });
  ai.Si = Si;
  ai.templateResolver = templateResolver;
  ai.GX = GX;
  ai.zX = [];
  return ai;
}

/**
 * 把 zX 候选坐标数组格式化为 'x,y' 字符串数组，便于断言顺序。
 * @param {{x,y}[]} zX
 * @returns {string[]}
 */
function coordsOf(zX) { return zX.map(c => `${c.x},${c.y}`); }

// ===== Si<2 随机洗牌 =====

test('Si<2 随机洗牌：WX 候选经 shuffle，不按评分排序', () => {
  // 构造 mock map：2 列 × 2 行，全为 '1_1' 可放置格（无 '0_1' 路线格），
  // 这样 Si>=2 评分时 DX 全 0、TX 因 wG=0 退化 1e9，排序无差异——
  // 但 Si<2 走 shuffle 分支，根本不评分，直接断言 shuffle 顺序。
  // 候选格（按收集顺序 x 升序→y 升序）：(0,0)(0,1)(1,0)(1,1)
  const grid = [
    ['1_1', '1_1'], // x=0: y=0,1
    ['1_1', '1_1'], // x=1: y=0,1
  ];
  const map = makeMockMap(grid, [{ x: 0, y: 0 }]);
  const reg = makeMockUnitRegistry();

  // controlledRandom.shuffle 把 [a,b,c,d] 重排成 [d,c,a,b]（orderMap=[3,2,0,1]）
  // 即候选 (0,0)(0,1)(1,0)(1,1) → shuffle 后 (1,1)(1,0)(0,0)(0,1)
  const rs = makeControlledRandom([3, 2, 0, 1]);
  const tpl = makeMockTemplateResolver(1, 0, 1);

  for (const Si of [0, 1]) {
    const ai = makeAIControllerForWX({ Si, mapData: map, unitRegistry: reg, randomSource: rs, templateResolver: tpl });
    const ok = ai.WX('1_1', null);
    assert.equal(ok, true, `Si=${Si} WX 应返回 true（有候选格）`);
    // shuffle 顺序：[3,2,0,1] → 候选 (0,0)(0,1)(1,0)(1,1) 变为 (1,1)(1,0)(0,0)(0,1)
    assert.deepEqual(coordsOf(ai.zX), ['1,1', '1,0', '0,0', '0,1'],
      `Si=${Si} 候选应按 shuffle 顺序返回，而非最远格或评分排序`);
    // 验证不按评分排序：若按最远格/评分，(0,0) 等不会在此 shuffle 顺序。
    // 关键：返回顺序 === shuffle 重排顺序（非收集顺序 ['0,0','0,1','1,0','1,1']）。
    assert.notDeepEqual(coordsOf(ai.zX), ['0,0', '0,1', '1,0', '1,1'],
      `Si=${Si} 不应返回收集原序（须经过 shuffle）`);
  }
});

test('Si<2 空候选集时 WX 返回 false', () => {
  // 全 '0_0' 不可放置格，候选收集为空。
  const grid = [['0_0', '0_0'], ['0_0', '0_0']];
  const map = makeMockMap(grid, []);
  const reg = makeMockUnitRegistry();
  const rs = makeControlledRandom([]);
  const tpl = makeMockTemplateResolver(0, 0, 0);
  const ai = makeAIControllerForWX({ Si: 0, mapData: map, unitRegistry: reg, randomSource: rs, templateResolver: tpl });
  const ok = ai.WX('1_1', null);
  assert.equal(ok, false, '空候选集 WX 返回 false');
  assert.equal(ai.zX.length, 0, '空候选集 zX 为空');
});

// ===== Si=2 DX 降序→TX 升序 + 前5洗牌 unshift =====

test('Si=2 DX 降序→TX 升序排序（候选<=3 不触发前5洗牌）', () => {
  // 构造 3 个候选格，DX/TX 可区分：
  //   (0,1): 邻居 (0,0)='0_1' route → DX=1；邻居 (1,1)/(-1,1)/(0,2) 非路线 → DX=1
  //   (2,1): 邻居 (2,0)='0_1' → DX=1；(2,2) 越界；(1,1)='1_1'；(3,1) 越界 → DX=1
  //   为让 DX 不同，构造：
  //     (0,1) 上下左右：(0,0)='0_1',(0,2)='0_1' → DX=2
  //     (2,1) 上下左右：(2,0)='0_1' → DX=1
  //     (4,1) 上下左右：无 '0_1' → DX=0
  //   TX：me 路线点 [(0,0),(2,0)]，wG=2,vG=floor(0.3)=0? → 用 vG=0,_G=2。
  //   y(ax,ay,v=0,w=2)：i 从 max(0,0)=0 到 min(2,2)=2，遍历 t[0],t[1]。
  //     (0,1): |0-0|+|1-0|=1, |0-2|+|1-0|=3 → TX=1
  //     (2,1): |2-0|+|1-0|=3, |2-2|+|1-0|=1 → TX=1
  //     (4,1): |4-0|+|1-0|=5, |4-2|+|1-0|=3 → TX=3
  //   排序（DX 降序→TX 升序）：
  //     DX=2: (0,1) TX=1
  //     DX=1: (2,1) TX=1
  //     DX=0: (4,1) TX=3
  //   → 顺序 [(0,1),(2,1),(4,1)]（DX 严格降序，无需 TX tie-break 区分 DX 不同的项）
  // 候选<=3 不触发前5洗牌（条件 z.length>3 不满足），故顺序即排序结果。
  // grid[x][y]（列优先，与 blockAt(x,y)=map[x][y] 同构）
  const grid = [
    // x=0: (0,1) 候选 '1_1'，邻居 (0,0)/(0,2)='0_1' 路线 → DX=2
    ['0_1', '1_1', '0_1', '0_0'],
    // x=1 (非候选，占位)
    ['0_0', '0_0', '0_0', '0_0'],
    // x=2: (2,1) 候选 '1_1'，邻居 (2,0)='0_1' 路线 → DX=1
    ['0_1', '1_1', '0_0', '0_0'],
    // x=3 (占位)
    ['0_0', '0_0', '0_0', '0_0'],
    // x=4: (4,1) 候选 '1_1'，邻居无 '0_1' → DX=0
    ['0_0', '1_1', '0_0', '0_0'],
  ];
  const me = [{ x: 0, y: 0 }, { x: 2, y: 0 }];
  const map = makeMockMap(grid, me);
  const reg = makeMockUnitRegistry();
  // wG=2, vG=floor(0.15*2)=0, _G=ceil(0.85*2)=2
  const tpl = makeMockTemplateResolver(2, 0, 2);
  // Si=2 候选<=3 不触发 shuffle，但 randomSource 仍提供（防 _shuffle 调用）
  const rs = makeControlledRandom([0, 1, 2]);
  const ai = makeAIControllerForWX({ Si: 2, mapData: map, unitRegistry: reg, randomSource: rs, templateResolver: tpl });

  const ok = ai.WX('1_1', null);
  assert.equal(ok, true, 'Si=2 WX 应返回 true');
  // 验证候选集恰好为 3 个 '1_1' 格
  assert.equal(ai.zX.length, 3, '候选格应为 3 个');
  // 排序后顺序：DX 降序 (0,1)DX2 → (2,1)DX1 → (4,1)DX0
  assert.deepEqual(coordsOf(ai.zX), ['0,1', '2,1', '4,1'],
    'Si=2 候选<=3 时按 DX 降序排序，不触发前5洗牌');
});

test('Si=2 DX 降序→TX 升序，TX tie-break 生效', () => {
  // 两个候选 DX 相同，TX 不同，断言 TX 升序 tie-break。
  //   (0,1): 邻居 (0,0)='0_1' → DX=1
  //   (2,1): 邻居 (2,0)='0_1' → DX=1
  //   TX（me=[(0,0)]，wG=1,vG=0,_G=1）：
  //     y(ax,ay,0,1): i 从 0 到 min(1,1)=1，遍历 t[0]=(0,0)。
  //       (0,1): |0-0|+|1-0|=1 → TX=1
  //       (2,1): |2-0|+|1-0|=3 → TX=3
  //   排序：DX 同为 1 → TX 升序 → (0,1) TX1 → (2,1) TX3
  const grid = [
    ['0_1', '1_1', '0_0'], // x=0: y=0 route, y=1 候选(0,1)
    ['0_0', '0_0', '0_0'], // x=1 占位
    ['0_1', '1_1', '0_0'], // x=2: y=0 route, y=1 候选(2,1)
  ];
  const me = [{ x: 0, y: 0 }];
  const map = makeMockMap(grid, me);
  const reg = makeMockUnitRegistry();
  // wG=1, vG=floor(0.15)=0, _G=ceil(0.85)=1
  const tpl = makeMockTemplateResolver(1, 0, 1);
  const rs = makeControlledRandom([0, 1]);
  const ai = makeAIControllerForWX({ Si: 2, mapData: map, unitRegistry: reg, randomSource: rs, templateResolver: tpl });

  ai.WX('1_1', null);
  assert.deepEqual(coordsOf(ai.zX), ['0,1', '2,1'],
    'Si=2 DX 相同时按 TX 升序 tie-break（近路线点优先）');
});

test('Si=2 候选>3 触发前5洗牌 unshift（保留随机性）', () => {
  // 构造 5 个候选格，DX 严格递减，使排序后顺序确定：
  //   候选 (xi,1)，i=0..4，DX = 4-i（通过控制邻居 '0_1' 路线格数）。
  //   为简化，每候选只放 1 个 '0_1' 邻居差异：用列 x=0..4，每列 y=1 为 '1_1' 候选，
  //   y=0 为 '0_1' 路线（仅特定列），使 DX 不同。
  //   实际构造：DX 通过 (x,0) 是否 '0_1' + (x,2) 是否 '0_1' 组合。
  //   简化：让 5 个候选 DX 全不同即可（排序确定），TX 因 wG=0 退化 1e9 全同（不 tie-break）。
  //
  // 用 wG=0（me=[]），则 y() 早返回 1e9，TX 全同 → 排序仅按 DX 降序。
  // 候选 (0,1)..(4,1)，DX 通过控制 (x,0)/(x,2) 路线格数：
  //   (0,1): (0,0)='0_1',(0,2)='0_1' → DX=2
  //   (1,1): (1,0)='0_1' → DX=1
  //   (2,1): 无 → DX=0
  //   (3,1): (3,0)='0_1',(3,2)='0_1',(?,?) → DX=2（与 (0,1) 同 DX，但 TX 同 1e9，stable 排序保收集序）
  //   为避免 DX tie 干扰，让 DX 严格递减：
  //   (0,1)DX=2, (1,1)DX=1, (2,1)DX=0 —— 仅 3 个不够。
  // 改用 4 邻居全控：每候选 DX∈{0,1,2,3,4} 难构造（最多 4 邻居）。
  // 简化：5 个候选 DX ∈ {4,3,2,1,0} 严格递减——需每候选四邻全为 '0_1' 才 DX=4，
  // 但相邻候选会互相影响邻居判定。改用分散布局：5 列间隔 2，每列独立控 DX。
  //
  // 最终方案：5 候选 DX ∈ {2,1,0,1,2}，排序后 DX 降序分组：
  //   DX=2: (0,1),(4,1)（收集序，stable）→ TX 同 1e9 不 tie-break
  //   DX=1: (1,1),(3,1)
  //   DX=0: (2,1)
  //   排序后：[(0,1),(4,1),(1,1),(3,1),(2,1)]
  //   前5 洗牌：splice(0,5) 取全部 5 个 → shuffle → unshift 回。
  //   controlledRandom orderMap=[4,3,2,1,0] → 反转 → [(2,1),(3,1),(1,1),(4,1),(0,1)]
  const grid = [
    // x=0: (0,1) 候选，邻居 (0,0)/(0,2)='0_1' → DX=2
    ['0_1', '1_1', '0_1'],
    // x=1: (1,1) 候选，邻居 (1,0)='0_1' → DX=1
    ['0_1', '1_1', '0_0'],
    // x=2: (2,1) 候选，邻居无 '0_1' → DX=0
    ['0_0', '1_1', '0_0'],
    // x=3: (3,1) 候选，邻居 (3,0)='0_1' → DX=1
    ['0_1', '1_1', '0_0'],
    // x=4: (4,1) 候选，邻居 (4,0)/(4,2)='0_1' → DX=2
    ['0_1', '1_1', '0_1'],
  ];
  const me = []; // wG=0 → TX 退化 1e9，排序仅按 DX
  const map = makeMockMap(grid, me);
  const reg = makeMockUnitRegistry();
  // wG=0,vG=0,_G=0：y() 因 u===0 早返回 1e9
  const tpl = makeMockTemplateResolver(0, 0, 0);
  // 前5 洗牌 orderMap=[4,3,2,1,0] → 反转排序后数组
  const rs = makeControlledRandom([4, 3, 2, 1, 0]);
  const ai = makeAIControllerForWX({ Si: 2, mapData: map, unitRegistry: reg, randomSource: rs, templateResolver: tpl });

  ai.WX('1_1', null);
  assert.equal(ai.zX.length, 5, '候选格应为 5 个');
  // 排序后（DX 降序，stable）：DX2:(0,1)(4,1) → DX1:(1,1)(3,1) → DX0:(2,1)
  // 即 [(0,1),(4,1),(1,1),(3,1),(2,1)]
  // 前5 全取洗牌（orderMap 反转）→ [(2,1),(3,1),(1,1),(4,1),(0,1)]
  assert.deepEqual(coordsOf(ai.zX), ['2,1', '3,1', '1,1', '4,1', '0,1'],
    'Si=2 候选>3 时前5经 shuffle unshift，顺序与 shuffle 一致而非纯排序');
  // 关键断言：结果不等于纯排序顺序（证明前5洗牌生效）
  assert.notDeepEqual(coordsOf(ai.zX), ['0,1', '4,1', '1,1', '3,1', '2,1'],
    'Si=2 候选>3 时前5洗牌打破纯 DX 降序顺序');
});

test('Si=2 候选恰好 4 个触发前5洗牌（>3 边界）', () => {
  // 候选 4 个（>3 满足条件），前 4 个被 splice(0,min(5,4))=4 全取洗牌 unshift。
  // 构造 4 候选 DX 严格降序，排序确定，洗牌后顺序由 orderMap 控制。
  //   (0,1)DX=2, (1,1)DX=1, (2,1)DX=0, (3,1) DX 通过 (3,0)/(3,2)='0_1' → DX=2?
  //   为严格降序需 4 个不同 DX，但最多 4 邻居 → DX∈{0,1,2,3,4} 可行但构造复杂。
  //   简化：用 wG=0（TX 全 1e9），DX ∈ {3,2,1,0}：
  //   (0,1): 四邻 (0,0)(0,2)(1,1)(-1,1) 中 (0,0)(0,2)(1,1)? —— (1,1) 是候选非路线。
  //   实际 DX 仅数 '0_1' 邻居。让 (0,1) 邻居 (0,0)(0,2)(1,1?非0_1) → 需 (1,1) 非 '0_1'。
  //   构造独立列：4 列，每列 (x,0)/(x,2) 控 DX，列间用 '0_0' 隔离避免互为邻居。
  //   (0,1): (0,0)='0_1',(0,2)='0_1' → DX=2
  //   (2,1): (2,0)='0_1' → DX=1
  //   (4,1): 无 → DX=0
  //   (6,1): (6,0)='0_1',(6,2)='0_1' → DX=2（与 (0,1) tie）
  //   仅 3 档 DX 不够 4 严格降序。接受 tie，用 stable 排序 + TX 同 1e9：
  //   排序后 DX2:(0,1)(6,1) → DX1:(2,1) → DX0:(4,1) → [(0,1),(6,1),(2,1),(4,1)]
  //   前4 全取洗牌 orderMap=[3,2,1,0] → 反转 → [(4,1),(2,1),(6,1),(0,1)]
  const grid = [
    ['0_1', '1_1', '0_1'], // x=0 候选(0,1) DX=2
    ['0_0', '0_0', '0_0'], // x=1 隔离
    ['0_1', '1_1', '0_0'], // x=2 候选(2,1) DX=1
    ['0_0', '0_0', '0_0'], // x=3 隔离
    ['0_0', '1_1', '0_0'], // x=4 候选(4,1) DX=0
    ['0_0', '0_0', '0_0'], // x=5 隔离
    ['0_1', '1_1', '0_1'], // x=6 候选(6,1) DX=2
  ];
  const me = [];
  const map = makeMockMap(grid, me);
  const reg = makeMockUnitRegistry();
  const tpl = makeMockTemplateResolver(0, 0, 0);
  const rs = makeControlledRandom([3, 2, 1, 0]); // 反转 4 元素
  const ai = makeAIControllerForWX({ Si: 2, mapData: map, unitRegistry: reg, randomSource: rs, templateResolver: tpl });

  ai.WX('1_1', null);
  assert.equal(ai.zX.length, 4, '候选格应为 4 个');
  // 排序：DX2:(0,1)(6,1) → DX1:(2,1) → DX0:(4,1) = [(0,1),(6,1),(2,1),(4,1)]
  // 洗牌反转 → [(4,1),(2,1),(6,1),(0,1)]
  assert.deepEqual(coordsOf(ai.zX), ['4,1', '2,1', '6,1', '0,1'],
    'Si=2 候选=4(>3) 触发前5洗牌（实际取前4），顺序随 shuffle');
});

// ===== Si=3 OG→DX→TX（OG DEFERRED 退化为 DX/TX）=====

test('Si=3 OG DEFERRED 退化为 0，排序等价 DX 降序→TX 升序', () => {
  // bX 退化返回 0（DEFERRED_PATHFINDING），故 OG 全 0，
  // 排序 b.OG!==a.OG 永不成立（全 0）→ 退化为 DX 降序→TX 升序（与 Si=2 同）。
  // 用与 Si=2 DX 排序测试相同的 map，断言 Si=3 结果与 Si=2（无前5洗牌分支）一致。
  // 注：Si=3 不触发前5洗牌（条件 r===2 不满足），故结果=纯排序。
  // grid[x][y]（列优先），与 Si=2 DX 排序测试同构：3 候选 DX=2/1/0
  const grid = [
    ['0_1', '1_1', '0_1', '0_0'], // x=0: (0,1) 候选 DX=2
    ['0_0', '0_0', '0_0', '0_0'], // x=1 占位
    ['0_1', '1_1', '0_0', '0_0'], // x=2: (2,1) 候选 DX=1
    ['0_0', '0_0', '0_0', '0_0'], // x=3 占位
    ['0_0', '1_1', '0_0', '0_0'], // x=4: (4,1) 候选 DX=0
  ];
  const me = [{ x: 0, y: 0 }, { x: 2, y: 0 }];
  const map = makeMockMap(grid, me);
  const reg = makeMockUnitRegistry();
  const tpl = makeMockTemplateResolver(2, 0, 2); // wG=2,vG=0,_G=2
  const rs = makeControlledRandom([0, 1, 2]);
  const ai = makeAIControllerForWX({ Si: 3, mapData: map, unitRegistry: reg, randomSource: rs, templateResolver: tpl, GX: new Map() });

  const ok = ai.WX('1_1', null);
  assert.equal(ok, true, 'Si=3 WX 应返回 true');
  // OG 全 0 → 退化为 DX 降序→TX 升序，与 Si=2（候选<=3 无洗牌）同序
  assert.deepEqual(coordsOf(ai.zX), ['0,1', '2,1', '4,1'],
    'Si=3 OG=0 时排序退化为 DX 降序→TX 升序（等价 Si=2 无洗牌）');
});

test('Si=3 OG 全 0 时退化为 DX/TX（不触发前5洗牌，区别于 Si=2）', () => {
  // 用 5 候选 map（同 Si=2 前5洗牌测试），但 Si=3 不触发前5洗牌，
  // 故结果=纯 DX 降序排序（stable），保留 DX 分组内收集序。
  const grid = [
    ['0_1', '1_1', '0_1'], // x=0 (0,1) DX=2
    ['0_1', '1_1', '0_0'], // x=1 (1,1) DX=1
    ['0_0', '1_1', '0_0'], // x=2 (2,1) DX=0
    ['0_1', '1_1', '0_0'], // x=3 (3,1) DX=1
    ['0_1', '1_1', '0_1'], // x=4 (4,1) DX=2
  ];
  const me = [];
  const map = makeMockMap(grid, me);
  const reg = makeMockUnitRegistry();
  const tpl = makeMockTemplateResolver(0, 0, 0); // wG=0 → TX 退化 1e9
  // Si=3 不触发前5洗牌，shuffle 不会被调用，但提供 rs 防 _shuffle 内 Math.random 回退
  const rs = makeControlledRandom([4, 3, 2, 1, 0]);
  const ai = makeAIControllerForWX({ Si: 3, mapData: map, unitRegistry: reg, randomSource: rs, templateResolver: tpl, GX: new Map() });

  ai.WX('1_1', null);
  assert.equal(ai.zX.length, 5, '候选格应为 5 个');
  // OG=0 退化 DX 降序（stable，TX 全 1e9 不 tie-break）：
  //   DX2:(0,1)(4,1) → DX1:(1,1)(3,1) → DX0:(2,1)
  //   = [(0,1),(4,1),(1,1),(3,1),(2,1)]
  // 不触发前5洗牌（r===3 非 2），故顺序=纯排序，与 Si=2 前5洗牌结果不同。
  assert.deepEqual(coordsOf(ai.zX), ['0,1', '4,1', '1,1', '3,1', '2,1'],
    'Si=3 OG=0 退化 DX 降序 stable 排序，不触发前5洗牌（区别于 Si=2）');
});

test('Si=3 bX 经 templateResolver 调用，DEFERRED 返回 0 不抛异常', () => {
  // 断言 Si=3 调 templateResolver.bX（DEFERRED 桩返回 0），不抛异常且 OG=0 退化。
  // 用一个 spy templateResolver 记录 bX 调用次数。
  const grid = [['0_1', '1_1', '0_0']]; // 1 候选 (0,1) DX=1
  const me = [{ x: 0, y: 0 }];
  const map = makeMockMap(grid, me);
  const reg = makeMockUnitRegistry();
  let bXCallCount = 0;
  const tpl = {
    wG: 1, vG: 0, _G: 1,
    bX() { bXCallCount += 1; return 0; }, // DEFERRED 桩 + spy
  };
  const rs = makeControlledRandom([0]);
  const ai = makeAIControllerForWX({ Si: 3, mapData: map, unitRegistry: reg, randomSource: rs, templateResolver: tpl, GX: new Map() });

  const ok = ai.WX('1_1', null);
  assert.equal(ok, true, 'Si=3 WX 须正常返回（bX DEFERRED 不抛）');
  assert.equal(bXCallCount, 1, 'Si=3 须调 templateResolver.bX 一次（每候选一次）');
  assert.deepEqual(coordsOf(ai.zX), ['0,1'], '单候选结果正确');
});
