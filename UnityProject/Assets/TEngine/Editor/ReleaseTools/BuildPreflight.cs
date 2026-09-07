using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
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
    }
}
