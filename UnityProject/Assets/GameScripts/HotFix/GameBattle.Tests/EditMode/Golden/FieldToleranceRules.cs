using System.Collections.Generic;
using System.Globalization;

namespace GameBattle.Tests.EditMode.Golden
{
    // ============================================================================
    // 任务 8.3：FieldToleranceRules —— 浮点字段级显式容差配置
    // ----------------------------------------------------------------------------
    // 职责（specs/battle-parity-verification/spec.md "Differences use explicit
    //   comparison rules"）：
    //   为 BattleTraceSnapshot / BattleDebugSnapshot 中的每个浮点字段配置显式容差，
    //   供 TraceComparator 在 JS/C# 轨迹对照时使用。
    //
    // 设计依据：
    //   - spec "Differences use explicit comparison rules"：
    //     离散状态、ID、数量和事件顺序 MUST 精确比较；浮点字段 SHALL 使用字段级声明容差。
    //   - design.md 决策 7："离散字段与事件顺序精确比较；位置/曲线等浮点值只按字段级容差比较。"
    //   - design.md 已批准偏差表：攻击效果 EffectId/Kind/ElapsedMs 占位为 0、poolStats 逐池
    //     统计恒空，这些字段在轨迹中已存在但值固定，相同状态产生相同输出，容差比较不影响结果。
    //
    // 容差来源：
    //   - 位置字段（Enemy.X/Y、Projectile.X/Y）：移动由 stepMs 驱动，每帧位移为 Speed*stepMs/1000，
    //     JS 与 C# 的 float 运算可能存在最低位舍入差异。1e-4f（0.0001 格）吸收单精度累积差异，
    //     不掩盖行为级偏差（一格=1.0，0.0001 格远小于半像素级视觉精度）。
    //   - 剩余路径距离（Enemy.RemainingPathDistance）：由路径长度减去已走距离计算，float 累积。
    //     1e-4f 吸收累积差异。
    //   - 攻击效果 ElapsedMs：当前占位为 0（已批准偏差 D-001），精确相等。
    //     后续暴露真实值后仍使用精确比较（long 类型），无需浮点容差。
    //
    // 与 task 8.2 GoldenInputComparator 的协调：
    //   - GoldenInputComparator 处理输入侧对照（配置/hash/随机/帧序列/CommandId），
    //     使用自己的 FloatFieldTolerance（1e-6f）比较随机序列 float 值。
    //   - FieldToleranceRules 专门处理轨迹侧（BattleTraceSnapshot）的浮点字段，
    //     容差值可能不同（位置 1e-4f vs 随机序列 1e-6f），因为字段语义不同。
    //   - 两者不重复功能：GoldenInputComparator 不消费 BattleTraceSnapshot 的实体字段，
    //     TraceComparator 不消费 golden-battle-bundle.json 的输入字段。
    //
    // 不变量：
    //   1. 每个浮点字段都有显式容差，不使用全局默认 epsilon。
    //   2. 容差配置不可变（readonly），构造后不可修改。
    //   3. 未注册的浮点字段路径视为容差 0（精确相等），不静默跳过。
    //
    // 本类为 internal：只供 GameBattle.Tests 内部 TraceComparator 与黄金对照测试使用。
    // ============================================================================

    /// <summary>
    /// 浮点字段级显式容差配置（task 8.3）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（spec "Differences use explicit comparison rules"）：</b>
    /// 为 <see cref="GameBattle.BattleTraceSnapshot"/> 中的每个浮点字段配置显式容差，
    /// 供 <see cref="TraceComparator"/> 在 JS/C# 轨迹对照时使用。</para>
    ///
    /// <para><b>字段级容差来源：</b></para>
    /// <list type="bullet">
    /// <item><b>位置字段</b>（Enemy.X/Y、Projectile.X/Y）：移动由 stepMs 驱动，
    /// JS 与 C# 的 float 运算可能存在最低位舍入差异。1e-4f（0.0001 格）吸收单精度累积差异。</item>
    /// <item><b>剩余路径距离</b>（Enemy.RemainingPathDistance）：float 累积计算，1e-4f 吸收差异。</item>
    /// <item><b>攻击效果 ElapsedMs</b>：当前占位为 0（已批准偏差 D-001），long 类型精确比较。</item>
    /// </list>
    ///
    /// <para><b>与 task 8.2 GoldenInputComparator 的协调：</b>
    /// GoldenInputComparator 处理输入侧对照，FieldToleranceRules 专门处理轨迹侧浮点字段，
    /// 两者不重复功能。</para>
    ///
    /// <para><b>本类为 internal：</b>只供 GameBattle.Tests 内部使用。</para>
    /// </remarks>
    internal static class FieldToleranceRules
    {
        // ====================================================================
        // 默认容差常量
        // ====================================================================

        /// <summary>
        /// 位置字段显式容差（0.0001 格）。
        /// <para>移动由 stepMs 驱动，JS 与 C# 的 float 运算可能存在最低位舍入差异。
        /// 1e-4f 吸收单精度累积差异，不掩盖行为级偏差（一格=1.0）。</para>
        /// <para>本容差为字段级显式声明，不使用 NUnit 默认 epsilon 或全局容差。</para>
        /// </summary>
        internal const float PositionTolerance = 1e-4f;

        /// <summary>
        /// 剩余路径距离字段显式容差（0.0001 格）。
        /// <para>由路径长度减去已走距离计算，float 累积。1e-4f 吸收累积差异。</para>
        /// </summary>
        internal const float RemainingDistanceTolerance = 1e-4f;

