# ADou 核心战斗还原任务与进度

> 状态：`进行中`  
> 初始建立时间：2026-08-03  
> 原始任务来源：[ADouToDo.txt](./ADouToDo.txt)  
> 参考基准：[CORE_COMBAT_STATUS.md](D:/UnityProject/MyTEngine/origin/reconstructed-project-core-combat-unity-docs-v0.8.1/reconstructed-project/analysis/CORE_COMBAT_STATUS.md)

## 本轮路径纠正与工程同步（2026-08-03）

- 正式执行工程：`D:\UnityProject\MyTEngine\TEngine\Origin\reconstructed-project`
- 参考来源工程：`D:\UnityProject\MyTEngine\origin\reconstructed-project-core-combat-unity-docs-v0.8.1\reconstructed-project`
- 路径纠正：本轮起所有代码修改只允许写入正式执行工程；参考来源工程仅作为只读同步来源，不在其中继续修改代码。
- 同步范围：将参考来源工程完整同步到正式执行工程，保持目录结构；同步前两侧均为 866 个文件，来源独有文件 0 个，正式工程额外文件 0 个；不执行删除操作。
- 冲突记录：同步前发现 6 个同名文件内容不同。以下记录正式工程旧文件 SHA-256 → 来源文件 SHA-256；本轮按任务要求以来源版本覆盖正式工程，并保留本记录。
  - `src/generals/GeneralUnit.js`：`7D861E4A76C5074710194292294B1426CDAD7C662EDFBB3BF3D5762B474B3DBF` → `64053C3EDEF9DCD7FA81A4C778206A07869615F86CDB8D7AA61A6ACD87AEDE1D`
  - `src/generals/GeneralFactory.js`：`D1A4395F41C82F42FB71BEF46C32A264576E84EE29FA0F5BAEE1142728072FA1` → `3B3D2A2F84284760319A18FFEA589E561E1140C9113E97D514F9F0A2FD6F53A3`
  - `src/units/UnitRegistry.js`：`76BF2D25A6F211963C12D8B5F82B2BAE9B709A202E3A962A74B2C30D49AADB35` → `75AA1E3B792A4E352B94C651D0B4870EC0A4500234E58D64BB8DC560F9874C62`
  - `src/battle/BattleManager.js`：`875939EE5AA5A362D641850E49C0AD6B10ADEC272EF5899C1886BBBCB81B0F1E` → `9683BAC9D1F9FF409507159225ABB5E962E872B4B47FCC8493CCA288D498F4DB`
  - `src/weapons/WeaponBase.js`：`B627498F190E8F8D07265DDC469DE55E3C0F58E8BE0B18F1E05A752F8B3359E4` → `4F2355B970AD8793AE405AD258D1ECBE3F3AD3354DB8F0B1854A9FE98822E2DC`
  - `src/weapons/WeaponManager.js`：`F78DDBF8C452B7964658E8DF60D3C9E2D2B2AF95728CB92291901D207FCBC73C` → `EB6960C55ED19991373198F96E567EF1F433C8FA316D90E8A00511193949226A`
- 本轮限制：只做路径纠正、工程同步、文件差异检查和 JavaScript 语法检查；不执行功能开发、测试或 Unity/TEngine 验证。
- 同步后检查：两侧均为 866 个文件，来源独有文件、正式额外文件和内容差异均为 0；6 个指定代码文件与参考来源哈希一致。
- 语法检查：正式工程 `src` 下 218 个 JavaScript 文件全部通过 `node --check`；工程内全部 439 个 JavaScript 文件中，`work` 历史抽取片段有 62 个不是独立语法单元而无法单独通过检查，未对其做修改。

## 使用规则

1. 本文件是执行清单和唯一进度记录，不直接修改原始 `ADouToDo.txt`。
2. 每次工作开始前，先更新对应任务的“状态”“完成记录”和“最后变动”。
3. 每次工作结束后，在“变更记录”末尾追加一条记录；不得删除历史记录。
4. 任务状态只使用以下值：
   - `未开始`：尚未进入实现或调查。
   - `进行中`：正在实现、验证或处理阻塞项。
   - `已完成`：已达到任务验收标准。
   - `阻塞`：存在明确外部依赖或无法继续的问题，需要记录原因。
5. 任务完成前不标记为“已完成”；测试、烟测和 Unity/TEngine 接入验证按阶段 6 单独记录。

## 总体进度

| 范围 | 任务数 | 已完成 | 进行中 | 阻塞 | 当前进度 |
|---|---:|---:|---:|---:|---:|
| P0 核心战斗闭环 | 4 | 4 | 0 | 0 | 100% |
| P1 核心扩展 | 6 | 2 | 0 | 0 | 33% |
| P2 非纯逻辑内容 | 4 | 0 | 0 | 0 | 0% |
| 执行阶段 | 6 | 3 | 2 | 0 | 50% |

当前执行顺序：阶段 1 → 阶段 2 → 阶段 3 → 阶段 4 → 阶段 5 → 阶段 6。

## P0：核心战斗闭环

### P0-01 武将完整战斗逻辑

- 状态：`已完成`
- 优先级：P0
- 目标：让合成后的武将能够像普通士兵一样参与战斗。
- 工作项：
  - [x] 在 `GeneralUnit` 中完成可注入的纯逻辑攻击循环和攻击调度，并接入 `BattleManager`。
  - [x] 实现目标选择、攻击范围、攻速和伤害计算；使用已恢复的等级倍率，基础战斗数值保持可注入。
  - [x] 接入武器装备后的属性影响；统一处理攻击力、攻击范围和攻速修正，并在替换、移除和回收时清理。
  - [x] 实现经验获取、升级和等级属性刷新。
  - [x] 接入武将技能触发入口。
  - [x] 实现武将死亡、回收和部件解绑。
- 验收标准：字形部件 → 合成武将 → 装备武器 → 搜索目标 → 发起攻击 → 命中敌人 → 武将回收，流程中的状态和数据均可连续传递。
- 完成记录：2026-08-03 完成攻击状态机、攻击冷却、目标查询、攻击派发和 `BattleManager` 调度接入；补齐默认最近目标选择、攻击范围、等级伤害倍率和等级攻速倍率计算；接入武器攻击力/范围/攻速修正及武器替换、移除、回收清理；完成经验累计、击杀经验分发、可注入升级阈值、等级刷新和等级属性倍率刷新；完成可注入 `skillKey` 的技能附着、`BattleManager` 触发入口、SkillManager 激活和回收清理；完成死亡状态、幂等回收、武器/技能清理、部件解绑和注册表移除。六个技能的具体效果仍属于 P0-03。
- 最后变动：`[完成] 2026-08-03` 完成 P0-01 全部工作项；阶段 1 的正式 Unity/TEngine 验收仍由阶段 6 统一执行。

### P0-02 武器实际攻击逻辑

- 状态：`已完成`
- 优先级：P0
- 参考文件：[Weapon.js](D:/UnityProject/MyTEngine/origin/reconstructed-project-core-combat-unity-docs-v0.8.1/reconstructed-project/src/weapons/types/Weapon.js)
- 工作项：
  - [x] 移除普通武器的空攻击或占位实现。
  - [x] 恢复普通武器攻击、伤害、范围和攻速。
  - [x] 恢复七星刀、铁弓、霸王弓、诸葛连弩等特殊效果。
  - [x] 建立武器与投射物、Buff、攻击效果之间的连接。
- 验收标准：每个已恢复武器都有明确攻击类型和效果；特殊概率、Buff、投射物和范围伤害可被独立调用。
- 完成记录：2026-08-03 完成普通武器直接命中、武器攻击效果对象和全部注册武器的明确攻击类型；恢复七星刀 10% 五枚流星/2 倍范围伤害、铁胎弓火焰灼烧、铁弓击退、霸王弓 50% 弹射和诸葛连弩十次普通射击后十支火箭；弓类攻击上下文、投射物、Buff 和特殊影响结算已连通。针对性新增 5 项武器用例及既有 6 项投射物用例通过；具体表现资源和未确认的原始数值仍由表现层/后续任务处理。
- 最后变动：`[完成] 2026-08-03` 完成 P0-02 纯逻辑实现。

### P0-03 六个武将主动技能

