using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Combat
{
    // ============================================================================
    // 任务 5.9：攻击时序与 Settling 边界场景测试
    // ----------------------------------------------------------------------------
    // 验证要求（task 5.9）：
    //   6. 两段攻击跨步：骑兵双段 150ms 命中跨子步累计，两段各自命中不互相干扰。
    //   8. 50ms 接触伤害：近战效果即时命中语义（ MeleeAttackEffect hitAtMs=0 即时命中）。
    //   9. 500ms 刀兵命中：KnifeAttackEffect 在 500ms 时命中，跨子步累计正确。
    //  10. 攻击释放回调且不再造成伤害：AttackEffectManager.Clear/Settling 取消残余效果
    //      且不造成伤害；Cancel 后效果不再推进/命中。
    //
    // 来源证据：
    //   - design.md 决策 0.4："首次 TryFreeze 成功后只完成当前同步提交并中止剩余 phase/子步，
    //     Settling 取消全部残余规则伤害...取消弹道、接触伤害、刀兵命中、攻击释放和动画回调。"
    //   - design.md 目录表：MeleeAttackEffect "即时近战"（hitAtMs=0/durationMs=0）；
    //     KnifeAttackEffect "500ms 时序"；CavalrySweepEffect "150ms 两段"。
    //   - spec battle-simulation "Melee effect is created by attack scheduling"：
    //     单位攻击调度在当前子步创建的近战效果，在当前子步的攻击效果阶段立即累计一次 stepMs。
    //   - spec battle-simulation "Effect manager participates in a substep"：
    //     攻击效果只累计一次该子步时长，不因嵌套 Manager 调用而双倍推进。
    //   - spec battle-runtime-lifecycle "Settling has no gameplay damage authority"：
    //     结果冻结时仍存在 50ms 接触伤害、500ms 刀兵命中、攻击释放回调，
    //     系统取消并回收这些残余任务，且它们在 Settling 中不得修改生命。
    //
    // 复用策略（reuse-first）：
    //   复用 KnifeAttackEffectTests / AttackResolverTests 中的 FakeOwner、FakeEnemy、
    //   MakeEnemy 模式。因测试文件隔离，本文件内定义局部桩（签名与现有桩一致）。
    //
    // 测试策略：
    //   使用真实 AttackResolver、真实 EnemyManager、真实 AttackEffectManager、真实
    //   MeleeAttackEffect / KnifeAttackEffect / CavalrySweepEffect，构造确定性场景
    //   验证时序与 Settling 边界。不接触 Scene、FUI 或资源加载。
    // ============================================================================

    /// <summary>
    /// 攻击时序与 Settling 边界场景测试（task 5.9）。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖：</para>
    /// <list type="bullet">
    /// <item>50ms 接触伤害：MeleeAttackEffect 即时命中（hitAtMs=0），首次 Update 即命中。</item>
    /// <item>500ms 刀兵命中：KnifeAttackEffect 跨子步累计到 500ms 命中。</item>
    /// <item>两段攻击跨步：CavalrySweepEffect 150ms 命中跨子步累计，两段独立 hitSet。</item>
    /// <item>攻击释放回调不造成伤害：Settling Clear 取消残余效果且不命中。</item>
    /// <item>Cancel 后不再推进：Cancel 置 active=false，后续 Update 不命中。</item>
    /// <item>AttackEffectManager 冻结中止：IsFrozen 后 Update 不推进剩余效果。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class AttackTimingAndSettlingTests
    {
        // ====================================================================
        // FakeOwner —— 最小 IAttackEffectOwner 测试桩（与 KnifeAttackEffectTests 一致）
        // ====================================================================

        /// <summary>
        /// 最小 IAttackEffectOwner 测试替身，提供运行时 ID 与阵营。
        /// </summary>
        private sealed class FakeOwner : IAttackEffectOwner
        {
            internal int RuntimeIdValue;
            internal bool SideValue;
            internal float CenterXValue = 40f;
            internal float CenterYValue = 40f;

            public int RuntimeId => RuntimeIdValue;
            public bool Side => SideValue;
            public float CenterX => CenterXValue;
            public float CenterY => CenterYValue;
        }

        // ====================================================================
        // FakeEnemy —— 最小 IEnemyEntity 测试桩（与 AttackResolverTests 一致）
        // ====================================================================

        /// <summary>
        /// 最小 IEnemyEntity 测试替身，带可观察的可变状态。
        /// </summary>
        private sealed class FakeEnemy : IEnemyEntity
        {
            public int Id { get; set; }
            public bool IsPlayerLane { get; set; }
            public int CurrentState { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; } = 40f;
            public float Height { get; set; } = 40f;
            public float RemainingPathDistance { get; set; } = float.PositiveInfinity;
            public int CurrentPathIndex { get; set; }
            public int Health { get; set; }
            public bool Targetable { get; set; } = true;

            /// <summary>Hit 调用累计伤害。</summary>
            public int TotalHitDamage;

            /// <summary>Hit 调用次数。</summary>
            public int HitCount;

            /// <summary>上次 Hit 的攻击者 ID。</summary>
            public int LastAttackerId = -1;

            public void Update(long deltaMs) { }

            public bool Hit(int damage, int attackerId)
            {
                if (Health <= 0) return false;
                if (damage <= 0) return false;
                Health = Math.Max(0, Health - damage);
                TotalHitDamage += damage;
                HitCount++;
                LastAttackerId = attackerId;
                if (Health <= 0) CurrentState = 4; // DEAD
                return true;
            }

            public bool GameOver()
            {
                CurrentState = 4;
                Targetable = false;
                return true;
            }

            public bool IsTargetableBy(bool playerSide)
            {
                return CurrentState != 0 && CurrentState != 4
                    && Targetable && IsPlayerLane == playerSide;
            }
        }

        // ====================================================================
        // 测试常量与辅助
        // ====================================================================

        /// <summary>刀兵命中延迟基线（对应 JS KNIFE_HIT_DELAY_BASE_MS = 500）。</summary>
        private const long KnifeHitDelayMs = 500;

        /// <summary>骑兵横扫命中延迟（对应 JS CAVALRY_SWEEP_DELAY_MS = 150）。</summary>
        private const long CavalrySweepDelayMs = 150;

        /// <summary>默认空间单元边长。</summary>
        private const int GridSize = 80;

        /// <summary>敌人格子宽/高。</summary>
        private const float CellWidth = 80f;
        private const float CellHeight = 80f;

        /// <summary>
        /// 构造一个 FakeEnemy，位于指定位置。
        /// </summary>
        private static FakeEnemy MakeEnemy(
            int id, bool isPlayerLane, float x, float y,
            int health = 100, int state = 1)
        {
            return new FakeEnemy
            {
                Id = id,
                IsPlayerLane = isPlayerLane,
                X = x,
                Y = y,
                Health = health,
                CurrentState = state,
            };
        }

        /// <summary>
        /// 构造已登记一个敌人的 EnemyManager。
        /// </summary>
        private static EnemyManager MakeManagerWithEnemy(out FakeEnemy enemy)
        {
            var mgr = new EnemyManager(GridSize);
            enemy = MakeEnemy(1, isPlayerLane: true, x: 40, y: 40, health: 100);
            mgr.Register(enemy);
            return mgr;
        }

        /// <summary>
        /// 构造并启动一个 KnifeAttackEffect。
        /// </summary>
        private static KnifeAttackEffect CreateAndLaunchKnife(
            IAttackEffectOwner owner,
            AttackResolver resolver,
            EnemyManager enemyManager,
            int targetId = 1,
            int damage = 30)
        {
            var effect = new KnifeAttackEffect();
            effect.Launch(owner, resolver, enemyManager, targetId, damage);
            return effect;
        }

        // ====================================================================
        // 场景 8：50ms 接触伤害（MeleeAttackEffect 即时命中）
        // ====================================================================

        [Test]
        [Description("MeleeAttackEffect 即时命中（hitAtMs=0）：首次 Update 即命中并完成，不延迟到下一子步。")]
        public void MeleeAttackEffect_ImmediateHit_OnFirstUpdate()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            var effect = new MeleeAttackEffect();
            effect.Launch(owner, resolver, mgr, damage: 30,
                cellWidth: CellWidth, cellHeight: CellHeight,
                radius: 50f);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            // 首次 Update（stepMs=50 模拟接触帧）——即时命中。
            manager.Update(50);

            Assert.AreEqual(1, enemy.HitCount, "首次 Update 应立即命中");
            Assert.AreEqual(70, enemy.Health, "血量应扣减 30");
            Assert.AreEqual(0, manager.ActiveCount, "即时效果应完成后移除");
        }

        [Test]
        [Description("MeleeAttackEffect 跨子步不重复命中（hitSet 去重）：同一效果内同一敌人只命中一次。")]
        public void MeleeAttackEffect_HitSetDedup_NoRepeatHitAcrossSubsteps()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            // 使用 durationMs > 0 使效果跨多子步，但 hitAtMs=0 立即命中。
            var effect = new MeleeAttackEffect();
            effect.Launch(owner, resolver, mgr, damage: 30,
                cellWidth: CellWidth, cellHeight: CellHeight,
                radius: 50f, durationMs: 100, hitAtMs: 0);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            manager.Update(50);
            Assert.AreEqual(1, enemy.HitCount, "首次 Update 命中");

            // 效果仍活动（durationMs=100 未满），但 hitSet 去重不再命中。
            Assert.AreEqual(1, manager.ActiveCount, "效果仍应活动（100ms 未满）");

            manager.Update(50);
            Assert.AreEqual(1, enemy.HitCount, "第二次 Update 不应重复命中（hitSet 去重）");
            Assert.AreEqual(30, enemy.TotalHitDamage, "总伤害应只有 30");

            // durationMs=100 满，效果完成移除。
            Assert.AreEqual(0, manager.ActiveCount, "100ms 满后应移除");
        }

        // ====================================================================
        // 场景 9：500ms 刀兵命中（跨子步累计）
        // ====================================================================

        [Test]
        [Description("KnifeAttackEffect 跨子步累计到 500ms 命中：200+200+100=500ms 时命中且只命中一次。")]
        public void KnifeAttackEffect_CrossSubstep_500ms_HitsOnce()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunchKnife(owner, resolver, mgr, damage: 30);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            // 200ms：未到 500ms，不命中。
            manager.Update(200);
            Assert.AreEqual(0, enemy.HitCount, "200ms 不应命中");
            Assert.AreEqual(1, manager.ActiveCount, "效果仍应活动");

            // 400ms：仍未到 500ms，不命中。
            manager.Update(200);
            Assert.AreEqual(0, enemy.HitCount, "400ms 不应命中");
            Assert.AreEqual(1, manager.ActiveCount, "效果仍应活动");

            // 500ms：命中。
            manager.Update(100);
            Assert.AreEqual(1, enemy.HitCount, "500ms 应命中一次");
            Assert.AreEqual(70, enemy.Health, "血量应扣减到 70");
            Assert.AreEqual(0, manager.ActiveCount, "命中后应移除");

            // 再推进不应重复命中。
            manager.Update(100);
            Assert.AreEqual(1, enemy.HitCount, "额外子步不应重复命中");
            Assert.AreEqual(30, enemy.TotalHitDamage, "总伤害不应增加");
        }

        [Test]
        [Description("KnifeAttackEffect 50ms 子步累计到 500ms 命中：10 个 50ms 子步后命中。")]
        public void KnifeAttackEffect_50msSubsteps_AccumulateTo500ms()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunchKnife(owner, resolver, mgr, damage: 30);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            // 9 个 50ms 子步 = 450ms，不命中。
            for (int i = 0; i < 9; i++)
            {
                manager.Update(50);
                Assert.AreEqual(0, enemy.HitCount, $"第 {i + 1} 步（{50 * (i + 1)}ms）不应命中");
            }

            // 第 10 个 50ms 子步 = 500ms，命中。
            manager.Update(50);
            Assert.AreEqual(1, enemy.HitCount, "500ms 应命中");
            Assert.AreEqual(70, enemy.Health, "血量应扣减 30");
        }

        // ====================================================================
        // 场景 6：两段攻击跨步（CavalrySweepEffect 150ms 双段）
        // ====================================================================

        [Test]
        [Description("CavalrySweepEffect 150ms 命中跨子步累计：100+50=150ms 时命中，两段独立 hitSet。")]
        public void CavalrySweepEffect_CrossSubstep_150ms_TwoStagesIndependent()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            // 骑兵双段：实例1 multiplier=0.5 radius=40，实例2 multiplier=0.5 radius=80。
            var effect1 = new CavalrySweepEffect();
            effect1.Launch(owner, resolver, mgr, damage: 60,
                cellWidth: CellWidth, cellHeight: CellHeight,
                multiplier: 0.5f, radius: 40f);

            var effect2 = new CavalrySweepEffect();
            effect2.Launch(owner, resolver, mgr, damage: 60,
                cellWidth: CellWidth, cellHeight: CellHeight,
                multiplier: 0.5f, radius: 80f);

            var manager = new AttackEffectManager();
            manager.Add(effect1);
            manager.Add(effect2);

            // 100ms：未到 150ms，不命中。
            manager.Update(100);
            Assert.AreEqual(0, enemy.HitCount, "100ms 不应命中");
            Assert.AreEqual(2, manager.ActiveCount, "两个效果仍应活动");

            // 50ms：累计 150ms，两段各自命中同一敌人（独立 hitSet）。
            manager.Update(50);
            Assert.AreEqual(2, enemy.HitCount, "150ms 时两段应各命中一次");
            // 总伤害 = 60*0.5 + 60*0.5 = 60。
            Assert.AreEqual(100 - 60, enemy.Health, "总伤害应为 60（两段各 30）");
            Assert.AreEqual(0, manager.ActiveCount, "两段命中后应移除");
        }

        [Test]
        [Description("CavalrySweepEffect 跨子步不重复命中：同效果实例内 hitSet 去重。")]
        public void CavalrySweepEffect_HitSetDedup_NoRepeatAcrossSubsteps()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            var effect = new CavalrySweepEffect();
            effect.Launch(owner, resolver, mgr, damage: 60,
                cellWidth: CellWidth, cellHeight: CellHeight,
                multiplier: 1f, radius: 50f);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            // 150ms：命中。
            manager.Update(150);
            Assert.AreEqual(1, enemy.HitCount, "150ms 应命中一次");

            // 效果仍活动（durationMs=270 未满），但 hitSet 去重不再命中。
            Assert.AreEqual(1, manager.ActiveCount, "效果仍应活动（270ms 未满）");

            manager.Update(50);
            Assert.AreEqual(1, enemy.HitCount, "200ms 不应重复命中（hitSet 去重）");
            Assert.AreEqual(60, enemy.TotalHitDamage, "总伤害不应增加");

            // 推进到 270ms 满后移除。
            manager.Update(70);
            Assert.AreEqual(0, manager.ActiveCount, "270ms 满后应移除");
        }

        // ====================================================================
        // 场景 10：攻击释放回调且不再造成伤害（Settling 边界）
        // ====================================================================

        [Test]
        [Description("Settling Clear 取消残余 KnifeAttackEffect 且不造成伤害：未到 500ms 的效果被取消，不命中。")]
        public void Settling_ClearCancelsPendingKnifeEffect_NoDamage()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunchKnife(owner, resolver, mgr, damage: 30);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            // 推进 300ms（未到 500ms），不命中。
            manager.Update(300);
            Assert.AreEqual(0, enemy.HitCount, "300ms 不应命中");
            Assert.AreEqual(1, manager.ActiveCount, "效果仍应活动");

            // Settling 开始：Clear 取消全部残余效果。
            manager.Clear();

            Assert.AreEqual(0, manager.ActiveCount, "Clear 后应无活动效果");
            Assert.IsTrue(manager.IsCleared, "应标记 IsCleared");

            // Clear 不造成伤害——未到 500ms 的效果被取消，不命中。
            Assert.AreEqual(0, enemy.HitCount, "Settling Clear 不应造成命中");
            Assert.AreEqual(100, enemy.Health, "Settling Clear 不应改变血量");

            // Clear 后 Update 不再推进。
            manager.Update(300);
            Assert.AreEqual(0, enemy.HitCount, "Clear 后 Update 不应造成命中");
            Assert.AreEqual(100, enemy.Health, "Clear 后血量不应改变");
        }

        [Test]
        [Description("Settling Clear 取消残余 MeleeAttackEffect 且不造成伤害。")]
        public void Settling_ClearCancelsPendingMeleeEffect_NoDamage()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            // durationMs=200 使效果跨子步，但 hitAtMs=0 立即命中。
            var effect = new MeleeAttackEffect();
            effect.Launch(owner, resolver, mgr, damage: 30,
                cellWidth: CellWidth, cellHeight: CellHeight,
                radius: 50f, durationMs: 200, hitAtMs: 0);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            // 首次 Update 命中（即时）。
            manager.Update(50);
            Assert.AreEqual(1, enemy.HitCount, "首次 Update 应命中");
            Assert.AreEqual(1, manager.ActiveCount, "效果仍活动（200ms 未满）");

            // Settling：Clear 取消残余效果。
            manager.Clear();
            Assert.AreEqual(0, manager.ActiveCount, "Clear 后应无活动效果");

            // Clear 后不再推进/命中。
            manager.Update(150);
            Assert.AreEqual(1, enemy.HitCount, "Clear 后不应再命中");
            Assert.AreEqual(30, enemy.TotalHitDamage, "总伤害不应增加");
        }

        [Test]
        [Description("Settling Clear 取消残余 CavalrySweepEffect 且不造成伤害：未到 150ms 的效果被取消。")]
        public void Settling_ClearCancelsPendingCavalryEffect_NoDamage()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            var effect = new CavalrySweepEffect();
            effect.Launch(owner, resolver, mgr, damage: 60,
                cellWidth: CellWidth, cellHeight: CellHeight,
                multiplier: 1f, radius: 50f);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            // 推进 100ms（未到 150ms），不命中。
            manager.Update(100);
            Assert.AreEqual(0, enemy.HitCount, "100ms 不应命中");
            Assert.AreEqual(1, manager.ActiveCount, "效果仍应活动");

            // Settling：Clear 取消。
            manager.Clear();
            Assert.AreEqual(0, manager.ActiveCount, "Clear 后应无活动效果");

            // Clear 不造成伤害。
            Assert.AreEqual(0, enemy.HitCount, "Settling Clear 不应造成命中");
            Assert.AreEqual(100, enemy.Health, "血量不应改变");

            // Clear 后 Update 不再推进。
            manager.Update(50);
            Assert.AreEqual(0, enemy.HitCount, "Clear 后 Update 不应命中");
        }

        [Test]
        [Description("AttackEffectManager.IsFrozen 后 Update 不推进剩余效果（冻结中止）。")]
        public void AttackEffectManager_Frozen_StopsUpdateNoDamage()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunchKnife(owner, resolver, mgr, damage: 30);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            // 推进 200ms（未到 500ms）。
            manager.Update(200);
            Assert.AreEqual(0, enemy.HitCount, "200ms 不应命中");

            // 冻结（Settling 入口置位）。
            manager.IsFrozen = true;

            // 冻结后 Update 直接返回，不推进——即使累计到 500ms 也不命中。
            manager.Update(300);
            Assert.AreEqual(0, enemy.HitCount, "冻结后不应命中");
            Assert.AreEqual(100, enemy.Health, "冻结后血量不应改变");
            Assert.AreEqual(1, manager.ActiveCount, "冻结后效果不移除（Update 未执行）");
        }

        [Test]
        [Description("Cancel 后效果不再推进：Cancel 置 active=false，后续 Update 不命中。")]
        public void Cancel_PreventsSubsequentResolve_NoDamage()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunchKnife(owner, resolver, mgr, damage: 30);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            // 推进 300ms（未到 500ms）。
            manager.Update(300);
            Assert.AreEqual(0, enemy.HitCount, "300ms 不应命中");

            // Cancel 效果（模拟 Settling/owner 死亡取消）。
            effect.Cancel("settling-cancel");
            Assert.IsFalse(effect.Active, "Cancel 后应 Active=false");

            // 后续 Update 不命中——Cancel 后不推进/不 Resolve。
            manager.Update(200);
            Assert.AreEqual(0, enemy.HitCount, "Cancel 后不应命中");
            Assert.AreEqual(100, enemy.Health, "Cancel 后血量不应改变");
        }

        [Test]
        [Description("Settling Clear 幂等：重复调用不抛异常，活动数保持 0。")]
        public void Settling_ClearIdempotent_AttackEffectManager()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunchKnife(owner, resolver, mgr, damage: 30);

            var manager = new AttackEffectManager();
            manager.Add(effect);
            manager.Update(300);

            manager.Clear();
            Assert.AreEqual(0, manager.ActiveCount);

            // 重复 Clear 不抛异常。
            manager.Clear();
            manager.Clear();
            Assert.AreEqual(0, manager.ActiveCount, "重复 Clear 后仍为 0");
            Assert.IsTrue(manager.IsCleared);
        }
    }
}