        // ====================================================================
        // 字段路径 → 容差映射
        // --------------------------------------------------------------------
        // 路径格式与 BattleTraceSnapshot.SerializeToText 的字段路径一致：
        //   enemies[i].x / enemies[i].y
        //   enemies[i].remainingDist
        //   projectiles[i].x / projectiles[i].y
        // 未注册路径视为容差 0（精确相等），不静默跳过。
        // ====================================================================

        /// <summary>
        /// 浮点字段路径 → 显式容差映射。
        /// <para>路径使用 SerializeToText 的字段路径格式。
        /// 含索引的字段使用通配符 '*' 匹配任意索引（如 enemies[*].x）。</para>
        /// <para>未注册的浮点字段路径视为容差 0（精确相等）。</para>
        /// </summary>
        private static readonly Dictionary<string, float> s_fieldTolerances =
            new Dictionary<string, float>
            {
                // 敌人位置字段（float，移动累积差异）
                { "enemies[*].x", PositionTolerance },
                { "enemies[*].y", PositionTolerance },

                // 敌人剩余路径距离（float，累积计算差异）
                { "enemies[*].remainingDist", RemainingDistanceTolerance },

                // 投射物位置字段（float，贝塞尔曲线累积差异）
                { "projectiles[*].x", PositionTolerance },
                { "projectiles[*].y", PositionTolerance },

                // 攻击效果 ElapsedMs 为 long 类型（非浮点），不在此注册；
                // TraceComparator 使用精确比较处理 long 字段。
            };

        /// <summary>
        /// 查询指定字段路径的显式容差。
        /// </summary>
        /// <param name="fieldPath">字段路径（如 <c>enemies[3].x</c>）。</param>
        /// <returns>该字段的显式容差；未注册则返回 0（精确相等）。</returns>
        /// <remarks>
        /// <para>支持通配符匹配：注册的 <c>enemies[*].x</c> 匹配 <c>enemies[3].x</c>、
        /// <c>enemies[0].x</c> 等任意索引路径。</para>
        /// <para>未注册的浮点字段路径返回 0，表示精确相等（不容差），
        /// 不静默跳过任何浮点字段。</para>
        /// </remarks>
        internal static float GetTolerance(string fieldPath)
        {
            // 1. 精确匹配
            if (s_fieldTolerances.TryGetValue(fieldPath, out float tolerance))
            {
                return tolerance;
            }

            // 2. 通配符匹配：将注册的 enemies[*].x 转为正则匹配 enemies[3].x
            foreach (var entry in s_fieldTolerances)
            {
                if (MatchesWildcard(entry.Key, fieldPath))
                {
                    return entry.Value;
                }
            }

            // 3. 未注册的浮点字段：返回 0（精确相等），不静默跳过
            return 0f;
        }

        /// <summary>
        /// 判断字段路径是否匹配通配符模式。
        /// </summary>
        /// <param name="pattern">通配符模式（含 * 的注册路径，如 <c>enemies[*].x</c>）。</param>
        /// <param name="path">实际字段路径（如 <c>enemies[3].x</c>）。</param>
        /// <returns>匹配返回 true。</returns>
        /// <remarks>
        /// 将 <c>*</c> 转为正则 <c>\d+</c>，匹配索引数字。
        /// </remarks>
        private static bool MatchesWildcard(string pattern, string path)
        {
            // 简单通配符匹配：将 [*] 替换为 [数字]
            // pattern: enemies[*].x → 期望 path: enemies[数字].x
            int starIndex = pattern.IndexOf('*');
            if (starIndex < 0)
            {
                return false;
            }

            // 分割 pattern 为前缀和后缀
            string prefix = pattern.Substring(0, starIndex);
            string suffix = pattern.Substring(starIndex + 1);

            // 检查 path 是否以 prefix 开头、以 suffix 结尾
            if (!path.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                return false;
            }
            if (!path.EndsWith(suffix, System.StringComparison.Ordinal))
            {
                return false;
            }

            // 中间部分应为数字（索引）
            string middle = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
            if (middle.Length == 0)
            {
                return false;
            }

            foreach (char c in middle)
            {
                if (!char.IsDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 判断字段是否为浮点类型（需要容差比较）。
        /// </summary>
        /// <param name="fieldPath">字段路径。</param>
        /// <returns>是浮点字段返回 true。</returns>
        /// <remarks>
        /// <para>根据字段路径后缀判断是否为浮点字段：</para>
        /// <list type="bullet">
        /// <item><c>.x</c> / <c>.y</c>：位置字段（float）。</item>
        /// <item><c>.remainingDist</c>：剩余路径距离（float）。</item>
        /// <item>其他：非浮点字段（int/bool/long/string），精确比较。</item>
        /// </list>
        /// </remarks>
        internal static bool IsFloatField(string fieldPath)
        {
            return fieldPath.EndsWith(".x", System.StringComparison.Ordinal)
                || fieldPath.EndsWith(".y", System.StringComparison.Ordinal)
                || fieldPath.EndsWith(".remainingDist", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// 格式化容差值为可读字符串（用于差异报告输出）。
        /// </summary>
        /// <param name="tolerance">容差值。</param>
        /// <returns>可读的容差描述。</returns>
        internal static string FormatTolerance(float tolerance)
        {
            if (tolerance <= 0f)
            {
                return "精确相等";
            }
            return tolerance.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
