using System.Collections.Generic;
using GameCommon.Battle;

namespace GameBattle
{
    // ============================================================================
    // 任务 8.1：BattleTraceSnapshot —— 可序列化、可跨 JS/C# 比较的稳定轨迹行
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 225 行 / Diagnostics/BattleTraceSnapshot.cs）：
    //   定义可序列化、可跨 JS/C# 比较的轨迹行。
    //
    // 稳定序列化要求（task 8.1）：
    //   1. 版本化：含 SchemaVersion 字段，schema 演进时升版本号，对照工具据此选择比较策略。
    //   2. 固定字段顺序：序列化输出按显式声明的字段顺序，不依赖反射顺序或运行时布局。
    //   3. 排除对象地址：不记录 GetHashCode、对象指针、RuntimeIdAllocator 之外的实例地址。
    //   4. 排除 Dictionary 未定义顺序：集合字段在写入前排序（按 Id 或显式键），不直接导出
    //      Dictionary/HashSet 的遍历结果。
    //   5. 排除真实时间噪声：只记录逻辑时间（frameNowMs / stepMs / elapsedGameTimeMs），
    //      不记录 DateTime.Now / Time.realtimeSinceStartup / Environment.TickCount64。
    //
    // 设计依据：
    //   - spec battle-parity-verification "Trace captures behaviorally significant state"：
    //     轨迹至少包含逻辑时间、波次、双方生命和金币、手牌、实体状态、位置、目标、投射物、
    //     攻击效果、公共事实、池统计和最终结果。
    //   - spec battle-parity-verification "Differences use explicit comparison rules"：
    //     离散状态精确比较；浮点字段使用字段级声明容差。
    //   - design.md 第 225 行：可序列化、可跨 JS/C# 比较的轨迹行。
    //   - design.md 决策 0.9：逻辑时间源唯一为 elapseSeconds，frameNowMs 同帧不变，stepMs 驱动位移。
    //
    // 不变量：
    //   1. 不可变：readonly struct，全部字段 readonly，构造后不可修改。
    //   2. 固定字段顺序：SerializeToText 按显式声明顺序输出，不依赖反射。
    //   3. 确定性：相同逻辑状态产生相同序列化输出（排除地址、无序集合、真实时间）。
    //   4. 跨语言可比：字段名与 JS 还原工程 golden-battle-bundle.json 的 trace 字段一致。
    //
    // 本类型为 internal：只供 GameBattle 内部 BattleTraceRecorder 与测试使用，
    // 不对其他程序集暴露。跨程序集诊断数据通过 SerializeToText 文本输出传递。
    // ============================================================================

