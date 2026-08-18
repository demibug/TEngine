using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Combat
{
    // ============================================================================
    // 任务 5.5：KnifeAttackEffect 单元测试 —— 同一效果仅推进一次
    // ----------------------------------------------------------------------------
    // 验证要求（task 5.5）：
    //   1. KnifeAttackTimeline 行为已合入 KnifeAttackEffect：无单独 KnifeAttackTimeline 类，
    //      唯一计时源为 AttackEffectManager.Update（禁止 TEngine Timer / Laya Timer 双轨推进）。
    //   2. 同一效果仅推进一次：单个 KnifeAttackEffect 实例在到达 500ms 延迟后只 Resolve 一次，
    //      重复 Update 调用不会二次结算伤害（_resolved 幂等守卫）。
    //   3. AttackEffectManager 每子步每效果只调一次 effect.Update（spec "Effect manager
    //      participates in a substep"）。
    //
    // 来源证据：
    //   - design.md 决策 5："KnifeAttackTimeline 合入 KnifeAttackEffect，只保留一个逻辑计时源。"
    //   - design.md 目录表："以逻辑时间实现刀兵 500ms 时序；吸收原 KnifeAttackTimeline.js，
    //     避免双计时器。"
    //   - tasks.md task 5.5："将原 KnifeAttackTimeline 行为合入 KnifeAttackEffect，
    //     禁止 TEngine Timer/Laya Timer 与 Manager 双轨推进，并添加同一效果仅推进一次的测试。"
    //   - spec battle-simulation "Effect manager participates in a substep"：
    //     攻击效果只累计一次该子步时长，不因嵌套 Manager 调用而双倍推进。
    //
    // 测试策略：
    //   本测试使用 FakeOwner（实现 IAttackEffectOwner）、FakeEnemy（实现 IEnemyEntity）、
    //   真实 AttackResolver 与真实 EnemyManager，构造 KnifeAttackEffect 并验证：
    //     - 到达 500ms 时命中目标并标记非活动
    //     - _resolved 守卫防止重复 Resolve（同一子步内 Update 两次不双倍伤害）
    //     - AttackEffectManager.Update 每子步每效果只调用一次 effect.Update
    //   不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。
    // ============================================================================

    /// <summary>
    /// KnifeAttackEffect 同一效果仅推进一次测试（task 5.5）。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖：</para>
    /// <list type="bullet">
    /// <item>合入确认：KnifeAttackTimeline 行为已合入 KnifeAttackEffect，无单独类。</item>
    /// <item>唯一计时源：KnifeAttackEffect 不注册 TEngine Timer / Laya Timer，只由 Manager.Update 推进。</item>
    /// <item>500ms 命中：elapsed >= 500ms 时 Resolve 并标记非活动。</item>
    /// <item>resolve-once 守卫：同一效果在 500ms 后重复 Update 不二次结算伤害。</item>
    /// <item>Manager 每子步每效果只调一次 Update：AttackEffectManager.Update 不重复推进同一效果。</item>
    /// <item>未到 500ms 不命中：elapsed < 500ms 时不 Resolve。</item>
    /// <item>Cancel 后不再推进：Cancel 置 active=false，后续 Update 不 Resolve。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class KnifeAttackEffectTests
    {
        // ====================================================================
        // FakeOwner —— 最小 IAttackEffectOwner 测试替身
        // ====================================================================

        /// <summary>
        /// 最小 IAttackEffectOwner 测试替身，提供运行时 ID 与阵营。
        /// </summary>
        private sealed class FakeOwner : IAttackEffectOwner
        {
            internal int RuntimeIdValue;
            internal bool SideValue;

            public int RuntimeId => RuntimeIdValue;
            public bool Side => SideValue;
            public float CenterX => 40f;
            public float CenterY => 40f;
        }

        // ====================================================================
        // FakeEnemy —— 最小 IEnemyEntity 测试替身（与 AttackResolverTests 一致）
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
            public float ProjectileAimOffsetX => 0f;
            public float ProjectileAimOffsetY => 0f;
            public float RemainingPathDistance { get; set; } = float.PositiveInfinity;
            public int CurrentPathIndex { get; set; }
            public int Health { get; set; }
            public int MaxHealth { get; set; }
            public bool Targetable { get; set; } = true;

            /// <summary>Hit 调用累计伤害（验证伤害提交）。</summary>
            public int TotalHitDamage;

            /// <summary>Hit 调用次数（验证是否被调用）。</summary>
            public int HitCount;

            /// <summary>上次 Hit 的攻击者 ID。</summary>
            public int LastAttackerId = -1;

            public void Update(long deltaMs)
            {
                // KnifeAttackEffect 不调用 Update，此处无需实现。
            }

            public bool Hit(int damage, int attackerId)
            {
                if (Health <= 0)
                {
                    return false;
                }

                if (damage <= 0)
                {
                    return false;
                }

                Health = Math.Max(0, Health - damage);
                TotalHitDamage += damage;
                HitCount++;
                LastAttackerId = attackerId;
                if (Health <= 0)
                {
                    CurrentState = 4; // DEAD
                }
                return true;
            }

            public bool GameOver()
            {
                CurrentState = 4; // DEAD
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

        /// <summary>默认空间单元边长（与 EnemyManager.DefaultGridSize 一致）。</summary>
        private const int GridSize = 80;

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
        /// 构造已登记一个敌人的 EnemyManager，供命中测试使用。
        /// </summary>
        private static EnemyManager MakeManagerWithEnemy(out FakeEnemy enemy)
        {
            var mgr = new EnemyManager(GridSize);
            enemy = MakeEnemy(1, isPlayerLane: true, x: 40, y: 40, health: 100);
            mgr.Register(enemy);
            return mgr;
        }

        /// <summary>
        /// 构造并启动一个 KnifeAttackEffect，配置参数并激活。
        /// </summary>
        private static KnifeAttackEffect CreateAndLaunch(
            IAttackEffectOwner owner,
            AttackResolver resolver,
            EnemyManager enemyManager,
            int targetId = 1,
            int damage = 30,
            float attackRange = 50f,
            float cellSize = 40f)
        {
            var effect = new KnifeAttackEffect();
            effect.Launch(owner, resolver, enemyManager, targetId, damage, attackRange, cellSize);
            return effect;
        }

        // ====================================================================
        // 合入确认测试 —— KnifeAttackTimeline 已合入 KnifeAttackEffect
        // ====================================================================

        [Test]
        [Description("KnifeAttackTimeline 行为已合入 KnifeAttackEffect：KnifeAttackEffect 可独立构造与启动，无需 Timeline。")]
        public void KnifeAttackTimeline_MergedIntoKnifeAttackEffect_EffectStandalone()
        {
            // KnifeAttackEffect 直接持有 owner/resolver/enemyManager/targetId/damage/elapsed，
            // 不依赖任何 timeline 对象。Launch 不接收 timeline 参数（对比 JS launch 接收 timeline）。
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr);

            Assert.IsTrue(effect.Active, "启动后应 Active=true");
            Assert.AreSame(owner, effect.Owner, "Owner 应为传入的 FakeOwner");
        }

        // ====================================================================
        // 唯一计时源测试 —— 不注册 Timer，只由 Manager.Update 推进
        // ====================================================================

        [Test]
        [Description("KnifeAttackEffect 不注册 TEngine Timer / Laya Timer，唯一计时源为 Update(deltaMs)。")]
        public void KnifeAttackEffect_NoTimerRegistration_OnlyAdvancedByUpdate()
        {
            // KnifeAttackEffect 只有 Update(long deltaMs) 作为推进入口，
            // 无 Timer 注册字段或方法。唯一计时源为 AttackEffectManager.Update → effect.Update。
            // 本测试验证：不调用 Update 时 elapsed 不累计，效果不命中。
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr);

            // 不调 Update，效果不应命中。
            Assert.AreEqual(0, enemy.HitCount, "未推进时不应命中");
            Assert.IsTrue(effect.Active, "未推进时仍应 Active=true");

            // 推进 499ms（不足 500ms），不命中。
            effect.Update(499);
            Assert.AreEqual(0, enemy.HitCount, "499ms 不足 500ms 不应命中");
            Assert.IsTrue(effect.Active, "499ms 不足 500ms 仍应 Active=true");
        }

        // ====================================================================
        // 500ms 命中测试
        // ====================================================================

        [Test]
        [Description("推进到 500ms 时 Resolve 命中目标并标记非活动。")]
        public void Update_At500ms_ResolvesAndMarksInactive()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, damage: 30);

            effect.Update(KnifeHitDelayMs);

            Assert.IsFalse(effect.Active, "500ms 命中后应 Active=false");
            Assert.AreEqual(1, enemy.HitCount, "应命中一次");
            Assert.AreEqual(70, enemy.Health, "血量应从 100 扣减到 70");
            Assert.AreEqual(10, enemy.LastAttackerId, "应记录攻击者 ID=10");
        }

        [Test]
        [Description("分多步推进累计到 500ms 时命中（逻辑时间累计语义）。")]
        public void Update_MultipleStepsAccumulateTo500ms_Resolves()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, damage: 30);

            effect.Update(200);
            Assert.IsTrue(effect.Active, "200ms 不足 500ms 仍应 Active");
            Assert.AreEqual(0, enemy.HitCount, "200ms 不应命中");

            effect.Update(200);
            Assert.IsTrue(effect.Active, "400ms 不足 500ms 仍应 Active");
            Assert.AreEqual(0, enemy.HitCount, "400ms 不应命中");

            effect.Update(100); // 累计 500ms
            Assert.IsFalse(effect.Active, "500ms 命中后应 Active=false");
            Assert.AreEqual(1, enemy.HitCount, "应命中一次");
            Assert.AreEqual(70, enemy.Health, "血量应扣减到 70");
        }

        // ====================================================================
        // resolve-once 守卫测试 —— 同一效果仅推进一次（核心测试）
        // ====================================================================

        [Test]
        [Description("同一效果在 500ms 命中后，重复 Update 调用不二次结算伤害（_resolved 幂等守卫）。")]
        public void Update_AfterResolve_RepeatUpdateDoesNotDoubleResolve()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, damage: 30);

            // 第一次推进 500ms → 命中。
            effect.Update(KnifeHitDelayMs);
            Assert.AreEqual(1, enemy.HitCount, "第一次推进应命中一次");
            Assert.AreEqual(70, enemy.Health, "第一次命中后血量应为 70");
            Assert.IsFalse(effect.Active, "命中后应 Active=false");

            // 重复 Update（模拟错误的双轨推进或重复调用）——不应二次结算。
            effect.Update(KnifeHitDelayMs);
            effect.Update(KnifeHitDelayMs);

            Assert.AreEqual(1, enemy.HitCount, "重复 Update 不应增加命中次数");
            Assert.AreEqual(70, enemy.Health, "重复 Update 不应再次扣血");
        }

        [Test]
        [Description("同一子步内 Update 两次不双倍推进伤害：第一次到达 500ms 命中后，第二次 Update 不再 Resolve。")]
        public void Update_TwiceInSameSubstep_AfterResolve_NoDoubleDamage()
        {
            // 本测试模拟 task 5.5 "同一效果仅推进一次" 的核心要求：
            // 即使同一子步内 Update 被调用两次（如错误的双轨推进），
            // _resolved 守卫 + _active=false 保证只命中一次。
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, damage: 30);

            // 同一子步内连续两次 Update（每次 500ms）。
            effect.Update(KnifeHitDelayMs);
            effect.Update(KnifeHitDelayMs);

            Assert.AreEqual(1, enemy.HitCount, "同一子步内两次 Update 只应命中一次");
            Assert.AreEqual(30, enemy.TotalHitDamage, "总伤害应只有一次 30，不应双倍");
        }

        // ====================================================================
        // AttackEffectManager 每子步每效果只调一次 Update 测试
        // ====================================================================

        [Test]
        [Description("AttackEffectManager.Update 每子步每效果只调用一次 effect.Update（spec Effect manager participates in a substep）。")]
        public void ManagerUpdate_AdvancesEffectOncePerSubstep()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, damage: 30);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            // 第一个子步 500ms：效果命中并标记非活动，Manager 移除回收。
            manager.Update(KnifeHitDelayMs);

            Assert.AreEqual(1, enemy.HitCount, "第一个子步应命中一次");
            Assert.AreEqual(1, manager.UpdateCount, "Manager 应只 Update 一次");
            Assert.AreEqual(0, manager.ActiveCount, "命中后效果应被移除，活动数归零");

            // 第二个子步：效果已不在活动集合，不会再被推进。
            manager.Update(KnifeHitDelayMs);

            Assert.AreEqual(1, enemy.HitCount, "第二个子步不应再次命中（效果已移除）");
            Assert.AreEqual(30, enemy.TotalHitDamage, "总伤害不应增加");
            Assert.AreEqual(2, manager.UpdateCount, "Manager 应 Update 两次（但第二次无活动效果）");
        }

        [Test]
        [Description("AttackEffectManager 多子步推进：未到 500ms 不命中，到 500ms 只命中一次。")]
        public void ManagerUpdate_MultipleSubsteps_OnlyResolvesOnceAt500ms()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, damage: 30);

            var manager = new AttackEffectManager();
            manager.Add(effect);

            // 三个子步：200 + 200 + 100 = 500ms
            manager.Update(200);
            Assert.AreEqual(0, enemy.HitCount, "200ms 不应命中");
            Assert.AreEqual(1, manager.ActiveCount, "效果仍应活动");

            manager.Update(200);
            Assert.AreEqual(0, enemy.HitCount, "400ms 不应命中");
            Assert.AreEqual(1, manager.ActiveCount, "效果仍应活动");

            manager.Update(100);
            Assert.AreEqual(1, enemy.HitCount, "500ms 应命中一次");
            Assert.AreEqual(0, manager.ActiveCount, "命中后应移除");

            // 再推进一次，不应重复命中。
            manager.Update(100);
            Assert.AreEqual(1, enemy.HitCount, "额外子步不应重复命中");
        }

        // ====================================================================
        // Cancel 后不再推进测试
        // ====================================================================

        [Test]
        [Description("Cancel 后效果不再推进，即使后续 Update 到达 500ms 也不命中。")]
        public void Cancel_BeforeResolve_PreventsSubsequentResolve()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, damage: 30);

            // 推进 300ms（未到 500ms），然后 Cancel。
            effect.Update(300);
            Assert.IsTrue(effect.Active, "300ms 仍应 Active");

            effect.Cancel("test-cancel");
            Assert.IsFalse(effect.Active, "Cancel 后应 Active=false");
            Assert.AreEqual(0, enemy.HitCount, "Cancel 前不应命中");

            // Cancel 后再推进 500ms，不应命中。
            effect.Update(KnifeHitDelayMs);
            Assert.AreEqual(0, enemy.HitCount, "Cancel 后不应命中");
            Assert.AreEqual(100, enemy.Health, "血量不应改变");
        }

        // ====================================================================
        // 命中点有限重选测试（design 决策 3/4，task 3.4）
        // --------------------------------------------------------------------
        // 验证：首选目标死亡 → 稳定回退命中替代目标一次；
        //       无替代目标 → 无伤害完成；
        //       首选有效 → 不切换；
        //       重复 Update 不重复伤害；Cancel 后不查询/不伤害；池复用无旧目标残留。
        // ====================================================================

        /// <summary>
        /// 构造已登记两个敌人的 EnemyManager（均在攻击范围内），供重选测试使用。
        /// </summary>
        private static EnemyManager MakeManagerWithTwoEnemies(
            out FakeEnemy first, out FakeEnemy second)
        {
            var mgr = new EnemyManager(GridSize);
            first = MakeEnemy(1, isPlayerLane: true, x: 40, y: 40, health: 100);
            second = MakeEnemy(2, isPlayerLane: true, x: 40, y: 40, health: 100);
            mgr.Register(first);
            mgr.Register(second);
            return mgr;
        }

        [Test]
        [Description("刀兵命中前原目标死亡且存在替代目标 → 命中稳定顺序中的下一个目标一次。")]
        public void KnifeHit_PreferredDead_HasAlternative_HitsAlternativeOnce()
        {
            EnemyManager mgr = MakeManagerWithTwoEnemies(out FakeEnemy first, out FakeEnemy second);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            // 初始目标为 first(id=1)，命中前 first 死亡。
            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, targetId: 1, damage: 30);
            first.GameOver(); // first 死亡，IsTargetableBy 返回 false。

            effect.Update(KnifeHitDelayMs);

            Assert.AreEqual(0, first.HitCount, "已死亡的初始目标不应被命中");
            Assert.AreEqual(1, second.HitCount, "应命中稳定顺序中的替代目标一次");
            Assert.AreEqual(70, second.Health, "替代目标血量应扣减 30");
            Assert.IsFalse(effect.Active, "命中后应完成");
        }

        [Test]
        [Description("同一子步两个刀兵锁定同一目标：首个击杀，后一个稳定回退，各自只结算一次。")]
        public void SameSubstep_TwoLockedAttacks_FirstKills_SecondFallsBackOnce()
        {
            EnemyManager mgr = MakeManagerWithTwoEnemies(out FakeEnemy first, out FakeEnemy second);
            first.Health = 30;
            var resolver = new AttackResolver();
            var manager = new AttackEffectManager();
            var firstOwner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };
            var secondOwner = new FakeOwner { RuntimeIdValue = 20, SideValue = true };

            KnifeAttackEffect firstEffect = CreateAndLaunch(
                firstOwner, resolver, mgr, targetId: first.Id, damage: 30);
            KnifeAttackEffect secondEffect = CreateAndLaunch(
                secondOwner, resolver, mgr, targetId: first.Id, damage: 30);
            manager.Add(firstEffect);
            manager.Add(secondEffect);

            // Manager 按登记顺序推进：首个效果击杀 first，第二个效果看到首选已死亡，
            // 在同一子步内稳定回退到 second。每个效果只有一次结算机会。
            manager.Update(KnifeHitDelayMs);

            Assert.AreEqual(1, first.HitCount, "首选目标只应收到一次致死伤害");
            Assert.AreEqual(0, first.Health, "首选目标应被首个效果击杀");
            Assert.AreEqual(firstOwner.RuntimeId, first.LastAttackerId,
                "致死伤害应来自登记顺序中的首个攻击者");
            Assert.AreEqual(1, second.HitCount, "后续攻击只应稳定回退并命中替代目标一次");
            Assert.AreEqual(70, second.Health, "替代目标只应扣除一次伤害");
            Assert.AreEqual(secondOwner.RuntimeId, second.LastAttackerId,
                "替代目标伤害应来自第二个攻击者");
            Assert.AreEqual(0, manager.ActiveCount, "两个效果结算后都应完成");
        }

        [Test]
        [Description("刀兵命中时原目标已失效且范围内无替代目标 → 无伤害完成。")]
        public void KnifeHit_PreferredDead_NoAlternative_NoDamageCompletes()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, targetId: 1, damage: 30);
            enemy.GameOver(); // 唯一目标死亡，无替代。

            effect.Update(KnifeHitDelayMs);

            Assert.AreEqual(0, enemy.HitCount, "无替代目标时不应命中");
            Assert.AreEqual(100, enemy.Health, "血量不应改变");
            Assert.IsFalse(effect.Active, "无伤害完成仍应标记非活动");
        }

        [Test]
        [Description("刀兵命中时首选目标仍有效 → 继续命中首选，不切换到其他目标。")]
        public void KnifeHit_PreferredValid_DoesNotSwitch()
        {
            EnemyManager mgr = MakeManagerWithTwoEnemies(out FakeEnemy first, out FakeEnemy second);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            // 首选 first 仍存活，second 也在范围内但不切换。
            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, targetId: 1, damage: 30);

            effect.Update(KnifeHitDelayMs);

            Assert.AreEqual(1, first.HitCount, "应命中首选目标");
            Assert.AreEqual(0, second.HitCount, "首选有效时不应切换到其他目标");
        }

        [Test]
        [Description("刀兵命中后重复 Update 不二次伤害（_resolved 幂等 + 重选只一次）。")]
        public void KnifeHit_RepeatUpdate_NoSecondaryDamage()
        {
            EnemyManager mgr = MakeManagerWithTwoEnemies(out FakeEnemy first, out FakeEnemy second);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, targetId: 1, damage: 30);
            first.GameOver();

            effect.Update(KnifeHitDelayMs);
            // 重复推进多次。
            effect.Update(100);
            effect.Update(100);

            Assert.AreEqual(1, second.HitCount, "重选只命中替代目标一次，不因重复 Update 二次伤害");
        }

        [Test]
        [Description("Cancel 后不查询目标、不伤害（取消路径不触发重选）。")]
        public void KnifeHit_Cancelled_NoQueryNoDamage()
        {
            EnemyManager mgr = MakeManagerWithTwoEnemies(out FakeEnemy first, out FakeEnemy second);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, targetId: 1, damage: 30);
            first.GameOver();

            effect.Cancel("owner-removed");
            effect.Update(KnifeHitDelayMs);

            Assert.AreEqual(0, first.HitCount, "Cancel 后不应命中任何目标");
            Assert.AreEqual(0, second.HitCount, "Cancel 后不应触发重选");
        }

        [Test]
        [Description("池复用 ResetState 清空旧目标与重选字段，无残留。")]
        public void KnifeHit_PoolReuse_NoStaleTargetResidue()
        {
            EnemyManager mgr = MakeManagerWithTwoEnemies(out FakeEnemy first, out FakeEnemy second);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, targetId: 1, damage: 30);
            first.GameOver();
            effect.Update(KnifeHitDelayMs);
            Assert.AreEqual(1, second.HitCount, "首次使用应命中替代目标");

            // 池回收 + 复用：新目标集合无 first，新初始目标 id=2。
            effect.ResetState();
            EnemyManager mgr2 = MakeManagerWithEnemy(out FakeEnemy newEnemy);
            effect.Launch(owner, resolver, mgr2, targetId: 1, damage: 20, attackRange: 50f, cellSize: 40f);
            effect.Update(KnifeHitDelayMs);

            Assert.AreEqual(1, newEnemy.HitCount, "复用后应命中新目标，无旧目标残留");
            Assert.AreEqual(1, second.HitCount, "旧目标命中次数不应改变（无污染）");
        }

        // ====================================================================
        // 池复用 ResetState 测试 —— 回收后 resolve-once 守卫重置
        // ====================================================================

        [Test]
        [Description("ResetState 清空 _resolved 守卫，复用后效果可再次正常命中。")]
        public void ResetState_ClearsResolvedGuard_EffectReusable()
        {
            EnemyManager mgr = MakeManagerWithEnemy(out FakeEnemy enemy);
            var resolver = new AttackResolver();
            var owner = new FakeOwner { RuntimeIdValue = 10, SideValue = true };

            KnifeAttackEffect effect = CreateAndLaunch(owner, resolver, mgr, damage: 30);

            // 第一次使用：命中并完成。
            effect.Update(KnifeHitDelayMs);
            Assert.AreEqual(1, enemy.HitCount, "第一次应命中");
            Assert.IsFalse(effect.Active, "第一次命中后应 Active=false");

            // ResetState 模拟池回收。
            effect.ResetState();
            Assert.IsFalse(effect.Active, "ResetState 后应 Active=false");
            Assert.AreNotSame(owner, effect.Owner, "ResetState 后 Owner 应为 null");

            // 重新 Launch 并使用——_resolved 守卫应已重置，可再次命中。
            EnemyManager mgr2 = MakeManagerWithEnemy(out FakeEnemy enemy2);
            effect.Launch(owner, resolver, mgr2, targetId: 1, damage: 25, attackRange: 50f, cellSize: 40f);
            effect.Update(KnifeHitDelayMs);

            Assert.AreEqual(1, enemy2.HitCount, "复用后应命中新目标");
            Assert.AreEqual(75, enemy2.Health, "新目标血量应扣减到 75");
            Assert.AreEqual(1, enemy.HitCount, "原目标命中次数不应改变（无污染）");
        }
    }
}
