# 20 类 Buff handler 覆盖核对表

> 变更：`gap-sweep-and-presentation` 组5 任务 5.1-5.4
> 取证优先级：`src/` > `work/bundle.strings-decoded.js` > `original/bundle.js`
> 结论：20 类 BuffType handler 全部实现，机械逻辑忠实还原 bundle，**非缺口**。唯一待确认项 `fall`(9) 的 `vi.Cv` 撞击副作用标注 `DEFERRED_FALL_IMPACT`。

## 1. 枚举与注册基线

### 1.1 20 类 BuffType 枚举（src/buffs/BuffTypes.js:3-24）

`BuffType`（`src/buffs/BuffTypes.js:3-24`）定义 20 个枚举值 0-19，`BuffName`（`:26-31`）给出对应类型名。bundle 侧 `ql` 枚举（`bundle:11923`）与 src 逐项一致（`attPower`=0 ... `charm`=19）。

| 值 | 名称 | src 行号 | bundle 行号 |
|---|---|---|---|
| 0 | ATTACK_POWER（attPower） | BuffTypes.js:4 | 11923 |
| 1 | ATTACK_SPEED（attSpeed） | BuffTypes.js:5 | 11923 |
| 2 | ATTACK_RANGE（attRange） | BuffTypes.js:6 | 11923 |
| 3 | MOVE_SPEED（moveSpeed） | BuffTypes.js:7 | 11923 |
| 4 | MAX_HP（maxHp） | BuffTypes.js:8 | 11923 |
| 5 | HP（hp） | BuffTypes.js:9 | 11923 |
| 6 | SCALE（scale） | BuffTypes.js:10 | 11923 |
| 7 | CUSTOM（custom） | BuffTypes.js:11 | 11923 |
| 8 | STUN（stun） | BuffTypes.js:12 | 11923 |
| 9 | FALL（fall） | BuffTypes.js:13 | 11923 |
| 10 | PIERCE（pierce） | BuffTypes.js:14 | 11923 |
| 11 | ELECTROCUTE（electrocute） | BuffTypes.js:15 | 11923 |
| 12 | KNOCKBACK（knockback） | BuffTypes.js:16 | 11923 |
| 13 | CHAOS（chaos） | BuffTypes.js:17 | 11923 |
| 14 | BURN_STATIC（burnStatic） | BuffTypes.js:18 | 11923 |
| 15 | LIMIT（limit） | BuffTypes.js:19 | 11923 |
| 16 | LOCK（lock） | BuffTypes.js:20 | 11923 |
| 17 | KNOCKDOWN（knockdown） | BuffTypes.js:21 | 11923 |
| 18 | SUPPRESSION（suppression） | BuffTypes.js:22 | 11923 |
| 19 | CHARM（charm） | BuffTypes.js:23 | 11923 |

### 1.2 BuffHandlerFactory 注册全部 20 类（src/buffs/BuffHandlerFactory.js:13-21）

`BuffHandlerFactory` 构造器（`BuffHandlerFactory.js:10-23`）以 `this.registry = new Map([...])`（`:13-21`）注册全部 20 类，并在构造末尾立即调用 `this.validate()`（`:22`）。`create(type)`（`:35-39`）按 `registry.get(Number(type))` 取 handler 类，未注册则抛 `Buff(ID:...) is not implemented`。

注册映射（`BuffHandlerFactory.js:13-21`）与 bundle `BUFF_PRODUCERS` 注册表 `rZ`（`bundle:26632-26653`）逐项一致：

