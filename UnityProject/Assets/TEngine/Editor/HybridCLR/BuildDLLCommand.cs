#if ENABLE_HYBRIDCLR
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
#endif
using System;
using System.Collections.Generic;
using System.IO;
#if ENABLE_OBFUZ
using Obfuz.Settings;
using Obfuz4HybridCLR;
#endif
using System.Linq;
using HybridCLR.Editor.Installer;
using TEngine.Editor;
using UnityEditor;
using UnityEngine;

public static class BuildDLLCommand
{
    private const string EnableHybridClrScriptingDefineSymbol = "ENABLE_HYBRIDCLR";
    private const string EnableObfuzScriptingDefineSymbol = "ENABLE_OBFUZ";

    /// <summary>
    /// 当前编辑器程序集是否编译进 HybridCLR 支持（ENABLE_HYBRIDCLR 宏状态）。
    /// <remarks>为 false 时显式的 DLL 构建请求必须失败报告，不能空操作成功。</remarks>
    /// </summary>
    public static bool HybridClrAvailable
    {
        get
        {
#if ENABLE_HYBRIDCLR
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// DLL 构建/复制结果。
    /// </summary>
    public sealed class DllBuildResult
    {
        public bool Success;

        /// <summary>全部错误（缺文件/空文件/复制异常等），不能只报第一条。</summary>
        public List<string> Errors = new List<string>();

        /// <summary>已复制的目标文件。</summary>
        public List<string> CopiedFiles = new List<string>();

        public string GetErrorSummary()
        {
            return string.Join("\n", Errors);
        }
    }

    #region HybridCLR/Define Symbols
    /// <summary>
    /// 禁用HybridCLR宏定义。
    /// </summary>
    [MenuItem("HybridCLR/Define Symbols/Disable HybridCLR", false, 30)]
    public static void DisableHybridCLR()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableHybridClrScriptingDefineSymbol);
        HybridCLR.Editor.SettingsUtil.Enable = false;
#if ENABLE_HYBRIDCLR
        UpdateSettingEditor.ForceUpdateAssemblies();
#endif
    }

    /// <summary>
    /// 开启HybridCLR宏定义。
    /// </summary>
    [MenuItem("HybridCLR/Define Symbols/Enable HybridCLR", false, 31)]
    public static void EnableHybridCLR()
    {
        // 先去判断安装了没
        var controller = new InstallerController();
        if (!controller.HasInstalledHybridCLR())
        {
            controller.InstallDefaultHybridCLR();
        }

        if (!HybridCLR.Editor.SettingsUtil.Enable)
        {
            HybridCLR.Editor.SettingsUtil.Enable = true;
#if ENABLE_HYBRIDCLR
            UpdateSettingEditor.ForceUpdateAssemblies();
#endif
        }
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableHybridClrScriptingDefineSymbol);
        ScriptingDefineSymbols.AddScriptingDefineSymbol(EnableHybridClrScriptingDefineSymbol);
        UpdateSettingEditor.ForceUpdateAssemblies();
    }
    #endregion

#if ENABLE_OBFUZ
    #region Obfuz/Define Symbols
    /// <summary>
    /// 禁用Obfuz宏定义。
    /// </summary>
    [MenuItem("Obfuz/Define Symbols/Disable Obfuz", false, 30)]
    public static void DisableObfuz()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
        ObfuzSettings.Instance.buildPipelineSettings.enable = false;
    }

    /// <summary>
    /// 开启Obfuz宏定义。
    /// </summary>
    [MenuItem("Obfuz/Define Symbols/Enable Obfuz", false, 31)]
    public static void EnableObfuz()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
        ScriptingDefineSymbols.AddScriptingDefineSymbol(EnableObfuzScriptingDefineSymbol);
        ObfuzSettings.Instance.buildPipelineSettings.enable = true;
    }
    #endregion
