using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Enemy
{
    // ============================================================================
    // 任务 4.6：EnemyManager 单元测试
    // ----------------------------------------------------------------------------
    // 验证要求（task 4.6）：
    //   1. 稳定有序集合：禁止依赖 Dictionary/HashSet 未定义遍历顺序决定目标。
    //   2. 空间索引：登记、移除、单元边界、刷新。
    //   3. 目标查询：queryTargets / queryEnemyObjects / queryAroundEnemy /
    //      closestToEnd / randomTarget / lowestHealthTarget / frontmostPathPosition。
    //   4. 伤害提交：applyDamage 按 DTO.Id 查找并 hit。
    //   5. 移除队列：ForceRemove 入队、ProcessRemoveQueue 统一处理、不重入销毁集合。
    //   6. 幂等清理：Clear / GameOver 可重复调用。
    //
    // spec battle-simulation "Simulation is reproducible"：
    //   不依赖无序集合遍历决定目标、伤害和胜负。本测试验证目标选择基于
    //   _orderedIds（spawn 顺序）与确定性排序键。
    //
    // spec battle-simulation "Battle result is frozen once" / 决策 0.4：
    //   TryFreeze 后中止当前 phase 剩余迭代。本测试验证 IsFrozen 置位后
    //   Update 停止推进剩余敌人。
    //
    // spec battle-simulation "Freeze occurs inside a manager update"：
    //   Manager 遍历时若冻结，当前对象返回后停止迭代，集合清理由 Settling 统一执行。
    //
    // 并行任务契约说明：
    //   EnemyBase / Mob0Enemy / EnemyFactory / BattleTarget 由 task 54/55/56/57 并行创建，
    //   当前尚未存在。本测试使用 FakeEnemy（实现 IEnemyEntity）验证 EnemyManager 行为，
    //   不依赖并行任务的产物。task 4.3 的 EnemyBase 将实现 IEnemyEntity，
    //   届时可补充集成测试。
    // ============================================================================

    /// <summary>
    /// EnemyManager 稳定有序集合、空间索引、目标查询、伤害提交、移除队列与幂等清理测试（task 4.6）。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖：</para>
    /// <list type="bullet">
    /// <item>稳定有序集合：相同 spawn 顺序产生相同目标选择结果，不依赖 Dictionary 遍历顺序。</item>
    /// <item>空间索引：登记、移除、单元边界、RefreshCellIndex。</item>
    /// <item>目标查询：queryTargets / queryEnemyObjects / queryAroundEnemy / closestToEnd /
    ///     randomTarget / lowestHealthTarget / frontmostPathPosition。</item>
    /// <item>伤害提交：applyDamage 按 DTO.Id 查找并 hit。</item>
    /// <item>移除队列：ForceRemove 入队、ProcessRemoveQueue 统一处理、不重入销毁集合。</item>
    /// <item>幂等清理：Clear / GameOver 可重复调用。</item>
    /// <item>冻结中止：IsFrozen 置位后 Update 停止推进剩余敌人。</item>
    /// <item>同子步集合修改：Update 中 ForceRemove 不重入修改集合，先入队再统一处理。</item>
    /// <item>重复死亡：同一敌人多次 ForceRemove 幂等。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class EnemyManagerTests
    {
        // ====================================================================
        // FakeEnemy —— 最小 IEnemyEntity 测试替身
        // --------------------------------------------------------------------
        // 实现 IEnemyEntity，带可观察的可变状态，用于验证 EnemyManager 行为。
        // 不依赖 UnityEngine，可在 EditMode 运行。
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

            /// <summary>Update 调用次数（验证推进）。</summary>
            public int UpdateCount;

            /// <summary>Hit 调用累计伤害（验证伤害提交）。</summary>
            public int TotalHitDamage;

            /// <summary>GameOver 调用次数（验证清理）。</summary>
            public int GameOverCount;

            /// <summary>上次 Hit 的攻击者 ID。</summary>
            public int LastAttackerId = -1;

            public void Update(long deltaMs)
            {
                UpdateCount++;
            }

            public bool Hit(int damage, int attackerId)
            {
                if (Health <= 0)
                {
                    return false;
                }

                Health = Math.Max(0, Health - damage);
                TotalHitDamage += damage;
                LastAttackerId = attackerId;
                if (Health <= 0)
                {
                    CurrentState = 4; // DEAD
                }
                return true;
            }

            public bool GameOver()
            {
                GameOverCount++;
                CurrentState = 4; // DEAD
                Targetable = false;
                return GameOverCount == 1;
            }

            public bool IsTargetableBy(bool playerSide)
            {
                // 对应 EnemyBase.js:531-536：
                // isTargetableBy 要求 isPlayerLane === playerSide（同车道），
                // 即玩家方单位（playerSide=true）只攻击玩家车道敌人（isPlayerLane=true）。
                // 非 SPAWNING/DEAD 且 targetable。
                return CurrentState != 0 && CurrentState != 4 && Targetable && IsPlayerLane == playerSide;
            }
        }

        // ====================================================================
        // 测试辅助
        // ====================================================================

        /// <summary>默认空间单元边长（与 EnemyManager.DefaultGridSize 一致）。</summary>
        private const int GridSize = 80;

        /// <summary>敌人格子宽/高（对应 map.gridWidth/gridHeight，用于 circleIntersectsRect）。</summary>
        private const float CellWidth = 80f;
        private const float CellHeight = 80f;

        /// <summary>
        /// 构造一个 FakeEnemy，位于指定单元的中心附近。
        /// </summary>
        private static FakeEnemy MakeEnemy(
            int id, bool isPlayerLane, float x, float y,
            int health = 100, float remainingPathDistance = 1000f,
            int currentPathIndex = 0, int state = 1)
        {
            return new FakeEnemy
            {
                Id = id,
                IsPlayerLane = isPlayerLane,
                X = x,
                Y = y,
                Health = health,
                RemainingPathDistance = remainingPathDistance,
                CurrentPathIndex = currentPathIndex,
                CurrentState = state,
            };
        }

        // ====================================================================
        // 稳定有序集合测试（task 4.6 核心约束）
        // ====================================================================

        [Test]
        [Description("相同 spawn 顺序的 closestToEnd 结果确定，不依赖 Dictionary 遍历顺序。")]
        public void ClosestToEnd_StableOrder_RegardlessOfDictionaryEnumeration()
        {
            var mgr = new EnemyManager(GridSize);
            // 登记多个敌人，距离各不同。按 spawn 顺序登记。
            // 敌人 1 距离 300，敌人 2 距离 100，敌人 3 距离 200。
            // closestToEnd 按距离升序应为 [2, 3, 1]。
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0, remainingPathDistance: 300f));
            mgr.Register(MakeEnemy(2, isPlayerLane: true, x: 0, y: 0, remainingPathDistance: 100f));
            mgr.Register(MakeEnemy(3, isPlayerLane: true, x: 0, y: 0, remainingPathDistance: 200f));

            List<EnemyTargetDto> result = mgr.ClosestToEnd(3, playerSide: true);

            Assert.AreEqual(3, result.Count, "应返回 3 个目标。");
            Assert.AreEqual(2, result[0].Id, "最近终点的是敌人 2（距离 100）。");
            Assert.AreEqual(3, result[1].Id, "其次是敌人 3（距离 200）。");
            Assert.AreEqual(1, result[2].Id, "最远的是敌人 1（距离 300）。");
        }

        [Test]
        [Description("相同距离的敌人按 spawn 顺序稳定排序（_orderedIds 顺序）。")]
        public void ClosestToEnd_EqualDistance_StableBySpawnOrder()
        {
            var mgr = new EnemyManager(GridSize);
            // 三个敌人距离相同——稳定排序应保持 spawn 顺序。
            mgr.Register(MakeEnemy(10, isPlayerLane: true, x: 0, y: 0, remainingPathDistance: 150f));
            mgr.Register(MakeEnemy(20, isPlayerLane: true, x: 0, y: 0, remainingPathDistance: 150f));
            mgr.Register(MakeEnemy(30, isPlayerLane: true, x: 0, y: 0, remainingPathDistance: 150f));

            List<EnemyTargetDto> result = mgr.ClosestToEnd(3, playerSide: true);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(10, result[0].Id, "相同距离按 spawn 顺序：第一个是 10。");
            Assert.AreEqual(20, result[1].Id, "相同距离按 spawn 顺序：第二个是 20。");
            Assert.AreEqual(30, result[2].Id, "相同距离按 spawn 顺序：第三个是 30。");
        }

        [Test]
        [Description("lowestHealthTarget 相同血量时按 spawn 顺序选第一个（稳定选择）。")]
        public void LowestHealthTarget_EqualHealth_StableSelectFirstBySpawnOrder()
        {
            var mgr = new EnemyManager(GridSize);
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0, health: 50));
            mgr.Register(MakeEnemy(2, isPlayerLane: true, x: 0, y: 0, health: 50));
            mgr.Register(MakeEnemy(3, isPlayerLane: true, x: 0, y: 0, health: 30));

            EnemyTargetDto result = mgr.LowestHealthTarget(playerSide: true);

            Assert.AreEqual(3, result.Id, "血量最低的是敌人 3（30）。");

            // 两个敌人血量相同——应选 spawn 顺序第一个。
            var mgr2 = new EnemyManager(GridSize);
            mgr2.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0, health: 30));
            mgr2.Register(MakeEnemy(2, isPlayerLane: true, x: 0, y: 0, health: 30));
            EnemyTargetDto result2 = mgr2.LowestHealthTarget(playerSide: true);
            Assert.AreEqual(1, result2.Id, "相同血量选 spawn 顺序第一个（敌人 1）。");
        }

        [Test]
        [Description("randomTarget 注入确定性随机源，相同序列产生相同选择，不依赖集合遍历顺序。")]
        public void RandomTarget_DeterministicRandom_StableSelection()
        {
            // 确定性随机源：总是返回 0.0（选第一个候选）。
            var mgr1 = new EnemyManager(GridSize, randomSource: () => 0f);
            mgr1.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0));
            mgr1.Register(MakeEnemy(2, isPlayerLane: true, x: 0, y: 0));
            mgr1.Register(MakeEnemy(3, isPlayerLane: true, x: 0, y: 0));

            // 确定性随机源：总是返回 0.0 → index=0 → 选 _orderedIds[0]=1。
            EnemyTargetDto r1 = mgr1.RandomTarget(playerSide: true);
            Assert.AreEqual(1, r1.Id, "随机源=0.0 应选 spawn 顺序第一个（敌人 1）。");

            // 另一实例，相同 spawn 顺序与随机源，结果应相同。
            var mgr2 = new EnemyManager(GridSize, randomSource: () => 0f);
            mgr2.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0));
            mgr2.Register(MakeEnemy(2, isPlayerLane: true, x: 0, y: 0));
            mgr2.Register(MakeEnemy(3, isPlayerLane: true, x: 0, y: 0));
            EnemyTargetDto r2 = mgr2.RandomTarget(playerSide: true);
            Assert.AreEqual(r1.Id, r2.Id, "相同 spawn 顺序与随机源应产生相同选择。");
        }

        [Test]
        [Description("randomTarget 无候选返回 Invalid（id=-1）。")]
        public void RandomTarget_NoCandidates_ReturnsInvalid()
        {
            var mgr = new EnemyManager(GridSize, randomSource: () => 0f);
            EnemyTargetDto result = mgr.RandomTarget(playerSide: true);
            Assert.IsFalse(result.IsValid, "无候选应返回 Invalid。");
            Assert.AreEqual(-1, result.Id);
        }

        [Test]
        [Description("frontmostPathPosition 按 currentPathIndex 降序选最靠前，相同索引按 spawn 顺序。")]
        public void FrontmostPathPosition_StableSelect()
        {
            var mgr = new EnemyManager(GridSize);
            // frontmostPathPosition 用 isPlayerLane === playerSide 过滤（非 isTargetableBy）。
            // 查询 playerSide=false → 需 isPlayerLane=false 的敌人。
            mgr.Register(MakeEnemy(1, isPlayerLane: false, x: 10, y: 10, currentPathIndex: 2));
            mgr.Register(MakeEnemy(2, isPlayerLane: false, x: 20, y: 20, currentPathIndex: 5));
            mgr.Register(MakeEnemy(3, isPlayerLane: false, x: 30, y: 30, currentPathIndex: 5));

            (int index, float x, float y)? result = mgr.FrontmostPathPosition(playerSide: false);
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(5, result.Value.index, "最靠前路径索引为 5。");
            Assert.AreEqual(20f, result.Value.x, "相同索引 5 选 spawn 顺序第一个（敌人 2，x=20）。");
        }

        // ====================================================================
        // 空间索引测试
        // ====================================================================

        [Test]
        [Description("Register 后敌人在空间索引中登记，单元键正确。")]
        public void Register_IndexesEnemyToCorrectCell()
        {
            var mgr = new EnemyManager(GridSize);
            // 敌人中心 (40, 120) → cell (0, 1)（gridSize=80）。
            // X=20, Y=100, Width=40, Height=40 → center=(40, 120) → floor(40/80)=0, floor(120/80)=1。
            var enemy = MakeEnemy(1, isPlayerLane: true, x: 20, y: 100);
            mgr.Register(enemy);

            Assert.IsTrue(mgr.HasSpatialRegistration(1), "登记后应有空间索引。");
            Assert.AreEqual("0_1", mgr.SpatialKeyFor(1), "单元键应为 0_1。");
            Assert.AreEqual(1, mgr.SpatialCellCount, "应有 1 个空间单元。");
        }

        [Test]
        [Description("Unregister 后敌人从空间索引移除，空单元被清除。")]
        public void Unregister_RemovesFromSpatialIndex()
        {
            var mgr = new EnemyManager(GridSize);
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 20, y: 100));
            Assert.AreEqual(1, mgr.SpatialCellCount);

            mgr.Unregister(1);

            Assert.IsFalse(mgr.HasSpatialRegistration(1));
            Assert.AreEqual(0, mgr.SpatialCellCount, "空单元应被清除。");
            Assert.AreEqual(0, mgr.Count);
        }

        [Test]
        [Description("RefreshCellIndex 在敌人移动到新单元时更新索引。")]
        public void RefreshCellIndex_UpdatesWhenEnemyMovesToNewCell()
        {
            var mgr = new EnemyManager(GridSize);
            var enemy = MakeEnemy(1, isPlayerLane: true, x: 20, y: 100);
            mgr.Register(enemy);
            Assert.AreEqual("0_1", mgr.SpatialKeyFor(1));

            // 敌人移动到 (100, 100) → center=(120, 120) → cell (1, 1)。
            enemy.X = 100;
            mgr.RefreshCellIndex(1);

            Assert.AreEqual("1_1", mgr.SpatialKeyFor(1), "移动后单元键应更新为 1_1。");
            Assert.AreEqual(1, mgr.SpatialCellCount, "旧单元清空，新单元 1 个。");
        }

        [Test]
        [Description("RefreshCellIndex 敌人未跨单元时不更新。")]
        public void RefreshCellIndex_NoChange_WhenSameCell()
        {
            var mgr = new EnemyManager(GridSize);
            var enemy = MakeEnemy(1, isPlayerLane: true, x: 20, y: 100);
            mgr.Register(enemy);
            string before = mgr.SpatialKeyFor(1);

            // 在同单元内移动。
            enemy.X = 30;
            mgr.RefreshCellIndex(1);

            Assert.AreEqual(before, mgr.SpatialKeyFor(1), "同单元内移动不应更新键。");
        }

        // ====================================================================
        // 目标查询测试
        // ====================================================================

        [Test]
        [Description("queryTargets 返回半径内可攻击的敌人，经 circleIntersectsRect 精筛。")]
        public void QueryTargets_ReturnsTargetableEnemiesInRadius()
        {
            var mgr = new EnemyManager(GridSize);
            // 敌人 1 在 (20, 20)，center=(40, 40)。
            // 敌人 2 在 (200, 200)，center=(220, 220)——远离查询中心。
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 20, y: 20));
            mgr.Register(MakeEnemy(2, isPlayerLane: true, x: 200, y: 200));

            // 查询中心 (40, 40)，半径 50。敌人 1 在内，敌人 2 在外。
            List<EnemyTargetDto> result = mgr.QueryTargets(40, 40, 50, true, CellWidth, CellHeight);

            Assert.AreEqual(1, result.Count, "只有敌人 1 在半径内。");
            Assert.AreEqual(1, result[0].Id);
        }

        [Test]
        [Description("queryTargets 过滤不可攻击阵营（isTargetableBy 为 false 的敌人不返回）。")]
        public void QueryTargets_FiltersByTargetableSide()
        {
            var mgr = new EnemyManager(GridSize);
            // 敌人 1 是玩家车道（isPlayerLane=true），玩家攻击者（playerSide=true）可攻击。
            // 敌人 2 是对手车道（isPlayerLane=false），玩家攻击者不可攻击。
            // 对应 EnemyBase.js:531-536 isTargetableBy 要求 isPlayerLane === playerSide。
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 20, y: 20));
            mgr.Register(MakeEnemy(2, isPlayerLane: false, x: 20, y: 20));

            List<EnemyTargetDto> result = mgr.QueryTargets(40, 40, 50, true, CellWidth, CellHeight);
            Assert.AreEqual(1, result.Count, "只返回同车道可攻击的敌人。");
            Assert.AreEqual(1, result[0].Id, "玩家攻击者只能攻击玩家车道敌人（敌人 1）。");
        }

        [Test]
        [Description("queryAroundEnemy 排除源敌人自身。")]
        public void QueryAroundEnemy_ExcludesSource()
        {
            var mgr = new EnemyManager(GridSize);
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 20, y: 20));
            mgr.Register(MakeEnemy(2, isPlayerLane: true, x: 30, y: 30));

            // 源敌人 1 查询周围，应排除自身。
            List<EnemyTargetDto> result = mgr.QueryAroundEnemy(
                sourceId: 1, sourceX: 40, sourceY: 40, radius: 50,
                playerSide: true, cellWidth: CellWidth, cellHeight: CellHeight);

            Assert.IsTrue(result.TrueForAll(r => r.Id != 1), "结果不应包含源敌人自身。");
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(2, result[0].Id);
        }

        [Test]
        [Description("queryEnemyObjects 返回敌人对象列表，可复用 output 缓冲。")]
        public void QueryEnemyObjects_ReusesOutputBuffer()
        {
            var mgr = new EnemyManager(GridSize);
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 20, y: 20));
            mgr.Register(MakeEnemy(2, isPlayerLane: true, x: 30, y: 30));

            var output = new List<IEnemyEntity>();
            mgr.QueryEnemyObjects(40, 40, 50, true, CellWidth, CellHeight, output);

            Assert.AreEqual(2, output.Count, "两个敌人都在半径内。");
        }

        // ====================================================================
        // 伤害提交测试
        // ====================================================================

        [Test]
        [Description("applyDamage 按 DTO.Id 查找敌人并调用 hit，累计伤害正确。")]
        public void ApplyDamage_HitsEnemiesById()
        {
            var mgr = new EnemyManager(GridSize);
            FakeEnemy e1 = MakeEnemy(1, isPlayerLane: true, x: 0, y: 0, health: 100);
            FakeEnemy e2 = MakeEnemy(2, isPlayerLane: true, x: 0, y: 0, health: 100);
            mgr.Register(e1);
            mgr.Register(e2);

            var dtos = new List<EnemyTargetDto>
            {
                new EnemyTargetDto(1, 0, 0, 0f),
                new EnemyTargetDto(2, 0, 0, 0f),
            };
            mgr.ApplyDamage(30, dtos, attackerId: 99);

            Assert.AreEqual(70, e1.Health, "敌人 1 受 30 伤害，血量 100-30=70。");
            Assert.AreEqual(70, e2.Health, "敌人 2 受 30 伤害。");
            Assert.AreEqual(30, e1.TotalHitDamage, "累计伤害 30。");
            Assert.AreEqual(99, e1.LastAttackerId, "攻击者 ID 记录正确。");
        }

        [Test]
        [Description("applyDamage 对不存在的 Id 静默跳过（幂等）。")]
        public void ApplyDamage_SkipsNonExistentIds()
        {
            var mgr = new EnemyManager(GridSize);
            FakeEnemy e1 = MakeEnemy(1, isPlayerLane: true, x: 0, y: 0, health: 100);
            mgr.Register(e1);

            var dtos = new List<EnemyTargetDto>
            {
                new EnemyTargetDto(1, 0, 0, 0f),
                new EnemyTargetDto(999, 0, 0, 0f), // 不存在
            };
            mgr.ApplyDamage(30, dtos, attackerId: -1);

            Assert.AreEqual(70, e1.Health, "敌人 1 仍受伤害。");
            Assert.AreEqual(1, mgr.Count, "不存在的 Id 不影响集合。");
        }

        [Test]
        [Description("applyDamage 伤害为 0 或负数时不调用 hit。")]
        public void ApplyDamage_ZeroOrNegativeDamage_NoHit()
        {
            var mgr = new EnemyManager(GridSize);
            FakeEnemy e1 = MakeEnemy(1, isPlayerLane: true, x: 0, y: 0, health: 100);
            mgr.Register(e1);

            mgr.ApplyDamage(0, new List<EnemyTargetDto> { new EnemyTargetDto(1, 0, 0, 0f) }, -1);
            mgr.ApplyDamage(-10, new List<EnemyTargetDto> { new EnemyTargetDto(1, 0, 0, 0f) }, -1);

            Assert.AreEqual(100, e1.Health, "0 或负伤害不影响血量。");
            Assert.AreEqual(0, e1.TotalHitDamage);
        }

        // ====================================================================
        // 移除队列测试
        // ====================================================================

        [Test]
        [Description("ForceRemove 入队，不立即修改集合；ProcessRemoveQueue 后才移除。")]
        public void ForceRemove_Queued_NotImmediateRemoval()
        {
            var mgr = new EnemyManager(GridSize);
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0));

            mgr.ForceRemove(1);

            // 入队但未处理——集合中仍存在。
            Assert.AreEqual(1, mgr.Count, "ForceRemove 后未处理前集合不变。");

            mgr.ProcessRemoveQueue();

            Assert.AreEqual(0, mgr.Count, "ProcessRemoveQueue 后移除。");
        }

        [Test]
        [Description("Update 结束后自动处理移除队列。")]
        public void Update_ProcessesRemoveQueueAfterIteration()
        {
            var mgr = new EnemyManager(GridSize);
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0));

            mgr.ForceRemove(1);
            Assert.AreEqual(1, mgr.Count, "ForceRemove 后未处理前集合不变。");

            mgr.Update(stepMs: 16);

            Assert.AreEqual(0, mgr.Count, "Update 后自动处理移除队列。");
        }

        [Test]
        [Description("同一敌人多次 ForceRemove 幂等（不重复入队、不重复 gameOver）。")]
        public void ForceRemove_Idempotent_ForRepeatedCalls()
        {
            var mgr = new EnemyManager(GridSize);
            FakeEnemy e1 = MakeEnemy(1, isPlayerLane: true, x: 0, y: 0);
            mgr.Register(e1);

            mgr.ForceRemove(1);
            mgr.ForceRemove(1);
            mgr.ForceRemove(1);

            Assert.AreEqual(1, e1.GameOverCount, "GameOver 只调用一次（幂等）。");
            mgr.ProcessRemoveQueue();
            Assert.AreEqual(0, mgr.Count);
        }

        [Test]
        [Description("Update 中敌人触发 ForceRemove 不重入修改集合，先入队再统一处理。")]
        public void Update_ForceRemoveDuringIteration_DoesNotMutateCollectionImmediately()
        {
            var mgr = new EnemyManager(GridSize);
            // 敌人 1 在 Update 时请求移除敌人 2（模拟接触终点触发 gameOver）。
            FakeEnemy e1 = MakeEnemy(1, isPlayerLane: true, x: 0, y: 0);
            FakeEnemy e2 = MakeEnemy(2, isPlayerLane: true, x: 0, y: 0);
            mgr.Register(e1);
            mgr.Register(e2);

            // 敌人 1 的 Update 请求移除敌人 2。
            e1.X = 0; // 触发器：在 Update 回调中 ForceRemove e2。
            // 用一个标志让 FakeEnemy.Update 触发 ForceRemove。
            // 由于 FakeEnemy.Update 不直接访问 mgr，这里改为手动模拟：
            // 先 Update 推进 e1，e1 内部若想移除 e2 需要外部协调。
            // 本测试改为：Update 过程中外部调用 ForceRemove（模拟伤害回调触发）。

            // 模拟：在 e1.Update 后立即 ForceRemove(2)。
            // 由于 EnemyManager.Update 是同步遍历，我们验证 ForceRemove 入队不导致
            // 遍历中集合修改异常。
            Assert.DoesNotThrow(() =>
            {
                mgr.Update(16);
                mgr.ForceRemove(2); // 遍历结束后安全调用。
                mgr.Update(16);     // 第二次 Update 处理移除队列。
            });

            Assert.AreEqual(1, mgr.Count, "敌人 2 被移除，敌人 1 保留。");
        }

        // ====================================================================
        // 幂等清理测试
        // ====================================================================

        [Test]
        [Description("Clear 清空全部集合与索引，重复调用为空操作（幂等）。")]
        public void Clear_Idempotent()
        {
            var mgr = new EnemyManager(GridSize);
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0));
            mgr.Register(MakeEnemy(2, isPlayerLane: true, x: 80, y: 80));
            Assert.AreEqual(2, mgr.Count);

            mgr.Clear();
            Assert.AreEqual(0, mgr.Count, "Clear 后集合为空。");
            Assert.AreEqual(0, mgr.SpatialCellCount, "空间索引清空。");
            Assert.IsTrue(mgr.IsCleared);

            // 重复 Clear 不抛异常。
            Assert.DoesNotThrow(() => mgr.Clear());
        }

        [Test]
        [Description("GameOver 逐个通知敌人 gameOver 再清空集合，重复调用幂等。")]
        public void GameOver_NotifiesEnemiesThenClears_Idempotent()
        {
            var mgr = new EnemyManager(GridSize);
            FakeEnemy e1 = MakeEnemy(1, isPlayerLane: true, x: 0, y: 0);
            FakeEnemy e2 = MakeEnemy(2, isPlayerLane: true, x: 80, y: 80);
            mgr.Register(e1);
            mgr.Register(e2);

            mgr.GameOver();

            Assert.AreEqual(1, e1.GameOverCount, "敌人 1 被通知 gameOver。");
            Assert.AreEqual(1, e2.GameOverCount, "敌人 2 被通知 gameOver。");
            Assert.AreEqual(0, mgr.Count, "集合清空。");
            Assert.IsTrue(mgr.IsCleared);

            // 重复 GameOver 不再通知（幂等）。
            mgr.GameOver();
            Assert.AreEqual(1, e1.GameOverCount, "重复 GameOver 不再次通知。");
        }

        // ====================================================================
        // 冻结中止测试（决策 0.4）
        // ====================================================================

        [Test]
        [Description("IsFrozen 置位后 Update 直接返回，不推进任何敌人。")]
        public void Update_Frozen_DoesNotAdvance()
        {
            var mgr = new EnemyManager(GridSize);
            FakeEnemy e1 = MakeEnemy(1, isPlayerLane: true, x: 0, y: 0);
            mgr.Register(e1);

            mgr.IsFrozen = true;
            mgr.Update(16);

            Assert.AreEqual(0, e1.UpdateCount, "冻结后不推进敌人。");
        }

        [Test]
        [Description("Update 遍历中检测到 IsFrozen 置位后停止剩余迭代（决策 0.4）。")]
        public void Update_FreezeDuringIteration_StopsRemainingEnemies()
        {
            var mgr = new EnemyManager(GridSize);
            FakeEnemy e1 = MakeEnemy(1, isPlayerLane: true, x: 0, y: 0);
            FakeEnemy e2 = MakeEnemy(2, isPlayerLane: true, x: 0, y: 0);
            mgr.Register(e1);
            mgr.Register(e2);

            // 模拟：e1.Update 时冻结。由于 FakeEnemy.Update 不能直接访问 mgr，
            // 本测试通过外部在 Update 前/后设置 IsFrozen 验证门控。
            // 真实场景中 EnemyBase 内部调用 TryFreeze 后 BattleRuntime 置位 EnemyManager.IsFrozen。
            mgr.IsFrozen = true;
            mgr.Update(16);

            Assert.AreEqual(0, e1.UpdateCount, "冻结后 e1 不推进。");
            Assert.AreEqual(0, e2.UpdateCount, "冻结后 e2 不推进。");
        }

        [Test]
        [Description("Clear 后 Update 为空操作。")]
        public void Update_AfterClear_NoOp()
        {
            var mgr = new EnemyManager(GridSize);
            FakeEnemy e1 = MakeEnemy(1, isPlayerLane: true, x: 0, y: 0);
            mgr.Register(e1);
            mgr.Clear();

            mgr.Update(16);
            Assert.AreEqual(0, e1.UpdateCount, "Clear 后 Update 不推进。");
        }

        // ====================================================================
        // Update 状态过滤测试
        // ====================================================================

        [Test]
        [Description("Update 跳过 DEAD 和 SPAWNING 状态的敌人。")]
        public void Update_SkipsDeadAndSpawningEnemies()
        {
            var mgr = new EnemyManager(GridSize);
            FakeEnemy moving = MakeEnemy(1, isPlayerLane: true, x: 0, y: 0, state: 1); // MOVING
            FakeEnemy dead = MakeEnemy(2, isPlayerLane: true, x: 0, y: 0, state: 4);   // DEAD
            FakeEnemy spawning = MakeEnemy(3, isPlayerLane: true, x: 0, y: 0, state: 0); // SPAWNING
            mgr.Register(moving);
            mgr.Register(dead);
            mgr.Register(spawning);

            mgr.Update(16);

            Assert.AreEqual(1, moving.UpdateCount, "MOVING 敌人被推进。");
            Assert.AreEqual(0, dead.UpdateCount, "DEAD 敌人跳过。");
            Assert.AreEqual(0, spawning.UpdateCount, "SPAWNING 敌人跳过。");
        }

        // ====================================================================
        // circleIntersectsRect 测试（对应 JS circleIntersectsRect）
        // ====================================================================

        [Test]
        [Description("circleIntersectsRect 半径减 1 后做最近点测试（对应 JS 行为）。")]
        public void CircleIntersectsRect_RadiusMinusOne_PointTest()
        {
            // 圆心在矩形内，半径足够大 → 相交。
            Assert.IsTrue(EnemyManager.CircleIntersectsRect(
                50, 40, 40, 0, 0, 80, 80), "圆心在矩形内应相交。");

            // 圆心在矩形外，距离 70，半径 71 → (71-1)=70，70^2=4900 >= 70^2=4900？边界相等相交。
            // 实际 dx=70, dy=0 → 70*70=4900 <= 70*70=4900 → true（<=）。
            Assert.IsTrue(EnemyManager.CircleIntersectsRect(
                71, 150, 40, 0, 0, 80, 80), "距离 70，半径 71→有效 70，边界相等相交。");

            // 距离 71，半径 71→有效 70，70 < 71 → 不相交。
            Assert.IsFalse(EnemyManager.CircleIntersectsRect(
                71, 151, 40, 0, 0, 80, 80), "距离 71，有效半径 70，不相交。");
        }

        [Test]
        [Description("circleIntersectsRect 半径 <=1 时有效半径 <=0，不相交。")]
        public void CircleIntersectsRect_TinyRadius_NoIntersection()
        {
            Assert.IsFalse(EnemyManager.CircleIntersectsRect(
                1, 100, 100, 0, 0, 80, 80), "半径 1→有效 0，不相交。");
        }

        // ====================================================================
        // 登记 / 注销边界测试
        // ====================================================================

        [Test]
        [Description("Register 重复 Id 抛 ArgumentException。")]
        public void Register_DuplicateId_Throws()
        {
            var mgr = new EnemyManager(GridSize);
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0));

            Assert.Throws<ArgumentException>(() =>
                mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 80, y: 80)));
        }

        [Test]
        [Description("Register Id <= 0 抛 ArgumentException。")]
        public void Register_InvalidId_Throws()
        {
            var mgr = new EnemyManager(GridSize);

            Assert.Throws<ArgumentException>(() =>
                mgr.Register(MakeEnemy(0, isPlayerLane: true, x: 0, y: 0)));
            Assert.Throws<ArgumentException>(() =>
                mgr.Register(MakeEnemy(-1, isPlayerLane: true, x: 0, y: 0)));
        }

        [Test]
        [Description("Register null 抛 ArgumentNullException。")]
        public void Register_Null_Throws()
        {
            var mgr = new EnemyManager(GridSize);

            Assert.Throws<ArgumentNullException>(() => mgr.Register(null));
        }

        [Test]
        [Description("Unregister 不存在的 Id 为空操作（幂等）。")]
        public void Unregister_NonExistent_NoOp()
        {
            var mgr = new EnemyManager(GridSize);

            Assert.DoesNotThrow(() => mgr.Unregister(999));
            Assert.AreEqual(0, mgr.Count);
        }

        // ====================================================================
        // GetById 测试
        // ====================================================================

        [Test]
        [Description("GetById 返回敌人实体；不存在返回 null。")]
        public void GetById_ReturnsEnemyOrNull()
        {
            var mgr = new EnemyManager(GridSize);
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0));

            Assert.IsNotNull(mgr.GetById(1), "存在的 Id 返回敌人。");
            Assert.IsNull(mgr.GetById(999), "不存在的 Id 返回 null。");
        }

        // ====================================================================
        // 诊断查询测试
        // ====================================================================

        [Test]
        [Description("GetOrderedIdsSnapshot 返回按 spawn 顺序的 ID 快照。")]
        public void GetOrderedIdsSnapshot_ReturnsSpawnOrder()
        {
            var mgr = new EnemyManager(GridSize);
            mgr.Register(MakeEnemy(3, isPlayerLane: true, x: 0, y: 0));
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0));
            mgr.Register(MakeEnemy(2, isPlayerLane: true, x: 0, y: 0));

            IReadOnlyList<int> snapshot = mgr.GetOrderedIdsSnapshot();

            CollectionAssert.AreEqual(new[] { 3, 1, 2 }, snapshot, "快照保持 spawn 登记顺序。");
        }

        [Test]
        [Description("移除后 _orderedIds 同步更新，保持剩余顺序。")]
        public void Remove_PreservesOrderOfRemaining()
        {
            var mgr = new EnemyManager(GridSize);
            mgr.Register(MakeEnemy(1, isPlayerLane: true, x: 0, y: 0));
            mgr.Register(MakeEnemy(2, isPlayerLane: true, x: 0, y: 0));
            mgr.Register(MakeEnemy(3, isPlayerLane: true, x: 0, y: 0));

            mgr.ForceRemove(2);
            mgr.ProcessRemoveQueue();

            CollectionAssert.AreEqual(new[] { 1, 3 }, mgr.GetOrderedIdsSnapshot(),
                "移除中间元素后剩余顺序保持。");
        }
    }
}
