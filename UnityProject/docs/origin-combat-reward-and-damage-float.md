# 原工程“打怪加钱”与“命中飘伤害”实现调查

## 1. 调查范围

本次对照了以下内容：

- 重构后的可读代码：`D:\UnityProject\MyTEngine\TEngine\Origin\reconstructed-project\src`
- 原始运行时代码：`D:\UnityProject\MyTEngine\TEngine\Origin\reconstructed-project\origin_project\js\bundle.js`
- 启动文件：`origin_project/js/index.js`。该文件只负责 Laya 初始化和打开启动场景，战斗逻辑主要在 `bundle.js`。
- 原工程资源：`origin_project/prefab/damageNum.lh`、`origin_project/prefab/goldUp.lh`

原始 `bundle.js` 使用混淆符号。本文涉及的 `ro`、`pe`、`Lw`、`Tw` 等行号，引用的是同目录 `work/bundle.strings-decoded.js` 这份用于字符串/符号还原的分析副本；最终来源仍是 `origin_project/js/bundle.js`。用户描述中的 `origin\\_project\\js` 在磁盘上的实际目录名是 `origin_project/js`。

## 2. 结论先行

### 打怪加钱

普通敌人死亡奖励 1 金币，特殊敌人死亡奖励 10 金币。奖励不是在武器命中处直接增加，而是在敌人进入死亡状态后统一结算：

```text
命中造成生命值归零
  -> EnemyBase.changeState(4)
  -> 死亡状态入口调用 Lw()
  -> 普通敌人 j = 1，特殊敌人 j = 10
  -> 玩家侧：播放金币飘字 fg(x, y, j)，再增加 au.gold
  -> 对手侧：只增加 au.Ji，不播放玩家可见的金币飘字
```

### 命中飘伤害

伤害数字由敌人 `hit` 流程中的 `Tw(damage)` 创建。它具备以下特征：

- 受设置项 `showDamageNum` 控制，默认开启。
- 从 `damageNum` 对象池取 `FontClip`，显示整数伤害。
- 同一个敌人在 300ms 窗口内再次受击时，复用同一个数字并累加伤害，不再新建飘字。
- 新数字从敌人中心开始，沿二次贝塞尔曲线做 500ms 的弧线运动，然后回收到对象池。
- 单次伤害的缩放公式为 `1 + 0.05 * min(floor(damage / 10), 15)`，最大缩放为 1.75。

## 3. 关键符号对应关系

| 原工程符号 | 语义 | `src` 对应 |
|---|---|---|
| `ro` | 敌人基类 | `src/entities/EnemyBase.js` |
| `pe` | 普通敌人实现 | `src/entities/NormalEnemyBase.js` 或对应敌人实现 |
| `Zi` | 当前生命值 | `EnemyBase.health` |
| `Zm` | 最大生命值 | `EnemyBase.maxHealth` |
| `nm` | 是否玩家侧路线 | `EnemyBase.isPlayerLane` |
| `om` | 是否特殊敌人 | `EnemyBase.isSpecial` |
| `Cm` | 死亡已开始的一次性标记 | `EnemyBase.deathStarted` |
| `Lw` | 开始死亡处理 | `EnemyBase._beginDeath()` / `beginDeath()` |
| `Tw` | 显示伤害数字 | `EnemyBase.effects.showDamageNumber()` 的表现端 |
| `fg` | 显示金币飘字 | 当前 `src` 尚未实现为真实 Laya 表现服务 |
| `sw.au.gold` | 玩家战斗金币 | `BattleState.gold` |
| `sw.au.Ji` | 对手战斗金币 | `BattleState.opponentGold` |
| `qs` | 全局表现/特效管理器 | 当前 `src` 由效果服务接口承接 |

## 4. 原工程如何实现打怪加钱

### 4.1 触发点：受击归零后进入死亡状态