- `[0..6, ua]`（src 0-6 → NumberBuffHandler）≈ bundle `[0, ua]...[6, ua]`（`26633-26639`），`ua`=poolNumberBuff=`Laya.Pool.createByClass(uM)`（`bundle:3058-3061`），`uM`=NumberBuffHandler 类（`bundle:21072`）
- `[7, ug]`（src 7 → CustomBuffHandler）≈ bundle `[7, ug]`（`26646`），`ug`=produceCustomBuff=`new uS`（`bundle:2660-2662`），`uS`=CustomBuffHandler 类（`bundle:21586`）
- `[8, states.StunBuffHandler]` ≈ bundle `[8, () => new uO]`（`26640`），`uO`=Stun 类（`bundle:21348`）
- `[9, states.FallBuffHandler]` ≈ bundle `[9, () => new uU]`（`26647`），`uU`=Fall 类（`bundle:21778`）
- `[10, states.PierceBuffHandler]` ≈ bundle `[10, () => new uW]`（`26648`），`uW`=Pierce 类（`bundle:21855`）
- `[11, states.ElectrocuteBuffHandler]` ≈ bundle `[hp, () => new u4]`（`26641`，hp=11），`u4`=Electrocute 类（`bundle:22223`）
- `[12, KnockbackBuffHandler]` ≈ bundle `[hq, () => new uQ]`（`26643`，hq=12），`uQ`=Knockback 类（`bundle:21457`）
- `[13, states.ChaosBuffHandler]` ≈ bundle `[hs, () => new uP]`（`26642`，hs=13），`uP`=Chaos 类（`bundle:21397`）
- `[14, BurnStaticBuffHandler]` ≈ bundle `[hv, () => new uR]`（`26644`，hv=14），`uR`=BurnStatic 类（`bundle:21500`）
- `[15, states.LimitBuffHandler]` ≈ bundle `[hw, () => new u0]`（`26652`，hw=15），`u0`=Limit 空壳（`bundle:22133`）
- `[16, states.LockBuffHandler]` ≈ bundle `[hx, () => new uT]`（`26645`，hx=16），`uT`=Lock 类（`bundle:21683`）
- `[17, states.KnockdownBuffHandler]` ≈ bundle `[hy, () => new uY]`（`26649`，hy=17），`uY`=Knockdown 类（`bundle:22003`）
- `[18, states.SuppressionBuffHandler]` ≈ bundle `[hz, () => new uZ]`（`26650`，hz=18），`uZ`=Suppression 类（`bundle:22066`）
- `[19, states.CharmBuffHandler]` ≈ bundle `[hA, () => new u1]`（`26651`，hA=19），`u1`=Charm 空壳（`bundle:22170`）

### 1.3 validate() 启动期强制（src/buffs/BuffHandlerFactory.js:24-34）

`validate()`（`:24-34`）启动期强制三重校验，与 bundle `oJ.init` 内的 `BuffHandlerRegistry` 校验（`bundle:22450-22468`）逐条对应：

1. 遍历 `BuffDefinitions`（src `:25`）每个声明类型，`registry.get(type)` 必须存在，否则抛 `declares ${name}, but no producer is registered`（src `:26-27` ≈ bundle `:22455`）。
2. `definition.kind === NUMBER && !isNumber` 抛 `must use NumberBuffHandler`（src `:28-29` ≈ bundle `:22457-22458`），`isNumber = ClassType === NumberBuffHandler`（src `:28` ≈ bundle `t4(f)`=`a===ua`，`bundle:3070-3072`）。
3. `definition.kind !== NUMBER && isNumber` 抛 `cannot use NumberBuffHandler`（src `:30` ≈ bundle `:22462`）；`kind === CUSTOM && ClassType !== CustomBuffHandler` 抛 `must use CustomBuffHandler`（src `:31` ≈ bundle `:22460`）。
4. 反向校验：`registry.keys()` 每项必须在 `BuffDefinitions` 中，否则抛 `producer ${type} has no definition`（src `:33` ≈ bundle `:22466-22467`）。

`BuffDefinitions`（`src/buffs/BuffDefinitions.js:35-48`）由循环生成 20 条定义：0-6 NUMBER（`:36-38`）、7 CUSTOM（`:39`）、8-19 STATE（`:40-48`，含 `channels`/`label`）。`stateChannels`（`:5-18`）定义各状态 buff 的 state channel 数组，与 bundle `uh`=`setState`（`bundle:3064-3067`，遍历 `wv` channel 数组调 `setState`）一致。

## 2. 20 类逐项核对表（0-19）

> 状态说明：实现=机械逻辑忠实还原 bundle；桩=最小子类（bundle 本身即空壳）；缺失=未注册/未实现。
> 处置说明：确认非缺口=已忠实还原无需补；标 DEFERRED=待取证后补，不阻塞主效果。

### 2.1 数值类（0-6，NumberBuffHandler）

