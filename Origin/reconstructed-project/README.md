# 赵云与阿斗工程重建

当前状态：**Round 06 累计快照完成（v0.6.0）。工程包含第一轮至第六轮全部成果，并新增正式弓兵、STOPPED 动画契约、SimpleDynamicArrow、ProjectileFactory/Manager 与远程击杀闭环。**

本目录是可追溯的分阶段重建工作区，不是对 `bundle.js` 的格式化副本：

- `original/`：不可变原始文件。
- `work/`：逐阶段转换产物和源码范围提取。
- `src/`：可维护的 CommonJS 客户端源码。
- `analysis/`：证据、映射、行为规格、风险和验证结果。
- `tests/`：无真实网络和原生平台调用的单元/行为测试。
- `vendor/`：随工程提供的 TypeScript 5.8.3 tarball，供全新解压目录执行 `npm install`。
- `history/`：被后续实现替代或与当前模块格式不兼容的历史交付文件。

## 累计工程内容

当前目录和交付 ZIP 均为第一至第六轮的**完整累计工程**，不是本轮增量补丁：

```text
Round 01  字符串解码工具、运行时分析、转换映射与测试
Round 02  HttpClient / NET-01、网络 mock、依赖契约与测试
Round 03  GameBootstrap、核心场景、BattleManager 和第一帧链路
Round 04  A*、Mob0、空间索引、伤害、死亡和双层对象池
Round 05  正式刀兵、UnitFactory、UnitRegistry、索敌、攻击和击杀闭环
Round 06  正式弓兵、STOPPED、二次贝塞尔箭矢、ProjectileManager 和远程击杀闭环
```

完整文件清单和哈希材料：

```text
analysis/complete-file-list.txt
analysis/complete-file-list.json
analysis/deliverable-manifest-cumulative-round-06.json
analysis/deliverable-manifest.json
analysis/cumulative-snapshot-audit.json
analysis/cumulative-verification-report.md
analysis/test-results-all.json
analysis/fresh-install-verification-round-06.json
analysis/fresh-install-verification-round-06.md
```

兼容保留的早期累计清单仍位于 `analysis/cumulative-file-list.txt` 和 `analysis/cumulative-file-manifest.json`。

## 历史文件保留策略

累计审计发现部分早期交付文件已被后续同轮或跨轮实现替换。处理规则如下：

- 可直接兼容的历史分析文件和源码范围提取已恢复到原累计路径。
- 会改变当前 CommonJS 解析语义或与活动实现重名的旧文件保存在 `history/superseded/`。
- `history/historical-file-preservation.json` 记录历史 ZIP、恢复位置、归档位置和文件名编码归一化。
- `src/`、`tests/`、`tools/` 中的当前文件是可执行权威版本；`history/` 只用于追溯。

## 当前可执行纵向链路

```text
index.js
  → Laya.init
  → LoadScene
  → MainScene 或特殊启动 MatchScene
  → MainScene.startGame（体力 -5）
  → MatchScene
  → BattleFlowCoordinator.startBattle
  → BattleManager.startGame
  → BattleScene.onAwake / onOpened
  → 双方两个池化 aDou 目标
  → GameLoop 第一固定帧
  → 第 1 波初始化
  → 首对 Mob0
  → MapData 四方向 A* 路径
  → Mob0 移动、接触伤害、死亡、空间索引注销和对象池回收
  → 正式文字键“刀”创建 KnifeSoldier
  → EnemyManager 80px 空间索引查询
  → 120px 范围、800ms 冷却和原攻击状态切换
  → 延迟结算 3 点伤害
  → Mob0 使用第四轮链路死亡并回收
  → 正式文字键“弓”创建 BowSoldier
  → 280px 空间索引查询并按剩余路径距离选择目标
  → Laya.Event.STOPPED
  → SimpleDynamicArrow 二次贝塞尔追踪飞行
  → ProjectileManager 命中、注销和复合池回收
  → Mob0 使用同一死亡链路结算
```

原游戏不会在 BattleScene 初始化时自动创建友军或武将。最先创建的是双方两个 `aDou`；普通敌人由 BattleManager 在准备期结束后成对生成。

## 全新解压安装与累计验证

