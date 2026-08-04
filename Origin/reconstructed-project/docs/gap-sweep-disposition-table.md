# 零散缺口处置表（gap-sweep-and-presentation 组1）

> 取证复核文档，覆盖任务 1.1-1.7。每项记录：bundle 证据（行号+效果摘要）/ src 现状（file:line）/ 处置结论（补实现 / 标 DEFERRED_xxx / 确认非缺口）。
>
> 取证优先级：`src/` > `work/bundle.strings-decoded.js` > `original/bundle.js`。bundle 行号均指 `work/bundle.strings-decoded.js`（67832 行，字符串已解码）。
>
> 本文档为纯取证产出，不修改任何 .js 源码/CSV/其他 docs 文件。处置结论中的「补实现」均指向组2/3/4 对应任务，不在本工作单元内执行。

---

## 1.1 Boss 技能 Inspire（鼓舞，张角）

### bundle 证据（bundle:31120-31186，符号 `tj`，效果 `gw` 在 31149-31183）

- `bundle:31120` `tj = function() { ... class extends vf ... }` —— 张角 Boss 技能类定义，`RE="attackjiao"`/`UE="gojiao"`。
- `bundle:31152-31178` `gw` 方法（技能执行效果）：
  - `bundle:31166` `Laya["timer"]["once"](b[176], this, () => { ... })` —— 延迟触发（`b[176]` 为延迟毫秒数，经 `hu` 解码表）。
  - `bundle:31175` `this["HE"] = vi["instance"]()["qx"](this["enemy"]["x"], this["enemy"]["y"], this["Lh"], this["nm"])` —— 取范围内友军敌（`vi.qx` 为 EnemyManager 范围查询，参数 center x/y/radius/side；`this["nm"]` 即 boss.isPlayerLane）。`HE` 为目标列表。
  - `bundle:31176` 对每个目标连施 3 个 buff（`f = b[101]` 为 durationMs）：
    - `applyBuff(id, 6, .2, !0, f)` —— BuffType.SCALE(6) 值 .2，multiplicative=true
    - `applyBuff(id, 4, .5, !0, f)` —— BuffType.MAX_HP(4) 值 .5，multiplicative=true
    - `applyBuff(id, 3, .3, !0, f)` —— BuffType.MOVE_SPEED(3) 值 .3，multiplicative=true
  - `bundle:31177` `pC["instance"]()["playSound"]("zhangJiao_skill_horn")` —— 播放张角技能号角音效（在 timer 回调之外，`gw` 方法主体末尾）。

**效果摘要**：对范围内友军敌施 3 buff（SCALE .2 / MAX_HP .5 / MOVE_SPEED .3，durationMs=5000）+ 播放音效 `zhangJiao_skill_horn`。

### src 现状

- `src/skills/SkillEffectPort.js:23` inline lambda 注册 `Inspire`：
  ```
  ({alliedEnemies=[],durationMs=5000})=>{ ... 3 buff applyBuff ... return{status:'APPLIED',ids}; }
  ```
  **取证结论：inline lambda 仅还原 3 buff 施加，未还原音效 `zhangJiao_skill_horn` 播放（bundle:31177）。** 即 inline lambda 对 buff 逻辑忠实，但音效为缺口。
- `src/skills/effects/InspireEffect.js`（**当前已存在**，git 未跟踪 `??`，2026-08-04 创建）：已从 inline lambda 迁出为独立类文件，`execute()` 忠实还原 3 buff（`:49-51`），**并已补齐音效**（`:55-57` `this.audioRegistry.play('zhangJiao_skill_horn')`，audioRegistry 缺省时跳过不抛异常）。文件头明确标注「inline lambda 未还原音效播放，spec 验收要求 MUST 播放 zhangJiao_skill_horn，此处补齐」。
- `src/skills/effects/index.js:5-6`：**未导出** `InspireEffect`（barrel 导出缺口）。
- `src/skills/SkillEffectPort.js:23`：**仍为 inline lambda**，未改为 `new InspireEffect(this.services()).execute(ctx)`（注册迁移缺口）。

