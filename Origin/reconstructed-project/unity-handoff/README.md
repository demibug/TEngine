# Unity 核心战斗接入文档

本目录面向将当前 JavaScript 重建工程迁移到 Unity/C# 的开发人员。核心原则是：**规则层保持引擎无关，Unity 只承担输入、表现、资源、场景和音频适配。**

## 推荐阅读顺序

1. [实现状态与可信边界](reference/00_IMPLEMENTATION_STATUS.md)
2. [总体架构与模块边界](reference/01_ARCHITECTURE.md)
3. [单场游戏完整时序](reference/02_SINGLE_GAME_FLOW.md)
4. [固定 Tick 与更新顺序](reference/03_COMBAT_TICK.md)
5. [生命周期、启动与清理顺序](reference/04_LIFECYCLE_AND_CLEANUP.md)
6. [命令、事件与 BattleResult](reference/05_COMMANDS_EVENTS_RESULTS.md)
7. [地图、坐标与放置规则](reference/06_MAP_AND_PLACEMENT.md)
8. [牌组、经济与 AI](reference/07_DECK_ECONOMY_AI.md)
9. [单位、敌人、Boss](reference/08_ENTITIES.md)
10. [武器、投射物与伤害](reference/09_WEAPONS_PROJECTILES_DAMAGE.md)
11. [Buff、Skill 与状态效果](reference/10_BUFFS_SKILLS.md)
12. [对象池与运行时所有权](reference/11_POOLING_AND_OWNERSHIP.md)
13. [管理器 API 参考](reference/12_MANAGER_API_REFERENCE.md)
14. [配置与 JSON Schema 指南](reference/13_CONFIG_GUIDE.md)
15. [Unity/C# 落地蓝图](reference/14_UNITY_BLUEPRINT.md)
16. [迁移顺序与验收清单](reference/15_MIGRATION_CHECKLIST.md)
17. [已知缺口与风险](reference/16_KNOWN_GAPS.md)
18. [源码追溯索引](reference/17_SOURCE_TRACEABILITY.md)

## C# 参考骨架

`csharp-reference/` 中的文件是接口和结构示例，不是完整 Unity 工程：

- `CombatPorts.cs`
- `CombatTickDriver.cs`
- `BattleInputCommand.cs`
- `BattleResultDto.cs`
- `ConfigDtos.cs`
- `RuntimeCompositionExample.cs`

## 现有配置导出

所有无函数配置位于：

```text
unity-export/config/
```

Unity 可在第一阶段直接读取 JSON，稳定后再转为 ScriptableObject。
