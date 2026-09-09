using GameLogic;
using TEngine;
using UnityEngine;
using Object = UnityEngine.Object;

public class GameModule
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewSession()
    {
        _base = null;
        _debugger = null;
        _fsm = null;
        _procedure = null;
        _resource = null;
        _audio = null;
        _ui = null;
        _fgui = null;
        _scene = null;
        _timer = null;
        _localization = null;
    }

    #region 框架模块

    /// <summary>
    /// 获取游戏基础模块。
    /// </summary>
    public static RootModule Base
    {
        get => ModuleSystem.IsRunning ? _base ??= Object.FindObjectOfType<RootModule>() : null;
        private set => _base = value;
    }

    private static RootModule _base;

    /// <summary>
    /// 获取调试模块。
    /// </summary>
    public static IDebuggerModule Debugger
    {
        get => ModuleSystem.IsRunning ? _debugger ??= Get<IDebuggerModule>() : null;
        private set => _debugger = value;
    }

    private static IDebuggerModule _debugger;

    /// <summary>
    /// 获取有限状态机模块。
    /// </summary>
    public static IFsmModule Fsm => ModuleSystem.IsRunning ? _fsm ??= Get<IFsmModule>() : null;

    private static IFsmModule _fsm;

    /// <summary>
    /// 流程管理模块。
    /// </summary>
    public static IProcedureModule Procedure => ModuleSystem.IsRunning ? _procedure ??= Get<IProcedureModule>() : null;

    private static IProcedureModule _procedure;

    /// <summary>
    /// 获取资源模块。
    /// </summary>
    public static IResourceModule Resource => ModuleSystem.IsRunning ? _resource ??= Get<IResourceModule>() : null;

    private static IResourceModule _resource;

    /// <summary>
    /// 获取音频模块。
    /// </summary>
    public static IAudioModule Audio => ModuleSystem.IsRunning ? _audio ??= Get<IAudioModule>() : null;

    private static IAudioModule _audio;

    /// <summary>
    /// 获取UI模块。
    /// </summary>
    public static UIModule UI => ModuleSystem.IsRunning ? _ui ??= UIModule.Instance : null;

    private static UIModule _ui;

    /// <summary>
    /// 获取独立的 FairyGUI 模块。现有 UGUI 继续通过 <see cref="UI"/> 使用。
    /// </summary>
    public static FguiModule FGUI => ModuleSystem.IsRunning ? _fgui ??= FguiModule.Instance : null;

    private static FguiModule _fgui;

    /// <summary>
    /// 获取场景模块。
    /// </summary>
    public static ISceneModule Scene => ModuleSystem.IsRunning ? _scene ??= Get<ISceneModule>() : null;

    private static ISceneModule _scene;

    /// <summary>
    /// 获取计时器模块。
    /// </summary>
    public static ITimerModule Timer => ModuleSystem.IsRunning ? _timer ??= Get<ITimerModule>() : null;

    private static ITimerModule _timer;

    /// <summary>
    /// 获取本地化模块。
    /// </summary>
    public static ILocalizationModule Localization => ModuleSystem.IsRunning ? _localization ??= Get<ILocalizationModule>() : null;

    private static ILocalizationModule _localization;

    #endregion

    /// <summary>
    /// 获取游戏框架模块类。
    /// </summary>
    private static T Get<T>() where T : class
    {
        T module = ModuleSystem.GetModule<T>();

        Log.Assert(condition: module != null, $"{typeof(T)} is null");

        return module;
    }

    public static void Shutdown()
    {
        try
        {
            Log.Info("GameModule Shutdown");
        }
        catch
        {
        }

        try
        {
            _fgui?.Shutdown();
        }
        catch (System.Exception exception)
        {
            try { Log.Error("GameModule FairyGUI shutdown failed: {0}", exception); }
            catch { }
        }
        finally
        {
            _base = null;
            _debugger = null;
            _fsm = null;
            _procedure = null;
            _resource = null;
            _audio = null;
            _ui = null;
            _fgui = null;
            _scene = null;
            _timer = null;
            _localization = null;
        }
    }
}
