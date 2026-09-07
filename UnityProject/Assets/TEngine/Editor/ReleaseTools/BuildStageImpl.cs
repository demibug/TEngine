using System;
using UnityEditor;

namespace TEngine
{
    /// <summary>
    /// 构建阶段实现集合。
    /// <remarks>默认指向真实实现；测试可替换字段注入替身，以验证失败后下游不被调用。不引入通用服务容器。</remarks>
    /// </summary>
    public sealed class BuildStageImpl
    {
        /// <summary>编辑器当前激活平台。</summary>
        public Func<BuildTarget> GetActiveTarget = () => EditorUserBuildSettings.activeBuildTarget;

        /// <summary>编辑器程序集是否编译进 HybridCLR 支持。</summary>
        public Func<bool> IsHybridClrAvailable = () => BuildDLLCommand.HybridClrAvailable;

        /// <summary>编译并复制热更DLL（失败应抛异常）。</summary>
        public Action<BuildConfig> CompileHotFixDll = config =>
        {
            var result = BuildDLLCommand.BuildAndCopyDllsWithResult(config.BuildTarget);
            if (result == null || !result.Success)
                throw new BuildExecutionException(BuildStage.Dll,
                    result == null ? "DLL 构建返回空结果" : result.GetErrorSummary());
        };

        /// <summary>复用模式校验（BuildHotFixDll=false）：校验已有 .bytes 与所选目标源文件一致（失败应抛异常）。</summary>
        public Action<BuildConfig> ValidateReusedDll = config =>
        {
            var result = BuildDLLCommand.ValidateReusedDllsWithResult(config.BuildTarget);
            if (result == null || !result.Success)
                throw new BuildExecutionException(BuildStage.Dll,
                    result == null ? "DLL 复用校验返回空结果" : result.GetErrorSummary());
        };

        /// <summary>执行 AssetBundle 构建（含输出必需文件核验）。</summary>
        public Func<BuildConfig, AbStageOutcome> BuildAssetBundle = config => ReleaseTools.BuildAssetBundleStage(config);

        /// <summary>执行最小包后处理（失败应抛异常）。</summary>
        public Action<BuildConfig, BuildExecutionResult> ProcessMinimalPackage = (config, result) =>
            ReleaseTools.ProcessMinimalPackageStage(config, result);

        /// <summary>执行 Player 构建（显式目标/目标组/输出路径，不自动切平台）。</summary>
        public Func<BuildTarget, BuildTargetGroup, string, PlayerStageOutcome> BuildPlayer =
            (target, targetGroup, outputPath) => ReleaseTools.BuildPlayerStage(target, targetGroup, outputPath);

        public static BuildStageImpl CreateDefault()
        {
            return new BuildStageImpl();
        }
    }
}
