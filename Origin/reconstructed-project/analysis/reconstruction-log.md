# 重建日志

## Round 01 — Source Map 检查与字符串解码

### 读取范围

- 完整文件：`bundle.js`
- 启动配置：`index.js`
- 引擎参考：LayaAir Core/UI/WebGL2D/微信适配/Spine/Trail2D
- 重点分析：`bundle.js:1–1014`

### 生成文件

- `original/bundle.js`
- `work/bundle.formatted.js`
- `work/obfuscation-runtime.original.js`
- `work/obfuscation-runtime.safe-eval.js`
- `work/bundle.strings-decoded.js`
- `tools/decode-strings.js`
- `analysis/source-map-report.md`
- `analysis/recovered-source-list.json`
- `analysis/baseline-manifest.json`
- `analysis/transformation-chain.json`
- `analysis/deliverable-manifest.json`
- `analysis/obfuscation-runtime.md`
- `analysis/obfuscation-runtime-values.json`
- `analysis/opaque-predicate-values.json`
- `analysis/string-decoding-report.md`
- `analysis/string-decoding-report.json`
- `analysis/string-decoding-map.jsonl`
- `analysis/string-decoding-unresolved.jsonl`
- `tests/behavior/decode-strings.test.js`
- `analysis/test-results-round-01.json`

### 已确认行为

- 49 组字符串表共 5,987 项。
- 数值索引表含 334 项。
- 只执行纯初始化范围 `1–1000`。
- 反篡改区间 `1001–1014` 未执行且保持原样。
- 主体 IIFE 未执行。
- 81,687 处字符串表达式已静态替换。
- 3,006 处没有充分证明，保持原样。
- 输出语法有效，行数不变。

### 推断内容

无业务命名推断。本轮只恢复可以直接计算出的字符串，不重命名类、方法或字段。

### 未解决引用

见 `analysis/string-decoding-unresolved.jsonl` 和 `analysis/unresolved-items.md`。

### 测试结果

`PASS`：

- 原件哈希未变化
- identity formatted 阶段字节一致
- 前 1,014 行字节一致
- 输出语法有效
- 替换映射数量一致
- 未解析记录数量一致
- 字符串表无后续变异
- 重复执行结果确定一致

### 行为变化风险

`LOW`：当前转换只把静态纯字符串表达式替换为同值字面量。尚未做运行时原工程与重建工程的完整行为对照，因此不能标记为零风险。

## Round 02 — NET-01 网络模块

### 读取范围

- 请求范围：`work/bundle.strings-decoded.js:5087–6037`
- 网络类本体：`5087–5392`
- 运行时别名：`5395` 的 `qK = tn`
- 静态字段：`3316`
- 单例基类：`3763–3768`
- 引擎参考：`original/libs/laya.core.js:2326–2382`
- 晚绑定依赖接口参考：
  - `uq = tw`：`13293`
  - `tw`：`11561–11908`
  - `rj.parseCloudSaveRaw`：`8444–8493`
  - `np.Gs`：`2845–2856`
  - `oc/sS`：`2078` 起

### 模块边界结论

`5395–6037` 中除 `qK = tn` 外均不属于 NET-01：

- `ry`：段位换算
- `sI`：平台默认基类
- `q3`：场景管理器
- `s1/r7`：用户类型配置与状态
- `qN` 微信平台实现从 `6038` 开始，本轮未进入

### 生成文件

- `src/core/SingletonBase.js`
- `src/network/HttpClient.js`
- `src/network/index.js`
- `tests/mocks/LayaHttpMock.js`
- `tests/mocks/NetworkMock.js`
- `tests/unit/HttpClient.test.js`
- `tests/behavior/NET-01.behavior.test.js`
- `tools/check-net01.js`
- `analysis/modules/NET-01.json`
- `analysis/behavior/NET-01.md`
- `analysis/mappings/NET-01-symbol-map.json`
- `analysis/architecture/module-conventions.md`
- `analysis/method-coverage-NET-01.json`
- `analysis/static-check-NET-01.json`
- `analysis/behavior-diff.md`
- `analysis/dependency-map.md`

### 已确认行为

- 22 个原构造函数、方法和访问器全部映射，无未解决函数。
- 默认超时 5000ms。
- 正式和调试基础 URL 均已恢复。
- 固定 JSON 响应类型和 authentication 请求头已恢复。
- 保留先 send 后 once 监听的原顺序。
- 登录、云存档同步/节流、服务器时间、排行、开局/结束上报、用户信息、埋点和错误上传均已迁移。
- 没有新增服务端业务 code 判定、重试或取消。

