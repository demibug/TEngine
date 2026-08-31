using System;
using Cysharp.Threading.Tasks;
using GameCommon.Battle;
using GameBattle.Weapon;

namespace GameBattle
{
    // ============================================================================
    // BattleModuleState 枚举
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / specs/battle-runtime-lifecycle/spec.md）：
    //   定义战斗模块的生命周期状态。迁移表由 task 2.5 在 BattleModuleState.cs
    //   中实现为合法迁移校验逻辑。本枚举在此文件定义，因为它是 IBattleModule
    //   只读状态查询的返回类型，也是 BattleOperationResult 的字段类型。
    //
    //   迁移规则（决策 0.7 + spec "Battle module exposes one authoritative lifecycle"）：
    //     Idle → Entering → Running → Settling
    //     Settling → Restarting → Entering
    //     活动状态（Entering/Running/Settling/Restarting）→ Exiting → Idle
    //     任意状态 → Faulted（意外失败）
    //     Faulted → Idle（清理后）
    //
    //   状态语义：
    //     Idle       — 空闲，可接受 Start；无活动运行时。
    //     Entering   — 加载中，配置/Scene/UI/资源初始化进行中。
    //     Running    — 运行中，BattleSimulation 正在推进子步。
    //     Settling   — 结算中，结果已冻结，BattleSimulation 不再推进，执行静默清理。
    //     Restarting — 重开中，旧运行时已销毁，正在创建新运行时。
    //     Exiting    — 退出中，正在销毁运行时并释放战斗专属宿主资源。
    //     Faulted    — 故障，意外失败后需先清理才能回到 Idle。
    // ============================================================================

    /// <summary>
    /// 战斗模块生命周期状态（task 2.4 定义，task 2.5 实现迁移表）。
    /// </summary>
    /// <remarks>
    /// <para>迁移规则（决策 0.7）：</para>
    /// <list type="bullet">
    /// <item><see cref="Idle"/> → <see cref="Entering"/> → <see cref="Running"/> → <see cref="Settling"/></item>
    /// <item><see cref="Settling"/> → <see cref="Restarting"/> → <see cref="Entering"/></item>
    /// <item>活动状态 → <see cref="Exiting"/> → <see cref="Idle"/></item>
    /// <item>任意状态 → <see cref="Faulted"/>（意外失败）；<see cref="Faulted"/> → <see cref="Idle"/>（清理后）</item>
    /// </list>
    /// <para>Start 只允许 <see cref="Idle"/>（重复 Start 返回 <see cref="BattleErrorCode.AlreadyActive"/>）；
    /// Restart 只允许 <see cref="Settling"/>；Exit 在任意状态幂等（决策 0.7）。</para>
    /// </remarks>
    public enum BattleModuleState
    {
        /// <summary>空闲：无活动运行时，可接受 Start。</summary>
        Idle = 0,

        /// <summary>加载中：配置/Scene/UI/资源初始化进行中。</summary>
        Entering = 1,

        /// <summary>运行中：BattleSimulation 正在推进子步。</summary>
        Running = 2,

        /// <summary>结算中：结果已冻结，BattleSimulation 不再推进，执行静默清理。</summary>
        Settling = 3,

        /// <summary>重开中：旧运行时已销毁，正在创建新运行时。</summary>
        Restarting = 4,

        /// <summary>退出中：正在销毁运行时并释放战斗专属宿主资源。</summary>
        Exiting = 5,

        /// <summary>故障：意外失败，需先清理才能回到 Idle。</summary>
        Faulted = 6,
    }

    // ============================================================================
    // IBattleModule 接口
    // ----------------------------------------------------------------------------
    // 职责（design.md / specs/battle-runtime-lifecycle/spec.md）：
    //   GameBattle 对外唯一战斗模块入口契约。定义 Start/Restart/Exit 公共异步 API
    //   和只读状态查询。实现由 BattleModule.cs（task 2.6/2.7）提供。
    //
    //   公共异步 API 使用 UniTask（CLAUDE.md 红线：异步优先）。
    //   预期失败返回 BattleOperationResult 而非异常（spec: "预期失败 MUST 返回结构化结果"）。
    //
    //   不修改 TEngine ModuleSystem 公共实现（task 2.7 约束）。
    // ============================================================================

    /// <summary>
    /// 战斗模块对外唯一入口契约（task 2.4）。
    /// </summary>
    /// <remarks>
    /// <para><b>设计依据（design.md 决策 1 + specs/battle-runtime-lifecycle/spec.md）：</b></para>
    /// <para>GameBattle 对外只通过 <c>IBattleModule</c> 暴露开始/重开/退出命令与只读状态查询。
    /// <c>BattleModule</c> 是 TEngine 长期存在的模块和唯一外部入口（design.md 第 3 节），
    /// 每局可变状态属于可销毁的 <c>BattleRuntime</c>，不在本接口层暴露。</para>
    ///
    /// <para><b>公共异步 API 语义（决策 0.7 + spec "Battle module exposes one authoritative lifecycle"）：</b></para>
    /// <list type="bullet">
    /// <item><see cref="StartAsync"/>：只允许 <see cref="BattleModuleState.Idle"/> 状态调用；
    /// 重复 Start 返回 <see cref="BattleErrorCode.AlreadyActive"/> 且不创建第二个活动运行时。</item>
    /// <item><see cref="RestartAsync"/>：只允许 <see cref="BattleModuleState.Settling"/> 状态调用；
    /// 其他状态返回结构化失败结果，不销毁当前运行时。</item>
    /// <item><see cref="ExitAsync"/>：在任意状态幂等；活动状态退出返回成功，重复 Exit 在
    /// 退出中返回 <see cref="BattleErrorCode.Exiting"/> 且不重复执行清理。</item>
    /// <item>预期失败（状态、配置、资源）返回 <see cref="BattleOperationResult"/> 而非异常。</item>
    /// </list>
    ///
    /// <para><b>只读状态查询：</b></para>
    /// <para><see cref="State"/> 返回当前模块状态快照，供调用方做后续决策。
    /// 该属性为只读，调用方无法通过它修改模块状态。</para>
    ///
    /// <para><b>不修改 TEngine ModuleSystem：</b></para>
    /// <para>本接口由 <c>BattleModule</c>（继承 TEngine <c>Module</c>）实现，
    /// 在 GameLogic 组合根通过 <c>ModuleSystem.RegisterModule&lt;IBattleModule&gt;</c> 注册（task 2.7）。
    /// 不修改 TEngine <c>ModuleSystem</c> 公共实现。</para>
    /// </remarks>
    public interface IBattleModule
    {
        /// <summary>与本模块同生命周期的局外武器业务对象。</summary>
        WeaponManager Weapon { get; }