| 值 | 类型名 | bundle 证据（行号+效果） | src handler 状态 | src 证据 | 处置 |
|---|---|---|---|---|---|
| 0 | attPower | `uM`（`bundle:21072`）`yv` 返回 `攻击力`+`提升/降低`（`:21098-21111`）；`fv`=`zw(type,delta)` stat delta（`:21125-21131`） | 实现 | NumberBuffHandler.js:5-58；`effectiveDelta`/`applyDelta`→`target.zw`（`:44-49`）；`label()` 攻击力/攻速/范围（`:51-57`） | 确认非缺口 |
| 1 | attSpeed | `uM` 同上，`yv` case 1=`攻速`（`bundle:21105-21106`）；stat delta 经 `zw` | 实现 | NumberBuffHandler.js（同 0，共用 handler，type=1 走 `zw`） | 确认非缺口 |
| 2 | attRange | `uM` 同上，`yv` case 2=`范围`（`bundle:21102-21103`）；stat delta 经 `zw` | 实现 | NumberBuffHandler.js（同 0，共用 handler，type=2 走 `zw`） | 确认非缺口 |
| 3 | moveSpeed | `uM` 同上，`yv` default 返回 null（仅 0/1/2 有标签，`bundle:21108-21109`）；stat delta 经 `zw` | 实现 | NumberBuffHandler.js（共用 handler，type=3 走 `zw`，`label()` 仅 0/1/2 有名） | 确认非缺口 |
| 4 | maxHp | `uM` 同上；stat delta 经 `zw` | 实现 | NumberBuffHandler.js（共用 handler，type=4 走 `zw`） | 确认非缺口 |
| 5 | hp | `uM` 同上；stat delta 经 `zw` | 实现 | NumberBuffHandler.js（共用 handler，type=5 走 `zw`） | 确认非缺口 |
| 6 | scale | `uM` 同上；stat delta 经 `zw` | 实现 | NumberBuffHandler.js（共用 handler，type=6 走 `zw`） | 确认非缺口 |

**核对说明**：bundle `uM`（`bundle:21072-21205`）继承 `rE`（BuffHandlerBase），`Qw`=`iv`（add，`:21117-21123`），`Kw`=`fv(num,Nw)`+push layer（`:21137-21153`），`nv`=splice+`uc(...,true)` 撤销（`:21159-21170`），`ev`=modify 重算 delta（`:21172-21186`）。src `NumberBuffHandler`（`NumberBuffHandler.js:5-58`）逐方法对齐：`addLayer`=`createLayer`+`effectiveDelta`+`applyDelta`+push+`registerRoundExpiry`（`:14-21`），`modifyLayer`=`applyDelta(-,true)`+重算+`applyDelta(+,false)`（`:23-32`），`removeLayer`=`applyDelta(-,true)`+splice（`:34-41`），`applyDelta`→`target.zw(type,value,removing)`（`:44-49`）。`effectiveDelta`（`:6-12`）区分加法/乘法（`multiplicative`=`Nw`，乘法取 `target.jw(type)*num`）忠实 bundle `ud`（乘法基线读取）。stat delta `zw` 机制完整还原。

### 2.2 custom 类（7，CustomBuffHandler）

| 值 | 类型名 | bundle 证据（行号+效果） | src handler 状态 | src 证据 | 处置 |
|---|---|---|---|---|---|
| 7 | custom | `uS`（`bundle:21586-21681`）继承 `rE`；`Kw` 从 `a.qw` 取 `Bv`/`onEnd`/`onStart`，push layer，调 `d.onStart(this)`（`:21626-21647`）；`nv`=splice+`e.onEnd.call(e,this)`（`:21652-21659`）；`ev` 仅改 time（`:21664-21672`） | 实现 | CustomBuffHandler.js:3-18；`addLayer` 取 `data.qw`，校验 `onStart`，存 `Bv`/`onStart`/`onEnd`，调 `onStart(this)`（`:4-15`）；`removeLayer`=`splice`+`onEnd.call(layer,this)`（`:17`）；`modifyLayer` 仅改 time（`:16`） | 确认非缺口 |

