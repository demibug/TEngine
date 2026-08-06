# ADou 任务进度变更记录

## 2026-08-04 · Change: minimal-battle-loop-gap-fix

### 目标
修补还原工程 61 个最简战斗闭环核心模块中的 3 个 BLOCKING 运行时缺口，并提供最简编排器与烟测，使 61 模块作为独立系统能跑通完整战斗循环（出兵→移动→打阿斗→放兵→攻击→死亡回收→胜负→入池），作为后续移植到 Unity 的逻辑验证基线。

3 个缺口：
1. 牌组 `drawText`/`drawHand` 会抽到农/铲/武将字，触发 `UnitRegistry` throw。
2. `WaveManager` 第 20 波（及 3/6/9/12/15 波）强制 Boss 生成，触发 `EnemyManager` throw。
3. `BattleManager` 硬要求 `specialSpawnPolicy` 注入，未注入即 throw。

### 修改/新增文件清单
修改（4 个 src 文件，增加模式开关 + 默认降级，不重写已有逻辑）：
- `src/deck/DeckDefinitions.js` — 导出 `BASE_SOLDIER_TEXTS`（复用已有 `BASE_POOL` 4 元素）。
- `src/deck/DeckManager.js` — 构造函数增加可选 `minimalMode`（默认 false）；`drawText` 最简模式只从 `BASE_SOLDIER_TEXTS` 抽取；`injectShovel` 最简模式跳过。
- `src/battle/WaveManager.js` — `configure` 增加可选 `skipBoss`（默认 false）；`planRound` 最简模式 `boss` 始终 false；`beginRound` 跳过 `bossManager.spawn` 并 null-guard；`bossManager` 从必填降为可选。
- `src/battle/BattleManager.js` — `_requireConfigured` 将 `specialSpawnPolicy` 移出必填列表；`_chooseSpecialSpawnIndex` 未注入时返回 -1（无特殊生成），不 throw。

新增（2 个文件）：
- `src/bootstrap/MinimalBattleBootstrap.js` — 轻量编排器，只 wire 61 个核心模块，注入 stub（dev presentation/audio 桩、null bossManager/skillManager），加载 maps/waves/enemies/units/deck-pool JSON，启动 GameLoop 跑通闭环。
- `tests/unit/MinimalBattleLoop.test.js` — 烟测用例，用 MockLaya 驱动 GameLoop 跑一局完整战斗。

### 验证结果
- **6.1 全量 node --check**：`src` 下全部 268 个 JS 文件 `node --check` 通过，pass 268 / fail 0。本 change 涉及的 5 个文件（DeckDefinitions/DeckManager/WaveManager/BattleManager/MinimalBattleBootstrap）全部 pass。
- **6.2 既有回归测试**：
  - 针对性回归（覆盖完整模式路径）：`DeckPool.test.js`（7/7 pass，验证 108 元素牌池 drawText 产铲/武将字）、`BootToBattleCore.test.js`（2/2 pass）、`BattleFirstFrame.test.js`（2/2 pass，WaveManager 默认 boss 波）、`BowAndKnifeAttackTogether`/`BowShootsMob0`/`DirectBattleDevelopmentMode`/`FriendlyUnitAttackTiming`/`GeneralProgression`/`GeneralSkillEntry`（均 pass，含 BattleManager specialSpawnPolicy 已注入路径）。
  - 全量回归：`tests/unit` + `tests/behavior` 共 102 个测试文件、457 个用例，pass 445 / fail 12。
  - 12 个失败均为 pre-existing，与本 change 无关：`tests/unit/EnemiesJsonValues.test.js`（10 个，纯 `unity-export/config/enemies.json` 数据校验，不引用被改 src）与 `tests/behavior/decode-strings.test.js`（2 个，缺 `analysis/string-decoding-report.json` 构建产物，ENOENT）。git 确认这两个测试文件及其数据文件均未被本 change 改动（工作树无修改、diff 为空）。
  - 本 change 引入的回归：0。
- **6.3 完整模式行为不变**：默认参数 `minimalMode=false`/`skipBoss=false` 路径未改动（构造函数默认值与原逻辑分支均保留）。佐证：DeckPool 完整模式 drawText 回归 pass、BattleFirstFrame 默认 boss 波 pass、BattleManager specialSpawnPolicy 已注入路径（BowShootsMob0/GeneralProgression/GeneralSkillEntry）pass。内联校验确认 DeckManager 默认 `minimalMode=false`。
- **U5 烟测**：`MinimalBattleLoop.test.js` 5 个用例全 pass（5.1+5.2 敌人移动扣血、5.3 放兵攻击死亡入池、5.4a 判负、5.4b 判胜、5.5 结束入池可续场）。

### 已知风险 / 后续建议
- **[Latent] DeckManager 最简模式未守卫路径**：`aiRearrange`（Si<2 铲前置排序）与 `drawCardNoRepeat`（武将字抽出后移除）路径在 `minimalMode=true` 下未显式守卫。当前最简编排器不触发 AI（无 aiController 注入），故不阻塞；若后续最简模式接入 AI 需复核这两条路径是否会触碰非基础兵字。
- **[Scope] isGameOver 判负为最小监听器镜像**：MinimalBattleBootstrap 的胜负判定采用最小监听器镜像实现，未含完整 `BattleFlowCoordinator` 的场景副作用（如场景切换、结算面板、音频）。仅用于逻辑闭环验证，不替代完整胜负流程。
- **[后续] 模式开关取舍**：`minimalMode`/`skipBoss` 为阶段性验证开关，移植到 Unity 时再决定是否保留；完整模式（默认）不受影响。
