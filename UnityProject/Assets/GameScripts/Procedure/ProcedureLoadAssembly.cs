using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
#if ENABLE_HYBRIDCLR
using HybridCLR;
#endif
using Launcher;
using TEngine;
using UnityEngine;
using YooAsset;

namespace Procedure
{
    /// <summary>
    /// 流程加载器 - 代码初始化。
    /// <remarks>
    /// 只有必需 DLL、metadata、主程序集入口校验和 Entrance 同步调用全部成功，才提交
    /// ProcedureStartGame。程序集加载和 metadata 注入不可回滚；热更失败只允许重启或退出。
    /// </remarks>
    /// </summary>
    public class ProcedureLoadAssembly : ProcedureBase
    {
        private const float DefaultStartupTimeoutSeconds = 60f;

        private readonly bool _enableAddressable = true;

        public override bool UseNativeDialog => true;

        private IFsm<IProcedureModule> _procedureOwner;
        private UpdateSetting _setting;
        private StartupAttempt _currentAttempt;
        private int _nextAttemptId;
        private bool _entranceInvoked;
        private bool _startGameSubmitted;
        private Assembly _mainLogicAssembly;
        private List<Assembly> _hotfixAssemblyList;
        private List<string> _hotUpdateAssemblyNames;
        private List<string> _aotMetadataAssemblyNames;

        /// <summary>
        /// 热更加载阶段超时（非缩放时间）。保留可注入属性以便生命周期测试缩短等待。
        /// </summary>
        public float StartupTimeoutSeconds { get; set; } = DefaultStartupTimeoutSeconds;

        protected override void OnInit(IFsm<IProcedureModule> procedureOwner)
        {
            base.OnInit(procedureOwner);
            _setting = Settings.UpdateSetting;
        }

        protected override void OnEnter(IFsm<IProcedureModule> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            _procedureOwner = procedureOwner;
            StartAttempt();
        }

        protected override void OnLeave(IFsm<IProcedureModule> procedureOwner, bool isShutdown)
        {
            try
            {
                base.OnLeave(procedureOwner, isShutdown);
            }
            finally
            {
                StartupAttempt attempt = _currentAttempt;
                _currentAttempt = null;
                attempt?.Invalidate();
                attempt?.Dispose();
            }
        }

        private void StartAttempt()
        {
            StartupAttempt previousAttempt = _currentAttempt;
            _currentAttempt = null;
            previousAttempt?.Invalidate();
            previousAttempt?.Dispose();

            StartupAttempt attempt = new StartupAttempt(++_nextAttemptId);
            _currentAttempt = attempt;
            LoadAssembly(attempt).Forget();
        }

        private async UniTaskVoid LoadAssembly(StartupAttempt attempt)
        {
            using (CancellationTokenSource timeoutCancellation =
                   CancellationTokenSource.CreateLinkedTokenSource(attempt.Token))
            {
                try
                {
                    UniTask loadTask = LoadAssemblyCore(attempt, timeoutCancellation.Token);
                    await loadTask.Timeout(
                        TimeSpan.FromSeconds(Math.Max(0.01f, StartupTimeoutSeconds)),
                        DelayType.UnscaledDeltaTime,
                        PlayerLoopTiming.Update,
                        timeoutCancellation);
                }
                catch (TimeoutException exception)
                {
                    FailAttempt(attempt, "程序集/metadata 阶段超时", exception);
                }
                catch (OperationCanceledException)
                {
                    // 流程离开或旧 attempt 失效属于正常取消，不产生新的失败提示。
                    if (IsCurrentRunning(attempt))
                    {
                        attempt.TryCancel();
                    }
                }
                catch (Exception exception)
                {
                    FailAttempt(attempt, "程序集/metadata/入口阶段", exception);
                }
            }
        }