**核对说明**：bundle `uS.Kw`（`:21626-21647`）从 `a.qw`（custom 对象）解构 `Bv`/`onEnd`/`onStart`，push `{id,time,timer:0,Bv,onEnd,onStart,num:0,Nw:!1}`，调 `d.onStart(this)`。src `CustomBuffHandler.addLayer`（`CustomBuffHandler.js:4-15`）逐字段对齐：`custom=data.qw`，校验 `onStart`，`layer.Bv=custom.Bv`/`layer.onStart=custom.onStart`/`layer.onEnd=custom.onEnd`，`layer.num=0;layer.Nw=false`，`layer.onStart(this)`。`removeLayer`（`:17`）`onEnd.call(layer,this)` 与 bundle `nv`（`:21658`）`onEnd.call(e,this)` 一致。onStart/onEnd 回调机制完整还原。

### 2.3 状态类（8-19，StateBuffHandler 及子类）

`StateBuffHandler`（`src/buffs/StateBuffHandler.js:6-63`，原 `uN`）基类实现 state channel `setState` 机制：`addLayer` merge/replace 策略（`:14-33`），`applyState(enabled,custom)` 遍历 `definition.channels` 调 `target.setState(channel,enabled,custom)`（`:56-62`），`removeLayer` 空层时 `applyState(false)`（`:38-45`）。对应 bundle `uN`（`bundle:21218-21346`）：`Qw`=`wv`+`iv`+`uh(target,wv,!0,qw)`（`:21237-21244`），`Kw` merge/replace（`:21274-21302`，`gv`=mergeLayers/`dv`=replaceDuration/`Lv`=replaceValue），`rv`=`uh(target,wv,!1)`（`:21331-21340`）。`uh`（`bundle:3064-3067`）=遍历 channel 数组调 `setState`。基类机械逻辑忠实。