- 状态：`已完成`
- 优先级：P0
- 工作项：
  - [x] 跳斩。
  - [x] 七进七出。
  - [x] 战吼。
  - [x] 圣剑。
  - [x] 箭雨。
  - [x] 火箭齐射。
- 实现约束：每个技能使用独立的纯逻辑效果对象，不直接依赖 Unity/Laya 表现层。
- 验收标准：六个技能均有可触发、可结算、可清理的逻辑效果，并能由武将战斗流程调用。
- 完成记录：2026-08-03 依据 OpenSpec 提案 `general-active-skills`（dependsOn `general-combat-wiring`）实现六个武将主动技能纯逻辑 effect：新增 `src/skills/effects/` 下 `BattleShoutEffect`/`HolySwordEffect`/`ArrowRainEffect`/`FireArrowBarrageEffect`/`LeapSlashEffect`/`SevenInSevenOutEffect` + 共用 `effectTargets.js`；`SkillEffectPort._installCoreHandlers` 注册六个 handler（替换原 `DEFERRED_EFFECT_WITH_EXACT_CONTRACT`），并补 `projectileManager`/`attackEffectManager` 到 services、新增 `onOwnerAttack` 每次攻击 hook 通道；`EffectHandle` 扩展 `onOwnerAttack`；`GeneralUnit.attack` 增一行 guarded hook 通知跳斩溅射。效果对齐 bundle 取证：跳斩 5 次 50% 溅射 + `guanYu_skill_roar`（bundle:38497/45983/45942）、战吼 2000ms STUN（45659）、圣剑范围伤害 + KNOCKDOWN + `holyBlade_skill`（45902/45687/45696）、箭雨多支箭经 ProjectileAttackEffect/ProjectileManager（46141/44798/44748）、火箭烈 `n=floor(max(1,(level-1)/2))` + `k=range(1,3,true)*n` + `DEFERRED_PROJECTILE_VARIANT`（46145/45744/45746/45877）、七进七出 7 次突进（45655）。回收复用已 wired 的 `removeOwner→clearOwner` 链路。新增 `tests/unit/GeneralActiveSkills.test.js` 13 项用例全部通过；全量 234 个 src 文件 `node --check` 通过；武将/统一攻击回归 15 项全部通过；未运行 Unity/TEngine 验证。火焰箭专属弹种以 `DEFERRED_PROJECTILE_VARIANT` 标记待提案 ④。
- 最后变动：`[完成] 2026-08-03` 完成 P0-03 全部六个技能纯逻辑 effect 与测试；Unity/TEngine 接入验收仍由阶段 6 统一执行。

### P0-04 枪兵、骑兵正式攻击时序

- 状态：`已完成`
- 优先级：P0
- 工作项：
  - [x] 恢复原始动画事件对应的结算时机。
  - [x] 建立统一攻击效果管理器。
  - [x] 建立攻击对象池。
  - [x] 处理多目标命中和清理顺序。
  - [x] 将枪兵、骑兵中直接 `update()` 的攻击回退逻辑替换为统一攻击效果管理。
- 验收标准：攻击触发、命中、伤害结算、对象回收和多目标清理顺序稳定且可复用。
- 完成记录：2026-08-03 新增 `AttackScheduler`、`AttackResolver`、`MeleeAttackEffect`、`ProjectileAttackEffect` 和 `AttackEffectManager`；`BattleManager` 接入统一调度和效果更新；枪兵、骑兵和刀兵效果接入统一管理；正式 `ObjectPool` 已用于攻击效果获取/回收；范围命中去重、延迟结算和回收清理已覆盖。2026-08-03 依据原始抽取恢复枪兵攻击动画前段 90ms 转向 + 270ms 突刺后的 `360ms / 播放倍率` 命中点，以及骑兵两次横扫统一 `150ms` 延迟、各半攻击力和半径/完整范围；开发表现桩已支持枪兵与骑兵动画。2026-08-03 将弓兵在原始 `STOPPED/650ms` 发射点创建的投射物改由 `ProjectileAttackEffect` 登记、更新、回收和单位回收取消。2026-08-03 新增 `WeaponAttackLifecycleEffect`，将武将普通武器的命中结算延迟到统一管理器更新阶段，并将武将弓类产生的投射物登记到同一管理器；武将回收时会取消未完成攻击效果。2026-08-04 依据 OpenSpec 提案 `attack-timing-finalization` 闭合原始动画事件精确结算接缝：枪兵 `PikeAttackEffect` 增 `animationEventTimingProvider`/`calibrateHitTiming` 校准钩子（`MeleeAttackEffect.calibrateHitTiming` 默认 no-op，无 provider 回退 `360/rate` 常量基线，规则层命中仍由 `AttackEffectManager.update()`→`hit()` 驱动不倒退）；弓兵 `BowSoldier`/`DevelopmentAnimationDriver` 注释固化 STOPPED 正式动画事件契约（dev 桩为无 Spine 回退）；骑兵 sweep/枪尖 Qx 经 `LayaEnemyPresentation` 5 个表现 port 方法（DEFERRED 桩 no-op）+ `CavalrySweepEffect`/`PikeAttackEffect` port 调度建立契约（实体 VFX 归 P2）；刀兵 `KnifeAttackEffect` 注释固化原始 Laya timer 方案（管理器只跟踪生命周期不推进计时）。新增 5 个时序用例测试文件 33 项全通过，既有统一攻击回归 13 项通过，全量 263 个 src 文件 `node --check` 通过，`test:attack-timing` 脚本 33 项通过。
- 最后变动：`[完成] 2026-08-04` 依据 OpenSpec 提案 `attack-timing-finalization` 闭合原始动画事件精确结算接缝：枪兵命中时机支持动画事件校准钩子（`MeleeAttackEffect.calibrateHitTiming` no-op + `PikeAttackEffect.animationEventTimingProvider`/`calibrateHitTiming`，无 provider 回退常量基线 `360/rate`，规则层仍管理器驱动不倒退）、弓兵 STOPPED 发射点契约文档化（正式动画事件驱动，`DevelopmentAnimationDriver` 为无 Spine 回退桩）、骑兵 sweep/枪尖 Qx 表现 port 契约（`LayaEnemyPresentation` 5 桩 DEFERRED no-op + `CavalrySweepEffect`/`PikeAttackEffect` port 调度，实体 VFX 归 P2）、刀兵时序文档化为原始 Laya timer 方案（管理器只跟踪生命周期不推进计时）；新增 5 个时序用例测试文件 33 项全通过，既有统一攻击回归 13 项通过，全量 263 个 src 文件 `node --check` 通过，`test:attack-timing` 脚本 33 项通过。P0-04 全部工作项完成，状态由「进行中」转「已完成」。

## P1：核心扩展

| 编号 | 任务 | 状态 | 验收要点 | 最后变动 |
|---|---|---|---|---|
| P1-01 | 特殊投射物和剩余武器效果 | 部分完成 | 投射物生命周期、命中效果和剩余武器特性可独立调用 | `[完成] 2026-08-03` |
| P1-02 | 非 Mob0 敌人的专属行为 | 已完成 | 不同敌人类型拥有与报告一致的行为分支；NormalEnemyBase 灵魂投射 sB + 吹飞 Xw/Gw 通用能力恢复；enemies.json 7 类敌人数值填入；敌人行为用例 60 项通过、Mob0 回归 15 项通过 | `[完成] 2026-08-03` |
| P1-03 | 友军受击契约确认与武将生命周期 | 部分完成 | 确认友军无受击契约（已忠实实现 `UnsupportedFriendlyUnitDamageError`；原始游戏友军无 HP／护甲／护盾／死亡机制，敌人攻击承受方为阿斗 BattleTarget）；武将 die/recycle 生命周期注入拆分至后续 P0 任务 | `[重定义验收] 2026-08-03` |
| P1-04 | 完整 AI 策略 | 已完成 | 5 步状态机 TG/4 级难度 Si/主动刷牌+周期收入+道具使用契约/难度分层放置 WX/子控制器 bG+MG+阵营模板 AG/用例与难度 0 等价/DEFERRED 回归均完成；smoke 回归 8.2 已确认 PASS（44/44 任务全部通过） | `[完成] 2026-08-03` |
| P1-05 | 卡牌拖拽、农民和完整字形合成交互 | 未开始 | 拖拽、农民操作和字形合成流程可完整执行 | `[新增] 2026-08-03` |
| P1-06 | 完整等级阈值和部分原始数值 | 未开始 | 等级阈值及缺失原始数值补齐并被战斗逻辑使用 | `[新增] 2026-08-03` |