### 推断内容

- `Pa` 命名为 `rankingType`，可信度 `MEDIUM`。
- `Wa` 命名为 `requestBestRankIfDue`，可信度 `MEDIUM`。
- `qa/$a` 仅使用中性遗留常量名，可信度 `LOW`。

### 晚绑定处理

没有创建空 `uq` 或复制 `tw`。通过调用时语义绑定承载玩家数据、云存档编解码、事件和日期工具；测试中可注入 mock，生产绑定待对应模块恢复。

### 测试结果

- Node 测试：32 通过，0 失败。
- JavaScript 语法检查：通过。
- CommonJS 导出检查：通过。
- 原方法覆盖：22/22。
- URL/API 路径检查：通过。
- 重复方法检查：通过。
- 已重建源码循环依赖检查：无循环。
- 静态检查重复执行输出哈希一致。
- 原始 `bundle.js` 和 `bundle.strings-decoded.js` 哈希未变化。

### 行为变化风险

`MEDIUM`：网络逻辑本体已完整还原；风险主要来自尚未正式接入的晚绑定数据模块，以及未在真实微信网络环境验证平台错误和超时对象。

## Round 03 — BOOT-TO-BATTLE 核心纵向切片

### 读取与定位范围

- `original/index.js:1-68`
- `bundle.strings-decoded.js:2078-2115` — EventBus
- `3163-3297` — BattleState
- `3763-3874` — 单例和 GameLoop
- `5715-5869`、`13165` — SceneManager 与别名
- `8525-9429`、`11436-12847` — 玩家最小数据、GameData、敌人和地图配置
- `18534-18608` — AnimationEntityPool
- `19220-19260` — EnemyFactory
- `29460-30477` — UnitRegistry 直接依赖集合
- `31062-31114` — Mob0
- `32939-33696` — EnemyManager
- `50323-50534` — BattleManager
- `50996-51270` — LoadScene
- `55027-55229` — BattleFlow
- `57007-59129` — BattleScene
- `60834-61284` — MatchScene
- `64782-65947` — MainScene

### 边界修正

- 先前估计 `SceneManager` 在 13165 行附近；实际类体位于 5715–5869，13165 只是 `sF = q3` 别名绑定。
- BattleScene 初始化不会自动创建友军或武将。`Gq` 明确创建的是两个池化 `aDou` 目标。
- 普通敌人不在 BattleScene.onAwake 创建；由 BattleManager 在准备期结束后按 1500ms 间隔成对生成。

### 生成源码

- 启动：GameBootstrap、DevelopmentBootstrap
- 核心：GameLoop、SceneManager、AnimationEntityPool、EventBus、对象池/保留表
- 数据：PlayerDataCore、GameDataCore、MapDataCore、EnemyDataCore、BattleState
- 场景：Load、Main、Match、Battle 控制器
- 战斗：BattleFlowCoordinator、BattleManager、EnemyManager、EnemyFactory、UnitRegistry
- 实体：BattleTarget 开发可观测实现、Mob0Enemy 关键路径实现
- 平台/网络：PlatformAdapter、DevelopmentPlatform、DevelopmentNetworkData

### 行为验证

- 正常启动进入 MainScene。
- 平台预加载失败继续。
- 登录失败使用本地数据继续。
- 特殊启动进入 MatchScene。
- MainScene 扣除 5 点体力并保持 5000ms 防抖。
- MatchScene 保留 50ms 完成、-1000ms 偏移、1500ms 转场。
- directBattle 仍执行真实 BattleFlow/BattleManager/BattleScene。
- BattleScene 创建两个 `sk_aDou` 对象，fast mode=false。
- 第一帧开始第 1 波；1500ms 后生成双方两个 Mob0。
- 清理时注销 BattleMgr/BattleScene，保留全局 enemyMgr。
- 所有未恢复敌人类型明确报错。

### 风险

总体 `MEDIUM`：启动和管理器控制流已恢复并可执行；真实画面、aDou 目标组件、敌人基类和玩家单位系统仍缺失，不能宣称完整战斗可玩。

### Round 03 最终验证与下一步（2026-07-24）