要求 Node.js 20 或更高版本。TypeScript 5.8.3 已作为本地 tarball 收录，普通安装会从工程内 `vendor/` 解析依赖：

```bash
npm install
npm run verify:all
```

在明确禁止联网的环境中也可以执行：

```bash
npm install --offline
npm run verify:all
```

`verify:all` 顺序执行：

```text
Round 01 字符串解码产物一致性测试
Round 02 NET-01 测试与静态检查
Round 03 BOOT-TO-BATTLE 测试与静态检查
Round 04 ENEMY-RUNTIME-01 测试、模拟与静态检查
Round 05 FRIENDLY-UNIT-COMBAT-01 测试、模拟与静态检查
Round 06 BOW-PROJECTILE-COMBAT-01 测试、模拟与静态检查
历史文件存在性和不可变输入 SHA-256 检查
```

也可以单独执行：

```bash
npm run verify:round02
npm run verify:round03
npm run verify:round04
npm run verify:round05
npm run verify:round06

npm run dev:boot
npm run dev:battle
npm run dev:mob0-simulation
npm run dev:friendly-unit
npm run dev:friendly-unit-simulation
npm run dev:micro-battle
npm run dev:bow-soldier
npm run dev:projectile
npm run dev:ranged-battle
npm run dev:all

npm run test:friendly-unit
npm run audit:cumulative
npm run manifest:cumulative
```

`dev:all` 会发现并执行当前全部 `dev:*` 命令；`verify:all` 会执行 Round 01–06 验证、全部开发命令和累计文件审计。


## 第六轮弓兵与投射物

```bash
npm run test:bow-soldier
npm run test:projectile
npm run verify:round06
npm run dev:bow-soldier
npm run dev:projectile
npm run dev:ranged-battle
```

正式闭环：

```text
UnitFactory.createByText("弓")
→ BowSoldier（2 伤害、280px 范围、800ms 间隔）
→ Laya.Event.STOPPED
→ SimpleDynamicArrow
→ TargetEnemyBezierMovement（二次贝塞尔、移动目标终点）
→ ProjectileManager 反向遍历更新/注销
→ EnemyBase.hit(2, bowSoldier)
→ Mob0 第四轮死亡与双池回收
```

开发模式不调用 `wx.*`、`tt.*` 或真实服务器。`DevelopmentAnimationDriver` 只在缺少正式 Spine/prefab 时按正式片段时长触发 STOPPED；正式 BowSoldier 仍只依赖 Laya 事件。

`dev:ranged-battle` 输出目标发现、动画开始、STOPPED、箭矢创建、轨迹中间点、命中、Mob0 死亡、重新索敌和逻辑/表现对象池数量。

## 第五轮微型战斗与计划命令

计划要求的命令均为当前累计工程的一部分：

```bash
npm run test:friendly-unit
npm run dev:friendly-unit
npm run dev:micro-battle
npm run verify:round05
npm run verify:all
```

`dev:friendly-unit` 与保留的兼容命令 `dev:friendly-unit-simulation` 执行同一个确定性正式刀兵场景。`dev:micro-battle` 输出完整可观测边界，包括：

- 原符号 `tb.zx[0]`、正式注册键 `刀` 和配置索引 0；
- 初始格坐标、像素坐标、120px 范围、3 点伤害和 0.8 秒间隔；
- 首次发现目标、进入攻击状态、攻击启动和伤害结算时刻；
- 首个 Mob0 死亡、注销、切换到第二目标及最终无目标待机；
- EnemyManager、UnitRegistry、空间索引和双层对象池数量；
- 真实网络请求与微信/字节调用均为 0。

UnitRegistry 的独立行为规格位于 `analysis/behavior/unit-registry.md`。

## 第四轮常用命令


```bash
npm run test:enemy-runtime
npm run test:mob0
npm run test:round04
npm run check:round04
npm run verify:round04
npm run dev:mob0-simulation
```

`verify:round04` 会执行：

- 第四轮 24 项测试，两次确定性复跑；
- 78 项静态完整性检查；
- 第三轮 15 项启动到开战回归；
- 第二轮 NET-01 的 32 项回归；
- 双方 Mob0 完整路径接触模拟；
- 原始 bundle、decoded bundle、index 和 HttpClient 哈希校验。

