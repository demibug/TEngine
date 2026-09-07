using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;
using BuildResult = UnityEditor.Build.Reporting.BuildResult;

namespace TEngine
{
    /// <summary>
    /// 打包工具类。
    /// <remarks>通过CommandLineReader可以不前台开启Unity实现静默打包以及CLI工作流，详见CommandLineReader.cs example1。
    /// 全流程以 BuildExecutionResult 区分成功/失败/取消；失败阶段停止下游，兼容包装以构建异常向菜单/CLI 传播失败（batchmode 下导致非零退出）。</remarks>
    /// </summary>
    public static class ReleaseTools
    {
        /// <summary>本次构建的资源包名。</summary>
        public const string PackageName = "DefaultPackage";

        #region CLI 入口

        /// <summary>
        /// CLI：编译并复制热更DLL（显式平台参数贯穿）。
        /// <remarks>失败抛出构建异常，batchmode 下 Unity 非零退出。</remarks>
        /// </summary>
        public static void BuildDll()
        {
            string platform = CommandLineReader.GetCustomArgument("platform");
            if (string.IsNullOrEmpty(platform))
                throw new BuildExecutionException(BuildStage.Preflight, "BuildDll 失败：缺少 platform 参数");

            if (!TryGetBuildTarget(platform, out BuildTarget target))
                throw new BuildExecutionException(BuildStage.Preflight, $"BuildDll 失败：未知平台 '{platform}'");

            var result = BuildDLLCommand.BuildAndCopyDllsWithResult(target);
            if (result == null || !result.Success)
                throw new BuildExecutionException(BuildStage.Dll,
                    result == null ? "DLL 构建返回空结果" : result.GetErrorSummary());
        }

        /// <summary>
        /// CLI：仅构建 AssetBundle（保留旧入口范围：不编译DLL、不打包Player、不做最小包）。
        /// <remarks>旧 BuildInternal 的有效默认配置在此显式映射；版本号必须显式提供，不回落 1.0。失败抛出构建异常。</remarks>
        /// </summary>
        public static void BuildAssetBundle()
        {
            string outputRoot = CommandLineReader.GetCustomArgument("outputRoot");
            if (string.IsNullOrEmpty(outputRoot))
                throw new BuildExecutionException(BuildStage.Preflight, "BuildAssetBundle 失败：缺少 outputRoot 参数");

            string packageVersion = CommandLineReader.GetCustomArgument("packageVersion");
            if (string.IsNullOrEmpty(packageVersion))
                throw new BuildExecutionException(BuildStage.Preflight, "BuildAssetBundle 失败：缺少 packageVersion 参数");

            string platform = CommandLineReader.GetCustomArgument("platform");
            if (string.IsNullOrEmpty(platform))
                throw new BuildExecutionException(BuildStage.Preflight, "BuildAssetBundle 失败：缺少 platform 参数");

            if (!TryGetBuildTarget(platform, out BuildTarget target))
                throw new BuildExecutionException(BuildStage.Preflight, $"BuildAssetBundle 失败：未知平台 '{platform}'");

            Debug.LogWarning($"Start BuildPackage BuildTarget:{target} outputPath:{outputRoot} packageVersion:{packageVersion}");

            var config = CreateLegacyCliConfig(target, outputRoot, packageVersion);
            var result = ExecuteBuildWithResult(config, buildPlayer: false);
            ThrowIfNotSuccess(result);
        }

        /// <summary>
        /// 旧 CLI 路径的显式配置映射（源自原 BuildInternal 默认值，不静默改变行为）。
        /// </summary>
        private static BuildConfig CreateLegacyCliConfig(BuildTarget target, string outputRoot, string packageVersion)
        {
            return new BuildConfig
            {
                BuildTarget = target,
                BuildPipeline = EBuildPipeline.ScriptableBuildPipeline,
                CompressOption = ECompressOption.LZ4,
                EncryptionType = GetEncryptionTypeFromResourceModuleDriver(),
                PackageVersion = packageVersion,
                OutputRoot = outputRoot,
                MinimalPackage = false,
                RetainTags = string.Empty,
                EnableSharePackRule = true,
                UseAssetDependencyDB = true,
                ClearBuildCache = false,
                VerifyBuildingResult = true,
                BuildinFileCopyOption = EBuildinFileCopyOption.ClearAndCopyAll,
                FileNameStyle = EFileNameStyle.BundleName_HashName,
                BuildHotFixDll = false,
                BuildPlayer = false,
                PlayerPlatform = target,
                PlayerOutputPath = string.Empty,
            };
        }