- `npm run test:boot`：7 通过，0 失败。
- `npm run test:battle-entry`：5 通过，0 失败。
- `npm run verify:round03`：15 个测试重复执行两次，30 次通过，0 失败；52 项静态检查通过。
- `npm run verify:round02`：NET-01 32 通过，0 失败。
- 未产生真实网络请求，未调用 `wx.*` 或 `tt.*`。
- 原始 bundle、decoded bundle 和 index 哈希保持不变。
- 下一轮优先闭包：`12194-12482`（A*/格网）、`19600-20858`（敌人事件/核心状态机）、`31262-31482`（普通敌人扩展基类）、`56574-57000`（路径/目标组件）。

## Round 04 — ENEMY-RUNTIME-01 敌人核心运行时

### 实际读取范围

请求范围：

- `19685–20858` — `ro`
- `31062–31114` — `st`
- `31262–31482` — `pe`
- `32939–33696` — `vi`
- `12483–12847` — `s4`

为闭合直接依赖，实际扩展读取：

- `11561–12175` — GameData/EnemyData 数值接口
- `12194–12482` — `tl/ru/oS` A* 和网格
- `13380–14360` — 通用字符串池/对象池
- `19184–19260` — `s0/ss` 敌人工厂与注册表
- `19600–19684` — `qE` 事件代理父类
- `57007–59129` — BattleScene/aDou 目标契约

所有扩展原因已记录于：

- `analysis/critical-path/enemy-runtime-classgraph.json`
- `analysis/modules/ENEMY-RUNTIME-01.json`
- `analysis/critical-path/enemy-runtime-dependency-closure.json`

### 类关系修正

确认真实继承链：

```text
st (Mob0)
  → pe (普通敌人表现/死亡层)
    → ro (敌人核心状态机)
      → qE (事件代理层)
```

`pe` 不是独立 MovementController；路径和移动均在 `ro` 中。逻辑实体和 Laya 表现节点不是同一实例：逻辑实体进入按类池，Mob0 表现节点进入字符串键 `mob` 池。

### 已迁移行为

- 0–4 状态值和切换边界。
- 双方四方向 A* 路线。
- 50 px/s，`deltaMs / 1000` 位移单位。
- 80ms 固定子步、500ms 单帧累计上限。
- 路径点切换、临近终点事件和离场。
- 500ms 接触冷却、50ms 延迟、固定 1 点 aDou 伤害。
- 受击扣血、生命条比例、伤害来源 ID 去重。
- 一次性死亡、普通/特殊奖励入口、100ms 普通死亡完成边界。
- EnemyManager 80px 空间索引、阵营/死亡过滤、圆矩形相交查询。
- EnemyManager 注册、移动更新、注销和清理。
- 类池与 `mob` 表现池双层回收。
- generation 隔离旧 timer 回调，防止污染复用实例。

### 兼容性修正

- `BattleTarget` 新增 `receiveEnemyContact(amount, sourceEnemy)`，只封装原 BattleState 字段写入和胜负事件，没有改变数值。
- `Mob0Enemy.gameOver` 保留已回收表现节点引用，匹配原 `ro.move()` 在 `gameOver()` 后同一调用帧继续网格检查的顺序。
- 第三轮测试中的“敌人移动未恢复”标记更新为 `RESTORED_ENEMY_RUNTIME_RO_PE`。
- `tools/check-round03.js` 的 Boss 波次断言由旧分析值 18 修正为源码确认值 20；仅修改静态检查，不改变生产逻辑。

### 新增源码

- `src/core/ObjectPool.js`
- `src/battle/MapData.js`
- `src/battle/EnemyFactory.js`
- `src/battle/EnemyManager.js`
- `src/battle/dev/DevelopmentCombatServices.js`
- `src/entities/EnemyEventProxy.js`
- `src/entities/EnemyBase.js`
- `src/entities/NormalEnemyBase.js`
- `src/entities/Mob0Enemy.js`
- `src/entities/BattleTarget.js`

### 新增测试和工具

- 8 个敌人运行时单元测试文件，共 16 项测试。
- 7 个 Mob0 行为测试文件，共 8 项测试。
- `tests/mocks/createEnemyRuntimeHarness.js`
- `tools/run-mob0-simulation.js`
- `tools/check-round04.js`
- `tools/verify-round04.js`

### 验证结果

- Round 04：24 通过，0 失败；确定性复跑两次。
- 静态检查：78 通过，0 失败；96 个 JavaScript 文件。
- Round 03 回归：15 通过，0 失败。
- NET-01 回归：32 通过，0 失败。
- 双方 Mob0 模拟：32 次路径点切换，各对对应 aDou 造成 1 点伤害，两个逻辑实体和两个表现节点均回收。
- 真实网络请求：0。
- `wx.*` / `tt.*` 原生调用：0。
- 原始 bundle、decoded bundle、index 和 HttpClient 哈希均保持不变。