## P2：非纯逻辑内容

| 编号 | 任务 | 状态 | 验收要点 | 最后变动 |
|---|---|---|---|---|
| P2-01 | Unity/Laya 场景、Prefab、Spine、Tween | 未开始 | 逻辑对象具备对应表现层承载 | `[新增] 2026-08-03` |
| P2-02 | 图片、粒子、音频、特效资源 | 未开始 | 所需资源接入并可按生命周期加载、释放 | `[新增] 2026-08-03` |
| P2-03 | BattleScene/GameOverScene 完整 UI | 未开始 | 战斗场景和结算场景 UI 流程完整 | `[新增] 2026-08-03` |
| P2-04 | 微信登录、广告、分享、排行、云存档 | 未开始 | 外部服务功能具备明确接口和接入验证记录 | `[新增] 2026-08-03` |

> P2 表现层准备（2026-08-04，依据 OpenSpec 提案 `gap-sweep-and-presentation` 阶段 2）：P2 表现层缺口清单 `docs/p2-presentation-gap-catalog.md` 与接入契约 `docs/p2-presentation-integration-contract.md` 已产出，覆盖 P2-01..04（现状+缺口+bundle/资源证据 + 现有 port 体系盘点 + 4 层架构接入边界 + DEFERRED 桩约定）。P2-01..04 实体实现（VFX/音频/场景/微信）仍为「未开始」——本变更 Non-Goal 不实现任何 VFX/音频/场景实体，清单+契约作为后续独立 P2 proposal 的输入。

## 分阶段执行清单

### 阶段 1：武将战斗闭环

- 状态：`已完成`
- 目标：让合成后的武将能够像普通士兵一样参与战斗。
- 关联任务：P0-01。
- 交付物：`GeneralCombatUnit`、武将攻击调度、目标选择、伤害/攻速/范围计算、等级和经验、武器挂载、死亡和回收。
- 完成条件：完成 P0-01 全部工作项，并通过“字形部件 → 武将回收”的逻辑链路检查。
- 完成记录：2026-08-03 完成武将攻击循环、经验升级、等级属性刷新、技能触发入口、死亡回收和部件解绑；阶段 1 纯逻辑工作项已完成，Unity/TEngine 接入验证保留至阶段 6。
- 最后变动：`[完成] 2026-08-03` 完成阶段 1 纯逻辑交付。

### 阶段 2：统一攻击系统

- 状态：`已完成`
- 目标：统一刀、弓、枪、骑和武将的攻击流程。
- 关联任务：P0-04，并为 P0-02、P0-03 提供公共接口。
- 建议组件：
  - [x] `AttackScheduler`
  - [x] `AttackResolver`
  - [x] `MeleeAttackEffect`
  - [x] `ProjectileAttackEffect`
  - [x] `AttackEffectManager`
- 完成条件：枪兵、骑兵和武将能够通过统一的攻击效果流程发起攻击、结算命中并清理对象。
- 完成记录：2026-08-03 完成统一攻击组件骨架、对象池、近战效果生命周期和枪兵/骑兵/刀兵接入；刀兵的 500ms 命中仍委托原始动画定时器精确驱动。2026-08-03 补齐枪兵 `360ms / 播放倍率` 命中与骑兵双横扫 `150ms` 延迟、伤害和范围参数，并让开发表现桩可承载枪兵/骑兵动画。2026-08-03 将弓兵在 `STOPPED` 后创建的投射物纳入 `ProjectileAttackEffect`，由统一管理器负责登记、完成检测、对象池回收和单位回收取消；2026-08-03 将武将普通武器命中包装为 `WeaponAttackLifecycleEffect`，并将武将弓类投射物纳入 `ProjectileAttackEffect`，覆盖延迟结算、管理器更新、效果回收和武将回收取消。
- 最后变动：`[完成] 2026-08-04` 依据 OpenSpec 提案 `attack-timing-finalization` 闭合原始动画事件精确结算接缝（枪兵动画事件校准钩子 + 弓兵 STOPPED 契约文档化 + 骑兵 sweep/枪尖 Qx 表现 port 契约 + 刀兵时序文档化 + 5 个时序用例 33 项 + 既有回归 13 项 + 全量 263 个 src `node --check` + `test:attack-timing` 33 项全通过）；阶段 2 原始动画事件精确结算接缝闭合，状态由「进行中」转「已完成」。

### 阶段 3：恢复武器逻辑

- 状态：`进行中`
- 目标：恢复武器的实际攻击和特殊效果。
- 关联任务：P0-02、P1-01。
- 执行顺序：
  1. [x] 七星刀
  2. [x] 铁弓/铁胎弓
  3. [x] 霸王弓
  4. [x] 诸葛连弩
  5. [x] 落日弓
  6. [x] 其他普通武器
- 完成条件：`Weapon.js` 不再返回空攻击；每个武器都有明确攻击类型、伤害和效果定义。
- 完成记录：2026-08-03 完成七星刀、铁弓/铁胎弓、霸王弓、诸葛连弩、落日弓及其他已注册武器的纯逻辑攻击接入；阶段整体仍受 P1-01 剩余武器效果范围约束。
- 最后变动：`[完成子项] 2026-08-03` 完成 P0-02；阶段仍需处理 P1-01 的剩余武器效果。

### 阶段 4：恢复武将主动技能

- 状态：`已完成`
- 目标：恢复六个武将主动技能的纯逻辑效果。
- 关联任务：P0-03。
- 执行顺序：战吼 → 圣剑 → 箭雨 → 火箭齐射 → 跳斩 → 七进七出。
- 完成条件：每个技能都能独立触发、结算和清理，并能接入武将技能触发入口。
- 完成记录：2026-08-03 按 OpenSpec 提案 `general-active-skills` 完成六个技能 effect、SkillEffectPort 注册与 onOwnerAttack hook；新增 `tests/unit/GeneralActiveSkills.test.js` 13 项用例覆盖触发/结算/清理/DEFERRED 回归/recycle 回归，全部通过；全量 234 个 src 文件语法检查通过；武将/统一攻击回归 15 项通过。阶段 4 纯逻辑工作完成，Unity/TEngine 接入验证保留至阶段 6。
- 最后变动：`[完成] 2026-08-03` 完成阶段 4 纯逻辑交付。

### 阶段 5：补齐战斗边界

- 状态：`进行中`
- 目标：补齐核心战斗之外但会影响战斗闭环的边界行为。
- 关联任务：P1-01 至 P1-06。
- 工作项：
  - [x] 友军受击契约确认（原始游戏友军无受击／血量／死亡机制，已忠实实现 `UnsupportedFriendlyUnitDamageError` 拒绝）。
  - [x] 非 Mob0 敌人特殊行为。
  - [x] 特殊投射物（P1-01 ④a/④b 纯逻辑层完成：24 武器 effect + 5 属性修正 + 23 弹种实体 + 8 移动策略 + 武器技能投射物实体连接；VFX/渲染为 P2 非目标）。
  - [x] Boss 技能边界。
  - [x] AI 高级策略。
  - [ ] 完整卡牌、拖拽和合成流程。
