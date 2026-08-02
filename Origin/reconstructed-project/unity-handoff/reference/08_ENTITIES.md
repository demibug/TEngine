# 单位、敌人和 Boss

## 友军单位

正式基础兵种：

| Key | Range | Damage | Interval | 模式 |
|---|---:|---:|---:|---|
| 刀 | 1.5 格 | 3 | 0.8s | 单目标近战 |
| 弓 | 3.5 格 | 2 | 0.8s | 远程，按路径剩余距离选目标 |
| 枪 | 2.5 格 | 2 | 0.8s | 枪击攻击对象 |
| 骑 | 2 格 | 2 | 0.8s | 范围 Sweep |

最高等级 5；伤害/攻速等级倍率：

```text
[1, 1.5, 2.1, 2.73, 3.4125]
```

## UnitRegistry

职责：

- 通过 UnitFactory 创建。
- 分配 ID。
- 按容器/阵营注册。
- 位置与地块占用。
- 半径查询。
- 最低/最高等级查询。
- 移除、清理和对象池回收。

## 普通敌人

正式键：

```text
Mob0, Mob1, Mob2, Mob3, Zombie, Cavalry, Puppet
```

Mob0 的路径、接触伤害、死亡和回收最完整；其他类型的核心配置与生命周期已恢复，部分专属 VFX 延后。

## Boss

12 个 Boss 通过 `BossBase + BossDefinitions + SkillManager` 运行。每个定义包含：

- 正式 key/name
- Spine 资源路径
- idle/attack 动画
- 技能 key
- `effectAtMs`
- `completeAtMs`

Boss 仍注册在 EnemyManager 的空间索引中，同时由 BossManager 维护 Boss 集合。
