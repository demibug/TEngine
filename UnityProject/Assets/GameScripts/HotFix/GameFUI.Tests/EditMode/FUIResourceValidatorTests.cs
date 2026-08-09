using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameFUI.Editor;
using NUnit.Framework;

namespace GameFUI.Tests.EditMode
{
    /// <summary>
    /// <see cref="FUIResourceValidator"/> 的 EditMode 单元测试，覆盖任务 8.2 返工要求的三个场景：
    /// 规范资源集通过、历史命名阻断、重复 location 阻断。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// - spec fairygui-package-loading Requirement"包名与资源 location 使用统一规则"——
    ///   描述文件、内部包名、外部资源前缀、location 唯一性与历史命名冲突均为构建前阻断维度。
    /// - design.md 决策11：构建前校验避免把寻址错误推迟到 Player 接入。
    ///
    /// 测试策略：
    /// 1. 规范资源通过场景直接调用公开 <see cref="FUIResourceValidator.ValidateAll"/>，
    ///    对真实 Assets/AssetRaw/FUI 目录做端到端校验（只读，不修改真实资源）。
    /// 2. 历史命名与重复 location 场景：ValidateAll 的目标目录为 public const 硬编码，
    ///    无法注入测试目录，且本次返工禁止修改 FUIResourceValidator.cs。故按返工要求选项 (a)，
    ///    通过反射调用 private 维度检测方法，以合成文件列表验证维度逻辑。
    ///    因 ValidateAll 返回 errors.Count == 0，维度方法向 errors 写入错误即等价于
    ///    ValidateAll 对该场景返回 false，无需污染真实资源即可证明阻断语义。
    ///
    /// 资源隔离：全部测试不创建、修改或删除真实 FUI 资源文件。SetUp 快照真实目录文件集合，
    /// TearDown 断言集合不变，提供不污染真实资源状态的客观证据。
    /// </remarks>
    [TestFixture]
    public class FUIResourceValidatorTests
    {
        /// <summary>真实 FUI 资源目录的文件相对路径快照，用于 TearDown 校验未被污染。</summary>
        private List<string> _realFuiFileSnapshot;

        /// <summary>
        /// 每个测试前快照真实 FUI 资源目录文件集合，用于测试后比对，确保测试未污染真实资源。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _realFuiFileSnapshot = SnapshotRealFuiFiles();
        }