        private async UniTask LoadAssemblyCore(StartupAttempt attempt, CancellationToken cancellationToken)
        {
            ValidateAssemblyConfiguration();
            _hotfixAssemblyList = new List<Assembly>();
            _mainLogicAssembly = null;

            bool useAlreadyLoadedAssemblies = !_setting.Enable ||
                                              _resourceModule.PlayMode == EPlayMode.EditorSimulateMode;
            if (useAlreadyLoadedAssemblies)
            {
                CollectAlreadyLoadedAssemblies(attempt);
            }
            else
            {
                if (_resourceModule.PlayMode != EPlayMode.OfflinePlayMode &&
                    _resourceModule.PlayMode != EPlayMode.HostPlayMode &&
                    _resourceModule.PlayMode != EPlayMode.WebPlayMode)
                {
                    throw new InvalidOperationException($"Unsupported resource play mode: {_resourceModule.PlayMode}.");
                }

                if (!_setting.EnableTwoStageUpdate)
                {
                    await LoadMetadataForAOTAssemblies(attempt, cancellationToken);
                    EnsureCurrentRunning(attempt);
                }
                else if (!TwoStageUpdateCoordinator.ResourcesReady)
                {
                    throw new InvalidOperationException(
                        "Business assemblies cannot load before the two-stage updater confirms and the host verifies resources ready.");
                }

                foreach (string hotUpdateAssemblyName in _hotUpdateAssemblyNames)
                {
                    await LoadHotUpdateAssembly(hotUpdateAssemblyName, attempt, cancellationToken);
                    EnsureCurrentRunning(attempt);
                }
            }

            EnsureMainAssemblyIsReady();
            if (_setting.EnableTwoStageUpdate)
            {
                TwoStageUpdateCoordinator.VerifyReadyForEntry(_resourceModule);
            }

            SubmitEntrance(attempt);
        }

        private void ValidateAssemblyConfiguration()
        {
            if (_setting == null)
            {
                throw new InvalidOperationException("UpdateSetting is missing.");
            }

            if (string.IsNullOrWhiteSpace(_setting.LogicMainDllName))
            {
                throw new InvalidOperationException("LogicMainDllName is empty.");
            }

            List<string> configuredHotUpdateNames = DeduplicateAssemblyNames(
                _setting.HotUpdateAssemblies,
                "HotUpdateAssemblies");
            _hotUpdateAssemblyNames = new List<string>();
            foreach (string configuredName in configuredHotUpdateNames)
            {
                if (!string.IsNullOrWhiteSpace(_setting.BootstrapAssemblyName) &&
                    string.Equals(configuredName, _setting.BootstrapAssemblyName, StringComparison.Ordinal))
                {
                    continue;
                }

                _hotUpdateAssemblyNames.Add(configuredName);
            }

            if (_setting.EnableTwoStageUpdate)
            {
                if (!TwoStageUpdateCoordinator.IsPrepared)
                {
                    throw new InvalidOperationException("Two-stage update session is not prepared.");
                }

                List<string> releaseBusinessNames = TwoStageUpdateCoordinator.GetBusinessAssemblyNames();
                if (!HaveSameOrderedNames(_hotUpdateAssemblyNames, releaseBusinessNames))
                {
                    throw new InvalidOperationException(
                        "Business assembly order/list drifted from the fixed release descriptor.");
                }
            }
            _aotMetadataAssemblyNames = DeduplicateAssemblyNames(_setting.AOTMetaAssemblies, "AOTMetaAssemblies");
        }

        private static bool HaveSameOrderedNames(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> DeduplicateAssemblyNames(IEnumerable<string> configuredNames, string fieldName)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (configuredNames == null)
            {
                return result;
            }

            foreach (string configuredName in configuredNames)
            {
                if (string.IsNullOrWhiteSpace(configuredName))
                {
                    throw new InvalidOperationException($"{fieldName} contains an empty assembly name.");
                }

                if (seen.Add(configuredName))
                {
                    result.Add(configuredName);
                }
                else
                {
                    Log.Warning($"{fieldName} contains duplicate entry '{configuredName}'; it was loaded once.");
                }
            }

            return result;
        }

        private void CollectAlreadyLoadedAssemblies(StartupAttempt attempt)
        {
            foreach (string configuredName in _hotUpdateAssemblyNames)
            {
                EnsureCurrentRunning(attempt);
                Assembly assembly = FindLoadedAssembly(configuredName);
                if (assembly == null)
                {
                    throw new InvalidOperationException($"Configured hot-update assembly '{configuredName}' is not loaded.");
                }

                AddAssemblyWithIdentityCheck(configuredName, assembly);
            }

            _mainLogicAssembly = FindLoadedAssembly(_setting.LogicMainDllName);
        }

