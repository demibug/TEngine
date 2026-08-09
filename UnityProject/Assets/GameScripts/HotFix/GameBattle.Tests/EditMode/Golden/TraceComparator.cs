using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCommon.Battle;

namespace GameBattle.Tests.EditMode.Golden
{
    // ============================================================================
    // 任务 8.3：TraceComparator —— JS/C# 轨迹精确对照与浮点容差比较
    // ----------------------------------------------------------------------------
    // 职责（specs/battle-parity-verification/spec.md
    //   "Differences use explicit comparison rules"）：
    //   精确比较离散状态、Runtime ID、数量、事件和 phase 顺序，
    //   为每个浮点字段配置显式容差、实际差值和首个偏离位置输出。
    //
    // 消费 task 92 产物：
    //   - BattleTraceSnapshot：稳定、版本化的轨迹行（task 8.1）。
    //   - BattleDebugSnapshot：开发诊断汇总（task 8.1），可选对照。
    //   - BattleTraceSnapshot 的子结构：EnemyTraceRow、ProjectileTraceRow、
    //     AttackEffectTraceRow、UnitCardTraceRow、PoolStatTraceRow。
    //   - BattleStateSnapshot：战斗状态标量快照。
    //   - BattleResultDto：最终结果 DTO。
    //
    // 与 task 8.2 GoldenInputComparator 的协调（不重复功能）：
    //   - GoldenInputComparator：输入侧对照（配置/hash/随机/帧序列/CommandId），
    //     只消费 BattleTraceSnapshot 的逻辑时间字段做语义校验。
    //   - TraceComparator：轨迹侧对照（实体状态/Runtime ID/数量/事件/phase 顺序），
    //     消费 BattleTraceSnapshot 的全部字段做逐字段精确/容差比较。
    //   - 两者互补：输入侧 + 轨迹侧 = 完整行为等价证据。
    //
    // 对照维度（task 8.3 要求）：
    //   1. 离散状态精确比较：BattleStateSnapshot 的 int/bool/long 字段。
    //   2. Runtime ID 精确比较：Enemy.Id、Projectile.Id、Projectile.AttackerId、
    //      Projectile.TargetId、AttackEffect.EffectId、AttackEffect.OwnerId。
    //   3. 数量精确比较：各集合的 Count 字段。
    //   4. 事件和 phase 顺序精确比较：UpdatePhase 枚举值、IsFrozen、IsResultFrozen。
    //   5. 浮点字段显式容差比较：Enemy.X/Y、Enemy.RemainingPathDistance、
    //      Projectile.X/Y，使用 FieldToleranceRules 配置的容差。
    //
    // 比较结果输出：
    //   - 实际差值：浮点字段记录 |expected - actual| 的精确值。
    //   - 首个偏离位置：结构化差异报告中的第一条差异，含路径/期望/实际/容差/差值。
    //
    // 不变量：
    //   1. 离散字段精确比较，不容差。
    //   2. 浮点字段使用 FieldToleranceRules 的显式容差。
    //   3. 比较结果包含实际差值和首个偏离位置。
    //   4. 对照失败时返回结构化报告，不抛异常（供测试断言消费）。
    //   5. 比较两个 BattleTraceSnapshot 实例（expected=JS 基线, actual=C# 运行结果）。
    //
    // 本类为 internal：只供 GameBattle.Tests 内部黄金对照测试使用。
    // ============================================================================

    /// <summary>
    /// JS/C# 轨迹精确对照与浮点容差比较工具（task 8.3）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（spec "Differences use explicit comparison rules"）：</b>
    /// 精确比较离散状态、Runtime ID、数量、事件和 phase 顺序，
    /// 为每个浮点字段配置显式容差、实际差值和首个偏离位置输出。</para>
    ///
    /// <para><b>消费 task 92 产物：</b>消费 <see cref="GameBattle.BattleTraceSnapshot"/>
    /// 及其子结构，对照两个快照实例（expected=JS 基线, actual=C# 运行结果）。</para>
    ///
    /// <para><b>与 task 8.2 协调：</b>GoldenInputComparator 处理输入侧对照，
    /// TraceComparator 处理轨迹侧对照，两者互补不重复。</para>
    ///
    /// <para><b>本类为 internal：</b>只供 GameBattle.Tests 内部使用。</para>
    /// </remarks>
    internal static class TraceComparator
    {
        // ====================================================================
        // 对照报告结构
        // ====================================================================