| 值 | 类型名 | bundle 证据（行号+效果） | src handler 状态 | src 证据 | 处置 |
|---|---|---|---|---|---|
| 8 | stun | `uO`（`bundle:21348-21395`）继承 `uN`；`Qw`=super+表现（stun 图标/音效 `stun_1s`，`:21350-21360`）；`yv`=`晕眩`（`:21378-21380`）；channels=`[1,0]`（src BuffDefinitions.js:6） | 实现 | TimedStateBuffHandlers.js:4 `StunBuffHandler extends StateBuffHandler`，`label(){return '晕眩';}` | 确认非缺口（state channel 还原；表现层音效/图标属 P2） |
| 9 | fall | `uU`（`bundle:21778-21824`）继承 `uN`，ctor `gv=!0,dv=!0`（mergeLayers+replaceDuration，`:21784`）；`Qw`=super.Qw+表现（scaleY/rotation Tween）+`vi.Cv(num,[{id}],qw)` 撞击副作用（`:21802-21808`，**`bundle:21806`**）；`yv`=`跌倒`；channels=`[0]`（src BuffDefinitions.js:12） | 实现（state channel）+ DEFERRED（撞击副作用） | TimedStateBuffHandlers.js:8 `FallBuffHandler extends StateBuffHandler{ constructor(){super({mergeLayers:true,replaceDuration:true});} label(){return '跌倒';} }`；**未调 `vi.Cv`/applyDamage** | 见 fall 专项（§3.1）：标 `DEFERRED_FALL_IMPACT` |
| 10 | pierce | `uW`（`bundle:21855-21923`）继承 `uN`，ctor `gv=!0`（mergeLayers，`:21862`）；`Qw`=super+表现（武器图/穿刺 Tween，`:21864-21872`）；`yv`=`穿刺`（`:21908-21910`）；channels=`[0]`（src BuffDefinitions.js:13） | 实现 | TimedStateBuffHandlers.js:9 `PierceBuffHandler extends StateBuffHandler`，`label(){return '穿刺';}` | 确认非缺口（state channel 还原；表现属 P2） |
| 11 | electrocute | `u4`（`bundle:22223-22280`）继承 `uN`；`Qw`=super+表现（电击图/音效 `maChao_attack_lightning`，`:22231-22243`）；`yv`=`电击`（`:22262-22264`）；channels=`[1,0]`（src BuffDefinitions.js:7） | 实现 | TimedStateBuffHandlers.js:5 `ElectrocuteBuffHandler extends StateBuffHandler`，`label(){return '电击';}` | 确认非缺口（state channel 还原；表现属 P2） |
| 12 | knockback | `uQ`（`bundle:21457-21498`）继承 `uN`；`Qw`=super+`target.setState(5,!0,qw)`（`:21459-21463`），`Kw`=super+`target.setState(5,!0,qw)`（合并时再次施加，`:21465-21469`）；`yv`=`击退`（`:21481-21483`）；channels=`[5]`（src BuffDefinitions.js:8） | 实现 | KnockbackBuffHandler.js:5-8 `onMergedLayer(_layer,data){ this.target.setState(5,true,data.qw); }`，`label(){return '击退';}` | 确认非缺口（合并时 re-apply 向量忠实 `uQ.Kw`） |
| 13 | chaos | `uP`（`bundle:21397-21455`）继承 `uN`；`Qw`=super+表现（混乱图标，`:21399-21411`）；`vv`=rotation+=5（每帧旋转，`:21445-21452`）；`yv`=`混乱`（`:21429-21431`）；channels=`[1,0,2]`（src BuffDefinitions.js:9） | 实现 | TimedStateBuffHandlers.js:6 `ChaosBuffHandler extends StateBuffHandler`，`label(){return '混乱';}` | 确认非缺口（state channel 还原；旋转表现属 P2） |
| 14 | burnStatic | `uR`（`bundle:21500-21584`）继承 `uN`，ctor `gv=!1`（不合并，各层独立，`:21503-21509`）；`Qw`=super+表现（地面火焰图/Tween，`:21511-21526`）+`gv=!1`；`yv`=`火焰灼烧`；channels=`[4]`（src BuffDefinitions.js:10）；tick 机制经 `vv`/update 每 1000ms 汇总层伤害 `setState(4,!0,damage)` | 实现 | BurnStaticBuffHandler.js:5-26 ctor `mergeLayers:false`+`tickIntervalMs=1000`（`:6-10`）；`update` 每 1000ms 汇总 `layer.num` 和调 `target.setState(4,true,damage)`（`:14-22`）；`label(){return '火焰灼烧';}` | 确认非缺口（不合并+tick 伤害忠实 `uR`） |
| 15 | limit | `u0`（`bundle:22133-22168`）继承 `uN`，**空壳**：`Qw`=仅 `super.Qw(a)`（`:22135-22136`），`rv`=仅 `super.rv()`（`:22138-22139`）；`yv`=`""`（空标签，`:22151-22153`）；`pv`=`!1`（非负面，`:22159-22161`）；channels=`[6]`（src BuffDefinitions.js:17） | 桩（最小子类，正确） | TimedStateBuffHandlers.js:13 `LimitBuffHandler extends StateBuffHandler { label(){return '';} }` | 见 limit/charm 专项（§3.2）：bundle 本身空壳，src 正确 |
| 16 | lock | `uT`（`bundle:21683-21774`）继承 `uN`；`Qw`=super+表现（锁链图/Tween，`:21686-21725`）；`yv`=`封锁`（`:21759-21761`）；channels=`[1,2]`（src BuffDefinitions.js:11） | 实现 | TimedStateBuffHandlers.js:7 `LockBuffHandler extends StateBuffHandler`，`label(){return '封锁';}` | 确认非缺口（state channel 还原；表现属 P2） |
| 17 | knockdown | `uY`（`bundle:22003-22064`）继承 `uN`，ctor `gv=!0,dv=!0`（merge+replace，`:22009`）；`Qw`=super+表现（rotation/y Tween，`:22011-22026`）；`yv`=`跌倒`（`:22047-22049`）；channels=`[1]`（src BuffDefinitions.js:14） | 实现 | TimedStateBuffHandlers.js:10 `KnockdownBuffHandler{ constructor(){super({mergeLayers:true,replaceDuration:true});} label(){return '跌倒';} }` | 确认非缺口（merge+replace 忠实 `uY` ctor） |
| 18 | suppression | `uZ`（`bundle:22066-22131`）继承 `uN`；`Qw`=super+表现（等级下降图 `lvlDown`，`:22075-22097`）；`yv`=`压制`（`:22114-22116`）；channels=`[3]`（src BuffDefinitions.js:15） | 实现 | TimedStateBuffHandlers.js:11 `SuppressionBuffHandler extends StateBuffHandler`，`label(){return '压制';}` | 确认非缺口（state channel 还原；表现属 P2） |
| 19 | charm | `u1`（`bundle:22170-22199`）继承 `uN`，**空壳**：类体仅 `class extends uN {}`（`:22171`）；`yv`=`魅惑`（`:22181-22183`）；`pv` 经 defineProperty（`:22189+`）；channels=`[2,3]`（src BuffDefinitions.js:16） | 桩（最小子类，正确） | TimedStateBuffHandlers.js:12 `CharmBuffHandler extends StateBuffHandler { label(){return '魅惑';} }` | 见 limit/charm 专项（§3.2）：bundle 本身空壳，src 正确 |