        #endregion

        #region MenuItem 入口（兼容原有菜单）

        [MenuItem("TEngine/Build/一键打包AssetBundle _F8")]
        public static void BuildCurrentPlatformAB()
        {
            var config = BuildConfig.CreateDefault();
            config.BuildHotFixDll = true;
            BuildWithConfig(config, buildPlayer: false);
        }

        [MenuItem("TEngine/Build/一键打包Window", false, 30)]
        public static void AutomationBuild()
        {
            var config = BuildConfig.CreateDefault();
            config.BuildTarget = BuildTarget.StandaloneWindows64;
            config.OutputRoot = Application.dataPath + "/../Builds/Windows";
            config.BuildPlayer = true;
            config.PlayerPlatform = BuildTarget.StandaloneWindows64;
            config.PlayerOutputPath = $"{Application.dataPath}/../Build/Windows/Release_Windows.exe";
            BuildWithConfig(config, buildPlayer: true);
        }

        [MenuItem("TEngine/Build/一键打包Android", false, 30)]
        public static void AutomationBuildAndroid()
        {
            var config = BuildConfig.CreateDefault();
            config.BuildTarget = BuildTarget.Android;
            config.OutputRoot = Application.dataPath + "/../Bundles";
            config.BuildPlayer = true;
            config.PlayerPlatform = BuildTarget.Android;
            config.PlayerOutputPath = $"{Application.dataPath}/../Build/Android/{BuildConfig.GetDefaultPackageVersion()}Android.apk";
            BuildWithConfig(config, buildPlayer: true);
        }

        [MenuItem("TEngine/Build/一键打包IOS", false, 30)]
        public static void AutomationBuildIOS()
        {
            var config = BuildConfig.CreateDefault();
            config.BuildTarget = BuildTarget.iOS;
            config.OutputRoot = Application.dataPath + "/../Bundles";
            config.BuildPlayer = true;
            config.PlayerPlatform = BuildTarget.iOS;
            config.PlayerOutputPath = $"{Application.dataPath}/../Build/IOS/XCode_Project";
            BuildWithConfig(config, buildPlayer: true);
        }

        #endregion

        #region 参数化构建入口

        /// <summary>
        /// 通过 BuildConfig 执行完整构建流程（兼容包装）。
        /// <remarks>失败/取消抛出 BuildExecutionException（窗口显示失败；CLI batchmode 下非零退出）。</remarks>
        /// </summary>
        public static void BuildWithConfig(BuildConfig config, bool buildPlayer)
        {
            var result = ExecuteBuildWithResult(config, buildPlayer);
            ThrowIfNotSuccess(result);
        }

        /// <summary>
        /// 通过 BuildConfig 执行完整构建流程，返回阶段化结果（窗口与测试调用此核心）。
        /// </summary>
        public static BuildExecutionResult ExecuteBuildWithResult(BuildConfig config, bool buildPlayer)
        {
            return ExecuteBuildWithResult(config, buildPlayer, BuildStageImpl.CreateDefault());
        }