### 暂缓范围

- Boss 和其他普通敌人类型。
- Buff 广播、特殊技能、击飞/击退完整分支。
- 正式 Spine/Tween/音频/特效/足迹/掉落表现。
- 10 波后的正式段位生命加成来源。
- 玩家基础士兵的真实攻击链。

### 下一轮建议

`FRIENDLY-UNIT-COMBAT-01`：

- `rb`：`24863–24930`
- `rc`：`22694–23112`
- `td`：`23114–23437`
- 基础兵种创建器：`24443–24834`
- 弓兵 `ok`：`26093–约26506`
- `vc` UnitRegistry：`29460–30476`
- BattleManager 攻击轮询：`50471–50519`

目标是让真实基础士兵通过已恢复的 `EnemyManager.qx` 选中 Mob0、按原攻击节奏结算伤害并进入本轮已验证的敌人死亡/回收链。

## Round 05 — FRIENDLY-UNIT-COMBAT-01（2026-07-24）

### 边界与兵种选择

- 核对 `qE → rb → ri → rc → td` 后确认 `rb` 是通用拖拽/事件父类，不是组合组件。
- `ok` 确认为弓兵，但立即依赖 `SimpleDynamicArrow`、BulletFactory、Tween 和资源表现；未用即时伤害替代。
- 选择基础构造表索引 0 的正式刀兵 `tb.zx[0]`，文字键 `刀`。
- 友军继承链未定义 HP、受击或死亡，未按游戏常识补全。

### 生成源码

- `src/core/GameObjectEventProxy.js`
- `src/units/UnitConfig.js`
- `src/units/UnitDragBase.js`
- `src/units/UnitBase.js`
- `src/units/SoldierBase.js`
- `src/units/KnifeSoldier.js`
- `src/units/UnitFactory.js`
- `src/units/UnitRegistry.js`
- `src/units/index.js`
- `src/combat/KnifeAttackTimeline.js`
- `src/battle/dev/DevelopmentUnitSpawner.js`
- `src/battle/dev/DevelopmentUnitServices.js`

### 兼容性修改

- `src/entities/EnemyEventProxy.js` 改为复用通用 `GameObjectEventProxy`，敌人对外行为不变。
- `src/battle/UnitRegistry.js` 变为 `src/units/UnitRegistry.js` 的兼容导出，避免修改第三轮导入点。
- `EnemyManager` 增加按 ID 读取接口，用于延迟命中时重新验证目标。
- `BattleManager` 恢复基础士兵攻击轮询，保持原 Map 遍历和两阶段攻击状态。
- DevelopmentBootstrap 增加正式刀兵工厂、时间线和显式开发放置入口；未修改平台或 HttpClient。

### 行为验证

- 刀兵通过正式工厂索引 0 和文字键 `刀` 创建。
- 一级正式值：3 伤害、120px 范围、0.8s 间隔。
- 固定 80ms 步长下，800ms 进入攻击状态，880ms 启动第一击，500ms 后命中。
- 两击击杀 6 HP Mob0；随后重新索敌并两击击杀第二个 Mob0。
- Mob0 继续使用第四轮死亡、空间注销和双池回收。
- 移除友军时取消旧延迟攻击；复用后旧回调不污染新生命周期。
- 多个友军可共享同一目标，PlacementReservationRegistry 不参与攻击占位。
- 真实网络和 `wx.*`/`tt.*` 调用均为 0。

### 暂缓

- 弓/枪/骑及投射物、贯穿和范围攻击闭包。
- 正式触摸拖拽、征兵、合成、武将和卡牌 UI。
- 正式攻击动画、Spine、prefab、特效和音频。
- 友军受击/死亡规则；当前所选源码未定义该能力。

## Round 05 累计快照加固（2026-07-24）

### 目的

- 保证交付包是第一至第五轮的完整累计工程，而不是本轮增量补丁。
- 保证全新解压目录执行 `npm install` 后无需访问 npm registry 即可运行全部验证。
- 建立单一入口 `npm run verify:all`，覆盖所有历史测试、静态检查和开发仿真。

### 增量修改

- 新增 `tools/verify-all.js`，依次验证 Round 01 至 Round 05，并执行两个开发仿真。
- 新增 `tools/build-cumulative-manifest.js`，输出完整文件清单、逐文件 SHA-256 和累计树哈希。
- 将 TypeScript 5.8.3 作为本地依赖包收录于 `vendor/typescript-5.8.3.tgz`。
- 新增 `.npmrc` 与 `package-lock.json`，使全新目录中的 `npm install` 可离线完成。
- 更新 `README.md`、`package.json`、累计交付清单和验证报告。