## 3. 专项核对

### 3.1 fall(9) 撞击副作用专项 —— DEFERRED_FALL_IMPACT

**bundle 证据**：`uU`（Fall，`bundle:21778-21824`）的 `Qw` 方法（`bundle:21786-21808`）在 `super.Qw(a)`（设 state channel [0]，跌倒状态）之后，于 `bundle:21806` 调用：

```
vi["instance"]()["Cv"](a["num"], [{ ["id"]: this["target"]["id"] }], a["qw"])
```

`vi.Cv`（`bundle:33280-33291`）定义：`Cv(a, b, c)` 遍历目标 id 数组 `b`，对每个 `this.JS.get(id)`（敌人）调 `g.hit(a, c)` —— 即对跌倒目标施加 `a.num` 伤害（碰撞/撞击伤害），`c`=`a.qw`（attacker/general 上下文）。`vi.Cv` 在 bundle 另有两处调用（`bundle:43275`/`43655`/`44314`，2 倍/半倍/3 倍 `sS` 伤害），均为碰撞伤害结算路径，属逻辑层（经 `EnemyManager` 调 `hit`）。

**src 状态**：`FallBuffHandler`（`TimedStateBuffHandlers.js:8`）仅 `extends StateBuffHandler` + `{ mergeLayers:true, replaceDuration:true }` + `label(){return '跌倒';}`。其 state channel 设置经 `StateBuffHandler.applyState`（`StateBuffHandler.js:56-62`）遍历 `definition.channels=[0]`（`BuffDefinitions.js:12`）调 `target.setState(0, enabled, custom)`。**src 未调用 `vi.Cv` 的等价方法**（`EnemyManager.applyDamage`，`EnemyManager.js:235-240`，`enemy.hit(damage,attacker)`）。

**评估结论**：`vi.Cv` 是 `EnemyManager` 的碰撞伤害方法（逻辑层，非纯表现）——跌倒目标受撞击伤害。src `FallBuffHandler` 仅还原 state channel（跌倒状态本身生效），未还原撞击伤害副作用。但因 `vi.Cv` 的完整语义（撞击伤害计算/连锁对象选取/与其它 `Cv` 调用点的伤害倍率关系）需进一步取证，且 `FallBuffHandler` 的 state channel 不阻塞吹飞主效果，标注 **`DEFERRED_FALL_IMPACT`**：

- `FallBuffHandler` 保留 state channel（`channels=[0]`）不阻塞，吹飞状态生效。
- 撞击伤害副作用（`vi.Cv`→`applyDamage`）待取证后补，不影响 20 类覆盖结论（handler 已注册且 state channel 还原）。
- 与 design.md 决策 4 一致：`fall` 撞击副作用标 `DEFERRED_FALL_IMPACT`，不阻塞吹飞 state channel。

### 3.2 limit(15)/charm(19) bundle 空壳专项

**limit(15)**：bundle `u0`（`bundle:22133-22168`）为**空壳**——`Qw` 仅 `return super["Qw"](a)`（`bundle:22135-22136`），`rv` 仅 `super["rv"]()`（`:22138-22139`），无任何额外逻辑；`yv` 返回 `""`（空标签，`:22151-22153`），`pv` 返回 `!1`（非负面，`:22159-22161`）。即 bundle 原版 limit buff 仅设 state channel [6]（`BuffDefinitions.js:17`），无独立效果逻辑。src `LimitBuffHandler`（`TimedStateBuffHandlers.js:13`）`extends StateBuffHandler { label(){return '';} }` 为最小子类，`label()` 返回空字符串与 bundle `yv=""` 一致，state channel 经基类 `applyState` 还原。**结论：bundle 本身空壳（`u0`），src 最小子类正确，非缺口。**