        /// <summary>
        /// 单条轨迹对照差异记录。
        /// </summary>
        internal sealed class TraceDifference
        {
            /// <summary>对照维度。</summary>
            public ComparisonField Field { get; }

            /// <summary>偏离字段路径。</summary>
            public string FieldPath { get; }

            /// <summary>期望值。</summary>
            public string Expected { get; }

            /// <summary>实际值。</summary>
            public string Actual { get; }

            /// <summary>容差说明（离散字段为"精确相等"，浮点字段为容差值）。</summary>
            public string Tolerance { get; }

            /// <summary>差值（浮点字段为实际差值，离散字段为 N/A）。</summary>
            public string Difference { get; }

            internal TraceDifference(
                ComparisonField field, string fieldPath,
                string expected, string actual,
                string tolerance = "精确相等", string difference = "N/A")
            {
                Field = field;
                FieldPath = fieldPath;
                Expected = expected;
                Actual = actual;
                Tolerance = tolerance;
                Difference = difference;
            }

            public override string ToString()
            {
                return $"[{Field}] {FieldPath}\n  期望: {Expected}\n  实际: {Actual}\n" +
                       $"  容差: {Tolerance}\n  差值: {Difference}";
            }
        }

        /// <summary>
        /// 对照维度枚举（对应 task 8.3 的五类对照能力）。
        /// </summary>
        internal enum ComparisonField
        {
            /// <summary>离散状态（int/bool/long/string 标量）。</summary>
            DiscreteState,

            /// <summary>Runtime ID（实体运行时标识）。</summary>
            RuntimeId,

            /// <summary>数量（集合 Count）。</summary>
            Count,

            /// <summary>事件和 phase 顺序（UpdatePhase/IsFrozen/IsResultFrozen）。</summary>
            EventAndPhaseOrder,

            /// <summary>浮点字段（位置/距离，使用显式容差）。</summary>
            FloatField,
        }

        /// <summary>
        /// 轨迹对照报告：收集所有维度的差异，提供首个偏离位置与全文报告。
        /// </summary>
        internal sealed class TraceComparisonReport
        {
            private readonly List<TraceDifference> _differences = new List<TraceDifference>();

            /// <summary>是否存在差异。</summary>
            public bool HasDifferences => _differences.Count > 0;

            /// <summary>首个偏离位置（无偏离时为 null）。</summary>
            public TraceDifference FirstDifference =>
                _differences.Count > 0 ? _differences[0] : null;

            /// <summary>差异总数。</summary>
            public int Count => _differences.Count;

            /// <summary>添加一条差异。</summary>
            public void Add(TraceDifference difference)
            {
                _differences.Add(difference);
            }

            /// <summary>添加离散 int 偏离（精确比较）。</summary>
            public void AddExactInt(ComparisonField field, string path, int expected, int actual)
            {
                if (expected != actual)
                {
                    Add(new TraceDifference(field, path,
                        expected.ToString(CultureInfo.InvariantCulture),
                        actual.ToString(CultureInfo.InvariantCulture)));
                }
            }

            /// <summary>添加离散 long 偏离（精确比较）。</summary>
            public void AddExactLong(ComparisonField field, string path, long expected, long actual)
            {
                if (expected != actual)
                {
                    Add(new TraceDifference(field, path,
                        expected.ToString(CultureInfo.InvariantCulture),
                        actual.ToString(CultureInfo.InvariantCulture)));
                }
            }

            /// <summary>添加离散 bool 偏离（精确比较）。</summary>
            public void AddExactBool(ComparisonField field, string path, bool expected, bool actual)
            {
                if (expected != actual)
                {
                    Add(new TraceDifference(field, path,
                        expected.ToString(),
                        actual.ToString()));
                }
            }

            /// <summary>添加离散 string 偏离（精确比较）。</summary>
            public void AddExactString(ComparisonField field, string path, string expected, string actual)
            {
                if (expected != actual)
                {
                    Add(new TraceDifference(field, path,
                        expected ?? "<null>",
                        actual ?? "<null>"));
                }
            }