### 历史成果检查

- 第四轮累计压缩包中的 210 个文件在当前目录全部存在，缺失 0。
- 第一至第五轮关键源码、测试、分析、映射和重建日志均由 `verify:all` 再次检查。
- 不可变的原始 bundle、decoded bundle、index 和 NET-01 HttpClient 哈希继续锁定。

### 全新解压验证结果

- 在全新临时目录复制累计工程，安装前确认不存在 `node_modules/`。
- 执行 `npm install`：PASS，本地安装 TypeScript 5.8.3。
- 执行 `npm run verify:all`：PASS。
- Round 01–05 验证全部通过。
- 当前 4 个 `dev:*` 命令全部通过。
- 累计审计通过，真实网络请求 0，`wx.*` / `tt.*` 原生调用 0。


## 2026-07-24 — Round 05 计划合规加固（v0.5.2）

- 在现有第一至第五轮累计工程上原地修改，没有创建脱离历史成果的示例项目。
- 新增精确命令：`test:friendly-unit`、`dev:friendly-unit`、`dev:micro-battle`。
- 扩充 `tools/run-friendly-unit-simulation.js`，输出正式符号、配置、初始位置、攻击时刻、死亡/注销时刻、重新索敌结果、管理器数量、空间索引和双层对象池状态。
- 新增 `tests/behavior/MicroBattleCli.test.js` 和 `analysis/behavior/unit-registry.md`。
- Round 05 测试由 24 增至 25，静态检查由 59/74 前序版本更新为 74/74。
- 再次确认基础友军源码没有 HP/受击/死亡契约；保留显式不支持错误，未编造规则。
- 全量 `verify:all` 通过，累计审计确认第一至第五轮文件仍完整存在。


## 2026-07-24 — Round 05 v0.5.2 候选包全新解压验证

- 从累计工程生成候选 ZIP：`reconstructed-project-round-05-complete-v0.5.2-candidate.zip`。
- 候选 ZIP 完整性检查通过，SHA-256：`87dac66b5e4c6acfb2b2f4c7c07f3e40cf7554483458bede44957af2ca9474bf`。
- 在全新目录解压，安装前确认不存在 `node_modules/`。
- `npm install`、`npm run verify:all`、`npm run dev:micro-battle`、`npm run test:friendly-unit` 全部通过。
- 隔离了未完成的弓兵/投射物草稿；第五轮最终包只保留已验证的正式刀兵闭环。
- 验证期间真实网络请求 0，`wx.*` / `tt.*` 原生调用 0。

## Round 06 — BOW-PROJECTILE-COMBAT-01（2026-07-24）

- 在 Round 05 v0.5.2 累计工程上原地增量恢复，没有创建独立示例工程。
- 确认 `ok extends td`、`rd extends qY`、`on extends pP`，`vA` 为活动投射物管理器，`vj/vk` 为类型/复合池工厂。
- 新增正式弓兵注册 `1 / 弓 / BowSoldier`；刀兵注册与时序不变。
- 恢复 STOPPED 动画结算、目标二次验证、`SimpleDynamicArrow`、二次贝塞尔移动、目标移动跟踪、单目标命中、反向注销和双池复用。
- 开发动画驱动独立于正式类；暂停和生命周期代号防止旧 STOPPED 回调污染复用实例。
- 80ms 固定步下：800ms 发现目标，880ms 开始攻击，1440ms STOPPED/创建箭矢，1840ms 首次命中；三箭击杀 6HP Mob0。
- 第六轮 33 项测试两次确定性执行通过，静态检查 109/109，通过 Round 02–05 全量回归。
- 真实网络请求和 `wx.*`/`tt.*` 调用均为 0；不可变输入哈希保持一致。

## Round 06 累计验证与下一闭包确认（2026-07-24）

- `npm run verify:all` 在当前累计工作区通过，覆盖 Round 01–06、全部静态检查、9 个开发命令和累计文件审计。
- 第六轮专项测试 33/33，两次确定性执行；静态检查 109/109。
- 累计审计 20/20，发现测试文件 66 个；真实网络请求和 `wx.*`/`tt.*` 调用均为 0。
- 不可变的原始 bundle、decoded bundle、index 和 NET-01 HttpClient 哈希继续一致。
- 已确认下一建议方向为 `BATTLE-END-AND-GAMEOVER-CORE`：`nE` GameOverScene 主体 `51559–52842`，`sE.gameOver` `55064–55210`，`nE` 状态枚举和 UUID `66320–66361`。

