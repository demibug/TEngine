using TEngine;

namespace GameCommon.Battle
{
    /// <summary>
    /// 战斗 UI 的强类型事件契约。
    /// </summary>
    /// <remarks>
    /// 该接口与其他 <see cref="EventInterfaceAttribute"/> 契约统一归属 GameCommon，
    /// 确保仅由 GameCommon 程序集生成 GameEventHelper，并由热更组合根单点初始化。
    /// 战斗内部信号仍由 GameBattle 的 BattleInternalSignalHub 管理。
    /// </remarks>
    [EventInterface(EEventGroup.GroupLogic)]
    public interface IBattleUiEvent
    {
        /// <summary>战斗目标生命变化。</summary>
        void OnHealthChanged(HealthChangedUiFact fact);

        /// <summary>金币变化。</summary>
        void OnGoldChanged(GoldChangedUiFact fact);

        /// <summary>波次生成计划已准备完成。</summary>
        void OnRoundSpawnPrepared(RoundSpawnPreparedUiFact fact);

        /// <summary>战斗结果已冻结。</summary>
        void OnBattleFrozen(BattleFrozenUiFact fact);
    }

    /// <summary>
    /// 生命变化 UI 事实载荷。
    /// </summary>
    public readonly struct HealthChangedUiFact
    {
        /// <summary>是否为玩家方目标。</summary>
        public readonly bool IsPlayerSide;

        /// <summary>变化后的当前生命。</summary>
        public readonly int CurrentHealth;

        /// <summary>本次变化量，正为恢复，负为伤害。</summary>
        public readonly int Delta;

        /// <summary>构造不可变生命变化 UI 事实。</summary>
        public HealthChangedUiFact(bool isPlayerSide, int currentHealth, int delta)
        {
            IsPlayerSide = isPlayerSide;
            CurrentHealth = currentHealth;
            Delta = delta;
        }
    }

    /// <summary>
    /// 金币变化 UI 事实载荷。
    /// </summary>
    public readonly struct GoldChangedUiFact
    {
        /// <summary>是否为玩家方。</summary>
        public readonly bool IsPlayerSide;

        /// <summary>变化后的当前金币。</summary>
        public readonly int CurrentGold;

        /// <summary>本次变化量，正为获得，负为消耗。</summary>
        public readonly int Delta;

        /// <summary>构造不可变金币变化 UI 事实。</summary>
        public GoldChangedUiFact(bool isPlayerSide, int currentGold, int delta)
        {
            IsPlayerSide = isPlayerSide;
            CurrentGold = currentGold;
            Delta = delta;
        }
    }

    /// <summary>
    /// 波次生成计划准备完成 UI 事实载荷。
    /// </summary>
    public readonly struct RoundSpawnPreparedUiFact
    {
        /// <summary>当前波次序号。</summary>
        public readonly int Round;

        /// <summary>本波次计划敌人数量。</summary>
        public readonly int EnemyCount;

        /// <summary>构造不可变波次准备 UI 事实。</summary>
        public RoundSpawnPreparedUiFact(int round, int enemyCount)
        {
            Round = round;
            EnemyCount = enemyCount;
        }
    }

    /// <summary>
    /// 战斗结果冻结 UI 事实载荷。
    /// </summary>
    public readonly struct BattleFrozenUiFact
    {
        /// <summary>胜负候选，true 为玩家胜利。</summary>
        public readonly bool IsWinCandidate;

        /// <summary>冻结点逻辑时间戳，单位毫秒。</summary>
        public readonly long FrozenAtMs;

        /// <summary>构造不可变战斗冻结 UI 事实。</summary>
        public BattleFrozenUiFact(bool isWinCandidate, long frozenAtMs)
        {
            IsWinCandidate = isWinCandidate;
            FrozenAtMs = frozenAtMs;
        }
    }
}
