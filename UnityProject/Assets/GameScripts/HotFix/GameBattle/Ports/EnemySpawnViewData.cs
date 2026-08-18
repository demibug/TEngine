using System;

namespace GameBattle
{
    /// <summary>敌对实体的表现种类。</summary>
    public enum EnemyPresentationKind
    {
        Normal = 1,
        Boss = 2,
    }

    // ============================================================================
    // 任务 5.1：EnemySpawnViewData —— 敌人出生表现 DTO
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 7 / specs/configured-enemy-spawning/spec.md / task 5.1）：
    //   把敌人出生表现参数收敛为不可变 DTO，贯通 EnemyManager.EnemySpawned →
    //   BattlePresenter → IBattleViewPort.OnEnemySpawned → Null/Unity 端口。
    //   表现层不再接收散参数，也不允许在端口内回退到固定 Mob0。
    //
    // 字段语义：
    //   - RuntimeId：本次租借的运行时 ID（RuntimeIdAllocator 分配）。
    //   - EnemyKey：普通敌人键（Mob0/Mob1/Mob2/Mob3）；Boss 使用 BossKey。
    //   - ResourceAddress：普通敌人来自 EnemyDefinitionSnapshot，Boss 来自 BossDefinitionSnapshot；
    //     两者复用同一个表现预加载池键。
    //   - IsPlayerLane / LogicX / LogicY：出生车道与逻辑坐标。
    //
    // 不变量：
    //   1. 不可变：get-only 属性，构造后不可修改。
    //   2. 空串策略：空 EnemyKey/ResourceAddress 表示"非生产普通敌人"，Unity 端口显式
    //      抛 BattlePresentationLoadException，不创建占位表现。
    //   3. 纯表现载荷：只携带标量与键，不持有 Enemy 实体或 Unity 对象引用。
    // ============================================================================

    /// <summary>
    /// 敌人出生表现 DTO：runtimeId + enemyKey/resourceAddress + 车道与逻辑坐标。
    /// </summary>
    /// <remarks>
    /// <para><b>贯通链（task 5.1）：</b>由 <see cref="EnemyManager"/> 在登记敌人时构造并
    /// 经 <see cref="EnemyManager.EnemySpawned"/> 发布；<see cref="BattlePresenter"/>
    /// 原样转发；<see cref="IBattleViewPort.OnEnemySpawned"/> 的 Null 实现忽略，
    /// <see cref="UnityBattleViewPort"/> 按 <see cref="ResourceAddress"/> 选择已预加载
    /// Prefab 与对应表现池，不再固定 <c>enemy_mob0</c>（design.md:131）。</para>
    ///
    /// <para><b>不可变：</b>get-only 属性；构造后不得修改。</para>
    ///
    /// <para><b>非生产兼容（spec "Reject an unsupported normal enemy"）：</b>
    /// 非配置化实体（测试替身/技能召唤占位）经本 DTO 传递时 <see cref="EnemyKey"/> 与
    /// <see cref="ResourceAddress"/> 为空串，Unity 端口显式失败而非静默回退 Mob0。</para>
    /// </remarks>
    public sealed class EnemySpawnViewData
    {
        /// <summary>敌人运行时 ID（由 RuntimeIdAllocator 每次租借分配）。</summary>
        public int RuntimeId { get; }

        /// <summary>普通敌人键（Mob0/Mob1/Mob2/Mob3；非普通实体为空串）。</summary>
        public string EnemyKey { get; }

        /// <summary>表现预加载池键（普通敌人 ResourceAddress 或 Boss ResourcePath）。</summary>
        public string ResourceAddress { get; }

        /// <summary>是否玩家方车道。</summary>
        public bool IsPlayerLane { get; }

        /// <summary>生成点逻辑 X 坐标。</summary>
        public float LogicX { get; }

        /// <summary>生成点逻辑 Y 坐标。</summary>
        public float LogicY { get; }

        /// <summary>普通敌人或 Boss。</summary>
        public EnemyPresentationKind Kind { get; }

        /// <summary>Boss 键；普通敌人为空。</summary>
        public string BossKey { get; }

        /// <summary>Boss 逻辑宽度；普通敌人为 0。</summary>
        public float LogicalWidth { get; }

        /// <summary>Boss 逻辑高度；普通敌人为 0。</summary>
        public float LogicalHeight { get; }

        /// <summary>Boss 待机/移动动画键；普通敌人为空。</summary>
        public string IdleAnimationKey { get; }

        /// <summary>Boss 技能动画键；普通敌人为空。</summary>
        public string SkillAnimationKey { get; }

        /// <summary>构造敌人出生表现 DTO。</summary>
        /// <param name="runtimeId">敌人运行时 ID。</param>
        /// <param name="enemyKey">普通敌人键（可为空串）。</param>
        /// <param name="resourceAddress">YooAsset 资源地址（可为空串）。</param>
        /// <param name="isPlayerLane">是否玩家方车道。</param>
        /// <param name="logicX">生成点逻辑 X 坐标。</param>
        /// <param name="logicY">生成点逻辑 Y 坐标。</param>
        public EnemySpawnViewData(
            int runtimeId,
            string enemyKey,
            string resourceAddress,
            bool isPlayerLane,
            float logicX,
            float logicY,
            EnemyPresentationKind kind = EnemyPresentationKind.Normal,
            string bossKey = null,
            float logicalWidth = 0f,
            float logicalHeight = 0f,
            string idleAnimationKey = null,
            string skillAnimationKey = null)
        {
            RuntimeId = runtimeId;
            EnemyKey = enemyKey ?? string.Empty;
            ResourceAddress = resourceAddress ?? string.Empty;
            IsPlayerLane = isPlayerLane;
            LogicX = logicX;
            LogicY = logicY;
            Kind = kind;
            BossKey = bossKey ?? string.Empty;
            LogicalWidth = logicalWidth;
            LogicalHeight = logicalHeight;
            IdleAnimationKey = idleAnimationKey ?? string.Empty;
            SkillAnimationKey = skillAnimationKey ?? string.Empty;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"EnemySpawnViewData(runtimeId={RuntimeId}, enemyKey={EnemyKey}, " +
                   $"resourceAddress={ResourceAddress}, isPlayerLane={IsPlayerLane}, " +
                   $"kind={Kind}, x={LogicX}, y={LogicY})";
        }
    }
}
