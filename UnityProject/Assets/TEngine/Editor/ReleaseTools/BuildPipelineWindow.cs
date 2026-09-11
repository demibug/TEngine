using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace TEngine
{
    public class BuildPipelineWindow : EditorWindow
    {
        private static readonly string[] PlatformNames = new string[]
        {
            "Windows 64-bit",
            "macOS",
            "Linux",
            "Android",
            "iOS",
            "WebGL",
        };

        private static readonly BuildTarget[] PlatformTargets = new BuildTarget[]
        {
            BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneOSX,
            BuildTarget.StandaloneLinux64,
            BuildTarget.Android,
            BuildTarget.iOS,
            BuildTarget.WebGL,
        };

        private static readonly string[] PipelineNames = new string[]
        {
            "ScriptableBuildPipeline (SBP)",
            "BuiltinBuildPipeline (内置)",
        };

        private static readonly string[] CompressNames = new string[]
        {
            "Uncompressed (不压缩)",
            "LZMA (高压缩)",
            "LZ4 (快速压缩)",
        };

        private static readonly string[] EncryptionNames = new string[]
        {
            "无加密",
            "文件偏移加密",
            "文件流加密",
        };

        private static readonly string[] CopyOptionNames = new string[]
        {
            "None (不拷贝)",
            "ClearAndCopyAll (清空后拷贝全部)",
            "ClearAndCopyByTags (清空后按Tag拷贝)",
            "OnlyCopyAll (仅拷贝全部)",
            "OnlyCopyByTags (仅按Tag拷贝)",
        };
        
        private static readonly string[] FileNameStyleNames = new string[]
        {
            "HashName (哈希名)",
            "BundleName (资源包名称)",
            "BundleName_HashName (资源包名称 + 哈希值名称)",
        };

        // 配置状态
        private BuildConfig _config;

        // UI 状态
        private Vector2 _scrollPosition;
        private bool _showBasicSettings = true;
        private bool _showMinimalPackageSettings = true;
        private bool _showAdvancedSettings;
        private bool _showDllSettings = true;
        private bool _showPlayerSettings;
        private bool _showBuildLog;
        private int _platformIndex;
        private int _playerPlatformIndex;

        // 构建日志
        private List<string> _buildLogs = new List<string>();
        private Vector2 _logScrollPosition;

        // 构建已排队标记：构建期间禁用按钮，避免重复触发
        private bool _buildScheduled;

        // 本地固定入口发布选项，仅保存在 EditorPrefs，不写入 UpdateSetting。
        private bool _publishAfterBuild;
        private string _publishRoot;

        [MenuItem("TEngine/Build/打包工具窗口", false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildPipelineWindow>("TEngine 打包工具");
            window.minSize = new Vector2(450, 600);
            window.Show();
        }

        private void OnEnable()
        {
            LoadSettings();
        }

        private void OnGUI()
        {
            if (_config == null)
                LoadSettings();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            {
                DrawHeader();
                DrawBasicSettings();
                DrawMinimalPackageSettings();
                DrawAdvancedSettings();
                DrawDllSettings();
                DrawPlayerSettings();
                DrawPublishSettings();
                DrawActionButtons();
                DrawBuildLog();
            }
            EditorGUILayout.EndScrollView();
        }

        #region Header

        private void DrawHeader()
        {
            GUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            EditorGUILayout.LabelField("TEngine 打包工具", titleStyle, GUILayout.Height(30));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // 刷新按钮
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新", GUILayout.Width(60), GUILayout.Height(22)))
            {
                LoadSettings();
            }

            if (GUILayout.Button("重置默认", GUILayout.Width(80), GUILayout.Height(22)))
            {
                _config = BuildConfig.CreateDefault();
                SaveSettings();
                AddLog("已重置为默认配置");
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);
        }

        #endregion

        #region 基础设置

        private void DrawBasicSettings()
        {
            _showBasicSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showBasicSettings,
                new GUIContent("基础设置", "目标平台、构建管线、加密等核心参数"));

            if (_showBasicSettings)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    // 目标平台
                    _platformIndex = EditorGUILayout.Popup("目标平台", _platformIndex, PlatformNames);
                    _config.BuildTarget = PlatformTargets[_platformIndex];

                    EditorGUILayout.Space(3);

                    // 构建管线
                    int pipelineIndex = _config.BuildPipeline == EBuildPipeline.BuiltinBuildPipeline ? 1 : 0;
                    pipelineIndex = EditorGUILayout.Popup("构建管线", pipelineIndex, PipelineNames);
                    _config.BuildPipeline = pipelineIndex == 1
                        ? EBuildPipeline.BuiltinBuildPipeline
                        : EBuildPipeline.ScriptableBuildPipeline;

                    // 压缩方式
                    _config.CompressOption = (ECompressOption)EditorGUILayout.Popup("压缩方式",
                        (int)_config.CompressOption, CompressNames);

                    // 加密方式
                    _config.EncryptionType = (EncryptionType)EditorGUILayout.Popup("加密方式",
                        (int)_config.EncryptionType, EncryptionNames);

                    EditorGUILayout.Space(3);

                    // 资源版本号
                    EditorGUILayout.BeginHorizontal();
                    _config.PackageVersion = EditorGUILayout.TextField("资源版本号", _config.PackageVersion);
                    if (GUILayout.Button("自动", GUILayout.Width(50)))
                    {
                        _config.PackageVersion = BuildConfig.GetDefaultPackageVersion();
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    _config.ReleaseId = EditorGUILayout.TextField("两阶段 Release ID", _config.ReleaseId);
                    if (GUILayout.Button("自动", GUILayout.Width(50)))
                    {
                        _config.ReleaseId = BuildConfig.GetDefaultReleaseId();
                    }
                    EditorGUILayout.EndHorizontal();

                    // 输出目录
                    EditorGUILayout.BeginHorizontal();
                    _config.OutputRoot = EditorGUILayout.TextField("AB输出目录", _config.OutputRoot);
                    if (GUILayout.Button("浏览", GUILayout.Width(50)))
                    {
                        string selected = EditorUtility.OpenFolderPanel("选择输出目录", _config.OutputRoot, "");
                        if (!string.IsNullOrEmpty(selected))
                        {
                            string projectPath = PathGetRelative(Application.dataPath + "/../", selected);
                            _config.OutputRoot = string.IsNullOrEmpty(projectPath) ? selected : projectPath;
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(3);
                    EditorGUILayout.HelpBox("选择构建目标平台和基础参数。AB输出目录支持相对路径（相对于项目根目录）。", MessageType.Info);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(5);
        }

        #endregion

        #region 最小包设置

        private void DrawMinimalPackageSettings()
        {
            _showMinimalPackageSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showMinimalPackageSettings,
                new GUIContent("最小包设置", "删除 StreamingAssets 中的 .bundle 文件以减小首包体积"));

            if (_showMinimalPackageSettings)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    _config.MinimalPackage = EditorGUILayout.ToggleLeft(
                        new GUIContent("启用最小包模式", "构建后删除 StreamingAssets 中的 .bundle 文件"),
                        _config.MinimalPackage);

                    if (_config.MinimalPackage)
                    {
                        EditorGUILayout.Space(3);
                        _config.RetainTags = EditorGUILayout.TextField(
                            new GUIContent("保留Tag(逗号分隔)", "带这些Tag的bundle不会被删除"),
                            _config.RetainTags);

                        EditorGUILayout.Space(3);

                        string tagInfo = string.IsNullOrWhiteSpace(_config.RetainTags)
                            ? "所有 .bundle 文件将被删除（仅保留清单）"
                            : $"保留带 [{_config.RetainTags}] Tag 的 bundle，其余删除";

                        EditorGUILayout.HelpBox(
                            $"最小包模式：删除 StreamingAssets 中所有 .bundle 文件，仅保留清单文件（.bytes/.hash/.version）。\n" +
                            $"当前: {tagInfo}\n\n" +
                            $"适用于 HostPlayMode 在线下载资源的场景，可大幅减小首包体积。",
                            MessageType.Info);
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(5);
        }

        #endregion

        #region 高级设置

        private void DrawAdvancedSettings()
        {
            _showAdvancedSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showAdvancedSettings,
                new GUIContent("高级设置", "共享打包、依赖数据库、增量构建等"));

            if (_showAdvancedSettings)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    _config.EnableSharePackRule = EditorGUILayout.ToggleLeft(
                        new GUIContent("启用共享资源打包", "自动提取共享资源到独立bundle"),
                        _config.EnableSharePackRule);

                    _config.UseAssetDependencyDB = EditorGUILayout.ToggleLeft(
                        new GUIContent("使用资源依赖数据库", "提高打包速度"),
                        _config.UseAssetDependencyDB);

                    _config.ClearBuildCache = EditorGUILayout.ToggleLeft(
                        new GUIContent("清理构建缓存(禁用增量构建)", "全量重新构建"),
                        _config.ClearBuildCache);

                    _config.VerifyBuildingResult = EditorGUILayout.ToggleLeft(
                        new GUIContent("验证构建结果", "构建后验证资源完整性"),
                        _config.VerifyBuildingResult);

                    EditorGUILayout.Space(3);

                    _config.BuildinFileCopyOption = (EBuildinFileCopyOption)EditorGUILayout.Popup(
                        "内置文件拷贝", (int)_config.BuildinFileCopyOption, CopyOptionNames);

                    _config.FileNameStyle = (EFileNameStyle)EditorGUILayout.Popup(
                        "文件名风格", (int)_config.FileNameStyle, FileNameStyleNames);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(5);
        }

        #endregion

        #region 热更DLL设置

        private void DrawDllSettings()
        {
            _showDllSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showDllSettings,
                new GUIContent("热更DLL设置", "HybridCLR 热更程序集编译"));

            if (_showDllSettings)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    _config.BuildHotFixDll = EditorGUILayout.ToggleLeft(
                        new GUIContent("构建前编译热更DLL", "执行 BuildDLLCommand.BuildAndCopyDlls"),
                        _config.BuildHotFixDll);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(5);
        }

        #endregion

        #region 打包Player设置

        private void DrawPlayerSettings()
        {
            _showPlayerSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showPlayerSettings,
                new GUIContent("打包Player设置", "构建可执行程序"));

            if (_showPlayerSettings)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    _config.BuildPlayer = EditorGUILayout.ToggleLeft(
                        new GUIContent("构建Player", "构建可执行程序(exe/apk/ipa)"),
                        _config.BuildPlayer);

                    if (_config.BuildPlayer)
                    {
                        EditorGUILayout.Space(3);

                        _playerPlatformIndex = EditorGUILayout.Popup("Player平台", _playerPlatformIndex, PlatformNames);
                        _config.PlayerPlatform = PlatformTargets[_playerPlatformIndex];

                        EditorGUILayout.BeginHorizontal();
                        _config.PlayerOutputPath = EditorGUILayout.TextField("输出路径", _config.PlayerOutputPath);
                        if (GUILayout.Button("浏览", GUILayout.Width(50)))
                        {
                            string selected = EditorUtility.SaveFilePanel("选择输出路径",
                                System.IO.Path.GetDirectoryName(_config.PlayerOutputPath),
                                System.IO.Path.GetFileName(_config.PlayerOutputPath), "");
                            if (!string.IsNullOrEmpty(selected))
                            {
                                _config.PlayerOutputPath = selected;
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(5);
        }

        #endregion

        #region 本地发布设置

        private void DrawPublishSettings()
        {
            EditorGUILayout.BeginVertical("HelpBox");
            {
                _publishAfterBuild = EditorGUILayout.ToggleLeft(
                    new GUIContent("构建成功后发布到本地目录",
                        "仅对 AssetBundle 构建和一键构建生效；Player-only 不发布"),
                    _publishAfterBuild);

                EditorGUILayout.BeginHorizontal();
                _publishRoot = EditorGUILayout.TextField(
                    new GUIContent("本地发布根目录", "发布为身份/current.json 与 releases/{ReleaseId} 的服务根目录"),
                    _publishRoot);
                if (GUILayout.Button("浏览", GUILayout.Width(50)))
                {
                    string selected = EditorUtility.OpenFolderPanel(
                        "选择本地发布根目录",
                        _publishRoot,
                        "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        string projectPath = PathGetRelative(Application.dataPath + "/../", selected);
                        _publishRoot = string.IsNullOrEmpty(projectPath) ? selected : projectPath;
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox(
                    "启用后要求 UpdateSetting 开启两阶段更新并选择 FixedEntry；构建和 Player 阶段全部成功后才会复制并提交 current.json。\n" +
                    "发布器不会修改 UpdateSetting，也不会上传远程服务器。",
                    MessageType.Info);
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        #endregion

        #region 操作按钮

        private void DrawActionButtons()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            GUILayout.Space(5);

            EditorGUI.BeginDisabledGroup(_buildScheduled);
            {
                // 主按钮行
                EditorGUILayout.BeginHorizontal();
                {
                    var abStyle = new GUIStyle(GUI.skin.button)
                    {
                        fontSize = 13,
                        fontStyle = FontStyle.Bold,
                    };

                    if (GUILayout.Button("构建 AssetBundle", abStyle, GUILayout.Height(35)))
                    {
                        QueueBuild(buildPlayer: false);
                    }

                    if (GUILayout.Button("构建 Player", abStyle, GUILayout.Height(35)))
                    {
                        BuildConfig snapshot = CloneConfig(_config);
                        ScheduleBuild(() => ExecuteBuildPlayerOnlyAsync(snapshot));
                    }
                }
                EditorGUILayout.EndHorizontal();

                // 一键构建按钮
                var fullBuildStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.2f, 0.6f, 1f) },
                };

                if (GUILayout.Button("一键构建 (AB + Player)", fullBuildStyle, GUILayout.Height(38)))
                {
                    _config.BuildPlayer = true;
                    QueueBuild(buildPlayer: true);
                }
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(5);
        }

        /// <summary>
        /// 排队执行构建，等到 OnGUI 返回后再真正调用。
        /// 构建是长同步操作，期间 Unity 会继续派发 IMGUI 事件；若直接在按钮回调里执行
        /// （此时滚动视图分组尚未闭合），外层 OnGUI 的 GUILayout 分组栈会被冲掉，
        /// 恢复后 EndScrollView 就会报 "EndLayoutGroup: BeginLayoutGroup must be called first"。
        /// </summary>
        private void QueueBuild(bool buildPlayer)
        {
            BuildConfig snapshot = CloneConfig(_config);
            // “构建 AssetBundle”明确不请求 Player；一键构建明确请求 Player。
            snapshot.BuildPlayer = buildPlayer;
            bool publishAfterBuild = _publishAfterBuild;
            string publishRoot = _publishRoot;
            ScheduleBuild(() => ExecuteBuildAsync(
                snapshot,
                buildPlayer,
                publishAfterBuild,
                publishRoot));
        }

        private void ScheduleBuild(Func<UniTask> build)
        {
            if (_buildScheduled)
            {
                return;
            }

            SaveSettings();
            _buildScheduled = true;
            EditorApplication.delayCall += () =>
            {
                RunScheduledBuildAsync(build).Forget();
            };

            Repaint();
        }

        private async UniTaskVoid RunScheduledBuildAsync(Func<UniTask> build)
        {
            try
            {
                await build();
            }
            catch (Exception exception)
            {
                AddLog($"[错误] 构建任务异常: {exception}");
                _showBuildLog = true;
            }
            finally
            {
                _buildScheduled = false;
                Repaint();
            }
        }

        #endregion

        #region 构建日志

        private void DrawBuildLog()
        {
            _showBuildLog = EditorGUILayout.BeginFoldoutHeaderGroup(_showBuildLog,
                new GUIContent($"构建日志 ({_buildLogs.Count})", "构建过程的日志输出"));

            if (_showBuildLog)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    if (GUILayout.Button("清空日志", GUILayout.Height(22)))
                    {
                        _buildLogs.Clear();
                    }

                    _logScrollPosition = EditorGUILayout.BeginScrollView(_logScrollPosition, GUILayout.Height(150));
                    {
                        foreach (var log in _buildLogs)
                        {
                            EditorGUILayout.SelectableLabel(log, EditorStyles.miniLabel,
                                GUILayout.Height(EditorStyles.miniLabel.CalcHeight(new GUIContent(log), position.width - 30)));
                        }
                    }
                    EditorGUILayout.EndScrollView();
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        #endregion

        #region 构建执行

        private async UniTask ExecuteBuildAsync(
            BuildConfig configSnapshot,
            bool buildPlayer,
            bool publishAfterBuild,
            string publishRoot)
        {
            _buildLogs.Clear();
            AddLog($"========== 开始构建 ==========");
            AddLog($"平台: {configSnapshot.BuildTarget} | 管线: {configSnapshot.BuildPipeline} | 最小包: {configSnapshot.MinimalPackage}");

            if (string.IsNullOrWhiteSpace(configSnapshot.PackageVersion))
            {
                configSnapshot.PackageVersion = BuildConfig.GetDefaultPackageVersion();
                AddLog($"版本号为空，自动生成: {configSnapshot.PackageVersion}");
            }

            BuildExecutionResult result = null;
            TwoStageBuildAndPublishResult publishResult = null;
            try
            {
                // 注册日志回调
                Application.logMessageReceived += OnBuildLogReceived;

                if (publishAfterBuild)
                {
                    AddLog("已启用构建后本地发布：构建完整成功后将校验并提交 current.json。");
                    publishResult = await TwoStageBuildAndPublish.ExecuteAsync(
                        configSnapshot,
                        publishRoot,
                        buildPlayer,
                        CancellationToken.None);
                    result = publishResult?.BuildResult;
                }
                else
                {
                    // 发布开关关闭时沿用原同步构建流程。
                    result = ReleaseTools.ExecuteBuildWithResult(configSnapshot, buildPlayer);
                }
            }
            finally
            {
                Application.logMessageReceived -= OnBuildLogReceived;
            }

            LogBuildResult(result);
            if (publishAfterBuild)
                LogPublishResult(publishResult);

            // 自动滚动到底部并展开日志
            _showBuildLog = true;
            Repaint();
        }

        private async UniTask ExecuteBuildPlayerOnlyAsync(BuildConfig configSnapshot)
        {
            _buildLogs.Clear();
            AddLog($"========== 仅构建 Player（独立模式） ==========");
            AddLog($"平台: {configSnapshot.PlayerPlatform} | 输出: {configSnapshot.PlayerOutputPath}");

            BuildExecutionResult result = null;
            try
            {
                Application.logMessageReceived += OnBuildLogReceived;
                // 保持 Player-only 与本地发布完全隔离。
                result = ReleaseTools.ExecutePlayerOnlyWithResult(configSnapshot);
            }
            finally
            {
                Application.logMessageReceived -= OnBuildLogReceived;
            }

            if (result == null)
                AddLog("[错误] 构建核心返回空结果");
            else if (result.Succeeded)
                AddLog($"========== 仅 Player 构建完成 ==========" +
                       $"\nPlayer输出: {result.PlayerOutputPath}\n" +
                       "注意：仅 Player 模式不执行 DLL/AB/最小包流程，不代表完整发布成功。");
            else if (result.Cancelled)
                AddLog($"========== Player 构建被取消 [阶段: {result.Stage}] ==========\n[取消] {result.Error}");
            else
                AddLog($"========== Player 构建失败 [阶段: {result.Stage}] ==========\n[失败] {result.Error}");

            _showBuildLog = true;
            Repaint();
        }

        private void LogBuildResult(BuildExecutionResult result)
        {
            // 结果判定以阶段化结果为准，方法正常返回不代表成功。
            if (result == null)
            {
                AddLog("[错误] 构建核心返回空结果");
            }
            else if (result.Succeeded)
            {
                AddLog("========== 构建成功 ==========");
                AddLog($"阶段: 已全部完成 | AB输出: {result.OutputPackageDirectory ?? "无"}");
                if (!string.IsNullOrEmpty(result.PlayerOutputPath))
                    AddLog($"Player输出: {result.PlayerOutputPath}");
            }
            else if (result.Cancelled)
            {
                AddLog($"========== 构建被取消 [阶段: {result.Stage}] ==========");
                AddLog($"[取消] {result.Error}");
            }
            else
            {
                AddLog($"========== 构建失败 [阶段: {result.Stage}] ==========");
                AddLog($"[失败] {result.Error}");
            }
        }

        private void LogPublishResult(TwoStageBuildAndPublishResult result)
        {
            if (result == null || result.PublishResult == null)
            {
                AddLog("========== 发布未执行 ==========");
                AddLog("[发布] 未获得发布结果。");
                return;
            }

            TwoStageReleasePublishResult publish = result.PublishResult;
            if (publish.Succeeded)
            {
                AddLog("========== 本地发布成功 ==========");
                AddLog($"发布目录: {publish.PublishDirectory}");
                AddLog($"入口路径: {publish.EntryPath}");
                if (publish.ReusedExistingRelease)
                    AddLog("[发布] 目标 release 内容完全一致，已复用历史目录。");
            }
            else if (publish.Cancelled)
            {
                AddLog("========== 本地发布已取消 ==========");
                AddLog($"[发布取消] {publish.Error}");
            }
            else if (publish.Status == TwoStageReleasePublishStatus.NotRun)
            {
                AddLog("========== 发布未执行 ==========");
                AddLog($"[发布] {publish.Error}");
            }
            else
            {
                AddLog("========== 本地发布失败 ==========");
                AddLog($"[发布失败] {publish.Error}");
            }
        }

        private void OnBuildLogReceived(string condition, string stackTrace, LogType type)
        {
            string prefix = type switch
            {
                LogType.Error => "[ERR]",
                LogType.Warning => "[WARN]",
                LogType.Assert => "[ASSERT]",
                _ => ""
            };

            if (!string.IsNullOrEmpty(prefix) || condition.StartsWith("[") || condition.Contains("构建") || condition.Contains("Build"))
            {
                AddLog($"{prefix}{condition}");
            }
        }

        private void AddLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _buildLogs.Add($"[{timestamp}] {message}");
            _logScrollPosition = new Vector2(0, float.MaxValue);
        }

        #endregion

        #region 持久化

        private void LoadSettings()
        {
            _config = new BuildConfig();

            _platformIndex = EditorPrefs.GetInt("TEngine_BP_BuildTarget", -1);
            if (_platformIndex < 0 || _platformIndex >= PlatformTargets.Length)
            {
                _platformIndex = GetActivePlatformIndex();
            }
            _config.BuildTarget = PlatformTargets[_platformIndex];

            int pipelineIndex = EditorPrefs.GetInt("TEngine_BP_BuildPipeline", 0);
            _config.BuildPipeline = pipelineIndex == 1 ? EBuildPipeline.BuiltinBuildPipeline : EBuildPipeline.ScriptableBuildPipeline;

            _config.CompressOption = (ECompressOption)EditorPrefs.GetInt("TEngine_BP_CompressOption", 1);
            _config.EncryptionType = (EncryptionType)EditorPrefs.GetInt("TEngine_BP_EncryptionType", 0);

            _config.PackageVersion = EditorPrefs.GetString("TEngine_BP_PackageVersion", "");
            _config.ReleaseId = EditorPrefs.GetString("TEngine_BP_ReleaseId", BuildConfig.GetDefaultReleaseId());
            _config.OutputRoot = EditorPrefs.GetString("TEngine_BP_OutputRoot", "./Builds/");

            _config.MinimalPackage = EditorPrefs.GetBool("TEngine_BP_MinimalPackage", false);
            _config.RetainTags = EditorPrefs.GetString("TEngine_BP_RetainTags", "");

            _config.EnableSharePackRule = EditorPrefs.GetBool("TEngine_BP_EnableSharePack", true);
            _config.UseAssetDependencyDB = EditorPrefs.GetBool("TEngine_BP_UseDepDB", true);
            _config.ClearBuildCache = EditorPrefs.GetBool("TEngine_BP_ClearCache", false);
            _config.VerifyBuildingResult = EditorPrefs.GetBool("TEngine_BP_VerifyResult", true);
            _config.BuildinFileCopyOption = (EBuildinFileCopyOption)EditorPrefs.GetInt("TEngine_BP_CopyOption", 0);
            _config.FileNameStyle = (EFileNameStyle)EditorPrefs.GetInt("TEngine_BP_FileNameStyle", 1);

            _config.BuildHotFixDll = EditorPrefs.GetBool("TEngine_BP_BuildDll", true);

            _config.BuildPlayer = EditorPrefs.GetBool("TEngine_BP_BuildPlayer", false);

            _playerPlatformIndex = EditorPrefs.GetInt("TEngine_BP_PlayerPlatform", -1);
            if (_playerPlatformIndex < 0 || _playerPlatformIndex >= PlatformTargets.Length)
            {
                _playerPlatformIndex = GetActivePlatformIndex();
            }
            _config.PlayerPlatform = PlatformTargets[_playerPlatformIndex];

            _config.PlayerOutputPath = EditorPrefs.GetString("TEngine_BP_PlayerOutput",
                BuildConfig.GetDefaultPlayerOutputPath(_config.PlayerPlatform));

            _publishAfterBuild = EditorPrefs.GetBool("TEngine_BP_PublishAfterBuild", false);
            _publishRoot = EditorPrefs.GetString("TEngine_BP_PublishRoot", "./LocalPublish");
        }

        private void SaveSettings()
        {
            EditorPrefs.SetInt("TEngine_BP_BuildTarget", _platformIndex);
            EditorPrefs.SetInt("TEngine_BP_BuildPipeline", _config.BuildPipeline == EBuildPipeline.BuiltinBuildPipeline ? 1 : 0);
            EditorPrefs.SetInt("TEngine_BP_CompressOption", (int)_config.CompressOption);
            EditorPrefs.SetInt("TEngine_BP_EncryptionType", (int)_config.EncryptionType);
            EditorPrefs.SetString("TEngine_BP_PackageVersion", _config.PackageVersion);
            EditorPrefs.SetString("TEngine_BP_ReleaseId", _config.ReleaseId);
            EditorPrefs.SetString("TEngine_BP_OutputRoot", _config.OutputRoot);
            EditorPrefs.SetBool("TEngine_BP_MinimalPackage", _config.MinimalPackage);
            EditorPrefs.SetString("TEngine_BP_RetainTags", _config.RetainTags);
            EditorPrefs.SetBool("TEngine_BP_EnableSharePack", _config.EnableSharePackRule);
            EditorPrefs.SetBool("TEngine_BP_UseDepDB", _config.UseAssetDependencyDB);
            EditorPrefs.SetBool("TEngine_BP_ClearCache", _config.ClearBuildCache);
            EditorPrefs.SetBool("TEngine_BP_VerifyResult", _config.VerifyBuildingResult);
            EditorPrefs.SetInt("TEngine_BP_CopyOption", (int)_config.BuildinFileCopyOption);
            EditorPrefs.SetInt("TEngine_BP_FileNameStyle", (int)_config.FileNameStyle);
            EditorPrefs.SetBool("TEngine_BP_BuildDll", _config.BuildHotFixDll);
            EditorPrefs.SetBool("TEngine_BP_BuildPlayer", _config.BuildPlayer);
            EditorPrefs.SetInt("TEngine_BP_PlayerPlatform", _playerPlatformIndex);
            EditorPrefs.SetString("TEngine_BP_PlayerOutput", _config.PlayerOutputPath);
            EditorPrefs.SetBool("TEngine_BP_PublishAfterBuild", _publishAfterBuild);
            EditorPrefs.SetString("TEngine_BP_PublishRoot", _publishRoot);
        }

        private int GetActivePlatformIndex()
        {
            BuildTarget active = EditorUserBuildSettings.activeBuildTarget;
            for (int i = 0; i < PlatformTargets.Length; i++)
            {
                if (PlatformTargets[i] == active)
                    return i;
            }
            return 0;
        }

        #endregion

        #region 工具方法

        private static string PathGetRelative(string relativeTo, string path)
        {
            try
            {
                var uri = new Uri(relativeTo + "/");
                var rel = Uri.UnescapeDataString(uri.MakeRelativeUri(new Uri(path)).ToString());
                return rel.Replace('/', '\\');
            }
            catch
            {
                return "";
            }
        }

        private static BuildConfig CloneConfig(BuildConfig source)
        {
            return new BuildConfig
            {
                BuildTarget = source.BuildTarget,
                BuildPipeline = source.BuildPipeline,
                CompressOption = source.CompressOption,
                EncryptionType = source.EncryptionType,
                PackageVersion = source.PackageVersion,
                ReleaseId = source.ReleaseId,
                OutputRoot = source.OutputRoot,
                MinimalPackage = source.MinimalPackage,
                RetainTags = source.RetainTags,
                EnableSharePackRule = source.EnableSharePackRule,
                UseAssetDependencyDB = source.UseAssetDependencyDB,
                ClearBuildCache = source.ClearBuildCache,
                VerifyBuildingResult = source.VerifyBuildingResult,
                BuildinFileCopyOption = source.BuildinFileCopyOption,
                FileNameStyle = source.FileNameStyle,
                BuildHotFixDll = source.BuildHotFixDll,
                BuildPlayer = source.BuildPlayer,
                PlayerPlatform = source.PlayerPlatform,
                PlayerOutputPath = source.PlayerOutputPath,
            };
        }

        #endregion
    }
}