        /// <summary>
        /// 当前模块生命周期状态（只读查询）。
        /// </summary>
        /// <remarks>
        /// 返回当前状态快照，供调用方判断可执行的操作（如 Idle 才能 Start，Settling 才能 Restart）。
        /// 该属性为只读，调用方无法通过它修改模块状态。
        /// </remarks>
        BattleModuleState State { get; }

        /// <summary>
        /// 显示战斗入口界面，不启动战斗运行时。
        /// </summary>
        /// <param name="loadout">供入口界面冻结持有的初始装载信息。</param>
        /// <returns>入口窗口完成打开后的异步任务。</returns>
        UniTask ShowEntryAsync(BattleLoadoutDto loadout);

        /// <summary>
        /// 开始一局战斗（公共异步 API）。
        /// </summary>
        /// <param name="loadout">不可变战斗装载信息（地图、种子、配置版本/hash 占位、牌组预设）。</param>
        /// <returns>
        /// 结构化操作结果。成功时 <see cref="BattleOperationResult.IsSuccess"/> 为 true 且状态为
        /// <see cref="BattleModuleState.Running"/>；预期失败返回错误码（如
        /// <see cref="BattleErrorCode.AlreadyActive"/>、<see cref="BattleErrorCode.ConfigMissing"/>、
        /// <see cref="BattleErrorCode.AssetLoadFailed"/> 等）。
        /// </returns>
        /// <remarks>
        /// <para><b>状态约束（spec "Duplicate start returns AlreadyActive"）：</b></para>
        /// <list type="bullet">
        /// <item>只允许在 <see cref="BattleModuleState.Idle"/> 状态调用。</item>
        /// <item>战斗正在加载（Entering）或运行（Running）时再次调用返回
        /// <see cref="BattleErrorCode.AlreadyActive"/>，不创建第二个活动运行时，不改变当前运行时状态。</item>
        /// <item>加载失败（配置/Scene/UI/资源）执行反向回滚，返回对应错误码，模块回到 Idle 或 Faulted。</item>
        /// </list>
        /// </remarks>
        UniTask<BattleOperationResult> StartAsync(BattleLoadoutDto loadout);

        /// <summary>
        /// 再来一局（公共异步 API）。
        /// </summary>
        /// <param name="loadout">新局的不可变战斗装载信息。</param>
        /// <returns>
        /// 结构化操作结果。成功时状态为 <see cref="BattleModuleState.Running"/>；
        /// 非 Settling 状态调用返回 <see cref="BattleErrorCode.NotSettling"/>。
        /// </returns>
        /// <remarks>
        /// <para><b>状态约束（spec "Restart rejected outside settling" + 决策 0.7）：</b></para>
        /// <list type="bullet">
        /// <item>只允许在 <see cref="BattleModuleState.Settling"/> 状态调用。</item>
        /// <item>在 Running、Entering 等非 Settling 状态调用返回
        /// <see cref="BattleErrorCode.NotSettling"/>，不销毁当前运行时，不创建新运行时。</item>
        /// <item>成功时销毁旧运行时的全部可变状态和局部订阅，再使用新装载创建新运行时
        /// （spec "Restart creates clean per-battle state"）。</item>
        /// <item>允许复用的资源和池与单局状态分离（design.md 决策 0.2）。</item>
        /// </list>
        /// </remarks>
        UniTask<BattleOperationResult> RestartAsync(BattleLoadoutDto loadout);

        /// <summary>
        /// 退出战斗、返回主界面（公共异步 API）。
        /// </summary>
        /// <returns>
        /// 结构化操作结果。成功时状态为 <see cref="BattleModuleState.Idle"/>。
        /// Exit 在任意状态幂等；Idle 状态退出返回成功，活动状态退出执行一次清理后回到 Idle，
        /// 退出中（Exiting）重复调用返回 <see cref="BattleErrorCode.Exiting"/> 且不重复执行清理。
        /// </returns>
        /// <remarks>
        /// <para><b>状态约束（spec "Exit is idempotent"）：</b></para>
        /// <list type="bullet">
        /// <item>在任意状态调用均安全，Exit 是幂等操作。</item>
        /// <item>活动状态（Entering/Running/Settling/Restarting）调用立即迁移到 Exiting，
        /// 只执行一次清理后回到 Idle。</item>
        /// <item>退出中（Exiting）重复调用返回 <see cref="BattleErrorCode.Exiting"/>，不重复清理。</item>
        /// <item>退出完成后（Idle）再次调用返回成功结果。</item>
        /// <item>退出时停止逻辑推进、关闭战斗 UI、销毁当前运行时，按所有权规则释放战斗资源
        /// （spec "Exit releases battle-owned state"）。</item>
        /// </list>
        /// </remarks>
        UniTask<BattleOperationResult> ExitAsync();
    }
}