        /// <summary>
        /// 核心编排：Preflight → Dll（编译或复用校验）→ AssetBundle → MinimalPackage → Player → Completed。
        /// <remarks>任一阶段失败/取消立即停止下游；异常详情保留在结果中。</remarks>
        /// </summary>
        public static BuildExecutionResult ExecuteBuildWithResult(BuildConfig config, bool buildPlayer, BuildStageImpl impl)
        {
            if (impl == null)
                impl = BuildStageImpl.CreateDefault();

            var result = new BuildExecutionResult();
            try
            {
                if (config == null)
                    return Fail(result, "BuildConfig 为空");

                bool playerRequested = buildPlayer || config.BuildPlayer;

                // 1. Preflight：所有无副作用检查（含平台一致性、参数有效性），失败不产生任何构建副作用
                result.Stage = BuildStage.Preflight;
                List<string> errors = BuildPreflight.Validate(config, playerRequested, impl.GetActiveTarget(),
                    impl.IsHybridClrAvailable(), GetEnabledScenePaths);
                if (errors.Count > 0)
                    return Fail(result, string.Join("\n", errors));

                result.Target = config.BuildTarget;
                result.PackageVersion = config.PackageVersion;

                // 2. Dll：编译复制；BuildHotFixDll=false 表示复用（校验既有产物一致），不能被旧产物掩盖
                result.Stage = BuildStage.Dll;
                if (config.BuildHotFixDll)
                {
                    Debug.Log("[ReleaseTools] 编译热更DLL...");
                    impl.CompileHotFixDll(config);
                }
                else if (impl.IsHybridClrAvailable())
                {
                    Debug.Log("[ReleaseTools] 复用已有热更DLL（校验产物一致性）...");
                    impl.ValidateReusedDll(config);
                }

                // 3. AssetBundle：Success=false 或异常均停止，不进入最小包/Player
                result.Stage = BuildStage.AssetBundle;
                var ab = impl.BuildAssetBundle(config);
                if (ab == null || !ab.Success)
                    return Fail(result,
                        ab == null ? "AssetBundle 构建返回空结果" : $"AssetBundle 构建失败: {ab.Error}",
                        ab?.Exception);

                result.OutputPackageDirectory = ab.OutputPackageDirectory;
                Debug.Log($"[ReleaseTools] AssetBundle 构建成功: {ab.OutputPackageDirectory}");

                // 4. MinimalPackage：先校验后删除，仅处理本次 DefaultPackage 内置目录；失败不进入 Player
                if (config.MinimalPackage)
                {
                    result.Stage = BuildStage.MinimalPackage;
                    impl.ProcessMinimalPackage(config, result);
                }

                // 5. Player：以 report.summary.result 判定，null 报告/Unknown 视为失败
                if (playerRequested)
                {
                    result.Stage = BuildStage.Player;
                    var player = impl.BuildPlayer(config.BuildTarget,
                        BuildConfig.GetBuildTargetGroup(config.BuildTarget), config.PlayerOutputPath);

                    if (player == null)
                        return Fail(result, "Player 构建返回空结果");

                    result.PlayerOutputPath = player.OutputPath;
                    if (player.Status == BuildStatus.Cancelled)
                    {
                        result.Status = BuildStatus.Cancelled;
                        result.Error = string.IsNullOrEmpty(player.Error) ? "Player 构建被取消" : player.Error;
                        return result;
                    }

                    if (player.Status != BuildStatus.Succeeded)
                        return Fail(result, string.IsNullOrEmpty(player.Error) ? "Player 构建失败" : player.Error, player.Exception);
                }

                // 6. Completed：所有请求阶段完成后才报告成功
                result.Stage = BuildStage.Completed;
                result.Status = BuildStatus.Succeeded;
                return result;
            }
            catch (Exception e)
            {
                return Fail(result, e.Message, e);
            }
        }

        /// <summary>
        /// “仅 Player”独立模式（兼容包装，失败抛出构建异常）。
        /// <remarks>只承诺 Player 编译结果（可用于首次生成裁剪 AOT），不强制 DLL/AB 预检，不冒称完整发布包成功。</remarks>
        /// </summary>
        public static void BuildPlayerOnly(BuildConfig config)
        {
            var result = ExecutePlayerOnlyWithResult(config);
            ThrowIfNotSuccess(result);
        }

        /// <summary>
        /// “仅 Player”独立模式，返回阶段化结果（窗口调用此核心）。
        /// </summary>
        public static BuildExecutionResult ExecutePlayerOnlyWithResult(BuildConfig config)
        {
            return ExecutePlayerOnlyWithResult(config, BuildStageImpl.CreateDefault());
        }

        public static BuildExecutionResult ExecutePlayerOnlyWithResult(BuildConfig config, BuildStageImpl impl)
        {
            if (impl == null)
                impl = BuildStageImpl.CreateDefault();

            if (config == null)
            {
                var emptyResult = new BuildExecutionResult { Stage = BuildStage.Preflight };
                return Fail(emptyResult, "BuildConfig 为空");
            }

            return ExecutePlayerOnlyCore(config.PlayerPlatform, BuildConfig.GetBuildTargetGroup(config.PlayerPlatform),
                config.PlayerOutputPath, impl);
        }