- 完成条件：P1 任务全部完成，且不破坏阶段 1～4 已完成的战斗流程。
- 完成记录：2026-08-03 依据 OpenSpec 提案 `special-weapons-projectiles`（④a 武器效果 + ④b 投射物实体）完成 P1-01 特殊投射物与剩余武器效果纯逻辑层：24 把特殊非弓武器每把有专属 effect（经 `WeaponSpecialEffects.js` 分派，晕眩/跌倒/攻速/金币经现有 BuffType 与经济路径，不引入新 BuffType）、5 把属性武器加成数值生效（铁剑 +3 攻、大戟 +1 距、长剑/长刀/长枪 +0.5 距）、23 弹种覆盖 bundle 注册全集（16 新建 + 5 壳补全 + 1 误标 ShenBiPunch→ShenBiArrow 校正）、8 移动策略接入 `resetData` 真实生命周期、武器技能投射物实体连接（七星刀流星雨→StarBullet、诸葛连弩火箭雨→FireArrow、火龙→FireDragonArrow 校正 IronBow 退化、陨石→新增 `MeteorStrikeEffect` 经 StaticFireBall/GroundSpikeBullet 孤子弹种承载）。取证偏差已标注（青龙偃月刀倍率/君子小人剑伤害 PARTIAL、陨石 bundle 原始为纯特效 DEFERRED、铁枪 1 个枪阵）。新增 `tests/unit/WeaponSkillProjectiles.test.js` 10 项、`SpecialWeaponEffects.test.js`/`AttributeWeaponBonuses.test.js`/`ProjectileTypes.test.js`/`ProjectileMovement.test.js` 全部通过；全量 259 个 src 文件 `node --check` 通过；弓兵/投射物/武将主动技能回归 85 项通过；`test:special-weapons`/`test:projectile-types` 脚本已加入 package.json。未运行 Unity/TEngine 验证。2026-08-03 依据 OpenSpec 提案 `non-mob0-enemy-behaviors`（dependsOn 无）完成 P1-02 非 Mob0 敌人专属行为：Zombie 沼澤浮現狀態機+氣泡粒子（`gB` 三阶段/`bubble`/`uB`/`tB`/`dB`/`Hw`/`fw`/`mw` 7 方法）、Cavalry 黄圈光环（`yellowCircle.png` 80×30 zIndex=-1）+骑兵呼吸（0.78→0.82→0.8 130ms）、Puppet 爱心粒子（300ms 周期/0.1~0.5 缩放/放大 1/3000/淡出 1/1000）+速度 10+`yt` 路径事件订阅+待机 0.9；NormalEnemyBase 灵魂投射 `sB`（typeIndex!=1+塔 Ci+num<3+距离<range，飞行 300ms 发 `ENEMY_SOUL_DELIVERED`）+吹飞 `Xw`（贝塞尔 QE/`hit(Zi-0.1)` 濒死/旋转/注册 `Gw`）/`Gw`（`time+=deltaMs/200` 贝塞尔插值/`time>=1`→`hit(1)` 致死）+gameOver 吹飞清理；`EnemyBase.configure` 增 `soulTowerResolver`/`soulFlightManager` 可注入接口（DEFERRED 桩默认不触发）；`LayaEnemyPresentation` 增 Zombie/Cavalry/Puppet 表现 port 方法。enemies.json 填入 7 类敌人基础数值（`healthByWave` 20 波 + typeIndex/speed/healthModifier，Zombie÷2 `bundle:31386`、Cavalry 80 `bundle:32398`、Puppet 10 `bundle:31793` +×Sh `bundle:12149`、typeIndex 回退 `bundle:11619`），每数值标注 bundle 行号。新增 7 个敌人行为测试文件合计 60 项用例全部通过；Mob0 既有敌人测试回归 15 项通过；全量 src JavaScript 文件 `node --check` 通过；`test:enemy-behaviors` 脚本已加入 package.json。未运行 Unity/TEngine 验证。2026-08-03 依据 OpenSpec 提案 `ai-advanced-strategy`（dependsOn 建议 `general-combat-wiring`，已完成）完成 P1-04 完整 AI 策略纯逻辑实施：`AIController.js` 移除 62 行极简占位（`deployUntilReady`/`tryDeploy`/`choosePlacement` 单一最远格策略），恢复 5 步状态机 `TG`（step1 `Ji>=gi`→`refresh`+step2 / `ni[Si]` 概率→`UG`/`YO`；step2 `Xi=true`+`bG.YX`+`XX>=5`→step3；step3 `KX[0]<PA.sb.length`→`bG.ZX`/否则 step4；step4 `rp.filter`+`MG.tG/iG/hG/aG`→step5；step5 `cG[0]<nG.length`→`MG.lG`/回 step1）+ `update` 按 `fG` 驱动；4 级动态难度 `Si`（0-3 钳制，`Tu(±1)` 升降级经 rank 表，`fG=[2000,1500,1000,500]ms`、`ni`/`ri`/`ii[Si][i]` 收入随 Si，`ii[3]=[20,20,20,20,20,20]`/`ei=[3,5,8,11,14,17]` hu 解码确认）；主动刷牌 `refresh`（type:2）+周期收入 `PG`（订阅 `WAVE_STARTED`，`ai加钱` 日志）+道具使用 `YO`（`hu[101]`=5000ms 冷却）/`Yb`（按 type 分派，`✅`/`❌` 日志）；难度分层放置 `WX`（`Si<2` 随机洗牌 `np.Ys`/`Si>=2` `DX`+`TX` 评分排序，Si=3 `OG` 寻路 DEFERRED 退化为 0，Si=2 前5洗牌 unshift）；子控制器 `AIDeploymentController`(bG) `YX`/`ZX`/`HX`/`$X`/`NX`/`qX`（`Si<2` 乘 `[.2,.3][Si]` 弱化）+`AIPlanningController`(MG) `tG`/`iG`/`hG`/`aG`/`lG`；阵营模板 `AITemplateResolver`(AG) `qj.kX`(Si>=2)/`qj.yX`(Si<2) 模板缓存，武将项 `DEFERRED_GENERAL_TEMPLATE` 空占位不阻塞基础单位；`AIDifficultyConfig.js`+`ai-difficulty.json` 配置加载（每数值标注 bundle 行号来源）；`gameOver` 重置 `step=1`/`yG=0`/`XX=0`/`KX`/`cG`/`sG`/`kG`/`SG`。难度 0 等价路径验证（`fG=2000ms`/`ni=0.001`/`WX` 随机/`qX`×0.2/`ii[0]` 全 0/道具 DEFERRED 桩 no-op）确认弱策略不破坏 smoke。DEFERRED 标注回归：`itemEffectDispatcher`/武将模板/`rankTableResolver`/`qj.bX` 寻路均未自行补成原版。本 change tasks.md 共 44 个任务，全部 PASS（44/44）；8.2 单局 smoke 回归 `FullMicroBattle` 跑通 GameOver、8.3 全量 src `node --check`、8.4 `package.json` 增 `test:ai-strategy` 脚本、8.5 本文档收尾均已完成，8.2 已由独立校验 subagent 确认 PASS。未运行 Unity/TEngine 验证。
- 最后变动：`[完成子项] 2026-08-03` 完成 P1-04 完整 AI 策略纯逻辑实施（5 步状态机 TG/4 级难度 Si/主动刷牌+周期收入+道具使用契约/难度分层放置 WX/子控制器 bG+MG+阵营模板 AG/用例与难度 0 等价/DEFERRED 回归均完成）；`[完成] 2026-08-03` 8.2 单局 smoke 回归已由独立校验 subagent 确认 PASS（本 change tasks.md 44/44 任务全部通过，含 8.2 `FullMicroBattle` 跑通 GameOver、8.3 全量 src `node --check`、8.4 `test:ai-strategy` 脚本、8.5 本文档收尾），P1-04 状态由"进行中"转"已完成"；`[完成子项] 2026-08-04` 依据 OpenSpec 提案 `gap-sweep-and-presentation` 完成 Boss 技能 12/12——Inspire(鼓舞)/CavalryOrder(铁骑号令) 从 `SkillEffectPort` inline lambda 迁出为独立效果类文件 `src/skills/effects/InspireEffect.js`/`CavalryOrderEffect.js`（经 effects barrel 导出，`SkillEffectPort` 以 new 实例化注册，与其余 10 个模式一致），12 个 Boss 技能全部有独立效果类文件，`check:round07m` 确认 12 requiredHandlers（含 Inspire/CavalryOrder）+ bossCount=12、单测 13/13 通过，阶段 5 工作项「Boss 技能边界」由「未开始」转「已完成」；阶段 5 仍因卡牌/拖拽/合成等 P1 任务（P1-05/P1-06）未开始而保持进行中。

### 阶段 6：统一验证

- 状态：`未开始`
- 目标：在阶段 1～5 完成后，再进行测试和引擎接入验证。
- 工作项：
  - [ ] 纯逻辑单元测试。
  - [ ] 单场战斗烟测。
  - [ ] Unity/TEngine 接入验证。