            /// <summary>
            /// 添加浮点字段偏离（超出 FieldToleranceRules 配置的显式容差时记录）。
            /// 记录实际差值和显式容差。
            /// </summary>
            public void AddFloat(string path, float expected, float actual)
            {
                float tolerance = FieldToleranceRules.GetTolerance(path);
                float diff = Math.Abs(expected - actual);
                if (diff > tolerance)
                {
                    Add(new TraceDifference(ComparisonField.FloatField, path,
                        expected.ToString("R", CultureInfo.InvariantCulture),
                        actual.ToString("R", CultureInfo.InvariantCulture),
                        FieldToleranceRules.FormatTolerance(tolerance),
                        diff.ToString("R", CultureInfo.InvariantCulture)));
                }
            }

            /// <summary>生成全文差异报告。</summary>
            public string Report()
            {
                if (_differences.Count == 0)
                {
                    return "无偏离（所有维度字段在容差内相等）";
                }

                var sb = new StringBuilder();
                sb.Append("检测到 ").Append(_differences.Count).AppendLine(" 处偏离：");
                for (int i = 0; i < _differences.Count; i++)
                {
                    sb.Append("[").Append(i + 1).Append("] ").AppendLine(_differences[i].ToString());
                }
                return sb.ToString();
            }
        }

        // ====================================================================
        // 全维度对照入口
        // --------------------------------------------------------------------
        // 比较两个 BattleTraceSnapshot 实例（expected=JS 基线, actual=C# 运行结果），
        // 覆盖离散状态、Runtime ID、数量、事件和 phase 顺序、浮点字段五个维度。
        // ====================================================================

        /// <summary>
        /// 执行两个 BattleTraceSnapshot 的全维度对照，返回完整对照报告。
        /// </summary>
        /// <param name="expected">期望快照（JS 黄金基线）。</param>
        /// <param name="actual">实际快照（C# 运行结果）。</param>
        /// <returns>对照报告。HasDifferences 为 false 表示全部维度通过。</returns>
        /// <remarks>
        /// <para><b>全维度对照（task 8.3）：</b>依次执行以下五个维度的对照：</para>
        /// <list type="number">
        /// <item><b>离散状态</b>：BattleStateSnapshot 的 int/bool/long 字段精确比较。</item>
        /// <item><b>Runtime ID</b>：实体运行时标识精确比较。</item>
        /// <item><b>数量</b>：各集合 Count 精确比较。</item>
        /// <item><b>事件和 phase 顺序</b>：UpdatePhase/IsFrozen/IsResultFrozen 精确比较。</item>
        /// <item><b>浮点字段</b>：位置/距离使用 FieldToleranceRules 显式容差比较。</item>
        /// </list>
        /// <para><b>比较结果：</b>包含实际差值和首个偏离位置。</para>
        /// </remarks>
        internal static TraceComparisonReport Compare(
            GameBattle.BattleTraceSnapshot expected,
            GameBattle.BattleTraceSnapshot actual)
        {
            var report = new TraceComparisonReport();

            // 维度 1：事件和 phase 顺序（逻辑时间与阶段标识，精确比较）
            CompareEventAndPhaseOrder(report, expected, actual);

            // 维度 2：离散状态（BattleStateSnapshot 标量，精确比较）
            CompareDiscreteState(report, expected.State, actual.State, "state");

            // 维度 3：数量（各集合 Count，精确比较）
            CompareCounts(report, expected, actual);

            // 维度 4：Runtime ID 与实体字段（精确比较离散值、容差比较浮点值）
            CompareEnemies(report, expected.Enemies, actual.Enemies);
            CompareProjectiles(report, expected.Projectiles, actual.Projectiles);
            CompareAttackEffects(report, expected.AttackEffects, actual.AttackEffects);
            CompareUnitCards(report, expected.PlayerHand, actual.PlayerHand, "playerHand");
            CompareUnitCards(report, expected.OpponentHand, actual.OpponentHand, "opponentHand");
            ComparePoolStats(report, expected.PoolStats, actual.PoolStats);

            // 维度 5：最终结果（BattleResultDto，精确比较）
            CompareFinalResult(report, expected.FinalResult, actual.FinalResult);

            return report;
        }

