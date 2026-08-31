using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameCommon.Battle;
using GameLogic;
#if ENABLE_OBFUZ
using Obfuz;
#endif
using TEngine;
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

    /// <summary>
    /// 热更域App主入口。
    /// </summary>
    /// <param name="objects"></param>
    public static void Entrance(object[] objects)
    {
        GameEventHelper.Init();
        _hotfixAssembly = (List<Assembly>)objects[0];
        Log.Warning("======= 看到此条日志代表你成功运行了热更新代码 =======");
        Log.Warning("======= Entrance GameApp =======");

        // task 1.11 / task 3.4：在 ProcedurePreload 完成 PRELOAD 预加载后，
        // 由热更域入口显式调用 ConfigSystem.Load()，构造 Luban Tables。
        // 必须在任何 BattleModule 配置访问前完成（battle-config-snapshot spec
        // "Runtime consumes an immutable configuration snapshot"），
        // 且 BattleSimulation 子步不得触发同步 IO（ConfigSystem.Tables getter
        // 已硬化为未 Load 时抛异常）。此调用在 GameEventHelper.Init() 之后、
        // StartGameLogic() 之前完成，保证组合根注册 BattleModule 前配置已就绪。
        ConfigSystem.Instance.Load();

        Utility.Unity.AddDestroyListener(Release);
        Log.Warning("======= StartGameLogic =======");
        StartGameLogic();
    }
    
    private static void StartGameLogic()
    {
        // task 2.7：在 GameLogic 唯一组合根显式注册一次 BattleModule。
        // 必须在任何战斗 UI 或 GameModule.Battle 访问之前调用，且只有 HotFixModules.Register
        // 允许调用 ModuleSystem.RegisterModule（task 2.7 约束）。
        HotFixModules.Register();

        // GameEvent.Get<ILoginUI>().ShowLoginUI();
        ShowBattleEntryAsync().Forget();
    }

    /// <summary>
    /// 在业务模块注册完成后显示 FairyGUI 战斗入口。
    /// </summary>
    private static async UniTask ShowBattleEntryAsync()
    {
        try
        {
            BattleWeaponLoadoutDto weapons = GameModule.Battle.Weapon.CreateBattleLoadout();
            await GameModule.Battle.ShowEntryAsync(
                BattleLoadoutDto.CreateLocalAiDefault(weapons: weapons));
        }
        catch (System.Exception ex)
        {
            Log.Error($"[GameApp] 打开 BattleStartPanel 失败：{ex}");
        }
    }
    
    private static void Release()
    {
        SingletonSystem.Release();
        Log.Warning("======= Release GameApp =======");
    }
}
