using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace TEngine
{
    /// <summary>
    /// 构建前置校验：只做无副作用检查，任何错误都应在任何编译/复制/AB/删除操作之前失败。
    /// </summary>
    public static class BuildPreflight
    {
        /// <summary>
        /// 完整构建（AB 可选 DLL 可选最小包可选 Player）前置校验。
        /// </summary>
        /// <param name="config">构建配置。</param>
        /// <param name="playerRequested">本次是否请求构建 Player（buildPlayer 参数或 config.BuildPlayer）。</param>
        /// <param name="activeTarget">编辑器当前激活平台（由调用方提供，不自动读取）。</param>
        /// <param name="hybridClrAvailable">编辑器程序集是否编译进 HybridCLR 支持。</param>
        /// <param name="enabledScenesProvider">启用场景路径提供者（null 时读取 EditorBuildSettings，测试可注入）。</param>
        /// <returns>错误列表，空列表表示通过。</returns>
        public static List<string> Validate(BuildConfig config, bool playerRequested, BuildTarget activeTarget,
            bool hybridClrAvailable, Func<IEnumerable<string>> enabledScenesProvider = null)
        {
            var errors = new List<string>();
            if (config == null)
            {
                errors.Add("BuildConfig 为空");
                return errors;
            }

            if (config.BuildTarget == BuildTarget.NoTarget)
                errors.Add("构建目标平台无效 (NoTarget)，拒绝回落默认平台");

            if (string.IsNullOrWhiteSpace(config.PackageVersion))
                errors.Add("资源版本号 (PackageVersion) 为空，拒绝回落默认版本");
            else if (ContainsInvalidFileNameChars(config.PackageVersion))
                errors.Add($"资源版本号包含非法文件名字符: {config.PackageVersion}");

            if (string.IsNullOrWhiteSpace(config.OutputRoot))
                errors.Add("AB 输出目录 (OutputRoot) 为空");

            if (activeTarget != config.BuildTarget)
                errors.Add($"编辑器当前平台 ({activeTarget}) 与构建目标 ({config.BuildTarget}) 不一致，本工具不会自动切换平台；请先切换平台，或在 CLI 下通过 Unity 启动参数 -buildTarget 指定目标");

            if (config.MinimalPackage && config.BuildinFileCopyOption == EBuildinFileCopyOption.None)
                errors.Add("最小包模式要求本次构建生成新的内置文件，当前内置文件拷贝选项为 None（不会写入 StreamingAssets），将只能处理旧内置包；请改用 ClearAndCopyAll 等选项");

            if (config.BuildHotFixDll && !hybridClrAvailable)
                errors.Add("已请求编译热更DLL，但 HybridCLR 未启用（编辑器程序集缺少 ENABLE_HYBRIDCLR 宏），不能空操作成功；请先执行菜单 HybridCLR/Define Symbols/Enable HybridCLR");

            errors.AddRange(ValidateTwoStageUpdate(config, hybridClrAvailable));

            if (playerRequested)
                errors.AddRange(ValidatePlayerRequest(config.BuildTarget, config.PlayerPlatform, config.PlayerOutputPath, enabledScenesProvider));

            return errors;
        }

        /// <summary>
        /// “仅 Player”独立模式前置校验（不强制 DLL/AB 预检，只承诺 Player 编译结果）。
        /// </summary>
        /// <param name="playerTarget">Player 目标平台。</param>
        /// <param name="playerOutputPath">Player 输出路径。</param>
        /// <param name="activeTarget">编辑器当前激活平台。</param>
        /// <param name="requestedGroup">调用方显式请求的目标组（可空，用于目标组一致性检查）。</param>
        /// <param name="enabledScenesProvider">启用场景路径提供者（null 时读取 EditorBuildSettings，测试可注入）。</param>
        public static List<string> ValidatePlayerOnly(BuildTarget playerTarget, string playerOutputPath,
            BuildTarget activeTarget, BuildTargetGroup? requestedGroup = null, Func<IEnumerable<string>> enabledScenesProvider = null)
        {
            var errors = new List<string>();

            if (playerTarget == BuildTarget.NoTarget)
                errors.Add("Player 目标平台无效 (NoTarget)");

            if (activeTarget != playerTarget)
                errors.Add($"编辑器当前平台 ({activeTarget}) 与 Player 目标 ({playerTarget}) 不一致，本工具不会自动切换平台；请先切换平台，或在 CLI 下通过 Unity 启动参数 -buildTarget 指定目标");

            if (string.IsNullOrWhiteSpace(playerOutputPath))
                errors.Add("Player 输出路径为空");
            else if (playerOutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                errors.Add($"Player 输出路径包含非法字符: {playerOutputPath}");

            if (requestedGroup.HasValue && BuildConfig.GetBuildTargetGroup(playerTarget) != requestedGroup.Value)
                errors.Add($"请求的目标组 ({requestedGroup.Value}) 与 Player 目标 ({playerTarget}) 不匹配");

            errors.AddRange(ValidateScenes(enabledScenesProvider));
            return errors;
        }

        private static List<string> ValidatePlayerRequest(BuildTarget buildTarget, BuildTarget playerPlatform,
            string playerOutputPath, Func<IEnumerable<string>> enabledScenesProvider)
        {
            var errors = new List<string>();

            if (playerPlatform == BuildTarget.NoTarget)
                errors.Add("Player 平台无效 (NoTarget)");
            else if (playerPlatform != buildTarget)
                errors.Add($"PlayerPlatform ({playerPlatform}) 与构建目标 ({buildTarget}) 不一致，完整构建要求二者相等");

            if (string.IsNullOrWhiteSpace(playerOutputPath))
                errors.Add("Player 输出路径 (PlayerOutputPath) 为空");
            else if (playerOutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                errors.Add($"Player 输出路径包含非法字符: {playerOutputPath}");

            errors.AddRange(ValidateScenes(enabledScenesProvider));
            return errors;
        }

        private static List<string> ValidateScenes(Func<IEnumerable<string>> enabledScenesProvider)
        {
            var errors = new List<string>();
            if (enabledScenesProvider == null)
                return errors;

            List<string> scenes = null;
            try
            {
                scenes = new List<string>(enabledScenesProvider());
            }
            catch (Exception e)
            {
                errors.Add($"读取启用场景列表失败: {e.Message}");
                return errors;
            }

            if (scenes.Count == 0)
                errors.Add("EditorBuildSettings 中没有启用的场景，无法构建 Player");
            return errors;
        }

        private static bool ContainsInvalidFileNameChars(string value)
        {
            return value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
        }

        private static IEnumerable<string> ValidateTwoStageUpdate(BuildConfig config, bool hybridClrAvailable)
        {
            var errors = new List<string>();
            UpdateSetting setting = Settings.UpdateSetting;
            if (setting == null || !setting.EnableTwoStageUpdate)
                return errors;

            if (!hybridClrAvailable)
                errors.Add("两阶段更新已开启，但 HybridCLR 宏未启用");
            if (!IsSupportedTwoStageTarget(config.BuildTarget))
                errors.Add($"两阶段更新首版不支持构建目标: {config.BuildTarget}");
            if (!IsSafePathSegment(config.ReleaseId))
                errors.Add("两阶段 ReleaseId 必须是安全的单一目录段");
            if (setting.UpdateStyle != UpdateStyle.Force)
                errors.Add("两阶段更新首版要求 UpdateStyle.Force");
            if (setting.TwoStageContractVersion <= 0)
                errors.Add("TwoStageContractVersion 必须大于 0");
            if (!IsSafePathSegment(setting.BasePlayerId))
                errors.Add("两阶段 BasePlayerId 必须是安全的单一目录段");
            if (!IsSafePathSegment(setting.Channel))
                errors.Add("两阶段 Channel 必须是安全的单一目录段");
            if (string.IsNullOrWhiteSpace(setting.BootstrapAssemblyName) ||
                setting.HotUpdateAssemblies == null ||
                !setting.HotUpdateAssemblies.Contains(setting.BootstrapAssemblyName))
                errors.Add("BootstrapAssemblyName 必须且只能来自 HotUpdateAssemblies");
            if (!string.Equals(setting.BootstrapAssemblyName, "GameUpdater.dll", StringComparison.Ordinal))
                errors.Add("两阶段首版 BootstrapAssemblyName 固定为 GameUpdater.dll");
            if (string.IsNullOrWhiteSpace(setting.LogicMainDllName) ||
                setting.HotUpdateAssemblies == null ||
                !setting.HotUpdateAssemblies.Contains(setting.LogicMainDllName) ||
                string.Equals(setting.LogicMainDllName, setting.BootstrapAssemblyName, StringComparison.Ordinal))
                errors.Add("LogicMainDllName 必须位于业务热更程序集名单且不能是 Bootstrap updater");
            if (string.IsNullOrWhiteSpace(setting.BootstrapTextAssetPath) ||
                string.Equals(setting.BootstrapTextAssetPath, setting.AssemblyTextAssetPath, StringComparison.OrdinalIgnoreCase))
                errors.Add("BootstrapTextAssetPath 必须非空且与业务 AssemblyTextAssetPath 分离");
            if (string.IsNullOrWhiteSpace(setting.BootstrapTag))
                errors.Add("BootstrapTag 为空");

            ValidateUniqueNames(errors, setting.HotUpdateAssemblies, "HotUpdateAssemblies");
            ValidateUniqueNames(errors, setting.AOTMetaAssemblies, "AOTMetaAssemblies");
            if (setting.AOTMetaAssemblies == null || setting.AOTMetaAssemblies.Count == 0)
                errors.Add("两阶段 Bootstrap 必须包含完整的 AOT metadata 名单");
            ValidateTrustedUrl(errors, setting.TwoStageReleaseDescriptorUrl, setting.AllowInsecureLoopbackHttp, "descriptor");
            ValidateTrustedUrl(errors, setting.TwoStageHostServerUrl, setting.AllowInsecureLoopbackHttp, "primary host");
            ValidateTrustedUrl(errors, setting.TwoStageFallbackHostServerUrl, setting.AllowInsecureLoopbackHttp, "fallback host");
            ValidateResourceDriver(errors);
            try
            {
                TwoStageReleaseBuilder.ValidateReleaseIdAvailable(config);
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }
            return errors;
        }

        private static void ValidateResourceDriver(List<string> errors)
        {
            const string gameEntryPath = "Assets/TEngine/Settings/Prefab/GameEntry.prefab";
            GameObject gameEntry = AssetDatabase.LoadAssetAtPath<GameObject>(gameEntryPath);
            ResourceModuleDriver driver = gameEntry != null
                ? gameEntry.GetComponentInChildren<ResourceModuleDriver>(true)
                : null;
            if (driver == null)
            {
                errors.Add($"两阶段构建找不到 ResourceModuleDriver: {gameEntryPath}");
                return;
            }

            SerializedProperty playMode = new SerializedObject(driver).FindProperty("playMode");
            if (playMode == null || playMode.enumValueIndex != (int)EPlayMode.HostPlayMode)
                errors.Add("两阶段 Player 要求 GameEntry.prefab 的 ResourceModuleDriver.playMode=HostPlayMode");
            if (driver.UpdatableWhilePlaying)
                errors.Add("两阶段首版不支持 ResourceModuleDriver.updatableWhilePlaying");
        }

        private static bool IsSupportedTwoStageTarget(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64:
                case BuildTarget.Android:
                case BuildTarget.iOS:
                case BuildTarget.PS5:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsSafePathSegment(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !ContainsInvalidFileNameChars(value) &&
                   value.IndexOf('/') < 0 &&
                   value.IndexOf('\\') < 0 &&
                   !value.Contains("..") &&
                   !Path.IsPathRooted(value);
        }

        private static void ValidateUniqueNames(List<string> errors, IEnumerable<string> names, string field)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (names == null)
            {
                errors.Add($"{field} 为空");
                return;
            }

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                    errors.Add($"{field} 包含空值或重复项: '{name}'");
            }
        }

        private static void ValidateTrustedUrl(List<string> errors, string value, bool allowLoopbackHttp, string label)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
            {
                errors.Add($"两阶段 {label} URL 无效: '{value}'");
                return;
            }

            bool secure = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool allowedLoopback = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                                   uri.IsLoopback && allowLoopbackHttp;
            if (!secure && !allowedLoopback)
                errors.Add($"两阶段 {label} URL 必须使用 HTTPS；仅显式允许的 loopback HTTP 可用于开发: '{value}'");
        }
    }
}
