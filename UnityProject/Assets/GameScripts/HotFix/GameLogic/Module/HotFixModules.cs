using GameBattle;
using GameFUI;
using GameWeapon;
using TEngine;

// ============================================================================
// 任务 2.7：HotFixModules —— GameLogic 唯一组合根的战斗模块注册入口
// ----------------------------------------------------------------------------
// 职责（design.md 第 1 节 / specs/battle-hotfix-integration/spec.md）：
//   本文件是 GameLogic 组合根中唯一允许调用 ModuleSystem.RegisterModule 的地方。
//   BattleModule 在此显式注册且只注册一次，保证 specs/battle-hotfix-integration 中
//   "Battle module registration is idempotent" 要求：重复入口不得产生重复更新、
//   重复监听或不可达旧模块实例。
//
//   约束（task 2.7）：
//     1. 只有组合根允许调用 ModuleSystem.RegisterModule，不得修改 TEngine
//        ModuleSystem 公共实现。
//     2. 不修改 GameBattle / GameCommon / GameProto 目录下任何文件。
//     3. GameLogic 最后加载，无程序集循环引用（GameLogic.asmdef 引用 GameBattle）。
//
//   调用时机：由 GameApp.StartGameLogic 在 GameEventHelper.Init 之后、任何战斗 UI
//   或 GameModule.Battle 访问之前调用一次（见 GameApp.cs）。
// ============================================================================

/// <summary>
/// GameLogic 组合根的战斗模块注册入口（task 2.7）。
/// </summary>
/// <remarks>
/// <para><b>唯一注册入口（task 2.7 约束）：</b></para>
/// <para>本类是组合根中唯一允许调用 <c>ModuleSystem.RegisterModule</c> 的地方。
/// BattleModule 在此显式注册且只注册一次，满足
/// specs/battle-hotfix-integration "Battle module registration is idempotent"：
/// 重复调用 <see cref="Register"/> 直接返回已注册实例，不产生重复更新、重复监听
/// 或不可达旧模块实例。</para>
///
/// <para><b>不修改 TEngine ModuleSystem（task 2.7 约束）：</b></para>
/// <para>直接使用 TEngine <c>ModuleSystem.RegisterModule&lt;T&gt;</c> 公共 API，
/// 不修改其公共实现。BattleModule 继承 TEngine <c>Module</c> 并实现
/// <see cref="IBattleModule"/>，注册后由 ModuleSystem 调用其 <c>OnInit</c>。</para>
///
/// <para><b>程序集拓扑（design.md 决策 1.7）：</b></para>
/// <para>GameLogic.asmdef 引用 GameBattle.asmdef，GameLogic 最后加载，无循环引用。
/// 本类所在 GameLogic 程序集可访问 GameBattle 的 public 类型
/// <see cref="BattleModule"/> 与 <see cref="IBattleModule"/>。</para>
/// </remarks>
public static class HotFixModules
{
    /// <summary>
    /// 已注册的 BattleModule 实例缓存。null 表示尚未注册。
    /// <para>用于幂等保护：重复调用 <see cref="Register"/> 时直接返回该实例，
    /// 不再次调用 ModuleSystem.RegisterModule。</para>
    /// </summary>
    private static IBattleModule _battleModule;

    /// <summary>
    /// 已登记到 ModuleSystem 的 GameFUI 模块。
    /// </summary>
    private static IFUIModule _fuiModule;
    private static IWeaponModule _weaponModule;

    /// <summary>
    /// 显式注册一次 BattleModule（task 2.7 唯一注册入口）。
    /// </summary>
    /// <returns>已注册的 <see cref="IBattleModule"/> 实例。</returns>
    /// <remarks>
    /// <para><b>幂等性（spec "Battle module registration is idempotent"）：</b></para>
    /// <para>若已注册则直接返回缓存实例，不重复调用
    /// <c>ModuleSystem.RegisterModule</c>，避免产生重复更新或不可达旧模块实例。
    /// 满足 specs/battle-hotfix-integration "Registration is invoked twice" 场景：
    /// 系统仍只有一个 BattleModule 接收帧更新且只执行一次初始化。</para>
    ///
    /// <para><b>注册流程：</b></para>
    /// <list type="bullet">
    /// <item>创建 <see cref="BattleModule"/> 实例（无参构造，task 2.6 提供）。</item>
    /// <item>通过 <c>ModuleSystem.RegisterModule&lt;IBattleModule&gt;</c> 注册，
    /// ModuleSystem 内部调用 <c>OnInit</c> 并登记到更新队列。</item>
    /// <item>缓存返回的实例供 <see cref="GameModule.Battle"/> 延迟访问。</item>
    /// </list>
    ///
    /// <para><b>调用时机：</b></para>
    /// <para>由 GameApp.StartGameLogic 在 GameEventHelper.Init 之后、任何战斗 UI 或
    /// GameModule.Battle 访问之前调用一次。</para>
    /// </remarks>
    public static IBattleModule Register()
    {
        // 幂等保护：已注册则直接返回缓存实例，不重复注册。
        if (_battleModule != null)
        {
            Log.Warning("[HotFixModules] BattleModule 已注册，跳过重复注册。");
            return _battleModule;
        }

        // 生产装配顺序固定为 GameFUI -> GameBattle -> FreezeBindings。
        // FUIModule 先登记到 ModuleSystem，使全局 Shutdown 按逆序先清理
        // BattleModule 所有的战斗窗口，再清理 GameFUI 基础设施。
        FUI.RegisterModule(GameModule.Resource, new FUIOptions
        {
            DesignWidth = 720,
            DesignHeight = 1280,
            ScreenMatchMode = FUIScreenMatchMode.MatchWidthOrHeight,
        });
        _fuiModule = ModuleSystem.RegisterModule<IFUIModule>((Module)FUI.Module);

        // 武器进度必须先于 BattleModule 监听战斗事件，确保开局装载与结算奖励闭环。
        _weaponModule = ModuleSystem.RegisterModule<IWeaponModule>(new WeaponModule());

        // 创建 BattleModule 实例。OnInit 由唯一 BattleModule 注册
        // UIBattle Binder、最终 Widget 与最终 Window。
        BattleModule module = new BattleModule();

        // 唯一允许调用 ModuleSystem.RegisterModule 的地方（task 2.7 约束）。
        // 注册后 ModuleSystem 调用 module.OnInit()。
        _battleModule = ModuleSystem.RegisterModule<IBattleModule>(module);

        // 所有业务 owner 注册完成后由组合根统一冻结；
        // GameLogic 不枚举任何具体战斗窗口。
        _fuiModule.FreezeBindings();

        Log.Info("[HotFixModules] GameFUI 与 BattleModule 注册完成，Registry 已冻结。");
        return _battleModule;
    }
}