### 处置结论：补实现（指向组2 任务 2.1/2.3/2.4）

- 3 buff 逻辑 + durationMs=5000：inline lambda 已忠实，非缺口。
- 音效 `zhangJiao_skill_horn`：inline lambda 缺失，`InspireEffect.js` 已补齐——属「补实现」，由组2 任务 2.1 承载（独立类文件 + 音效）。
- barrel 导出（`index.js`）+ `SkillEffectPort` 注册迁移：组2 任务 2.3/2.4 承载，当前未完成（index.js 未导出、SkillEffectPort 仍 inline）。
- **不标 DEFERRED**：inline lambda 已忠实还原 buff 效果（非桩），缺的只是类文件包装 + 音效，属「补齐」非「新建」。

---

## 1.2 Boss 技能 CavalryOrder（铁骑号令，华雄）

### bundle 证据（bundle:32753-32802，符号 `tz`，效果 `gw` 在 32784-32798）

- `bundle:32753` `tz = function() { ... class extends vf ... }` —— 华雄 Boss 技能类定义，`RE="attackhx"`/`UE="gohx"`。
- `bundle:32784-32794` `gw` 方法（技能执行效果）：
  - `bundle:32789` `pC["instance"]()["playSound"]("summon_cavalry_skill")` —— 播放召唤骑兵音效。
  - `bundle:32789-32792` `this["tw"]["BE"](this["RE"], 3, () => { this["changeState"](1) }, () => { vi["instance"]()["jL"](5, this["nm"]) })` —— `BE` 为动画事件回调包装，第二回调 `() => { vi["instance"]()["jL"](5, this["nm"]) }` 召唤 5 个骑兵（`vi.jL(5, nm)`，`nm` 即 boss.isPlayerLane）。

**效果摘要**：播放音效 `summon_cavalry_skill` + 召唤 5 骑兵（`vi.jL(5, nm)`）。

### src 现状

- `src/skills/SkillEffectPort.js:24` inline lambda 注册 `CavalryOrder`：
  ```
  ({boss,enemyManager})=>{ ... for(i<5) manager.spawnByKey('Cavalry',boss.isPlayerLane,false) ... return{status:'APPLIED',enemyIds}; }
  ```
  **取证结论：inline lambda 仅还原 5 骑兵召唤，未还原音效 `summon_cavalry_skill` 播放（bundle:32789）。** 即召唤逻辑忠实，音效为缺口。
- `src/skills/effects/CavalryOrderEffect.js`（**当前已存在**，git 未跟踪 `??`，2026-08-04 创建）：已从 inline lambda 迁出为独立类文件，`execute()` 忠实还原 5 骑兵召唤（`:42-44`），**并已补齐音效**（`:37-38` `this.audioRegistry.play('summon_cavalry_skill', { ownerId: boss.id })`）。文件头明确标注「inline lambda 未还原音效播放，此处补齐」。
- `src/skills/effects/index.js:5-6`：**未导出** `CavalryOrderEffect`（barrel 导出缺口）。
- `src/skills/SkillEffectPort.js:24`：**仍为 inline lambda**，未改为 `new CavalryOrderEffect(this.services()).execute(ctx)`（注册迁移缺口）。

### 处置结论：补实现（指向组2 任务 2.2/2.3/2.4）

- 5 骑兵召唤逻辑：inline lambda 已忠实，非缺口。
- 音效 `summon_cavalry_skill`：inline lambda 缺失，`CavalryOrderEffect.js` 已补齐——属「补实现」，由组2 任务 2.2 承载。
- barrel 导出（`index.js`）+ `SkillEffectPort` 注册迁移：组2 任务 2.3/2.4 承载，当前未完成。
- **不标 DEFERRED**：召唤逻辑已忠实（非桩），缺的只是类文件包装 + 音效，属「补齐」。

---

## 1.3 20 类 Buff handler 覆盖核对

### 注册与强制覆盖机制