原始敌人 `hit`（可读副本 `work/bundle.strings-decoded.js:20546-20574`，原始混淆代码在 `origin_project/js/bundle.js` 对应的受击方法附近）先扣血并把生命值钳制到 0，然后执行：

```js
this["Zi"] -= a;
this["Zi"] < 0 && (this["Zi"] = 0);
this["Tw"](a);                  // 伤害飘字
this["Zi"] <= 0 && this["changeState"](4);
```

状态 4 的入口会调用 `Lw`（可读副本 `:20635-20660`）。普通敌人 `pe.Lw` 先调用基类 `super.Lw()`，再负责死亡特效、100ms 淡出和回收（`work/round-04-extracts/NormalEnemy-pe-31262-31482.js`）。因此金币结算位于死亡处理，而不是某个武器或投射物类中。

### 4.2 奖励数量和左右路线分支

基类死亡方法 `Lw` 位于 `work/bundle.strings-decoded.js:20100-20115`。核心逻辑还原如下：

```js
this["Cm"] = true;
this["enemy"]["visible"] = false;
this["enemy"]["event"]("onDead");

let reward = 1;
this["om"] && (reward = 10);

if (this["nm"]) {
  // 以敌人中心点作为金币飘字起点
  this["Vy"]["x"] = this["enemy"]["x"] + this["enemy"]["width"] / 2;
  this["Vy"]["y"] = this["enemy"]["y"];
  this["enemy"]["parent"]["localToGlobal"](this["Vy"]);
  qs["instance"]()["fg"](this["Vy"]["x"], this["Vy"]["y"], reward);
  this["sw"]["au"]["gold"] += reward;
} else {
  this["sw"]["au"]["Ji"] += reward;
}
```

由此可以确认：

1. 普通敌人奖励 `1`，特殊敌人奖励 `10`。
2. `nm` 为真时写入玩家金币 `gold`，并显示金币飘字。
3. `nm` 为假时写入对手金币 `Ji`，不走玩家金币飘字。
4. `Cm` 负责死亡只处理一次；当前重构代码的 `_beginDeath()` 也有同样的防重入保护。
5. `Lw` 后续的 `Fw()` 是死亡后的其他掉落/碎片流程，不改变上述金币结算规则。

### 4.3 金币飘字 `fg`

金币表现函数位于 `work/bundle.strings-decoded.js:15305-15330`，原始代码在 `origin_project/js/bundle.js` 同名方法附近。

```js
const prefabKey = amount === 1 ? "goldUpImg" : "goldUp";
const item = pool.getItem(prefabKey, this);
effectBus.event("Ut", item, goldEffectZ);
item.posAtGlobalPoint(x, y);

if (amount !== 1) {
  item.getChildByName("txt").text = "+" + amount;
}

Tween(item)
  .to("y", startY - 30, 400)
  .chain()
  .to("y", startY - 20, 300)
  .to("alpha", 0, 300)
  .then(() => pool.recover(prefabKey, item));
```

具体表现：

| 项目 | 原工程行为 |
|---|---|
| 数量为 1 | 使用 `goldUpImg`，即金币上升图片 |
| 数量大于 1 | 使用 `goldUp` prefab，并把子文本改为 `+10` 等 |
| 起点 | 敌人中心点，经过坐标系转换后加入场景 `effectBox` |
| 第一段 | 向上 30 像素，400ms |
| 第二段 | 移动到起点上方 20 像素，同时透明度降为 0，300ms |
| 结束 | 重置、移除节点并回收到对象池 |

`goldUp.lh` 的结构是金币图片加一个文本节点，默认文本为 `+1`；文件内容见 `origin_project/prefab/goldUp.lh`。`goldUpImg` 的创建函数位于 `work/bundle.strings-decoded.js:13646-13652`，资源为 `resources/img/battleUI/goldUpImg.png`。

### 4.4 金币数据与战斗 UI 的连接

原工程 `gold` 的 setter（`work/bundle.strings-decoded.js:3232-3239`）会派发 `Dt` 事件：