- 当前限制：阶段 6 的正式验收尚未开始；本轮仅执行与本次修改直接相关的 JavaScript 语法检查和针对性逻辑用例，未运行 Unity/TEngine 验证。2026-08-04 验证链部分探查（非阶段 6 正式验收）：`verify:round03` PASS（15 tests × 2 runs fail=0）；`verify:round04`/`verify:round05` 仅静态"必备文档文件"检查 FAIL（缺 `analysis/modules/ENEMY-RUNTIME-01.json`/`FRIENDLY-UNIT-COMBAT-01.json` 等 4 文档产物，git 证据确认从未存在、非本变更删除，tasks.md 无产出这些 analysis 文档的任务），测试维度 fail=0（round04 24/24、round05 25/25、回归全过）；`dev:skill-gameover` smoke 末尾 `title==='胜利'` 断言失败（GameOverSceneController 无 title 节点，属 P2 表现层 UI 缺口、非 Boss 技能回归）。上述 verify 失败与 smoke 断言均预先存在、非本变更范围，阶段 6 仍为「未开始」，待正式验收处理。
- 完成条件：三类验证均有结果记录，失败项已回到对应任务修复并重新验证。
- 完成记录：暂无。
- 最后变动：`[新增] 2026-08-03` 建立阶段。

## 变更记录

| 时间 | 类型 | 变更内容 | 影响范围 |
|---|---|---|---|
| 2026-08-03 | `[新增]` | 根据 `Origin/ADouToDo.txt` 建立本任务与进度文件，拆分 P0/P1/P2 任务、6 个执行阶段、状态规则和验收标准。 | 全部任务 |
| 2026-08-03 | `[完成子项]` | 为 `GeneralUnit` 增加可注入的纯逻辑攻击循环：攻击状态、冷却、目标选择、攻击派发、状态回退和运行时清理；`GeneralFactory`/`UnitRegistry` 支持传入战斗契约；`BattleManager` 改为调度已配置武将。仅完成攻击循环，不填入尚未确认的武器、伤害和范围原始数值。 | P0-01、阶段 1 |
| 2026-08-03 | `[完成子项]` | 为 `GeneralUnit` 增加范围内目标排序、最近目标默认策略、可注入目标策略、等级伤害倍率、等级攻速倍率、攻击力/范围/攻速修正字段；基础数值仍由战斗契约提供，未猜测武将专属原始数值。 | P0-01、阶段 1 |
| 2026-08-03 | `[完成子项]` | 为武器基类增加统一战斗属性修正接口；武将装备、替换、移除和回收时同步应用/清理武器攻击力、范围和攻速修正；`WeaponManager` 改走武将挂载入口；未恢复具体武器攻击分支。 | P0-01、阶段 1；不标记 P0-02 完成 |
| 2026-08-03 | `[路径纠正/工程同步]` | 将参考来源工程完整同步到正式执行工程；同步前记录 6 个同名差异文件并以来源版本覆盖，未删除正式工程额外文件；后续代码任务只修改正式执行工程。本轮不进行功能开发、测试或 Unity 验证。 | 正式执行工程、路径与同步边界 |
| 2026-08-03 | `[完成]` | 完成 P0-02 武器实际攻击逻辑：新增引擎无关 `WeaponAttackEffect`；普通武器不再返回空攻击；接入七星刀流星雨、铁胎弓灼烧、铁弓击退、霸王弓弹射、诸葛连弩十连火箭；修正弓类接收 `GeneralUnit` 攻击上下文，并将投射物、Buff 和特殊影响接入命中结算。新增 `tests/unit/WeaponLogic.test.js` 覆盖核心分支。 | P0-02、阶段 3 |
| 2026-08-03 | `[记录修正]` | 将 P0-01 状态从“已完成”修正为“进行中”，与经验升级、武将技能入口、死亡回收和部件解绑等未完成工作项保持一致；总体统计不变。 | P0-01、阶段 1 |
| 2026-08-03 | `[完成子项]` | 在正式执行工程完成武将经验链路：`GeneralUnit` 支持经验累计、可注入且不猜测缺失后续数值的升级阈值、自动升级和等级属性刷新；`EnemyBase` 将现有普通/特殊敌人奖励（1/10）随击杀事件传递；`BattleManager` 监听击杀事件并通过 `UnitRegistry` 向所有伤害贡献武将分发经验。新增 `tests/unit/GeneralProgression.test.js`；修改文件通过 JavaScript 语法检查，针对性 10 项逻辑用例全部通过，未运行 Unity/TEngine 验证。 | P0-01、阶段 1 |
| 2026-08-03 | `[补充修正]` | 补齐 `GeneralUnit` 构造时带入已有经验的等级/属性即时刷新；复查后 219 个 `src` JavaScript 文件语法检查通过，针对性 10 项逻辑用例仍全部通过。 | P0-01、阶段 1 |
| 2026-08-03 | `[完成子项]` | 在正式执行工程接入武将技能触发入口：`GeneralUnit` 支持注入 `skillManager/skillKey`、技能可用性判断和触发；`GeneralFactory`、`UnitRegistry` 支持在合成时附着技能；`BattleManager.triggerGeneralSkill()` 提供统一触发入口；回收时委托 `SkillManager.removeOwner()` 清理。新增 `tests/unit/GeneralSkillEntry.test.js`；针对性 11 项逻辑用例全部通过，219 个 `src` JavaScript 文件语法检查通过，未运行 Unity/TEngine 验证。六个技能的具体效果未在本轮提前实现，保留给 P0-03。 | P0-01、阶段 1 |
| 2026-08-03 | `[完成]` | 完成 P0-01 与阶段 1 剩余生命周期：`GeneralUnit` 增加 `ACTIVE/DEAD/RECYCLED` 状态、幂等 `die/recycle`、武器和技能清理；`GeneralPart` 增加解绑接口；`UnitRegistry.removeUnit()` 支持武将移除，`removeGeneral()` 解除部件归属并防止重复合成。新增 `tests/unit/GeneralLifecycle.test.js`；针对性 15 项逻辑用例全部通过，219 个 `src` JavaScript 文件语法检查通过，未运行 Unity/TEngine 验证。 | P0-01、阶段 1 |
| 2026-08-03 | `[完成子项]` | 开始阶段 2/P0-04：新增 `AttackScheduler`、`AttackResolver`、`MeleeAttackEffect`、`ProjectileAttackEffect` 和 `AttackEffectManager`；`BattleManager` 接入统一调度/效果更新；枪兵和骑兵不再直接调用攻击效果 `update()`，改由统一管理器负责延迟命中、多目标去重和战斗结束清理。新增 `tests/unit/UnifiedAttackSystem.test.js`；针对性 19 项逻辑用例全部通过，225 个 `src` JavaScript 文件语法检查通过，未运行 Unity/TEngine 验证。原始动画事件精确结算和攻击对象池仍未完成。 | P0-04、阶段 2 |
| 2026-08-03 | `[完成子项]` | 在正式执行工程补齐攻击效果对象池和刀兵统一生命周期：`AttackEffectManager` 支持通过 `ObjectPool.takeByClass/recoverByClass` 获取/回收效果；枪兵、骑兵改为池化效果；刀兵接入统一管理器但仍使用原始 Laya 定时器精确触发 500ms 命中，避免固定步进造成时序漂移。新增对象池和定时器时序用例；针对性 20 项逻辑用例全部通过，226 个 `src` JavaScript 文件语法检查通过，未运行 Unity/TEngine 验证。 | P0-04、阶段 2 |
| 2026-08-03 | `[完成子项]` | 依据 `work/bundle.strings-decoded.js:24705-24831` 恢复枪兵/骑兵正式攻击时序：枪兵按播放倍率在 360ms 命中，骑兵两次横扫均延迟 150ms、各使用半攻击力，范围分别为半攻击范围和完整攻击范围；两类单位启动 `attack` 动画，开发表现桩支持 `pike/cavalry`。补充统一攻击系统时序与兵种接入用例，并将旧测试夹具改为注入 `AttackEffectManager`、验证枪兵正式注册。全量 `src` 226 个 JavaScript 文件通过语法检查；`round05` 25 项、`round06` 33 项、统一攻击系统 6 项全部通过，未运行 Unity/TEngine 验证。 | P0-04、阶段 2 |
| 2026-08-03 | `[完成子项]` | 将 `BowSoldier` 在原始 `STOPPED/650ms` 发射点创建的箭矢改由 `ProjectileAttackEffect` 统一登记和驱动；投射物完成后自动退出效果管理器，弓兵回收时取消未完成投射物；补充弓兵发射、战斗结束、投射物对象池和统一攻击效果用例。全量 `src` 226 个 JavaScript 文件通过语法检查；统一攻击相关 12 项、投射物相关 18 项、`round05` 25 项、`round06` 33 项全部通过，未运行 Unity/TEngine 验证。 | 阶段 2、弓兵攻击生命周期 |
| 2026-08-03 | `[完成子项]` | 新增 `WeaponAttackLifecycleEffect`，让 `GeneralUnit` 在配置 `AttackEffectManager` 时延迟普通武器命中到管理器更新阶段；武将回收会取消挂起效果；武将弓类由 `GeneralUnit` 将已创建投射物登记为 `ProjectileAttackEffect`，避免重复登记并保持原始投射物生命周期。新增 `GeneralUnifiedAttack.test.js`，武将专项 3 项、统一攻击相关合计 10 项、`round05` 25 项、`round06` 33 项、`test:projectile` 18 项全部通过；`src` 227 个 JavaScript 文件全部通过语法检查；`test:round03` 当前 15 项通过 12 项、失败 3 项，失败集中在既有 BattleScene 首帧/未注册敌人类型/DirectBattle 行为断言；未运行 Unity/TEngine 验证。 | P0-04、阶段 2、武将攻击生命周期 |
| 2026-08-03 | `[重定义验收/数值修正]` | 修正小兵成长数值还原错误：`UnitConfig` `MAX_SOLDIER_LEVEL` 5→3、`EXPERIENCE_THRESHOLDS` 从误用的武将 `Ip` 表 `[0,10,null,null,null]` 改为 bundle `Dp` 表 `[0,8,23]`（取证 `bundle:11278`/`bundle:40157`），同步 `DeckDefinitions`、`UnitLevelService`、`unity-export/config/units.json` 与生成器 `export-unity-config.js`（`maxLevel` 改用 `MAX_SOLDIER_LEVEL` 常量）；重定义 P1-03 验收，移除伪需求护甲／护盾／完整承受伤害（原始游戏友军无受击契约、不可被击杀，承受方为阿斗 BattleTarget，已忠实实现 `UnsupportedFriendlyUnitDamageError`），武将 die/recycle 生命周期注入拆分至后续 P0 任务。tests/ 无小兵 4-5 级断言，无需调整。全量 227 个 src 文件语法检查通过；`test:friendly-units` 16 项、武将/投射物/弓兵回归合计 14 项、`test:round03` 12/15（3 项失败为既有 BattleScene 首帧/未注册敌人类型/DirectBattle，与本轮无关）全部通过。 | P1-03、小兵成长数值 |
| 2026-08-03 | `[完成]` | 依据 OpenSpec 提案 `general-active-skills`（dependsOn `general-combat-wiring`）完成 P0-03/阶段 4 六个武将主动技能纯逻辑 effect：新增 `BattleShoutEffect`/`HolySwordEffect`/`ArrowRainEffect`/`FireArrowBarrageEffect`/`LeapSlashEffect`/`SevenInSevenOutEffect` + 共用 `effectTargets.js`；`SkillEffectPort` 注册六 handler 替换 DEFERRED、补 `projectileManager`/`attackEffectManager` 到 services、新增 `onOwnerAttack` hook；`EffectHandle` 扩展 `onOwnerAttack`；`GeneralUnit.attack` 增 guarded hook 通知跳斩溅射。效果对齐 bundle 取证（跳斩 5 次 50% 溅射+`guanYu_skill_roar`、战吼 2000ms STUN、圣剑范围伤害+KNOCKDOWN+`holyBlade_skill`、箭雨多支箭经 ProjectileAttackEffect/ProjectileManager、火箭烈 `n=floor(max(1,(level-1)/2))`+`k=range(1,3,true)*n`+`DEFERRED_PROJECTILE_VARIANT`、七进七出 7 次突进）。取证修正：火箭烈多重数下限钳制为 1（`bundle:45744` `Math.max(1,…)`，非 0），已同步 spec/proposal。新增 `tests/unit/GeneralActiveSkills.test.js` 13 项全部通过；全量 234 个 src 文件 `node --check` 通过；武将/统一攻击回归 15 项通过；`test:general-skills` 脚本已加入 package.json。未运行 Unity/TEngine 验证。 | P0-03、阶段 4、武将主动技能 |

