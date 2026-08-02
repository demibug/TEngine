# 单场游戏完整时序

## 已验证的确定性流程

```mermaid
sequenceDiagram
    participant Main
    participant Match
    participant Flow as BattleFlowCoordinator
    participant Battle
    participant Deck
    participant Input
    participant AI
    participant Wave
    participant Target as A-Dou Target
    participant GameOver

    Main->>Main: 检查并消耗 5 体力
    Main->>Match: 打开 MatchScene
    Match->>Flow: startBattle()
    Flow->>Deck: startGame()
    Flow->>Battle: startGame()
    Flow->>AI: startGame()
    Flow->>Battle: 打开 BattleScene
    Input->>Deck: 读取卡牌
    Input->>Input: 校验格子/金币
    Input->>Battle: UnitFactory 创建并注册单位
    AI->>Input: 使用相同命令路径部署
    Battle->>Wave: beginRound()
    Wave->>Battle: 生成普通敌人/Boss
    Battle->>Target: 伤害
    Target->>Flow: BATTLE_FINISHED(isWin)
    Flow->>Flow: 防重复 + 生成 BattleResult
    Flow->>Flow: 按顺序清理全部 Manager
    Flow->>GameOver: open(result)
    GameOver->>Main: 返回主界面
```

## 已验证 Smoke 数据

- 场景顺序：`MainScene → MatchScene → BattleScene → GameOverScene → MainScene`
- 玩家购买：弓，费用 1，装备 LongBow。
- 玩家完成一次刀兵合成，目标等级 2。
- AI 使用同一 Deck/Input/Factory 链部署刀兵。
- 普通波：10 个单位。
- Boss 波：双侧张梁。
- 自动结算：`BATTLE_FINISHED`，不是脚本手动调用 `gameOver()`。
- BattleResult：胜利、1 星、23 金币、29320ms、Round 2、6 次击杀。

详细原始报告：

```text
analysis/smoke/single-game-flow.json
```

## Unity 入口建议

1. MainScene 只处理体力和模式选择。
2. MatchScene 构造 `BattleStartRequest`。
3. BattleScene 创建 `CombatCompositionRoot`。
4. UI 发出命令，不直接修改 Registry、金币或单位。
5. 结算只由 `BATTLE_FINISHED` 触发。