```js
set gold(value) {
  this._gold = value;
  eventBus.event("Dt");
}
```

战斗场景的 `s$`（`work/bundle.strings-decoded.js:58084-58114`）监听该事件，读取 `sw.au.gold` 并刷新 `goldNum.text`。金币飘字 `fg` 派发的 `Ut` 事件则由场景的 `m$`（`:57738-57745`）把对象加入 `effectBox`。因此原工程把“数值结算”和“金币表现”分成两条通道：

```text
死亡 -> gold += reward -> Dt -> 刷新顶部金币数
死亡 -> fg(...)      -> Ut -> 加入 effectBox -> 播放金币飘字
```

## 5. 原工程如何实现命中飘伤害

### 5.1 受击主流程

原始 `hit` 的可读代码位于 `work/bundle.strings-decoded.js:20546-20574`：

1. 如果当前生命值已经不大于 0，直接返回。
2. 受击音效有 50ms 节流。
3. `Zi -= damage`，小于 0 时钳制为 0。
4. 更新命中事件和生命条；延迟生命条使用 500ms 线性 Tween。
5. 调用 `Tw(damage)` 创建或更新伤害数字。
6. 生命值归零时切换到死亡状态。

注意：同一段代码后面的 `ht` 事件用于记录攻击者/贡献者并给武将经验，当前 `src/battle/BattleManager.js:181-188` 也将 `ENEMY_KILLED_BY` 用于 `awardGeneralExperience`。它不是金币结算事件，金币仍由死亡奖励服务处理。

### 5.2 `Tw` 的创建、合并和回收

`Tw` 位于 `work/bundle.strings-decoded.js:20393-20431`，流程如下：

```text
showDamageNum == false
  -> 直接返回

当前时间 - 上一次新建飘字时间 < 300ms
  -> 复用当前 Uw
  -> Rw = floor(Rw + damage)
  -> 更新文本和缩放

否则
  -> 从 damageNum 对象池取一个 FontClip
  -> 文本 = damage.toFixed(0)
  -> 定位到敌人中心
  -> 交给 qs.ud 做二次曲线动画
  -> 500ms 后 removeSelf，并 recover("damageNum", item)
```

合并是按敌人实例保存的：`Uw` 是当前敌人的飘字对象，`Rw` 是当前累计值，`Rm` 是上一次新建数字的时间。300ms 内的多次命中只更新同一个数字；超过窗口后，新建下一条数字。

### 5.3 数字大小和运动轨迹

缩放公式为：

```js
const bucket = Math.min(Math.floor(damage / 10), 15);
const scale = 1 + 0.05 * bucket;
```

因此伤害 0～9 时为 1 倍，伤害每增加 10 进入下一档，伤害达到 150 后封顶 1.75 倍。合并伤害使用累计值 `Rw` 重新计算缩放。

新数字先放在敌人中心，然后构造三个点：

```text
B = 当前中心点
A = (B.x + random(-50, 50), B.y - random(100, 150))
E = (A.x, B.y)
```

`qs.ud(B, A, E, damageNum, 500, callback)`（`work/bundle.strings-decoded.js:17610-17698`）由 `np.Us` 每帧做二次贝塞尔插值。因此它不是单纯“持续向上直线飘”：数字从中心出发，经过上方控制点，最后落在随机的横向位置但回到原始高度，整体看起来是一个上拱的弧线。结束回调将节点从场景移除并回收。

### 5.4 `damageNum` prefab

`origin_project/prefab/damageNum.lh` 是一个 `FontClip`：

- 尺寸 `80 x 30`
- 锚点 `(0.5, 0.5)`
- 使用 `resources/img/gameObject/bitmapFont/number1.png`
- 字符表为 `0123456789`
- 字符间距 `interval = 50`

