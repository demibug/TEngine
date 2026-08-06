using GameBattle;
using GameLogic;
using TEngine;
using Object = UnityEngine.Object;

public class GameModule
{
    #region 框架模块

    /// <summary>
    /// 获取游戏基础模块。
    /// </summary>
    public static RootModule Base
    {
        get => _base ??= Object.FindObjectOfType<RootModule>();
        private set => _base = value;
    }

    private static RootModule _base;

    /// <summary>
    /// 获取调试模块。
    /// </summary>
    public static IDebuggerModule Debugger
    {
        get => _debugger ??= Get<IDebuggerModule>();
        private set => _debugger = value;
    }


    private static IDebuggerModule _debugger;

    /// <summary>
    /// 获取有限状态机模块。
    /// </summary>
    public static IFsmModule Fsm => _fsm ??= Get<IFsmModule>();

    private static IFsmModule _fsm;

    /// <summary>
    /// 流程管理模块。
    /// </summary>
    public static IProcedureModule Procedure => _procedure ??= Get<IProcedureModule>();

    private static IProcedureModule _procedure;

    /// <summary>
    /// 获取资源模块。
    /// </summary>
    public static IResourceModule Resource => _resource ??= Get<IResourceModule>();

    private static IResourceModule _resource;

    /// <summary>
    /// 获取音频模块。
    /// </summary>
    public static IAudioModule Audio => _audio ??= Get<IAudioModule>();

    private static IAudioModule _audio;

    /// <summary>
    /// 获取UI模块。
    /// </summary>
    public static UIModule UI => _ui ??= UIModule.Instance;

    private static UIModule _ui;

    /// <summary>
    /// 获取场景模块。
    /// </summary>
    public static ISceneModule Scene => _scene ??= Get<ISceneModule>();

    private static ISceneModule _scene;

    /// <summary>
    /// 获取计时器模块。
    /// </summary>
    public static ITimerModule Timer => _timer ??= Get<ITimerModule>();

    private static ITimerModule _timer;

    /// <summary>
    /// 获取本地化模块。
    /// </summary>
    public static ILocalizationModule Localization => _localization ??= Get<ILocalizationModule>();
    
    private static ILocalizationModule _localization;

    /// <summary>
    /// 获取战斗模块（task 2.7 缓存访问）。
    /// </summary>
    /// <remarks>
    /// <para><b>缓存访问（task 2.7）：</b></para>
    /// <para>通过 <see cref="Get{T}"/> 缓存 <c>ModuleSystem.GetModule&lt;IBattleModule&gt;()</c>
    /// 的返回值，避免重复查找。BattleModule 由 <c>HotFixModules.Register</c> 在组合根
    /// 显式注册一次（task 2.7 唯一注册入口），此处仅做延迟缓存访问，不调用
    /// <c>ModuleSystem.RegisterModule</c>。</para>
    /// <para>对应 specs/battle-hotfix-integration "Battle module registration is idempotent"：
    /// 组合根注册后，此处访问返回同一注册实例。</para>
    /// </remarks>
    public static IBattleModule Battle => _battle ??= Get<IBattleModule>();

    private static IBattleModule _battle;
    #endregion
    
    /// <summary>
    /// 获取游戏框架模块类。
    /// </summary>
    /// <typeparam name="T">游戏框架模块类。</typeparam>
    /// <returns>游戏框架模块实例。</returns>
    private static T Get<T>() where T : class
    {
        T module = ModuleSystem.GetModule<T>();

        Log.Assert(condition: module != null, $"{typeof(T)} is null");

        return module;
    }
    
    public static void Shutdown()
    {
        Log.Info("GameModule Shutdown");
            
        _base = null;
        _debugger = null;
        _fsm = null;
        _procedure = null;
        _resource = null;
        _audio = null;
        _ui = null;
        _scene = null;
        _timer = null;
        _localization = null;
        // task 2.7：清理 BattleModule 缓存引用。
        // ModuleSystem.Shutdown 会逆序调用各模块 Shutdown（含 BattleModule.Shutdown），
        // 此处只清空缓存访问引用，不重复释放模块内部资源。
        _battle = null;
    }
}