所有测试均不访问真实服务器，也不调用 `wx.*` 或 `tt.*`。

## 如何运行开发模式

开发入口：`src/bootstrap/DevelopmentBootstrap.js`。它要求显式传入 Laya 运行时或测试 mock，不伪装成微信环境。

```js
const { DevelopmentBootstrap } = require('./src/bootstrap/DevelopmentBootstrap');

const bootstrap = new DevelopmentBootstrap({
  Laya,
  config: {
    enabled: true,
    directBattle: false,
    skipPlatformLogin: true,
    skipRemoteShareConfig: true,
    useLocalPlayerData: true,
    developmentBattleStartDelayMs: 0,
  },
});

await bootstrap.start();
```

### 正常场景流程

```js
config: { directBattle: false }
```

```text
LoadScene → MainScene → MatchScene → BattleScene
```

### 直接进入真实战斗初始化

```js
config: { directBattle: true }
```

该模式仍执行真实的：

```text
GameDataCore.startGame
→ BattleFlowCoordinator.startBattle
→ BattleManager.startGame
→ BattleSceneController 生命周期
→ EnemyFactory / EnemyManager / Mob0Enemy
```

它不会创建静态假战斗页面，也不会绕过 BattleManager。

### 特殊启动直接进入匹配

```js
config: { directBattle: false, forceMatchLaunch: true }
```

这对应 LoadScene 的特殊 MatchScene 分支，不等同于 direct battle。

## Mob0 独立运行模拟

```bash
npm run dev:mob0-simulation
```

该命令使用真实重建类和受控 Laya mock，运行双方 Mob0 的完整路径。当前确定性结果包括：

- 32 次路径点切换；
- 双方 aDou 各受到 1 点伤害；
- 两个逻辑实体进入 `Mob0Enemy` 类池；
- 两个表现节点进入 `mob` 键池；
- 真实网络请求 0；
- 原生平台调用 0。

## 已恢复的敌人运行时

- 继承链：`Mob0Enemy(st) → NormalEnemyBase(pe) → EnemyBase(ro) → EnemyEventProxy(qE)`。
- A*：仅 `0_0`、`0_1` 可走，四方向，无对角线。
- 移动：`position += direction × speed × deltaMs / 1000`。
- 基础速度：50 px/s。
- 固定更新：80ms 子步，单帧累计上限 500ms。
- 接触：500ms 冷却，50ms 延迟，固定 1 点 aDou 伤害。
- 受击：直接扣血、生命条更新、一次性死亡边界。
- 普通死亡表现边界：100ms。
- 空间索引：80px 网格，`id→enemy`、`cell→IDs`、`id→cell` 三组结构。
- 回收：逻辑实体按类回收，Mob0 表现节点按字符串键 `mob` 回收。
- 复用隔离：生命周期 generation 防止旧延迟回调影响新实例。

## 正式逻辑与开发适配器

正式逻辑：

- `src/bootstrap/GameBootstrap.js`
- `src/core/`
- `src/scenes/`
- `src/battle/`
- `src/entities/`
- `src/data/`

开发隔离层：

- `src/bootstrap/DevelopmentBootstrap.js`
- `src/platform/dev/`
- `src/network/dev/`
- `src/battle/dev/`

开发层只解除平台、服务器、动画和缺失资源依赖；不修改第二轮 `HttpClient` 的已验证行为。

## 暂缓的平台和非核心功能

- 微信/字节正式登录与授权
- 用户信息按钮
- 激励视频、Banner、插屏
- 分享、录屏和侧边栏
- 开放数据域、排行榜和正式云存档
- 统计与错误日志真实发送
- 商店、设置、头像和活动 UI

缺失方法不会由 Proxy、空对象或万能 ServiceLocator 自动吞掉。

## 阻止真实画面运行的缺失项

当前控制流和敌人逻辑可在 mock 中运行，但真实 Laya 画面仍缺：