        /// <summary>
        /// 每个测试后断言真实 FUI 资源目录文件集合与 SetUp 快照一致，
        /// 确保测试未创建或删除真实资源。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            List<string> after = SnapshotRealFuiFiles();
            Assert.AreEqual(_realFuiFileSnapshot.Count, after.Count,
                "测试不应改变真实 FUI 资源目录文件数量。");
            for (int i = 0; i < after.Count; i++)
            {
                Assert.AreEqual(_realFuiFileSnapshot[i], after[i],
                    "测试不应改变真实 FUI 资源目录文件集合。");
            }
        }

        // ============================================================
        // 场景 1：规范资源集通过——真实 Assets/AssetRaw/FUI 下 UIBattle_fui/UICommon_fui/UICommon_atlas0
        // ============================================================

        /// <summary>
        /// 规范资源集应使 <see cref="FUIResourceValidator.ValidateAll"/> 返回 true 且 errors 为空。
        /// 真实工作区资源（UIBattle_fui.bytes、UICommon_fui.bytes、UICommon_atlas0.png）均为规范命名，
        /// 历史命名产物已在任务 2.7 移除，五维度校验应全部通过。
        /// </summary>
        /// <remarks>
        /// 此场景端到端验证公开 ValidateAll 入口与真实工作区资源一致性，
        /// 是钩子 FUIPreprocessBuildValidator 在真实构建前依赖的通过基线。
        /// </remarks>
        [Test]
        [Description("规范资源集：ValidateAll 返回 true，errors 为空。")]
        public void CanonicalResources_ValidateAll_ReturnsTrueAndNoErrors()
        {
            List<string> errors = new List<string>();
            bool ok = FUIResourceValidator.ValidateAll(errors);

            Assert.IsTrue(ok, "规范资源集应通过全部五维度校验。");
            Assert.AreEqual(0, errors.Count, "规范资源集不应产生任何错误：{0}",
                string.Join("; ", errors));
        }

        // ============================================================
        // 场景 2：历史命名阻断——合成 BattleUI_fui.bytes 与 Common_fui.bytes 验证维度5
        // ============================================================

        /// <summary>
        /// 历史命名并存阻断：合成文件列表含 BattleUI_fui.bytes，且规范包 UIBattle 同时存在，
        /// 维度5 应报告"新旧命名同时存在"错误。该错误写入 errors 即等价 ValidateAll 返回 false。
        /// </summary>
        /// <remarks>
        /// spec"包名与资源 location 使用统一规则"——同一逻辑包同时存在规范与历史命名产物时，
        /// 校验 SHALL 报告冲突并阻止构建。ValidateAll 硬编码扫描真实目录无法注入此场景，
        /// 故反射调用 private ValidateHistoricalNamingConflicts 以合成数据验证维度逻辑。
        /// ValidateAll 内部对同一 errors 列表调用本维度方法，故维度写入错误等价 ValidateAll 返回 false。
        /// </remarks>
        [Test]
        [Description("历史命名并存：BattleUI_fui.bytes 与规范 UIBattle 并存 → 维度5 报告新旧命名冲突。")]
        public void HistoricalNamingCoexists_Dimension5_ReportsConflict()
        {
            // 合成文件列表：历史命名描述文件，与真实工作区无关，不写入磁盘。
            List<string> allFiles = new List<string>
            {
                "Assets/AssetRaw/FUI/BattleUI_fui.bytes",
            };
            HashSet<string> canonicalNames = new HashSet<string> { "UIBattle" };
            List<string> errors = new List<string>();

            InvokePrivateVoid("ValidateHistoricalNamingConflicts",
                new object[] { allFiles, canonicalNames, errors });

            Assert.Greater(errors.Count, 0, "历史命名并存应产生阻断性错误。");
            Assert.IsTrue(
                ContainsAny(errors, "新旧命名", "历史命名"),
                "错误应包含历史命名维度信息，实际：{0}", string.Join("; ", errors));
        }

        /// <summary>
        /// 历史命名残留阻断：合成文件列表含 Common_fui.bytes 但无对应规范包 UICommon，
        /// 维度5 应报告"历史命名残留"错误。
        /// </summary>
        [Test]
        [Description("历史命名残留：Common_fui.bytes 无对应规范包 → 维度5 报告历史命名残留。")]
        public void HistoricalNamingRemnant_Dimension5_ReportsRemnant()
        {
            List<string> allFiles = new List<string>
            {
                "Assets/AssetRaw/FUI/Common_fui.bytes",
            };
            // 不含 UICommon，走残留分支。
            HashSet<string> canonicalNames = new HashSet<string>();
            List<string> errors = new List<string>();

            InvokePrivateVoid("ValidateHistoricalNamingConflicts",
                new object[] { allFiles, canonicalNames, errors });

            Assert.Greater(errors.Count, 0, "历史命名残留应产生阻断性错误。");
            Assert.IsTrue(
                ContainsAny(errors, "历史命名"),
                "错误应包含历史命名残留信息，实际：{0}", string.Join("; ", errors));
        }

        // ============================================================
        // 场景 3：重复 location 阻断——合成同无扩展名文件名验证维度4
        // ============================================================

        /// <summary>
        /// 重复 location 阻断：合成文件列表含 UIBattle_fui.bytes 与 UIBattle_fui.png，
        /// 两者无扩展名文件名均为 UIBattle_fui，维度4 应报告 location 唯一性冲突。
        /// AddressByFileName 下文件名即 location，重复会导致寻址冲突。
        /// </summary>
        /// <remarks>
        /// spec"包名与资源 location 使用统一规则"——location 唯一性为构建前阻断维度。
        /// 反射调用 private ValidateLocationUniqueness 以合成数据验证维度逻辑，
        /// ValidateAll 内部对同一 errors 列表调用本维度方法，故维度写入错误等价 ValidateAll 返回 false。
        /// </remarks>
        [Test]
        [Description("重复 location：UIBattle_fui.bytes 与 UIBattle_fui.png 同名 → 维度4 报告冲突。")]
        public void DuplicateLocation_Dimension4_ReportsConflict()
        {
            List<string> allFiles = new List<string>
            {
                "Assets/AssetRaw/FUI/UIBattle_fui.bytes",
                "Assets/AssetRaw/FUI/UIBattle_fui.png",
            };
            List<string> errors = new List<string>();

            InvokePrivateVoid("ValidateLocationUniqueness",
                new object[] { allFiles, errors });

            Assert.Greater(errors.Count, 0, "重复 location 应产生阻断性错误。");
            Assert.IsTrue(
                ContainsAny(errors, "location 唯一性", "UIBattle_fui"),
                "错误应包含 location 唯一性冲突信息，实际：{0}", string.Join("; ", errors));
        }

        // ============================================================
        // 辅助方法
        // ============================================================

        /// <summary>
        /// 反射调用 FUIResourceValidator 的 private static void 方法。
        /// 签名变更时 Assert.Fail 明确报错，避免静默跳过。
        /// </summary>
        /// <param name="methodName">维度检测方法名。</param>
        /// <param name="parameters">与方法签名一致的参数数组。</param>
        private static void InvokePrivateVoid(string methodName, object[] parameters)
        {
            MethodInfo method = typeof(FUIResourceValidator).GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                Assert.Fail($"未找到 FUIResourceValidator.{methodName}，校验器签名可能已变更。");
                return;
            }

            try
            {
                method.Invoke(null, parameters);
            }
            catch (TargetInvocationException tie)
            {
                // 解包内部异常，暴露维度方法真实错误，避免被反射层吞掉。
                if (tie.InnerException != null)
                {
                    throw tie.InnerException;
                }
                throw;
            }
        }

        /// <summary>
        /// 判断 errors 中是否包含任一关键词（大小写不敏感）。
        /// </summary>
        /// <param name="errors">错误信息集合。</param>
        /// <param name="keywords">待匹配关键词。</param>
        /// <returns>命中返回 true。</returns>
        private static bool ContainsAny(List<string> errors, params string[] keywords)
        {
            foreach (string err in errors)
            {
                foreach (string kw in keywords)
                {
                    if (err.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 快照真实 FUI 资源目录下全部文件路径（跳过 .meta 与隐藏文件），用于 TearDown 比对。
        /// </summary>
        /// <returns>排序后的文件路径列表。</returns>
        private static List<string> SnapshotRealFuiFiles()
        {
            List<string> result = new List<string>();
            string absRoot = Path.GetFullPath(FUIResourceValidator.FUIAssetRoot);
            if (!Directory.Exists(absRoot))
            {
                return result;
            }
            foreach (string absFile in Directory.EnumerateFiles(absRoot, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(absFile);
                if (fileName.EndsWith(".meta") || fileName.StartsWith("."))
                {
                    continue;
                }
                result.Add(absFile.Replace('\\', '/'));
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