- `src/buffs/BuffHandlerFactory.js:13-21` `registry = new Map([...])` 注册全部 20 类 BuffType：
  - `:14-15` 0-6（ATTACK_POWER/ATTACK_SPEED/ATTACK_RANGE/MOVE_SPEED/MAX_HP/HP/SCALE）→ `NumberBuffHandler`
  - `:16` 7（CUSTOM）→ `CustomBuffHandler`；8（STUN）/9（FALL）→ `states.StunBuffHandler`/`states.FallBuffHandler`
  - `:17` 10（PIERCE）/11（ELECTROCUTE）/12（KNOCKBACK）→ `states.PierceBuffHandler`/`states.ElectrocuteBuffHandler`/`KnockbackBuffHandler`
  - `:18` 13（CHAOS）/14（BURN_STATIC）/15（LIMIT）→ `states.ChaosBuffHandler`/`BurnStaticBuffHandler`/`states.LimitBuffHandler`
  - `:19` 16（LOCK）/17（KNOCKDOWN）/18（SUPPRESSION）→ `states.LockBuffHandler`/`states.KnockdownBuffHandler`/`states.SuppressionBuffHandler`
  - `:20` 19（CHARM）→ `states.CharmBuffHandler`
- `src/buffs/BuffHandlerFactory.js:24-34` `validate()`：遍历 `BuffDefinitions` 强制每类有 producer、kind 匹配（NUMBER→NumberBuffHandler、CUSTOM→CustomBuffHandler）、registry 无悬空 key。启动期构造即调用（`:22`），缺失即 throw。

### 20 类逐项状态

| 值 | 类型名 | bundle 证据 | src handler | 处置 |
|---|---|---|---|---|
| 0-6 | ATTACK_POWER/ATTACK_SPEED/ATTACK_RANGE/MOVE_SPEED/MAX_HP/HP/SCALE | 数值类 stat delta `zw` | `NumberBuffHandler`（`NumberBuffHandler.js:5`，`applyDelta`→`target.zw`）实现 | 确认非缺口 |
| 7 | CUSTOM | onStart/onEnd 自定义 | `CustomBuffHandler`（`CustomBuffHandler.js:3`，`layer.onStart`/`onEnd`）实现 | 确认非缺口 |
| 8 | STUN | state channel `[1,0]` | `StunBuffHandler`（`TimedStateBuffHandlers.js:4`）实现 | 确认非缺口 |
| 9 | FALL | state channel `[0]` + `vi.Cv` 撞击副作用（`bundle:21806`） | `FallBuffHandler`（`TimedStateBuffHandlers.js:8`，仅 state channel，**未还原 `vi.Cv` 撞击**） | 标 `DEFERRED_FALL_IMPACT`（见下） |
| 10 | PIERCE | state channel `[0]` | `PierceBuffHandler`（`:9`）实现 | 确认非缺口 |
| 11 | ELECTROCUTE | state channel `[1,0]` | `ElectrocuteBuffHandler`（`:5`）实现 | 确认非缺口 |
| 12 | KNOCKBACK | state channel `[5]` + 合并重施向量 | `KnockbackBuffHandler`（`KnockbackBuffHandler.js:5`，`onMergedLayer`→`setState(5,true,data.qw)`）实现 | 确认非缺口 |
| 13 | CHAOS | state channel `[1,0,2]` | `ChaosBuffHandler`（`:6`）实现 | 确认非缺口 |
| 14 | BURN_STATIC | state channel `[4]` + 每 1000ms 汇总伤害 | `BurnStaticBuffHandler`（`BurnStaticBuffHandler.js:5`，`tickIntervalMs=1000`，`update` 汇总 layers 伤害 `setState(4,true,damage)`）实现 | 确认非缺口 |
| 15 | LIMIT | **bundle 空壳** `u0`（`bundle:22133-22169`，`Qw` 仅 `super.Qw`，`yv` 返回 `""`，`pv` 返回 `!1`） | `LimitBuffHandler`（`TimedStateBuffHandlers.js:13`，`label()` 返回 `''`，最小子类） | 确认非缺口（bundle 本身空壳，src 最小子类正确） |
| 16 | LOCK | state channel `[1,2]` | `LockBuffHandler`（`:7`）实现 | 确认非缺口 |
| 17 | KNOCKDOWN | state channel `[1]` | `KnockdownBuffHandler`（`:10`，`mergeLayers:true,replaceDuration:true`）实现 | 确认非缺口 |
| 18 | SUPPRESSION | state channel `[3]` | `SuppressionBuffHandler`（`:11`）实现 | 确认非缺口 |
| 19 | CHARM | **bundle 空壳** `u1`（`bundle:22170+`，类体空 `{}`，仅 `yv` 返回 `"魅惑"`） | `CharmBuffHandler`（`TimedStateBuffHandlers.js:12`，`label()` 返回 `'魅惑'`，最小子类） | 确认非缺口（bundle 本身空壳，src 最小子类正确） |