1. `game.js`、`game.json`。
2. `scene/LoadScene.ls`、`MainScene.ls`、`MatchScene.ls`、`BattleScene.ls`。
3. `fileconfig.json`、资源 UUID、分包和实际图片资源。
4. 正式 `mob` prefab/节点结构。
5. `ve`/`uz` Spine 包装、动画事件和真实 Tween。
6. 正式音频、命中、死亡、足迹和掉落表现。
7. `tm` 阿斗表现组件和结算画面。

## 尚未恢复的战斗能力

- 其他基础兵种、武将、完整征兵 UI、拖拽换位和文字合成。
- Boss、Mob1–Mob3、Zombie、Cavalry、Puppet。
- Buff、击退、特殊死亡、碎片掉落的完整分支。
- 武器、子弹、技能和特效系统。
- 完整胜负结算和 GameOverScene。

未注册类型被请求时会明确报错，不会伪造创建成功。

## 关键分析材料

- `analysis/critical-path/enemy-runtime-classgraph.md`
- `analysis/critical-path/enemy-runtime-classgraph.json`
- `analysis/critical-path/enemy-runtime-dependency-closure.json`
- `analysis/mappings/ENEMY-RUNTIME-01-symbol-map.json`
- `analysis/modules/ENEMY-RUNTIME-01.json`
- `analysis/modules/ENEMY-RUNTIME-01-method-coverage.json`
- `analysis/behavior/ENEMY-RUNTIME-01.md`
- `analysis/behavior/enemy-state-machine.md`
- `analysis/behavior/enemy-lifecycle.md`
- `analysis/behavior/enemy-pool-reset-contract.md`
- `analysis/behavior/enemy-target-selection.md`
- `analysis/behavior/enemy-spatial-index.md`
- `analysis/test-results-round-04.json`
- `analysis/static-checks-round-04.json`
- `analysis/round-04-report.md`

## 不可变基线

```text
original/bundle.js
  SHA-256 19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595

work/bundle.strings-decoded.js
  SHA-256 f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b

original/index.js
  SHA-256 4024b47ce0f6832a4aa969a6d7329385f6df4c0bbbfb6f26047fd22920574d1b

src/network/HttpClient.js
  SHA-256 bfb3ffeff499ba0077dfbfcf94c3a94215ff09166a84bbb522f698943ed89189
```

## 第五轮：正式刀兵友军战斗链

本轮恢复了第一个正式友军单位，不使用项目外新增的通用兵种：

```text
工厂索引 0 / 文字键“刀”
→ KnifeSoldier
→ UnitRegistry 注册和正式放置入口
→ BattleManager 固定更新轮询
→ EnemyManager 80px 空间索引
→ 120px 范围内选择 Mob0
→ 0.8 秒攻击冷却
→ 500 / 动画倍率 ms 后结算 3 点伤害
→ Mob0 第四轮死亡、空间注销和双池回收
```

源码确认刀兵是固定站位单位，没有追击移动。所恢复的 `rb → rc → td → knife` 继承闭包也没有友军生命值、受击或死亡接口；因此当前“友军清理”严格按原代码的 `gameOver`、注销和对象池回收处理，不伪造 HP 或死亡规则。

### 运行第五轮测试

```bash
npm run test:friendly-units
npm run test:friendly-combat
npm run test:round05
```

完整静态检查、确定性复跑以及 Round 03、Round 04、NET-01 回归：

```bash
npm run verify:round05
```

### 运行友军微型战斗仿真

```bash
npm run dev:friendly-unit-simulation
```

确定性开发仿真使用真实工厂、UnitRegistry、BattleManager、EnemyManager、KnifeSoldier 和 Mob0Enemy。一级刀兵在固定坐标连续击杀两个首波 Mob0；每个 Mob0 的正式开发首波生命为 6，刀兵每次造成 3 点伤害，因此每个目标需要两次命中。该命令不访问服务器，也不调用 `wx.*` 或 `tt.*`。

### 开发环境生成正式刀兵

`DevelopmentBootstrap.createContext()` 暴露：

```text
context.developmentUnitSpawner
context.unitFactory
context.unitRegistry
context.knifeAttackTimeline
```

开发生成器只接受明确的测试坐标，并且必须经过真实 `UnitFactory → UnitRegistry → activatePlacement` 链路。它不会直接 `new` 单位，也不会绕过放置冲突检查。

