using System;
using UnityEditor;

namespace TEngine
{
    /// <summary>
    /// 构建流程阶段。
    /// <remarks>阶段按执行顺序排列；结果中的 Stage 表示已到达（或失败于）的阶段，未执行的阶段不冒充成功。</remarks>
    /// </summary>
    public enum BuildStage
    {
        /// <summary>前置校验（无副作用检查）。</summary>
        Preflight,

        /// <summary>热更DLL编译/复制或复用校验。</summary>
        Dll,

        /// <summary>AssetBundle 构建。</summary>
        AssetBundle,

        /// <summary>最小包后处理。</summary>
        MinimalPackage,

        /// <summary>Player 构建。</summary>
        Player,

        /// <summary>全部请求阶段已完成。</summary>
        Completed,
    }

    /// <summary>
    /// 构建结果状态。
    /// </summary>
    public enum BuildStatus
    {
        /// <summary>未执行。</summary>
        NotRun,

        /// <summary>成功（所有请求阶段完成）。</summary>
        Succeeded,

        /// <summary>失败（记录失败阶段与原因）。</summary>
        Failed,

        /// <summary>取消（例如 Player 构建被用户取消）。</summary>
        Cancelled,
    }

    /// <summary>
    /// 构建流程执行结果（编辑器内轻量结果，区别于 Unity/YooAsset 的 BuildResult）。
    /// </summary>
    public class BuildExecutionResult
    {
        /// <summary>最终状态。</summary>
        public BuildStatus Status = BuildStatus.NotRun;

        /// <summary>已到达（或失败于）的阶段。</summary>
        public BuildStage Stage = BuildStage.Preflight;

        /// <summary>失败/取消原因。</summary>
        public string Error;

        /// <summary>原始异常（供诊断，正常失败可为空）。</summary>
        public Exception Exception;

        /// <summary>本次构建目标平台。</summary>
        public BuildTarget Target;

        /// <summary>本次资源版本号。</summary>
        public string PackageVersion;

        /// <summary>已确认存在的 AB 输出目录。</summary>
        public string OutputPackageDirectory;

        /// <summary>已确认存在的 Player 输出路径。</summary>
        public string PlayerOutputPath;

        /// <summary>是否成功。</summary>
        public bool Succeeded => Status == BuildStatus.Succeeded;

        /// <summary>是否失败。</summary>
        public bool Failed => Status == BuildStatus.Failed;

        /// <summary>是否被取消。</summary>
        public bool Cancelled => Status == BuildStatus.Cancelled;

        /// <summary>一行描述（用于窗口显示与 CLI 异常消息）。</summary>
        public string Describe()
        {
            switch (Status)
            {
                case BuildStatus.Succeeded:
                    return $"构建成功 [阶段:{Stage} Target:{Target} Version:{PackageVersion}] AB输出: {OutputPackageDirectory ?? "无"} Player输出: {PlayerOutputPath ?? "无"}";
                case BuildStatus.Cancelled:
                    return $"构建被取消 [阶段:{Stage} Target:{Target}] {Error}";
                case BuildStatus.Failed:
                    return $"构建失败 [阶段:{Stage} Target:{Target}] {Error}";
                default:
                    return $"构建未执行 [阶段:{Stage} Target:{Target}]";
            }
        }
    }

    /// <summary>
    /// 构建失败/取消异常：兼容包装用它向菜单与 CLI 调用方传播失败（batchmode 下导致 Unity 非零退出）。
    /// </summary>
    public class BuildExecutionException : Exception
    {
        /// <summary>失败阶段。</summary>
        public BuildStage Stage { get; }

        public BuildExecutionException(BuildStage stage, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Stage = stage;
        }
    }

    /// <summary>
    /// AssetBundle 阶段执行结果。
    /// </summary>
    public class AbStageOutcome
    {
        public bool Success;
        public string Error;
        public string OutputPackageDirectory;
        public Exception Exception;

        public static AbStageOutcome Fail(string error, Exception exception = null)
        {
            return new AbStageOutcome { Success = false, Error = error, Exception = exception };
        }

        public static AbStageOutcome Succeed(string outputPackageDirectory)
        {
            return new AbStageOutcome { Success = true, OutputPackageDirectory = outputPackageDirectory };
        }
    }

    /// <summary>
    /// Player 阶段执行结果。
    /// </summary>
    public class PlayerStageOutcome
    {
        public BuildStatus Status = BuildStatus.NotRun;
        public string Error;
        public string OutputPath;
        public Exception Exception;

        public static PlayerStageOutcome Fail(string error, Exception exception = null)
        {
            return new PlayerStageOutcome { Status = BuildStatus.Failed, Error = error, Exception = exception };
        }

        public static PlayerStageOutcome Cancelled(string error)
        {
            return new PlayerStageOutcome { Status = BuildStatus.Cancelled, Error = error };
        }

        public static PlayerStageOutcome Succeed(string outputPath)
        {
            return new PlayerStageOutcome { Status = BuildStatus.Succeeded, OutputPath = outputPath };
        }
    }
}