## Round 06 候选累计包全新解压验证（2026-07-24）

- 候选 ZIP 不包含 `node_modules/`，压缩数据完整性检查通过。
- 在全新目录解压后执行 `npm install`，从工程内 `vendor/typescript-5.8.3.tgz` 安装 TypeScript 5.8.3。
- `npm run verify:all`、`npm run verify:round06`、`npm run dev:ranged-battle` 全部通过。
- 候选验证报告写入 `analysis/fresh-install-verification-round-06.json` 和 `.md`。
- 最终精确 ZIP 会在加入该报告后再次全新解压执行同样四条命令；精确 ZIP 哈希与验证结果在归档外部报告中交付，避免自哈希递归。


## ROUND-07A-SPEAR
- 恢复正式枪兵注册：index=2/text=枪/animationKey=pike。
- 来源：work/bundle.strings-decoded.js:24556-24749。
- 未恢复完整 sv/vA 枪击对象池依赖，保留到后续 Weapon/Bullet 阶段。

## ROUND-07B-CAVALRY
- ROUND-07B-CAVALRY: restored CavalrySoldier and cavalrySweep direct attack dependency.


## ROUND-07C-PROJECTILE-RUNTIME
Public projectile runtime extended with separate movement and hit strategy modules.

## Round 07D Bullet Factory
Completed registry and concrete projectile wrappers.


Round 07E Weapon Foundation completed.

## ROUND-07G-TRAIL2D
Restored Trail2D adapter and weapon rendering lifecycle framework.

## ROUND-07H-COMBAT-FOUNDATION-REPAIR
- Fixed projectile export syntax.
- Fixed ProjectileFactory tuple registration.
- Replaced spear/cavalry immediate damage placeholders with attack effect lifecycle.

## Round 07K — Buff System (v0.7.11)

恢复 20 个正式 Buff type、数值/状态/自定义 Handler、冲突规则、回合和毫秒生命周期、Unit/Enemy 目标契约，并接入长弓、角弓、神臂弓和落日弓。状态表现资源仍缺失。

## Round 07L — Core Skill/Boss/Enemy/Wave Runtime

- Added SkillFactory/SkillManager/SkillEffectPort with 19 registered skills.
- Added BossBase/BossFactory/BossManager and 12 distinct boss classes.
- Added Mob1/Mob2/Mob3/Zombie/Cavalry/Puppet core runtime classes.
- Added WaveManager with normal-wave counts, boss probabilities, forced-boss support and three-boss rotation.
- Integrated SkillMgr, BossMgr and WaveManager into DevelopmentBootstrap and BattleFlowCoordinator.
- Complex skill VFX and boss animation timelines remain explicit deferred effect contracts.


## Round 07M — Skill/Boss Presentation and GameOver (v0.7.13)
- Added deterministic boss skill timelines for all 12 bosses.
- Added concrete core handlers for all 12 Boss skills; presentation resources remain TODO_RESOURCE_MISSING.
- Added DeadEntityRegistry revival window, permanent demolition tile state, rain debuff/overlay lifecycle, level suppression/merge lock, devour HP/scale growth, darkness overlay lifetime.
- Added BattleResult, GameOverSceneController, UUID registration and return-to-main flow.
- Static check and deterministic skill/game-over smoke: PASS.

## Round 07N — Origin project resource integration (v0.7.14)

The supplied `origin_project/` was added as an immutable resource reference and runtime asset source. Generated scene, prefab, Spine, image and Trail2D catalogs. Restored the original `Spine2DRenderNode` wrapper contract, resource-backed prefab creation, enemy/Boss presentation, skill VFX resolution, formal GameOver node bindings, critical scene UUID validation and asset sync tooling. Boss Spine and six core skill VFX resources are now confirmed available. Audio subpackage files remain absent.

## Round 08 — Playable combat and Unity handoff

- Added engine-neutral battle economy, deck/card flow, command-driven placement, merge/level services and opponent AI.
- Integrated WeaponManager into formal UnitRegistry/BattleFlow lifecycle.
- Connected `BATTLE_FINISHED` to automatic GameOver and formal BattleResult creation.
- Added deterministic single-game smoke without DevelopmentUnitSpawner or manual gameOver.
- Exported Unity data catalogs and C# architecture mapping.
