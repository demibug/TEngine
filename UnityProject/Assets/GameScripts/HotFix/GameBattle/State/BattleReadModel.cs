namespace GameBattle
{
    // ============================================================================
    // 任务 3.7：BattleReadModel —— 不暴露内部集合的只读状态视图
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / State/BattleReadModel.cs）：
    //   生成 UI/诊断只读快照，避免暴露内部集合和实体。
    //
    // 决策依据（design.md / spec battle-runtime-lifecycle / task 3.7 验证要求）：
    //   - BattleReadModel 不暴露内部集合：Presenter/诊断只能通过本类型提供的只读属性
    //     或不可变快照方法访问状态，不能拿到 BattleState 或 Manager 的内部引用。
    //   - 状态修改只允许经规则服务提交：BattleReadModel 只读，不提供任何写入口。
    //   - 不暴露实体引用：本类型只返回标量与不可变快照，不返回 Manager/Entity 引用。
    //
    // 不变量：
    //   1. 只读：本类型不持有可变状态，不提供任何 setter/Apply 方法。
    //   2. 不暴露内部集合：快照方法返回不可变结构或标量副本。
    //   3. 实时性：本类型直接读 BattleState 当前值，不缓存（避免脏读）；
    //      若 Presenter 需要帧快照，由 Presenter 自身复制快照。
    // ============================================================================

    /// <summary>
    /// 不暴露内部集合的只读状态视图，供 UI/诊断消费。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md State/BattleReadModel.cs）：</b>生成 UI/诊断只读快照，
    /// 避免暴露内部集合和实体。</para>
    ///
    /// <para><b>不暴露内部集合（task 3.7 验证要求）：</b>本类型只暴露标量只读属性与
    /// 不可变快照方法 <see cref="Snapshot"/>，不返回 <see cref="BattleState"/> 或任何
    /// Manager 的内部集合引用。Presenter 无法通过本类型修改规则状态。</para>
    ///
    /// <para><b>状态修改只允许经规则服务提交（task 3.7 验证要求）：</b>
    /// 本类型只读，不提供任何 setter/Apply 方法。状态变更经 <see cref="BattleState"/>
    /// 的 Apply* 方法由规则服务提交。</para>
    ///
    /// <para><b>不暴露实体引用：</b>本类型只返回标量与不可变快照结构，不返回
    /// Enemy/Unit/Projectile 等实体引用。实体只读视图由各自 Manager 的只读查询提供。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 Presenter/诊断使用。
    /// 跨程序集的只读事实通过 <c>GameCommon.Battle.BattleResultDto</c> 传递。</para>
    /// </remarks>
    internal sealed class BattleReadModel
    {
        /// <summary>
        /// 关联的权威状态。只读访问，不持有可变副本。
        /// </summary>
        private readonly BattleState _state;

        /// <summary>
        /// 关联的运行时 ID 分配器，用于诊断快照。
        /// </summary>
        private readonly RuntimeIdAllocator _idAllocator;

        /// <summary>
        /// 槽位面板（最终方案：提供槽位只读快照）。可为 null（旧路径降级）。
        /// </summary>
        private readonly UnitSlotBoard _slotBoard;

        /// <summary>
        /// 构造只读视图。
        /// </summary>
        /// <param name="state">权威状态（非 null）。</param>
        /// <param name="idAllocator">ID 分配器（非 null）。</param>
        /// <param name="slotBoard">槽位面板（最终方案，可为 null）。</param>
        internal BattleReadModel(BattleState state, RuntimeIdAllocator idAllocator, UnitSlotBoard slotBoard = null)
        {
            _state = state ?? throw new System.ArgumentNullException(nameof(state));
            _idAllocator = idAllocator ?? throw new System.ArgumentNullException(nameof(idAllocator));
            _slotBoard = slotBoard;
        }

        /// <summary>
        /// 槽位只读快照（最终方案：供 UI/表现层查询待上场与战场槽）。
        /// </summary>
        /// <returns>槽位只读快照；槽位面板未注入时返回空快照（旧路径）。</returns>
        internal UnitSlotSnapshot SlotSnapshot()
            => _slotBoard != null ? _slotBoard.Snapshot() : default;

        // ====================================================================
        // 标量只读属性 —— 直接委托 BattleState，不缓存
        // ====================================================================

        /// <summary>当前波次。</summary>
        public int CurrentRound => _state.CurrentRound;

        /// <summary>玩家方当前生命。</summary>
        public int PlayerHealth => _state.PlayerHealth;

        /// <summary>玩家方最大生命。</summary>
        public int PlayerMaxHealth => _state.PlayerMaxHealth;

        /// <summary>玩家方当前金币。</summary>
        public int PlayerGold => _state.PlayerGold;

        /// <summary>对手方当前生命。</summary>
        public int OpponentHealth => _state.OpponentHealth;

        /// <summary>对手方最大生命。</summary>
        public int OpponentMaxHealth => _state.OpponentMaxHealth;

        /// <summary>对手方当前金币。</summary>
        public int OpponentGold => _state.OpponentGold;

        /// <summary>总击杀数。</summary>
        public int KillCount => _state.KillCount;

        /// <summary>Boss 击杀数（本期固定 0）。</summary>
        public int BossKillCount => _state.BossKillCount;

        /// <summary>是否已结束。</summary>
        public bool IsGameOver => _state.IsGameOver;

        /// <summary>是否已发生接触伤害。</summary>
        public bool ContactOccurred => _state.ContactOccurred;

        /// <summary>战斗开始时间戳（毫秒）。</summary>
        public long StartTimeMs => _state.StartTimeMs;

        /// <summary>结果星级（0~3）。</summary>
        public int ResultStar => _state.ResultStar;

        /// <summary>玩家方招募刷新消耗。</summary>
        public int PlayerRecruitCost => _state.PlayerRecruitCost;

        /// <summary>对手方招募刷新消耗。</summary>
        public int OpponentRecruitCost => _state.OpponentRecruitCost;

        /// <summary>最大波次。</summary>
        public int MaxRounds => _state.MaxRounds;

        /// <summary>是否无尽模式。</summary>
        public bool EndlessMode => _state.EndlessMode;

        // ====================================================================
        // 不可变快照方法
        // ====================================================================

        /// <summary>
        /// 生成当前状态的不可变快照，供 Presenter/诊断安全持有。
        /// </summary>
        /// <returns>不可变状态快照结构。</returns>
        /// <remarks>
        /// 快照为值类型副本，调用后与 BattleState 后续变更隔离。
        /// Presenter 可安全持有快照进行帧间插值/同步，不影响规则状态。
        /// </remarks>
        internal BattleStateSnapshot Snapshot()
        {
            return new BattleStateSnapshot(
                currentRound: _state.CurrentRound,
                playerHealth: _state.PlayerHealth,
                playerMaxHealth: _state.PlayerMaxHealth,
                playerGold: _state.PlayerGold,
                opponentHealth: _state.OpponentHealth,
                opponentMaxHealth: _state.OpponentMaxHealth,
                opponentGold: _state.OpponentGold,
                killCount: _state.KillCount,
                bossKillCount: _state.BossKillCount,
                isGameOver: _state.IsGameOver,
                contactOccurred: _state.ContactOccurred,
                startTimeMs: _state.StartTimeMs,
                resultStar: _state.ResultStar,
                lastRuntimeId: _idAllocator.LastAllocatedId);
        }

        /// <summary>
        /// 生成用于结果冻结的只读标量集合（供 BattleResultBuilder 读取）。
        /// </summary>
        /// <returns>不可变结果输入快照。</returns>
        internal BattleResultInputs SnapshotResultInputs()
        {
            return new BattleResultInputs(
                isGameOver: _state.IsGameOver,
                playerHealth: _state.PlayerHealth,
                playerMaxHealth: _state.PlayerMaxHealth,
                opponentHealth: _state.OpponentHealth,
                playerGold: _state.PlayerGold,
                currentRound: _state.CurrentRound,
                killCount: _state.KillCount,
                bossKillCount: _state.BossKillCount,
                endlessMode: _state.EndlessMode,
                resultStar: _state.ResultStar,
                startTimeMs: _state.StartTimeMs);
        }
    }

    /// <summary>
    /// 不可变战斗状态快照（值类型副本，供 Presenter/诊断安全持有）。
    /// </summary>
    internal readonly struct BattleStateSnapshot
    {
        public readonly int CurrentRound;
        public readonly int PlayerHealth;
        public readonly int PlayerMaxHealth;
        public readonly int PlayerGold;
        public readonly int OpponentHealth;
        public readonly int OpponentMaxHealth;
        public readonly int OpponentGold;
        public readonly int KillCount;
        public readonly int BossKillCount;
        public readonly bool IsGameOver;
        public readonly bool ContactOccurred;
        public readonly long StartTimeMs;
        public readonly int ResultStar;
        public readonly int LastRuntimeId;

        internal BattleStateSnapshot(
            int currentRound,
            int playerHealth,
            int playerMaxHealth,
            int playerGold,
            int opponentHealth,
            int opponentMaxHealth,
            int opponentGold,
            int killCount,
            int bossKillCount,
            bool isGameOver,
            bool contactOccurred,
            long startTimeMs,
            int resultStar,
            int lastRuntimeId)
        {
            CurrentRound = currentRound;
            PlayerHealth = playerHealth;
            PlayerMaxHealth = playerMaxHealth;
            PlayerGold = playerGold;
            OpponentHealth = opponentHealth;
            OpponentMaxHealth = opponentMaxHealth;
            OpponentGold = opponentGold;
            KillCount = killCount;
            BossKillCount = bossKillCount;
            IsGameOver = isGameOver;
            ContactOccurred = contactOccurred;
            StartTimeMs = startTimeMs;
            ResultStar = resultStar;
            LastRuntimeId = lastRuntimeId;
        }
    }

    /// <summary>
    /// 不可变结果冻结输入快照（供 BattleResultBuilder 读取的只读标量集合）。
    /// </summary>
    internal readonly struct BattleResultInputs
    {
        public readonly bool IsGameOver;
        public readonly int PlayerHealth;
        public readonly int PlayerMaxHealth;
        public readonly int OpponentHealth;
        public readonly int PlayerGold;
        public readonly int CurrentRound;
        public readonly int KillCount;
        public readonly int BossKillCount;
        public readonly bool EndlessMode;
        public readonly int ResultStar;
        public readonly long StartTimeMs;

        internal BattleResultInputs(
            bool isGameOver,
            int playerHealth,
            int playerMaxHealth,
            int opponentHealth,
            int playerGold,
            int currentRound,
            int killCount,
            int bossKillCount,
            bool endlessMode,
            int resultStar,
            long startTimeMs)
        {
            IsGameOver = isGameOver;
            PlayerHealth = playerHealth;
            PlayerMaxHealth = playerMaxHealth;
            OpponentHealth = opponentHealth;
            PlayerGold = playerGold;
            CurrentRound = currentRound;
            KillCount = killCount;
            BossKillCount = bossKillCount;
            EndlessMode = endlessMode;
            ResultStar = resultStar;
            StartTimeMs = startTimeMs;
        }
    }
}