- `src/buffs/BuffTypes.js:3-24`：20 类 BuffType 值 0-19 定义齐备。
- `src/buffs/BuffDefinitions.js:5-48`：`stateChannels`/`labels`/`definitions` 覆盖 8-19，FALL channels=`[0]`（`:12`）、LIMIT channels=`[6]` label=`''`（`:17,28`）、CHARM channels=`[2,3]` label=`'魅惑'`（`:16,32`）。
- `src/buffs/StateBuffHandler.js:56-62` `applyState`：经 `target.setState(channel, enabled, custom)` 设状态通道——确认 fall 仅设 channel 0，无 `vi.Cv`。

### fall(9) 撞击副作用 DEFERRED_FALL_IMPACT

- `bundle:21806` `Qw` 方法（fall buff 应用方法）末尾：`vi["instance"]()["Cv"](a["num"], [{id: this["target"]["id"]}], a["qw"])` —— `vi.Cv` 为 EnemyManager 碰撞伤害方法（参数 num + 目标 id 列表 + qw），即被击退单位撞击其他单位的连锁伤害。
- `bundle:21806` 前段为 Laya Tween 视觉特效（scaleY/rotation 动画）+ `qs.Ng` 位置调整（属表现层）。
- src `FallBuffHandler`（`TimedStateBuffHandlers.js:8`）仅经 `StateBuffHandler.applyState` 设 channel 0（吹飞状态本身生效），**未还原 `vi.Cv` 撞击连锁伤害**。
- **处置：标 `DEFERRED_FALL_IMPACT`**。`vi.Cv` 的完整语义（撞击伤害计算/连锁对象选取）需进一步取证；`FallBuffHandler` 保留 state channel 不阻塞吹飞主效果，撞击副作用待取证后补。属逻辑层非纯表现（碰撞伤害结算），但因取证未完整标 DEFERRED。

### 处置结论（1.3 整体）

- 20 类全覆盖，机械逻辑忠实 bundle：**确认非缺口**（核对表产出本身，由组5 任务 5.1-5.4 承载）。
- `limit`(15)/`charm`(19)：bundle 本身空壳（`u0`/`u1`），src 最小子类正确——确认非缺口。
- `fall`(9) `vi.Cv` 撞击副作用：标 `DEFERRED_FALL_IMPACT`，待取证后补（不阻塞吹飞 state channel）。

---

## 1.4 武器 5 把属性加成

### bundle 证据

武器纯属性加成（无 special 技能）5 把，数值字段在 `WEAPON_DEFINITIONS` 中已存在（无独立 bundle 行号取证需求——本项为确认非缺口，数值已由提案 ④ 落地）。

### src 现状

- `src/weapons/types/Weapon.js` `WEAPON_DEFINITIONS`（`:5-25`）5 把纯属性武器：
  - `:9` `'3:hN': { name:'铁剑', attackType:'sword', addAttackPower:3 }` —— 铁剑 +3 攻
  - `:10` `'3:h2': { name:'长剑', attackType:'sword', attackRangeBonus:0.5 }` —— 长剑 +0.5 距
  - `:15` `'1:hs': { name:'大戟', attackType:'pike', attackRangeBonus:1 }` —— 大戟 +1 距
  - `:21` `'2:hC': { name:'长刀', attackType:'melee', attackRangeBonus:0.5 }` + `'1:hp': { name:'长枪', attackType:'pike', attackRangeBonus:0.5 }` —— 长刀/长枪 +0.5 距