**charm(19)**：bundle `u1`（`bundle:22170-22199`）为**空壳**——类体仅 `let a = class extends uN {};`（`bundle:22171`），无任何方法重写；`yv` 返回 `"魅惑"`（`:22181-22183`）。即 bundle 原版 charm buff 仅设 state channel [2,3]（`BuffDefinitions.js:16`），无独立效果逻辑。src `CharmBuffHandler`（`TimedStateBuffHandlers.js:12`）`extends StateBuffHandler { label(){return '魅魅';} }` 为最小子类，`label()` 返回 `'魅惑'` 与 bundle `yv="魅惑"` 一致，state channel 经基类还原。**结论：bundle 本身空壳（`u1`），src 最小子类正确，非缺口。**

### 3.3 武器 5 把属性加成专项 —— 已由提案 ④ 覆盖，非缺口

武器 5 把纯属性加成（非特殊效果武器）已在提案 ④（`special-weapons-projectiles`，已归档）完整修复，数值字段存在且经 `WeaponBase` 读取返回真实值：

| 武器 | key | src 定义（行号+字段） | 加成 |
|---|---|---|---|
| 铁剑 | `3:hN` | Weapon.js:9 `'3:hN': { name:'铁剑', attackType:'sword', addAttackPower:3 }` | +3 攻击力 |
| 大戟 | `1:hs` | Weapon.js:15 `'1:hs': { name:'大戟', attackType:'pike', attackRangeBonus:1 }` | +1 攻击距离 |
| 长剑 | `3:h2` | Weapon.js:10 `'3:h2': { name:'长剑', attackType:'sword', attackRangeBonus:0.5 }` | +0.5 攻击距离 |
| 长刀 | `2:hC` | Weapon.js:21 `'2:hC': { name:'长刀', attackType:'melee', attackRangeBonus:0.5 }` | +0.5 攻击距离 |
| 长枪 | `1:hp` | Weapon.js:21 `'1:hp': { name:'长枪', attackType:'pike', attackRangeBonus:0.5 }` | +0.5 攻击距离 |

**读取链路**：`WeaponBase.init`（`WeaponBase.js:10-19`）经 `getConfig()`（`:12`，`Weapon.js:33` 合并 `Weapon.config`+`this.definition`）读取 `addAttackPower`/`attackRangeBonus`/`attackSpeedBonus`（`WeaponBase.js:14-17`），`getCombatModifiers()`（`WeaponBase.js:48`）返回 `{ attackPower:this.attackPowerBonus, range:this.attackRangeBonus, attackSpeed:this.attackSpeedBonus }` 真实值。**结论：已由提案 ④ 覆盖，非缺口。**

## 4. 核对总结

| 维度 | 结论 |
|---|---|
| 20 类注册 | BuffHandlerFactory.js:13-21 注册全部 20 类，与 bundle `rZ`（26632-26653）逐项一致 |
| validate() 强制 | BuffHandlerFactory.js:24-34 启动期三重校验，与 bundle `oJ.init`（22450-22468）对应 |
| 0-6 数值类 | NumberBuffHandler 实现，stat delta `zw` 忠实 bundle `uM`（21072） |
| 7 custom | CustomBuffHandler 实现，onStart/onEnd 忠实 bundle `uS`（21586） |
| 8-19 状态类 | StateBuffHandler + 10 子类实现，state channel `setState` 忠实 bundle `uN`（21218）+ 子类 |
| limit(15)/charm(19) | bundle 空壳（u0/u1），src 最小子类正确，**非缺口** |
| fall(9) | state channel 还原；`vi.Cv` 撞击副作用（bundle:21806）未还原，标 **`DEFERRED_FALL_IMPACT`**，不阻塞 |
| 武器 5 把 | 已由提案 ④ 覆盖，**非缺口** |

**整体结论**：20 类 Buff handler 覆盖核对完成，全部 20 类在 `BuffHandlerFactory` 注册且经 `validate()` 启动期强制，机械逻辑忠实还原 bundle。`limit`/`charm` 为 bundle 本身空壳（src 最小子类正确）。`fall` 撞击副作用标 `DEFERRED_FALL_IMPACT`（待取证后补，不阻塞 state channel）。武器 5 把属性加成已由提案 ④ 覆盖。**20 类 Buff handler 覆盖非缺口，本核对表为查漏确认产出，不补 handler 实现（fall 撞击副作用 DEFERRED 除外）。**