对象池创建函数位于 `work/bundle.strings-decoded.js:13487-13491`。当前资源索引也保留在 `src/resources/PrefabCatalog.js:2269-2279`，图片资源索引位于 `src/resources/ImageCatalog.js:67`。

## 6. `src` 重构代码的覆盖情况

### 已覆盖的核心规则

- `src/entities/EnemyBase.js:453-478`：扣血、钳制生命值、调用 `effects.showDamageNumber`，生命归零后切换死亡状态，并派发 `ENEMY_KILLED_BY`。
- `src/entities/EnemyBase.js:483-496`：死亡防重入、按 `isSpecial` 选择 1/10 奖励，并调用 `rewardService.onEnemyKilled`。
- `src/battle/dev/DevelopmentCombatServices.js:200-206`：奖励服务通过 `BattleEconomy.award` 增加玩家侧或对手侧金币，并维护击杀统计。
- `src/battle/BattleEconomy.js:43-47`：`award()` 通过 `setBalance()` 写入金币，并累计 `killGold`。
- `src/battle/BattleState.js:64-68`：玩家 `gold` setter 派发 `GameEvents.GOLD_CHANGED`；`src/core/EventBus.js:73` 将其映射为原工程的 `Dt`。
- `src/resources/PrefabCatalog.js:2269-2279`：`damageNum`、`goldUp` prefab 资源关系已保留。

### 当前仍是表现接口/桩的部分

当前 `src` 已经把表现职责抽象为接口，但还没有完全复刻原工程的 Laya 表现：

- `DevelopmentEnemyEffects.showDamageNumber()`（`src/battle/dev/DevelopmentCombatServices.js:195`）目前只记录 `['damageNumber', enemy.id, damage]`，没有创建 `damageNum`、坐标转换、合并窗口或二次贝塞尔 Tween。
- 当前奖励服务会增加金币，但没有调用原工程的 `qs.fg()` 等价物，因此“金币数值正确”与“金币飘字显示”还不是同一完整链路。
- `PrefabCatalog` 已有资源入口，但还需要在实际 Laya 场景中注册对象池，并把效果对象加入等价于原工程 `effectBox` 的表现层。

## 7. Unity 移植建议

建议保持“规则”和“表现”分离，但保留原工程的时序：

1. `Enemy.TakeDamage()` 只负责扣血、归零和触发一次 `EnemyKilled`。
2. `EnemyRewardService` 在 `EnemyKilled` 中按普通/特殊敌人结算 1/10 金币；玩家侧结算后触发金币变更事件。
3. `DamageNumberPresenter` 按敌人保存当前飘字对象和累计值，实现 300ms 合并窗口。
4. `GoldFloatPresenter` 只对玩家侧显示；普通奖励 1 使用单图，特殊奖励 10 使用带文本的 `goldUp`，文本为 `+10`。
5. 两类表现都使用对象池，回收前重置透明度、缩放、文本和父节点。
6. 重点验收以下边界：
   - 普通敌人玩家侧死亡：金币 `+1`，出现金币表现。
   - 特殊敌人玩家侧死亡：金币 `+10`，文本为 `+10`。
   - 对手侧敌人死亡：只增加 `opponentGold`，不出现玩家金币飘字。
   - 同一敌人 300ms 内多次受击：数字累加；超过 300ms：创建新数字。
   - 伤害达到 150 及以上：伤害数字缩放不超过 1.75。
   - `showDamageNum` 关闭：不创建伤害数字，但不影响扣血和死亡奖励。

## 8. 结论边界

本文确认的是“普通/特殊敌人死亡导致的打怪加钱”和“敌人命中伤害飘字”两条主链。原工程中还存在宝箱、道具、招募/技能等其他金币写入点；这些路径不能与敌人死亡奖励混为一谈。对战斗移植而言，最重要的公共契约是：

```text
敌人死亡只结算一次
金币数值走 BattleState.gold / opponentGold
玩家侧金币表现走 effectBox
伤害表现走 damageNum 对象池
```