- `src/weapons/WeaponBase.js:10-19` `init(id,type)`：经 `getConfig()` 读取 `addAttackPower`/`attackRangeBonus`/`attackSpeedBonus` 字段（`:14-17`），非零值生效。
- `src/weapons/WeaponBase.js:48` `getCombatModifiers()`：返回 `{ attackPower:this.attackPowerBonus, range:this.attackRangeBonus, attackSpeed:this.attackSpeedBonus }` 真实值。

### 处置结论：确认非缺口（已由提案 ④ 覆盖）

5 把纯属性武器数值字段已存在，`init` 读取、`getCombatModifiers()` 返回真实值。已由提案 ④（`special-weapons-projectiles`，已归档）完整修复。本提案仅在核对表标注「已覆盖」，不补实现。

---

## 1.5 Deck 牌池与刷新/辅助分支

### bundle 证据

- **牌池 108 元素**（`bundle:11963-11970`，`nu` 类构造函数）：`this["eh"]`/`this["ah"]`/`this["nh"]` 三组同数组（`bundle:11969`），分布：`刀`×21/`弓`×19/`枪`×19/`骑`×18/`铲`×11 + 武将字（刘/赵/赵/云/关/羽/平/兴/马/马/超/张/张/飞/苞/翼/黄/黄/忠/盖/`eF`/备）。注：`eF` 经 `bundle:3304` 解码为 `"祖"`（非 `农`）；`农` 仅出现在 `dP` 基础字判定表（`bundle:46553`），牌池数组本身含 `祖` 不含 `农`。元素总数约 108。
- **刷新 `xY`**（`bundle:49525-49561`）：cost 逻辑——`nm`(side) 选 gold/Ji 余额与 fi/gi cost；余额不足 → `showTip("馒头不足")` + `playSound("popup_notification")`，返回 `{success:!1, reason:"馒头不足"}`（`:49540-49551`）；否则扣 cost、`fi/gi += 2`（cost+2/次）、调 `NY(l)` 两阶段清除、AI 侧（`!l`）调 `qY()`+`PY()`，返回 `{success:!0}`（`:49552-49560`）。
- **`bO` 抽牌**（`bundle:46503-46528`）：`au.Li.length>=2` → 从 `Li` 特殊列表抽（`:46514`）；否则 `kO`/`SO` 池随机抽（`:46515-46518`）；基础字（刀/枪/弓/骑/铲/农）不移除，武将字在 `Fi`/`Oi` 置位时 splice 移除（`:46519-46521`）；`"刀"` 兜底（`:46516`）。
- **`xO` 铲注入**（`bundle:46529-46545`）：`roundDay>3` 直接返回（`:46536`）；否则数 `kO` 中 `铲`，`Math.floor(f/5)` 次注入 1 铲到 `kO`+`SO`（`:46537-46540`）。
- **`dP` 武将字复制**（`bundle:46546-46574`）：基础字表 `['刀','弓','枪','骑','铲','农']`（`:46553`），遍历池中非基础字（武将字）50% 概率（`Math.random()<.5`）push 复制（`:46557-46563`），置位 `Fi`/`Oi`（`:46565`）。
- **`qY` AI 重排**（`bundle:49563-49595`）：`WO.ub(3,false)` 取 AI 手牌槽（`:49575`）；`n=map.fe`（5 卡）循环 `bO(false)` 抽牌；`Si<2` 或 `铲` → push `l`（前置），否则 push `m`（`:49579`）；`Si>=2` 则 `m` 追加 `l`（`:49581-49582`）；逐槽非 `铲` 调 `gP(3,f,false,d)` 生成（`:49588`），`铲` 调 `k.setItem(null,d)`（`:49594`）。
- **`NY` 两阶段清除**（`bundle:49597-49614`）：`WO.ub(3,a)` 取手牌槽（`:49612`）；遍历槽逐个 despawn（`WP`/`HP`/`Nb`/`cA` 不同类型移除，`:49613`）；`p.removeAll()` 清除（`:49614`）。

