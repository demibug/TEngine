using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
using GameLogic.UI.FGUI.Imp;
#if ENABLE_OBFUZ
using Obfuz;
#endif
using TEngine;
using UnityEngine;
#pragma warning disable CS0436


/// <summary>
/// 游戏App。
/// </summary>
#if ENABLE_OBFUZ
[ObfuzIgnore(ObfuzScope.TypeName | ObfuzScope.MethodName)]
#endif
public partial class GameApp
{
    private static List<Assembly> _hotfixAssembly;
    private static bool _releaseStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewSession()
    {
        _hotfixAssembly = null;
        _releaseStarted = false;
    }

    /// <summary>
    /// 热更域App主入口。
    /// </summary>
    /// <param name="objects"></param>
    public static void Entrance(object[] objects)
    {
        if (!ModuleSystem.IsRunning)
        {
            Log.Warning("GameApp entrance ignored because the module system is shutting down.");
            return;
        }

        TEngine.GameEventHelper.Init();
        _hotfixAssembly = (List<Assembly>)objects[0];
        Log.Warning("======= 看到此条日志代表你成功运行了热更新代码 =======");
        Log.Warning("======= Entrance GameApp =======");
        RootModule.BeforeShutdown += Release;
        Utility.Unity.AddDestroyListener(Release);
        Log.Warning("======= StartGameLogic =======");
        StartGameLogic();
    }
    
    private static void StartGameLogic()
    {
        // GameEvent.Get<ILoginUI>().ShowLoginUI();
        GameModule.UI.ShowUIAsync<BattleMainUI>();
        InitializeFairyGuiAsync().Forget();
    }

    /// <summary>
    /// Explicit FairyGUI initialization point. Failure is observed here so UGUI remains available.
    /// </summary>
    public static async UniTask InitializeFairyGuiAsync()
    {
        try
        {
            await FguiSampleRegistration.ShowCoexistenceSampleAsync();
            Log.Info("FairyGUI integration initialized and coexistence sample opened.");
        }
        catch (Exception exception)
        {
            Log.Error("FairyGUI initialization or sample opening failed; UGUI remains active. {0}", exception);
        }
    }
    
    private static void Release()
    {
        if (_releaseStarted)
        {
            return;
        }

        _releaseStarted = true;
        RootModule.BeforeShutdown -= Release;
        try
        {
            SingletonSystem.Release();
        }
        catch (Exception exception)
        {
            LogErrorSafely("GameApp singleton release failed: {0}", exception);
        }

        try
        {
            GameModule.Shutdown();
        }
        catch (Exception exception)
        {
            LogErrorSafely("GameApp module cache cleanup failed: {0}", exception);
        }

        _hotfixAssembly = null;
        try
        {
            Log.Warning("======= Release GameApp =======");
        }
        catch
        {
        }
    }

    private static void LogErrorSafely(string format, Exception exception)
    {
        try { Log.Error(format, exception); }
        catch { }
    }
}
