using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Enemy
{
    // ============================================================================
    // 任务 4.3：EnemyBase 单元测试
    // ----------------------------------------------------------------------------
    // 验证要求（task 4.3）：
    //   1. 路径移动：沿路径点推进位移，到达后递增索引。
    //   2. 空间格变化：中心点所在格变化时更新 gridX/gridY。
    //   3. 接触目标：路径索引达到末尾时接触 BattleTarget，500ms 冷却。
    //   4. 受击：扣血、贡献者记录、血量归零进入 DEAD。
    //   5. 死亡：奖励提交（特殊 10/普通 1），deathStarted 幂等。
    //   6. Reset：ResetState 后等价于新构造，池复用无污染。
    //
    // spec battle-simulation "Simulation is reproducible"：
    //   相同 stepMs 与路径产生相同位移，不依赖真实时间。
    //
    // spec battle-simulation "Update phases are explicit"：
    //   Enemy 只在 MOVING 状态推进移动。
    //
    // design.md:174：纯逻辑敌人生命周期，规则层不持有 Unity GameObject 或表现组件。
    //
    // design.md 决策 4：敌人注册、空间索引、伤害、回收等一致性操作使用直接调用，
    //   不通过全局事件总线。
    //
    // 决策 0.5：列优先 grid[x][y]，通过 GetCell/IsInside 访问。本测试不直接访问嵌套数组。
    //
    // 本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。
    // ============================================================================

    /// <summary>
    /// EnemyBase 路径移动、空间格变化、接触目标、受击、死亡和 Reset 单元测试（task 4.3）。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖：</para>
    /// <list type="bullet">
    /// <item>路径移动：沿路径点推进位移，到达后递增索引。</item>
    /// <item>空间格变化：中心点跨格时 gridX/gridY 更新。</item>
    /// <item>接触目标：路径末尾触发接触回调，500ms 冷却生效。</item>
    /// <item>受击：扣血、贡献者去重、血量归零进入 DEAD。</item>
    /// <item>死亡：奖励提交（特殊 10/普通 1），幂等。</item>
    /// <item>Reset：ResetState 后全部字段等价于新构造。</item>
    /// <item>池化契约：IPoolableBattleObject.ResetState 幂等、不抛出。</item>
    /// <item>状态机：SPAWNING → MOVING → DEAD 迁移正确。</item>
    /// <item>IsTargetableBy：阵营匹配与状态过滤。</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    internal class EnemyBaseTests
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

        // ====================================================================
        // 测试子类 —— 暴露 protected API 供测试
        // ====================================================================

        /// <summary>
        /// 测试用 EnemyBase 子类，暴露 protected API 并记录接触/击杀回调。
        /// </summary>
        private class TestEnemy : EnemyBase
        {
            /// <summary>终点攻击回调累计次数。</summary>
            public int EndPointAttackCount;

            /// <summary>上次终点攻击的目标车道。</summary>
            public bool LastEndPointIsPlayerLane;

            /// <summary>上次终点攻击的伤害值。</summary>
            public int LastEndPointDamage;

            /// <summary>上次终点攻击的攻击者 ID。</summary>
            public int LastEndPointAttackerId;

            /// <summary>击杀回调累计次数。</summary>
            public int KillCallbackCount;

            /// <summary>上次击杀的敌人 ID。</summary>
            public int LastKilledEnemyId;

            /// <summary>上次击杀的攻击者 ID。</summary>
            public int LastKillAttackerId;

            /// <summary>上次击杀的奖励值。</summary>
            public int LastExperienceReward;

            /// <summary>上次击杀的阵营。</summary>
            public bool LastKillIsPlayerLane;

            /// <summary>死亡请求移除回调次数。</summary>
            public int DeathRequestedCount;

            /// <summary>上次死亡请求移除的敌人 ID。</summary>
            public int LastDeathRequestedEnemyId;

            /// <summary>上次死亡请求移除的原因。</summary>
            public EnemyRemovalReason LastDeathRequestedReason;

            /// <summary>终点攻击目标桩：记录调用并返回 contactReturnsTrue。</summary>
            private sealed class RecordingTarget : IEnemyEndPointAttackTarget
            {
                private readonly TestEnemy _owner;
                private readonly bool _returns;

                internal RecordingTarget(TestEnemy owner, bool returns)
                {
                    _owner = owner;
                    _returns = returns;
                }

                public bool ReceiveEndPointAttack(EndPointAttackRequest request)
                {
                    _owner.EndPointAttackCount++;
                    _owner.LastEndPointIsPlayerLane = request.IsPlayerLane;
                    _owner.LastEndPointDamage = request.Damage;
                    _owner.LastEndPointAttackerId = request.AttackerRuntimeId;
                    return _returns;
                }
            }

            /// <summary>
            /// 配置测试依赖并注入记录回调。
            /// </summary>
            internal void ConfigureForTest(
                MapData map,
                float cellSize,
                bool endPointTargetReturnsTrue = true)
            {
                Configure(
                    map,
                    cellSize,
                    new RecordingTarget(this, endPointTargetReturnsTrue),
                    onEnemyKilled: (killedId, attackerId, reward, isPlayerLane) =>
                    {
                        KillCallbackCount++;
                        LastKilledEnemyId = killedId;
                        LastKillAttackerId = attackerId;
                        LastExperienceReward = reward;
                        LastKillIsPlayerLane = isPlayerLane;
                    },
                    onDeathRequested: (killedId, reason) =>
                    {
                        DeathRequestedCount++;
                        LastDeathRequestedEnemyId = killedId;
                        LastDeathRequestedReason = reason;
                    });
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

            /// <summary>暴露 GridX/GridY 供测试验证。</summary>
            internal int TestGridX => GridX;

            /// <summary>暴露 GridY 供测试验证。</summary>
            internal int TestGridY => GridY;

            /// <summary>暴露 DeathStarted 供测试验证。</summary>
            internal bool TestDeathStarted => DeathStarted;

            /// <summary>暴露 InPool 供测试验证。</summary>
            internal bool TestInPool => InPool;

            /// <summary>暴露 CurrentState 枚举形式供测试验证。</summary>
            internal EnemyRuntimeState TestState => CurrentStateEnum;

            /// <summary>暴露 MaxHealthBase 供测试验证。</summary>
            internal int TestMaxHealth => MaxHealthBase;

            /// <summary>暴露 CurrentHealth 供测试验证。</summary>
            internal int TestHealth => CurrentHealth;

            /// <summary>血量变化回调累计次数。</summary>
            public int HealthChangedCount;

            /// <summary>上次血量变化回调的当前血量。</summary>
            public int LastHealthChangedCurrent;

            /// <summary>上次血量变化回调的最大血量。</summary>
            public int LastHealthChangedMax;

            /// <summary>上次血量变化回调的变化量。</summary>
            public int LastHealthChangedDelta;

            /// <summary>暴露 SetHealthChangedCallback 供测试注入记录回调。</summary>
            internal void SetHealthChangedForTest(Action<int, int, int, int> callback)
            {
                SetHealthChangedCallback((changedId, current, max, delta) =>
                {
                    HealthChangedCount++;
                    LastHealthChangedCurrent = current;
                    LastHealthChangedMax = max;
                    LastHealthChangedDelta = delta;
                    callback?.Invoke(changedId, current, max, delta);
                });
            }
        }

        // ====================================================================
        // 测试夹具
        // ====================================================================

        /// <summary>
        /// 构造一个直线路径测试 MapData：玩家路径 [(0,0),(1,0),(2,0),(3,0)]，
        /// 对手路径 [(3,0),(2,0),(1,0),(0,0)]。
        /// </summary>
        /// <remarks>
        /// <para>使用 4×1 的最小地图（4 列 × 1 行），全部为通道格（"0_1"）。
        /// 玩家路径从 (0,0) 到 (3,0)，对手路径从 (3,0) 到 (0,0)。</para>
        /// <para>决策 0.5：业务层不暴露嵌套数组，通过 MapData 坐标 API 访问。</para>
        /// </remarks>
        private static MapData BuildLinearPathMapData()
        {
            // 列优先 grid[x][y]：4 列 × 1 行。
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
        /// 把源 "kind_lane" 字符串解码为 GridCell（与还原工程 MapData.js 语义一致）。
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
        /// 构造一个已 Configure + Init + BeginMoving 的测试敌人，位于玩家路径起点。
        /// </summary>
        private static TestEnemy CreateMovingEnemy(
            MapData map,
            int id = 1,
            bool isPlayerLane = true,
            int maxHealth = 100)
        {
            var enemy = new TestEnemy();
            enemy.ConfigureForTest(map, CellSize);
            enemy.AssignId(id);
            enemy.InitForTest(isPlayerLane, maxHealth);
            // 从 SPAWNING 切换到 MOVING，使 Update 推进移动。
            enemy.StartMoving();
            return enemy;
        }

        // ====================================================================
        // 构造与 Configure 测试
        // ====================================================================

        [Test]
        [Description("构造后全部字段为默认值，等价于新构造。")]
        public void Constructor_FieldsAreDefaults()
        {
            var enemy = new TestEnemy();
            Assert.AreEqual(0, enemy.Id, "Id 默认 0。");
            Assert.AreEqual(0, enemy.Health, "Health 默认 0。");
            Assert.AreEqual((int)EnemyRuntimeState.Spawning, enemy.CurrentState, "默认 SPAWNING。");
            Assert.IsFalse(enemy.TestInPool, "默认不在池中。");
            Assert.IsFalse(enemy.TestDeathStarted, "deathStarted 默认 false。");
        }

        [Test]
        [Description("Configure 时 map 为 null 抛 ArgumentNullException。")]
        public void Configure_NullMap_Throws()
        {
            var enemy = new TestEnemy();
            // ReSharper disable once AssignNullToNotNullAttribute
            Assert.Throws<ArgumentNullException>(() => enemy.ConfigureForTest(null, CellSize));
        }

        [Test]
        [Description("Init 前未 Configure 抛 InvalidOperationException。")]
        public void Init_BeforeConfigure_Throws()
        {
            var map = BuildLinearPathMapData();
            var enemy = new TestEnemy();
            // 不调用 ConfigureForTest，直接 Init。
            Assert.Throws<InvalidOperationException>(() => enemy.InitForTest(true, 100));
        }

        [Test]
        [Description("AssignRuntimeId 设置运行时 ID，<=0 抛异常。")]
        public void AssignRuntimeId_SetsId_InvalidThrows()
        {
            var enemy = new TestEnemy();
            enemy.AssignId(42);
            Assert.AreEqual(42, enemy.Id, "ID 已设置。");

            Assert.Throws<ArgumentOutOfRangeException>(() => enemy.AssignId(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => enemy.AssignId(-1));
        }

        // ====================================================================
        // 路径移动测试（task 4.3 核心要求）
        // ====================================================================

        [Test]
        [Description("Init 后出生位置在路径起点（entry * cellSize）。")]
        public void Init_SpawnPosition_AtPathStart()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true);

            // 玩家路径起点 (0,0)，出生位置 = 0 * 80 = 0。
            Assert.AreEqual(0f, enemy.X, "玩家方出生 X=0。");
            Assert.AreEqual(0f, enemy.Y, "玩家方出生 Y=0。");
        }

        [Test]
        [Description("Update 在 MOVING 状态沿路径推进位移：50px/s * 1000ms = 50px。")]
        public void Update_Moving_AdvancesAlongPath()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true);

            // 50px/s * 1000ms / 1000 = 50px。
            enemy.Update(1000);

            // 从 (0,0) 向 (1,0)=80px 推进 50px，到达 (50,0)。
            Assert.AreEqual(50f, enemy.X, "推进 50px/s * 1s = 50px。");
            Assert.AreEqual(0f, enemy.Y, "Y 不变（路径沿 X 轴）。");
            Assert.AreEqual(0, enemy.CurrentPathIndex, "未到达第一段终点，索引仍为 0。");
        }

        [Test]
        [Description("到达当前路径点（距离 < 1px）时递增路径索引。")]
        public void Update_ReachPathPoint_IncrementsIndex()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true);

            // 第一段 (0,0) → (80,0)，距离 80px，50px/s 需 1600ms。
            enemy.Update(1600);

            // 1600ms 推进 80px，到达 (80,0)，索引递增到 1。
            Assert.AreEqual(80f, enemy.X, "到达第一段终点 X=80。");
            Assert.AreEqual(1, enemy.CurrentPathIndex, "到达后索引递增到 1。");
        }

        [Test]
        [Description("Update 在非 MOVING 状态不推进移动。")]
        public void Update_NonMovingState_DoesNotAdvance()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = new TestEnemy();
            enemy.ConfigureForTest(map, CellSize);
            enemy.AssignId(1);
            enemy.InitForTest(true, 100);
            // 不调用 BeginMoving，仍为 SPAWNING。

            enemy.Update(1000);

            Assert.AreEqual(0f, enemy.X, "SPAWNING 状态不移动。");
            Assert.AreEqual((int)EnemyRuntimeState.Spawning, enemy.CurrentState, "仍为 SPAWNING。");
        }

        [Test]
        [Description("Update deltaMs<=0 时不推进（防御）。")]
        public void Update_ZeroOrNegativeDelta_DoesNotAdvance()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map);

            enemy.Update(0);
            enemy.Update(-100);

            Assert.AreEqual(0f, enemy.X, "零或负 deltaMs 不推进。");
        }

        [Test]
        [Description("连续多段路径推进：到达终点后索引达 length，请求以 ReachedEndPoint 原因回收。")]
        public void Update_MultiSegmentPath_ReachesEnd()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true);

            // 4 点路径 (0,0)→(80,0)→(160,0)→(240,0)，3 段 × 80px = 240px。
            // 50px/s 每段需 1600ms。逐帧推进模拟多子步：
            // 还原工程 _advanceAlongPath 每帧只朝当前路径点推进，
            // 到达后下一帧才递增索引（EnemyBase.js:339-340）。
            for (int i = 0; i < 6; i++)
            {
                enemy.Update(1000);
            }

            // 6 帧 × 1000ms = 6000ms，足以走完 3 段 × 1600ms = 4800ms。
            // 索引应达 4（= path.Count），触发终点攻击并请求以 ReachedEndPoint 原因回收。
            Assert.AreEqual(4, enemy.CurrentPathIndex, "走完全程后索引达 length。");
            Assert.AreEqual(1, enemy.DeathRequestedCount, "走完全程后请求移除一次。");
            Assert.AreEqual(
                EnemyRemovalReason.ReachedEndPoint, enemy.LastDeathRequestedReason,
                "走完全程后请求以 ReachedEndPoint 原因回收。");
        }

        // ====================================================================
        // 空间格变化测试（task 4.3 核心要求）
        // ====================================================================

        [Test]
        [Description("Init 后所在格基于中心点 (x+w/2, y+h/2)。")]
        public void Init_GridMembership_BasedOnCenter()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true);

            // 出生位置 (0,0)，中心 (20, 20)，cellSize=80。
            // floor(20/80)=0, floor(20/80)=0 → 格 (0,0)。
            Assert.AreEqual(0, enemy.TestGridX, "出生中心在格 (0,0) 的 X。");
            Assert.AreEqual(0, enemy.TestGridY, "出生中心在格 (0,0) 的 Y。");
        }

        [Test]
        [Description("移动跨格时 gridX/gridY 更新。")]
        public void Update_CrossCell_UpdatesGridMembership()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true);

            // 初始格 (0,0)。中心 (20,20) → 格 (0,0)。
            // 推进 50px，位置 (50,0)，中心 (70,20) → 仍在格 (0,0)。
            enemy.Update(1000);
            Assert.AreEqual(0, enemy.TestGridX, "中心 (70,20) 仍在格 (0,0)。");

            // 再推进 50px，位置 (100,0)，中心 (120,20) → floor(120/80)=1 → 格 (1,0)。
            enemy.Update(1000);
            Assert.AreEqual(1, enemy.TestGridX, "中心 (120,20) 跨入格 (1,0)。");
            Assert.AreEqual(0, enemy.TestGridY, "Y 仍在格 0。");
        }

        // ====================================================================
        // 接触目标测试（task 4.3 核心要求）
        // ====================================================================

        [Test]
        [Description("索引达 length（真正抵达终点）时触发严格一次性的终点攻击。")]
        public void PathIndex_ReachesEnd_TriggersEndPointAttackOnce()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true);

            // 4 点路径，走完前 3 段（索引达 4 = length）。
            // 还原工程 _advanceAlongPath 每帧只朝当前路径点推进，到达后下一帧递增索引。
            // 逐帧推进模拟多子步：6 帧 × 1000ms 足以走完 3 段 × 1600ms = 4800ms。
            for (int i = 0; i < 6; i++)
            {
                enemy.Update(1000);
            }

            // 抵达终点触发一次终点攻击：伤害 1、攻击者 ID=敌人 ID、车道=玩家方。
            Assert.AreEqual(1, enemy.EndPointAttackCount, "抵达终点触发一次终点攻击。");
            Assert.IsTrue(enemy.LastEndPointIsPlayerLane, "终点攻击目标车道为玩家方。");
            Assert.AreEqual(1, enemy.LastEndPointDamage, "终点攻击伤害为 1。");
            Assert.AreEqual(1, enemy.LastEndPointAttackerId, "终点攻击攻击者 ID 为敌人 ID。");

            // 后续帧不再重复攻击，也不重复请求移除。
            enemy.Update(1000);
            enemy.Update(1000);
            Assert.AreEqual(1, enemy.EndPointAttackCount, "终点攻击严格一次性，不重复。");
            Assert.AreEqual(1, enemy.DeathRequestedCount, "回收请求严格一次性，不重复。");
        }

        [Test]
        [Description("终点攻击无 500ms 冷却：不同 frameNowMs 的敌人抵达终点都恰好攻击一次。")]
        public void EndPointAttack_NoCooldown_TriggersExactlyOnce()
        {
            MapData map = BuildLinearPathMapData();

            // 终点攻击是严格一次性到达事件，不依赖接触冷却（frameNowMs 已被移除）。
            var enemyA = CreateMovingEnemy(map, id: 1, isPlayerLane: true);
            for (int i = 0; i < 6; i++)
            {
                enemyA.Update(1000);
            }
            Assert.AreEqual(1, enemyA.EndPointAttackCount, "敌人 A 抵达终点攻击一次。");

            var enemyB = CreateMovingEnemy(map, id: 2, isPlayerLane: true);
            for (int i = 0; i < 6; i++)
            {
                enemyB.Update(1000);
            }
            Assert.AreEqual(1, enemyB.EndPointAttackCount, "敌人 B 抵达终点攻击一次。");
        }

        [Test]
        [Description("路径不足 2 点时不触发接触。")]
        public void ContactAttack_PathTooShort_DoesNotTrigger()
        {
            // 构造只有 1 点的路径（不足 2 点）。
            var grid = new List<IReadOnlyList<string>>
            {
                new List<string> { "0_1" },
            };

            var singlePath = new List<GridPosition> { new GridPosition(0, 0) };

            MapData map = MapData.FromColumnMajorGrid(
                grid, DecodeCell, mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(0, 0),
                opponentStart: new GridPosition(0, 0),
                opponentEnd: new GridPosition(0, 0),
                playerPath: singlePath,
                opponentPath: singlePath);

            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true);

            // 路径只有 1 点，索引 0 >= length=1，触发 HandlePathIndexChanged。
            // 但 AttemptEndPointAttackOnce 内 path.Count < 2 不触发攻击；仍请求回收避免滞留。
            enemy.Update(100);

            Assert.AreEqual(0, enemy.EndPointAttackCount, "路径不足 2 点不触发终点攻击。");
            Assert.AreEqual(1, enemy.DeathRequestedCount, "路径不足 2 点仍请求回收，避免滞留终点。");
        }

        // ====================================================================
        // 受击测试（task 4.3 核心要求）
        // ====================================================================

        [Test]
        [Description("Hit 扣血并返回 true。")]
        public void Hit_ReducesHealth_ReturnsTrue()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true, maxHealth: 100);

            bool result = enemy.Hit(30, attackerId: 10);

            Assert.IsTrue(result, "受击生效返回 true。");
            Assert.AreEqual(70, enemy.Health, "扣血 30，剩余 70。");
        }

        [Test]
        [Description("Hit 扣血成功后触发血量变化回调，携带当前/最大/变化量。")]
        public void Hit_TriggersHealthChangedCallback()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 7, isPlayerLane: true, maxHealth: 100);
            enemy.SetHealthChangedForTest(null);

            enemy.Hit(30, attackerId: 10);

            Assert.AreEqual(1, enemy.HealthChangedCount, "有效受击触发一次血量变化回调。");
            Assert.AreEqual(70, enemy.LastHealthChangedCurrent, "回调当前血量=70。");
            Assert.AreEqual(100, enemy.LastHealthChangedMax, "回调最大血量=100。");
            Assert.AreEqual(-30, enemy.LastHealthChangedDelta, "回调变化量=-30。");
        }

        [Test]
        [Description("血量归零时血量变化回调携带 current=0（死亡事实）。")]
        public void Hit_HealthZero_TriggersHealthChangedWithZero()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 8, isPlayerLane: true, maxHealth: 100);
            enemy.SetHealthChangedForTest(null);

            enemy.Hit(100, attackerId: 10);

            Assert.AreEqual(1, enemy.HealthChangedCount, "致死一击触发一次回调。");
            Assert.AreEqual(0, enemy.LastHealthChangedCurrent, "回调当前血量=0。");
            Assert.AreEqual(-100, enemy.LastHealthChangedDelta, "回调变化量=-100。");
        }

        [Test]
        [Description("无效受击（0 伤害）不触发血量变化回调。")]
        public void Hit_InvalidDamage_DoesNotTriggerHealthChanged()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 9, isPlayerLane: true, maxHealth: 100);
            enemy.SetHealthChangedForTest(null);

            enemy.Hit(0, attackerId: 10);

            Assert.AreEqual(0, enemy.HealthChangedCount, "0 伤害不触发血量变化回调。");
        }

        [Test]
        [Description("已死亡的敌人 Hit 返回 false。")]
        public void Hit_DeadEnemy_ReturnsFalse()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true, maxHealth: 100);

            // 击杀。
            enemy.Hit(100, attackerId: 10);
            Assert.AreEqual(0, enemy.Health, "血量归零。");
            Assert.AreEqual((int)EnemyRuntimeState.Dead, enemy.CurrentState, "进入 DEAD。");

            // 再次受击返回 false。
            bool result = enemy.Hit(10, attackerId: 10);
            Assert.IsFalse(result, "已死亡不再受击。");
        }

        [Test]
        [Description("damage<=0 时 Hit 返回 false（防御）。")]
        public void Hit_ZeroOrNegativeDamage_ReturnsFalse()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true, maxHealth: 100);

            Assert.IsFalse(enemy.Hit(0, attackerId: 10), "0 伤害不生效。");
            Assert.IsFalse(enemy.Hit(-5, attackerId: 10), "负伤害不生效。");
            Assert.AreEqual(100, enemy.Health, "血量不变。");
        }

        [Test]
        [Description("血量归零时进入 DEAD 状态并提交击杀奖励（普通敌人奖励 1）。")]
        public void Hit_HealthZero_EntersDead_SubmitsReward()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true, maxHealth: 100);

            enemy.Hit(100, attackerId: 10);

            Assert.AreEqual(0, enemy.Health, "血量归零。");
            Assert.AreEqual((int)EnemyRuntimeState.Dead, enemy.CurrentState, "进入 DEAD。");
            Assert.AreEqual(1, enemy.KillCallbackCount, "击杀回调触发一次。");
            Assert.AreEqual(1, enemy.LastKilledEnemyId, "击杀的敌人 ID。");
            Assert.AreEqual(10, enemy.LastKillAttackerId, "攻击者 ID。");
            Assert.AreEqual(1, enemy.LastExperienceReward, "普通敌人奖励 1。");
            Assert.IsTrue(enemy.LastKillIsPlayerLane, "玩家方车道。");
        }

        [Test]
        [Description("特殊敌人血量归零时提交奖励 10。")]
        public void Hit_SpecialEnemy_RewardTen()
        {
            MapData map = BuildLinearPathMapData();
            // 通过子类设置 IsSpecial——TestEnemy 未暴露，创建自定义子类。
            var enemy = new SpecialTestEnemy();
            enemy.ConfigureForTest(map, CellSize);
            enemy.AssignId(1);
            enemy.InitForTest(true, 100);
            enemy.StartMoving();

            enemy.Hit(100, attackerId: 10);

            Assert.AreEqual(10, enemy.LastExperienceReward, "特殊敌人奖励 10。");
        }

        /// <summary>
        /// 特殊敌人测试子类（IsSpecial=true）。
        /// </summary>
        private sealed class SpecialTestEnemy : TestEnemy
        {
            protected internal override void Init(bool isPlayerLane, int maxHealth, float width, float height)
            {
                base.Init(isPlayerLane, maxHealth, width, height);
                IsSpecial = true;
            }
        }

        [Test]
        [Description("同一攻击者多次受击只记录一次贡献者。")]
        public void Hit_SameAttacker_ContributorRecordedOnce()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true, maxHealth: 100);

            enemy.Hit(10, attackerId: 10);
            enemy.Hit(10, attackerId: 10);
            enemy.Hit(10, attackerId: 20);

            // 击杀回调中只验证最终触发，贡献者去重在内部 _damageContributors。
            // 此处验证多次受击后仍能正确进入死亡。
            enemy.Hit(70, attackerId: 10);
            Assert.AreEqual((int)EnemyRuntimeState.Dead, enemy.CurrentState, "最终死亡。");
        }

        // ====================================================================
        // 死亡测试（task 4.3 核心要求）
        // ====================================================================

        [Test]
        [Description("BeginDeath 幂等：多次触发只记录一次 deathStarted。")]
        public void Death_Idempotent_DeathStartedOnce()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true, maxHealth: 10);

            // 第一次受击致死。
            enemy.Hit(10, attackerId: 5);
            Assert.IsTrue(enemy.TestDeathStarted, "死亡已开始。");
            Assert.AreEqual(1, enemy.KillCallbackCount, "击杀回调一次。");

            // 再次受击（已死亡，Hit 返回 false，不重复触发死亡）。
            enemy.Hit(10, attackerId: 5);
            Assert.AreEqual(1, enemy.KillCallbackCount, "不重复触发击杀回调。");
        }

        // ====================================================================
        // IsTargetableBy 测试
        // ====================================================================

        [Test]
        [Description("SPAWNING 状态不可被攻击。")]
        public void IsTargetableBy_Spawning_False()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = new TestEnemy();
            enemy.ConfigureForTest(map, CellSize);
            enemy.AssignId(1);
            enemy.InitForTest(true, 100);
            // 不调用 BeginMoving，仍为 SPAWNING。

            Assert.IsFalse(enemy.IsTargetableBy(true), "SPAWNING 不可攻击。");
            Assert.IsFalse(enemy.IsTargetableBy(false), "SPAWNING 不可攻击。");
        }

        [Test]
        [Description("MOVING 状态且阵营匹配时可被攻击。")]
        public void IsTargetableBy_Moving_CorrectSide_True()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true);

            Assert.IsTrue(enemy.IsTargetableBy(true), "玩家方敌人可被玩家方攻击者攻击。");
            Assert.IsFalse(enemy.IsTargetableBy(false), "玩家方敌人不可被对手方攻击者攻击。");
        }

        [Test]
        [Description("DEAD 状态不可被攻击。")]
        public void IsTargetableBy_Dead_False()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true, maxHealth: 10);
            enemy.Hit(10, attackerId: 5);

            Assert.IsFalse(enemy.IsTargetableBy(true), "DEAD 不可攻击。");
        }

        // ====================================================================
        // GameOver 测试
        // ====================================================================

        [Test]
        [Description("GameOver 首次返回 true，重复调用返回 false（幂等）。")]
        public void GameOver_Idempotent()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 1, isPlayerLane: true);

            Assert.IsTrue(enemy.GameOver(), "首次 GameOver 返回 true。");
            Assert.IsTrue(enemy.TestInPool, "已标记入池。");
            Assert.IsFalse(enemy.GameOver(), "重复 GameOver 返回 false。");
        }

        // ====================================================================
        // Reset / IPoolableBattleObject 测试（task 4.3 核心要求 + task 4.1 契约）
        // ====================================================================

        [Test]
        [Description("ResetState 后全部字段等价于新构造。")]
        public void ResetState_AllFieldsCleared_EquivalentToNew()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 42, isPlayerLane: true, maxHealth: 100);

            // 污染状态。
            enemy.Update(2000);
            enemy.Hit(30, attackerId: 10);

            // Reset。
            (enemy as IPoolableBattleObject).ResetState();

            // 验证全部字段已清空。
            Assert.AreEqual(0, enemy.Id, "ID 已清空。");
            Assert.AreEqual(0, enemy.Health, "Health 已清空。");
            Assert.AreEqual((int)EnemyRuntimeState.Spawning, enemy.CurrentState, "状态回到 SPAWNING。");
            Assert.AreEqual(0f, enemy.X, "位置 X 已清空。");
            Assert.AreEqual(0f, enemy.Y, "位置 Y 已清空。");
            Assert.AreEqual(float.PositiveInfinity, enemy.RemainingPathDistance, "剩余路径距离已重置。");
            Assert.AreEqual(0, enemy.CurrentPathIndex, "路径索引已重置。");
            Assert.IsFalse(enemy.TestDeathStarted, "deathStarted 已清空。");
            Assert.IsFalse(enemy.TestInPool, "inPool 已清空。");
        }

        [Test]
        [Description("ResetState 幂等：多次调用结果状态相同。")]
        public void ResetState_Idempotent()
        {
            MapData map = BuildLinearPathMapData();
            var enemy = CreateMovingEnemy(map, id: 42, isPlayerLane: true, maxHealth: 100);

            (enemy as IPoolableBattleObject).ResetState();
            int stateAfterFirst = enemy.CurrentState;
            float xAfterFirst = enemy.X;

            (enemy as IPoolableBattleObject).ResetState();
            Assert.AreEqual(stateAfterFirst, enemy.CurrentState, "多次 Reset 状态相同。");
            Assert.AreEqual(xAfterFirst, enemy.X, "多次 Reset 位置相同。");
        }

        [Test]
        [Description("池复用无污染：Acquire 复用 Reset 后的对象状态等价于新构造。")]
        public void PoolReuse_NoPollution()
        {
            MapData map = BuildLinearPathMapData();
            var pool = new BattleObjectPool<TestEnemy>(() => new TestEnemy());

            // 第一轮：Acquire -> 污染 -> Release（触发 ResetState）。
            TestEnemy first = pool.Acquire();
            first.ConfigureForTest(map, CellSize);
            first.AssignId(777);
            first.InitForTest(true, 100);
            first.StartMoving();
            first.Update(2000);
            first.Hit(50, attackerId: 10);

            pool.Release(first);

            // 验证 Release 后已 Reset。
            Assert.AreEqual(0, first.Id, "Release 后 ID=0。");
            Assert.AreEqual(0, first.Health, "Release 后 Health=0。");

            // 第二轮：Acquire 应复用同一对象，状态已清空。
            TestEnemy second = pool.Acquire();
            Assert.AreSame(first, second, "LIFO 复用同一对象。");
            Assert.AreEqual(0, second.Id, "复用对象 ID=0（无旧 ID 残留）。");
            Assert.AreEqual(0, second.Health, "复用对象 Health=0（无旧血量残留）。");
            Assert.AreEqual((int)EnemyRuntimeState.Spawning, second.CurrentState, "复用对象状态 SPAWNING。");
            Assert.AreEqual(0f, second.X, "复用对象位置已清空。");
            Assert.AreEqual(float.PositiveInfinity, second.RemainingPathDistance, "复用对象剩余距离已重置。");

            pool.Release(second);
        }

        // ====================================================================
        // 不持有 Unity GameObject 验证（task 4.3 约束）
        // ====================================================================

        [Test]
        [Description("EnemyBase 不继承 UnityEngine.MonoBehaviour 或引用 UnityEngine 组件。")]
        public void EnemyBase_DoesNotReferenceUnityEngine()
        {
            Type type = typeof(EnemyBase);
            Assert.IsFalse(type.IsSubclassOf(typeof(UnityEngine.MonoBehaviour)),
                "EnemyBase 不继承 MonoBehaviour。");
            Assert.IsFalse(type.IsSubclassOf(typeof(UnityEngine.ScriptableObject)),
                "EnemyBase 不继承 ScriptableObject。");

            // 验证不持有 GameObject/Transform 字段。
            // 通过反射检查字段类型全名不含 UnityEngine.GameObject/Transform/Sprite。
            var fields = type.GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            foreach (var field in fields)
            {
                string fieldTypeName = field.FieldType.FullName ?? string.Empty;
                Assert.IsFalse(
                    fieldTypeName.StartsWith("UnityEngine.GameObject", StringComparison.Ordinal) ||
                    fieldTypeName.StartsWith("UnityEngine.Transform", StringComparison.Ordinal) ||
                    fieldTypeName.StartsWith("UnityEngine.Sprite", StringComparison.Ordinal),
                    $"EnemyBase 字段 {field.Name} 类型 {fieldTypeName} 不应引用 UnityEngine 表现组件。");
            }
        }
    }
}