### src 现状

- `src/deck/DeckDefinitions.js:2` `BASE_POOL = Object.freeze(['刀','弓','枪','骑'])` —— **4 元素**（缺 `铲`/武将字）。
- `src/units/UnitConfig.js:8` `BASE_SOLDIER_TEXTS = Object.freeze(['刀','弓','枪','骑'])` —— 牌池第二源（`DeckManager.poolForSide` 经 `gameData.friendlyUnits.texts` 读取 `FriendlyUnitConfig.texts`），**4 元素**。
- `src/deck/DeckManager.js:16` `poolForSide(_side)`：取 `gameData.friendlyUnits.texts || this.definitions.basePool`，均 4 元素。
- `src/deck/DeckManager.js:17` `drawText(side)`：均匀 4 抽（`pool[Math.floor(r*pool.length)]`），**无 `bO` 武将字 no-repeat / `Li` 特殊列表**。
- `src/deck/DeckManager.js:32-38` `refresh(side)`：调 `economy.payRefresh` 后直接重抽（`hand[i]=createCard(drawText...)`），**无 `NY` 两阶段清除（未先 despawn）**、**无 `qY` AI 重排**。
- `src/deck/DeckManager.js`：**无 `xO` 铲注入 / `dP` 武将字复制 / `qY` AI 重排方法**。
- `src/battle/BattleEconomy.js:32-40` `payRefresh(side)`：cost 逻辑忠实 `bundle:49525-49561`——`refreshCost`（10 基础）、`spend` 不足返回 `{success:false, reason:'馒头不足'}`（`:27`）、扣 cost 后 `playerRecruitCost += 2`/`opponentRecruitCost += 2`（`:36-37`）、`refreshCount++`、返回 `{...result, nextCost}`（`:39`）。**确认忠实，非缺口**。

### 处置结论

| 分支 | bundle 证据 | src 现状 | 处置 |
|---|---|---|---|
| 牌池 108 元素 | `bundle:11969`（刀×21/弓×19/枪×19/骑×18/铲×11+武将字） | `DeckDefinitions.js:2`/`UnitConfig.js:8` 仅 4 元素 | 补实现（组3 任务 3.1-3.3） |
| `bO` 抽牌 no-repeat | `bundle:46503-46528` | `DeckManager.js:17` 均匀 4 抽，无 no-repeat | 补实现（组3 任务 3.4） |
| `xO` 铲注入 | `bundle:46529-46545` | `DeckManager.js` 无 `xO` | 补实现（组3 任务 3.5） |
| `dP` 武将字复制 | `bundle:46546-46574` | `DeckManager.js` 无 `dP` | 补实现（组3 任务 3.6） |
| `qY` AI 重排 | `bundle:49563-49595` | `DeckManager.js` 无 `qY` | 补实现（组3 任务 3.7） |
| `NY` 两阶段清除 | `bundle:49597-49614` | `DeckManager.js:32-38` 直接重抽未先 despawn | 补实现（组3 任务 3.8） |
| 刷新 cost | `bundle:49525-49561` | `BattleEconomy.js:32-40` 忠实 | 确认非缺口 |
| `bO` 的 `Li` 特殊列表 | `bundle:46514` `au.Li.length>=2` | src 无 `Li` 语义 | 标 `DEFERRED_LI_DRAW_LIST`（`bO` 走正常牌池兜底，组3 任务 3.4） |
| 武将字合成端到端 | 依赖 ② 武将系统 | 牌池武将字可达即补齐 | 标 `DEFERRED_GENERAL_MERGE`（组3 任务 3.11） |

---

## 1.6 round03 失败项

### bundle 证据

N/A（本项为测试基线断言过时，非 bundle 逻辑缺口）。

### src 现状