| 2026-08-03 | `[完成]` | 依据 OpenSpec 提案 `special-weapons-projectiles`（dependsOn 无，与 `general-combat-wiring`/`general-active-skills` 无文件重叠）完成 P1-01/阶段 5 特殊投射物与剩余武器效果纯逻辑层。④a：24 把特殊非弓武器经新增 `WeaponSpecialEffects.js` 承载专属 effect（概率攻速类虎啸战刀/狼牙棒/铁蒺藜骨朵、首击类虎头湛金枪 3 枪阵/铁枪 1 枪阵/钩镰枪跌倒/古锭刀金币、计数类三尖刀/铁刀/10 把君子小人剑、击杀类梨花枪 8 朵梨花/青龙偃月刀刀气/点钢枪、等级类方天画戟/龙胆亮银枪/丈八蛇矛），晕眩/跌倒/攻速经现有 `STUN`/`KNOCKDOWN`/`ATTACK_SPEED` BuffType，金币经 `battleEconomy.award`；5 把属性武器 `WEAPON_DEFINITIONS` 增 `addAttackPower`/`attackRangeBonus` 字段使 `getCombatModifiers()` 返回非零。④b：16 新建弹种子类 + 5 壳补全专属逻辑 + `ShenBiPunch`→`ShenBiArrow` 误标校正，覆盖 bundle 23 弹种全集；7 新建移动策略 + 3 占位骨架校正接入 `ProjectileBase.resetData({movement})` 真实生命周期；武器技能投射物实体连接——七星刀流星雨→StarBullet（`WeaponAttackEffect._applyMeteorShower` 经 `projectileSpawner`）、诸葛连弩火箭雨→FireArrow（`ZhugeCrossbow` 已用，与 bundle:38995 一致）、火龙→FireDragonArrow（校正 `IronBow` 先前退化为 FireArrow+special 标签，对齐 bundle:42572 type:vs=FireDragonArrow，BREAKING：`WeaponLogic.test.js` 断言同步改为 FireDragonArrow）、陨石→新增 `MeteorStrikeEffect` 经 StaticFireBall/GroundSpikeBullet 孤子弹种承载（DEFERRED：bundle:27450 陨石原始为纯 Laya.Image 视觉特效不走弹种通道，此为纯逻辑层弹种化重建）。数值标注：青龙偃月刀倍率/君子小人剑伤害/铁蒺藜骨朵眩晕时长/陨石数量 PARTIAL 可注入常量，弹种未取证逻辑 DEFERRED。新增 `tests/unit/WeaponSkillProjectiles.test.js` 10 项；`SpecialWeaponEffects.test.js`/`AttributeWeaponBonuses.test.js`/`WeaponLogic.test.js`/`ProjectileTypes.test.js`/`ProjectileMovement.test.js` 合计 52 项通过；弓兵/投射物/武将主动技能行为回归 33 项通过；全量 259 个 src 文件 `node --check` 通过；`test:special-weapons`/`test:projectile-types` 脚本已加入 package.json。未运行 Unity/TEngine 验证。 | P1-01、阶段 5、特殊武器与投射物实体 |

