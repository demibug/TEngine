using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameCommon.Battle;
using GameFUI;
using GameBattle.Weapon;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 2.6：BattleModule —— Start/Restart/Exit 生命周期实现
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1/3 节 / specs/battle-runtime-lifecycle/spec.md）：
    //   BattleModule 是 TEngine 长期存在的模块和唯一外部入口（design 决策 1）。
    //   本文件实现 IBattleModule 定义的三条公共异步命令（Start/Restart/Exit），
    //   并保证以下不变量：
    //
    //   1. 状态防重：生命周期状态在第一次 await 前同步更新。
    //   2. 重复语义（决策 0.7）：
    //      - Start 只允许 Idle；重复 Start 返回 AlreadyActive，不创建第二个运行时。
    //      - Restart 只允许 Settling；其他状态返回 NotSettling，不销毁当前运行时。
    //      - Exit 在任意状态幂等；Exiting 中重复调用直接返回 Exiting。
    //   3. 加载失败执行反向回滚（spec "Partial initialization is recoverable"）：
    //      使用 BattleRuntimeScope 跟踪进入步骤，失败时逆序释放。
    //   4. Faulted 必须先清理才能回到 Idle（决策 0.7）：
    //      Faulted → Idle 由 Exit 或显式清理完成，不允许直接 Start/Restart。
    //   5. 使用 BattleModuleStateTransitions.CanTransition 校验所有状态迁移。
    //
    //   BattleRuntime / BattleRuntimeFactory 尚未实现（task 2.9/2.10），
    //   当前通过可注入的加载/清理委托抽象出进入与退出步骤，使生命周期门控逻辑
    //   可独立测试。后续 task 接入具体 Factory 时只需替换委托实现。
    //
    //   不修改 TEngine ModuleSystem 公共实现（task 2.7 约束）。
    // ============================================================================

    /// <summary>
    /// 战斗模块实现：TEngine 长期存在的模块和 GameBattle 对外唯一外部入口。
    /// </summary>
    /// <remarks>
    /// <para><b>继承关系（design.md 决策 1）：</b></para>
    /// <para>继承 TEngine <see cref="Module"/> 并实现 <see cref="IBattleModule"/>。
    /// 由 GameLogic 组合根通过 <c>ModuleSystem.RegisterModule&lt;IBattleModule&gt;</c>
    /// 注册（task 2.7），不修改 TEngine <c>ModuleSystem</c> 公共实现。</para>
    ///
    /// <para><b>状态防重（task 2.6 核心）：</b></para>
    /// <para>Start/Restart/Exit 在第一次 await 前同步更新生命周期状态；重复按钮调用读取现有状态后
    /// 直接返回结构化结果，不需要锁或命令队列。</para>
    ///
    /// <para><b>重复语义（决策 0.7）：</b></para>
    /// <list type="bullet">
    /// <item><see cref="StartAsync"/> 只允许 <see cref="BattleModuleState.Idle"/>；
    /// 重复 Start 返回 <see cref="BattleErrorCode.AlreadyActive"/>。</item>
    /// <item><see cref="RestartAsync"/> 只允许 <see cref="BattleModuleState.Settling"/>；
    /// 其他状态返回 <see cref="BattleErrorCode.NotSettling"/>。</item>
    /// <item><see cref="ExitAsync"/> 在任意状态幂等；Exiting 中重复调用返回
    /// <see cref="BattleErrorCode.Exiting"/>。</item>
    /// </list>
    ///
    /// <para><b>加载失败回滚（spec "Partial initialization is recoverable"）：</b></para>
    /// <para>进入战斗的每个步骤都登记到 <see cref="BattleRuntimeScope"/>，
    /// 任一步骤失败时逆序释放已完成部分，不留下半初始化运行时。</para>
    /// </remarks>
    public sealed class BattleModule : Module, IBattleModule, IUpdateModule
    {
        // ====================================================================
        // 可注入的加载/清理委托
        // ----------------------------------------------------------------
        // BattleRuntime / BattleRuntimeFactory 尚未实现（task 2.9/2.10），
        // 当前通过委托抽象出进入与退出步骤。后续接入具体 Factory 时只需替换委托。
        // 测试可通过注入模拟委托验证生命周期门控逻辑，不依赖真实运行时。
        // ====================================================================

        /// <summary>
        /// 进入战斗的加载步骤委托。
        /// </summary>
        /// <param name="loadout">不可变战斗装载信息。</param>
        /// <param name="scope">本局运行时所有权作用域，用于登记部分初始化步骤。</param>
        /// <returns>结构化操作结果。成功时状态应为 Running。</returns>
        internal delegate UniTask<BattleOperationResult> BattleEntryHandler(
            BattleLoadoutDto loadout,
            BattleRuntimeScope scope);

        /// <summary>
        /// 退出战斗的清理步骤委托。
        /// </summary>
        /// <param name="scope">本局运行时所有权作用域（可能为 null，表示无活动运行时）。</param>
        /// <returns>结构化操作结果。成功时状态应为 Idle。</returns>
        internal delegate UniTask<BattleOperationResult> BattleExitHandler(BattleRuntimeScope scope);

        // ====================================================================
        // 内部状态
        // ====================================================================

        /// <summary>
        /// 当前模块生命周期状态。
        /// <para>所有读写都发生在 Unity 主线程，操作入口在第一次 await 前同步更新状态。</para>
        /// </summary>
        private BattleModuleState _state = BattleModuleState.Idle;

        /// <summary>
        /// 当前活动运行时的所有权作用域。
        /// <para>null 表示无活动运行时（Idle 或退出后）。
        /// 非 null 表示正在进入、运行、结算或重开中。</para>
        /// </summary>
        private BattleRuntimeScope _activeScope;

        /// <summary>
        /// 当前活动战斗运行时（task 6.10 闭环接入）。
        /// <para>null 表示无活动运行时。Running 状态下由 <see cref="Update"/> 每帧驱动
        /// <see cref="BattleRuntime.Advance"/> 推进模拟。Settling/Exiting 时停止驱动。
        /// 重开/退出时由 Dispose 销毁旧 Runtime。</para>
        /// </summary>
        private BattleRuntime _activeRuntime;

        /// <summary>
        /// 跨局战斗对象池作用域。BattleModule 是唯一所有者：重开保留已清空容量，
        /// 退出、失败回滚和 Shutdown 清空全部容量。
        /// </summary>
        private readonly BattlePoolScope _poolScope = new BattlePoolScope();

        /// <summary>模块级战斗世界宿主，静态地图不进入单局 Scope。</summary>
        private readonly BattleWorldHost _worldHost = new BattleWorldHost();

        /// <summary>
        /// 与当前入口 Loadout 对应的已准备启动上下文。
        /// <para>Context 仅在 Loadout 完全匹配时复用；退出、失败或显示新入口时清空
        /// （design.md 决策 3）。</para>
        /// </summary>
        private BattleStartupContext _activeContext;

        /// <summary>模块已关闭；迟到的异步结果不得再提交。</summary>
        private bool _isShutdown;

        /// <summary>入口窗口正在准备/打开；重复点击直接忽略。</summary>
        private bool _isOpeningEntry;

        /// <summary>入口世界准备的幂等任务；失败后清空以允许重试。</summary>
        private AsyncLazy<UnityEngine.GameObject> _entryPreparationTask;

        /// <summary>入口世界准备任务对应的配置快照（用于识别是否仍匹配当前地图）。</summary>
        private BattleConfigSnapshot _preparationSnapshot;

        /// <summary>最近一次入口装载，用于退出后重新显示入口。</summary>
        private BattleLoadoutDto _lastLoadout;

        /// <summary>与 BattleModule 同生命周期的局外武器业务对象。</summary>
        private WeaponManager _weapon;

        /// <summary>访问局外武器数据和操作。</summary>
        public WeaponManager Weapon =>
            _weapon ?? throw new InvalidOperationException("武器系统尚未初始化");

        /// <summary>
        /// 进入战斗的加载步骤委托。
        /// <para>默认实现直接返回成功（用于门控逻辑验证）。
        /// 后续 task 2.9/2.10 接入 BattleRuntimeFactory 后替换为真实加载逻辑。</para>
        /// </summary>
        private readonly BattleEntryHandler _entryHandler;

        /// <summary>
        /// 退出战斗的清理步骤委托。
        /// <para>默认实现释放 Scope 并返回成功。
        /// 后续接入真实运行时后替换为完整清理逻辑。</para>
        /// </summary>
        private readonly BattleExitHandler _exitHandler;

        // ====================================================================
        // IBattleModule 只读状态查询
        // ====================================================================

        /// <inheritdoc />
        public BattleModuleState State => _state;

        /// <inheritdoc />
        public async UniTask ShowEntryAsync(BattleLoadoutDto loadout)
        {
            if (_isShutdown || _isOpeningEntry || _state != BattleModuleState.Idle)
            {
                Log.Warning($"[BattleModule] 当前状态为 {_state}，跳过重复打开战斗入口。");
                return;
            }

            _isOpeningEntry = true;
            try
            {
                _lastLoadout = loadout;

                // 新入口（本分支已在 Idle 下执行，无活动 Runtime）：清除上一局残留的
                // Context、入口世界准备缓存与单局 Bindings，再准备新 Context。
                // 非 Idle 状态不进入本分支，因此不会破坏活动 Runtime（design.md 决策 3）。
                _activeContext = null;
                _entryPreparationTask = null;
                _preparationSnapshot = null;
                _worldHost.ClearDynamicRoots();
                _worldHost.ClearBindings();

                // 世界加载前先解析并校验配置（spec "Battle startup is transactional"）。
                // 配置非法时只记录并返回，不加载世界、不显示入口，等待重试。
                BattleStartupContext context = BattleStartupContext.Prepare(loadout);
                if (!context.IsValid)
                {
                    Log.Warning(
                        $"[BattleModule] 打开入口前配置准备失败 code={context.ErrorCode} " +
                        $"msg={context.DiagnosticMessage}（MapId={loadout.MapId}）");
                    _activeContext = null;
                    return;
                }

                _activeContext = context;

                try
                {
                    await EnsureEntryWorldAsync(context.Config);
                }
                catch
                {
                    // 世界准备失败：清空 Context，避免残留不匹配的缓存。
                    _activeContext = null;
                    if (_isShutdown)
                    {
                        return;
                    }

                    throw;
                }

                if (_isShutdown || _state != BattleModuleState.Idle)
                {
                    _activeContext = null;
                    return;
                }

                await FUI.ShowAsync<BattleStartPanel>(
                    new BattleStartEntryArgs(loadout, StartAsync));

                if (_isShutdown || _state != BattleModuleState.Idle)
                {
                    FUI.Close<BattleStartPanel>();
                }
            }
            finally
            {
                _isOpeningEntry = false;
            }
        }

        /// <summary>
        /// 等待模块级地图准备（按当前地图运行快照加载/复用世界）；并发入口复用同一任务。
        /// </summary>
        private async UniTask EnsureEntryWorldAsync(BattleConfigSnapshot config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (_entryPreparationTask == null || !ReferenceEquals(_preparationSnapshot, config))
            {
                _preparationSnapshot = config;
                _entryPreparationTask = UniTask.Lazy(
                    () => _worldHost.EnsureWorldForMapAsync(config.Map));
            }

            try
            {
                UnityEngine.GameObject map = await _entryPreparationTask.Task;
                if (map == null || _worldHost.Bindings == null)
                {
                    throw new InvalidOperationException(
                        $"{config.Map.ResourceAddress} 地图实例或节点绑定无效。");
                }

                _worldHost.HideWorld();
            }
            catch
            {
                _entryPreparationTask = null;
                _preparationSnapshot = null;
                throw;
            }
        }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造 BattleModule，使用默认加载/清理委托。
        /// <para>TEngine ModuleSystem 通过 <see cref="Activator"/> 创建模块实例，
        /// 必须提供无参构造。默认委托仅做最小化处理，后续 task 2.9/2.10 接入
        /// BattleRuntimeFactory 后通过组合根注入真实加载逻辑。</para>
        /// </summary>
        public BattleModule()
            : this(null, null)
        {
        }

        /// <summary>
        /// 构造 BattleModule，注入指定的加载/清理委托（测试与后续 Factory 接入用）。
        /// </summary>
        /// <param name="entryHandler">
        /// 进入战斗的加载步骤委托；为 null 时使用默认实现。
        /// </param>
        /// <param name="exitHandler">
        /// 退出战斗的清理步骤委托；为 null 时使用默认实现。
        /// </param>
        internal BattleModule(BattleEntryHandler entryHandler, BattleExitHandler exitHandler)
        {
            _entryHandler = entryHandler ?? DefaultEntryHandler;
            _exitHandler = exitHandler ?? DefaultExitHandler;
        }

        // ====================================================================
        // 默认加载/清理委托实现
        // ====================================================================

        /// <summary>
        /// 默认进入事务：先解析并校验 Loadout 对应配置（复用/即时准备 Context），
        /// 再按所选地图加载世界与节点绑定，激活战斗世界，严格预加载表现资源，
        /// 组装并启动本局 Runtime，最后打开 BattleHudPanel。
        /// 任一步骤失败均由调用方执行逆序回滚，且不会提交 Running 状态。
        /// </summary>
        private async UniTask<BattleOperationResult> DefaultEntryHandler(
            BattleLoadoutDto loadout,
            BattleRuntimeScope scope)
        {
            _lastLoadout = loadout;

            // 世界加载前解析并校验配置：复用匹配的已准备 Context，否则即时准备
            // （design.md 决策 3）。配置错误在世界加载前阻断，禁止回退 map0。
            BattleStartupContext context = _activeContext;
            if (context == null || !context.IsValid || !context.Matches(loadout))
            {
                context = BattleStartupContext.Prepare(loadout);
                if (!context.IsValid)
                {
                    Log.Warning(
                        $"[BattleModule] 配置准备失败 code={context.ErrorCode} msg={context.DiagnosticMessage}");
                    return BattleOperationResult.Fail(
                        context.ErrorCode,
                        BattleModuleState.Idle,
                        context.DiagnosticMessage,
                        BattleFailureStage.WorldPreparation,
                        resourceAddress: string.Empty,
                        nodePath: $"MapId={loadout.MapId}");
                }

                _activeContext = context;
            }

            try
            {
                await EnsureEntryWorldAsync(context.Config);
            }
            catch (BattleMapResourceAddressException ex)
            {
                // 寻址无效（空地址、未收集或不存在）：映射 AssetMissing（design.md 决策 6）。
                return BattleOperationResult.Fail(
                    BattleErrorCode.AssetMissing,
                    BattleModuleState.Idle,
                    $"战斗地图资源地址无效，无法加载世界: {ex.Message}",
                    BattleFailureStage.WorldPreparation,
                    ex.ResourceAddress,
                    exception: ex);
            }
            catch (BattleMapLoadException ex)
            {
                // 地址有效但加载失败：映射 AssetLoadFailed。
                return BattleOperationResult.Fail(
                    BattleErrorCode.AssetLoadFailed,
                    BattleModuleState.Idle,
                    $"准备战斗地图失败: {ex.Message}",
                    BattleFailureStage.WorldPreparation,
                    ex.ResourceAddress,
                    exception: ex);
            }
            catch (BattleMapBindingException ex)
            {
                // 节点/运行几何绑定失败：阶段固定 MapBinding；
                // 缺节点映射 AssetMissing，重复/几何无效映射 PartialInitializationFailed。
                return BattleOperationResult.Fail(
                    ex.IsMissingNode
                        ? BattleErrorCode.AssetMissing
                        : BattleErrorCode.PartialInitializationFailed,
                    BattleModuleState.Idle,
                    $"战斗地图节点绑定失败: {ex.Message}",
                    BattleFailureStage.MapBinding,
                    ex.ResourceAddress,
                    ex.NodePath,
                    exception: ex);
            }
            catch (Exception ex)
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.AssetLoadFailed,
                    BattleModuleState.Idle,
                    $"准备战斗地图失败: {ex.Message}",
                    BattleFailureStage.WorldPreparation,
                    context.Config.Map.ResourceAddress,
                    exception: ex);
            }

            if (!CanCommitEntry())
            {
                return CreateStaleEntryResult("地图准备完成时战斗入口已失效。");
            }

            BattleMapBindings bindings = _worldHost.Bindings;
            if (bindings == null)
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.AssetMissing,
                    BattleModuleState.Idle,
                    "地图节点绑定不存在。",
                    BattleFailureStage.MapBinding,
                    context.Config.Map.ResourceAddress,
                    context.Config.Map.Name);
            }

            try
            {
                _worldHost.ClearDynamicRoots();
                _worldHost.ApplyBattleCamera();
                _worldHost.ActivateWorld();
            }
            catch (Exception ex)
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.PartialInitializationFailed,
                    BattleModuleState.Idle,
                    $"激活战斗世界失败: {ex.Message}",
                    BattleFailureStage.CameraSetup,
                    context.Config.Map.ResourceAddress,
                    exception: ex);
            }

            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout,
                _poolScope,
                bindings,
                context.Config);
            if (!assembly.IsSuccess)
            {
                Log.Warning(
                    $"[BattleModule] BattleRuntimeFactory.Create 失败 code={assembly.ErrorCode} msg={assembly.DiagnosticMessage}");
                return BattleOperationResult.Fail(
                    assembly.ErrorCode,
                    BattleModuleState.Idle,
                    assembly.DiagnosticMessage,
                    BattleFailureStage.RuntimeCreate);
            }

            _worldHost.ShowPlacementSlots(
                assembly.InputController.MapState,
                playerSide: true);

            assembly.SignalHub.TileOpened.Subscribe(fact =>
                _worldHost.ShowPlacementSlots(assembly.InputController.MapState, playerSide: true));

            IBattleViewPort viewPort = assembly.ViewPort;
            IBattleAudioPort audioPort = assembly.AudioPort;
            IBattleVfxPort vfxPort = assembly.VfxPort;

            try
            {
                // 只加载所选计划实际引用的普通敌人/Boss 地址；Null 端口允许纯逻辑运行，
                // 生产端口对空 Boss resourcePath 明确失败，不创建占位表现。
                IReadOnlyList<string> enemyResourceAddresses =
                    BattlePresentationResourcePlan.CollectEnemyResourceAddresses(
                        context.Config,
                        requireBossPresentation: !(viewPort is NullBattleViewPort));
                IReadOnlyList<string> generalResourceAddresses =
                    BattlePresentationResourcePlan.CollectGeneralResourceAddresses(context.Config);
                IReadOnlyList<string> generalPartWords =
                    BattlePresentationResourcePlan.CollectGeneralPartWords(context.Config);
                await UniTask.WhenAll(
                    viewPort.PreloadAsync(
                        enemyResourceAddresses,
                        generalResourceAddresses,
                        generalPartWords),
                    audioPort.PreloadAsync(),
                    vfxPort.PreloadAsync());
            }
            catch (BattlePresentationLoadException ex)
            {
                assembly.Scope.Rollback();
                return BattleOperationResult.Fail(
                    BattleErrorCode.AssetLoadFailed,
                    BattleModuleState.Idle,
                    ex.Message,
                    ex.FailureStage,
                    ex.ResourceAddress,
                    exception: ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                Log.Error($"[BattleModule] 表现端口资源预加载失败: {ex}");
                assembly.Scope.Rollback();
                return BattleOperationResult.Fail(
                    BattleErrorCode.AssetLoadFailed,
                    BattleModuleState.Idle,
                    $"表现端口资源预加载失败: {ex.Message}",
                    BattleFailureStage.PresentationPreload,
                    exception: ex);
            }

            if (!CanCommitEntry())
            {
                assembly.Scope.Rollback();
                return CreateStaleEntryResult("表现资源预加载完成时战斗入口已失效。");
            }

            BattleRuntime runtime = new BattleRuntime(assembly);
            _activeRuntime = runtime;

            try
            {
                runtime.BattleManager.StartGame(startNowMs: 0, spawnStrategyIndex: 0);
                runtime.InputController.StartGame();
                runtime.OpponentAI?.StartGame();
                runtime.Presenter?.NotifyBattleStarted(
                    runtime.BattleState.MaxRounds,
                    runtime.BattleState.PlayerMaxHealth,
                    runtime.BattleState.OpponentMaxHealth);
                runtime.EventBridge?.PublishBattleStarted(loadout);
            }
            catch (Exception ex)
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.PartialInitializationFailed,
                    BattleModuleState.Idle,
                    $"启动战斗服务失败: {ex.Message}",
                    BattleFailureStage.ServiceStart,
                    exception: ex);
            }

            try
            {
                await FUI.ShowAsync<BattleHudPanel>(
                    new BattleHudEntryArgs(
                        ExitAsync,
                        () => runtime.Presenter?.HandleRecruitClick(playerSide: true)
                              ?? BattleInputResult.Fail(0, BattleInputRejectReason.Unknown, "战斗表现层不可用"),
                        (sourceSlotId, targetSlotId) =>
                            runtime.Presenter?.HandleDropUnit(sourceSlotId, targetSlotId)
                            ?? BattleInputResult.Fail(0, BattleInputRejectReason.Unknown, "战斗表现层不可用"),
                        (sourceSlotId, stageX, stageY) =>
                            runtime.Presenter?.HandleUseShovel(sourceSlotId, stageX, stageY)
                            ?? BattleInputResult.Fail(0, BattleInputRejectReason.Unknown, "战斗表现层不可用"),
                        () => runtime.Presenter?.GetSlotSnapshot().GetSlots(isPlayerSide: true, SlotZone.Reserve)
                              ?? (IReadOnlyList<UnitSlot>)Array.Empty<UnitSlot>(),
                        ResolvePlayerBattleSlotForStage,
                        (float stageX, float stageY, out int sourceSlotId) =>
                            ResolvePlayerBattleSourceForStage(stageX, stageY, out sourceSlotId),
                        slotId => runtime.Presenter != null
                            ? runtime.Presenter.GetSlotSnapshot().GetSlotById(slotId)
                            : default,
                        soldierType => (viewPort as UnityBattleViewPort)?.GetUnitIcon(soldierType),
                        partWord => (viewPort as UnityBattleViewPort)?.GetGeneralPartIcon(partWord),
                        propType => (viewPort as UnityBattleViewPort)?.GetPropIcon(propType)));
            }
            catch (Exception ex)
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.UILoadFailed,
                    BattleModuleState.Idle,
                    $"打开 BattleHudPanel 失败: {ex.Message}",
                    BattleFailureStage.HudOpen,
                    exception: ex);
            }

            if (!CanCommitEntry())
            {
                FUI.Close<BattleHudPanel>();
                return CreateStaleEntryResult("HUD 打开完成时战斗入口已失效。");
            }

            BattleOperationResult closeEntryResult = CloseEntryAfterHudForTransaction(
                () => FUI.HasWindow<BattleStartPanel>(),
                () => FUI.Close<BattleStartPanel>());
            if (!closeEntryResult.IsSuccess)
            {
                return closeEntryResult;
            }

            Log.Info("[BattleModule] 战斗世界、必需资源、运行时服务与 HUD 已全部就绪。");
            return BattleOperationResult.Ok(BattleModuleState.Running);
        }

        /// <summary>入口异步步骤完成后，只有当前模块仍处于本轮 Entering 才允许提交。</summary>
        private bool CanCommitEntry()
        {
            return !_isShutdown && _state == BattleModuleState.Entering;
        }

        private BattleOperationResult CreateStaleEntryResult(string message)
        {
            return BattleOperationResult.Fail(
                BattleErrorCode.Exiting,
                _state,
                message,
                BattleFailureStage.Rollback);
        }

        /// <summary>
        /// HUD 成功打开后关闭入口。此处是启动事务的提交前检查点，不能由入口面板事后自行关闭。
        /// </summary>
        internal static BattleOperationResult CloseEntryAfterHudForTransaction(
            Func<bool> hasEntryPanel,
            Action closeEntryPanel)
        {
            if (hasEntryPanel == null)
            {
                throw new ArgumentNullException(nameof(hasEntryPanel));
            }

            if (closeEntryPanel == null)
            {
                throw new ArgumentNullException(nameof(closeEntryPanel));
            }

            try
            {
                if (hasEntryPanel())
                {
                    closeEntryPanel();
                }

                return BattleOperationResult.Ok(BattleModuleState.Entering);
            }
            catch (Exception ex)
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.UILoadFailed,
                    BattleModuleState.Entering,
                    $"关闭 BattleStartPanel 失败: {ex.Message}",
                    BattleFailureStage.HudOpen,
                    exception: ex);
            }
        }

        /// <summary>
        /// 默认退出前置事务：先隐藏世界并恢复相机，再恢复入口、关闭 HUD 并清空动态根。
        /// <para>本方法失败时不触碰 Runtime 或 Scope；它们由调用方保留至下一次 Exit 重试。
        /// 所有权释放只在本方法成功后由 ExitInternalAsync 执行。</para>
        /// </summary>
        private async UniTask<BattleOperationResult> DefaultExitHandler(
            BattleRuntimeScope scope)
        {
            // 入口界面必须在战场不可见、主相机已恢复的状态下打开，
            // 否则 ShowAsync 期间会把上一局画面暴露在入口 UI 后面。
            _worldHost.HideWorld();
            _worldHost.RestoreCamera();

            try
            {
                // 退出通常由按钮点击触发。让当前 FairyGUI 指针事件完整结束后
                // 再创建入口按钮，避免同一次点击穿透后立即重新开战。
                await UniTask.Yield(PlayerLoopTiming.Update);
                await FUI.ShowAsync<BattleStartPanel>(
                    new BattleStartEntryArgs(_lastLoadout, StartAsync));
            }
            catch (Exception ex)
            {
                // 入口 UI 恢复失败时，动态对象和 HUD 尚未清理，
                // 因此可以恢复战场可见性，保留完整现场供下一次 Exit 重试。
                try
                {
                    _worldHost.ApplyBattleCamera();
                    _worldHost.ActivateWorld();
                }
                catch (Exception rollbackEx)
                {
                    Log.Error($"[BattleModule] 退出回滚战场可见性失败: {rollbackEx}");
                }

                Log.Error($"[BattleModule] 退出前恢复 BattleStartPanel 失败: {ex}");
                return BattleOperationResult.Fail(
                    BattleErrorCode.UILoadFailed,
                    BattleModuleState.Exiting,
                    $"退出前恢复 BattleStartPanel 失败: {ex.Message}",
                    BattleFailureStage.Exit,
                    exception: ex);
            }

            try
            {
                FUI.Close<BattleResultPanel>();
                FUI.Close<BattleHudPanel>();
            }
            catch (Exception ex)
            {
                try
                {
                    FUI.Close<BattleStartPanel>();
                }
                catch (Exception closeEntryEx)
                {
                    Log.Error($"[BattleModule] 退出回滚时关闭 BattleStartPanel 失败: {closeEntryEx}");
                }

                try
                {
                    _worldHost.ApplyBattleCamera();
                    _worldHost.ActivateWorld();
                }
                catch (Exception rollbackEx)
                {
                    Log.Error($"[BattleModule] 退出回滚战场可见性失败: {rollbackEx}");
                }

                Log.Error($"[BattleModule] 关闭 BattleHudPanel 失败，保留当前战斗以允许重试退出: {ex}");
                return BattleOperationResult.Fail(
                    BattleErrorCode.UILoadFailed,
                    BattleModuleState.Exiting,
                    $"关闭 BattleHudPanel 失败: {ex.Message}",
                    BattleFailureStage.Exit,
                    exception: ex);
            }

            _worldHost.ClearDynamicRoots();

            return BattleOperationResult.Ok(BattleModuleState.Idle);
        }

        // ====================================================================
        // TEngine Module 抽象方法实现
        // ====================================================================

        /// <inheritdoc />
        public override void OnInit()
        {
            _state = BattleModuleState.Idle;
            _isShutdown = false;
            _isOpeningEntry = false;
            _weapon = new WeaponManager(ConfigSystem.Instance.Tables);
            _worldHost.EnsureRoot();

            // 由唯一 BattleModule 拥有 UIBattle 注册；GameLogic 组合根只负责
            // 注册模块和最后冻结 Registry，不枚举具体战斗窗口。
            if (!(FUI.Module is FUIModule fuiModule))
            {
                throw new FUIException("BattleModule 初始化失败：未找到可注册绑定的 FUIModule。");
            }

            BattleFuiOwner.Register(fuiModule.BindingRegistry);
            Log.Info("[BattleModule] OnInit: 已创建非激活 BattleWorldRoot，并完成 UIBattle 注册。");
        }

        // ====================================================================
        // IUpdateModule 实现 —— task 6.10 闭环接入
        // --------------------------------------------------------------------
        // TEngine 框架每帧调用 Update，BattleModule 只在 Running 状态下把帧时间
        // 转交给 BattleRuntime.Advance。Settling/Exiting/Idle 状态不推进模拟
        // （spec "Settling has no gameplay damage authority" / "Module receives
        // updates while settling"）。决策 0.9：只使用 elapseSeconds 作为逻辑时间源，
        // 暂停期间不以 realElapseSeconds 补偿推进。
        // ====================================================================

        /// <summary>
        /// TEngine 框架每帧轮询入口（IUpdateModule）。
        /// <para>只在 Running 状态下将帧时间转交给当前活动 Runtime 的
        /// <see cref="BattleRuntime.Advance"/>，驱动 <see cref="BattleSimulation"/>
        /// 执行 500ms 截断、80ms 子步拆分与阶段调度。非 Running 状态为空操作。</para>
        /// <para><b>所有权链（design.md 第 2 节）：</b>
        /// <code>
        /// TEngine Update
        ///   -> BattleModule.Update(elapseSeconds, realElapseSeconds)
        ///     -> BattleRuntime.Advance(deltaMilliseconds)
        ///       -> BattleSimulation.Advance(frameNowMs)
        ///         -> explicit ordered phases
        /// </code></para>
        /// <para><b>逻辑时间源（决策 0.9）：</b>只使用 <paramref name="elapseSeconds"/>
        /// 作为逻辑时间源，不使用 <paramref name="realElapseSeconds"/> 补偿推进。
        /// elapseSeconds → 毫秒：deltaMilliseconds = (long)(elapseSeconds * 1000)。</para>
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（秒），由 TEngine 框架提供。</param>
        /// <param name="realElapseSeconds">真实流逝时间（秒），本期不使用（决策 0.9）。</param>
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            // 只在 Running 状态驱动 Runtime。
            // Settling 状态：Runtime.Advance 内部已有 IsSettling 守卫直接返回，
            // 但此处提前检查避免无效调用。
            // Idle/Entering/Exiting/Faulted/Restarting 状态不推进。
            if (_state != BattleModuleState.Running)
            {
                return;
            }

            BattleRuntime runtime = _activeRuntime;
            if (runtime == null || runtime.IsDisposed)
            {
                return;
            }

            // 决策 0.9：只使用 elapseSeconds 作为逻辑时间源。
            // elapseSeconds → 毫秒：负值或零值不推进（BattleSimulation.Advance 内部
            // 对 remaining <= 0 直接返回）。
            long deltaMilliseconds = (long)(elapseSeconds * 1000);
            if (deltaMilliseconds <= 0)
            {
                return;
            }

            runtime.Advance(deltaMilliseconds);
            runtime.Presenter?.SyncFrame(elapseSeconds);

            // 检查是否在本次 Advance 中触发了 TryFreeze → 结果冻结。
            // Simulation.IsFrozen 由 Simulation.TryFreeze() 在 phase 检查点置位
            // （TryFreeze 检查 resultBuilder.IsFrozen 并据返回值置位 Simulation.IsFrozen）。
            // 若 Simulation 已冻结，迁移到 Settling 并调用 EnterSettling。
            if (!runtime.IsSettling && runtime.Simulation != null && runtime.Simulation.IsFrozen)
            {
                TransitionTo(BattleModuleState.Settling);
                runtime.EnterSettling();

                // EnterSettling 完成静默清理并冻结最终结果后，由 BattleModule 直接提交
                // 局外武器奖励，再打开结算窗口。Update 已经迁移到 Settling，因此后续
                // 多帧不会重复提交同一局奖励。
                BattleResultDto result = runtime.ResultBuilder.GetFrozenResult();
                WeaponRewardGrant weaponReward =
                    _weapon.ApplyBattleResult(_lastLoadout, result);
                ShowBattleResultPanelAsync(result, weaponReward).Forget();
            }
        }

        private async UniTaskVoid ShowBattleResultPanelAsync(
            BattleResultDto result,
            WeaponRewardGrant weaponReward)
        {
            try
            {
                await FUI.ShowAsync<BattleResultPanel>(
                    new BattleResultEntryArgs(result, weaponReward, ExitAsync));

                // 异步加载期间可能已从其他入口退出；旧局结算窗不得带到开始界面。
                if (_isShutdown || _state != BattleModuleState.Settling)
                {
                    FUI.Close<BattleResultPanel>();
                }
            }
            catch (Exception ex)
            {
                if (!_isShutdown)
                {
                    Log.Error($"[BattleModule] 打开 BattleResultPanel 失败: {ex}");
                }
            }
        }

        /// <inheritdoc />
        public override void Shutdown()
        {
            _isShutdown = true;
            _isOpeningEntry = false;
            _weapon?.SaveIfDirty();
            _weapon = null;
            ForceCleanupInternal();

            try
            {
                FUI.Close<BattleResultPanel>();
                FUI.Close<BattleHudPanel>();
                FUI.Close<BattleStartPanel>();
            }
            catch (Exception ex)
            {
                Log.Warning($"[BattleModule] Shutdown 关闭战斗窗口失败: {ex.Message}");
            }

#if UNITY_EDITOR
            // Editor 退出 PlayMode 时必须同步销毁，确保 AssetsReference 在资源池关闭前归还引用。
            _worldHost.Release(destroyImmediate: true);
#else
            _worldHost.Release();
#endif
            _entryPreparationTask = null;
            _preparationSnapshot = null;
            _activeContext = null;
            Log.Info("[BattleModule] Shutdown: 已释放运行时、地图、根节点并恢复主相机。");
        }

        // ====================================================================
        // IBattleModule 公共异步 API 实现
        // ====================================================================

        /// <inheritdoc />
        public UniTask<BattleOperationResult> StartAsync(BattleLoadoutDto loadout)
        {
            return StartInternalAsync(loadout);
        }

        /// <inheritdoc />
        public UniTask<BattleOperationResult> RestartAsync(BattleLoadoutDto loadout)
        {
            return RestartInternalAsync(loadout);
        }

        /// <inheritdoc />
        public UniTask<BattleOperationResult> ExitAsync()
        {
            return ExitInternalAsync();
        }

        // ====================================================================
        // Start 内部实现
        // ====================================================================

        /// <summary>
        /// Start 内部实现。状态在第一次 await 前同步迁移到 Entering。
        /// </summary>
        private async UniTask<BattleOperationResult> StartInternalAsync(BattleLoadoutDto loadout)
        {
            if (_isShutdown)
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.Faulted,
                    _state,
                    "BattleModule 已关闭，不能开始新战斗。");
            }

            // ----------------------------------------------------------
            // 状态校验：Start 只允许 Idle。
            // ----------------------------------------------------------
            if (_state == BattleModuleState.Faulted)
            {
                // Faulted 必须先清理才能回到 Idle，然后才能 Start。
                return BattleOperationResult.Fail(
                    BattleErrorCode.Faulted,
                    _state,
                    "模块处于故障状态，需先清理（Exit）才能开始新战斗。");
            }

            if (_state != BattleModuleState.Idle)
            {
                // 重复 Start：返回 AlreadyActive，不改变当前状态。
                // 对应 spec "Duplicate start returns AlreadyActive"。
                return BattleOperationResult.Fail(
                    BattleErrorCode.AlreadyActive,
                    _state,
                    $"战斗正在进行中（状态={_state}），不创建第二个活动运行时。");
            }

            // ----------------------------------------------------------
            // 状态迁移校验：Idle → Entering。
            // ----------------------------------------------------------
            if (!BattleModuleStateTransitions.CanTransition(_state, BattleModuleState.Entering))
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.Unknown,
                    _state,
                    $"状态迁移校验失败：{_state} → Entering 不合法。");
            }

            TransitionTo(BattleModuleState.Entering);

            // ----------------------------------------------------------
            // 创建本局运行时作用域，跟踪部分初始化步骤。
            // 加载失败时逆序回滚（spec "Partial initialization is recoverable"）。
            // ----------------------------------------------------------
            BattleRuntimeScope scope = new BattleRuntimeScope();
            _activeScope = scope;

            try
            {
                // 执行加载步骤委托。
                BattleOperationResult entryResult =
                    await _entryHandler(loadout, scope);

                // Exit/Shutdown 已在等待期间接管本局所有权时，迟到的入口结果只能丢弃。
                // 清理由当前 Exit/Shutdown 负责；这里不能再次回滚或记录非法迁移错误。
                if (!CanCommitEntry())
                {
                    return CreateStaleEntryResult("入口加载完成时本轮 Entering 已失效。");
                }

                if (!entryResult.IsSuccess)
                {
                    // 加载失败：执行反向回滚。
                    // 对应 spec "Resource loading fails during entry"：
                    // 系统不会进入运行状态，不留下半初始化运行时。
                    Log.Warning(
                        $"[BattleModule] 加载失败，执行反向回滚: {entryResult.ErrorCode}");

                    // 状态迁移：Entering → Exiting → Idle（合法路径）。
                    // 回滚在 ExitInternalAsync 的清理路径中执行。
                    return await RollbackAndReturnAsync(entryResult);
                }

                // 加载成功：迁移到 Running。
                if (!BattleModuleStateTransitions.CanTransition(
                    _state, BattleModuleState.Running))
                {
                    // 状态迁移不合法（理论上不应该发生），回滚并返回错误。
                    Log.Error(
                        $"[BattleModule] 加载成功但状态迁移校验失败：{_state} → Running。");
                    return await RollbackAndReturnAsync(
                        BattleOperationResult.Fail(
                            BattleErrorCode.Unknown,
                            _state,
                            $"加载成功但状态迁移校验失败：{_state} → Running 不合法。"));
                }

                TransitionTo(BattleModuleState.Running);
                return BattleOperationResult.Ok(_state);
            }
            catch (Exception ex)
            {
                Log.Error($"[BattleModule] Start 发生非预期异常: {ex}");
                return await RollbackAndReturnAsync(
                    BattleOperationResult.Fail(
                        BattleErrorCode.Unknown,
                        _state,
                        $"Start 发生非预期异常: {ex.Message}",
                        BattleFailureStage.Rollback,
                        exception: ex));
            }
        }

        /// <summary>
        /// 加载失败时的反向回滚辅助方法。
        /// <para>状态迁移路径：Entering → Exiting → Idle。
        /// 逆序释放 scope 中已登记的所有权，然后回到 Idle。</para>
        /// </summary>
        /// <param name="failResult">要返回的失败结果。</param>
        /// <returns>传入的失败结果（状态已更新为 Idle）。</returns>
        private async UniTask<BattleOperationResult> RollbackAndReturnAsync(
            BattleOperationResult failResult)
        {
            // Entering → Exiting（合法迁移）。
            if (BattleModuleStateTransitions.CanTransition(
                _state, BattleModuleState.Exiting))
            {
                TransitionTo(BattleModuleState.Exiting);
            }

            // 销毁活动 Runtime（若有）并释放 scope。
            BattleRuntime runtime = _activeRuntime;
            _activeRuntime = null;

            BattleRuntimeScope scope = _activeScope;
            _activeScope = null;

            // 进入失败：清空本次 Context 与入口世界准备任务（design.md 决策 3）。
            _activeContext = null;
            _entryPreparationTask = null;
            _preparationSnapshot = null;

            if (runtime != null && !runtime.IsDisposed)
            {
                try
                {
                    runtime.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error($"[BattleModule] Rollback Runtime.Dispose 异常: {ex}");
                }
            }

            if (scope != null)
            {
                try
                {
                    scope.Rollback();
                }
                catch (Exception ex)
                {
                    Log.Error($"[BattleModule] 回滚过程中发生异常: {ex}");
                }
            }

            try
            {
                FUI.Close<BattleHudPanel>();
            }
            catch (Exception ex)
            {
                Log.Warning($"[BattleModule] 回滚时关闭 BattleHudPanel 失败: {ex.Message}");
            }

            _worldHost.ClearDynamicRoots();
            _worldHost.HideWorld();
            _worldHost.ClearBindings();

            // Start/Restart 失败后模块回到 Idle，本次会话不再保留池容量。
            ClearPoolsForExit("进入失败回滚");

            // Exiting → Idle（合法迁移）。
            if (BattleModuleStateTransitions.CanTransition(
                _state, BattleModuleState.Idle))
            {
                TransitionTo(BattleModuleState.Idle);
            }

            // 返回更新了状态快照的失败结果。
            if (!_isShutdown)
            {
                try
                {
                    // HasWindow 会把 Closing 也视为存活；失败回滚必须显式 Show，
                    // 由 FUI 自身将关闭中的旧请求收敛为新的 Open，不能据 HasWindow 跳过恢复。
                    await FUI.ShowAsync<BattleStartPanel>(
                        new BattleStartEntryArgs(_lastLoadout, StartAsync));
                }
                catch (Exception ex)
                {
                    Log.Error($"[BattleModule] 回滚后恢复 BattleStartPanel 失败: {ex}");
                }
            }

            return failResult.WithState(_state);
        }

        // ====================================================================
        // Restart 内部实现
        // ====================================================================

        /// <summary>
        /// Restart 内部实现。状态在第一次 await 前同步迁移到 Restarting/Entering。
        /// </summary>
        private async UniTask<BattleOperationResult> RestartInternalAsync(BattleLoadoutDto loadout)
        {
            if (_isShutdown)
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.Faulted,
                    _state,
                    "BattleModule 已关闭，不能重开战斗。");
            }

            // ----------------------------------------------------------
            // 状态校验：Restart 只允许 Settling。
            // ----------------------------------------------------------
            if (_state == BattleModuleState.Faulted)
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.Faulted,
                    _state,
                    "模块处于故障状态，需先清理（Exit）才能继续。");
            }

            if (_state != BattleModuleState.Settling)
            {
                // 非 Settling 状态调用 Restart：返回 NotSettling。
                // 对应 spec "Restart rejected outside settling"。
                return BattleOperationResult.Fail(
                    BattleErrorCode.NotSettling,
                    _state,
                    $"Restart 只允许 Settling 状态（当前={_state}），不销毁当前运行时。");
            }

            // ----------------------------------------------------------
            // 状态迁移：Settling → Restarting → Entering → Running。
            // ----------------------------------------------------------
            if (!BattleModuleStateTransitions.CanTransition(
                _state, BattleModuleState.Restarting))
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.Unknown,
                    _state,
                    $"状态迁移校验失败：{_state} → Restarting 不合法。");
            }

            TransitionTo(BattleModuleState.Restarting);

            // 销毁旧运行时的全部可变状态和局部订阅。
            // 对应 spec "Restart creates clean per-battle state"。
            // ----------------------------------------------------------
            BattleRuntime oldRuntime = _activeRuntime;
            BattleRuntimeScope oldScope = _activeScope;
            _activeRuntime = null;
            _activeScope = null;

            // 新一局使用新地图快照：清空旧 Context，由入口处理器按新 Loadout 即时准备。
            _activeContext = null;
            _entryPreparationTask = null;
            _preparationSnapshot = null;

            try
            {
                if (oldRuntime != null && !oldRuntime.IsDisposed)
                {
                    oldRuntime.Dispose();
                }

                if (oldScope != null)
                {
                    oldScope.Release();
                }

                if (!_poolScope.ClearForNewBattle())
                {
                    _poolScope.ClearAll();
                    TransitionTo(BattleModuleState.Faulted);
                    return BattleOperationResult.Fail(
                        BattleErrorCode.Unknown,
                        _state,
                        "Restart 前仍有活动池租借，已清空池并进入 Faulted。");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BattleModule] Restart 清理旧运行时异常: {ex}");
                TransitionTo(BattleModuleState.Faulted);
                return BattleOperationResult.Fail(
                    BattleErrorCode.Unknown,
                    _state,
                    $"Restart 清理旧运行时异常: {ex.Message}");
            }

            // ----------------------------------------------------------
            // 创建新运行时（复用 Start 的加载逻辑）。
            // ----------------------------------------------------------
            TransitionTo(BattleModuleState.Entering);

            BattleRuntimeScope newScope = new BattleRuntimeScope();
            _activeScope = newScope;

            try
            {
                BattleOperationResult entryResult =
                    await _entryHandler(loadout, newScope);

                // 重开等待期间若已退出或关闭，迟到结果不得重新提交 Running，
                // 也不得与 Exit/Shutdown 重复争用清理所有权。
                if (!CanCommitEntry())
                {
                    return CreateStaleEntryResult("重开加载完成时本轮 Entering 已失效。");
                }

                if (!entryResult.IsSuccess)
                {
                    // 新局加载失败：回滚新 scope，返回失败结果。
                    Log.Warning(
                        $"[BattleModule] Restart 新局加载失败，执行回滚: {entryResult.ErrorCode}");

                    return await RollbackAndReturnAsync(entryResult);
                }

                if (!BattleModuleStateTransitions.CanTransition(
                    _state, BattleModuleState.Running))
                {
                    Log.Error(
                        $"[BattleModule] Restart 加载成功但状态迁移校验失败：{_state} → Running。");
                    return await RollbackAndReturnAsync(
                        BattleOperationResult.Fail(
                            BattleErrorCode.Unknown,
                            _state,
                            $"Restart 加载成功但状态迁移校验失败：{_state} → Running。"));
                }

                TransitionTo(BattleModuleState.Running);
                return BattleOperationResult.Ok(_state);
            }
            catch (Exception ex)
            {
                Log.Error($"[BattleModule] Restart 发生非预期异常: {ex}");
                return await RollbackAndReturnAsync(
                    BattleOperationResult.Fail(
                        BattleErrorCode.Unknown,
                        _state,
                        $"Restart 发生非预期异常: {ex.Message}",
                        BattleFailureStage.Rollback,
                        exception: ex));
            }
        }

        // ====================================================================
        // Exit 内部实现
        // ====================================================================

        /// <summary>
        /// Exit 内部实现。活动状态在第一次 await 前同步迁移到 Exiting。
        /// </summary>
        /// <remarks>
        /// Exit 在任意状态幂等：
        /// - Idle 状态调用 Exit 直接返回成功（幂等，无活动运行时需清理）。
        /// - 其他状态执行退出清理后回到 Idle。
        /// - Exiting 状态重复调用返回 Exiting，不重复清理。
        /// </remarks>
        private async UniTask<BattleOperationResult> ExitInternalAsync()
        {
            // ----------------------------------------------------------
            // 幂等检查：Idle 状态直接返回成功。
            // ----------------------------------------------------------
            if (_state == BattleModuleState.Idle)
            {
                // 退出完成后再次调用返回已完成的幂等结果。
                return BattleOperationResult.Ok(BattleModuleState.Idle);
            }

            if (_state == BattleModuleState.Exiting)
            {
                return BattleOperationResult.Fail(
                    BattleErrorCode.Exiting,
                    _state,
                    "退出操作正在进行，不重复执行清理。");
            }

            // ----------------------------------------------------------
            // Faulted 状态：Exit 作为清理路径，Faulted → Idle（清理后）。
            // 但迁移表中 Faulted → Exiting 不合法，Faulted 只能直接 → Idle。
            // 因此 Faulted 状态的 Exit 直接执行清理然后迁移到 Idle。
            // ----------------------------------------------------------
            if (_state == BattleModuleState.Faulted)
            {
                return await ExitFromFaultedAsync();
            }

            // ----------------------------------------------------------
            // 活动状态退出：先迁移到 Exiting，执行清理，再迁移到 Idle。
            // ----------------------------------------------------------
            if (!BattleModuleStateTransitions.CanTransition(
                _state, BattleModuleState.Exiting))
            {
                // 理论上不会到达：所有活动状态都可以 → Exiting。
                return BattleOperationResult.Fail(
                    BattleErrorCode.Unknown,
                    _state,
                    $"状态迁移校验失败：{_state} → Exiting 不合法。");
            }

            TransitionTo(BattleModuleState.Exiting);

            try
            {
                BattleOperationResult exitResult =
                    await _exitHandler(_activeScope);

                if (!exitResult.IsSuccess)
                {
                    return PreserveForExitRetry(exitResult);
                }
            }
            catch (Exception ex)
            {
                return PreserveForExitRetry(BattleOperationResult.Fail(
                    BattleErrorCode.Unknown,
                    _state,
                    $"Exit 清理异常: {ex.Message}",
                    BattleFailureStage.Exit,
                    exception: ex));
            }

            ReleaseActiveRuntimeForExit("Exit");

            // 退出完成：Exiting → Idle。
            if (!BattleModuleStateTransitions.CanTransition(
                _state, BattleModuleState.Idle))
            {
                Log.Error($"[BattleModule] Exit 完成但状态迁移校验失败：{_state} → Idle。");
            }
            else
            {
                TransitionTo(BattleModuleState.Idle);
            }

            return BattleOperationResult.Ok(_state);
        }

        /// <summary>
        /// 从 Faulted 状态执行退出清理。
        /// <para>Faulted → Idle 是合法迁移（清理后恢复），
        /// 不经过 Exiting 状态（迁移表中 Faulted 只能直接 → Idle）。</para>
        /// </summary>
        private async UniTask<BattleOperationResult> ExitFromFaultedAsync()
        {
            try
            {
                // 即使 scope 为 null，也调用清理委托执行其他退出步骤（如关闭 UI）。
                BattleOperationResult exitResult = await _exitHandler(_activeScope);
                if (!exitResult.IsSuccess)
                {
                    Log.Warning($"[BattleModule] Faulted Exit 清理步骤返回失败: {exitResult.ErrorCode}");
                    return exitResult.WithState(_state);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BattleModule] Faulted 清理异常: {ex}");
                return BattleOperationResult.Fail(
                    BattleErrorCode.Unknown,
                    _state,
                    $"Faulted Exit 清理异常: {ex.Message}",
                    BattleFailureStage.Exit,
                    exception: ex);
            }

            ReleaseActiveRuntimeForExit("Faulted Exit");

            // Faulted → Idle（清理后恢复）。
            if (BattleModuleStateTransitions.CanTransition(
                _state, BattleModuleState.Idle))
            {
                TransitionTo(BattleModuleState.Idle);
            }

            return BattleOperationResult.Ok(_state);
        }

        /// <summary>
        /// 退出前置 UI 恢复失败时保留运行时所有权与可见战斗世界，供下一次 Exit 重试。
        /// </summary>
        private BattleOperationResult PreserveForExitRetry(BattleOperationResult failure)
        {
            Log.Warning($"[BattleModule] Exit 清理未完成，保留当前战斗以允许重试: {failure.ErrorCode}");
            if (_state != BattleModuleState.Faulted)
            {
                TransitionTo(BattleModuleState.Faulted);
            }

            return failure.WithState(_state);
        }

        /// <summary>退出前置事务成功后，才释放本局运行时与作用域。</summary>
        private void ReleaseActiveRuntimeForExit(string reason)
        {
            BattleRuntime runtimeToRelease = _activeRuntime;
            _activeRuntime = null;
            BattleRuntimeScope scopeToRelease = _activeScope;
            _activeScope = null;

            // 退出完成：清空本局 Context 与入口世界准备任务（design.md 决策 3）。
            _activeContext = null;
            _entryPreparationTask = null;
            _preparationSnapshot = null;

            // 清空单局 Bindings（动态根已由退出处理器清理；地图实例保留供地址复用）。
            try
            {
                _worldHost.ClearBindings();
            }
            catch (Exception ex)
            {
                Log.Warning($"[BattleModule] {reason} 清空地图绑定异常: {ex.Message}");
            }

            if (runtimeToRelease != null && !runtimeToRelease.IsDisposed)
            {
                try
                {
                    runtimeToRelease.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error($"[BattleModule] {reason} runtime.Dispose 异常: {ex}");
                }
            }

            if (scopeToRelease != null && !scopeToRelease.IsDisposed)
            {
                try
                {
                    scopeToRelease.Release();
                }
                catch (Exception ex)
                {
                    Log.Error($"[BattleModule] {reason} scope 释放异常: {ex}");
                }
            }

            ClearPoolsForExit(reason);
        }

        // ====================================================================
        // 强制清理（TEngine Shutdown 路径）
        // ====================================================================

        /// <summary>
        /// 强制清理内部状态（TEngine Shutdown 时调用）。
        /// <para>对应 spec "Exit releases battle-owned state" 的框架级关闭路径。
        /// 不抛异常，尽可能释放所有资源。</para>
        /// </summary>
        private void ForceCleanupInternal()
        {
            // 销毁活动 Runtime（释放全部单局所有权）。
            BattleRuntime runtime = _activeRuntime;
            _activeRuntime = null;

            BattleRuntimeScope scope = _activeScope;
            _activeScope = null;
            _activeContext = null;
            _entryPreparationTask = null;
            _preparationSnapshot = null;

            if (runtime != null && !runtime.IsDisposed)
            {
                try
                {
                    runtime.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error($"[BattleModule] 强制清理 Runtime 异常: {ex}");
                }
            }

            if (scope != null && !scope.IsDisposed)
            {
                try
                {
                    scope.Release();
                }
                catch (Exception ex)
                {
                    Log.Error($"[BattleModule] 强制清理 Scope 异常: {ex}");
                }
            }

            ClearPoolsForExit("Shutdown");

            _state = BattleModuleState.Idle;
        }

        /// <summary>
        /// 返回主界面、失败回滚或 Shutdown 时清空全部跨局池容量。
        /// </summary>
        private void ClearPoolsForExit(string reason)
        {
            try
            {
                _poolScope.ClearAll();
                Log.Info($"[BattleModule] {reason}: 战斗对象池容量已清空。");
            }
            catch (Exception ex)
            {
                Log.Error($"[BattleModule] {reason}: 清空战斗对象池异常: {ex}");
            }
        }

        // ====================================================================
        // 状态迁移辅助
        // ====================================================================

        /// <summary>
        /// 把 FairyGUI Stage 坐标解析为玩家方战场槽位标识（最终方案）。
        /// </summary>
        /// <param name="screenX">Stage X 坐标。</param>
        /// <param name="screenY">Stage Y 坐标。</param>
        /// <param name="targetSlotId">解析出的玩家战场槽位固定标识；未命中为 -1。</param>
        /// <returns>解析成功且命中战场槽返回 true。</returns>
        /// <remarks>
        /// <para>经 Presenter 的坐标转换（ICoordinateConverter）把 Stage 坐标转换为格子，
        /// 再经槽位面板查找玩家侧对应战场槽。未命中返回 false（表现弹回源槽）。</para>
        /// </remarks>
        private bool ResolvePlayerBattleSlotForStage(
            float screenX,
            float screenY,
            out int targetSlotId)
        {
            targetSlotId = -1;
            BattleRuntime runtime = _activeRuntime;
            if (runtime?.Presenter == null)
            {
                return false;
            }

            return runtime.Presenter.TryResolvePlayerBattleSlot(screenX, screenY, out targetSlotId);
        }

        /// <summary>
        /// 解析玩家战场可起拖的源槽（统一拖放规则：源命中外层槽位框架）。
        /// </summary>
        /// <param name="screenX">Stage X 坐标。</param>
        /// <param name="screenY">Stage Y 坐标。</param>
        /// <param name="battleSlotId">解析出的玩家战场源槽位固定标识；未命中为 -1。</param>
        /// <returns>
        /// 解析成功且命中非空玩家战场槽的外层框架返回 true。
        /// 起拖不依赖单位内部的 Spine/序列帧 Renderer；投放目标仍使用
        /// <see cref="ResolvePlayerBattleSlotForStage"/> 完整槽位命中。
        /// </returns>
        /// <remarks>
        /// 先经 Presenter 按屏幕坐标锁定玩家战场槽，再检查槽位快照是否有单位。
        /// 槽位是输入命中层，单位 Prefab 只是表现层；这样普通兵、无 Spine 武将、
        /// Spine 武将和未合成武将字都共享相同的起拖规则。
        /// </remarks>
        private bool ResolvePlayerBattleSourceForStage(
            float screenX,
            float screenY,
            out int battleSlotId)
        {
            battleSlotId = -1;
            BattleRuntime runtime = _activeRuntime;
            if (runtime?.Presenter == null)
            {
                return false;
            }

            // 1. 先锁定玩家战场槽（完整槽位命中）。
            if (!runtime.Presenter.TryResolvePlayerBattleSlot(screenX, screenY, out battleSlotId))
            {
                return false;
            }

            // 2. 槽必须有单位。
            UnitSlot slot = runtime.Presenter.GetSlotSnapshot().GetSlotById(battleSlotId);
            if (slot.IsEmpty || slot.SlotId.Zone != SlotZone.Battle)
            {
                return false;
            }

            // 源命中到的是外层槽位框架，而不是单位 Prefab 内部的动画 Renderer。
            // 规则层的 SlotId 才是拖拽身份；无论表现是序列帧、Spine 还是单字，
            // 都不能因为内部 Renderer bounds 缺失/偏移而阻断起拖。
            return true;
        }

        /// <summary>
        /// 执行状态迁移（Unity 主线程同步调用）。
        /// <para>调用前必须已通过 <see cref="BattleModuleStateTransitions.CanTransition"/>
        /// 校验。Debug 模式下断言迁移合法性。</para>
        /// </summary>
        /// <param name="to">目标状态。</param>
        private void TransitionTo(BattleModuleState to)
        {
            BattleModuleState from = _state;

            // Debug 断言：迁移必须合法。
            if (!BattleModuleStateTransitions.CanTransition(from, to))
            {
                Log.Error(
                    $"[BattleModule] 非法状态迁移：{from} → {to}。" +
                    "请在调用 TransitionTo 前用 CanTransition 校验。");
                // 生产环境不抛异常以保持稳健，但记录错误。
                // 测试环境可通过 Log.Assert 验证。
            }

            _state = to;
            Log.Debug($"[BattleModule] 状态迁移：{from} → {to}");
        }
    }
}
