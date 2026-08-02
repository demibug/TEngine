# 配置与 JSON Schema 指南

## 目录

```text
unity-export/config/
```

## 推荐 Unity 导入方式

### 第一阶段

直接读取 JSON，映射为 C# DTO。

### 第二阶段

编写 Editor Importer，将 JSON 转成 ScriptableObject。保持 JSON 为事实来源，避免手工改两份数据。

## 文件用途

| 文件 | 用途 |
|---|---|
| `units.json` | 四兵种、攻击参数、等级倍率 |
| `generals.json` | 12 武将名称与武器类型；当前部分恢复 |
| `enemies.json` | 7 普通敌人 |
| `bosses.json` | 12 Boss、技能、动画时间线 |
| `weapons.json` | 正式武器目录与状态 |
| `projectiles.json` | 投射物类型 |
| `buffs.json` | 20 Buff 类型 |
| `skills.json` | 普通与 Boss 技能 |
| `waves.json` | 数量、Boss 波、概率、策略 |
| `maps.json` | 网格与双方路径 |
| `battle-economy.json` | 初始金币、刷新和购买 |
| `events.json` | 事件语义和原始短键 |
| `battle-result-schema.json` | 结算 DTO |

## ID 稳定性

Unity 应使用 `key/type/index` 作为持久 ID，不使用类名或本地化名称作为唯一键。

## 精度

等级倍率 JSON 中存在 JS 浮点表示，例如 `2.0999999999999996`。Unity 可在导入时规范化为 `2.1`，但应记录转换。