#endif

    #region 构建/复制入口

    /// <summary>
    /// 菜单入口：以当前激活平台编译并复制。失败抛出构建异常。
    /// </summary>
    [MenuItem("HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath")]
    public static void BuildAndCopyDlls()
    {
        BuildAndCopyDlls(EditorUserBuildSettings.activeBuildTarget);
    }

    /// <summary>
    /// 兼容入口：显式目标编译并复制（显式 target 贯穿编译与复制，底层不读取 activeBuildTarget）。失败抛出构建异常。
    /// </summary>
    public static void BuildAndCopyDlls(BuildTarget target)
    {
        var result = BuildAndCopyDllsWithResult(target);
        if (result == null || !result.Success)
            throw new TEngine.BuildExecutionException(TEngine.BuildStage.Dll,
                result == null ? "DLL 构建返回空结果" : result.GetErrorSummary());
    }

    /// <summary>
    /// 核心：显式目标编译 → 生成复制计划 → 整体校验（存在且非空）→ 统一复制。
    /// <remarks>缺任意 AOT、热更或选中的混淆 DLL 都整体失败并开始复制前停止，不能被旧产物掩盖，不能 continue。</remarks>
    /// </summary>
    public static DllBuildResult BuildAndCopyDllsWithResult(BuildTarget target)
    {
        var result = new DllBuildResult();
        if (target == BuildTarget.NoTarget)
        {
            result.Errors.Add("构建目标无效 (NoTarget)");
            return result;
        }

#if ENABLE_HYBRIDCLR
        try
        {
            // 1. 编译热更程序集（显式目标）
            CompileDllCommand.CompileDll(target);

            // 2. 解析并校验目标目录（按需创建，禁止越出 Assets 输出范围）
            string dstDir = TEngine.DllArtifactCopier.ResolveAssemblyTextAssetDir(true);

            // 3. 生成复制计划（含混淆产物选择规则）
            List<TEngine.DllCopyEntry> entries = BuildBaseCopyPlan(target, dstDir);
            AppendObfuzPlan(target, dstDir, entries, regenerate: true);

            // 4. 先整体校验所有源文件，缺任意文件都失败（列出全部缺失项，不 continue）
            List<string> errors = TEngine.DllArtifactCopier.ValidateSources(entries);
            if (errors.Count > 0)
            {
                result.Errors.AddRange(errors);
                return result;
            }

            // 5. 统一复制；复制异常立即失败，不执行 AB（由上层编排保证）
            TEngine.DllArtifactCopier.CopyAll(entries);
            result.CopiedFiles.AddRange(entries.Select(e => e.DestPath));

            AssetDatabase.Refresh();
            result.Success = true;
        }
        catch (Exception e)
        {
            result.Errors.Add(e.ToString());
        }
        return result;
#else
        result.Errors.Add("HybridCLR 未启用（编辑器程序集缺少 ENABLE_HYBRIDCLR 宏），无法编译热更DLL；请先执行菜单 HybridCLR/Define Symbols/Enable HybridCLR 后重试");
        return result;
#endif
    }

    /// <summary>
    /// 复用模式校验（BuildHotFixDll=false 时 AB 前调用）：不编译不复制，校验既有 .bytes
    /// 存在、非空，并与所选目标对应源文件内容一致（哈希比较，不看时间戳）。
    /// <remarks>源缺失或不一致失败，要求重新准备；HybridCLR 未启用时不强制 DLL/AOT。</remarks>
    /// </summary>
    public static DllBuildResult ValidateReusedDllsWithResult(BuildTarget target)
    {
        var result = new DllBuildResult();
        if (target == BuildTarget.NoTarget)
        {
            result.Errors.Add("构建目标无效 (NoTarget)");
            return result;
        }

#if ENABLE_HYBRIDCLR
        try
        {
            // 只解析校验目标目录（不创建）
            string dstDir = TEngine.DllArtifactCopier.ResolveAssemblyTextAssetDir(false);

            List<TEngine.DllCopyEntry> entries = BuildBaseCopyPlan(target, dstDir);
            AppendObfuzPlan(target, dstDir, entries, regenerate: false);

            List<string> errors = TEngine.DllArtifactCopier.ValidateReuse(entries);
            if (errors.Count > 0)
            {
                result.Errors.AddRange(errors);
                return result;
            }

            result.Success = true;
        }
        catch (Exception e)
        {
            result.Errors.Add(e.ToString());
        }
        return result;
#else
        // HybridCLR 未启用且未请求 DLL 构建时不强制 DLL/AOT
        result.Success = true;
        return result;
#endif
    }

    /// <summary>
    /// 兼容入口：复制 AOT + 热更 + 混淆产物（不执行首次编译，混淆分支按原流程会重新编译混淆）。失败抛出构建异常。
    /// </summary>
    public static void CopyAOTHotUpdateDlls(BuildTarget target)
    {
        var result = CopyAOTHotUpdateDllsWithResult(target);
        if (result == null || !result.Success)
            throw new TEngine.BuildExecutionException(TEngine.BuildStage.Dll,
                result == null ? "DLL 复制返回空结果" : result.GetErrorSummary());
    }

    /// <summary>
    /// 核心：显式目标复制 AOT + 热更 + 混淆产物（先整体校验再复制）。
    /// </summary>
    public static DllBuildResult CopyAOTHotUpdateDllsWithResult(BuildTarget target)
    {
        var result = new DllBuildResult();
        if (target == BuildTarget.NoTarget)
        {
            result.Errors.Add("构建目标无效 (NoTarget)");
            return result;
        }

#if ENABLE_HYBRIDCLR
        try
        {
            string dstDir = TEngine.DllArtifactCopier.ResolveAssemblyTextAssetDir(true);

            List<TEngine.DllCopyEntry> entries = BuildBaseCopyPlan(target, dstDir);
            AppendObfuzPlan(target, dstDir, entries, regenerate: true);

            List<string> errors = TEngine.DllArtifactCopier.ValidateSources(entries);
            if (errors.Count > 0)
            {
                result.Errors.AddRange(errors);
                return result;
            }

            TEngine.DllArtifactCopier.CopyAll(entries);
            result.CopiedFiles.AddRange(entries.Select(e => e.DestPath));

            AssetDatabase.Refresh();
            result.Success = true;
        }
        catch (Exception e)
        {
            result.Errors.Add(e.ToString());
        }
        return result;
#else
        result.Errors.Add("HybridCLR 未启用（编辑器程序集缺少 ENABLE_HYBRIDCLR 宏），无法复制热更DLL");
        return result;
#endif
    }

    /// <summary>
    /// 兼容入口：以当前激活平台复制 AOT 程序集。失败抛出构建异常。
    /// </summary>
    public static void CopyAOTAssembliesToAssetPath()
    {
        CopyAOTAssembliesToAssetPath(EditorUserBuildSettings.activeBuildTarget);
    }

    /// <summary>
    /// 显式目标复制 AOT 程序集（先校验存在且非空，缺失即失败，不能 continue）。
    /// </summary>
    public static void CopyAOTAssembliesToAssetPath(BuildTarget target)
    {
        var result = new DllBuildResult();
#if ENABLE_HYBRIDCLR
        try
        {
            string aotAssembliesSrcDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            string aotAssembliesDstDir = TEngine.DllArtifactCopier.ResolveAssemblyTextAssetDir(true);

            var entries = new List<TEngine.DllCopyEntry>();
            foreach (var dll in TEngine.Settings.UpdateSetting.AOTMetaAssemblies)
            {
                entries.Add(new TEngine.DllCopyEntry($"{aotAssembliesSrcDir}/{dll}", $"{aotAssembliesDstDir}/{dll}.bytes"));
            }

            List<string> errors = TEngine.DllArtifactCopier.ValidateSources(entries);
            if (errors.Count > 0)
                throw new TEngine.BuildExecutionException(TEngine.BuildStage.Dll, string.Join("\n", errors));

            TEngine.DllArtifactCopier.CopyAll(entries);
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            result.Errors.Add(e.ToString());
        }

        if (result.Errors.Count > 0)
            throw new TEngine.BuildExecutionException(TEngine.BuildStage.Dll, result.GetErrorSummary());
#else
        throw new TEngine.BuildExecutionException(TEngine.BuildStage.Dll, "HybridCLR 未启用，无法复制 AOT 程序集");
#endif
    }

    /// <summary>
    /// 兼容入口：以当前激活平台复制热更程序集。失败抛出构建异常。
    /// </summary>
    public static void CopyHotUpdateAssembliesToAssetPath()
    {
        CopyHotUpdateAssembliesToAssetPath(EditorUserBuildSettings.activeBuildTarget);
    }

    /// <summary>
    /// 显式目标复制热更程序集（先校验存在且非空，缺失即失败，不能 continue）。
    /// </summary>
    public static void CopyHotUpdateAssembliesToAssetPath(BuildTarget target)
    {
#if ENABLE_HYBRIDCLR
        try
        {
            string hotfixDllSrcDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            string hotfixAssembliesDstDir = TEngine.DllArtifactCopier.ResolveAssemblyTextAssetDir(true);

            var entries = new List<TEngine.DllCopyEntry>();
            foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
            {
                entries.Add(new TEngine.DllCopyEntry($"{hotfixDllSrcDir}/{dll}", $"{hotfixAssembliesDstDir}/{dll}.bytes"));
            }

            List<string> errors = TEngine.DllArtifactCopier.ValidateSources(entries);
            if (errors.Count > 0)
                throw new TEngine.BuildExecutionException(TEngine.BuildStage.Dll, string.Join("\n", errors));

            TEngine.DllArtifactCopier.CopyAll(entries);
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            throw new TEngine.BuildExecutionException(TEngine.BuildStage.Dll, e.ToString(), e);
        }
#else
        throw new TEngine.BuildExecutionException(TEngine.BuildStage.Dll, "HybridCLR 未启用，无法复制热更程序集");
#endif
    }

    #endregion

    #region 复制计划