| 2026-08-03 | `[完成]` | 依据 OpenSpec 提案 `non-mob0-enemy-behaviors`（dependsOn 无）完成 P1-02/阶段 5 非 Mob0 敌人专属行为。Zombie：恢复沼澤浮現三阶段状态机（`gB` 淡入200ms/上升80px/s/目标y=140→80）+气泡粒子（5%/上限3/40px/s/0.7每秒alpha）+`uB`沼泽贴图/`tB`三段Tween呼吸/`dB`清理/`Hw`/`fw`/`mw` 共 7 方法。Cavalry：黄圈光环（`yellowCircle.png` 80×30 zIndex=-1）+骑兵呼吸（0.78→0.82→0.8 130ms）+速度80。Puppet：爱心粒子（300ms周期/0.1~0.5缩放/放大1/3000/淡出1/1000）+速度10+`yt`路径事件订阅+待机0.9。NormalEnemyBase：灵魂投射 `sB`（typeIndex!=1+塔Ci+num<3+距离<range，飞行300ms发 `ENEMY_SOUL_DELIVERED`，typeIndex=1不触发）+吹飞 `Xw`（贝塞尔QE/`hit(Zi-0.1)`濒死/旋转/注册`Gw`）/`Gw`（`time+=deltaMs/200`二次贝塞尔插值/`time>=1`→`hit(1)`致死+`ZE=0`）+gameOver吹飞清理（`nx.wa("blownUp")`等价/Tween.killAll/tw复位）；`EnemyBase.configure` 增 `soulTowerResolver`/`soulFlightManager` 可注入接口（DEFERRED 桩默认不触发）；校正 `blowUpCurve` 键名 `{ug,p1,p2,time}` 对齐 bundle；新增 `GameEvents.ENEMY_SOUL_DELIVERED`。`LayaEnemyPresentation` 增 Zombie/Cavalry/Puppet 表现 port 方法。enemies.json 填入 7 类敌人基础数值（`healthByWave` 20 波数组 `bundle:12038` + typeIndex/speed/healthModifier：Zombie÷2 `bundle:31386`、Cavalry 80 `bundle:32398`、Puppet 10 `bundle:31793` +×Sh `bundle:12149`、typeIndex 回退 `bundle:11619`），每数值标注 bundle 行号来源。新增 7 个敌人行为测试文件合计 60 项用例全部通过；Mob0 既有敌人测试回归 15 项通过；全量 src JavaScript 文件 `node --check` 通过；`test:enemy-behaviors` 脚本已加入 package.json。未运行 Unity/TEngine 验证。 | P1-02、阶段 5、非 Mob0 敌人行为 |

| 2026-08-03 | `[完成子项]` | 依据 OpenSpec 提案 `ai-advanced-strategy`（dependsOn 建议 `general-combat-wiring`，已完成）完成 P1-04/阶段 5 完整 AI 策略纯逻辑实施。`AIController.js` 移除 62 行极简占位（`deployUntilReady`/`tryDeploy`/`choosePlacement` 单一最远格策略），恢复 5 步状态机 `TG`（step1 `Ji>=gi`→`refresh`+step2 / `ni[Si]` 概率→`UG`/`YO`；step2 `Xi=true`+`bG.YX`+`XX>=5`→step3；step3 `KX[0]<PA.sb.length`→`bG.ZX`/否则 step4；step4 `rp.filter`+`MG.tG/iG/hG/aG`→step5；step5 `cG[0]<nG.length`→`MG.lG`/回 step1）+ `update` 按 `fG` 驱动；4 级动态难度 `Si`（0-3 钳制，`Tu(±1)` 升降级经 rank 表，`fG=[2000,1500,1000,500]ms`，`ni`/`ri`/`ii[Si][i]` 收入随 Si，`ii[3]=[20,20,20,20,20,20]`/`ei=[3,5,8,11,14,17]` hu 解码确认）；主动刷牌 `refresh`（type:2）+周期收入 `PG`（订阅 `WAVE_STARTED`，`ai加钱` 日志）+道具使用 `YO`（`hu[101]`=5000ms 冷却）/`Yb`（按 type 分派，`✅`/`❌` 日志）；难度分层放置 `WX`（`Si<2` 随机洗牌 `np.Ys`/`Si>=2` `DX`+`TX` 评分排序，Si=3 `OG` 寻路 DEFERRED 退化，Si=2 前5洗牌 unshift）；子控制器 `AIDeploymentController`(bG) `YX`/`ZX`/`HX`/`$X`/`NX`/`qX`（`Si<2` 乘 `[.2,.3][Si]` 弱化）+`AIPlanningController`(MG) `tG`/`iG`/`hG`/`aG`/`lG`；阵营模板 `AITemplateResolver`(AG) `qj.kX`(Si>=2)/`qj.yX`(Si<2) 模板缓存，武将项 `DEFERRED_GENERAL_TEMPLATE` 空占位；`AIDifficultyConfig.js`+`ai-difficulty.json` 配置加载（每数值标注 bundle 行号）；难度 0 等价路径验证（`fG=2000ms`/`ni=0.001`/`WX` 随机/`qX`×0.2/`ii[0]` 全 0/道具 DEFERRED 桩 no-op）确认弱策略。DEFERRED 回归：`itemEffectDispatcher`/武将模板/`rankTableResolver`/`qj.bX` 寻路均未自行补成原版。本 change tasks.md 共 44 个任务，1.1-8.1 全部 PASS（40 项）；剩余 8.2（单局 smoke 回归）/8.3（全量 src `node --check`）/8.4（`package.json` `test:ai-strategy` 脚本）/8.5（本文档）正在本批次处理。P1-04 状态由"未开始"更新为"进行中"，待 8.2 smoke 回归确认后再转"已完成"。未运行 Unity/TEngine 验证。 | P1-04、阶段 5、完整 AI 策略 |

| 2026-08-03 | `[完成]` | 任务 8.5 收尾：8.2 单局 smoke 回归已由独立校验 subagent 确认 PASS（本 change tasks.md 44/44 任务全部通过，含 `FullMicroBattle` 跑通 GameOver、全量 src `node --check`、`test:ai-strategy` 脚本）。据此将 P1-04 状态由"进行中"转"已完成"，最后变动由 `[完成子项]` 更新为 `[完成]`；总体进度 P1 行已完成 1→2、进行中 1→0、进度 17%→33%；阶段 5 最后变动与完成记录同步标注 8.2 已确认；备注 P1-04 段落由"进行中…待 8.2 确认"改为"已完成"。阶段 5 整体仍因 Boss 技能/卡牌等 P1 任务未开始而保持进行中。未运行 Unity/TEngine 验证。 | P1-04、阶段 5、完整 AI 策略 |

| 2026-08-04 | `[完成]` | 依据 OpenSpec 提案 `attack-timing-finalization`（dependsOn 无，与既有已完成提案无文件重叠）闭合 P0-04/阶段 2 原始动画事件精确结算接缝。枪兵：`PikeAttackEffect.launch()` 增可选 `animationEventTimingProvider` + 实现 `calibrateHitTiming`，`MeleeAttackEffect` 增 `calibrateHitTiming` 钩子（默认 no-op），无 provider 回退 `360/rate` 常量基线，`PIKE_HIT_DELAY_MS`/`PIKE_EFFECT_DURATION_MS` 注释标注为原始 Tween 链第三段 onStart 等价常量（`bundle:24733-24741`），规则层命中仍由 `AttackEffectManager.update()`→`MeleeAttackEffect.hit()` 驱动不倒退。弓兵：`BowSoldier.js`/`DevelopmentAnimationDriver.js` 注释固化 STOPPED 正式动画事件契约（正式动画运行时驱动，dev driver 按时长模拟为无 Spine 回退，两者经同一 `_onAttackAnimationStopped` 入口），流程不变。骑兵 sweep/枪尖 Qx：`LayaEnemyPresentation` 增 `createCavalrySweepVisual`/`removeCavalrySweepVisual`/`createPikeTipVisual`/`animatePikeTipThrust`/`hidePikeTipVisual` 5 个表现 port 方法（DEFERRED 桩 no-op，实体 VFX 归 P2），`CavalrySweepEffect`/`PikeAttackEffect` 增 port 调度，伤害/命中结算不依赖视觉对象。刀兵：`KnifeAttackEffect.js` 注释固化原始 Laya timer 方案（`usesTimer` 时 `update()` 只 `return active` 不推进计时，命中由 `Laya.timer.once` 精确触发），逻辑不变。新增 5 个时序用例测试文件 33 项全通过；既有统一攻击回归 13 项通过；全量 263 个 src 文件 `node --check` 通过；`package.json` `test:attack-timing` 脚本 33 项通过。据此将 P0-04/阶段 2 状态由「进行中」转「已完成」，总体进度 P0 行已完成 3→4、进行中 1→0、进度 75%→100%，执行阶段行已完成 2→3、进度 33%→50%。未运行 Unity/TEngine 验证。 | P0-04、阶段 2、统一攻击时序接缝闭合 |