        private static BuildExecutionResult ExecutePlayerOnlyCore(BuildTarget playerTarget, BuildTargetGroup targetGroup,
            string outputPath, BuildStageImpl impl)
        {
            var result = new BuildExecutionResult();
            try
            {
                // Preflight：目标组一致性、平台一致性、输出路径、启用场景
                result.Stage = BuildStage.Preflight;
                List<string> errors = BuildPreflight.ValidatePlayerOnly(playerTarget, outputPath,
                    impl.GetActiveTarget(), targetGroup, GetEnabledScenePaths);
                if (errors.Count > 0)
                    return Fail(result, string.Join("\n", errors));

                result.Target = playerTarget;
                result.Stage = BuildStage.Player;
                var player = impl.BuildPlayer(playerTarget, targetGroup, outputPath);
                if (player == null)
                    return Fail(result, "Player 构建返回空结果");

                result.PlayerOutputPath = player.OutputPath;
                if (player.Status == BuildStatus.Cancelled)
                {
                    result.Status = BuildStatus.Cancelled;
                    result.Error = string.IsNullOrEmpty(player.Error) ? "Player 构建被取消" : player.Error;
                    return result;
                }

                if (player.Status != BuildStatus.Succeeded)
                    return Fail(result, string.IsNullOrEmpty(player.Error) ? "Player 构建失败" : player.Error, player.Exception);

                result.Stage = BuildStage.Completed;
                result.Status = BuildStatus.Succeeded;
                return result;
            }
            catch (Exception e)
            {
                return Fail(result, e.Message, e);
            }
        }

        private static void ThrowIfNotSuccess(BuildExecutionResult result)
        {
            if (result == null)
                throw new BuildExecutionException(BuildStage.Preflight, "构建结果为空");
            if (result.Succeeded)
                return;

            throw new BuildExecutionException(result.Stage, result.Describe(), result.Exception);
        }

        private static BuildExecutionResult Fail(BuildExecutionResult result, string error, Exception exception = null)
        {
            result.Status = BuildStatus.Failed;
            result.Error = error;
            result.Exception = exception;
            Debug.LogError($"[ReleaseTools] {result.Describe()}");
            return result;
        }

        #endregion

        #region AssetBundle 构建

        /// <summary>
        /// AssetBundle 阶段默认实现：以 config 显式参数构造 YooAsset 构建参数，成功后核验本次输出必需文件。
        /// </summary>
        internal static AbStageOutcome BuildAssetBundleStage(BuildConfig config)
        {
            try
            {
                Debug.Log($"[ReleaseTools] 开始构建 : {config.BuildTarget}");

                string outputRoot = ResolveOutputRoot(config);
                BuildParameters buildParameters = CreateBuildParameters(config, outputRoot);
                IBuildPipeline pipeline = config.BuildPipeline == EBuildPipeline.BuiltinBuildPipeline
                    ? new BuiltinBuildPipeline()
                    : (IBuildPipeline)new ScriptableBuildPipeline();

                var buildResult = pipeline.Run(buildParameters, true);
                if (buildResult == null)
                    return AbStageOutcome.Fail("YooAsset 构建返回 null 结果");

                if (!buildResult.Success)
                    return AbStageOutcome.Fail(string.IsNullOrEmpty(buildResult.ErrorInfo)
                        ? "YooAsset 构建失败（无错误详情）"
                        : buildResult.ErrorInfo);

                // 核验本次输出的必需文件（清单/版本/报告），旧产物不能掩盖缺失
                List<string> missing = VerifyAssetBundleOutputs(buildResult.OutputPackageDirectory, config.PackageVersion);
                if (missing.Count > 0)
                    return AbStageOutcome.Fail($"AssetBundle 输出缺失必需文件:\n{string.Join("\n", missing)}");

                Debug.Log($"[ReleaseTools] 构建成功 : {buildResult.OutputPackageDirectory}");
                return AbStageOutcome.Succeed(buildResult.OutputPackageDirectory);
            }
            catch (Exception e)
            {
                return AbStageOutcome.Fail($"AssetBundle 构建异常: {e.Message}", e);
            }
        }

