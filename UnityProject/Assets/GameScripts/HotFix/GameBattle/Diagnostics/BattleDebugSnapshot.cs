using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 8.1：BattleDebugSnapshot —— 开发诊断汇总（稳定、版本化序列化）
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 226 行 / Diagnostics/BattleDebugSnapshot.cs）：
    //   提供开发诊断汇总，不作为正式业务 API。
    //
    // 与 BattleTraceSnapshot 的区别：
    //   - BattleTraceSnapshot：黄金轨迹对照用，字段覆盖 spec 要求的全部行为显著状态，
    //     供 task 8.2/8.3 对照工具逐字段精确/容差比较。
    //   - BattleDebugSnapshot：开发诊断用，包含更宽松的诊断计数器与 Manager 内部状态汇总，
    //     帮助开发者在 Editor 中快速定位问题，不参与正式黄金对照。
    //
    // 稳定序列化要求（task 8.1，与 BattleTraceSnapshot 一致）：
    //   1. 版本化：含 SchemaVersion 字段。
    //   2. 固定字段顺序：SerializeToText 按显式声明顺序输出，不依赖反射。
    //   3. 排除对象地址：只记录计数与确定性整数标识。
    //   4. 排除 Dictionary 未定义顺序：集合字段排序后再序列化。
    //   5. 排除真实时间噪声：只记录逻辑时间。
    //
    // 不变量：
    //   1. 不可变：readonly struct，全部字段 readonly。
    //   2. 确定性：相同逻辑状态产生相同序列化输出。
    //   3. 不作为正式业务 API：仅供开发诊断与 Editor 调试视图使用。
    //
    // 本类型为 internal：只供 GameBattle 内部 BattleTraceRecorder 与测试使用。
    // ============================================================================

    /// <summary>
    /// 开发诊断汇总快照（task 8.1）。不作为正式业务 API。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 226 行）：</b>提供开发诊断汇总，供 Editor 调试视图
    /// 与本地问题排查使用。与 <see cref="BattleTraceSnapshot"/> 的区别在于：
    /// 后者用于黄金轨迹对照，字段集严格匹配 spec 要求；本类型包含更宽松的诊断计数器，
    /// 帮助开发者快速观察 Manager 内部状态，不参与正式黄金对照。</para>
    ///
    /// <para><b>稳定序列化（task 8.1）：</b>与 <see cref="BattleTraceSnapshot"/> 一致，
    /// 版本化、固定字段顺序、排除对象地址、排除 Dictionary 未定义顺序、排除真实时间噪声。
    /// 即使是诊断快照，也保持确定性序列化，便于在不同运行间 diff。</para>
    ///
    /// <para><b>不作为正式业务 API（design.md 第 226 行）：</b>本类型不进入跨程序集公共契约，
    /// 不被 Presenter 或 UI 消费，不被黄金对照工具作为权威输入。正式黄金对照使用
    /// <see cref="BattleTraceSnapshot"/>。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部诊断与测试使用。</para>
    /// </remarks>
    internal readonly struct BattleDebugSnapshot
    {
        // ====================================================================
        // Schema 版本
        // ====================================================================

        /// <summary>
        /// 诊断快照 schema 版本。schema 演进时升版本号。
        /// <para>当前版本 1。</para>
        /// </summary>
        public const int SchemaVersion = 1;

        // ====================================================================
        // 逻辑时间字段（排除真实时间噪声）
        // ====================================================================

        /// <summary>外部帧时间戳（毫秒，对应 BattleSimulation.FrameNowMs）。</summary>
        public readonly long FrameNowMs;

        /// <summary>规则位移累计时间（毫秒，对应 BattleSimulation.ElapsedGameTimeMs）。</summary>
        public readonly long ElapsedGameTimeMs;

        // ====================================================================
        // Manager 诊断计数器（标量，排除对象地址与无序集合）
        // ====================================================================

        /// <summary>敌人数量（对应 EnemyManager.Count）。</summary>
        public readonly int EnemyCount;

        /// <summary>敌人空间单元数量（对应 EnemyManager.SpatialCellCount）。</summary>
        public readonly int EnemySpatialCellCount;

        /// <summary>活动投射物数量（对应 ProjectileManager.ActiveCount）。</summary>
        public readonly int ProjectileActiveCount;

        /// <summary>投射物累计 Update 调用次数（对应 ProjectileManager.UpdateCount）。</summary>
        public readonly int ProjectileUpdateCount;

        /// <summary>活动攻击效果数量（对应 AttackEffectManager.ActiveCount）。</summary>
        public readonly int AttackEffectActiveCount;

        /// <summary>攻击效果累计 Update 调用次数（对应 AttackEffectManager.UpdateCount）。</summary>
        public readonly int AttackEffectUpdateCount;

        /// <summary>已注册士兵数量（对应 UnitRegistry.Count）。</summary>
        public readonly int UnitCount;

        /// <summary>玩家方士兵数量（对应 UnitRegistry.PlayerSoldierCount）。</summary>
        public readonly int UnitPlayerCount;

        /// <summary>格子预留数量（对应 PlacementReservationRegistry.Count）。</summary>
        public readonly int ReservationCount;

        /// <summary>已注册池类型数量（对应 BattlePoolScope.PoolCount）。</summary>
        public readonly int PoolTypeCount;

        // ====================================================================
        // 战斗状态标量（排除对象地址）
        // ====================================================================

        /// <summary>当前波次。</summary>
        public readonly int CurrentRound;

        /// <summary>玩家方当前生命。</summary>
        public readonly int PlayerHealth;

        /// <summary>对手方当前生命。</summary>
        public readonly int OpponentHealth;

        /// <summary>玩家方当前金币。</summary>
        public readonly int PlayerGold;

        /// <summary>对手方当前金币。</summary>
        public readonly int OpponentGold;

        /// <summary>总击杀数。</summary>
        public readonly int KillCount;

        // ====================================================================
        // 状态标志
        // ====================================================================

        /// <summary>是否已冻结（Simulation 层）。</summary>
        public readonly bool IsFrozen;

        /// <summary>是否已结算（ResultBuilder 层）。</summary>
        public readonly bool IsResultFrozen;

        /// <summary>是否已进入 Settling。</summary>
        public readonly bool IsSettling;

        // ====================================================================
        // 池统计（已按类型名升序排序，排除 Dictionary 未定义顺序）
        // ====================================================================

        /// <summary>
        /// 池统计轨迹行列表（按类型名升序排序）。
        /// <para>复用 BattleTraceSnapshot.PoolStatTraceRow 结构，避免重复定义。</para>
        /// </summary>
        public readonly IReadOnlyList<BattleTraceSnapshot.PoolStatTraceRow> PoolStats;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造不可变诊断快照。
        /// </summary>
        /// <param name="frameNowMs">外部帧时间戳（毫秒）。</param>
        /// <param name="elapsedGameTimeMs">规则位移累计时间（毫秒）。</param>
        /// <param name="enemyCount">敌人数量。</param>
        /// <param name="enemySpatialCellCount">敌人空间单元数量。</param>
        /// <param name="projectileActiveCount">活动投射物数量。</param>
        /// <param name="projectileUpdateCount">投射物累计 Update 次数。</param>
        /// <param name="attackEffectActiveCount">活动攻击效果数量。</param>
        /// <param name="attackEffectUpdateCount">攻击效果累计 Update 次数。</param>
        /// <param name="unitCount">已注册士兵数量。</param>
        /// <param name="unitPlayerCount">玩家方士兵数量。</param>
        /// <param name="reservationCount">格子预留数量。</param>
        /// <param name="poolTypeCount">池类型数量。</param>
        /// <param name="currentRound">当前波次。</param>
        /// <param name="playerHealth">玩家方当前生命。</param>
        /// <param name="opponentHealth">对手方当前生命。</param>
        /// <param name="playerGold">玩家方当前金币。</param>
        /// <param name="opponentGold">对手方当前金币。</param>
        /// <param name="killCount">总击杀数。</param>
        /// <param name="isFrozen">是否已冻结（Simulation 层）。</param>
        /// <param name="isResultFrozen">是否已结算（ResultBuilder 层）。</param>
        /// <param name="isSettling">是否已进入 Settling。</param>
        /// <param name="poolStats">池统计列表（按类型名升序）。</param>
        internal BattleDebugSnapshot(
            long frameNowMs,
            long elapsedGameTimeMs,
            int enemyCount,
            int enemySpatialCellCount,
            int projectileActiveCount,
            int projectileUpdateCount,
            int attackEffectActiveCount,
            int attackEffectUpdateCount,
            int unitCount,
            int unitPlayerCount,
            int reservationCount,
            int poolTypeCount,
            int currentRound,
            int playerHealth,
            int opponentHealth,
            int playerGold,
            int opponentGold,
            int killCount,
            bool isFrozen,
            bool isResultFrozen,
            bool isSettling,
            IReadOnlyList<BattleTraceSnapshot.PoolStatTraceRow> poolStats)
        {
            FrameNowMs = frameNowMs;
            ElapsedGameTimeMs = elapsedGameTimeMs;
            EnemyCount = enemyCount;
            EnemySpatialCellCount = enemySpatialCellCount;
            ProjectileActiveCount = projectileActiveCount;
            ProjectileUpdateCount = projectileUpdateCount;
            AttackEffectActiveCount = attackEffectActiveCount;
            AttackEffectUpdateCount = attackEffectUpdateCount;
            UnitCount = unitCount;
            UnitPlayerCount = unitPlayerCount;
            ReservationCount = reservationCount;
            PoolTypeCount = poolTypeCount;
            CurrentRound = currentRound;
            PlayerHealth = playerHealth;
            OpponentHealth = opponentHealth;
            PlayerGold = playerGold;
            OpponentGold = opponentGold;
            KillCount = killCount;
            IsFrozen = isFrozen;
            IsResultFrozen = isResultFrozen;
            IsSettling = isSettling;
            PoolStats = poolStats ?? System.Array.Empty<BattleTraceSnapshot.PoolStatTraceRow>();
        }

        // ====================================================================
        // 稳定序列化
        // --------------------------------------------------------------------
        // 按显式声明的字段顺序输出文本，不依赖反射顺序。
        // ====================================================================

        /// <summary>
        /// 按固定字段顺序将诊断快照序列化为文本。
        /// </summary>
        /// <returns>稳定文本表示，相同逻辑状态产生相同输出。</returns>
        /// <remarks>
        /// <para><b>固定字段顺序（task 8.1）：</b>输出顺序为：
        /// schemaVersion → frameNowMs → elapsedGameTimeMs → 各 Manager 计数器 →
        /// 战斗状态标量 → 状态标志 → poolStats[*]。</para>
        /// <para><b>排除对象地址、Dictionary 未定义顺序、真实时间噪声：</b>
        /// 与 <see cref="BattleTraceSnapshot.SerializeToText"/> 规则一致。</para>
        /// </remarks>
        internal string SerializeToText()
        {
            var sb = new System.Text.StringBuilder(256);

            // 1. Schema 版本
            sb.Append("schemaVersion=").Append(SchemaVersion).Append('\n');

            // 2. 逻辑时间（固定顺序）
            sb.Append("frameNowMs=").Append(FrameNowMs).Append('\n');
            sb.Append("elapsedGameTimeMs=").Append(ElapsedGameTimeMs).Append('\n');

            // 3. Manager 诊断计数器（固定顺序）
            sb.Append("enemyCount=").Append(EnemyCount).Append('\n');
            sb.Append("enemySpatialCellCount=").Append(EnemySpatialCellCount).Append('\n');
            sb.Append("projectileActiveCount=").Append(ProjectileActiveCount).Append('\n');
            sb.Append("projectileUpdateCount=").Append(ProjectileUpdateCount).Append('\n');
            sb.Append("attackEffectActiveCount=").Append(AttackEffectActiveCount).Append('\n');
            sb.Append("attackEffectUpdateCount=").Append(AttackEffectUpdateCount).Append('\n');
            sb.Append("unitCount=").Append(UnitCount).Append('\n');
            sb.Append("unitPlayerCount=").Append(UnitPlayerCount).Append('\n');
            sb.Append("reservationCount=").Append(ReservationCount).Append('\n');
            sb.Append("poolTypeCount=").Append(PoolTypeCount).Append('\n');

            // 4. 战斗状态标量（固定顺序）
            sb.Append("currentRound=").Append(CurrentRound).Append('\n');
            sb.Append("playerHealth=").Append(PlayerHealth).Append('\n');
            sb.Append("opponentHealth=").Append(OpponentHealth).Append('\n');
            sb.Append("playerGold=").Append(PlayerGold).Append('\n');
            sb.Append("opponentGold=").Append(OpponentGold).Append('\n');
            sb.Append("killCount=").Append(KillCount).Append('\n');

            // 5. 状态标志（固定顺序）
            sb.Append("isFrozen=").Append(IsFrozen).Append('\n');
            sb.Append("isResultFrozen=").Append(IsResultFrozen).Append('\n');
            sb.Append("isSettling=").Append(IsSettling).Append('\n');

            // 6. 池统计（按类型名升序，逐元素带索引）
            sb.Append("poolStats.count=").Append(PoolStats.Count).Append('\n');
            for (int i = 0; i < PoolStats.Count; i++)
            {
                BattleTraceSnapshot.PoolStatTraceRow r = PoolStats[i];
                sb.Append("poolStats[").Append(i).Append("].typeName=").Append(r.TypeName).Append('\n');
                sb.Append("poolStats[").Append(i).Append("].activeCount=").Append(r.ActiveCount).Append('\n');
                sb.Append("poolStats[").Append(i).Append("].freeCount=").Append(r.FreeCount).Append('\n');
            }

            return sb.ToString();
        }
    }
}