### 当前已注册和暂缓的友军类型

已注册：

```text
0 / 刀 / KnifeSoldier
```

显式暂缓并在请求时抛错：

```text
1 / 弓
2 / 枪
3 / 骑
武将、农民及其他文字单位
```

弓兵已确认依赖 `SimpleDynamicArrow`、BulletFactory、弹道、命中策略和表现资源，本轮没有用即时伤害替代真实投射物。

### 第五轮关键分析材料

- `analysis/critical-path/friendly-unit-classgraph.md`
- `analysis/critical-path/friendly-unit-dependency-closure.json`
- `analysis/critical-path/first-friendly-unit-selection.md`
- `analysis/critical-path/first-friendly-unit-stats.json`
- `analysis/mappings/FRIENDLY-UNIT-COMBAT-01-symbol-map.json`
- `analysis/modules/FRIENDLY-UNIT-COMBAT-01.json`
- `analysis/modules/FRIENDLY-UNIT-COMBAT-01-method-coverage.json`
- `analysis/behavior/FRIENDLY-UNIT-COMBAT-01.md`
- `analysis/behavior/friendly-unit-lifecycle.md`
- `analysis/behavior/friendly-unit-pool-reset-contract.md`
- `analysis/behavior/friendly-target-selection.md`
- `analysis/test-results-round-05.json`
- `analysis/static-checks-round-05.json`
- `analysis/round-05-report.md`


## ROUND-07A-SPEAR
- 恢复正式枪兵注册：index=2/text=枪/animationKey=pike。
- 来源：work/bundle.strings-decoded.js:24556-24749。
- 未恢复完整 sv/vA 枪击对象池依赖，保留到后续 Weapon/Bullet 阶段。

## ROUND-07B-CAVALRY
- ROUND-07B-CAVALRY: restored CavalrySoldier and cavalrySweep direct attack dependency.


## ROUND-07C-PROJECTILE-RUNTIME
Public projectile runtime extended with separate movement and hit strategy modules.

## v0.7.8 ROUND-07H
Combat foundation repair completed.

## v0.7.11 Buff runtime

新增 `src/buffs/`：20 个正式 Buff type、数值/状态/自定义处理器、冲突与持续时间、Unit/Enemy 集成。开发启动器现在使用真实 `BuffManager`，不再使用生命周期占位服务。

## v0.7.12 core content runtime

The cumulative reconstruction now includes:

- 19 registered skill classes with cooldown/activation/lifecycle management.
- 12 registered Boss classes with shared BossBase runtime.
- Mob1, Mob2, Mob3, Zombie, Cavalry and Puppet enemy classes.
- BossFactory/BossManager and WaveManager.
- Normal waves, boss-wave probability, forced boss rounds and boss rotation.

Run the development smoke command:

```bash
npm run dev:core-content
```

Complex skill and boss visual effects are intentionally isolated behind effect contracts so the combat framework can continue to be developed without the missing Laya scenes, prefabs and Spine assets.


## Round 07M

技能表现端、目标 Boss 技能、Boss 非即时动画时间线和 GameOver 核心已接入。资源缺失清单见 `docs/MISSING_SKILL_BOSS_RESOURCES.md`。


## Round 07M — Skill/Boss presentation and GameOver
Run `npm run check:round07m` for static validation and `npm run dev:skill-gameover` for the deterministic no-resource skill/GameOver simulation. Missing formal assets are listed in `docs/MISSING_SKILL_BOSS_RESOURCES.md`.

## Round 08: Playable core combat / Unity handoff

Run the deterministic formal single-game flow:

```bash
npm run dev:single-game
```

Export engine-neutral data for Unity:

```bash
npm run export:unity
```

Unity port documentation is under `unity-handoff/`. The formal single-game path uses DeckManager, BattleEconomy, BattleInputController, UnitFactory, UnitRegistry, WeaponManager and AIController; it does not use DevelopmentUnitSpawner or a manual gameOver call.


## Unity 核心战斗接入文档

完整 Unity 迁移参考入口：

```text
unity-handoff/README.md
```

其中 `reference/` 为详细架构与规则文档，`csharp-reference/` 为 C# 接口和实现骨架。