- `package.json:17` `test:round03`：8 个测试文件（`BootToBattleCore`/`AnimationEntityPool`/`BootToMainScene`/`MainToMatchScene`/`MatchToBattleScene`/`DirectBattleDevelopmentMode`/`BattleFirstFrame`/`BattleCleanup`），合计 15 个测试用例（任务所述「15项」）。
- `tests/behavior/BattleFirstFrame.test.js:25` `assert.equal(context.enemyManager.prepareWaveCount, 1)` —— **过时断言**：首帧波次经 `WaveManager.beginRound` 配置而非 `enemyManager.prepareWave()`。`src/battle/BattleManager.js:111-123` `_beginWave`：`if (this.waveManager)` 分支恒走 `waveManager.beginRound`（`:115`），`else` 分支 `enemyManager.prepareWave()`（`:118`）为 dev harness 死路径（WaveManager 已注入）。
- `src/battle/WaveManager.js:11` `planHistory=[]`、`:34` `planRound` push planHistory、`:36` `beginRound` 调 `planRound` —— **首帧后 `waveManager.planHistory.length === 1`** 为有效断言路径。
- `tests/behavior/BattleFirstFrame.test.js:44` `assert.throws(() => context.enemyFactory.create('Mob1'), /未为类型 Mob1 注册创建器/)` —— **过时断言**：Mob1 已由 P1-02 注册。`src/bootstrap/DevelopmentBootstrap.js:202` `enemyFactory.registerPooledClass('Mob1', Mob1Enemy, ...)`，全 7 类（Mob0/Mob1/Mob2/Mob3/Zombie/Cavalry/Puppet）注册于 `:201-207`。
- `tests/behavior/BattleFirstFrame.test.js`：git 未修改（仍为含 `:25`/`:44` 过时断言的提交态）。
- 其余 13 项用例：`updateCount===1`/`firstFrameExecuted===true`/`wave===1`/`state===SPAWNING`/`Mob0` pair spawn 等均经当前架构通过（DirectBattle 已通过）。

### 处置结论：补实现（改测试，指向组4 任务 4.1/4.2）

- `:25` `prepareWaveCount===1` → `waveManager.planHistory.length===1`（或 `waveManager.roundPlans.has(1)`）：组4 任务 4.1，断言 WaveManager 路径。
- `:44` `Mob1` → `Mob99`（真实未注册类型）：组4 任务 4.2，保留「未注册类型抛异常」契约。
- **改测试不改逻辑**：取证确认逻辑正确（WaveManager 恒走、Mob1 已注册是成功非缺口），断言测试旧状态须跟进。修正后 15/15 通过 + 验证链恢复（组4 任务 4.3/4.4）。

---

## 1.7 文档产出（本处置表）

本处置表即任务 1.7 交付物。覆盖 1.1-1.6 每项 bundle 证据/src 现状/处置结论，处置结论明确分为：补实现（指向组2/3/4 对应任务）/ 标 DEFERRED_xxx / 确认非缺口。

### DEFERRED 标注汇总

| 标注 | 适用项 | 含义 | 不阻塞主效果 |
|---|---|---|---|
| `DEFERRED_FALL_IMPACT` | 1.3 fall(9) | `vi.Cv` 撞击连锁伤害语义待取证 | 吹飞 state channel 保留生效 |
| `DEFERRED_LI_DRAW_LIST` | 1.5 `bO` `Li` 特殊列表 | `au.Li` 武将合成列表语义未完整取证 | `bO` 走正常牌池兜底 |
| `DEFERRED_GENERAL_MERGE` | 1.5 武将字合成端到端 | 依赖 ② 武将系统 | 牌池武将字可达即补齐 |

### 确认非缺口汇总

- 1.3：20 类 Buff handler 全覆盖（`limit`/`charm` bundle 空壳 src 正确）。
- 1.4：武器 5 把属性加成（已由提案 ④ 覆盖）。
- 1.5 刷新 cost：`BattleEconomy.payRefresh` 忠实 `bundle:49525-49561`。