    /// <summary>
    /// 可序列化、可跨 JS/C# 比较的稳定轨迹行（task 8.1）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 225 行）：</b>定义可序列化、可跨 JS/C# 比较的轨迹行，
    /// 供黄金轨迹对照工具（task 8.2）按字段精确比较或浮点容差比较。</para>
    ///
    /// <para><b>稳定序列化（task 8.1 核心要求）：</b></para>
    /// <list type="bullet">
    /// <item><b>版本化</b>：<see cref="SchemaVersion"/> 标记轨迹 schema 版本，
    /// 对照工具据此选择比较策略。schema 演进时升版本号。</item>
    /// <item><b>固定字段顺序</b>：<see cref="SerializeToText"/> 按显式声明顺序输出字段，
    /// 不依赖反射顺序或运行时布局，保证相同状态产生相同文本。</item>
    /// <item><b>排除对象地址</b>：只记录 RuntimeId（由 RuntimeIdAllocator 分配的确定性整数标识），
    /// 不记录 GetHashCode、对象指针或实例地址。</item>
    /// <item><b>排除 Dictionary 未定义顺序</b>：集合字段（实体列表）在构造时已按 Id 排序，
    /// 不直接导出 Dictionary/HashSet 遍历结果。</item>
    /// <item><b>排除真实时间噪声</b>：只记录逻辑时间
    /// （<see cref="FrameNowMs"/> / <see cref="StepMs"/> / <see cref="ElapsedGameTimeMs"/>），
    /// 不记录 <c>DateTime.Now</c> / <c>Time.realtimeSinceStartup</c>。</item>
    /// </list>
    ///
    /// <para><b>字段覆盖（spec "Trace captures behaviorally significant state"）：</b>
    /// 轨迹至少包含逻辑时间、波次、双方生命和金币、手牌、实体状态、位置、目标、投射物、
    /// 攻击效果、公共事实、池统计和最终结果。各字段以子结构组织，序列化时按固定顺序输出。</para>
    ///
    /// <para><b>不可变性：</b>本结构为 readonly struct，全部字段 readonly，构造后不可修改。
    /// 集合字段为 <see cref="System.Collections.Generic.IReadOnlyList{T}"/> 不可变视图，
    /// 构造时由 <see cref="BattleTraceRecorder"/> 从 Manager 快照拷贝并排序。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 BattleTraceRecorder 与测试使用。</para>
    /// </remarks>
    internal readonly struct BattleTraceSnapshot
    {
        // ====================================================================
        // Schema 版本
        // ====================================================================

        /// <summary>
        /// 轨迹 schema 版本。schema 演进（新增/删除/重命名字段）时升版本号，
        /// 对照工具据此选择比较策略。
        /// <para>当前版本 1，覆盖 task 8.1 要求的全部字段。</para>
        /// </summary>
        public const int SchemaVersion = 1;

        // ====================================================================
        // 逻辑时间字段（排除真实时间噪声）
        // ====================================================================

        /// <summary>
        /// 本轨迹行记录时的外部帧时间戳（毫秒）。同一外部帧的所有子步共享同一值
        /// （决策 0.9，对应 BattleSimulation.FrameNowMs）。
        /// <para><b>排除真实时间噪声：</b>只记录逻辑帧时间戳，不记录 DateTime.Now。</para>
        /// </summary>
        public readonly long FrameNowMs;

        /// <summary>
        /// 本轨迹行记录时的当前子步时长（毫秒），驱动位移/弹道/效果累计
        /// （决策 0.9，对应 BattleSimulation.StepMs）。
        /// </summary>
        public readonly long StepMs;

        /// <summary>
        /// 本轨迹行记录时的规则位移累计时间（毫秒），受 500ms 截断约束
        /// （对应 BattleSimulation.ElapsedGameTimeMs）。
        /// </summary>
        public readonly long ElapsedGameTimeMs;

        // ====================================================================
        // 更新阶段标识
        // ====================================================================

        /// <summary>
        /// 本轨迹行记录时的更新阶段（对应 BattleUpdatePhase 枚举整数值）。
        /// <para>供对照工具按阶段分组比较轨迹行。</para>
        /// </summary>
        public readonly int UpdatePhase;

        // ====================================================================
        // 战斗状态字段
        // ====================================================================

        /// <summary>
        /// 战斗状态快照（波次、双方生命/金币/击杀等标量）。
        /// <para>来自 <see cref="BattleReadModel.Snapshot"/> 的不可变副本。</para>
        /// </summary>
        public readonly BattleStateSnapshot State;

        // ====================================================================
        // 实体集合字段（已按 Id 排序，排除 Dictionary 未定义顺序）
        // ====================================================================

        /// <summary>
        /// 敌人实体轨迹行列表（按 RuntimeId 升序排序）。
        /// <para><b>排除 Dictionary 未定义顺序：</b>构造时从 EnemyManager 快照拷贝并按 Id 排序，
        /// 不直接导出 Dictionary/HashSet 遍历结果。</para>
        /// <para><b>排除对象地址：</b>只记录 RuntimeId 与标量状态，不记录对象引用或地址。</para>
        /// </summary>
        public readonly IReadOnlyList<EnemyTraceRow> Enemies;

        /// <summary>
        /// 投射物实体轨迹行列表（按 RuntimeId 升序排序）。
        /// <para>排序与排除规则同 <see cref="Enemies"/>。</para>
        /// </summary>
        public readonly IReadOnlyList<ProjectileTraceRow> Projectiles;

        /// <summary>
        /// 攻击效果轨迹行列表（按 OwnerId 升序，同 OwnerId 按 EffectId 升序排序）。
        /// <para>排序与排除规则同 <see cref="Enemies"/>。</para>
        /// </summary>
        public readonly IReadOnlyList<AttackEffectTraceRow> AttackEffects;

        // ====================================================================
        // 手牌字段
        // ====================================================================

        /// <summary>
        /// 玩家侧手牌轨迹行列表（按 CardId 升序排序）。
        /// <para>来自 <see cref="DeckManager.Snapshot"/> 的不可变副本，按 CardId 排序。</para>
        /// </summary>
        public readonly IReadOnlyList<UnitCardTraceRow> PlayerHand;

        /// <summary>
        /// 对手侧手牌轨迹行列表（按 CardId 升序排序）。
        /// </summary>
        public readonly IReadOnlyList<UnitCardTraceRow> OpponentHand;

        // ====================================================================
        // 池统计字段
        // ====================================================================

        /// <summary>
        /// 池统计轨迹行列表（按类型名升序排序）。
        /// <para><b>排除 Dictionary 未定义顺序：</b>构造时从 BattlePoolScope 快照拷贝并按类型名排序。</para>
        /// </summary>
        public readonly IReadOnlyList<PoolStatTraceRow> PoolStats;

        // ====================================================================
        // 公共事实字段
        // ====================================================================

        /// <summary>
        /// 是否已冻结（首次 TryFreeze 成功后置位，对应 BattleSimulation.IsFrozen）。
        /// <para>公共事实，供对照工具判断轨迹行是否进入 Settling 阶段。</para>
        /// </summary>
        public readonly bool IsFrozen;

        /// <summary>
        /// 是否已结算（结果已冻结，对应 BattleResultBuilder.IsFrozen）。
        /// </summary>
        public readonly bool IsResultFrozen;

        // ====================================================================
        // 最终结果字段（仅在结算后填充）
        // ====================================================================

        /// <summary>
        /// 已冻结的最终结果 DTO。未结算时为 null。
        /// <para>来自 <see cref="BattleResultBuilder.FrozenResult"/> 的不可变副本。</para>
        /// </summary>
        public readonly BattleResultDto? FinalResult;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造不可变轨迹行。
        /// </summary>
        /// <param name="frameNowMs">外部帧时间戳（毫秒）。</param>
        /// <param name="stepMs">当前子步时长（毫秒）。</param>
        /// <param name="elapsedGameTimeMs">规则位移累计时间（毫秒）。</param>
        /// <param name="updatePhase">更新阶段（BattleUpdatePhase 枚举整数值）。</param>
        /// <param name="state">战斗状态快照。</param>
        /// <param name="enemies">敌人轨迹行列表（按 Id 升序）。</param>
        /// <param name="projectiles">投射物轨迹行列表（按 Id 升序）。</param>
        /// <param name="attackEffects">攻击效果轨迹行列表（按 OwnerId/EffectId 升序）。</param>
        /// <param name="playerHand">玩家侧手牌轨迹行列表（按 CardId 升序）。</param>
        /// <param name="opponentHand">对手侧手牌轨迹行列表（按 CardId 升序）。</param>
        /// <param name="poolStats">池统计轨迹行列表（按类型名升序）。</param>
        /// <param name="isFrozen">是否已冻结（Simulation 层）。</param>
        /// <param name="isResultFrozen">是否已结算（ResultBuilder 层）。</param>
        /// <param name="finalResult">已冻结的最终结果 DTO（未结算时传 null）。</param>
        /// <remarks>
        /// <para>调用方（<see cref="BattleTraceRecorder"/>）负责在构造前对集合字段排序，
        /// 保证排除 Dictionary 未定义顺序。本构造不再次排序，保持职责单一。</para>
        /// </remarks>
        internal BattleTraceSnapshot(
            long frameNowMs,
            long stepMs,
            long elapsedGameTimeMs,
            int updatePhase,
            BattleStateSnapshot state,
            IReadOnlyList<EnemyTraceRow> enemies,
            IReadOnlyList<ProjectileTraceRow> projectiles,
            IReadOnlyList<AttackEffectTraceRow> attackEffects,
            IReadOnlyList<UnitCardTraceRow> playerHand,
            IReadOnlyList<UnitCardTraceRow> opponentHand,
            IReadOnlyList<PoolStatTraceRow> poolStats,
            bool isFrozen,
            bool isResultFrozen,
            BattleResultDto? finalResult)
        {
            FrameNowMs = frameNowMs;
            StepMs = stepMs;
            ElapsedGameTimeMs = elapsedGameTimeMs;
            UpdatePhase = updatePhase;
            State = state;
            Enemies = enemies ?? System.Array.Empty<EnemyTraceRow>();
            Projectiles = projectiles ?? System.Array.Empty<ProjectileTraceRow>();
            AttackEffects = attackEffects ?? System.Array.Empty<AttackEffectTraceRow>();
            PlayerHand = playerHand ?? System.Array.Empty<UnitCardTraceRow>();
            OpponentHand = opponentHand ?? System.Array.Empty<UnitCardTraceRow>();
            PoolStats = poolStats ?? System.Array.Empty<PoolStatTraceRow>();
            IsFrozen = isFrozen;
            IsResultFrozen = isResultFrozen;
            FinalResult = finalResult;
        }

        // ====================================================================
        // 稳定序列化
        // --------------------------------------------------------------------
        // 按显式声明的字段顺序输出文本，不依赖反射顺序。
        // 每行一个键值对，集合字段逐元素展开并带索引，保证两端逐字段可比。
        // ====================================================================

        /// <summary>
        /// 按固定字段顺序将轨迹行序列化为文本。
        /// </summary>
        /// <returns>稳定文本表示，相同逻辑状态产生相同输出。</returns>
        /// <remarks>
        /// <para><b>固定字段顺序（task 8.1）：</b>输出顺序为：
        /// schemaVersion → frameNowMs → stepMs → elapsedGameTimeMs → updatePhase →
        /// state.* → enemies[*] → projectiles[*] → attackEffects[*] →
        /// playerHand[*] → opponentHand[*] → poolStats[*] →
        /// isFrozen → isResultFrozen → finalResult.*</para>
        /// <para><b>排除对象地址：</b>只输出 RuntimeId 与标量值。</para>
        /// <para><b>排除 Dictionary 未定义顺序：</b>集合已在构造时排序，此处按索引顺序输出。</para>
        /// <para><b>排除真实时间噪声：</b>只输出逻辑时间字段。</para>
        /// </remarks>
        internal string SerializeToText()
        {
            var sb = new System.Text.StringBuilder(512);

            // 1. Schema 版本
            sb.Append("schemaVersion=").Append(SchemaVersion).Append('\n');

            // 2. 逻辑时间（固定顺序）
            sb.Append("frameNowMs=").Append(FrameNowMs).Append('\n');
            sb.Append("stepMs=").Append(StepMs).Append('\n');
            sb.Append("elapsedGameTimeMs=").Append(ElapsedGameTimeMs).Append('\n');
            sb.Append("updatePhase=").Append(UpdatePhase).Append('\n');

            // 3. 战斗状态（固定字段顺序，与 BattleStateSnapshot 字段声明顺序一致）
            sb.Append("state.currentRound=").Append(State.CurrentRound).Append('\n');
            sb.Append("state.playerHealth=").Append(State.PlayerHealth).Append('\n');
            sb.Append("state.playerMaxHealth=").Append(State.PlayerMaxHealth).Append('\n');
            sb.Append("state.playerGold=").Append(State.PlayerGold).Append('\n');
            sb.Append("state.opponentHealth=").Append(State.OpponentHealth).Append('\n');
            sb.Append("state.opponentMaxHealth=").Append(State.OpponentMaxHealth).Append('\n');
            sb.Append("state.opponentGold=").Append(State.OpponentGold).Append('\n');
            sb.Append("state.killCount=").Append(State.KillCount).Append('\n');
            sb.Append("state.bossKillCount=").Append(State.BossKillCount).Append('\n');
            sb.Append("state.isGameOver=").Append(State.IsGameOver).Append('\n');
            sb.Append("state.contactOccurred=").Append(State.ContactOccurred).Append('\n');
            sb.Append("state.startTimeMs=").Append(State.StartTimeMs).Append('\n');
            sb.Append("state.resultStar=").Append(State.ResultStar).Append('\n');
            sb.Append("state.lastRuntimeId=").Append(State.LastRuntimeId).Append('\n');

            // 4. 敌人集合（按 Id 升序，逐元素带索引）
            SerializeEnemyRows(sb, "enemies", Enemies);

            // 5. 投射物集合（按 Id 升序）
            SerializeProjectileRows(sb, "projectiles", Projectiles);

            // 6. 攻击效果集合（按 OwnerId/EffectId 升序）
            SerializeAttackEffectRows(sb, "attackEffects", AttackEffects);

            // 7. 手牌（按 CardId 升序）
            SerializeUnitCardRows(sb, "playerHand", PlayerHand);
            SerializeUnitCardRows(sb, "opponentHand", OpponentHand);

            // 8. 池统计（按类型名升序）
            SerializePoolStatRows(sb, "poolStats", PoolStats);

            // 9. 公共事实
            sb.Append("isFrozen=").Append(IsFrozen).Append('\n');
            sb.Append("isResultFrozen=").Append(IsResultFrozen).Append('\n');

            // 10. 最终结果（仅在结算后输出）
            if (FinalResult.HasValue)
            {
                BattleResultDto r = FinalResult.Value;
                sb.Append("finalResult.isWin=").Append(r.IsWin).Append('\n');
                sb.Append("finalResult.star=").Append(r.Star).Append('\n');
                sb.Append("finalResult.gold=").Append(r.Gold).Append('\n');
                sb.Append("finalResult.battleDurationMs=").Append(r.BattleDurationMs).Append('\n');
                sb.Append("finalResult.round=").Append(r.Round).Append('\n');
                sb.Append("finalResult.playerTargetHealth=").Append(r.PlayerTargetHealth).Append('\n');
                sb.Append("finalResult.opponentTargetHealth=").Append(r.OpponentTargetHealth).Append('\n');
                sb.Append("finalResult.killCount=").Append(r.KillCount).Append('\n');
                sb.Append("finalResult.bossKillCount=").Append(r.BossKillCount).Append('\n');
                sb.Append("finalResult.endlessRound=").Append(r.EndlessRound).Append('\n');
                sb.Append("finalResult.gameMode=").Append((int)r.GameMode).Append('\n');
                sb.Append("finalResult.resultState=").Append((int)r.ResultState).Append('\n');
            }
            else
            {
                sb.Append("finalResult=null\n");
            }

            return sb.ToString();
        }

        // ====================================================================
        // 集合序列化辅助（按索引顺序输出，排除无序遍历）
        // ====================================================================

        private static void SerializeEnemyRows(
            System.Text.StringBuilder sb, string prefix, IReadOnlyList<EnemyTraceRow> rows)
        {
            sb.Append(prefix).Append(".count=").Append(rows.Count).Append('\n');
            for (int i = 0; i < rows.Count; i++)
            {
                EnemyTraceRow r = rows[i];
                sb.Append(prefix).Append('[').Append(i).Append("].id=").Append(r.Id).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].isPlayerLane=").Append(r.IsPlayerLane).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].state=").Append(r.State).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].x=").Append(r.X).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].y=").Append(r.Y).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].health=").Append(r.Health).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].pathIndex=").Append(r.PathIndex).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].remainingDist=").Append(r.RemainingPathDistance).Append('\n');
            }
        }

        private static void SerializeProjectileRows(
            System.Text.StringBuilder sb, string prefix, IReadOnlyList<ProjectileTraceRow> rows)
        {
            sb.Append(prefix).Append(".count=").Append(rows.Count).Append('\n');
            for (int i = 0; i < rows.Count; i++)
            {
                ProjectileTraceRow r = rows[i];
                sb.Append(prefix).Append('[').Append(i).Append("].id=").Append(r.Id).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].attackerId=").Append(r.AttackerId).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].targetId=").Append(r.TargetId).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].x=").Append(r.X).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].y=").Append(r.Y).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].active=").Append(r.Active).Append('\n');
            }
        }

        private static void SerializeAttackEffectRows(
            System.Text.StringBuilder sb, string prefix, IReadOnlyList<AttackEffectTraceRow> rows)
        {
            sb.Append(prefix).Append(".count=").Append(rows.Count).Append('\n');
            for (int i = 0; i < rows.Count; i++)
            {
                AttackEffectTraceRow r = rows[i];
                sb.Append(prefix).Append('[').Append(i).Append("].effectId=").Append(r.EffectId).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].ownerId=").Append(r.OwnerId).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].kind=").Append(r.Kind).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].active=").Append(r.Active).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].elapsedMs=").Append(r.ElapsedMs).Append('\n');
            }
        }

        private static void SerializeUnitCardRows(
            System.Text.StringBuilder sb, string prefix, IReadOnlyList<UnitCardTraceRow> rows)
        {
            sb.Append(prefix).Append(".count=").Append(rows.Count).Append('\n');
            for (int i = 0; i < rows.Count; i++)
            {
                UnitCardTraceRow r = rows[i];
                sb.Append(prefix).Append('[').Append(i).Append("].cardId=").Append(r.CardId).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].text=").Append(r.SoldierText).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].level=").Append(r.Level).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].cost=").Append(r.Cost).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].isPlayerSide=").Append(r.IsPlayerSide).Append('\n');
            }
        }

        private static void SerializePoolStatRows(
            System.Text.StringBuilder sb, string prefix, IReadOnlyList<PoolStatTraceRow> rows)
        {
            sb.Append(prefix).Append(".count=").Append(rows.Count).Append('\n');
            for (int i = 0; i < rows.Count; i++)
            {
                PoolStatTraceRow r = rows[i];
                sb.Append(prefix).Append('[').Append(i).Append("].typeName=").Append(r.TypeName).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].activeCount=").Append(r.ActiveCount).Append('\n');
                sb.Append(prefix).Append('[').Append(i).Append("].freeCount=").Append(r.FreeCount).Append('\n');
            }
        }

        // ====================================================================
        // 实体轨迹行子结构
        // --------------------------------------------------------------------
        // 每个实体只记录 RuntimeId 与标量状态，不记录对象引用或地址。
        // ====================================================================

        /// <summary>
        /// 敌人实体轨迹行：只记录 RuntimeId 与标量状态，排除对象地址。
        /// </summary>
        internal readonly struct EnemyTraceRow
        {
            /// <summary>敌人运行时 ID（由 RuntimeIdAllocator 分配的确定性整数标识）。</summary>
            public readonly int Id;

            /// <summary>是否玩家方车道。</summary>
            public readonly bool IsPlayerLane;

            /// <summary>当前运行时状态（0=SPAWNING,1=MOVING,2=SKILL,3=STUNNED,4=DEAD）。</summary>
            public readonly int State;

            /// <summary>逻辑位置 X。</summary>
            public readonly float X;

            /// <summary>逻辑位置 Y。</summary>
            public readonly float Y;

            /// <summary>当前血量。</summary>
            public readonly int Health;

            /// <summary>当前路径点索引。</summary>
            public readonly int PathIndex;

            /// <summary>剩余路径距离。</summary>
            public readonly float RemainingPathDistance;

            internal EnemyTraceRow(
                int id, bool isPlayerLane, int state,
                float x, float y, int health, int pathIndex, float remainingPathDistance)
            {
                Id = id;
                IsPlayerLane = isPlayerLane;
                State = state;
                X = x;
                Y = y;
                Health = health;
                PathIndex = pathIndex;
                RemainingPathDistance = remainingPathDistance;
            }
        }

        /// <summary>
        /// 投射物实体轨迹行：只记录 RuntimeId 与标量状态，排除对象地址。
        /// </summary>
        internal readonly struct ProjectileTraceRow
        {
            /// <summary>投射物运行时 ID。</summary>
            public readonly int Id;

            /// <summary>攻击者运行时 ID（无攻击者传 -1）。</summary>
            public readonly int AttackerId;

            /// <summary>目标敌人运行时 ID（无目标传 -1）。</summary>
            public readonly int TargetId;

            /// <summary>逻辑位置 X。</summary>
            public readonly float X;

            /// <summary>逻辑位置 Y。</summary>
            public readonly float Y;

            /// <summary>是否活动。</summary>
            public readonly bool Active;

            internal ProjectileTraceRow(
                int id, int attackerId, int targetId,
                float x, float y, bool active)
            {
                Id = id;
                AttackerId = attackerId;
                TargetId = targetId;
                X = x;
                Y = y;
                Active = active;
            }
        }

        /// <summary>
        /// 攻击效果轨迹行：只记录 EffectId/OwnerId 与标量状态，排除对象地址。
        /// </summary>
        internal readonly struct AttackEffectTraceRow
        {
            /// <summary>效果运行时 ID（由 RuntimeIdAllocator 分配）。</summary>
            public readonly int EffectId;

            /// <summary>所有者（发起单位）运行时 ID。</summary>
            public readonly int OwnerId;

            /// <summary>效果种类（0=Melee,1=Knife,2=Pike,3=CavalrySweep,4=Projectile）。</summary>
            public readonly int Kind;

            /// <summary>是否活动。</summary>
            public readonly bool Active;

            /// <summary>已累计的逻辑时间（毫秒）。</summary>
            public readonly long ElapsedMs;

            internal AttackEffectTraceRow(
                int effectId, int ownerId, int kind, bool active, long elapsedMs)
            {
                EffectId = effectId;
                OwnerId = ownerId;
                Kind = kind;
                Active = active;
                ElapsedMs = elapsedMs;
            }
        }

        /// <summary>
        /// 手牌卡轨迹行：只记录 CardId 与标量状态，排除对象地址。
        /// </summary>
        internal readonly struct UnitCardTraceRow
        {
            /// <summary>卡牌唯一标识。</summary>
            public readonly int CardId;

            /// <summary>兵种文字。</summary>
            public readonly string SoldierText;

            /// <summary>卡牌等级。</summary>
            public readonly int Level;

            /// <summary>卡牌消耗。</summary>
            public readonly int Cost;

            /// <summary>是否玩家侧。</summary>
            public readonly bool IsPlayerSide;

            internal UnitCardTraceRow(int cardId, string soldierText, int level, int cost, bool isPlayerSide)
            {
                CardId = cardId;
                SoldierText = soldierText ?? string.Empty;
                Level = level;
                Cost = cost;
                IsPlayerSide = isPlayerSide;
            }
        }

        /// <summary>
        /// 池统计轨迹行：只记录类型名与计数，排除对象地址。
        /// </summary>
        internal readonly struct PoolStatTraceRow
        {
            /// <summary>池化类型名（用于跨端匹配）。</summary>
            public readonly string TypeName;

            /// <summary>活动租借数。</summary>
            public readonly int ActiveCount;

            /// <summary>空闲数。</summary>
            public readonly int FreeCount;

            internal PoolStatTraceRow(string typeName, int activeCount, int freeCount)
            {
                TypeName = typeName ?? string.Empty;
                ActiveCount = activeCount;
                FreeCount = freeCount;
            }
        }
    }
}