        /// <summary>
        /// 解析输出根目录（相对路径相对于工程根）。
        /// </summary>
        public static string ResolveOutputRoot(BuildConfig config)
        {
            string outputRoot = config.OutputRoot;
            if (!Path.IsPathRooted(outputRoot))
                outputRoot = Path.Combine(Application.dataPath + "/../", outputRoot);
            return Path.GetFullPath(outputRoot).Replace('\\', '/');
        }

        /// <summary>
        /// 由 config 显式构造 YooAsset 构建参数（统一核心，版本号来自本次输入）。
        /// </summary>
        public static BuildParameters CreateBuildParameters(BuildConfig config, string resolvedOutputRoot)
        {
            BuildParameters buildParameters;

            if (config.BuildPipeline == EBuildPipeline.BuiltinBuildPipeline)
            {
                var builtinBuildParameters = new BuiltinBuildParameters();
                builtinBuildParameters.CompressOption = config.CompressOption;
                buildParameters = builtinBuildParameters;
            }
            else
            {
                var scriptableBuildParameters = new ScriptableBuildParameters();
                scriptableBuildParameters.CompressOption = config.CompressOption;
                scriptableBuildParameters.BuiltinShadersBundleName = GetBuiltinShaderBundleName(PackageName);
                scriptableBuildParameters.ReplaceAssetPathWithAddress = Settings.UpdateSetting.GetReplaceAssetPathWithAddress();
                buildParameters = scriptableBuildParameters;
            }

            buildParameters.BuildOutputRoot = resolvedOutputRoot;
            buildParameters.BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = config.BuildPipeline.ToString();
            buildParameters.BuildTarget = config.BuildTarget;
            buildParameters.BuildBundleType = (int)EBuildBundleType.AssetBundle;
            buildParameters.PackageName = PackageName;
            buildParameters.PackageVersion = config.PackageVersion;
            buildParameters.VerifyBuildingResult = config.VerifyBuildingResult;
            buildParameters.EnableSharePackRule = config.EnableSharePackRule;
            buildParameters.FileNameStyle = config.FileNameStyle;
            buildParameters.BuildinFileCopyOption = config.BuildinFileCopyOption;
            buildParameters.BuildinFileCopyParams = string.Empty;
            buildParameters.EncryptionServices = GetEncryptionFromType(config.EncryptionType);
            buildParameters.ClearBuildCacheFiles = config.ClearBuildCache;
            buildParameters.UseAssetDependencyDB = config.UseAssetDependencyDB;
            return buildParameters;
        }

        /// <summary>
        /// 核验 AB 输出目录中的必需文件（文件名全部取自已安装 YooAsset 的工具方法）。
        /// <remarks>返回缺失/为空文件描述列表，空列表表示通过。</remarks>
        /// </summary>
        public static List<string> VerifyAssetBundleOutputs(string outputPackageDirectory, string packageVersion)
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(outputPackageDirectory) || !Directory.Exists(outputPackageDirectory))
            {
                missing.Add($"输出目录不存在: {outputPackageDirectory}");
                return missing;
            }

            (string label, string fileName)[] required =
            {
                ("构建报告", YooAssetSettingsData.GetBuildReportFileName(PackageName, packageVersion)),
                ("清单二进制", YooAssetSettingsData.GetManifestBinaryFileName(PackageName, packageVersion)),
                ("清单JSON", YooAssetSettingsData.GetManifestJsonFileName(PackageName, packageVersion)),
                ("清单哈希", YooAssetSettingsData.GetPackageHashFileName(PackageName, packageVersion)),
                ("版本文件", YooAssetSettingsData.GetPackageVersionFileName(PackageName)),
            };

            foreach (var item in required)
            {
                string path = $"{outputPackageDirectory}/{item.fileName}";
                if (!File.Exists(path))
                    missing.Add($"{item.label} 不存在: {path}");
                else if (new FileInfo(path).Length == 0)
                    missing.Add($"{item.label} 为空: {path}");
            }

