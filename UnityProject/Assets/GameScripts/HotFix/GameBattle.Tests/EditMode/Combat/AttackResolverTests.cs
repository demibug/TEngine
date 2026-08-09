using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Combat
{
    // ============================================================================
    // 任务 5.1：AttackResolver 单元测试
    // ----------------------------------------------------------------------------
    // 验证要求（task 5.1）：
    //   1. 目标查询：QueryTargets / QueryEnemyObjects 委托 EnemyManager 稳定有序查询。
    //   2. 伤害提交：Hit 委托 IEnemyEntity.Hit，null/死亡/非正伤害被拒绝。
    //   3. 目标死亡边界：已死亡目标的 Hit 返回 false 不提交伤害。
    //   4. 批量伤害：ApplyDamage 委托 EnemyManager.ApplyDamage。
    //   5. 无状态：多次调用结果一致，不持有可变状态。
    //
    // spec battle-simulation "Simulation is reproducible"：
    //   不依赖无序集合遍历决定目标、伤害和胜负。AttackResolver 只委托
    //   EnemyManager 的稳定有序查询，本测试验证委托路径正确。
    //
    // spec battle-simulation "Battle result is frozen once" / 决策 0.4：
    //   冻结后迟到伤害被拒绝。本测试通过模拟死亡目标验证 Hit 守卫。
    //
    // 设计约束（task 5.1）：
    //   本期不为只有一个生产实现的 Resolver 额外创建公共接口。
    //   AttackResolver 为 internal sealed class，测试经 InternalsVisibleTo 访问。
    // ============================================================================

    /// <summary>
    /// AttackResolver 目标查询、伤害提交、死亡边界与批量伤害测试（task 5.1）。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖：</para>
    /// <list type="bullet">
    /// <item>目标查询：QueryTargets / QueryEnemyObjects 委托 EnemyManager，返回稳定有序结果。</item>
    /// <item>伤害提交：Hit 委托 IEnemyEntity.Hit，null 目标返回 false。</item>
    /// <item>目标死亡边界：已死亡目标（Health &lt;= 0）Hit 返回 false。</item>
    /// <item>非正伤害守卫：damage &lt;= 0 时 Hit 返回 false。</item>
    /// <item>批量伤害：ApplyDamage 委托 EnemyManager.ApplyDamage。</item>
    /// <item>无状态：同一 AttackResolver 实例多次查询结果一致。</item>
    /// <item>EnemyManager 为 null 时安全降级返回空列表。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class AttackResolverTests
    {
        // ====================================================================
        // FakeEnemy —— 最小 IEnemyEntity 测试替身（与 EnemyManagerTests 一致）
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
                // AttackResolver 不调用 Update，此处无需实现。
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

        /// <summary>默认空间单元边长（与 EnemyManager.DefaultGridSize 一致）。</summary>
        private const int GridSize = 80;

        /// <summary>敌人格子宽/高（对应 map.gridWidth/gridHeight）。</summary>
        private const float CellWidth = 80f;
        private const float CellHeight = 80f;

        /// <summary>
        /// 构造一个 FakeEnemy，位于指定位置。
        /// </summary>
        private static FakeEnemy MakeEnemy(
            int id, bool isPlayerLane, float x, float y,
            int health = 100, float remainingPathDistance = 1000f,
            int state = 1)
        {
            return new FakeEnemy
            {
                Id = id,
                IsPlayerLane = isPlayerLane,
                X = x,
                Y = y,
                Health = health,
                RemainingPathDistance = remainingPathDistance,
                CurrentState = state,
            };
        }

        /// <summary>
        /// 构造已登记若干敌人的 EnemyManager，供查询/伤害测试使用。
        /// </summary>
        private static EnemyManager MakeManagerWithEnemies(out FakeEnemy enemy1, out FakeEnemy enemy2)
        {
            var mgr = new EnemyManager(GridSize);
            enemy1 = MakeEnemy(1, isPlayerLane: true, x: 40, y: 40, health: 100);
            enemy2 = MakeEnemy(2, isPlayerLane: true, x: 120, y: 40, health: 50);
            mgr.Register(enemy1);
            mgr.Register(enemy2);
            return mgr;
        }

        // ====================================================================
        // 目标查询测试
        // ====================================================================

        [Test]
        [Description("QueryTargets 委托 EnemyManager 返回稳定有序目标 DTO 列表。")]
        public void QueryTargets_DelegatesToEnemyManager_ReturnsStableResult()
        {
            EnemyManager mgr = MakeManagerWithEnemies(out FakeEnemy e1, out FakeEnemy e2);
            var resolver = new AttackResolver();

            // 查询中心位于敌人 1 附近，半径覆盖敌人 1 但不覆盖敌人 2。
            List<EnemyTargetDto> targets = resolver.QueryTargets(
                mgr, centerX: 40f, centerY: 40f, range: 50f,
                playerSide: true, cellWidth: CellWidth, cellHeight: CellHeight);

            Assert.AreEqual(1, targets.Count, "应只返回敌人 1");
            Assert.AreEqual(1, targets[0].Id, "返回的目标 ID 应为敌人 1");
        }

        [Test]
        [Description("QueryTargets 覆盖多个敌人时返回全部匹配目标。")]
        public void QueryTargets_MultipleEnemies_ReturnsAllMatching()
        {
            EnemyManager mgr = MakeManagerWithEnemies(out FakeEnemy e1, out FakeEnemy e2);
            var resolver = new AttackResolver();

            // 查询中心位于两敌人之间，半径覆盖两者。
            List<EnemyTargetDto> targets = resolver.QueryTargets(
                mgr, centerX: 80f, centerY: 40f, range: 100f,
                playerSide: true, cellWidth: CellWidth, cellHeight: CellHeight);

            Assert.AreEqual(2, targets.Count, "应返回两个敌人");
        }

        [Test]
        [Description("QueryTargets 在 EnemyManager 为 null 时安全降级返回空列表。")]
        public void QueryTargets_NullEnemyManager_ReturnsEmptyList()
        {
            var resolver = new AttackResolver();

            List<EnemyTargetDto> targets = resolver.QueryTargets(
                null, centerX: 0f, centerY: 0f, range: 100f,
                playerSide: true, cellWidth: CellWidth, cellHeight: CellHeight);

            Assert.IsNotNull(targets, "null EnemyManager 不应返回 null");
            Assert.AreEqual(0, targets.Count, "null EnemyManager 应返回空列表");
        }

        [Test]
        [Description("QueryTargets 对已死亡敌人（IsTargetableBy=false）不返回。")]
        public void QueryTargets_DeadEnemy_ExcludedFromResults()
        {
            var mgr = new EnemyManager(GridSize);
            FakeEnemy alive = MakeEnemy(1, isPlayerLane: true, x: 40, y: 40, health: 100);
            FakeEnemy dead = MakeEnemy(2, isPlayerLane: true, x: 40, y: 40, health: 0, state: 4);
            mgr.Register(alive);
            mgr.Register(dead);

            var resolver = new AttackResolver();

            List<EnemyTargetDto> targets = resolver.QueryTargets(
                mgr, centerX: 40f, centerY: 40f, range: 100f,
                playerSide: true, cellWidth: CellWidth, cellHeight: CellHeight);

            Assert.AreEqual(1, targets.Count, "只应返回存活敌人");
            Assert.AreEqual(1, targets[0].Id, "存活敌人 ID=1");
        }

        [Test]
        [Description("QueryEnemyObjects 委托 EnemyManager 返回敌人对象列表。")]
        public void QueryEnemyObjects_DelegatesToEnemyManager_ReturnsEntities()
        {
            EnemyManager mgr = MakeManagerWithEnemies(out FakeEnemy e1, out FakeEnemy e2);
            var resolver = new AttackResolver();

            List<IEnemyEntity> enemies = resolver.QueryEnemyObjects(
                mgr, centerX: 40f, centerY: 40f, range: 50f,
                playerSide: true, cellWidth: CellWidth, cellHeight: CellHeight, null);

            Assert.AreEqual(1, enemies.Count, "应只返回敌人 1");
            Assert.AreSame(e1, enemies[0], "返回的实体应为敌人 1");
        }

        [Test]
        [Description("QueryEnemyObjects 在 EnemyManager 为 null 时安全降级返回空列表。")]
        public void QueryEnemyObjects_NullEnemyManager_ReturnsEmptyList()
        {
            var resolver = new AttackResolver();

            List<IEnemyEntity> enemies = resolver.QueryEnemyObjects(
                null, centerX: 0f, centerY: 0f, range: 100f,
                playerSide: true, cellWidth: CellWidth, cellHeight: CellHeight, null);

            Assert.IsNotNull(enemies, "null EnemyManager 不应返回 null");
            Assert.AreEqual(0, enemies.Count, "null EnemyManager 应返回空列表");
        }

        [Test]
        [Description("QueryEnemyObjects 传入 output 缓冲时追加结果而非新建列表。")]
        public void QueryEnemyObjects_WithOutputBuffer_AppendsToList()
        {
            EnemyManager mgr = MakeManagerWithEnemies(out FakeEnemy e1, out FakeEnemy e2);
            var resolver = new AttackResolver();
            var output = new List<IEnemyEntity>();

            List<IEnemyEntity> result = resolver.QueryEnemyObjects(
                mgr, centerX: 40f, centerY: 40f, range: 50f,
                playerSide: true, cellWidth: CellWidth, cellHeight: CellHeight, output);

            Assert.AreSame(output, result, "应返回传入的 output 列表");
            Assert.AreEqual(1, result.Count, "应追加敌人 1");
        }

        // ====================================================================
        // 伤害提交测试
        // ====================================================================

        [Test]
        [Description("Hit 对存活目标提交伤害并返回 true。")]
        public void Hit_AliveTarget_AppliesDamage_ReturnsTrue()
        {
            var target = MakeEnemy(1, isPlayerLane: true, x: 40, y: 40, health: 100);
            var resolver = new AttackResolver();

            bool result = resolver.Hit(target, damage: 30, attackerId: 5);

            Assert.IsTrue(result, "存活目标应返回 true");
            Assert.AreEqual(70, target.Health, "血量应从 100 扣减到 70");
            Assert.AreEqual(5, target.LastAttackerId, "应记录攻击者 ID");
            Assert.AreEqual(1, target.HitCount, "Hit 应被调用一次");
        }

        [Test]
        [Description("Hit 对 null 目标返回 false 不抛异常。")]
        public void Hit_NullTarget_ReturnsFalse()
        {
            var resolver = new AttackResolver();

            bool result = resolver.Hit(null, damage: 30, attackerId: 5);

            Assert.IsFalse(result, "null 目标应返回 false");
        }

        [Test]
        [Description("Hit 对非正伤害返回 false 不提交。")]
        public void Hit_NonPositiveDamage_ReturnsFalse()
        {
            var target = MakeEnemy(1, isPlayerLane: true, x: 40, y: 40, health: 100);
            var resolver = new AttackResolver();

            bool resultZero = resolver.Hit(target, damage: 0, attackerId: 5);
            bool resultNeg = resolver.Hit(target, damage: -10, attackerId: 5);

            Assert.IsFalse(resultZero, "damage=0 应返回 false");
            Assert.IsFalse(resultNeg, "damage<0 应返回 false");
            Assert.AreEqual(100, target.Health, "血量不应改变");
            Assert.AreEqual(0, target.HitCount, "Hit 内部不应被调用");
        }

        // ====================================================================
        // 目标死亡边界测试
        // ====================================================================

        [Test]
        [Description("Hit 对已死亡目标（Health<=0）返回 false 不提交伤害。")]
        public void Hit_DeadTarget_ReturnsFalse_NoDamage()
        {
            var dead = MakeEnemy(1, isPlayerLane: true, x: 40, y: 40, health: 0, state: 4);
            var resolver = new AttackResolver();

            bool result = resolver.Hit(dead, damage: 30, attackerId: 5);

            Assert.IsFalse(result, "已死亡目标应返回 false");
            Assert.AreEqual(0, dead.Health, "死亡目标血量不应改变");
            Assert.AreEqual(0, dead.HitCount, "死亡目标 Hit 内部不应被调用");
        }

        [Test]
        [Description("Hit 致死伤害使目标血量归零，后续 Hit 返回 false。")]
        public void Hit_LethalDamage_TargetDies_SubsequentHitRejected()
        {
            var target = MakeEnemy(1, isPlayerLane: true, x: 40, y: 40, health: 30);
            var resolver = new AttackResolver();

            // 第一次伤害致死。
            bool firstHit = resolver.Hit(target, damage: 30, attackerId: 5);
            Assert.IsTrue(firstHit, "致死伤害应返回 true");
            Assert.AreEqual(0, target.Health, "血量应归零");
            Assert.AreEqual(4, target.CurrentState, "状态应为 DEAD=4");

            // 第二次伤害对已死亡目标被拒绝。
            bool secondHit = resolver.Hit(target, damage: 20, attackerId: 6);
            Assert.IsFalse(secondHit, "已死亡目标的迟到伤害应返回 false");
            Assert.AreEqual(0, target.Health, "死亡目标血量不应改变");
            Assert.AreEqual(1, target.HitCount, "Hit 内部只应被调用一次（第一次）");
        }

        [Test]
        [Description("Hit 连续多次伤害直到死亡，每次存活时返回 true。")]
        public void Hit_MultipleHits_UntilDeath_EachAliveHitReturnsTrue()
        {
            var target = MakeEnemy(1, isPlayerLane: true, x: 40, y: 40, health: 50);
            var resolver = new AttackResolver();

            bool h1 = resolver.Hit(target, 20, -1);
            Assert.IsTrue(h1, "第一次伤害应生效");
            Assert.AreEqual(30, target.Health);

            bool h2 = resolver.Hit(target, 20, -1);
            Assert.IsTrue(h2, "第二次伤害应生效");
            Assert.AreEqual(10, target.Health);

            bool h3 = resolver.Hit(target, 20, -1);
            Assert.IsTrue(h3, "第三次致死伤害应生效");
            Assert.AreEqual(0, target.Health);
            Assert.AreEqual(4, target.CurrentState, "状态应为 DEAD=4");

            bool h4 = resolver.Hit(target, 10, -1);
            Assert.IsFalse(h4, "死亡后伤害应被拒绝");
        }

        // ====================================================================
        // 批量伤害测试
        // ====================================================================

        [Test]
        [Description("ApplyDamage 委托 EnemyManager.ApplyDamage 对多个目标提交伤害。")]
        public void ApplyDamage_DelegatesToEnemyManager_MultiTargetDamage()
        {
            EnemyManager mgr = MakeManagerWithEnemies(out FakeEnemy e1, out FakeEnemy e2);
            var resolver = new AttackResolver();

            var dtos = new List<EnemyTargetDto>
            {
                new EnemyTargetDto(1, 40f, 40f, 1000f),
                new EnemyTargetDto(2, 120f, 40f, 800f),
            };

            resolver.ApplyDamage(mgr, damage: 30, targetDtos: dtos, attackerId: 7);

            Assert.AreEqual(70, e1.Health, "敌人 1 应扣血 30");
            Assert.AreEqual(20, e2.Health, "敌人 2 应扣血 30");
            Assert.AreEqual(7, e1.LastAttackerId, "敌人 1 应记录攻击者 7");
            Assert.AreEqual(7, e2.LastAttackerId, "敌人 2 应记录攻击者 7");
        }

        [Test]
        [Description("ApplyDamage 对空 DTO 列表不提交伤害。")]
        public void ApplyDamage_EmptyDtoList_NoDamage()
        {
            EnemyManager mgr = MakeManagerWithEnemies(out FakeEnemy e1, out FakeEnemy e2);
            var resolver = new AttackResolver();

            resolver.ApplyDamage(mgr, damage: 30, targetDtos: new List<EnemyTargetDto>(), attackerId: 7);

            Assert.AreEqual(100, e1.Health, "敌人 1 血量不应改变");
            Assert.AreEqual(50, e2.Health, "敌人 2 血量不应改变");
        }

        [Test]
        [Description("ApplyDamage 对不存在的 DTO ID 静默跳过。")]
        public void ApplyDamage_NonExistentId_SilentlySkipped()
        {
            EnemyManager mgr = MakeManagerWithEnemies(out FakeEnemy e1, out FakeEnemy e2);
            var resolver = new AttackResolver();

            var dtos = new List<EnemyTargetDto>
            {
                new EnemyTargetDto(1, 40f, 40f, 1000f),
                new EnemyTargetDto(999, 0f, 0f, 0f), // 不存在的 ID
            };

            resolver.ApplyDamage(mgr, damage: 30, targetDtos: dtos, attackerId: 7);

            Assert.AreEqual(70, e1.Health, "敌人 1 应扣血 30");
            Assert.AreEqual(50, e2.Health, "敌人 2 血量不应改变");
        }

        [Test]
        [Description("ApplyDamage 对 null EnemyManager 安全降级不抛异常。")]
        public void ApplyDamage_NullEnemyManager_NoException()
        {
            var resolver = new AttackResolver();
            var dtos = new List<EnemyTargetDto> { new EnemyTargetDto(1, 0f, 0f, 0f) };

            // 不应抛异常。
            resolver.ApplyDamage(null, damage: 30, targetDtos: dtos, attackerId: 7);
        }

        [Test]
        [Description("ApplyDamage 对非正伤害不提交。")]
        public void ApplyDamage_NonPositiveDamage_NoEffect()
        {
            EnemyManager mgr = MakeManagerWithEnemies(out FakeEnemy e1, out FakeEnemy e2);
            var resolver = new AttackResolver();
            var dtos = new List<EnemyTargetDto>
            {
                new EnemyTargetDto(1, 40f, 40f, 1000f),
            };

            resolver.ApplyDamage(mgr, damage: 0, targetDtos: dtos, attackerId: 7);
            resolver.ApplyDamage(mgr, damage: -5, targetDtos: dtos, attackerId: 7);

            Assert.AreEqual(100, e1.Health, "非正伤害不应改变血量");
        }

        [Test]
        [Description("ApplyDamage 对 null DTO 列表安全降级不抛异常。")]
        public void ApplyDamage_NullDtoList_NoException()
        {
            EnemyManager mgr = MakeManagerWithEnemies(out _, out _);
            var resolver = new AttackResolver();

            // 不应抛异常。
            resolver.ApplyDamage(mgr, damage: 30, targetDtos: null, attackerId: 7);
        }

        // ====================================================================
        // 无状态测试
        // ====================================================================

        [Test]
        [Description("同一 AttackResolver 实例多次查询结果一致（无状态）。")]
        public void QueryTargets_Stateless_MultipleCallsProduceSameResult()
        {
            EnemyManager mgr = MakeManagerWithEnemies(out FakeEnemy e1, out FakeEnemy e2);
            var resolver = new AttackResolver();

            List<EnemyTargetDto> first = resolver.QueryTargets(
                mgr, 40f, 40f, 50f, true, CellWidth, CellHeight);
            List<EnemyTargetDto> second = resolver.QueryTargets(
                mgr, 40f, 40f, 50f, true, CellWidth, CellHeight);

            Assert.AreEqual(first.Count, second.Count, "两次查询结果数量应一致");
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].Id, second[i].Id, $"第 {i} 个目标 ID 应一致");
            }
        }

        [Test]
        [Description("两个 AttackResolver 实例对同一 EnemyManager 查询结果一致（无构造状态差异）。")]
        public void QueryTargets_TwoInstances_SameResult()
        {
            EnemyManager mgr = MakeManagerWithEnemies(out FakeEnemy e1, out FakeEnemy e2);
            var resolverA = new AttackResolver();
            var resolverB = new AttackResolver();

            List<EnemyTargetDto> resultA = resolverA.QueryTargets(
                mgr, 80f, 40f, 100f, true, CellWidth, CellHeight);
            List<EnemyTargetDto> resultB = resolverB.QueryTargets(
                mgr, 80f, 40f, 100f, true, CellWidth, CellHeight);

            Assert.AreEqual(resultA.Count, resultB.Count, "两个实例查询结果数量应一致");
            for (int i = 0; i < resultA.Count; i++)
            {
                Assert.AreEqual(resultA[i].Id, resultB[i].Id, $"第 {i} 个目标 ID 应一致");
            }
        }

        // ====================================================================
        // 阵营过滤测试
        // ====================================================================

        [Test]
        [Description("QueryTargets 按 playerSide 过滤，只返回对方阵营敌人。")]
        public void QueryTargets_PlayerSide_FiltersOpponentLane()
        {
            var mgr = new EnemyManager(GridSize);
            // 玩家方敌人（isPlayerLane=true），对手方攻击者（playerSide=false）可攻击。
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 40, y: 40));
            // 对手方敌人（isPlayerLane=false），玩家方攻击者（playerSide=true）可攻击。
            mgr.Register(MakeEnemy(2, isPlayerLane: false, x: 40, y: 40));

            var resolver = new AttackResolver();

            // playerSide=true → 查询 isPlayerLane=true 的敌人（玩家方攻击对手方）。
            // 注：IsTargetableBy 要求 IsPlayerLane == playerSide，即 playerSide=true
            // 只能攻击 isPlayerLane=true 的敌人。
            List<EnemyTargetDto> playerSideTargets = resolver.QueryTargets(
                mgr, 40f, 40f, 100f, playerSide: true, CellWidth, CellHeight);
            Assert.AreEqual(1, playerSideTargets.Count, "playerSide=true 应只返回同阵营敌人");
            Assert.AreEqual(1, playerSideTargets[0].Id, "应返回敌人 1（isPlayerLane=true）");

            // playerSide=false → 查询 isPlayerLane=false 的敌人。
            List<EnemyTargetDto> opponentSideTargets = resolver.QueryTargets(
                mgr, 40f, 40f, 100f, playerSide: false, CellWidth, CellHeight);
            Assert.AreEqual(1, opponentSideTargets.Count, "playerSide=false 应只返回同阵营敌人");
            Assert.AreEqual(2, opponentSideTargets[0].Id, "应返回敌人 2（isPlayerLane=false）");
        }
    }
}