| 2026-08-04 | `[完成]` | 依据 OpenSpec 提案 `gap-sweep-and-presentation` 收尾零散缺口与 P2 契约（dependsOn 建议 ①-⑦，已完成；与 ②③④⑥⑦/P1-02 无文件重叠）。Boss 技能 12/12：Inspire(鼓舞)/CavalryOrder(铁骑号令) 从 `SkillEffectPort.js:23-24` inline lambda 迁出为独立效果类文件 `src/skills/effects/InspireEffect.js`/`CavalryOrderEffect.js`（经 effects barrel 导出，`SkillEffectPort._installCoreHandlers` 以 `new` 实例化注册，与其余 10 个模式一致），12 个 Boss 技能全部有独立效果类文件；`check:round07m` PASS（12 requiredHandlers 含 Inspire/CavalryOrder + bossCount=12），InspireEffect/CavalryOrderEffect 单测 13/13 通过。Deck 牌池补齐 108 元素（`bundle:11969` 铲×11/武将字 ~17 + 刀×21/弓×19/枪×19/骑×18）+ 5 缺失分支（`bO` 抽牌武将字 no-repeat `bundle:46503-46528`、`xO` 铲注入 `bundle:46529-46545`、`dP` 武将字复制 `bundle:46546-46574`、`qY` AI 重排 `bundle:49563-49595`、`NY` 两阶段清除 `bundle:49597-49614`），`deck-pool.json` 合法（totalElements=108）。round03 过时断言修正：`BattleFirstFrame.test.js:25` `enemyManager.prepareWaveCount===1`→`waveManager.planHistory.length===1`、`:44` `enemyFactory.create('Mob1')`→`create('Mob99')`，`test:round03` 15/15 通过、`verify:round03` PASS（15 tests × 2 runs fail=0），文档此前"3 项失败"描述已过时并据此修正。20 类 Buff handler 覆盖核对表产出（0-19 全覆盖，`limit`/`charm` bundle 空壳、`fall` `vi.Cv` 撞击副作用标 `DEFERRED_FALL_IMPACT`，确认非缺口）。P2 表现层缺口清单 `docs/p2-presentation-gap-catalog.md` + 接入契约 `docs/p2-presentation-integration-contract.md` 产出，覆盖 P2-01..04（Non-Goal：不实现 VFX/音频/场景实体）。回归：全量 src 265 个 `.js` `node --check` 0 失败；FullMicroBattle + MicroBattleCli smoke 2/2 通过；`dev:skill-gameover` smoke 前 6 boss 技能断言全过。已知限制（预先存在、非本变更范围，如实记录不标记为已恢复）：`verify:round04`/`verify:round05` 仅静态"必备文档文件"检查 FAIL（缺 `analysis/modules/ENEMY-RUNTIME-01.json`/`FRIENDLY-UNIT-COMBAT-01.json` 等 4 文档产物，git 证据确认从未存在、tasks.md 无产出任务），测试维度 fail=0（round04 24/24、round05 25/25、回归全过）；`dev:skill-gameover` smoke 末尾 `title==='胜利'` 断言失败（GameOverSceneController 无 title 节点，属 P2 表现层 UI 缺口、非 Boss 技能回归）；`DEFERRED_LI_DRAW_LIST`/`DEFERRED_GENERAL_MERGE` 在 DeckManager.js 仅注释行、`DEFERRED_FALL_IMPACT` 仅 docs 标注，三标记均未自行补成原版。据此将阶段 5 工作项「Boss 技能边界」由「未开始」转「已完成」（阶段 5 仍因 P1-05/P1-06 卡牌任务保持进行中），更新 round03 状态与 P2 准备记录。未运行 Unity/TEngine 验证。 | Boss 技能 12/12、Deck 牌池 108 元素、round03 修正、P2 缺口清单+接入契约 |

## 当前阻塞与备注

- 阻塞：暂无。
- 备注：P0-01、P0-02 和阶段 1 的纯逻辑工作已完成；P0-03/阶段 4 武将主动技能已完成；P0-04 与阶段 2 已完成（弓兵投射物及武将普通武器攻击已纳入统一攻击生命周期，原始动画事件精确结算接缝已闭合）；阶段 3 恢复武器逻辑仍进行中；2026-08-04 依据 OpenSpec 提案 `attack-timing-finalization` 闭合原始动画事件精确结算接缝（枪兵动画事件校准钩子 + 弓兵 STOPPED 契约 + 骑兵 sweep/枪尖 Qx port + 刀兵时序文档 + 时序用例 33 项 + 既有回归 13 项 + 全量 263 个 src `node --check` + `test:attack-timing` 33 项全通过），P0-04/阶段 2 状态转「已完成」，P0 核心战斗闭环 4/4 全部完成；P1-01/阶段 5 特殊投射物与剩余武器效果纯逻辑层已完成（VFX/渲染保留至 P2）；P1-02 非 Mob0 敌人专属行为已完成（Zombie/Cavalry/Puppet + NormalEnemyBase 灵魂投射 sB/吹飞 Xw/Gw + enemies.json 7 类数值，敌人行为用例 60 项/Mob0 回归 15 项通过）；P1-04 完整 AI 策略已完成（5 步状态机 TG/4 级难度 Si/主动刷牌+周期收入+道具使用契约/难度分层放置 WX/子控制器 bG+MG+阵营模板 AG/用例与难度 0 等价/DEFERRED 回归，本 change tasks.md 44/44 任务全部通过，8.2 单局 smoke 回归 `FullMicroBattle` 跑通 GameOver 已由独立校验 subagent 确认 PASS），状态为"已完成"；其余 P1 任务（P1-05/P1-06）仍为"未开始"。2026-08-04 依据 OpenSpec 提案 `gap-sweep-and-presentation` 收尾零散缺口：`test:round03` 原 2 项过时断言已修正（`BattleFirstFrame.test.js:25` 改断言 `waveManager.planHistory.length===1`、`:44` 改用真实未注册类型 `Mob99`），`test:round03` 15/15 通过、`verify:round03` PASS（15 tests × 2 runs fail=0），文档此前"3 项失败"描述已过时并据此修正；Boss 技能 12/12 全部有独立效果类文件——Inspire(鼓舞)/CavalryOrder(铁骑号令) 已从 `SkillEffectPort` inline lambda 迁出为 `src/skills/effects/InspireEffect.js`/`CavalryOrderEffect.js`（经 effects barrel 导出，`SkillEffectPort` 以 new 实例化注册），`check:round07m` 确认 12 requiredHandlers（含 Inspire/CavalryOrder）+ bossCount=12、单测 13/13 通过；Deck 牌池补齐 108 元素（铲/武将字）+5 缺失分支（bO/xO/dP/qY/NY）；20 类 Buff handler 覆盖核对表产出（确认非缺口）；P2 表现层缺口清单（`docs/p2-presentation-gap-catalog.md`）与接入契约（`docs/p2-presentation-integration-contract.md`）已产出，覆盖 P2-01..04，不实现 VFX/音频/场景实体（Non-Goal）。已知限制（预先存在、非本变更范围，如实记录不标记为已恢复）：`verify:round04`/`verify:round05` 仅静态"必备文档文件"检查 FAIL（缺 `analysis/modules/ENEMY-RUNTIME-01.json`/`FRIENDLY-UNIT-COMBAT-01.json` 等 4 文档产物，git 证据确认从未存在、tasks.md 无产出这些 analysis 文档的任务），测试维度 fail=0（round04 24/24、round05 25/25、回归全过）；`dev:skill-gameover` smoke 末尾 `title==='胜利'` 断言失败（GameOverSceneController 无 title 节点，属 P2 表现层 UI 缺口、非 Boss 技能回归）；`DEFERRED_LI_DRAW_LIST`/`DEFERRED_GENERAL_MERGE` 在 DeckManager.js 仅注释行未实现原版、`DEFERRED_FALL_IMPACT` 仅 docs 标注（FallBuffHandler 未调 vi.Cv），三标记均未自行补成原版。后续实现、范围调整、验收结果和验证限制变化必须追加到"变更记录"。
