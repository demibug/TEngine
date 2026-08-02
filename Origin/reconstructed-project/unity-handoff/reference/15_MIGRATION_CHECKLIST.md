# Unity 迁移顺序与验收清单

## Phase 1：规则核心

- [ ] 导入 config JSON。
- [ ] 实现 EventBus。
- [ ] 实现 80ms CombatTickDriver。
- [ ] 移植 BattleState、Economy、Deck、Input Commands。
- [ ] 移植 Unit/Enemy Registry 和 MapData。
- [ ] 跑通无画面的单局 Smoke。

## Phase 2：战斗对象

- [ ] 刀/弓/枪/骑。
- [ ] 7 种普通敌人。
- [ ] 波次与 Boss 波。
- [ ] 武器、投射物、伤害。
- [ ] Buff、Skill、12 Boss。

## Phase 3：表现层

- [ ] Grid 和放置预览。
- [ ] Unit/Enemy/Boss Prefab。
- [ ] Spine-Unity Boss 动画。
- [ ] Projectile/Trail。
- [ ] Skill VFX、天气和遮罩。
- [ ] HUD、Deck、GameOver。

## Phase 4：验收

- [ ] Main → Battle → GameOver → Main。
- [ ] 玩家购买、放置、移动、合成。
- [ ] AI 使用同一命令链。
- [ ] 普通波和 Boss 波。
- [ ] 自动 BATTLE_FINISHED。
- [ ] 清理后 Manager 全部为空。
- [ ] 确定性随机种子下结果一致。
- [ ] Pause/Resume 不补算暂停时间。
- [ ] 对象池复用无旧回调。
