/** FUIPreprocessBuildValidator.cs — 构建前自动校验 FUI 资源规范
 *
 * 对应 OpenSpec Change integrate-fairygui-module 任务 8.2。
 * 将 FUIResourceValidator.ValidateAll 接入 Unity 自动构建前钩子
 * （IPreprocessBuildWithReport），校验失败时抛出 BuildFailedException
 * 阻断构建，避免把 FUI 资源寻址错误推迟到后续 Player 接入。
 *
 * 校验维度与手动菜单入口（FUIResourceValidator.ValidateFromMenu）完全一致，
 * 覆盖 {PackageName}_fui 描述、内部包名、外部资源前缀、location 唯一性
 * 与历史命名冲突五个维度。本钩子不重复校验逻辑，只复用 ValidateAll。
 *
 * 依据 design.md 决策11：构建前校验描述 location、内部包名、资源前缀、
 * 重复文件和生成注册信息，避免把寻址错误推迟到后续 Player 接入。
 */

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GameFUI.Editor
{
    /// <summary>
    /// Unity 构建前钩子：自动执行 FUI 资源规范校验。
    /// 校验失败时抛出 <see cref="BuildFailedException"/> 阻断构建，
    /// 异常消息包含全部错误项，便于定位修复。
    /// </summary>
    /// <remarks>
    /// callbackOrder 设为 -1000：负值表示在默认处理器之前执行，
    /// 早于 YooAsset 资源编目等默认流程；与项目现有 Spine 处理器（-2000）
    /// 互不覆盖（Spine 只改写 Prefab/贴图，不涉及 FUI 资源）。
    /// Unity 自动发现 IPreprocessBuildWithReport 实现类，无需 [InitializeOnLoad]。
    /// </remarks>
    public class FUIPreprocessBuildValidator : IPreprocessBuildWithReport
    {
        /// <summary>
        /// 回调顺序。负值表示在默认处理器之前执行。
        /// SpineBuildPreprocessor 使用 -2000；本钩子使用 -1000，
        /// 既早于 YooAsset 编目等默认流程，又与 Spine 处理器互不覆盖。
        /// </summary>
        public int callbackOrder => -1000;

        /// <summary>
        /// 构建前回调：执行 FUI 资源校验，失败时抛出异常阻断构建。
        /// </summary>
        /// <param name="report">Unity 构建报告（本钩子不读取其内容）。</param>
        void IPreprocessBuildWithReport.OnPreprocessBuild(BuildReport report)
        {
            List<string> errors = new List<string>();
            if (FUIResourceValidator.ValidateAll(errors))
            {
                return; // 校验通过，放行构建。
            }

            // 校验失败：聚合全部错误信息，抛出 Unity 构建阻断异常。
            // BuildFailedException 是 Unity 在构建回调中识别的语义化异常，
            // 抛出后构建流程立即终止并报告失败。
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[GameFUI.Editor] FUI 资源构建前校验失败，已阻断构建：");
            foreach (string err in errors)
            {
                sb.AppendLine("  - " + err);
            }
            throw new BuildFailedException(sb.ToString());
        }
    }
}
