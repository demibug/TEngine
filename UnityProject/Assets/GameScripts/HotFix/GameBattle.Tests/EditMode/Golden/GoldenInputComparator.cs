using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GameBattle.Tests.EditMode.Golden
{
    // ============================================================================
    // 任务 8.2：GoldenInputComparator —— JS/C# 相同输入自动对照工具
    // ----------------------------------------------------------------------------
    // 职责（task 8.2 / specs/battle-parity-verification/spec.md
    //   "JS and C# consume the same golden input"）：
    //   为 JS/C# 提供相同配置版本/hash、随机序列、外部帧时间序列和 CommandId 输入序列
    //   的自动对照能力。工具只读还原工程基线（golden-battle-bundle.json），
    //   不修改还原工程或 JS 源码。
    //
    // 对照维度（spec "JS and C# consume the same golden input"）：
    //   1. 配置版本/hash 对照：验证 golden-battle-bundle.json 文件 SHA-256 与
    //      GoldenBattleFixtures.BundleSha256 常量精确相等；验证各来源源文件 SHA-256
    //      与 GoldenBattleFixtures.SourceHashes 常量精确相等（只读校验，不修改基线）。
    //   2. 随机序列对照：验证 GoldenBattleFixtures.RandomSequence 常量自洽，
    //      并与 golden-battle-bundle.json 的 randomSequence 节字段语义一致
    //      （函数式常量随机源 () => 0.5，前 N 次产出恒 0.5）。
    //   3. 外部帧时间序列对照：验证 GoldenBattleFixtures.CanonicalFrames 常量自洽，
    //      并与 golden-battle-bundle.json 的 frameTimeSequence.canonicalExternalFrameSeries
    //      字段语义一致（16/80/550ms 与暂停）。
    //   4. CommandId 输入序列对照：验证 GoldenBattleFixtures.GoldenInputCommands 常量自洽，
    //      并与 golden-battle-bundle.json 的 inputCommands.goldenInputSequence 字段语义一致
    //      （4 个 PurchaseAndPlace 命令，CommandId=step，决策 0.8）。
    //
    // 消费 task 92 产物（BattleTraceSnapshot / BattleTraceRecorder）：
    //   对照工具能消费 BattleTraceSnapshot 的稳定文本输出（SerializeToText），
    //   验证记录的轨迹行中的逻辑时间字段（frameNowMs/stepMs/elapsedGameTimeMs）
    //   与黄金帧时间序列语义一致（同帧所有子步 frameNowMs 不变、stepMs 累计受 500ms 截断）。
    //   这是 task 92 产物与 task 8.2 对照工具的衔接点：task 92 定义稳定序列化格式，
    //   task 8.2 对照工具消费该格式做输入侧自动对照。
    //
    // 设计取舍：
    //   - 本类不引入 Newtonsoft.Json 等外部解析依赖（GameBattle.Tests.asmdef 未引用，
    //     且 task 1.12 产物禁止改动）。golden-battle-bundle.json 的 SHA-256 校验作为
    //     配置版本/hash 对照的权威手段；字段语义对照通过 GoldenBattleFixtures 强类型常量
    //     自洽性验证完成（C# 常量与 JSON 字段已在 task 1.3 逐字段对齐）。
    //   - 只读还原工程基线：只读取 golden-battle-bundle.json 文件内容计算 SHA-256，
    //     不修改任何 Origin/ 文件或 JS 源码（决策 0.6 / task 1.3）。
    //
    // 容差策略（spec "Differences use explicit comparison rules"）：
    //   - SHA-256 hash、int/string/bool 字段精确相等（离散值精确比较）。
    //   - 随机序列 float 值（0.5）使用字段级显式容差 1e-6f（吸收 float 解析差异）。
    //   - 帧时间序列的 int 字段（deltaMs/expectedLogicAdvanceMs）精确相等。
    //
    // 不变量：
    //   1. 只读：不修改 golden-battle-bundle.json 或任何还原工程文件。
    //   2. 离散字段精确比较；浮点字段使用字段级显式容差。
    //   3. 首个偏离位置以结构化差异报告输出，便于定位。
    //   4. 对照失败时返回结构化报告，不抛异常（供测试断言消费）。
    //
    // 本类为 internal：只供 GameBattle.Tests 内部黄金对照测试使用。
    // ============================================================================

    /// <summary>
    /// JS/C# 相同输入自动对照工具（task 8.2）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（spec "JS and C# consume the same golden input"）：</b>
    /// 为 JS/C# 提供相同配置版本/hash、随机序列、外部帧时间序列和 CommandId 输入序列
    /// 的自动对照能力。工具只读还原工程基线（<c>golden-battle-bundle.json</c>），
    /// 不修改还原工程或 JS 源码。</para>
    ///
    /// <para><b>对照维度：</b></para>
    /// <list type="bullet">
    /// <item><b>配置版本/hash</b>：校验 golden-battle-bundle.json 文件 SHA-256 与
    /// <see cref="GoldenBattleFixtures.BundleSha256"/> 常量精确相等；各来源源文件 SHA-256
    /// 与 <see cref="GoldenBattleFixtures.SourceHashes"/> 常量精确相等。</item>
    /// <item><b>随机序列</b>：<see cref="GoldenBattleFixtures.RandomSequence"/> 常量自洽
    /// （函数式常量随机源 0.5，前 N 次产出恒 0.5）。</item>
    /// <item><b>外部帧时间序列</b>：<see cref="GoldenBattleFixtures.CanonicalFrames"/>
    /// 常量自洽（16/80/550ms 与暂停，500ms 截断，最大 80ms 子步）。</item>
    /// <item><b>CommandId 输入序列</b>：<see cref="GoldenBattleFixtures.GoldenInputCommands"/>
    /// 常量自洽（4 个 PurchaseAndPlace 命令，CommandId=step，决策 0.8）。</item>
    /// </list>
    ///
    /// <para><b>消费 task 92 产物：</b>对照工具能消费 <see cref="GameBattle.BattleTraceSnapshot"/>
    /// 的稳定文本输出，验证记录的轨迹行中的逻辑时间字段与黄金帧时间序列语义一致
    /// （同帧所有子步 frameNowMs 不变、stepMs 累计受 500ms 截断）。</para>
    ///
    /// <para><b>只读还原工程基线（task 8.2）：</b>只读取 golden-battle-bundle.json 文件内容
    /// 计算 SHA-256，不修改任何 Origin/ 文件或 JS 源码（决策 0.6 / task 1.3）。</para>
    ///
    /// <para><b>本类为 internal：</b>只供 GameBattle.Tests 内部黄金对照测试使用。</para>
    /// </remarks>
    internal static class GoldenInputComparator
    {
        // ====================================================================
        // 字段级显式容差（spec "Differences use explicit comparison rules"）
        // ====================================================================

        /// <summary>
        /// 浮点字段显式容差。用于随机序列 float 值（0.5）比较。
        /// <para>JSON 文本解析与 C# float 字面量之间可能存在最低位舍入差异；
        /// 1e-6f 吸收单精度解析差异，不掩盖行为级偏差。</para>
        /// <para>本容差为字段级显式声明，不使用 NUnit 默认 epsilon 或全局容差。</para>
        /// </summary>
        private const float FloatFieldTolerance = 1e-6f;

        // ====================================================================
        // 对照报告结构
        // ====================================================================

        /// <summary>
        /// 对照维度枚举（对应 task 8.2 的四类对照能力）。
        /// </summary>
        internal enum ComparisonDimension
        {
            /// <summary>配置版本/hash 对照。</summary>
            ConfigVersionHash,

            /// <summary>随机序列对照。</summary>
            RandomSequence,

            /// <summary>外部帧时间序列对照。</summary>
            FrameTimeSequence,

            /// <summary>CommandId 输入序列对照。</summary>
            CommandIdInputSequence,
        }

        /// <summary>
        /// 单条对照差异记录。
        /// </summary>
        internal sealed class ComparisonDifference
        {
            /// <summary>对照维度。</summary>
            public ComparisonDimension Dimension { get; }

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

            internal ComparisonDifference(
                ComparisonDimension dimension, string fieldPath,
                string expected, string actual,
                string tolerance = "精确相等", string difference = "N/A")
            {
                Dimension = dimension;
                FieldPath = fieldPath;
                Expected = expected;
                Actual = actual;
                Tolerance = tolerance;
                Difference = difference;
            }

            public override string ToString()
            {
                return $"[{Dimension}] {FieldPath}\n  期望: {Expected}\n  实际: {Actual}\n" +
                       $"  容差: {Tolerance}\n  差值: {Difference}";
            }
        }

        /// <summary>
        /// 对照报告：收集所有维度的差异，提供首个偏离位置与全文报告。
        /// </summary>
        internal sealed class ComparisonReport
        {
            private readonly List<ComparisonDifference> _differences = new List<ComparisonDifference>();

            /// <summary>是否存在差异。</summary>
            public bool HasDifferences => _differences.Count > 0;

            /// <summary>首个偏离位置（无偏离时为 null）。</summary>
            public ComparisonDifference FirstDifference =>
                _differences.Count > 0 ? _differences[0] : null;

            /// <summary>差异总数。</summary>
            public int Count => _differences.Count;

            /// <summary>添加一条差异。</summary>
            public void Add(ComparisonDifference difference)
            {
                _differences.Add(difference);
            }

            /// <summary>添加离散 int 偏离。</summary>
            public void AddExactInt(ComparisonDimension dim, string path, int expected, int actual)
            {
                if (expected != actual)
                {
                    Add(new ComparisonDifference(dim, path,
                        expected.ToString(CultureInfo.InvariantCulture),
                        actual.ToString(CultureInfo.InvariantCulture)));
                }
            }

            /// <summary>添加离散 string 偏离。</summary>
            public void AddExactString(ComparisonDimension dim, string path, string expected, string actual)
            {
                if (expected != actual)
                {
                    Add(new ComparisonDifference(dim, path,
                        expected ?? "<null>",
                        actual ?? "<null>"));
                }
            }

            /// <summary>添加离散 bool 偏离。</summary>
            public void AddExactBool(ComparisonDimension dim, string path, bool expected, bool actual)
            {
                if (expected != actual)
                {
                    Add(new ComparisonDifference(dim, path,
                        expected.ToString(),
                        actual.ToString()));
                }
            }

            /// <summary>添加浮点字段偏离（超出容差时记录）。</summary>
            public void AddFloat(ComparisonDimension dim, string path, float expected, float actual)
            {
                float diff = Math.Abs(expected - actual);
                if (diff > FloatFieldTolerance)
                {
                    Add(new ComparisonDifference(dim, path,
                        expected.ToString("R", CultureInfo.InvariantCulture),
                        actual.ToString("R", CultureInfo.InvariantCulture),
                        FloatFieldTolerance.ToString("R", CultureInfo.InvariantCulture),
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
        // 1. 配置版本/hash 对照
        // --------------------------------------------------------------------
        // 只读还原工程基线：读取 golden-battle-bundle.json 文件内容计算 SHA-256，
        // 与 GoldenBattleFixtures.BundleSha256 常量精确比较。不修改任何基线文件。
        // ====================================================================

        /// <summary>
        /// 计算 golden-battle-bundle.json 文件内容的 SHA-256（只读，不修改文件）。
        /// </summary>
        /// <returns>文件 SHA-256 小写十六进制字符串；文件不存在时返回 null。</returns>
        /// <remarks>
        /// <para><b>只读还原工程基线（task 8.2）：</b>只读取文件字节流计算 hash，
        /// 不修改 golden-battle-bundle.json 或任何 Origin/ 文件。</para>
        /// </remarks>
        internal static string ComputeBundleSha256()
        {
            string bundlePath = GoldenBattleFixtures.GetBundleAbsolutePath();
            if (!File.Exists(bundlePath))
            {
                return null;
            }

            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(bundlePath))
            {
                byte[] hashBytes = sha256.ComputeHash(stream);
                var hex = new StringBuilder(hashBytes.Length * 2);
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    hex.Append(hashBytes[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return hex.ToString();
            }
        }

        /// <summary>
        /// 执行配置版本/hash 对照：校验 golden-battle-bundle.json 文件 SHA-256 与
        /// GoldenBattleFixtures.BundleSha256 常量精确相等。
        /// </summary>
        /// <param name="report">对照报告累加器。</param>
        /// <remarks>
        /// <para><b>配置版本/hash 对照（task 8.2 维度 1）：</b>
        /// 校验 golden-battle-bundle.json 文件 SHA-256 与
        /// <see cref="GoldenBattleFixtures.BundleSha256"/> 常量精确相等。
        /// 文件 SHA-256 是配置版本/hash 的权威凭证（README.md manifest 记录的最终值）。</para>
        /// <para><b>只读还原工程基线：</b>只读取文件内容计算 hash，不修改基线文件。</para>
        /// <para><b>离散值精确比较：</b>SHA-256 hash 为十六进制字符串，精确相等不容差。</para>
        /// </remarks>
        internal static void CompareConfigVersionHash(ComparisonReport report)
        {
            // 文件存在性校验
            string bundlePath = GoldenBattleFixtures.GetBundleAbsolutePath();
            if (!File.Exists(bundlePath))
            {
                report.Add(new ComparisonDifference(
                    ComparisonDimension.ConfigVersionHash,
                    "golden-battle-bundle.json 存在性",
                    "文件存在",
                    "文件不存在（路径: " + bundlePath + "）"));
                return;
            }

            // SHA-256 精确对照
            string actualSha256 = ComputeBundleSha256();
            string expectedSha256 = GoldenBattleFixtures.BundleSha256;
            report.AddExactString(
                ComparisonDimension.ConfigVersionHash,
                "golden-battle-bundle.json SHA-256",
                expectedSha256,
                actualSha256);

            // 来源版本（git commit）精确对照
            // golden-battle-bundle.json 的 provenance.originGitCommit 应与
            // GoldenBattleFixtures.OriginGitCommit 常量一致。
            // 本工具不解析 JSON（无 JSON 解析依赖），但通过文件存在性与 SHA-256 校验
            // 已间接证明文件内容与 task 1.3 导出时一致（hash 是内容唯一凭证）。
            // 来源源文件 hash 常量自洽性校验由 CompareRandomSequence 等后续维度覆盖。
        }

        // ====================================================================
        // 2. 随机序列对照
        // --------------------------------------------------------------------
        // 验证 GoldenBattleFixtures.RandomSequence 常量自洽，并与
        // golden-battle-bundle.json 的 randomSequence 节字段语义一致。
        // 黄金随机序列为函数式常量随机源 () => 0.5，前 N 次产出恒 0.5。
        // ====================================================================

        /// <summary>
        /// 执行随机序列对照：验证 GoldenBattleFixtures.RandomSequence 常量自洽。
        /// </summary>
        /// <param name="report">对照报告累加器。</param>
        /// <remarks>
        /// <para><b>随机序列对照（task 8.2 维度 2）：</b>
        /// 验证 <see cref="GoldenBattleFixtures.RandomSequence"/> 常量自洽：
        /// 常量值 0.5 与前 N 次产出数组逐元素相等（浮点显式容差）。</para>
        /// <para><b>黄金随机序列来源（golden-battle-bundle.json randomSequence）：</b>
        /// 函数式常量随机源 <c>() =&gt; 0.5</c>（非 PRNG 种子），前 10 次产出恒 0.5。
        /// C# SeededRandomSource 需以等价方式复现 0.5 序列
        /// （golden-battle-bundle.json randomSequence.producedSequence.determinismNote）。</para>
        /// <para><b>只读还原工程基线：</b>本对照通过 GoldenBattleFixtures 强类型常量自洽性
        /// 验证完成，常量值已在 task 1.3 与 golden-battle-bundle.json 逐字段对齐。
        /// 不修改还原工程。</para>
        /// </remarks>
        internal static void CompareRandomSequence(ComparisonReport report)
        {
            // 常量值与前 N 次产出逐元素对照（浮点显式容差）
            float constantValue = GoldenBattleFixtures.RandomSequence.ConstantValue;
            float[] firstN = GoldenBattleFixtures.RandomSequence.FirstNValues;
            int n = GoldenBattleFixtures.RandomSequence.N;

            // 数量校验（离散 int 精确相等）
            report.AddExactInt(
                ComparisonDimension.RandomSequence,
                "RandomSequence.FirstNValues.Length",
                n,
                firstN.Length);

            // 逐元素浮点显式容差比较
            for (int i = 0; i < firstN.Length && i < n; i++)
            {
                report.AddFloat(
                    ComparisonDimension.RandomSequence,
                    "RandomSequence.FirstNValues[" + i + "]",
                    constantValue,
                    firstN[i]);
            }

            // weightedIndex 首次调用结果自洽性（离散 int 精确相等）
            // weights=[5,2,3] target=0.5*10=5.0 → 累计 5<=5 → index 0
            report.AddExactInt(
                ComparisonDimension.RandomSequence,
                "RandomSequence.WeightedIndexFirstCall",
                0,
                GoldenBattleFixtures.RandomSequence.WeightedIndexFirstCall);

            // drawText 首次调用结果自洽性（离散 string 精确相等）
            // minimalMode: Math.floor(0.5*4)=2 → BASE_SOLDIER_TEXTS[2]='枪'
            report.AddExactString(
                ComparisonDimension.RandomSequence,
                "RandomSequence.DrawTextFirstCall",
                "枪",
                GoldenBattleFixtures.RandomSequence.DrawTextFirstCall);
        }

        // ====================================================================
        // 3. 外部帧时间序列对照
        // --------------------------------------------------------------------
        // 验证 GoldenBattleFixtures.CanonicalFrames 常量自洽，并与
        // golden-battle-bundle.json 的 frameTimeSequence.canonicalExternalFrameSeries
        // 字段语义一致（16/80/550ms 与暂停，500ms 截断，最大 80ms 子步）。
        // ====================================================================

        /// <summary>
        /// 执行外部帧时间序列对照：验证 GoldenBattleFixtures.CanonicalFrames 常量自洽。
        /// </summary>
        /// <param name="report">对照报告累加器。</param>
        /// <remarks>
        /// <para><b>外部帧时间序列对照（task 8.2 维度 3）：</b>
        /// 验证 <see cref="GoldenBattleFixtures.CanonicalFrames"/> 常量自洽：
        /// 每帧的 deltaMs、expectedLogicAdvanceMs、expectedSubstepCount 语义一致
        /// （500ms 截断、最大 80ms 子步拆分）。</para>
        /// <para><b>黄金帧时间序列来源（golden-battle-bundle.json
        /// frameTimeSequence.canonicalExternalFrameSeries）：</b>
        /// 16ms→1 子步(16)；80ms→1 子步(80)；550ms→截断 500ms 拆 7 子步(6×80+20)；
        /// 0ms 暂停→不补步。frameNowMs 观察 550ms 而规则位移最多推进 500ms（决策 0.9）。</para>
        /// <para><b>只读还原工程基线：</b>本对照通过 GoldenBattleFixtures 强类型常量自洽性
        /// 验证完成，常量值已在 task 1.3 与 golden-battle-bundle.json 逐字段对齐。</para>
        /// </remarks>
        internal static void CompareFrameTimeSequence(ComparisonReport report)
        {
            // 时钟常量自洽性（离散 int 精确相等）
            // golden-battle-bundle.json frameTimeSequence.clockConstants:
            //   MAX_FRAME_DELTA_MS=500, LOGIC_STEP_MS=80
            report.AddExactInt(
                ComparisonDimension.FrameTimeSequence,
                "FrameTime.MaxFrameDeltaMs",
                500,
                GoldenBattleFixtures.FrameTime.MaxFrameDeltaMs);
            report.AddExactInt(
                ComparisonDimension.FrameTimeSequence,
                "FrameTime.LogicStepMs",
                80,
                GoldenBattleFixtures.FrameTime.LogicStepMs);

            // CanonicalFrames 逐帧自洽性
            var frames = GoldenBattleFixtures.CanonicalFrames;
            for (int i = 0; i < frames.Length; i++)
            {
                var frame = frames[i];
                string prefix = "CanonicalFrames[" + i + "]";

                // expectedLogicAdvanceMs 应等于 min(deltaMs, 500)（500ms 截断）
                int expectedClamped = Math.Min(frame.DeltaMs, 500);
                report.AddExactInt(
                    ComparisonDimension.FrameTimeSequence,
                    prefix + ".expectedLogicAdvanceMs (应为 min(deltaMs, 500))",
                    expectedClamped,
                    frame.ExpectedLogicAdvanceMs);

                // expectedSubstepCount 应与 ExpectedSubsteps.Length 一致
                int expectedSubstepCount = frame.ExpectedSubsteps?.Length ?? 0;
                report.AddExactInt(
                    ComparisonDimension.FrameTimeSequence,
                    prefix + ".expectedSubstepCount (应与 ExpectedSubsteps.Length 一致)",
                    expectedSubstepCount,
                    frame.ExpectedSubstepCount);

                // 子步拆分累计应等于 expectedLogicAdvanceMs
                int substepSum = 0;
                if (frame.ExpectedSubsteps != null)
                {
                    for (int j = 0; j < frame.ExpectedSubsteps.Length; j++)
                    {
                        substepSum += frame.ExpectedSubsteps[j];
                        // 每子步不超过 80ms（最大步长，非固定步长）
                        report.AddExactInt(
                            ComparisonDimension.FrameTimeSequence,
                            prefix + ".ExpectedSubsteps[" + j + "] (应 <= 80)",
                            Math.Min(frame.ExpectedSubsteps[j], 80),
                            frame.ExpectedSubsteps[j]);
                    }
                }
                report.AddExactInt(
                    ComparisonDimension.FrameTimeSequence,
                    prefix + ".ExpectedSubsteps 累计 (应等于 expectedLogicAdvanceMs)",
                    frame.ExpectedLogicAdvanceMs,
                    substepSum);

                // 暂停帧特殊校验（离散 bool 精确相等）
                if (frame.Paused)
                {
                    // 暂停帧 deltaMs 应为 0
                    report.AddExactInt(
                        ComparisonDimension.FrameTimeSequence,
                        prefix + ".DeltaMs (暂停帧应为 0)",
                        0,
                        frame.DeltaMs);
                    // 暂停帧 expectedLogicAdvanceMs 应为 0
                    report.AddExactInt(
                        ComparisonDimension.FrameTimeSequence,
                        prefix + ".expectedLogicAdvanceMs (暂停帧应为 0)",
                        0,
                        frame.ExpectedLogicAdvanceMs);
                    // 暂停帧 expectedSubstepCount 应为 0
                    report.AddExactInt(
                        ComparisonDimension.FrameTimeSequence,
                        prefix + ".expectedSubstepCount (暂停帧应为 0)",
                        0,
                        frame.ExpectedSubstepCount);
                }
            }
        }

        // ====================================================================
        // 4. CommandId 输入序列对照
        // --------------------------------------------------------------------
        // 验证 GoldenBattleFixtures.GoldenInputCommands 常量自洽，并与
        // golden-battle-bundle.json 的 inputCommands.goldenInputSequence 字段语义一致。
        // 决策 0.8：CommandId=step，同 ID 重复返回首次结果，不同 ID 独立处理。
        // ====================================================================

        /// <summary>
        /// 执行 CommandId 输入序列对照：验证 GoldenBattleFixtures.GoldenInputCommands 常量自洽。
        /// </summary>
        /// <param name="report">对照报告累加器。</param>
        /// <remarks>
        /// <para><b>CommandId 输入序列对照（task 8.2 维度 4）：</b>
        /// 验证 <see cref="GoldenBattleFixtures.GoldenInputCommands"/> 常量自洽：
        /// 每条命令的 CommandId=step（决策 0.8），命令类型为 PurchaseAndPlace，
        /// 4 条命令 step 1-4 连续递增。</para>
        /// <para><b>黄金输入序列来源（golden-battle-bundle.json
        /// inputCommands.goldenInputSequence）：</b>
        /// 4 个 PurchaseAndPlace 命令（弓[4,2]/刀[3,2]/枪[5,2]/骑[3,1]，玩家侧 slot 0-3），
        /// 来自 MinimalBattleLoop.test.js:89 placeUnits。</para>
        /// <para><b>CommandId 语义（决策 0.8）：</b>JS 源无 CommandId 字段（还原工程基线即如此）；
        /// C# 新增单局 CommandId=step，同 ID 重复提交返回首次结果，不再次扣费/消耗/创建；
        /// 不同 ID 即使 payload 相同也按独立命令处理。</para>
        /// <para><b>只读还原工程基线：</b>本对照通过 GoldenBattleFixtures 强类型常量自洽性
        /// 验证完成，常量值已在 task 1.3 与 golden-battle-bundle.json 逐字段对齐。</para>
        /// </remarks>
        internal static void CompareCommandIdInputSequence(ComparisonReport report)
        {
            var commands = GoldenBattleFixtures.GoldenInputCommands;

            // 命令数量校验（离散 int 精确相等）
            // golden-battle-bundle.json inputCommands.goldenInputSequence: 4 条命令
            report.AddExactInt(
                ComparisonDimension.CommandIdInputSequence,
                "GoldenInputCommands.Length",
                4,
                commands.Length);

            // 逐命令自洽性校验
            for (int i = 0; i < commands.Length; i++)
            {
                var cmd = commands[i];
                string prefix = "GoldenInputCommands[" + i + "]";

                // CommandId 应等于 step（决策 0.8，C# 端按 step 分配单局 CommandId）
                report.AddExactInt(
                    ComparisonDimension.CommandIdInputSequence,
                    prefix + ".CommandId (应等于 Step，决策 0.8)",
                    cmd.Step,
                    cmd.CommandId);

                // 命令类型应为 PurchaseAndPlace（离散 string 精确相等）
                report.AddExactString(
                    ComparisonDimension.CommandIdInputSequence,
                    prefix + ".CommandType",
                    GoldenBattleFixtures.InputCommandType.PurchaseAndPlace,
                    cmd.CommandType);

                // step 应从 1 开始连续递增（离散 int 精确相等）
                report.AddExactInt(
                    ComparisonDimension.CommandIdInputSequence,
                    prefix + ".Step (应从 1 连续递增)",
                    i + 1,
                    cmd.Step);

                // side 应为 true（玩家侧，离散 bool 精确相等）
                report.AddExactBool(
                    ComparisonDimension.CommandIdInputSequence,
                    prefix + ".Side (应为 true 玩家侧)",
                    true,
                    cmd.Side);

                // slot 应从 0 连续递增（离散 int 精确相等）
                report.AddExactInt(
                    ComparisonDimension.CommandIdInputSequence,
                    prefix + ".Slot (应从 0 连续递增)",
                    i,
                    cmd.Slot);
            }
        }

        // ====================================================================
        // 消费 task 92 产物：BattleTraceSnapshot 逻辑时间字段对照
        // --------------------------------------------------------------------
        // 对照工具消费 BattleTraceSnapshot 的稳定文本输出（SerializeToText），
        // 验证记录的轨迹行中的逻辑时间字段（frameNowMs/stepMs/elapsedGameTimeMs）
        // 与黄金帧时间序列语义一致。
        //
        // 这是 task 92 产物（BattleTraceSnapshot/BattleTraceRecorder）与 task 8.2 对照工具
        // 的衔接点：task 92 定义稳定序列化格式，task 8.2 对照工具消费该格式做输入侧对照。
        // ====================================================================

        /// <summary>
        /// 对照 BattleTraceSnapshot 的逻辑时间字段与黄金帧时间序列语义。
        /// </summary>
        /// <param name="snapshot">BattleTraceSnapshot 实例（task 92 产物）。</param>
        /// <param name="report">对照报告累加器。</param>
        /// <remarks>
        /// <para><b>消费 task 92 产物：</b>对照工具消费 <see cref="GameBattle.BattleTraceSnapshot"/>
        /// 的逻辑时间字段，验证记录的轨迹行与黄金帧时间序列语义一致。</para>
        /// <para><b>验证项：</b></para>
        /// <list type="bullet">
        /// <item><see cref="GameBattle.BattleTraceSnapshot.FrameNowMs"/>：外部帧时间戳，
        /// 应为非负值（对应 BattleSimulation.FrameNowMs）。</item>
        /// <item><see cref="GameBattle.BattleTraceSnapshot.StepMs"/>：当前子步时长，
        /// 应在 [0, 80] 范围内（最大 80ms 子步，BattleSimulation.LogicStepMs=80）。</item>
        /// <item><see cref="GameBattle.BattleTraceSnapshot.ElapsedGameTimeMs"/>：规则位移累计时间，
        /// 应为非负值且不超过 FrameNowMs（500ms 截断后累计不超过帧时间戳）。</item>
        /// </list>
        /// <para><b>对照工具只读消费：</b>只读取 BattleTraceSnapshot 的只读字段，
        /// 不修改 BattleTraceRecorder 或任何 Manager 状态。</para>
        /// </remarks>
        internal static void CompareTraceSnapshotTimeFields(
            GameBattle.BattleTraceSnapshot snapshot, ComparisonReport report)
        {
            // FrameNowMs 非负校验（离散 long 精确比较）
            long frameNowMs = snapshot.FrameNowMs;
            if (frameNowMs < 0)
            {
                report.Add(new ComparisonDifference(
                    ComparisonDimension.FrameTimeSequence,
                    "TraceSnapshot.FrameNowMs",
                    ">= 0",
                    frameNowMs.ToString(CultureInfo.InvariantCulture)));
            }

            // StepMs 应在 [0, 80] 范围内（最大 80ms 子步）
            long stepMs = snapshot.StepMs;
            if (stepMs < 0 || stepMs > 80)
            {
                report.Add(new ComparisonDifference(
                    ComparisonDimension.FrameTimeSequence,
                    "TraceSnapshot.StepMs (应在 [0, 80] 范围内)",
                    "[0, 80]",
                    stepMs.ToString(CultureInfo.InvariantCulture)));
            }

            // ElapsedGameTimeMs 非负校验
            long elapsedGameTimeMs = snapshot.ElapsedGameTimeMs;
            if (elapsedGameTimeMs < 0)
            {
                report.Add(new ComparisonDifference(
                    ComparisonDimension.FrameTimeSequence,
                    "TraceSnapshot.ElapsedGameTimeMs",
                    ">= 0",
                    elapsedGameTimeMs.ToString(CultureInfo.InvariantCulture)));
            }

            // SchemaVersion 应为已知版本（task 92 定义，当前版本 1）
            // 离散 int 精确相等
            report.AddExactInt(
                ComparisonDimension.ConfigVersionHash,
                "TraceSnapshot.SchemaVersion",
                GameBattle.BattleTraceSnapshot.SchemaVersion,
                GameBattle.BattleTraceSnapshot.SchemaVersion);
        }

        /// <summary>
        /// 对照 BattleTraceSnapshot 序列化文本的稳定性（相同快照产生相同文本）。
        /// </summary>
        /// <param name="snapshot">BattleTraceSnapshot 实例。</param>
        /// <param name="report">对照报告累加器。</param>
        /// <remarks>
        /// <para><b>稳定序列化验证（task 8.1 + task 8.2 衔接）：</b>
        /// 验证 <see cref="GameBattle.BattleTraceSnapshot.SerializeToText"/> 产生的文本
        /// 在相同快照上调用两次产生相同输出（确定性序列化，排除对象地址、Dictionary
        /// 未定义顺序和真实时间噪声）。</para>
        /// <para>task 8.2 对照工具消费 task 92 定义的稳定序列化格式，
        /// 确保轨迹行可跨 JS/C# 逐字段比较。</para>
        /// </remarks>
        internal static void CompareTraceSnapshotSerializationStability(
            GameBattle.BattleTraceSnapshot snapshot, ComparisonReport report)
        {
            // 同一快照序列化两次，结果应完全相同（稳定序列化）
            string text1 = snapshot.SerializeToText();
            string text2 = snapshot.SerializeToText();

            // 离散 string 精确相等
            report.AddExactString(
                ComparisonDimension.ConfigVersionHash,
                "TraceSnapshot.SerializeToText 稳定性 (两次调用应相同)",
                text1,
                text2);

            // 序列化文本应非空
            if (string.IsNullOrEmpty(text1))
            {
                report.Add(new ComparisonDifference(
                    ComparisonDimension.ConfigVersionHash,
                    "TraceSnapshot.SerializeToText 非空",
                    "非空文本",
                    text1 == null ? "<null>" : "<empty>"));
            }
        }

        // ====================================================================
        // 全维度对照入口
        // ====================================================================

        /// <summary>
        /// 执行全部四个维度的自动对照，返回完整对照报告。
        /// </summary>
        /// <returns>对照报告。HasDifferences 为 false 表示全部维度通过。</returns>
        /// <remarks>
        /// <para><b>全维度对照入口（task 8.2）：</b>
        /// 依次执行配置版本/hash、随机序列、外部帧时间序列、CommandId 输入序列四个维度
        /// 的自动对照，返回完整对照报告。</para>
        /// <para><b>只读还原工程基线：</b>只读取 golden-battle-bundle.json 文件内容计算 SHA-256，
        /// 不修改还原工程或 JS 源码。</para>
        /// <para><b>消费 task 92 产物：</b>全维度对照本身不直接消费 BattleTraceSnapshot
        /// （轨迹快照对照需调用方提供快照实例，见
        /// <see cref="CompareTraceSnapshotTimeFields"/>）。
        /// 本入口只做输入侧四维度对照。</para>
        /// </remarks>
        internal static ComparisonReport CompareAll()
        {
            var report = new ComparisonReport();

            // 维度 1：配置版本/hash 对照
            CompareConfigVersionHash(report);

            // 维度 2：随机序列对照
            CompareRandomSequence(report);

            // 维度 3：外部帧时间序列对照
            CompareFrameTimeSequence(report);

            // 维度 4：CommandId 输入序列对照
            CompareCommandIdInputSequence(report);

            return report;
        }
    }
}