#if ENABLE_HYBRIDCLR
    /// <summary>
    /// 基础复制计划：AOT 补充元数据 + 热更程序集（排除保留名单），源目录按显式目标解析。
    /// </summary>
    private static List<TEngine.DllCopyEntry> BuildBaseCopyPlan(BuildTarget target, string dstDir)
    {
        var entries = new List<TEngine.DllCopyEntry>();

        // AOT 补充元数据 dll（裁剪产物，需要先构建过一次该目标平台的 Player）
        string aotAssembliesSrcDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
        foreach (var dll in TEngine.Settings.UpdateSetting.AOTMetaAssemblies)
        {
            entries.Add(new TEngine.DllCopyEntry($"{aotAssembliesSrcDir}/{dll}", $"{dstDir}/{dll}.bytes"));
        }

        // 热更程序集（排除保留名单）
        string hotUpdateDllPath = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
        foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
        {
            entries.Add(new TEngine.DllCopyEntry($"{hotUpdateDllPath}/{dll}", $"{dstDir}/{dll}.bytes"));
        }

        return entries;
    }

    /// <summary>
    /// 追加/覆盖混淆产物计划（保留现有选择规则：混淆名单内的程序集必须取混淆输出，缺混淆文件不能默默使用未混淆文件）。
    /// </summary>
    /// <param name="regenerate">true 时按原流程重新编译并混淆；false 时仅引用既有混淆产物（复用校验用）。</param>
    private static void AppendObfuzPlan(BuildTarget target, string dstDir, List<TEngine.DllCopyEntry> entries, bool regenerate)
    {
#if ENABLE_OBFUZ
        if (regenerate)
        {
            // 保留原流程：混淆前重新编译
            CompileDllCommand.CompileDll(target);

            string obfuscatedHotUpdateDllPath = PrebuildCommandExt.GetObfuscatedHotUpdateAssemblyOutputPath(target);
            ObfuscateUtil.ObfuscateHotUpdateAssemblies(target, obfuscatedHotUpdateDllPath);
        }

        string obfuscatedDllPath = PrebuildCommandExt.GetObfuscatedHotUpdateAssemblyOutputPath(target);
        string plainDllPath = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
        List<string> obfuscationRelativeAssemblyNames = ObfuzSettings.Instance.assemblySettings.GetObfuscationRelativeAssemblyNames();

        foreach (string assName in SettingsUtil.HotUpdateAssemblyNamesIncludePreserved)
        {
            string srcDir = obfuscationRelativeAssemblyNames.Contains(assName) ? obfuscatedDllPath : plainDllPath;
            string srcFile = $"{srcDir}/{assName}.dll";
            string dstFile = $"{dstDir}/{assName}.dll.bytes";
            UpsertEntry(entries, dstFile, srcFile);
        }
#endif
    }

    private static void UpsertEntry(List<TEngine.DllCopyEntry> entries, string dstFile, string srcFile)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].DestPath, dstFile, StringComparison.OrdinalIgnoreCase))
            {
                entries[i] = new TEngine.DllCopyEntry(srcFile, dstFile);
                return;
            }
        }
        entries.Add(new TEngine.DllCopyEntry(srcFile, dstFile));
    }
#endif

    #endregion
}