        // ====================================================================
        // 维度 1：事件和 phase 顺序（精确比较）
        // --------------------------------------------------------------------
        // UpdatePhase 枚举值、IsFrozen、IsResultFrozen 精确比较。
        // 这些字段反映战斗的阶段顺序和事件时序，必须精确相等。
        // ====================================================================

        /// <summary>
        /// 对照事件和 phase 顺序（UpdatePhase/IsFrozen/IsResultFrozen，精确比较）。
        /// </summary>
        private static void CompareEventAndPhaseOrder(
            TraceComparisonReport report,
            GameBattle.BattleTraceSnapshot expected,
            GameBattle.BattleTraceSnapshot actual)
        {
            // 逻辑时间字段（long，精确比较）
            report.AddExactLong(ComparisonField.EventAndPhaseOrder,
                "frameNowMs", expected.FrameNowMs, actual.FrameNowMs);
            report.AddExactLong(ComparisonField.EventAndPhaseOrder,
                "stepMs", expected.StepMs, actual.StepMs);
            report.AddExactLong(ComparisonField.EventAndPhaseOrder,
                "elapsedGameTimeMs", expected.ElapsedGameTimeMs, actual.ElapsedGameTimeMs);

            // 更新阶段（int，对应 BattleUpdatePhase 枚举整数值，精确比较）
            report.AddExactInt(ComparisonField.EventAndPhaseOrder,
                "updatePhase", expected.UpdatePhase, actual.UpdatePhase);

            // 公共事实（bool，精确比较）
            report.AddExactBool(ComparisonField.EventAndPhaseOrder,
                "isFrozen", expected.IsFrozen, actual.IsFrozen);
            report.AddExactBool(ComparisonField.EventAndPhaseOrder,
                "isResultFrozen", expected.IsResultFrozen, actual.IsResultFrozen);
        }

        // ====================================================================
        // 维度 2：离散状态（BattleStateSnapshot 标量，精确比较）
        // ====================================================================

