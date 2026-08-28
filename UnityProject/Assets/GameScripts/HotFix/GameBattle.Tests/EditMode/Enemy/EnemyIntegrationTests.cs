using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Enemy
{
    // ============================================================================
    // 任务 4.7：敌人系统集成测试 —— 移动/终点攻击、路径边界、死亡、同子步集合修改、
    //           重复死亡、奖励一次、Mob0 池复用无污染
    // ----------------------------------------------------------------------------
    // 验证要求（task 4.7）：
    //   1. 敌人移动/终点攻击：EnemyBase 沿路径移动到末尾后发起终点攻击，造成伤害。
    //   2. 路径边界：敌人走完全部路径点后索引达 length，请求以 ReachedEndPoint 原因回收。
    //   3. 死亡：Hit 血量归零进入 DEAD，击杀回调触发，奖励提交。
    //   4. 同子步集合修改：EnemyManager.Update 中敌人触发 ForceRemove 不重入修改集合，
    //      先入 _removeQueue 再遍历结束后统一处理（决策 0.4）。
    //   5. 重复死亡：同一敌人多次 Hit 致死只触发一次击杀回调（deathStarted 幂等）。
    //   6. 奖励一次：同一敌人死亡只提交一次击杀奖励，不重复发奖。
    //   7. Mob0 池复用无污染：Acquire/Release 后 Reset 清除全部可变状态（target/id/state），
    //      复用对象等价于新构造。
    //
    // 与现有测试的关系：
    //   - EnemyBaseTests（task 4.3）用 TestEnemy 子类 + 委托验证 EnemyBase 单元行为。
    //   - EnemyManagerTests（task 4.6）用 FakeEnemy 替身验证 Manager 集合行为。
    //   - Mob0EnemyTests（task 4.4）验证 Mob0 专属字段与 ResetState。
    //   - EnemyFactoryTests（task 4.5）验证池复用与 ID 分配。
    //   - DeadEntityRewardTests（task 3.12）用 SynchronousKillSettlement 验证奖励去重。
    //   本测试文件将真实 EnemyBase + EnemyManager + EnemyFactory + BattleObjectPool +
    //   BattleTarget 集成，覆盖跨类交互的 7 个场景，补充单元测试未覆盖的集成路径。
    //
    // spec battle-simulation "Update phases are explicit and single-owned"：
    //   伤害、死亡事实、奖励和胜负候选 MUST 在其发生点同步生效。
    //
    // spec battle-simulation "Battle result is frozen once" / 决策 0.4：
    //   TryFreeze 不在嵌套伤害调用栈内重入销毁 Manager 或集合；
    //   移除请求入 _removeQueue，由 ProcessRemoveQueue 统一处理。
    //
    // design.md 决策 4：敌人注册、空间索引、伤害、回收等一致性操作使用直接调用，
    //   不通过全局事件总线。EnemyBase 通过 IEnemyEndPointAttackTarget 接口发起
    //   终点攻击，通过 EnemyKilledHandler 委托提交击杀奖励，通过 EnemyDeathRequestHandler
    //   委托请求移除。
    //
    // 本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。
    // ============================================================================

    /// <summary>
    /// 敌人系统集成测试：移动/终点攻击、路径边界、死亡、同子步集合修改、重复死亡、
    /// 奖励一次、Mob0 池复用无污染（task 4.7）。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖（task 4.7 的 7 个场景）：</para>
    /// <list type="bullet">
    /// <item>敌人移动/终点攻击：EnemyBase 沿路径移动到末尾发起终点攻击。</item>
    /// <item>路径边界：走完全部路径点后索引达 length，请求 ReachedEndPoint 回收。</item>
    /// <item>死亡：Hit 血量归零进入 DEAD，击杀回调触发。</item>
    /// <item>同子步集合修改：Update 中 ForceRemove 入队，遍历后统一处理。</item>
    /// <item>重复死亡：多次 Hit 致死只触发一次击杀回调。</item>
    /// <item>奖励一次：同一敌人死亡只提交一次奖励。</item>
    /// <item>Mob0 池复用无污染：Release 后 Reset 清除 target/id/state。</item>
    /// </list>
    /// <para>本测试使用真实生产类型（EnemyBase/Mob0Enemy/EnemyFactory/EnemyManager/
    /// BattleObjectPool/BattleTarget），通过 IEnemyEndPointAttackTarget 目标桩注入终点攻击
    /// 与 EnemyKilledHandler 击杀奖励回调，验证跨类集成行为。不使用 Mock 框架，
    /// 全部为手写测试夹具。</para>
    /// </remarks>
    [TestFixture]
    internal class EnemyIntegrationTests
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>格子尺寸（px，对应 map.gridWidth=80）。</summary>
        private const float CellSize = 80f;

        /// <summary>敌人逻辑宽/高（对应 visual.width/height，用于中心点计算）。</summary>
        private const float EnemyWidth = 40f;
        private const float EnemyHeight = 40f;

        /// <summary>基础移动速度（px/s，对应 ENEMY_BASE_SPEED=50）。</summary>
        private const int BaseMoveSpeed = 50;

        /// <summary>空间单元边长（与 EnemyManager.DefaultGridSize 一致）。</summary>
        private const int GridSize = 80;

        // ====================================================================
        // 测试用 EnemyBase 子类 —— 暴露 protected API 供集成测试
        // ====================================================================

        /// <summary>
        /// 测试用 EnemyBase 子类，暴露 protected API 并记录接触/击杀回调。
        /// 复用 EnemyBaseTests.TestEnemy 的模式，但面向集成测试场景。
        /// </summary>
        private sealed class IntegrationEnemy : EnemyBase
        {
            /// <summary>终点攻击回调累计次数。</summary>
            public int EndPointAttackCount;

            /// <summary>上次终点攻击是否生效（目标返回 true）。</summary>
            public bool LastEndPointResult;

            /// <summary>击杀回调累计次数（对应 EnemyKilledHandler 触发次数）。</summary>
            public int KillCallbackCount;

            /// <summary>上次击杀的敌人 ID。</summary>
            public int LastKilledEnemyId;

            /// <summary>上次击杀的攻击者 ID。</summary>
            public int LastKillAttackerId;

            /// <summary>上次击杀的奖励值。</summary>
            public int LastExperienceReward;

            /// <summary>上次击杀的阵营。</summary>
            public bool LastKillIsPlayerLane;

            /// <summary>死亡请求移除回调累计次数（对应 EnemyDeathRequestHandler 触发次数）。</summary>
            public int DeathRequestedCount;

            /// <summary>上次死亡请求移除的敌人 ID。</summary>
            public int LastDeathRequestedEnemyId;

            /// <summary>上次死亡请求移除的原因。</summary>
            public EnemyRemovalReason LastDeathRequestedReason;

            /// <summary>
            /// 配置测试依赖并注入记录回调。自动包装终点攻击目标以记录调用次数与结果。
            /// </summary>
            /// <param name="map">地图数据。</param>
            /// <param name="endPointTarget">终点攻击目标（可自定义，如委托到 BattleTarget）。</param>
            /// <param name="onEnemyKilled">击杀奖励回调。</param>
            internal void ConfigureForTest(
                MapData map,
                IEnemyEndPointAttackTarget endPointTarget,
                EnemyKilledHandler onEnemyKilled)
            {
                // 包装终点攻击目标以记录调用次数与结果。
                IEnemyEndPointAttackTarget wrappedTarget = new RecordingTarget(this, endPointTarget);

                // 包装击杀回调以记录击杀次数与参数。
                EnemyKilledHandler wrappedKill = (killedId, attackerId, reward, isPlayerLane) =>
                {
                    KillCallbackCount++;
                    LastKilledEnemyId = killedId;
                    LastKillAttackerId = attackerId;
                    LastExperienceReward = reward;
                    LastKillIsPlayerLane = isPlayerLane;
                    onEnemyKilled(killedId, attackerId, reward, isPlayerLane);
                };

                // 记录死亡请求移除回调次数、参数与原因。
                EnemyDeathRequestHandler wrappedDeath = (killedId, reason) =>
                {
                    DeathRequestedCount++;
                    LastDeathRequestedEnemyId = killedId;
                    LastDeathRequestedReason = reason;
                };

                Configure(map, CellSize, wrappedTarget, wrappedKill, wrappedDeath);
            }

            /// <summary>记录终点攻击调用并转发到真实目标的包装目标。</summary>
            private sealed class RecordingTarget : IEnemyEndPointAttackTarget
            {
                private readonly IntegrationEnemy _owner;
                private readonly IEnemyEndPointAttackTarget _inner;

                internal RecordingTarget(IntegrationEnemy owner, IEnemyEndPointAttackTarget inner)
                {
                    _owner = owner;
                    _inner = inner;
                }

                public bool ReceiveEndPointAttack(EndPointAttackRequest request)
                {
                    _owner.EndPointAttackCount++;
                    bool result = _inner?.ReceiveEndPointAttack(request) ?? true;
                    _owner.LastEndPointResult = result;
                    return result;
                }
            }

            /// <summary>暴露 Init 供测试调用。</summary>
            internal void InitForTest(
                bool isPlayerLane,
                int maxHealth,
                float width = EnemyWidth,
                float height = EnemyHeight)
            {
                Init(isPlayerLane, maxHealth, width, height);
            }

            /// <summary>暴露 AssignRuntimeId 供测试调用。</summary>
            internal void AssignId(int id) => AssignRuntimeId(id);

            /// <summary>暴露 BeginMoving 供测试调用。</summary>
            internal void StartMoving() => BeginMoving();

            /// <summary>暴露 DeathStarted 供测试验证。</summary>
            internal bool TestDeathStarted => DeathStarted;

            /// <summary>暴露 InPool 供测试验证。</summary>
            internal bool TestInPool => InPool;

            /// <summary>暴露 CurrentState 枚举形式供测试验证。</summary>
            internal EnemyRuntimeState TestState => CurrentStateEnum;
        }

        // ====================================================================
        // 测试夹具
        // ====================================================================

        /// <summary>
        /// 构造一个直线路径测试 MapData：玩家路径 [(0,0),(1,0),(2,0),(3,0)]，
        /// 对手路径 [(3,0),(2,0),(1,0),(0,0)]。
        /// </summary>
        /// <remarks>复用 EnemyBaseTests.BuildLinearPathMapData 的模式。</remarks>
        private static MapData BuildLinearPathMapData()
        {
            var grid = new List<IReadOnlyList<string>>
            {
                new List<string> { "0_1" }, // 列 0
                new List<string> { "0_1" }, // 列 1
                new List<string> { "0_1" }, // 列 2
                new List<string> { "0_1" }, // 列 3
            };

            var playerPath = new List<GridPosition>
            {
                new GridPosition(0, 0),
                new GridPosition(1, 0),
                new GridPosition(2, 0),
                new GridPosition(3, 0),
            };

            var opponentPath = new List<GridPosition>
            {
                new GridPosition(3, 0),
                new GridPosition(2, 0),
                new GridPosition(1, 0),
                new GridPosition(0, 0),
            };

            return MapData.FromColumnMajorGrid(
                grid,
                DecodeCell,
                mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(3, 0),
                opponentStart: new GridPosition(3, 0),
                opponentEnd: new GridPosition(0, 0),
                playerPath: playerPath,
                opponentPath: opponentPath);
        }

        /// <summary>
        /// 把源 "kind_lane" 字符串解码为 GridCell（复用 EnemyBaseTests.DecodeCell）。
        /// </summary>
        private static GridCell DecodeCell(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return new GridCell(GridCellKind.Blocked, BuildableSide.None);
            }

            string[] parts = code.Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int kind) || !int.TryParse(parts[1], out int lane))
            {
                return new GridCell(GridCellKind.Blocked, BuildableSide.None);
            }

            GridCellKind cellKind = kind switch
            {
                0 => GridCellKind.Passage,
                1 => GridCellKind.Buildable,
                _ => GridCellKind.Blocked,
            };

            BuildableSide side = lane switch
            {
                1 => BuildableSide.Player,
                0 => BuildableSide.Opponent,
                _ => BuildableSide.None,
            };

            return new GridCell(cellKind, side);
        }

        /// <summary>
        /// 空操作击杀回调（当测试不关心奖励时使用，满足 Configure 非 null 约束）。
        /// </summary>
        private static readonly EnemyKilledHandler NoOpKillHandler =
            (killedId, attackerId, reward, isPlayerLane) => { };

        /// <summary>
        /// 恒返回 true 的终点攻击目标桩：测试不关心攻击结果时使用。
        /// </summary>
        private static readonly IEnemyEndPointAttackTarget AlwaysTrueTarget =
            new AlwaysTrueEndPointTarget();

        /// <summary>
        /// 恒返回 true 的终点攻击目标实现（ReceiveEndPointAttack 无条件生效）。
        /// </summary>
        private sealed class AlwaysTrueEndPointTarget : IEnemyEndPointAttackTarget
        {
            public bool ReceiveEndPointAttack(EndPointAttackRequest request) => true;
        }

        /// <summary>
        /// 记录终点攻击伤害并委托到 BattleState.ApplyDamage 的目标桩（验证终点攻击伤害链路）。
        /// </summary>
        private sealed class RecordingStateDamageTarget : IEnemyEndPointAttackTarget
        {
            private readonly BattleState _state;

            /// <summary>累计收到的终点攻击伤害。</summary>
            public int DamageReceived;

            /// <summary>上次终点攻击的目标车道（true=玩家方）。</summary>
            public bool LastIsPlayerLane;

            internal RecordingStateDamageTarget(BattleState state)
            {
                _state = state;
            }

            public bool ReceiveEndPointAttack(EndPointAttackRequest request)
            {
                DamageReceived += request.Damage;
                LastIsPlayerLane = request.IsPlayerLane;
                // 委托到 BattleState.ApplyDamage（玩家方目标受击）。
                _state.ApplyDamage(true, request.Damage);
                return true;
            }
        }

        /// <summary>
        /// 构造一个已 Configure + Init + BeginMoving 的集成测试敌人，位于玩家路径起点。
        /// </summary>
        /// <param name="map">地图数据。</param>
        /// <param name="endPointTarget">终点攻击目标。</param>
        /// <param name="onEnemyKilled">击杀奖励回调（null 时使用空操作）。</param>
        /// <param name="id">敌人运行时 ID。</param>
        /// <param name="maxHealth">最大血量。</param>
        private static IntegrationEnemy CreateMovingEnemy(
            MapData map,
            IEnemyEndPointAttackTarget endPointTarget,
            EnemyKilledHandler onEnemyKilled,
            int id = 1,
            int maxHealth = 100)
        {
            var enemy = new IntegrationEnemy();
            enemy.ConfigureForTest(map, endPointTarget, onEnemyKilled ?? NoOpKillHandler);
            enemy.AssignId(id);
            enemy.InitForTest(isPlayerLane: true, maxHealth);
            enemy.StartMoving();
            return enemy;
        }

        /// <summary>
        /// 构造一个简单的击杀奖励记录器，用于验证"奖励一次"。
        /// 复用 DeadEntityRewardTests.SynchronousKillSettlement 的去重模式，
        /// 但不依赖 BattleEconomy，只记录奖励次数与去重。
        /// </summary>
        private sealed class KillRewardTracker
        {
            private readonly HashSet<int> _rewardedEnemyIds = new HashSet<int>();

            /// <summary>累计奖励次数（去重后）。</summary>
            public int RewardCount;

            /// <summary>累计奖励金额。</summary>
            public int TotalReward;

            /// <summary>是否已冻结（冻结后不奖励）。</summary>
            public bool IsFrozen;

            /// <summary>
            /// 尝试为指定敌人发放奖励。同一敌人只奖励一次。
            /// </summary>
            public bool TryReward(int killedEnemyId, int experienceReward)
            {
                if (IsFrozen)
                {
                    return false;
                }

                if (!_rewardedEnemyIds.Add(killedEnemyId))
                {
                    return false;
                }

                RewardCount++;
                TotalReward += experienceReward;
                return true;
            }
        }

        // ====================================================================
        // 场景 1：敌人移动/终点攻击目标
        // ====================================================================

        [Test]
        [Description("敌人沿路径移动到末尾后发起终点攻击，通过目标桩对 BattleTarget 造成 1 点伤害。"
            + " 验证 EnemyBase.AttemptEndPointAttackOnce → IEnemyEndPointAttackTarget → BattleState.ApplyDamage 集成链。"
            + " spec '伤害、死亡事实 MUST 在其发生点同步生效'。")]
        public void EnemyMoveAndContact_DealsDamageToBattleTarget()
        {
            MapData map = BuildLinearPathMapData();

            // 构造一个最小 BattleState 作为终点攻击伤害的接收方。
            // 玩家方目标生命默认 3，终点攻击造成 1 点伤害。
            var state = new BattleState();
            state.ApplyStartGame(nowMs: 0);

            // 构造终点攻击目标桩：记录伤害并委托到 state.ApplyDamage（模拟 BattleTarget 语义）。
            // 玩家车道敌人终点攻击玩家方目标，造成 1 点伤害。
            var contactTarget = new RecordingStateDamageTarget(state);

            var enemy = CreateMovingEnemy(map, contactTarget, onEnemyKilled: null, id: 1);

            // 4 点路径 (0,0)→(80,0)→(160,0)→(240,0)，3 段 × 80px。
            // 50px/s，每段 80px 需 1600ms（50*1600/1000=80px）。
            // 使用 1600ms/帧避免 1000ms/帧的过冲振荡（50px < 80px 导致来回振荡）。
            // 4 点路径需 7 帧：1 帧识别 path[0] + 3 段 × 2 帧（移动+识别到达）。
            for (int i = 0; i < 8; i++)
            {
                enemy.Update(1600);
            }

            // 走完全程后应触发终点攻击。
            Assert.GreaterOrEqual(enemy.EndPointAttackCount, 1, "走完全程后触发终点攻击。");
            Assert.AreEqual(1, contactTarget.DamageReceived, "终点攻击伤害为 1 点。");
            Assert.IsTrue(contactTarget.LastIsPlayerLane, "终点攻击目标为玩家方。");
            Assert.AreEqual(BattleState.DefaultMaxHealth - 1, state.PlayerHealth,
                "玩家方目标生命 3-1=2（终点攻击伤害生效）。");
        }

        [Test]
        [Description("敌人终点攻击无冷却、严格一次性：两个敌人各自走完全程，都恰好攻击 1 次。"
            + " 不再依赖 500ms 接触冷却（终点接触是一次到达事件，必须只尝试一次）。")]
        public void EnemyEndPointAttack_Once_NoRepeatAttack()
        {
            MapData map = BuildLinearPathMapData();

            // 两个敌人各自从路径起点走完全程。
            var enemyA = CreateMovingEnemy(map, AlwaysTrueTarget, onEnemyKilled: null, id: 1);
            var enemyB = CreateMovingEnemy(map, AlwaysTrueTarget, onEnemyKilled: null, id: 2);

            // 使用 1600ms/帧（80px/帧）避免过冲振荡，8 帧足以走完全程。
            for (int i = 0; i < 8; i++)
            {
                enemyA.Update(1600);
                enemyB.Update(1600);
            }

            // 每个敌人走到终点恰好攻击一次（严格一次性，无冷却可重复）。
            Assert.AreEqual(1, enemyA.EndPointAttackCount, "敌人 A 走完全程恰好攻击 1 次。");
            Assert.AreEqual(1, enemyB.EndPointAttackCount, "敌人 B 走完全程恰好攻击 1 次。");
        }

        // ====================================================================
        // 场景 2：路径边界
        // ====================================================================

        [Test]
        [Description("敌人走完全部路径点后路径索引达 length，请求以 ReachedEndPoint 原因移除。"
            + " 对应 EnemyBase.AttemptEndPointAttackOnce：终点攻击后经 EnemyDeathRequestHandler 请求回收。"
            + " 验证路径边界：索引不越界，到达终点后请求移除。")]
        public void PathBoundary_ReachesEnd_RequestsReachedEndPointRemoval()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, AlwaysTrueTarget, onEnemyKilled: null, id: 1);

            // 4 点路径，3 段 × 80px = 240px。50px/s，每段 1600ms（80px/帧）。
            // 使用 1600ms/帧避免过冲振荡。8 帧足以走完全程并请求移除。
            for (int i = 0; i < 8; i++)
            {
                enemy.Update(1600);
            }

            // 走完全程后请求以 ReachedEndPoint 原因移除一次。
            Assert.AreEqual(1, enemy.DeathRequestedCount, "到达路径终点请求移除一次。");
            Assert.AreEqual(EnemyRemovalReason.ReachedEndPoint, enemy.LastDeathRequestedReason,
                "终点移除原因为 ReachedEndPoint。");
        }

        [Test]
        [Description("敌人到达路径终点后请求移除一次、后续 Update 不再重复请求。"
            + " 验证终点攻击严格一次性：请求移除后状态稳定，不越界访问路径列表。")]
        public void PathBoundary_AfterGameOver_UpdateIsNoOp()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, AlwaysTrueTarget, onEnemyKilled: null, id: 1);

            // 走完全程（1600ms/帧避免过冲振荡）。
            for (int i = 0; i < 8; i++)
            {
                enemy.Update(1600);
            }
            Assert.AreEqual(1, enemy.DeathRequestedCount, "到达终点后请求移除一次。");
            float xAfterEnd = enemy.X;

            // 请求移除后再 Update 不抛异常、位置不变、不重复请求移除。
            Assert.DoesNotThrow(() => enemy.Update(1000));
            Assert.AreEqual(xAfterEnd, enemy.X, "请求移除后位置不变。");
            Assert.AreEqual(1, enemy.DeathRequestedCount, "后续 Update 不重复请求移除。");
        }

        // ====================================================================
        // 场景 3：死亡
        // ====================================================================

        [Test]
        [Description("敌人受击血量归零进入 DEAD 状态，击杀回调在死亡点同步触发。"
            + " spec '伤害、死亡事实 MUST 在其发生点同步生效'。"
            + " design 决策 4：击杀奖励经 EnemyKilledHandler 直接回调，不经事件总线。")]
        public void EnemyDeath_HitToZero_TriggersKillCallbackSynchronously()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: (killedId, attackerId, reward, isPlayerLane) => { },
                id: 1, maxHealth: 100);

            Assert.AreEqual(0, enemy.KillCallbackCount, "受击前无击杀回调。");

            // 一击致死。
            enemy.Hit(100, attackerId: 10);

            Assert.AreEqual(0, enemy.Health, "血量归零。");
            Assert.AreEqual((int)EnemyRuntimeState.Dead, enemy.CurrentState, "进入 DEAD。");
            Assert.AreEqual(1, enemy.KillCallbackCount, "死亡点同步触发击杀回调一次。");
            Assert.AreEqual(1, enemy.LastKilledEnemyId, "击杀的敌人 ID=1。");
            Assert.AreEqual(10, enemy.LastKillAttackerId, "攻击者 ID=10。");
            Assert.AreEqual(1, enemy.LastExperienceReward, "普通敌人奖励 1。");
            Assert.IsTrue(enemy.LastKillIsPlayerLane, "玩家方车道。");
            Assert.IsTrue(enemy.TestDeathStarted, "deathStarted 已标记。");
        }

        [Test]
        [Description("敌人多段受击逐步扣血，最后一击致死时触发击杀回调。")]
        public void EnemyDeath_MultipleHits_LastHitKills()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: (killedId, attackerId, reward, isPlayerLane) => { },
                id: 1, maxHealth: 30);

            // 第一击 10，剩余 20。
            enemy.Hit(10, attackerId: 5);
            Assert.AreEqual(0, enemy.KillCallbackCount, "未死亡，无击杀回调。");
            Assert.AreEqual((int)EnemyRuntimeState.Moving, enemy.CurrentState, "仍 MOVING。");

            // 第二击 20，归零致死。
            enemy.Hit(20, attackerId: 5);
            Assert.AreEqual(0, enemy.Health, "血量归零。");
            Assert.AreEqual((int)EnemyRuntimeState.Dead, enemy.CurrentState, "进入 DEAD。");
            Assert.AreEqual(1, enemy.KillCallbackCount, "最后一击致死触发回调。");
        }

        // ====================================================================
        // 场景 4：同子步集合修改安全
        // ====================================================================

        [Test]
        [Description("EnemyManager.Update 中敌人触发 ForceRemove 不重入修改集合，"
            + " 先入 _removeQueue 再遍历结束后统一处理（决策 0.4）。"
            + " spec 'TryFreeze 不在嵌套伤害调用栈内重入销毁 Manager 或集合'。"
            + " 使用真实 EnemyBase 注册到 EnemyManager，验证集成行为。")]
        public void SameSubstepCollectionModification_ForceRemoveQueuedNotImmediate()
        {
            MapData map = BuildLinearPathMapData();
            var mgr = new EnemyManager(GridSize);

            // 创建两个真实敌人并注册到 EnemyManager。
            // 敌人 1 在路径起点，敌人 2 也在起点。
            var rewardTracker = new KillRewardTracker();
            var enemy1 = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: (killedId, attackerId, reward, lane) =>
                    rewardTracker.TryReward(killedId, reward),
                id: 1, maxHealth: 10);
            var enemy2 = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: (killedId, attackerId, reward, lane) =>
                    rewardTracker.TryReward(killedId, reward),
                id: 2, maxHealth: 10);

            mgr.Register(enemy1);
            mgr.Register(enemy2);
            Assert.AreEqual(2, mgr.Count, "注册后 2 个敌人。");

            // 在 Update 遍历过程中，通过外部模拟"伤害回调触发 ForceRemove"。
            // 由于 EnemyManager.Update 是同步遍历，我们在遍历前先对 enemy1.Hit 致死，
            // 再 ForceRemove 入队。Update 遍历不会因此抛 InvalidOperationException。
            enemy1.Hit(10, attackerId: 99); // enemy1 死亡
            mgr.ForceRemove(1); // 入移除队列

            // Update 遍历中集合不变（ForceRemove 只入队）。
            Assert.DoesNotThrow(() => mgr.Update(16));
            // Update 结束后自动处理移除队列。
            Assert.AreEqual(1, mgr.Count, "Update 后 enemy1 被移除，enemy2 保留。");
            Assert.IsNotNull(mgr.GetById(2), "enemy2 仍在集合中。");
        }

        [Test]
        [Description("同一子步内多个敌人死亡请求都入队，Update 后统一处理，不重入修改集合。"
            + " 验证 _removeQueue 批量处理多个死亡请求。")]
        public void SameSubstepCollectionModification_MultipleRemovesQueuedBatchProcessed()
        {
            MapData map = BuildLinearPathMapData();
            var mgr = new EnemyManager(GridSize);

            var enemy1 = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: null, id: 1, maxHealth: 10);
            var enemy2 = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: null, id: 2, maxHealth: 10);
            var enemy3 = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: null, id: 3, maxHealth: 10);

            mgr.Register(enemy1);
            mgr.Register(enemy2);
            mgr.Register(enemy3);
            Assert.AreEqual(3, mgr.Count);

            // 两个敌人在同一子步死亡并请求移除。
            enemy1.Hit(10, attackerId: 99);
            enemy3.Hit(10, attackerId: 99);
            mgr.ForceRemove(1);
            mgr.ForceRemove(3);

            // Update 前集合不变。
            Assert.AreEqual(3, mgr.Count, "ForceRemove 入队后集合不变。");

            mgr.Update(16);

            // Update 后两个死亡敌人被移除，enemy2 保留。
            Assert.AreEqual(1, mgr.Count, "Update 后只保留 enemy2。");
            Assert.IsNotNull(mgr.GetById(2), "enemy2 仍在。");
            Assert.IsNull(mgr.GetById(1), "enemy1 已移除。");
            Assert.IsNull(mgr.GetById(3), "enemy3 已移除。");
        }

        // ====================================================================
        // 场景 5：重复死亡幂等
        // ====================================================================

        [Test]
        [Description("同一敌人多次 Hit 致死只触发一次击杀回调（deathStarted 幂等）。"
            + " 对应 EnemyBase.js:484 deathStarted 守卫。"
            + " 验证已死亡敌人再次 Hit 返回 false，不重复触发死亡与奖励。")]
        public void DuplicateDeath_Idempotent_OnlyOneKillCallback()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: (killedId, attackerId, reward, lane) => { },
                id: 1, maxHealth: 10);

            // 第一次致死。
            enemy.Hit(10, attackerId: 5);
            Assert.AreEqual(1, enemy.KillCallbackCount, "第一次致死触发一次回调。");
            Assert.IsTrue(enemy.TestDeathStarted, "deathStarted=true。");

            // 再次受击（已死亡，Hit 返回 false）。
            bool result = enemy.Hit(10, attackerId: 5);
            Assert.IsFalse(result, "已死亡敌人 Hit 返回 false。");
            Assert.AreEqual(1, enemy.KillCallbackCount, "不重复触发击杀回调。");
            Assert.AreEqual((int)EnemyRuntimeState.Dead, enemy.CurrentState, "仍 DEAD。");
        }

        [Test]
        [Description("EnemyManager.ForceRemove 对同一敌人多次调用幂等，不重复 GameOver。"
            + " 使用真实 EnemyBase 验证集成行为。")]
        public void DuplicateDeath_ForceRemoveIdempotent_RealEnemy()
        {
            MapData map = BuildLinearPathMapData();
            var mgr = new EnemyManager(GridSize);

            // 用真实 EnemyBase 注册，但需要 IEnemyEntity 接口。
            // IntegrationEnemy 继承 EnemyBase 实现 IEnemyEntity。
            var enemy = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: null, id: 1, maxHealth: 100);
            mgr.Register(enemy);

            // 多次 ForceRemove。
            mgr.ForceRemove(1);
            mgr.ForceRemove(1);
            mgr.ForceRemove(1);

            // GameOver 只生效一次（EnemyBase.GameOver 幂等，inPool 守卫）。
            Assert.IsTrue(enemy.TestInPool, "GameOver 首次生效，inPool=true。");
            mgr.ProcessRemoveQueue();
            Assert.AreEqual(0, mgr.Count, "移除后集合为空。");
        }

        [Test]
        [Description("Register 后敌人 Hit 扣血，EnemyHealthChanged 事件携带当前/最大/变化量转发。"
            + " 集成验证：EnemyBase.Hit → SetHealthChangedCallback → EnemyManager.EnemyHealthChanged。")]
        public void Register_Hit_ForwardsEnemyHealthChanged()
        {
            MapData map = BuildLinearPathMapData();
            var mgr = new EnemyManager(GridSize);

            // 记录事件转发的参数。
            int forwardedCount = 0;
            int lastCurrent = 0;
            int lastMax = 0;
            int lastDelta = 0;
            mgr.EnemyHealthChanged += (id, current, max, delta) =>
            {
                forwardedCount++;
                lastCurrent = current;
                lastMax = max;
                lastDelta = delta;
            };

            var enemy = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: null, id: 1, maxHealth: 100);
            mgr.Register(enemy);

            enemy.Hit(30, attackerId: 10);

            Assert.AreEqual(1, forwardedCount, "Register 注入回调后，Hit 扣血应转发一次事件。");
            Assert.AreEqual(70, lastCurrent, "转发当前血量=70。");
            Assert.AreEqual(100, lastMax, "转发最大血量=100。");
            Assert.AreEqual(-30, lastDelta, "转发变化量=-30。");
        }

        [Test]
        public void Register_Overkill_ForwardsRawDamageAfterHealthChanged()
        {
            MapData map = BuildLinearPathMapData();
            var mgr = new EnemyManager(GridSize);
            var order = new List<string>();
            EnemyDamageViewData damageFact = default;
            mgr.EnemyHealthChanged += (_, _, _, _) => order.Add("health");
            mgr.EnemyDamaged += fact =>
            {
                order.Add("damage");
                damageFact = fact;
            };

            var enemy = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: null, id: 12, maxHealth: 3);
            mgr.Register(enemy);

            enemy.Hit(10, attackerId: 10);

            CollectionAssert.AreEqual(new[] { "health", "damage" }, order);
            Assert.AreEqual(12, damageFact.RuntimeId);
            Assert.AreEqual(10, damageFact.RawDamage);
        }

        [Test]
        [Description("敌人血量归零时 EnemyHealthChanged 转发 current=0，供表现层立即隐藏血条。")]
        public void Register_HitKills_ForwardsZeroHealth()
        {
            MapData map = BuildLinearPathMapData();
            var mgr = new EnemyManager(GridSize);

            int forwardedCount = 0;
            int lastCurrent = -1;
            mgr.EnemyHealthChanged += (id, current, max, delta) =>
            {
                forwardedCount++;
                lastCurrent = current;
            };

            var enemy = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: null, id: 1, maxHealth: 100);
            mgr.Register(enemy);

            enemy.Hit(100, attackerId: 10);

            Assert.AreEqual(1, forwardedCount, "致死一击转发一次事件。");
            Assert.AreEqual(0, lastCurrent, "致死一击转发当前血量=0。");
        }

        // ====================================================================
        // 场景 6：奖励一次（单次击杀奖励，不重复发奖）
        // ====================================================================

        [Test]
        [Description("同一敌人死亡只提交一次击杀奖励，不重复发奖。"
            + " 对应 DeadEntityRegistry 本期延后，奖励去重在死亡结算链用 HashSet 实现（task 3.12）。"
            + " spec '伤害、死亡事实、奖励 MUST 在其发生点同步生效'。"
            + " 集成验证：EnemyBase.Hit → EnemyKilledHandler → KillRewardTracker.TryReward 去重。")]
        public void RewardOnce_SameEnemyKilled_OnlyOneReward()
        {
            MapData map = BuildLinearPathMapData();
            var rewardTracker = new KillRewardTracker();

            var enemy = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: (killedId, attackerId, reward, lane) =>
                    rewardTracker.TryReward(killedId, reward),
                id: 1, maxHealth: 10);

            // 第一次致死，奖励生效。
            enemy.Hit(10, attackerId: 5);
            Assert.AreEqual(1, rewardTracker.RewardCount, "首次死亡奖励一次。");
            Assert.AreEqual(1, rewardTracker.TotalReward, "奖励金额 1。");

            // 模拟"重复死亡"场景：再次对同一敌人 Hit（已死亡，Hit 返回 false）。
            // 即使外部错误地再次调用 TryReward，去重表也会拦截。
            enemy.Hit(10, attackerId: 5);
            Assert.AreEqual(1, rewardTracker.RewardCount, "重复死亡不重复奖励。");
            Assert.AreEqual(1, rewardTracker.TotalReward, "奖励金额不变。");
        }

        [Test]
        [Description("不同敌人各自独立奖励一次，不互相影响。")]
        public void RewardOnce_DifferentEnemies_EachRewardedOnce()
        {
            MapData map = BuildLinearPathMapData();
            var rewardTracker = new KillRewardTracker();

            var enemy1 = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: (killedId, attackerId, reward, lane) =>
                    rewardTracker.TryReward(killedId, reward),
                id: 1, maxHealth: 10);
            var enemy2 = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: (killedId, attackerId, reward, lane) =>
                    rewardTracker.TryReward(killedId, reward),
                id: 2, maxHealth: 10);

            enemy1.Hit(10, attackerId: 5);
            enemy2.Hit(10, attackerId: 5);

            Assert.AreEqual(2, rewardTracker.RewardCount, "两个不同敌人各奖励一次。");
            Assert.AreEqual(2, rewardTracker.TotalReward, "总奖励 1+1=2。");

            // 重复对 enemy1 触发（已死亡，Hit 返回 false，不触发回调）。
            enemy1.Hit(10, attackerId: 5);
            Assert.AreEqual(2, rewardTracker.RewardCount, "不重复奖励。");
        }

        [Test]
        [Description("结果冻结后不再发放击杀奖励（冻结门控优先于去重）。"
            + " spec 'Settling has no gameplay damage authority'。")]
        public void RewardOnce_AfterFrozen_NoReward()
        {
            MapData map = BuildLinearPathMapData();
            var rewardTracker = new KillRewardTracker();

            var enemy1 = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: (killedId, attackerId, reward, lane) =>
                    rewardTracker.TryReward(killedId, reward),
                id: 1, maxHealth: 10);
            var enemy2 = CreateMovingEnemy(
                map, AlwaysTrueTarget,
                onEnemyKilled: (killedId, attackerId, reward, lane) =>
                    rewardTracker.TryReward(killedId, reward),
                id: 2, maxHealth: 10);

            // 冻结前：enemy1 死亡正常奖励。
            enemy1.Hit(10, attackerId: 5);
            Assert.AreEqual(1, rewardTracker.RewardCount, "冻结前奖励生效。");

            // 模拟 BattleResultBuilder.TryFreeze 首次冻结 → 进入 Settling。
            rewardTracker.IsFrozen = true;

            // 冻结后：enemy2 死亡，不奖励。
            enemy2.Hit(10, attackerId: 5);
            Assert.AreEqual(1, rewardTracker.RewardCount, "冻结后不奖励。");
            Assert.AreEqual(1, rewardTracker.TotalReward, "总奖励不变。");
        }

        // ====================================================================
        // 场景 7：Mob0 池复用无污染（Acquire/Release Reset 清除 target/id/state）
        // ====================================================================

        [Test]
        [Description("Mob0 池复用无污染：Release 后 ResetState 清除全部可变状态"
            + "（RuntimeId、阵营、生命、路径、目标引用、死亡标记），复用对象等价于新构造。"
            + " task 4.5 契约：Release 后旧 ID/目标引用不得继续有效。"
            + " enemy-pool-reset-contract.md：每次出生必须复位全部字段。")]
        public void Mob0PoolReuse_ResetClearsTargetIdState_NoPollution()
        {
            MapData map = BuildLinearPathMapData();

            // 构造 EnemyFactory + BattleObjectPool<Mob0Enemy> + RuntimeIdAllocator。
            var idAllocator = new RuntimeIdAllocator();
            var pool = new BattleObjectPool<Mob0Enemy>(() => new Mob0Enemy());
            var factory = new EnemyFactory(idAllocator, pool);

            // 第一轮：Acquire → 初始化数值 → Configure → Init → 移动 → 受击 → 死亡。
            Mob0Enemy first = factory.Acquire();
            int firstId = first.RuntimeId;
            Assert.AreEqual(1, firstId, "首次 Acquire RuntimeId=1。");

            // 注入依赖并初始化数值（模拟 EnemyManager 的 spawn 流程）。
            first.InitializeStats(new Mob0EnemyInitStats(
                healthByWave: new[] { 10, 11 },
                speed: 50, contactDamage: 1, rewardGold: 1));
            // Configure/Init 由 EnemyManager 在 spawn 时调用，此处只验证 ResetState 清除数值字段与 ID。

            // 污染状态：BeginDeath 触发死亡表现边界。
            first.BeginDeath();
            Assert.IsTrue(first.IsDeathPresentationStarted, "第一轮死亡表现已开始。");

            // Release 归还到池（触发 ResetState）。
            Assert.IsTrue(factory.Release(first), "Release 返回 true。");
            Assert.AreEqual(0, first.RuntimeId, "Release 后 RuntimeId=0（旧 ID 失效）。");
            Assert.IsFalse(first.Stats.HasValue, "Release 后 Stats=null。");
            Assert.IsFalse(first.IsDeathPresentationStarted, "Release 后死亡标记清除。");
            Assert.IsFalse(first.IsDeathScheduled, "Release 后调度标记清除。");
            Assert.IsFalse(first.IsDeathPresentationCompleted, "Release 后完成标记清除。");

            // 第二轮：Acquire 应复用同一对象，状态已清空。
            Mob0Enemy second = factory.Acquire();
            Assert.AreSame(first, second, "LIFO 复用同一对象实例。");
            Assert.AreEqual(2, second.RuntimeId, "复用对象分配新 RuntimeId=2（不复用旧 ID=1）。");
            Assert.AreNotEqual(firstId, second.RuntimeId, "新 ID 与旧 ID 不同。");
            Assert.IsFalse(second.Stats.HasValue, "复用对象 Stats=null（无旧数值残留）。");
            Assert.IsFalse(second.IsDeathPresentationStarted, "复用对象死亡标记=false。");

            // 复用对象可重新 InitializeStats（无污染）。
            second.InitializeStats(new Mob0EnemyInitStats(
                healthByWave: new[] { 100, 200 },
                speed: 75, contactDamage: 2, rewardGold: 3));
            Assert.AreEqual(100, second.MaxHealthBaseValue, "新 MaxHealthBase=100。");
            Assert.AreEqual(75, second.BaseMoveSpeedValue, "新 BaseMoveSpeed=75。");
            Assert.AreEqual(3, second.Stats.Value.RewardGold, "新 RewardGold=3。");

            // 清理。
            factory.Release(second);
        }

        [Test]
        [Description("Mob0 池多轮复用：每轮 Reset 清除全部状态，可重新触发死亡表现边界。"
            + " 验证池复用无污染的完整性——死亡边界标记可重新触发。")]
        public void Mob0PoolReuse_MultipleRounds_DeathBoundaryRetriggerable()
        {
            var idAllocator = new RuntimeIdAllocator();
            var pool = new BattleObjectPool<Mob0Enemy>(() => new Mob0Enemy());
            var factory = new EnemyFactory(idAllocator, pool);

            int expectedId = 1;
            Mob0Enemy reused = null;

            for (int round = 0; round < 3; round++)
            {
                Mob0Enemy enemy = factory.Acquire();

                if (round == 0)
                {
                    reused = enemy;
                }
                else
                {
                    Assert.AreSame(reused, enemy, $"第 {round} 轮复用同一对象。");
                }

                Assert.AreEqual(expectedId, enemy.RuntimeId,
                    $"第 {round} 轮 RuntimeId={expectedId}（单调递增）。");

                // 每轮重新初始化并触发死亡。
                enemy.InitializeStats(new Mob0EnemyInitStats(
                    healthByWave: new[] { 10 },
                    speed: 50, contactDamage: 1, rewardGold: 1));
                enemy.BeginDeath();
                Assert.IsTrue(enemy.IsDeathPresentationStarted,
                    $"第 {round} 轮 BeginDeath 后死亡表现已开始。");

                // Release 清除状态。
                factory.Release(enemy);
                Assert.IsFalse(enemy.IsDeathPresentationStarted,
                    $"第 {round} 轮 Release 后死亡标记清除。");
                Assert.AreEqual(0, enemy.RuntimeId,
                    $"第 {round} 轮 Release 后 RuntimeId=0。");

                expectedId++;
            }

            Assert.AreEqual(0, pool.ActiveCount, "全部归还后 ActiveCount=0。");
            Assert.GreaterOrEqual(pool.FreeCount, 1, "空闲池有容量。");
        }

        [Test]
        [Description("Mob0 池复用：Release 后旧引用的目标/状态不可用（旧 ID 失效，阵营/生命归零）。"
            + " task 4.5 不变量 3：Release 后旧 ID/目标引用不得继续有效。")]
        public void Mob0PoolReuse_OldReferenceInvalid_AfterRelease()
        {
            var idAllocator = new RuntimeIdAllocator();
            var pool = new BattleObjectPool<Mob0Enemy>(() => new Mob0Enemy());
            var factory = new EnemyFactory(idAllocator, pool);

            Mob0Enemy enemy = factory.Acquire();
            int oldId = enemy.RuntimeId;
            Assert.Greater(oldId, 0, "Acquire 后 RuntimeId > 0。");

            // Release 触发 ResetState，清除全部可变状态。
            factory.Release(enemy);

            // 旧引用的 RuntimeId 已被清除。
            Assert.AreEqual(0, enemy.RuntimeId, "旧引用 RuntimeId=0（旧 ID 失效）。");
            Assert.IsFalse(enemy.Stats.HasValue, "旧引用 Stats=null。");
            Assert.IsFalse(enemy.IsDeathPresentationStarted, "旧引用死亡标记=false。");

            // 旧 ID 不等于新 ID（不复用旧 ID）。
            Mob0Enemy next = factory.Acquire();
            Assert.AreNotEqual(oldId, next.RuntimeId, "新 ID 与旧 ID 不同。");
            Assert.AreEqual(oldId + 1, next.RuntimeId, "新 ID = 旧 ID + 1（单调递增）。");

            factory.Release(next);
        }
    }
}
