# 牌组、经济与 AI

## 战斗经济默认值

```json
{
  "initialGold": 20,
  "refreshCostStart": 10,
  "refreshCostIncrement": 2,
  "unitBaseCost": 1,
  "handSize": 5
}
```

来源：`unity-export/config/battle-economy.json`。

## DeckManager

- 双方各 5 张牌。
- 基础池：刀、弓、枪、骑。
- 卡牌含 `id/text/level/cost/source/locked`。
- 刷新时锁定牌不替换。
- 购买成功后立即补一张新牌。
- 卡牌成本当前为 `max(baseUnitCost, level)`。

## BattleEconomy

所有战斗金币修改必须经由：

- `spend`
- `payRefresh`
- `payRecruit`
- `award`

Unity 不应允许 UI 直接修改金币字段。

## AIController

当前恢复的是最小正式链路 AI：

```text
定时决策
→ 读取对手手牌
→ 选择合法地块
→ 发送 PurchaseAndPlace 命令
→ 使用与玩家相同的 Economy/Factory/Registry
```

它足以形成真实对局，但不等于原版全部高级策略。迁移时建议先复刻当前 AI，再逐步增强阵容、合成、武器和技能决策。