        private async UniTask LoadHotUpdateAssembly(
            string configuredName,
            StartupAttempt attempt,
            CancellationToken cancellationToken)
        {
            UpdateArtifactDescriptor releaseArtifact = _setting.EnableTwoStageUpdate
                ? TwoStageUpdateCoordinator.GetArtifact(configuredName)
                : null;
            string assetLocation = releaseArtifact?.Address ?? GetAssemblyAssetLocation(configuredName);
            TextAsset textAsset = null;
            try
            {
                Log.Debug($"LoadAsset: [ {assetLocation} ]");
                textAsset = await _resourceModule.LoadAssetAsync<TextAsset>(assetLocation, cancellationToken);
                EnsureCurrentRunning(attempt);
                if (textAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Required hot-update DLL asset '{configuredName}' returned null from '{assetLocation}'.");
                }

                if (releaseArtifact != null)
                {
                    TwoStageUpdateCoordinator.ValidateArtifactBytes(releaseArtifact, textAsset.bytes);
                }

                Assembly assembly;
                try
                {
                    assembly = Assembly.Load(textAsset.bytes);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Assembly.Load failed for configured DLL '{configuredName}' from asset '{assetLocation}'.",
                        exception);
                }

                if (_setting.EnableTwoStageUpdate)
                {
                    TwoStageUpdateCoordinator.RecordLoadedAssembly(configuredName, assembly);
                }

                AddAssemblyWithIdentityCheck(configuredName, assembly);
                Log.Debug($"Assembly [ {assembly.GetName().Name} ] loaded for '{configuredName}'.");
            }
            finally
            {
                if (textAsset != null)
                {
                    _resourceModule.UnloadAsset(textAsset);
                }
            }
        }

        private async UniTask LoadMetadataForAOTAssemblies(
            StartupAttempt attempt,
            CancellationToken cancellationToken)
        {
            // EditorSimulate 不需要补充 AOT metadata；真正的 Player/HybridCLR 才执行这一阶段。
#if UNITY_EDITOR
            return;
#else
            if (_aotMetadataAssemblyNames.Count == 0)
            {
                return;
            }

#if !ENABLE_HYBRIDCLR
            throw new InvalidOperationException(
                "AOT metadata is configured for a Player without ENABLE_HYBRIDCLR.");
#else
            foreach (string configuredName in _aotMetadataAssemblyNames)
            {
                EnsureCurrentRunning(attempt);
                string assetLocation = GetAssemblyAssetLocation(configuredName);
                TextAsset textAsset = null;
                try
                {
                    Log.Debug($"LoadMetadataAsset: [ {assetLocation} ]");
                    textAsset = await _resourceModule.LoadAssetAsync<TextAsset>(assetLocation, cancellationToken);
                    EnsureCurrentRunning(attempt);
                    if (textAsset == null)
                    {
                        throw new InvalidOperationException(
                            $"Required AOT metadata asset '{configuredName}' returned null from '{assetLocation}'.");
                    }

                    HomologousImageMode mode = HomologousImageMode.SuperSet;
                    LoadImageErrorCode result = HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(textAsset.bytes, mode);
                    Log.Info($"LoadMetadataForAOTAssembly: configured='{configuredName}', asset='{textAsset.name}', mode={mode}, ret={result}");
                    if (result != LoadImageErrorCode.OK)
                    {
                        throw new InvalidOperationException(
                            $"LoadMetadataForAOTAssembly failed for '{configuredName}' with result '{result}'.");
                    }
                }
                finally
                {
                    if (textAsset != null)
                    {
                        _resourceModule.UnloadAsset(textAsset);
                    }
                }
            }
#endif
#endif
        }

        private string GetAssemblyAssetLocation(string configuredName)
        {
            if (_enableAddressable)
            {
                return configuredName;
            }

            return Utility.Path.GetRegularPath(
                Path.Combine(
                    "Assets",
                    _setting.AssemblyTextAssetPath,
                    $"{configuredName}{_setting.AssemblyTextAssetExtension}"));
        }