        /// <summary>
        /// 对照 BattleStateSnapshot 的离散标量字段（int/bool/long，精确比较）。
        /// </summary>
        private static void CompareDiscreteState(
            TraceComparisonReport report,
            GameBattle.BattleStateSnapshot expected,
            GameBattle.BattleStateSnapshot actual,
            string prefix)
        {
            // 离散 int 精确比较
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".currentRound", expected.CurrentRound, actual.CurrentRound);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".playerHealth", expected.PlayerHealth, actual.PlayerHealth);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".playerMaxHealth", expected.PlayerMaxHealth, actual.PlayerMaxHealth);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".playerGold", expected.PlayerGold, actual.PlayerGold);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".opponentHealth", expected.OpponentHealth, actual.OpponentHealth);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".opponentMaxHealth", expected.OpponentMaxHealth, actual.OpponentMaxHealth);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".opponentGold", expected.OpponentGold, actual.OpponentGold);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".killCount", expected.KillCount, actual.KillCount);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".bossKillCount", expected.BossKillCount, actual.BossKillCount);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".resultStar", expected.ResultStar, actual.ResultStar);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".lastRuntimeId", expected.LastRuntimeId, actual.LastRuntimeId);

            // 离散 bool 精确比较
            report.AddExactBool(ComparisonField.DiscreteState,
                prefix + ".isGameOver", expected.IsGameOver, actual.IsGameOver);
            report.AddExactBool(ComparisonField.DiscreteState,
                prefix + ".contactOccurred", expected.ContactOccurred, actual.ContactOccurred);

            // 离散 long 精确比较
            report.AddExactLong(ComparisonField.DiscreteState,
                prefix + ".startTimeMs", expected.StartTimeMs, actual.StartTimeMs);
        }

        // ====================================================================
        // 维度 3：数量（各集合 Count，精确比较）
        // ====================================================================

        /// <summary>
        /// 对照各集合的 Count（精确比较）。
        /// </summary>
        private static void CompareCounts(
            TraceComparisonReport report,
            GameBattle.BattleTraceSnapshot expected,
            GameBattle.BattleTraceSnapshot actual)
        {
            report.AddExactInt(ComparisonField.Count,
                "enemies.count", expected.Enemies.Count, actual.Enemies.Count);
            report.AddExactInt(ComparisonField.Count,
                "projectiles.count", expected.Projectiles.Count, actual.Projectiles.Count);
            report.AddExactInt(ComparisonField.Count,
                "attackEffects.count", expected.AttackEffects.Count, actual.AttackEffects.Count);
            report.AddExactInt(ComparisonField.Count,
                "playerHand.count", expected.PlayerHand.Count, actual.PlayerHand.Count);
            report.AddExactInt(ComparisonField.Count,
                "opponentHand.count", expected.OpponentHand.Count, actual.OpponentHand.Count);
            report.AddExactInt(ComparisonField.Count,
                "poolStats.count", expected.PoolStats.Count, actual.PoolStats.Count);
        }

        // ====================================================================
        // 维度 4：Runtime ID 与实体字段
        // --------------------------------------------------------------------
        // 离散字段（Id/State/Health/PathIndex/Active 等）精确比较。
        // 浮点字段（X/Y/RemainingPathDistance）使用 FieldToleranceRules 容差比较。
        // ====================================================================

        /// <summary>
        /// 对照敌人实体集合（Runtime ID 精确比较、位置/距离浮点容差比较）。
        /// </summary>
        private static void CompareEnemies(
            TraceComparisonReport report,
            IReadOnlyList<GameBattle.BattleTraceSnapshot.EnemyTraceRow> expected,
            IReadOnlyList<GameBattle.BattleTraceSnapshot.EnemyTraceRow> actual)
        {
            int count = Math.Min(expected.Count, actual.Count);
            for (int i = 0; i < count; i++)
            {
                var exp = expected[i];
                var act = actual[i];
                string prefix = "enemies[" + i + "]";

                // Runtime ID 精确比较
                report.AddExactInt(ComparisonField.RuntimeId,
                    prefix + ".id", exp.Id, act.Id);

                // 离散字段精确比较
                report.AddExactBool(ComparisonField.DiscreteState,
                    prefix + ".isPlayerLane", exp.IsPlayerLane, act.IsPlayerLane);
                report.AddExactInt(ComparisonField.DiscreteState,
                    prefix + ".state", exp.State, act.State);
                report.AddExactInt(ComparisonField.DiscreteState,
                    prefix + ".health", exp.Health, act.Health);
                report.AddExactInt(ComparisonField.DiscreteState,
                    prefix + ".pathIndex", exp.PathIndex, act.PathIndex);

                // 浮点字段显式容差比较（位置和剩余距离）
                report.AddFloat(prefix + ".x", exp.X, act.X);
                report.AddFloat(prefix + ".y", exp.Y, act.Y);
                report.AddFloat(prefix + ".remainingDist", exp.RemainingPathDistance, act.RemainingPathDistance);
            }
        }

        /// <summary>
        /// 对照投射物实体集合（Runtime ID 精确比较、位置浮点容差比较）。
        /// </summary>
        private static void CompareProjectiles(
            TraceComparisonReport report,
            IReadOnlyList<GameBattle.BattleTraceSnapshot.ProjectileTraceRow> expected,
            IReadOnlyList<GameBattle.BattleTraceSnapshot.ProjectileTraceRow> actual)
        {
            int count = Math.Min(expected.Count, actual.Count);
            for (int i = 0; i < count; i++)
            {
                var exp = expected[i];
                var act = actual[i];
                string prefix = "projectiles[" + i + "]";

                // Runtime ID 精确比较
                report.AddExactInt(ComparisonField.RuntimeId,
                    prefix + ".id", exp.Id, act.Id);
                report.AddExactInt(ComparisonField.RuntimeId,
                    prefix + ".attackerId", exp.AttackerId, act.AttackerId);
                report.AddExactInt(ComparisonField.RuntimeId,
                    prefix + ".targetId", exp.TargetId, act.TargetId);

                // 离散字段精确比较
                report.AddExactBool(ComparisonField.DiscreteState,
                    prefix + ".active", exp.Active, act.Active);

                // 浮点字段显式容差比较（位置）
                report.AddFloat(prefix + ".x", exp.X, act.X);
                report.AddFloat(prefix + ".y", exp.Y, act.Y);
            }
        }

        /// <summary>
        /// 对照攻击效果实体集合（Runtime ID 精确比较）。
        /// </summary>
        private static void CompareAttackEffects(
            TraceComparisonReport report,
            IReadOnlyList<GameBattle.BattleTraceSnapshot.AttackEffectTraceRow> expected,
            IReadOnlyList<GameBattle.BattleTraceSnapshot.AttackEffectTraceRow> actual)
        {
            int count = Math.Min(expected.Count, actual.Count);
            for (int i = 0; i < count; i++)
            {
                var exp = expected[i];
                var act = actual[i];
                string prefix = "attackEffects[" + i + "]";

                // Runtime ID 精确比较
                report.AddExactInt(ComparisonField.RuntimeId,
                    prefix + ".effectId", exp.EffectId, act.EffectId);
                report.AddExactInt(ComparisonField.RuntimeId,
                    prefix + ".ownerId", exp.OwnerId, act.OwnerId);

                // 离散字段精确比较
                report.AddExactInt(ComparisonField.DiscreteState,
                    prefix + ".kind", exp.Kind, act.Kind);
                report.AddExactBool(ComparisonField.DiscreteState,
                    prefix + ".active", exp.Active, act.Active);

                // ElapsedMs 为 long 类型（非浮点），精确比较
                // 注：当前占位为 0（已批准偏差 D-001），精确比较不受影响
                report.AddExactLong(ComparisonField.DiscreteState,
                    prefix + ".elapsedMs", exp.ElapsedMs, act.ElapsedMs);
            }
        }

        /// <summary>
        /// 对照手牌卡集合（CardId 精确比较）。
        /// </summary>
        private static void CompareUnitCards(
            TraceComparisonReport report,
            IReadOnlyList<GameBattle.BattleTraceSnapshot.UnitCardTraceRow> expected,
            IReadOnlyList<GameBattle.BattleTraceSnapshot.UnitCardTraceRow> actual,
            string prefix)
        {
            int count = Math.Min(expected.Count, actual.Count);
            for (int i = 0; i < count; i++)
            {
                var exp = expected[i];
                var act = actual[i];
                string p = prefix + "[" + i + "]";

                // 离散字段精确比较
                report.AddExactInt(ComparisonField.DiscreteState,
                    p + ".cardId", exp.CardId, act.CardId);
                report.AddExactString(ComparisonField.DiscreteState,
                    p + ".text", exp.SoldierText, act.SoldierText);
                report.AddExactInt(ComparisonField.DiscreteState,
                    p + ".level", exp.Level, act.Level);
                report.AddExactInt(ComparisonField.DiscreteState,
                    p + ".cost", exp.Cost, act.Cost);
                report.AddExactBool(ComparisonField.DiscreteState,
                    p + ".isPlayerSide", exp.IsPlayerSide, act.IsPlayerSide);
            }
        }

        /// <summary>
        /// 对照池统计集合（离散字段精确比较）。
        /// </summary>
        private static void ComparePoolStats(
            TraceComparisonReport report,
            IReadOnlyList<GameBattle.BattleTraceSnapshot.PoolStatTraceRow> expected,
            IReadOnlyList<GameBattle.BattleTraceSnapshot.PoolStatTraceRow> actual)
        {
            int count = Math.Min(expected.Count, actual.Count);
            for (int i = 0; i < count; i++)
            {
                var exp = expected[i];
                var act = actual[i];
                string prefix = "poolStats[" + i + "]";

                // 离散字段精确比较
                report.AddExactString(ComparisonField.DiscreteState,
                    prefix + ".typeName", exp.TypeName, act.TypeName);
                report.AddExactInt(ComparisonField.DiscreteState,
                    prefix + ".activeCount", exp.ActiveCount, act.ActiveCount);
                report.AddExactInt(ComparisonField.DiscreteState,
                    prefix + ".freeCount", exp.FreeCount, act.FreeCount);
            }
        }

        // ====================================================================
        // 维度 5：最终结果（BattleResultDto，精确比较）
        // ====================================================================

        /// <summary>
        /// 对照最终结果 DTO（BattleResultDto，精确比较）。
        /// </summary>
        private static void CompareFinalResult(
            TraceComparisonReport report,
            BattleResultDto? expected,
            BattleResultDto? actual)
        {
            // null 状态精确比较
            if (!expected.HasValue && !actual.HasValue)
            {
                // 两者均为 null，无偏离
                return;
            }

            if (expected.HasValue != actual.HasValue)
            {
                report.Add(new TraceDifference(ComparisonField.DiscreteState,
                    "finalResult",
                    expected.HasValue ? "已冻结" : "null",
                    actual.HasValue ? "已冻结" : "null"));
                return;
            }

            // 两者均有值，逐字段精确比较
            BattleResultDto exp = expected.Value;
            BattleResultDto act = actual.Value;
            string prefix = "finalResult";

            // 离散 bool 精确比较
            report.AddExactBool(ComparisonField.DiscreteState,
                prefix + ".isWin", exp.IsWin, act.IsWin);

            // 离散 int 精确比较
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".star", exp.Star, act.Star);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".gold", exp.Gold, act.Gold);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".round", exp.Round, act.Round);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".playerTargetHealth", exp.PlayerTargetHealth, act.PlayerTargetHealth);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".opponentTargetHealth", exp.OpponentTargetHealth, act.OpponentTargetHealth);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".killCount", exp.KillCount, act.KillCount);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".bossKillCount", exp.BossKillCount, act.BossKillCount);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".endlessRound", exp.EndlessRound, act.EndlessRound);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".gameMode", (int)exp.GameMode, (int)act.GameMode);
            report.AddExactInt(ComparisonField.DiscreteState,
                prefix + ".resultState", (int)exp.ResultState, (int)act.ResultState);

            // 离散 long 精确比较
            report.AddExactLong(ComparisonField.DiscreteState,
                prefix + ".battleDurationMs", exp.BattleDurationMs, act.BattleDurationMs);
        }

        // ====================================================================
        // 辅助：比较两个轨迹序列的 phase 顺序
        // --------------------------------------------------------------------
        // 验证两个 BattleTraceSnapshot 序列的 UpdatePhase 顺序完全一致。
        // 用于验证 JS/C# 在同一输入下的阶段执行顺序等价。
        // ====================================================================

        /// <summary>
        /// 对照两个轨迹序列的 UpdatePhase 顺序（精确比较）。
        /// </summary>
        /// <param name="expectedSequence">期望轨迹序列（JS 黄金基线）。</param>
        /// <param name="actualSequence">实际轨迹序列（C# 运行结果）。</param>
        /// <returns>对照报告。HasDifferences 为 false 表示 phase 顺序一致。</returns>
        /// <remarks>
        /// <para><b>phase 顺序对照（task 8.3 "事件和 phase 顺序"）：</b>
        /// 验证两个轨迹序列的 UpdatePhase 值逐行精确相等。
        /// 序列长度不等视为偏离；逐行 phase 值不等视为偏离。</para>
        /// <para>该对照不比较实体字段，只验证阶段执行顺序。
        /// 实体字段对照使用 <see cref="Compare"/>。</para>
        /// </remarks>
        internal static TraceComparisonReport ComparePhaseOrder(
            IReadOnlyList<GameBattle.BattleTraceSnapshot> expectedSequence,
            IReadOnlyList<GameBattle.BattleTraceSnapshot> actualSequence)
        {
            var report = new TraceComparisonReport();

            // 序列长度精确比较
            report.AddExactInt(ComparisonField.Count,
                "traceSequence.count",
                expectedSequence.Count, actualSequence.Count);

            // 逐行 phase 顺序精确比较
            int count = Math.Min(expectedSequence.Count, actualSequence.Count);
            for (int i = 0; i < count; i++)
            {
                report.AddExactInt(ComparisonField.EventAndPhaseOrder,
                    "traceSequence[" + i + "].updatePhase",
                    expectedSequence[i].UpdatePhase,
                    actualSequence[i].UpdatePhase);

                // 逻辑时间也应顺序一致
                report.AddExactLong(ComparisonField.EventAndPhaseOrder,
                    "traceSequence[" + i + "].frameNowMs",
                    expectedSequence[i].FrameNowMs,
                    actualSequence[i].FrameNowMs);
                report.AddExactLong(ComparisonField.EventAndPhaseOrder,
                    "traceSequence[" + i + "].stepMs",
                    expectedSequence[i].StepMs,
                    actualSequence[i].StepMs);
                report.AddExactLong(ComparisonField.EventAndPhaseOrder,
                    "traceSequence[" + i + "].elapsedGameTimeMs",
                    expectedSequence[i].ElapsedGameTimeMs,
                    actualSequence[i].ElapsedGameTimeMs);
            }

            return report;
        }
    }
}
