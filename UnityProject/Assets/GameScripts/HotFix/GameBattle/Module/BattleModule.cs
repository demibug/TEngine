using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameCommon.Battle;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 2.6：BattleModule —— Start/Restart/Exit 串行门与生命周期实现
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1/3 节 / specs/battle-runtime-lifecycle/spec.md）：
    //   BattleModule 是 TEngine 长期存在的模块和唯一外部入口（design 决策 1）。
    //   本文件实现 IBattleModule 定义的三条公共异步命令（Start/Restart/Exit），
    //   并保证以下不变量：
    //
    //   1. 串行门：同一时刻只有一个生命周期操作在执行（SemaphoreSlim(1,1)）。
    //   2. 重复语义（决策 0.7）：
    //      - Start 只允许 Idle；重复 Start 返回 AlreadyActive，不创建第二个运行时。
    //      - Restart 只允许 Settling；其他状态返回 NotSettling，不销毁当前运行时。
    //      - Exit 在任意状态幂等；并发调用共享进行中的退出操作而不重复执行清理。
    //   3. 加载失败执行反向回滚（spec "Partial initialization is recoverable"）：
    //      使用 BattleRuntimeScope 跟踪进入步骤，失败时逆序释放。
    //   4. 调用方取消不能绕过内部清理（task 2.6）：
    //      取消异常从内部清理 finally 块抛出，清理逻辑先于异常传播执行。
    //   5. Faulted 必须先清理才能回到 Idle（决策 0.7）：
    //      Faulted → Idle 由 Exit 或显式清理完成，不允许直接 Start/Restart。
    //   6. 使用 BattleModuleStateTransitions.CanTransition 校验所有状态迁移。
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
    /// <para><b>串行门（task 2.6 核心）：</b></para>
    /// <para>使用 <see cref="SemaphoreSlim"/>(1,1) 保证同一时刻只有一个生命周期操作在执行。
    /// Start/Restart/Exit 在执行实际逻辑前必须先获取串行门，防止并发命令绕过状态校验。</para>
    ///
    /// <para><b>重复语义（决策 0.7）：</b></para>
    /// <list type="bullet">
    /// <item><see cref="StartAsync"/> 只允许 <see cref="BattleModuleState.Idle"/>；
    /// 重复 Start 返回 <see cref="BattleErrorCode.AlreadyActive"/>。</item>
    /// <item><see cref="RestartAsync"/> 只允许 <see cref="BattleModuleState.Settling"/>；
    /// 其他状态返回 <see cref="BattleErrorCode.NotSettling"/>。</item>
    /// <item><see cref="ExitAsync"/> 在任意状态幂等；并发调用共享进行中的退出操作。</item>
    /// </list>
    ///
    /// <para><b>加载失败回滚（spec "Partial initialization is recoverable"）：</b></para>
    /// <para>进入战斗的每个步骤都登记到 <see cref="BattleRuntimeScope"/>，
    /// 任一步骤失败时逆序释放已完成部分，不留下半初始化运行时。</para>
    ///
    /// <para><b>调用方取消语义（决策 0.7）：</b></para>
    /// <para>调用方取消抛出 <see cref="OperationCanceledException"/>，保留取消异常语义。
    /// 但取消不能绕过内部清理：清理逻辑在 finally 块中先于异常传播执行。</para>
    /// </remarks>
    public sealed class BattleModule : Module, IBattleModule
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
        /// <param name="cancellationToken">运行时取消令牌（已链接调用方令牌）。</param>
        /// <returns>结构化操作结果。成功时状态应为 Running。</returns>
        internal delegate UniTask<BattleOperationResult> BattleEntryHandler(
            BattleLoadoutDto loadout,
            BattleRuntimeScope scope,
            CancellationToken cancellationToken);

        /// <summary>
        /// 退出战斗的清理步骤委托。
        /// </summary>
        /// <param name="scope">本局运行时所有权作用域（可能为 null，表示无活动运行时）。</param>
        /// <param name="cancellationToken">运行时取消令牌。</param>
        /// <returns>结构化操作结果。成功时状态应为 Idle。</returns>
        internal delegate UniTask<BattleOperationResult> BattleExitHandler(
            BattleRuntimeScope scope,
            CancellationToken cancellationToken);

        // ====================================================================
        // 内部状态
        // ====================================================================

        /// <summary>
        /// 串行门：保证同一时刻只有一个生命周期操作在执行。
        /// <para>初始计数为 1，Start/Restart/Exit 在执行实际逻辑前必须先 Wait，
        /// 完成后 Release。使用 SemaphoreSlim 而非 lock，因为异步方法不能在 lock 内 await。</para>
        /// </summary>
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 当前模块生命周期状态。
        /// <para>所有读写都在串行门保护下进行（构造和 Shutdown 除外），
        /// 因此不需要额外同步原语。</para>
        /// </summary>
        private BattleModuleState _state = BattleModuleState.Idle;

        /// <summary>
        /// 当前活动运行时的所有权作用域。
        /// <para>null 表示无活动运行时（Idle 或退出后）。
        /// 非 null 表示正在进入、运行、结算或重开中。</para>
        /// </summary>
        private BattleRuntimeScope _activeScope;

        /// <summary>
        /// 进行中的 Exit 操作的完成源。
        /// <para>非 null 表示有退出操作正在进行；并发 Exit 调用共享此完成源，
        /// 避免重复执行清理。退出完成后置 null。</para>
        /// </summary>
        private UniTaskCompletionSource<BattleOperationResult> _pendingExitTcs;

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
        /// 默认加载步骤：不执行真实加载，直接返回成功并将状态设为 Running。
        /// <para>后续 task 2.9/2.10 接入 BattleRuntimeFactory 后替换。</para>
        /// </summary>
        private async UniTask<BattleOperationResult> DefaultEntryHandler(
            BattleLoadoutDto loadout,
            BattleRuntimeScope scope,
            CancellationToken cancellationToken)
        {
            await UniTask.Yield(cancellationToken);
            return BattleOperationResult.Ok(BattleModuleState.Running);
        }

        /// <summary>
        /// 默认清理步骤：释放 Scope 并返回成功（状态 Idle）。
        /// </summary>
        private UniTask<BattleOperationResult> DefaultExitHandler(
            BattleRuntimeScope scope,
            CancellationToken cancellationToken)
        {
            if (scope != null)
            {
                scope.Release();
            }
            return UniTask.FromResult(BattleOperationResult.Ok(BattleModuleState.Idle));
        }

        // ====================================================================
        // TEngine Module 抽象方法实现
        // ====================================================================

        /// <inheritdoc />
        public override void OnInit()
        {
            // 模块初始化：当前无额外初始化逻辑。
            // 状态已在字段初始化时设为 Idle，串行门已创建。
            Log.Info("[BattleModule] OnInit: 模块初始化完成。");
        }

        /// <inheritdoc />
        public override void Shutdown()
        {
            // TEngine 全局 Shutdown：强制清理，不经过串行门（框架级关闭）。
            // 对应 spec "Exit releases battle-owned state" 的框架级路径。
            ForceCleanupInternal();
            _lifecycleGate?.Dispose();
            Log.Info("[BattleModule] Shutdown: 模块已关闭。");
        }

        // ====================================================================
        // IBattleModule 公共异步 API 实现
        // ====================================================================

        /// <inheritdoc />
        public async UniTask<BattleOperationResult> StartAsync(
            BattleLoadoutDto loadout,
            CancellationToken cancellationToken = default)
        {
            // 串行门：等待当前操作完成后再执行。
            await _lifecycleGate.WaitAsync(cancellationToken);
            try
            {
                return await StartInternalAsync(loadout, cancellationToken);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        /// <inheritdoc />
        public async UniTask<BattleOperationResult> RestartAsync(
            BattleLoadoutDto loadout,
            CancellationToken cancellationToken = default)
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            try
            {
                return await RestartInternalAsync(loadout, cancellationToken);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        /// <inheritdoc />
        public async UniTask<BattleOperationResult> ExitAsync(
            CancellationToken cancellationToken = default)
        {
            // ==================================================================
            // Exit 并发合并：多个调用方并发请求退出时共享进行中的退出操作。
            // ==================================================================
            // 先获取串行门外层检查是否有进行中的 Exit。
            // 但为了支持并发合并，Exit 的门控逻辑分为两步：
            //   1. 快速检查：如果已有进行中的 Exit（_pendingExitTcs != null），
            //      直接附加到该完成源，不等待串行门。
            //   2. 否则获取串行门执行退出操作。
            //
            // 这里的设计权衡：
            //   - 并发 Exit 不需要全部排队等待串行门，只需第一个获取门的执行退出，
            //     后续调用附加到同一个完成源。
            //   - 使用 UniTaskCompletionSource 实现合并，保证所有并发调用方获得相同结果。
            // ==================================================================

            UniTaskCompletionSource<BattleOperationResult> pendingTcs;

            // 快速检查是否有进行中的退出操作（无锁读取，稍后在串行门内二次确认）。
            pendingTcs = _pendingExitTcs;
            if (pendingTcs != null)
            {
                // 已有进行中的退出操作，直接附加到该完成源，不重复执行清理。
                return await pendingTcs.Task.AttachExternalCancellation(cancellationToken);
            }

            // 没有进行中的退出操作，获取串行门。
            await _lifecycleGate.WaitAsync(cancellationToken);
            try
            {
                // 二次确认：在等待串行门期间可能已有其他调用方启动了退出操作。
                if (_pendingExitTcs != null)
                {
                    return await _pendingExitTcs.Task
                        .AttachExternalCancellation(cancellationToken);
                }

                // 当前调用方是第一个请求退出的，创建完成源并执行退出。
                _pendingExitTcs = new UniTaskCompletionSource<BattleOperationResult>();

                try
                {
                    BattleOperationResult result = await ExitInternalAsync(cancellationToken);
                    _pendingExitTcs.TrySetResult(result);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    // 调用方取消：取消异常传播前确保完成源有结果。
                    // 退出操作即使被取消也会在 ExitInternalAsync 的 finally 中完成清理。
                    // 这里设置一个取消时的结果，让附加的并发调用方获得确定性结果。
                    _pendingExitTcs.TrySetResult(
                        BattleOperationResult.Fail(
                            BattleErrorCode.Unknown,
                            _state,
                            "退出操作被调用方取消，内部清理已执行。"));
                    throw;
                }
                catch (Exception ex)
                {
                    _pendingExitTcs.TrySetResult(
                        BattleOperationResult.Fail(
                            BattleErrorCode.Unknown,
                            _state,
                            $"退出操作发生异常: {ex.Message}"));
                    throw;
                }
                finally
                {
                    // 退出操作完成（无论成功/失败/取消），清除进行中标记。
                    _pendingExitTcs = null;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        // ====================================================================
        // Start 内部实现
        // ====================================================================

        /// <summary>
        /// Start 内部实现（在串行门保护下执行）。
        /// </summary>
        private async UniTask<BattleOperationResult> StartInternalAsync(
            BattleLoadoutDto loadout,
            CancellationToken cancellationToken)
        {
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

            // 创建运行时取消令牌，链接调用方令牌。
            // 调用方取消会传播到运行时令牌，但内部清理不受影响（finally 块使用独立令牌）。
            CancellationTokenSource runtimeCts = new CancellationTokenSource();
            scope.TrackCancellationTokenSource(runtimeCts, "runtime-cts");

            // 链接调用方取消令牌：调用方取消传播到运行时加载步骤。
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                runtimeCts.Token, cancellationToken);

            try
            {
                // 执行加载步骤委托。
                BattleOperationResult entryResult =
                    await _entryHandler(loadout, scope, linkedCts.Token);

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
            catch (OperationCanceledException)
            {
                // 调用方取消：不能绕过内部清理。
                // 对应 task 2.6："调用方取消不能绕过内部清理"。
                // 先执行反向回滚，再重新抛出取消异常。
                Log.Info("[BattleModule] Start 被调用方取消，执行内部清理。");
                await RollbackAndReturnAsync(
                    BattleOperationResult.Fail(
                        BattleErrorCode.Unknown,
                        _state,
                        "Start 被调用方取消，内部清理已执行。"));
                throw;
            }
            catch (Exception ex)
            {
                // 非预期异常：执行回滚后进入 Faulted 状态。
                Log.Error($"[BattleModule] Start 发生非预期异常: {ex}");

                // 尝试清理已完成的部分初始化步骤。
                try
                {
                    scope.Rollback();
                }
                catch (Exception cleanupEx)
                {
                    Log.Error($"[BattleModule] 清理过程中发生异常: {cleanupEx}");
                }
                _activeScope = null;

                // 任意状态 → Faulted 是合法迁移。
                TransitionTo(BattleModuleState.Faulted);
                return BattleOperationResult.Fail(
                    BattleErrorCode.Unknown,
                    _state,
                    $"Start 发生非预期异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载失败/取消时的反向回滚辅助方法。
        /// <para>状态迁移路径：Entering → Exiting → Idle。
        /// 逆序释放 scope 中已登记的所有权，然后回到 Idle。</para>
        /// </summary>
        /// <param name="failResult">要返回的失败结果。</param>
        /// <returns>传入的失败结果（状态已更新为 Idle）。</returns>
        private UniTask<BattleOperationResult> RollbackAndReturnAsync(
            BattleOperationResult failResult)
        {
            // Entering → Exiting（合法迁移）。
            if (BattleModuleStateTransitions.CanTransition(
                _state, BattleModuleState.Exiting))
            {
                TransitionTo(BattleModuleState.Exiting);
            }

            // 执行逆序回滚。
            BattleRuntimeScope scope = _activeScope;
            _activeScope = null;

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

            // Exiting → Idle（合法迁移）。
            if (BattleModuleStateTransitions.CanTransition(
                _state, BattleModuleState.Idle))
            {
                TransitionTo(BattleModuleState.Idle);
            }

            // 返回更新了状态快照的失败结果。
            BattleOperationResult updatedResult = new BattleOperationResult(
                failResult.ErrorCode,
                _state,
                failResult.DiagnosticMessage);
            return UniTask.FromResult(updatedResult);
        }

        // ====================================================================
        // Restart 内部实现
        // ====================================================================

        /// <summary>
        /// Restart 内部实现（在串行门保护下执行）。
        /// </summary>
        private async UniTask<BattleOperationResult> RestartInternalAsync(
            BattleLoadoutDto loadout,
            CancellationToken cancellationToken)
        {
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

            // ----------------------------------------------------------
            // 销毁旧运行时的全部可变状态和局部订阅。
            // 对应 spec "Restart creates clean per-battle state"。
            // ----------------------------------------------------------
            BattleRuntimeScope oldScope = _activeScope;
            _activeScope = null;

            try
            {
                if (oldScope != null)
                {
                    oldScope.Release();
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

            CancellationTokenSource runtimeCts = new CancellationTokenSource();
            newScope.TrackCancellationTokenSource(runtimeCts, "runtime-cts");

            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                runtimeCts.Token, cancellationToken);

            try
            {
                BattleOperationResult entryResult =
                    await _entryHandler(loadout, newScope, linkedCts.Token);

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
            catch (OperationCanceledException)
            {
                // 调用方取消：先执行内部清理，再抛出取消异常。
                Log.Info("[BattleModule] Restart 被调用方取消，执行内部清理。");
                await RollbackAndReturnAsync(
                    BattleOperationResult.Fail(
                        BattleErrorCode.Unknown,
                        _state,
                        "Restart 被调用方取消，内部清理已执行。"));
                throw;
            }
            catch (Exception ex)
            {
                Log.Error($"[BattleModule] Restart 发生非预期异常: {ex}");

                try
                {
                    newScope.Rollback();
                }
                catch (Exception cleanupEx)
                {
                    Log.Error($"[BattleModule] Restart 清理异常: {cleanupEx}");
                }
                _activeScope = null;

                TransitionTo(BattleModuleState.Faulted);
                return BattleOperationResult.Fail(
                    BattleErrorCode.Unknown,
                    _state,
                    $"Restart 发生非预期异常: {ex.Message}");
            }
        }

        // ====================================================================
        // Exit 内部实现
        // ====================================================================

        /// <summary>
        /// Exit 内部实现（在串行门保护下执行）。
        /// </summary>
        /// <remarks>
        /// Exit 在任意状态幂等：
        /// - Idle 状态调用 Exit 直接返回成功（幂等，无活动运行时需清理）。
        /// - 其他状态执行退出清理后回到 Idle。
        /// - 对应 spec "Exit is idempotent and concurrent-safe"。
        /// </remarks>
        private async UniTask<BattleOperationResult> ExitInternalAsync(
            CancellationToken cancellationToken)
        {
            // ----------------------------------------------------------
            // 幂等检查：Idle 状态直接返回成功。
            // ----------------------------------------------------------
            if (_state == BattleModuleState.Idle)
            {
                // 退出完成后再次调用返回已完成的幂等结果。
                return BattleOperationResult.Ok(BattleModuleState.Idle);
            }

            // ----------------------------------------------------------
            // Faulted 状态：Exit 作为清理路径，Faulted → Idle（清理后）。
            // 但迁移表中 Faulted → Exiting 不合法，Faulted 只能直接 → Idle。
            // 因此 Faulted 状态的 Exit 直接执行清理然后迁移到 Idle。
            // ----------------------------------------------------------
            if (_state == BattleModuleState.Faulted)
            {
                return await ExitFromFaultedAsync(cancellationToken);
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

            // 执行退出清理步骤。
            BattleRuntimeScope scopeToRelease = _activeScope;
            _activeScope = null;

            // 使用独立取消令牌执行清理，调用方取消不能绕过内部清理。
            // 对应 task 2.6："调用方取消不能绕过内部清理"。
            using CancellationTokenSource cleanupCts = new CancellationTokenSource();

            try
            {
                // 即使调用方取消，清理仍使用独立令牌执行。
                BattleOperationResult exitResult =
                    await _exitHandler(scopeToRelease, cleanupCts.Token);

                if (!exitResult.IsSuccess)
                {
                    // 清理步骤返回失败，记录但继续完成退出（保证回到 Idle）。
                    Log.Warning(
                        $"[BattleModule] Exit 清理步骤返回失败: {exitResult.ErrorCode}");
                }
            }
            catch (Exception ex)
            {
                // 清理异常不阻断退出流程，记录后继续回到 Idle。
                Log.Error($"[BattleModule] Exit 清理异常: {ex}");
            }
            finally
            {
                // 确保 scope 被释放（幂等）。
                if (scopeToRelease != null && !scopeToRelease.IsDisposed)
                {
                    try
                    {
                        scopeToRelease.Release();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[BattleModule] Exit scope 释放异常: {ex}");
                    }
                }
            }

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
        private async UniTask<BattleOperationResult> ExitFromFaultedAsync(
            CancellationToken cancellationToken)
        {
            // Faulted 状态可能残留未清理的 scope。
            BattleRuntimeScope scopeToRelease = _activeScope;
            _activeScope = null;

            using CancellationTokenSource cleanupCts = new CancellationTokenSource();

            try
            {
                // 即使 scope 为 null，也调用清理委托执行其他退出步骤（如关闭 UI）。
                await _exitHandler(scopeToRelease, cleanupCts.Token);
            }
            catch (Exception ex)
            {
                Log.Error($"[BattleModule] Faulted 清理异常: {ex}");
            }
            finally
            {
                if (scopeToRelease != null && !scopeToRelease.IsDisposed)
                {
                    try
                    {
                        scopeToRelease.Release();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[BattleModule] Faulted scope 释放异常: {ex}");
                    }
                }
            }

            // Faulted → Idle（清理后恢复）。
            if (BattleModuleStateTransitions.CanTransition(
                _state, BattleModuleState.Idle))
            {
                TransitionTo(BattleModuleState.Idle);
            }

            return BattleOperationResult.Ok(_state);
        }

        // ====================================================================
        // 强制清理（TEngine Shutdown 路径）
        // ====================================================================

        /// <summary>
        /// 强制清理内部状态（TEngine Shutdown 时调用，不经过串行门）。
        /// <para>对应 spec "Exit releases battle-owned state" 的框架级关闭路径。
        /// 不抛异常，尽可能释放所有资源。</para>
        /// </summary>
        private void ForceCleanupInternal()
        {
            BattleRuntimeScope scope = _activeScope;
            _activeScope = null;
            _pendingExitTcs = null;

            if (scope != null && !scope.IsDisposed)
            {
                try
                {
                    scope.Release();
                }
                catch (Exception ex)
                {
                    Log.Error($"[BattleModule] 强制清理异常: {ex}");
                }
            }

            _state = BattleModuleState.Idle;
        }

        // ====================================================================
        // 状态迁移辅助
        // ====================================================================

        /// <summary>
        /// 执行状态迁移（在串行门保护下调用）。
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