        private Assembly FindLoadedAssembly(string configuredName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(GetAssemblyFileName(assembly), configuredName, StringComparison.Ordinal))
                {
                    return assembly;
                }
            }

            return null;
        }

        private void AddAssemblyWithIdentityCheck(string configuredName, Assembly assembly)
        {
            if (assembly == null)
            {
                throw new InvalidOperationException($"Assembly for configured name '{configuredName}' is null.");
            }

            string actualAssemblyName = GetAssemblyFileName(assembly);
            if (!string.Equals(actualAssemblyName, configuredName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Configured assembly '{configuredName}' resolved to assembly identity '{actualAssemblyName}'.");
            }

            foreach (Assembly loadedAssembly in _hotfixAssemblyList)
            {
                if (string.Equals(GetAssemblyFileName(loadedAssembly), actualAssemblyName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Assembly identity '{actualAssemblyName}' was resolved more than once.");
                }
            }

            _hotfixAssemblyList.Add(assembly);
            if (string.Equals(actualAssemblyName, _setting.LogicMainDllName, StringComparison.Ordinal))
            {
                _mainLogicAssembly = assembly;
            }
        }

        private void EnsureMainAssemblyIsReady()
        {
            if (_mainLogicAssembly == null)
            {
                throw new InvalidOperationException(
                    $"Main logic assembly '{_setting.LogicMainDllName}' is missing after all configured loads completed.");
            }
        }

        private static string GetAssemblyFileName(Assembly assembly)
        {
            return $"{assembly.GetName().Name}.dll";
        }

        private void SubmitEntrance(StartupAttempt attempt)
        {
            EnsureCurrentRunning(attempt);
            if (_startGameSubmitted)
            {
                throw new InvalidOperationException("ProcedureStartGame has already been submitted in this session.");
            }

            Type appType = _mainLogicAssembly.GetType("GameApp", false);
            if (!StartupEntryContract.TryFindEntrance(appType, out MethodInfo entryMethod, out string contractError))
            {
                throw new InvalidOperationException(contractError);
            }

            if (_entranceInvoked)
            {
                throw new InvalidOperationException(
                    "GameApp.Entrance was already invoked; hot-update assemblies cannot be reloaded in this process.");
            }

            // 闸门必须在 Invoke 前设置：Entrance 抛错后也不能通过本进程重试再次注入程序集。
            _entranceInvoked = true;
            try
            {
                object[] entranceArguments = { new object[] { _hotfixAssemblyList } };
                entryMethod.Invoke(null, entranceArguments);
            }
            catch (TargetInvocationException exception)
            {
                Exception innerException = exception.InnerException ?? exception;
                throw new InvalidOperationException("GameApp.Entrance threw an exception.", innerException);
            }

            EnsureCurrentRunning(attempt);
            if (_setting.EnableTwoStageUpdate &&
                !TwoStageUpdateCoordinator.TryWriteEntryCompletedCheckpoint(out string checkpointError))
            {
                // Entrance 已执行成功，本进程绝不能因持久化失败再次调用。允许继续，但明确告警。
                Log.Warning($"EntryCompleted checkpoint was not saved: {checkpointError}");
            }

            _startGameSubmitted = true;
            if (!attempt.TrySucceed())
            {
                _startGameSubmitted = false;
                return;
            }

            // 明确的提交顺序：入口同步正常返回后，才进入 ProcedureStartGame。
            ChangeState<ProcedureStartGame>(_procedureOwner);
        }

        private bool IsCurrentRunning(StartupAttempt attempt)
        {
            return ModuleSystem.IsRunning && ReferenceEquals(_currentAttempt, attempt) && attempt != null && attempt.IsRunning;
        }

        private void EnsureCurrentRunning(StartupAttempt attempt)
        {
            if (!IsCurrentRunning(attempt))
            {
                throw new OperationCanceledException(attempt?.Token ?? default(CancellationToken));
            }
        }

        private void FailAttempt(StartupAttempt attempt, string stage, Exception exception)
        {
            if (!IsCurrentRunning(attempt) || !attempt.TryFail())
            {
                return;
            }

            string mainAssemblyName = _setting?.LogicMainDllName ?? "<unknown>";
            string message = exception?.ToString() ?? "Unknown error.";
            string userMessage = $"热更启动失败！阶段：{stage}；主程序集：{mainAssemblyName}\n\n" +
                                 $"<color=#FF0000>原因：{message}</color>\n\n" +
                                 "程序集加载不可回滚，请重启游戏或退出。";
            Log.Error(userMessage);
            LauncherMgr.ShowUI<LoadUpdateUI>("热更启动失败！");
            LauncherMgr.ShowMessageBox(userMessage, Application.Quit, Application.Quit);
        }
    }
}
