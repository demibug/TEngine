'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

/**
 * 任务组 7.7：DEFERRED 标注回归
 * 扫描确认未明示数值/外部接口未自行补成原版：
 *   - 灵魂塔 soulTowerResolver（au.Ii/Ti Ci/num/range/pos）为 DEFERRED 桩，默认 {Ci:false} 不触发
 *   - 飞行管理器 soulFlightManager（qs.vg）为 DEFERRED 桩，no-op + 立即 onComplete
 *   - Xw 触发方（攻击/技能系统）为 DEFERRED，NormalEnemyBase 只恢复被触发能力
 *   - ENEMY_SOUL_DELIVERED（ut 事件）为新建常量，消费者 DEFERRED
 *   - Puppet puppetSkip 定时器调用方为 DEFERRED
 *
 * 用 grep/正则扫描源码确认标注存在，未自行编造塔/飞行管理器/触发方实现。
 */
const SRC = path.join(__dirname, '..', '..', 'src');

function readFile(rel) {
  return fs.readFileSync(path.join(SRC, rel), 'utf8');
}

test('灵魂塔 soulTowerResolver 标注 DEFERRED，默认桩 {Ci:false} 不触发', t => {
  const enemyBase = readFile('entities/EnemyBase.js');
  // 默认桩返回 {Ci:false} 使 sB 条件永不满足。
  assert.ok(enemyBase.includes('defaultSoulTowerResolver'), '定义 defaultSoulTowerResolver 默认桩');
  assert.ok(/return\s*\{\s*Ci:\s*false/.test(enemyBase), '默认桩返回 {Ci:false}');
  // 标注 DEFERRED：塔状态接口 au.Ii/Ti 未取证。
  assert.ok(enemyBase.includes('DEFERRED'), 'EnemyBase 标注 DEFERRED');
  assert.ok(enemyBase.includes('au.Ii') || enemyBase.includes('au.Ii/Ti'), '标注塔接口 au.Ii/Ti');
  assert.ok(enemyBase.includes('soulTowerResolver'), 'soulTowerResolver 可注入接口');
});

test('飞行管理器 soulFlightManager 标注 DEFERRED，默认桩 no-op', t => {
  const enemyBase = readFile('entities/EnemyBase.js');
  assert.ok(enemyBase.includes('defaultSoulFlightManager'), '定义 defaultSoulFlightManager 默认桩');
  assert.ok(enemyBase.includes('soulFlightManager'), 'soulFlightManager 可注入接口');
  // 默认桩 no-op + 立即 onComplete（仅当 sB 条件被外部注入塔满足时才会调到）。
  assert.ok(/defaultSoulFlightManager[\s\S]*?fly\([\s\S]*?onComplete/.test(enemyBase), '默认桩 fly no-op + 立即 onComplete');
  // 标注 DEFERRED：qs.vg 飞行管理器未取证。
  assert.ok(enemyBase.includes('qs.vg'), '标注飞行管理器 qs.vg');
});

test('Xw 触发方标注 DEFERRED，NormalEnemyBase 只恢复被触发能力', t => {
  const normalEnemyBase = readFile('entities/NormalEnemyBase.js');
  // Xw 注释标注 DEFERRED：调用方与 heightArg 来源属提案 ②③。
  assert.ok(normalEnemyBase.includes('Xw'), '实现 Xw 方法');
  assert.ok(/Xw[\s\S]*?DEFERRED[\s\S]*?调用方/.test(normalEnemyBase) || /DEFERRED[\s\S]*?Xw/.test(normalEnemyBase)
    || normalEnemyBase.includes('DEFERRED：调用方与 heightArg 来源'), 'Xw 触发方标注 DEFERRED');
  // 不自行编造触发条件：Xw 是被触发 API，NormalEnemyBase 内部不调 Xw。
  assert.ok(!/this\.Xw\(/.test(normalEnemyBase), 'NormalEnemyBase 内部不自行调用 Xw（外部触发）');
});

test('ENEMY_SOUL_DELIVERED 为新建常量，消费者 DEFERRED', t => {
  const eventBus = readFile('core/EventBus.js');
  assert.ok(eventBus.includes('ENEMY_SOUL_DELIVERED'), '定义 ENEMY_SOUL_DELIVERED 常量');
  assert.ok(eventBus.includes("'ut'") || eventBus.includes('"ut"'), '映射 bundle ut 事件');
  // sB 发事件，消费者（召唤方）属提案 ②③，DEFERRED。
  const normalEnemyBase = readFile('entities/NormalEnemyBase.js');
  assert.ok(normalEnemyBase.includes('ENEMY_SOUL_DELIVERED'), 'sB 发 ENEMY_SOUL_DELIVERED 事件');
  assert.ok(normalEnemyBase.includes('DEFERRED'), 'NormalEnemyBase 标注 DEFERRED');
});

test('sB 不自行编造塔实现，经 soulTowerResolver 注入取塔状态', t => {
  const normalEnemyBase = readFile('entities/NormalEnemyBase.js');
  // _tryDeliverSoul 经 soulTowerResolver 取塔，不直接构造塔对象。
  assert.ok(normalEnemyBase.includes('this.soulTowerResolver'), '经 soulTowerResolver 取塔状态');
  // sB 经 soulFlightManager.fly 飞行，不直接构造飞行投射物。
  assert.ok(normalEnemyBase.includes('this.soulFlightManager.fly'), '经 soulFlightManager.fly 飞行');
  // 不自行编造塔字段语义（Ci/num/range/pos 取自注入塔，未硬编码塔实现）。
  assert.ok(!/class\s+\w*Tower/.test(normalEnemyBase), '不编造塔类实现');
});

test('吹飞 gameLoop 标注 DEFERRED，默认委托 GameLoop.instance() 单例', t => {
  const enemyBase = readFile('entities/EnemyBase.js');
  // gameLoop 默认桩委托 GameLoop.instance() 单例（对齐 bundle nx.instance()）。
  assert.ok(enemyBase.includes('defaultGameLoopAccessor'), '定义 defaultGameLoopAccessor 默认桩');
  assert.ok(enemyBase.includes('GameLoop.instance()'), '默认委托 GameLoop.instance() 单例');
  assert.ok(enemyBase.includes('DEFERRED'), 'EnemyBase 标注 DEFERRED');
});

test('Puppet puppetSkip 定时器调用方标注 DEFERRED', t => {
  const puppet = readFile('entities/types/PuppetEnemy.js');
  // gameOver 注销 puppetSkip 定时器（外部召唤方注册，DEFERRED）。
  assert.ok(puppet.includes('puppetSkip'), '注销 puppetSkip 定时器');
  assert.ok(puppet.includes('DEFERRED'), 'Puppet 标注 DEFERRED');
  assert.ok(/DEFERRED[\s\S]*?调用方/.test(puppet) || /调用方[\s\S]*?DEFERRED/.test(puppet)
    || puppet.includes('DEFERRED: 调用方'), 'puppetSkip 调用方标注 DEFERRED');
});

test('表现层经 presentation port，未自行编造渲染实现', t => {
  // Zombie/Cavalry/Puppet 表现层方法经 presentation port 承载，逻辑层只持状态与调度。
  const zombie = readFile('entities/types/ZombieEnemy.js');
  assert.ok(zombie.includes('this.presentation.createSwampDecal'), 'Zombie 沼泽经 port');
  assert.ok(zombie.includes('this.presentation.createBubbleParticle'), 'Zombie 气泡经 port');
  assert.ok(zombie.includes('this.presentation.startZombieBreathing'), 'Zombie 呼吸经 port');
  const cavalry = readFile('entities/types/CavalryEnemy.js');
  assert.ok(cavalry.includes('this.presentation.createCavalryAura'), 'Cavalry 光环经 port');
  assert.ok(cavalry.includes('this.presentation.startCavalryBreathing'), 'Cavalry 呼吸经 port');
  const puppet = readFile('entities/types/PuppetEnemy.js');
  assert.ok(puppet.includes('this.presentation.createPuppetHeart'), 'Puppet 爱心经 port');
  // 不直接操作 Laya.Tween/Laya.Image（经 port 承载）。
  assert.ok(!/new\s+this\.laya\.Image/.test(zombie), 'Zombie 不直接 new Laya.Image');
  assert.ok(!/new\s+this\.laya\.Image/.test(cavalry), 'Cavalry 不直接 new Laya.Image');
});

test('enemies.json 标注 deferred VFX（Zombie bubble / Cavalry yellow-circle）', t => {
  const raw = fs.readFileSync(path.join(SRC, '..', 'unity-export', 'config', 'enemies.json'), 'utf8');
  const data = JSON.parse(raw);
  assert.equal(data.types.Zombie.deferred, 'bubble VFX', 'Zombie bubble VFX 标注 deferred');
  assert.equal(data.types.Cavalry.deferred, 'yellow-circle VFX', 'Cavalry yellow-circle VFX 标注 deferred');
});

test('未自行补成原版外部接口（无灵魂塔类/飞行管理器实现/Xw 内部触发）', t => {
  // 扫描 src/entities 确认无自行编造的塔实现/飞行管理器实现。
  const entitiesDir = path.join(SRC, 'entities');
  function listFiles(dir, ext = '.js') {
    const out = [];
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) out.push(...listFiles(full, ext));
      else if (entry.name.endsWith(ext)) out.push(full);
    }
    return out;
  }
  const files = listFiles(entitiesDir);
  let towerClassCount = 0;
  let flightManagerClassCount = 0;
  for (const file of files) {
    const content = fs.readFileSync(file, 'utf8');
    // 不编造灵魂塔类（如 SoulTower 实现 au.Ii/Ti）。
    if (/class\s+SoulTower/.test(content)) towerClassCount++;
    // 不编造飞行管理器类（如 SoulFlightManager 实现 qs.vg）。
    if (/class\s+SoulFlightManager/.test(content)) flightManagerClassCount++;
  }
  assert.equal(towerClassCount, 0, '不自行编造灵魂塔类实现');
  assert.equal(flightManagerClassCount, 0, '不自行编造飞行管理器类实现');
});

// ===========================================================================
// 任务 7.6（ai-advanced-strategy）：AI DEFERRED 标注回归
// 扫描确认 src/ai/ 的 4 项 DEFERRED 标注存在且未被自行补成原版实现：
//   - itemEffectDispatcher（道具 effect）：默认桩 no-op 返回 success:false，标 DEFERRED_ITEM_SYSTEM
//   - 武将模板（Mp/Bp/qj.kX/yX 武将项）：空占位标 DEFERRED_GENERAL_TEMPLATE
//   - rankTableResolver：默认桩 no-op 钳制 0-3，标 DEFERRED_RANK_TABLE（rank.json 不存在）
//   - qj.bX 寻路（OG 寻路距离）：标 DEFERRED_PATHFINDING 退化返回 0
// 用 grep/正则扫描源码文本断言标注存在，并断言默认桩行为，断言未出现"自行补成原版"的实体实现。
// ===========================================================================
const AI_SRC = path.join(SRC, 'ai');

function readAiFile(rel) {
  return fs.readFileSync(path.join(AI_SRC, rel), 'utf8');
}

test('AI 7.6: itemEffectDispatcher 标注 DEFERRED_ITEM_SYSTEM，默认桩 use 返回 success:false', () => {
  const controller = readAiFile('AIController.js');
  // 标注 DEFERRED_ITEM_SYSTEM 存在。
  assert.ok(controller.includes('DEFERRED_ITEM_SYSTEM'), 'AIController 标注 DEFERRED_ITEM_SYSTEM');
  // 默认桩方法 _defaultItemEffectDispatcher 存在。
  assert.ok(controller.includes('_defaultItemEffectDispatcher'), '定义 _defaultItemEffectDispatcher 默认桩');
  // 默认桩 use 返回 {success:false}（no-op 视为失败，spec 约束桩返回 false）。
  assert.ok(/_defaultItemEffectDispatcher[\s\S]*?use[\s\S]*?return\s*\{\s*success:\s*false\s*\}/.test(controller),
    '默认桩 use 返回 {success:false}');
  // itemEffectDispatcher 为可注入接口（构造接收 + 默认桩兜底）。
  assert.ok(controller.includes('itemEffectDispatcher'), 'itemEffectDispatcher 可注入接口');
  // 未自行编造道具 effect：不 require 原版道具 effect 类（nO/pM/o0/tE）。
  assert.ok(!/require\([^)]*\b(nO|pM|o0|tE)\b/.test(controller), '不 require 原版道具 effect 类 nO/pM/o0/tE');
  // 未出现真实道具 effect 类调用（new nO/pM/o0/tE 实体分派）。
  assert.ok(!/new\s+(nO|pM|o0|tE)\b/.test(controller), '不 new 原版道具 effect 类');
});

test('AI 7.6: 武将模板项标注 DEFERRED_GENERAL_TEMPLATE，Mp/Bp 空占位不阻塞基础单位', () => {
  const resolver = readAiFile('AITemplateResolver.js');
  // 标注 DEFERRED_GENERAL_TEMPLATE 存在。
  assert.ok(resolver.includes('DEFERRED_GENERAL_TEMPLATE'), 'AITemplateResolver 标注 DEFERRED_GENERAL_TEMPLATE');
  // 模板 Map 含 Mp（武将）/Bp（平民）键。
  assert.ok(resolver.includes("'Mp'") || resolver.includes('"Mp"'), '模板含 Mp 武将项键');
  assert.ok(resolver.includes("'Bp'") || resolver.includes('"Bp"'), '模板含 Bp 平民项键');
  // 武将项 Mp/Bp 为空占位（值空数组 []，未自行还原武将部署数据）。
  assert.ok(/\[\s*'Mp'[\s\S]*?\[\s*\]\s*\]/.test(resolver) || /\[\s*"Mp"[\s\S]*?\[\s*\]\s*\]/.test(resolver),
    'Mp 武将项为空占位 []');
  assert.ok(/\[\s*'Bp'[\s\S]*?\[\s*\]\s*\]/.test(resolver) || /\[\s*"Bp"[\s\S]*?\[\s*\]\s*\]/.test(resolver),
    'Bp 平民项为空占位 []');
  // qj.kX/yX 仅作为注释/字符串标注引用，未自行实现 kX/yX 函数体。
  assert.ok(!/function\s+(kX|yX)\s*\(/.test(resolver), '未自行实现 qj.kX/yX 函数体');
  // 基础单位模板键 Lp/Yc 存在（不阻塞基础单位部署）。
  assert.ok(resolver.includes("'Lp'") || resolver.includes('"Lp"'), '模板含 Lp 基础单位键');
});

test('AI 7.6: rankTableResolver 标注 DEFERRED_RANK_TABLE，默认桩 no-op 钳制 0-3', () => {
  const controller = readAiFile('AIController.js');
  // 标注 DEFERRED_RANK_TABLE 存在。
  assert.ok(controller.includes('DEFERRED_RANK_TABLE'), 'AIController 标注 DEFERRED_RANK_TABLE');
  // 默认桩方法 _defaultRankTableResolver 存在。
  assert.ok(controller.includes('_defaultRankTableResolver'), '定义 _defaultRankTableResolver 默认桩');
  // 默认桩 resolve 钳制 0-3 no-op（不跨档读 rank 表，仅 Si+delta 钳制）。
  assert.ok(/_defaultRankTableResolver[\s\S]*?resolve[\s\S]*?Math\.min\(3[\s\S]*?Math\.max\(0/.test(controller),
    '默认桩 resolve 钳制 0-3 no-op');
  // rankTableResolver 为可注入接口。
  assert.ok(controller.includes('rankTableResolver'), 'rankTableResolver 可注入接口');
  // 未自行读 rank 表：AIController 不读取 rank.json。
  assert.ok(!/rank\.json/.test(controller), 'AIController 不读 rank.json');
  // unity-export/config/rank.json 不存在（确认未自行补成 rank 表文件）。
  const rankPath = path.join(SRC, '..', 'unity-export', 'config', 'rank.json');
  assert.ok(!fs.existsSync(rankPath), 'unity-export/config/rank.json 不存在（rank 表 DEFERRED 未自行补成）');
});

test('AI 7.6: qj.bX 寻路标注 DEFERRED_PATHFINDING，退化返回 0 未实现寻路算法', () => {
  const resolver = readAiFile('AITemplateResolver.js');
  // 标注 DEFERRED_PATHFINDING 存在。
  assert.ok(resolver.includes('DEFERRED_PATHFINDING'), 'AITemplateResolver 标注 DEFERRED_PATHFINDING');
  // bX 方法存在且退化返回 0。
  assert.ok(/bX\s*\([^)]*\)\s*\{[\s\S]*?return\s+0\s*;?/.test(resolver), 'bX 退化返回 0');
  // 未自行实现寻路算法（无 BFS/A*/Dijkstra/visited/openSet 等寻路数据结构）。
  assert.ok(!/\bBFS\b/.test(resolver) && !/\bAStar\b|aStar\b|astar\b/i.test(resolver), 'bX 未实现 A* 寻路');
  assert.ok(!/dijkstra|Dijkstra/.test(resolver), 'bX 未实现 Dijkstra');
  assert.ok(!/openSet|closedSet|visited\[/.test(resolver), 'bX 未实现寻路队列/访问标记');
  // AIController 中 OG 评分经 templateResolver.bX 承载，标注 DEFERRED_PATHFINDING 退化 0。
  const controller = readAiFile('AIController.js');
  assert.ok(controller.includes('DEFERRED_PATHFINDING 退化 0') || /bX[\s\S]*?DEFERRED_PATHFINDING/.test(controller),
    'AIController OG 标注 DEFERRED_PATHFINDING 退化 0');
});

test('AI 7.6: src/ai 全目录未自行补成原版实体实现（无道具 effect 类/rank 读取/寻路算法）', () => {
  // 扫描 src/ai 全目录确认无自行编造的原版实体实现。
  function listAiFiles(dir, ext = '.js') {
    const out = [];
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) out.push(...listAiFiles(full, ext));
      else if (entry.name.endsWith(ext)) out.push(full);
    }
    return out;
  }
  const files = listAiFiles(AI_SRC);
  let itemEffectClassCount = 0;
  let rankReadCount = 0;
  let pathAlgoCount = 0;
  for (const file of files) {
    const content = fs.readFileSync(file, 'utf8');
    // 不编造道具 effect 类（nO/pM/o0/tE 等原版 effect 实体类）。
    if (/class\s+(nO|pM|o0|tE)\b/.test(content)) itemEffectClassCount++;
    // 不在 src/ai 内读 rank.json（rank 表跨档 DEFERRED）。
    if (/rank\.json/.test(content)) rankReadCount++;
    // 不实现寻路算法（A*/Dijkstra/BFS 数据结构）。
    if (/openSet|closedSet|dijkstra|Dijkstra|\bAStar\b/.test(content)) pathAlgoCount++;
  }
  assert.equal(itemEffectClassCount, 0, 'src/ai 不自行编造道具 effect 类');
  assert.equal(rankReadCount, 0, 'src/ai 不读 rank.json');
  assert.equal(pathAlgoCount, 0, 'src/ai 不自行实现寻路算法');
});