            return missing;
        }

        #endregion

        #region 最小包后处理

        /// <summary>
        /// 读取文件的文本数据。
        /// </summary>
        public static string ReadAllText(string filePath)
        {
            if (File.Exists(filePath) == false)
            {
                return null;
            }
            return File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// 最小包模式（兼容入口）：先完整校验报告与目录，再仅删除本次 DefaultPackage 内置目录中不带保留 Tag 的 .bundle。
        /// <remarks>校验/删除失败抛出构建异常并停止下游；未找到报告同样失败，不再“跳过最小包处理”。</remarks>
        /// </summary>
        public static void ProcessMinimalPackage(string packageVersion, string retainTags, string outputPackageDirectory)
        {
            ProcessMinimalPackage(packageVersion, retainTags, outputPackageDirectory, BuildTarget.NoTarget);
        }

        /// <summary>
        /// 最小包模式（完整入口，显式校验目标）。
        /// </summary>
        public static void ProcessMinimalPackage(string packageVersion, string retainTags, string outputPackageDirectory,
            BuildTarget expectedTarget)
        {
            string streamingRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();
            string builtinPackageDirectory = $"{streamingRoot}/{PackageName}";

            if (string.IsNullOrEmpty(packageVersion))
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 资源版本号为空");

            if (string.IsNullOrEmpty(outputPackageDirectory) || !Directory.Exists(outputPackageDirectory))
                throw new BuildExecutionException(BuildStage.MinimalPackage, $"[最小包] 构建输出目录不存在: {outputPackageDirectory}");

            string reportFileName = YooAssetSettingsData.GetBuildReportFileName(PackageName, packageVersion);
            string reportPath = $"{outputPackageDirectory}/{reportFileName}";
            string reportJson = ReadAllText(reportPath);
            if (reportJson == null)
                throw new BuildExecutionException(BuildStage.MinimalPackage, $"[最小包] 未找到构建报告: {reportPath}，拒绝删除任何文件");

            MinimalPackagePlan plan;
            try
            {
                plan = MinimalPackageProcessor.CreatePlan(reportJson, PackageName, packageVersion, expectedTarget,
                    retainTags, builtinPackageDirectory);
            }
            catch (Exception e)
            {
                throw new BuildExecutionException(BuildStage.MinimalPackage, $"[最小包] 校验失败，未删除任何文件: {e.Message}", e);
            }

            Debug.Log($"[最小包] 保留Tag: [{retainTags}] | .bundle 共 {plan.TotalBundleFiles} 个 | 计划删除 {plan.DeleteFiles.Count} 个，保留 {plan.RetainFiles.Count} 个 | 目录: {plan.BuiltinPackageDirectory}");

            try
            {
                MinimalPackageProcessor.ExecutePlan(plan);
            }
            catch (Exception e)
            {
                throw new BuildExecutionException(BuildStage.MinimalPackage,
                    $"[最小包] 删除/后处理失败，已停止后续流程（输出需重新构建后才可使用）: {e.Message}", e);
            }

            Debug.Log("[最小包] 处理完成");
        }

        /// <summary>
        /// 最小包阶段默认实现（由编排核心调用）。
        /// </summary>
        internal static void ProcessMinimalPackageStage(BuildConfig config, BuildExecutionResult result)
        {
            ProcessMinimalPackage(result.PackageVersion, config.RetainTags, result.OutputPackageDirectory, result.Target);
        }

        #endregion

        #region Player 构建

        /// <summary>
        /// Player 阶段默认实现：显式目标/目标组/输出路径，不切换平台；以报告结果判定成功/失败/取消。
        /// </summary>
        internal static PlayerStageOutcome BuildPlayerStage(BuildTarget buildTarget, BuildTargetGroup buildTargetGroup,
            string locationPathName)
        {
            try
            {
                string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
                if (scenes.Length == 0)
                    return PlayerStageOutcome.Fail("EditorBuildSettings 中没有启用的场景");

                BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = locationPathName,
                    targetGroup = buildTargetGroup,
                    target = buildTarget,
                    options = BuildOptions.None
                };

                var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
                if (report == null)
                    return PlayerStageOutcome.Fail("BuildPlayer 返回 null 报告");

                BuildSummary summary = report.summary;
                switch (summary.result)
                {
                    case BuildResult.Succeeded:
                        Debug.Log($"[ReleaseTools] Build success: {summary.totalSize / 1024 / 1024} MB, {summary.outputPath}");
                        return PlayerStageOutcome.Succeed(summary.outputPath);

                    case BuildResult.Cancelled:
                        return PlayerStageOutcome.Cancelled("Player 构建被取消");

                    case BuildResult.Failed:
                        return PlayerStageOutcome.Fail("Player 构建失败 (BuildResult.Failed)");

                    default:
                        return PlayerStageOutcome.Fail($"Player 构建结果未知: {summary.result}");
                }
            }
            catch (Exception e)
            {
                return PlayerStageOutcome.Fail($"Player 构建异常: {e.Message}", e);
            }
        }

        /// <summary>
        /// 旧版 Player 构建入口（兼容包装）。
        /// <remarks>不再自动切换平台：激活平台与请求目标不一致时直接失败；失败抛出构建异常。</remarks>
        /// </summary>
        public static void BuildImp(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget, string locationPathName)
        {
            var result = ExecutePlayerOnlyCore(buildTarget, buildTargetGroup, locationPathName, BuildStageImpl.CreateDefault());
            ThrowIfNotSuccess(result);
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 平台名转构建目标；未知平台返回 false（调用方必须失败，不得回落 NoTarget）。
        /// </summary>
        public static bool TryGetBuildTarget(string platform, out BuildTarget target)
        {
            switch (platform)
            {
                case "Android":
                    target = BuildTarget.Android;
                    return true;
                case "IOS":
                    target = BuildTarget.iOS;
                    return true;
                case "Windows":
                    target = BuildTarget.StandaloneWindows64;
                    return true;
                case "MacOS":
                    target = BuildTarget.StandaloneOSX;
                    return true;
                case "Linux":
                    target = BuildTarget.StandaloneLinux64;
                    return true;
                case "WebGL":
                    target = BuildTarget.WebGL;
                    return true;
                case "Switch":
                    target = BuildTarget.Switch;
                    return true;
                case "PS4":
                    target = BuildTarget.PS4;
                    return true;
                case "PS5":
                    target = BuildTarget.PS5;
                    return true;
                default:
                    target = BuildTarget.NoTarget;
                    return false;
            }
        }

        private static List<string> GetEnabledScenePaths()
        {
            return EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToList();
        }

        private static string GetBuiltinShaderBundleName(string packageName)
        {
            var uniqueBundleName = AssetBundleCollectorSettingData.Setting.UniqueBundleName;
            var packRuleResult = DefaultPackRule.CreateShadersPackRuleResult();
            return packRuleResult.GetBundleName(packageName, uniqueBundleName);
        }

        /// <summary>
        /// 根据 EncryptionType 枚举获取加密服务。
        /// </summary>
        private static IEncryptionServices GetEncryptionFromType(EncryptionType encryptionType)
        {
            return encryptionType switch
            {
                EncryptionType.FileOffSet => new FileOffsetEncryption(),
                EncryptionType.FileStream => new FileStreamEncryption(),
                _ => null
            };
        }

        /// <summary>
        /// 从 ResourceModuleDriver 读取加密类型（旧 CLI 路径显式映射用）。
        /// </summary>
        private static EncryptionType GetEncryptionTypeFromResourceModuleDriver()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab GameEntry");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[ReleaseTools] Failed to find GameEntry.prefab");
                return EncryptionType.None;
            }

            var gameEntryPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var gameEntryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(gameEntryPath);
            if (gameEntryPrefab == null)
            {
                Debug.LogWarning("[ReleaseTools] Failed to load GameEntry.prefab");
                return EncryptionType.None;
            }

            var resourceModuleDriver = gameEntryPrefab.GetComponentInChildren<ResourceModuleDriver>();
            if (resourceModuleDriver == null)
            {
                Debug.LogWarning("[ReleaseTools] ResourceModuleDriver not found in GameEntry.prefab");
                return EncryptionType.None;
            }

            var encryptionType = resourceModuleDriver.EncryptionType;
            Debug.Log($"[ReleaseTools] Use EncryptionType from ResourceModuleDriver: {encryptionType}");
            return encryptionType;
        }

        #endregion
    }
}
