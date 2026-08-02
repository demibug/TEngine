# 源码追溯索引

## 事实来源

| 内容 | 主要来源 |
|---|---|
| 启动/场景流程 | `src/bootstrap/`, `src/scenes/`, `analysis/critical-path/` |
| 单场流程 | `src/battle/BattleFlowCoordinator.js`, `analysis/smoke/single-game-flow.json` |
| Tick | `src/core/GameLoop.js`, `src/core/FixedUpdateManager.js` |
| 牌组与经济 | `src/deck/`, `src/battle/BattleEconomy.js` |
| 输入命令 | `src/input/` |
| 单位 | `src/units/`, `unity-export/config/units.json` |
| 敌人 | `src/entities/`, `src/battle/EnemyManager.js` |
| Boss | `src/bosses/`, `unity-export/config/bosses.json` |
| 武器 | `src/weapons/`, `unity-export/config/weapons.json` |
| 投射物 | `src/projectiles/`, `unity-export/config/projectiles.json` |
| Buff | `src/buffs/`, `unity-export/config/buffs.json` |
| Skill | `src/skills/`, `unity-export/config/skills.json` |
| 波次 | `src/battle/WaveManager.js`, `unity-export/config/waves.json` |
| 地图 | `src/battle/MapData.js`, `unity-export/config/maps.json` |
| 结算 | `src/battle/BattleResult.js`, `unity-export/config/battle-result-schema.json` |

## 原始 Bundle 追溯

多数重建文件顶部含：

- 原始符号
- bundle 行范围
- 重建状态

进一步映射位于：

```text
analysis/mappings/
analysis/modules/
analysis/behavior/
```

Unity 迁移中遇到不确定逻辑时，应先检查这些文件，再回查 `work/bundle.strings-decoded.js`。
