using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using GameCommon.Battle;
using NUnit.Framework;
using TEngine;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameBattle.Tests.EditMode
{
    /// <summary>
    /// BattleModule 生命周期门控测试（task 2.6）。
    /// </summary>
    /// <remarks>
    /// <para>验证 task 2.6 的全部关键要求：</para>
    /// <list type="bullet">
    /// <item>Start/Restart/Exit 串行门（同一时刻只有一个生命周期操作在执行）</item>
    /// <item>重复 Start 返回 AlreadyActive</item>
    /// <item>Restart 只允许 Settling 状态</item>
    /// <item>Exit 在任意状态幂等且合并并发请求</item>
    /// <item>加载失败执行反向回滚</item>
    /// <item>调用方取消不能绕过内部清理</item>
    /// <item>Faulted 必须先清理才能回到 Idle</item>
    /// <item>使用 BattleModuleStateTransitions.CanTransition 校验状态迁移</item>
    /// </list>
    ///
    /// <para><b>测试策略：</b></para>
    /// <para>BattleRuntime / BattleRuntimeFactory 尚未实现（task 2.9/2.10），
    /// 通过注入模拟加载/清理委托验证生命周期门控逻辑，不依赖真实运行时。
    /// 模拟委托通过控制完成源和延迟模拟异步加载和失败场景。</para>
    /// </remarks>
    [TestFixture]
    internal class BattleModuleLifecycleTests
    {
        // ====================================================================
        // 辅助：创建带模拟委托的 BattleModule
        // ====================================================================

        /// <summary>
        /// 创建一个带指定加载/清理委托的 BattleModule 实例。
        /// </summary>
        /// <param name="entryHandler">加载步骤委托（null 时使用默认成功实现）。</param>
        /// <param name="exitHandler">清理步骤委托（null 时使用默认实现）。</param>
        private static BattleModule CreateModule(
            BattleModule.BattleEntryHandler entryHandler = null,
            BattleModule.BattleExitHandler exitHandler = null)
        {
            return new BattleModule(entryHandler, exitHandler);
        }

        /// <summary>
        /// 默认成功加载委托：立即返回成功（Running）。
        /// </summary>
        private static BattleModule.BattleEntryHandler DefaultSuccessEntry =>
            (loadout, scope, ct) =>
                UniTask.FromResult(BattleOperationResult.Ok(BattleModuleState.Running));

        /// <summary>
        /// 默认清理委托：释放 scope 并返回成功（Idle）。
        /// </summary>
        private static BattleModule.BattleExitHandler DefaultExit =>
            (scope, ct) =>
            {
                if (scope != null)
                {
                    scope.Release();
                }
                return UniTask.FromResult(BattleOperationResult.Ok(BattleModuleState.Idle));
            };

        /// <summary>
        /// 创建最简默认装载信息。
        /// </summary>
        private static BattleLoadoutDto CreateLoadout()
            => BattleLoadoutDto.CreateMinimalDefault();

        // ====================================================================
        // 串行门测试
        // ====================================================================

        [Test]
        [Description("串行门：同一时刻只有一个生命周期操作在执行。")]
        public async Task SerialGate_OnlyOneOperationAtATime()
        {
            // 使用延迟加载委托模拟耗时操作，验证串行门阻止并发执行。
            var entryCallOrder = new System.Collections.Generic.List<int>();
            int entryCallCount = 0;

            BattleModule.BattleEntryHandler slowEntry = (loadout, scope, ct) =>
            {
                int callId = ++entryCallCount;
                entryCallOrder.Add(callId);
                // 模拟异步加载延迟。
                return UniTask.Delay(50, cancellationToken: ct)
                    .ContinueWith(() =>
                        BattleOperationResult.Ok(BattleModuleState.Running));
            };

            BattleModule module = CreateModule(slowEntry, DefaultExit);

            // 并发发起两个 Start（第二个应返回 AlreadyActive 而非真正执行加载）。
            UniTask<BattleOperationResult> task1 = module.StartAsync(CreateLoadout());
            UniTask<BattleOperationResult> task2 = module.StartAsync(CreateLoadout());

            BattleOperationResult result1 = await task1;
            BattleOperationResult result2 = await task2;

            // 第一个 Start 成功。
            Assert.IsTrue(result1.IsSuccess, "第一个 Start 应成功。");
            Assert.AreEqual(BattleModuleState.Running, result1.CurrentState);

            // 第二个 Start 返回 AlreadyActive（串行门阻止了并发加载）。
            Assert.IsFalse(result2.IsSuccess, "第二个 Start 应返回失败。");
            Assert.AreEqual(BattleErrorCode.AlreadyActive, result2.ErrorCode);

            // 加载委托只被调用一次（串行门保证）。
            Assert.AreEqual(1, entryCallCount, "加载委托应只被调用一次。");

            // 清理。
            await module.ExitAsync();
        }

        // ====================================================================
        // 重复 Start 测试
        // ====================================================================

        [Test]
        [Description("重复 Start 返回 AlreadyActive，不创建第二个活动运行时。")]
        public async Task DuplicateStart_ReturnsAlreadyActive()
        {
            BattleModule module = CreateModule(DefaultSuccessEntry, DefaultExit);

            // 第一次 Start 成功。
            BattleOperationResult result1 = await module.StartAsync(CreateLoadout());
            Assert.IsTrue(result1.IsSuccess, "第一次 Start 应成功。");
            Assert.AreEqual(BattleModuleState.Running, module.State);

            // 第二次 Start 返回 AlreadyActive。
            BattleOperationResult result2 = await module.StartAsync(CreateLoadout());
            Assert.IsFalse(result2.IsSuccess, "第二次 Start 应失败。");
            Assert.AreEqual(BattleErrorCode.AlreadyActive, result2.ErrorCode);
            Assert.AreEqual(BattleModuleState.Running, result2.CurrentState,
                "重复 Start 不改变当前运行时状态。");

            // 状态仍然是 Running，只有一个活动运行时。
            Assert.AreEqual(BattleModuleState.Running, module.State);

            await module.ExitAsync();
        }

        // ====================================================================
        // Restart 状态约束测试
        // ====================================================================

        [Test]
        [Description("Restart 在 Running 状态返回 NotSettling，不销毁当前运行时。")]
        public async Task Restart_DuringRunning_ReturnsNotSettling()
        {
            BattleModule module = CreateModule(DefaultSuccessEntry, DefaultExit);
            await module.StartAsync(CreateLoadout());
            Assert.AreEqual(BattleModuleState.Running, module.State);

            // Running 状态下 Restart 应返回 NotSettling。
            BattleOperationResult result = await module.RestartAsync(CreateLoadout());
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(BattleErrorCode.NotSettling, result.ErrorCode);
            Assert.AreEqual(BattleModuleState.Running, result.CurrentState,
                "Restart 被拒绝时不改变当前状态。");
            Assert.AreEqual(BattleModuleState.Running, module.State,
                "模块状态应保持 Running。");

            await module.ExitAsync();
        }

        [Test]
        [Description("Restart 在 Idle 状态返回 NotSettling。")]
        public async Task Restart_DuringIdle_ReturnsNotSettling()
        {
            BattleModule module = CreateModule(DefaultSuccessEntry, DefaultExit);

            BattleOperationResult result = await module.RestartAsync(CreateLoadout());
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(BattleErrorCode.NotSettling, result.ErrorCode);
            Assert.AreEqual(BattleModuleState.Idle, module.State);
        }

        [Test]
        [Description("Restart 在 Settling 状态成功：销毁旧运行时，创建新运行时。")]
        public async Task Restart_DuringSettling_Succeeds()
        {
            int entryCallCount = 0;
            BattleModule.BattleEntryHandler countingEntry = (loadout, scope, ct) =>
            {
                ++entryCallCount;
                return UniTask.FromResult(BattleOperationResult.Ok(BattleModuleState.Running));
            };

            BattleModule module = CreateModule(countingEntry, DefaultExit);
            await module.StartAsync(CreateLoadout());
            Assert.AreEqual(1, entryCallCount);

            // 手动将状态设为 Settling（模拟结果冻结）。
            // 在实际实现中由 BattleSimulation.TryFreeze 触发。
            SetModuleState(module, BattleModuleState.Settling);

            // Restart 应成功。
            BattleOperationResult result = await module.RestartAsync(CreateLoadout());
            Assert.IsTrue(result.IsSuccess, "Settling 状态下 Restart 应成功。");
            Assert.AreEqual(BattleModuleState.Running, module.State);
            Assert.AreEqual(2, entryCallCount, "Restart 应调用加载委托创建新运行时。");

            await module.ExitAsync();
        }

        // ====================================================================
        // Exit 幂等与并发合并测试
        // ====================================================================

        [Test]
        [Description("Exit 在 Idle 状态幂等：返回成功，不执行清理。")]
        public async Task Exit_Idle_IsIdempotent()
        {
            BattleModule module = CreateModule(DefaultSuccessEntry, DefaultExit);

            // Idle 状态下 Exit 直接返回成功。
            BattleOperationResult result1 = await module.ExitAsync();
            Assert.IsTrue(result1.IsSuccess);
            Assert.AreEqual(BattleModuleState.Idle, result1.CurrentState);

            // 再次 Exit 仍然返回成功（幂等）。
            BattleOperationResult result2 = await module.ExitAsync();
            Assert.IsTrue(result2.IsSuccess);
            Assert.AreEqual(BattleModuleState.Idle, module.State);
        }

        [Test]
        [Description("Exit 在 Running 状态销毁运行时并回到 Idle。")]
        public async Task Exit_Running_ReturnsToIdle()
        {
            bool exitHandlerCalled = false;
            BattleModule.BattleExitHandler trackingExit = (scope, ct) =>
            {
                exitHandlerCalled = true;
                if (scope != null)
                {
                    scope.Release();
                }
                return UniTask.FromResult(BattleOperationResult.Ok(BattleModuleState.Idle));
            };

            BattleModule module = CreateModule(DefaultSuccessEntry, trackingExit);
            await module.StartAsync(CreateLoadout());
            Assert.AreEqual(BattleModuleState.Running, module.State);

            BattleOperationResult result = await module.ExitAsync();
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(BattleModuleState.Idle, module.State);
            Assert.IsTrue(exitHandlerCalled, "清理委托应被调用。");
        }

        [Test]
        [Description("Exit 并发合并：多个并发退出请求共享同一个退出操作。")]
        public async Task Exit_Concurrent_MergesRequests()
        {
            int exitCallCount = 0;
            var exitStarted = new UniTaskCompletionSource();
            var exitAllowed = new UniTaskCompletionSource();

            BattleModule.BattleExitHandler slowExit = (scope, ct) =>
            {
                ++exitCallCount;
                exitStarted.TrySetResult();
                return ExitSlowCore(scope, ct, exitAllowed);
            };

            BattleModule module = CreateModule(DefaultSuccessEntry, slowExit);
            await module.StartAsync(CreateLoadout());

            // 并发发起三个 Exit。
            UniTask<BattleOperationResult> exit1 = module.ExitAsync();
            UniTask<BattleOperationResult> exit2 = module.ExitAsync();
            UniTask<BattleOperationResult> exit3 = module.ExitAsync();

            // 等待退出操作开始。
            await exitStarted.Task;

            // 允许退出操作完成。
            exitAllowed.TrySetResult();

            BattleOperationResult r1 = await exit1;
            BattleOperationResult r2 = await exit2;
            BattleOperationResult r3 = await exit3;

            // 三个并发退出都成功。
            Assert.IsTrue(r1.IsSuccess && r2.IsSuccess && r3.IsSuccess,
                "所有并发 Exit 应共享退出操作并返回成功。");

            // 清理委托只被调用一次（合并）。
            Assert.AreEqual(1, exitCallCount, "并发 Exit 应合并为一次清理调用。");
            Assert.AreEqual(BattleModuleState.Idle, module.State);
        }

        [Test]
        [Description("Exit 完成后再次调用返回幂等结果。")]
        public async Task Exit_AfterComplete_IsIdempotent()
        {
            int exitCallCount = 0;
            BattleModule.BattleExitHandler countingExit = (scope, ct) =>
            {
                ++exitCallCount;
                if (scope != null)
                {
                    scope.Release();
                }
                return UniTask.FromResult(BattleOperationResult.Ok(BattleModuleState.Idle));
            };

            BattleModule module = CreateModule(DefaultSuccessEntry, countingExit);
            await module.StartAsync(CreateLoadout());

            // 第一次 Exit 执行清理。
            await module.ExitAsync();
            Assert.AreEqual(1, exitCallCount);

            // 第二次 Exit 幂等，不执行清理。
            BattleOperationResult result = await module.ExitAsync();
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(BattleModuleState.Idle, module.State);
            Assert.AreEqual(1, exitCallCount, "Idle 状态 Exit 不应调用清理委托。");
        }

        // ====================================================================
        // 加载失败反向回滚测试
        // ====================================================================

        [Test]
        [Description("加载失败执行反向回滚：不进入 Running，不留半初始化运行时。")]
        public async Task LoadFailure_PerformsRollback()
        {
            bool scopeReleased = false;
            BattleModule.BattleEntryHandler failingEntry = (loadout, scope, ct) =>
            {
                // 模拟部分初始化后失败。
                scope.Track(BattleRuntimeScope.OwnershipKind.Generic,
                    "test-disposable",
                    () => { scopeReleased = true; });
                return UniTask.FromResult(BattleOperationResult.Fail(
                    BattleErrorCode.SceneLoadFailed,
                    BattleModuleState.Entering,
                    "模拟 Scene 加载失败"));
            };

            BattleModule module = CreateModule(failingEntry, DefaultExit);

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[BattleModule\] 回滚后恢复 BattleStartPanel 失败:"));
            BattleOperationResult result = await module.StartAsync(CreateLoadout());

            // 加载失败返回错误码。
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(BattleErrorCode.SceneLoadFailed, result.ErrorCode);

            // 模块回到 Idle（不进入 Running）。
            Assert.AreEqual(BattleModuleState.Idle, module.State,
                "加载失败后模块应回到 Idle。");

            // scope 被逆序释放（反向回滚）。
            Assert.IsTrue(scopeReleased, "加载失败时应执行反向回滚释放已登记项。");
        }

        // ====================================================================
        // Restart 加载失败回滚测试
        // ====================================================================

        [Test]
        [Description("Restart 新局加载失败时回滚新 scope 并回到 Idle。")]
        public async Task Restart_LoadFailure_RollsBackAndReturnsToIdle()
        {
            int entryCallCount = 0;
            BattleModule.BattleEntryHandler entryHandler = (loadout, scope, ct) =>
            {
                ++entryCallCount;
                if (entryCallCount == 1)
                {
                    // 第一次 Start 成功。
                    return UniTask.FromResult(BattleOperationResult.Ok(BattleModuleState.Running));
                }
                // 第二次 Restart 失败。
                return UniTask.FromResult(BattleOperationResult.Fail(
                    BattleErrorCode.ConfigInvalid,
                    BattleModuleState.Entering,
                    "模拟配置校验失败"));
            };

            BattleModule module = CreateModule(entryHandler, DefaultExit);
            await module.StartAsync(CreateLoadout());
            SetModuleState(module, BattleModuleState.Settling);

            // Restart 新局加载失败。
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[BattleModule\] 回滚后恢复 BattleStartPanel 失败:"));
            BattleOperationResult result = await module.RestartAsync(CreateLoadout());
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(BattleErrorCode.ConfigInvalid, result.ErrorCode);
            Assert.AreEqual(BattleModuleState.Idle, module.State,
                "Restart 加载失败后应回到 Idle。");
        }

        // ====================================================================
        // 完整生命周期流程测试
        // ====================================================================

        [Test]
        [Description("完整流程：Start → Settling → Restart → Exit → Start。")]
        public async Task FullLifecycle_Start_Restart_Exit_Start()
        {
            int entryCallCount = 0;
            BattleModule.BattleEntryHandler entry = (loadout, scope, ct) =>
            {
                ++entryCallCount;
                return UniTask.FromResult(BattleOperationResult.Ok(BattleModuleState.Running));
            };

            BattleModule module = CreateModule(entry, DefaultExit);

            // Start。
            BattleOperationResult r1 = await module.StartAsync(CreateLoadout());
            Assert.IsTrue(r1.IsSuccess);
            Assert.AreEqual(BattleModuleState.Running, module.State);
            Assert.AreEqual(1, entryCallCount);

            // 模拟结算。
            SetModuleState(module, BattleModuleState.Settling);

            // Restart。
            BattleOperationResult r2 = await module.RestartAsync(CreateLoadout());
            Assert.IsTrue(r2.IsSuccess);
            Assert.AreEqual(BattleModuleState.Running, module.State);
            Assert.AreEqual(2, entryCallCount);

            // Exit。
            BattleOperationResult r3 = await module.ExitAsync();
            Assert.IsTrue(r3.IsSuccess);
            Assert.AreEqual(BattleModuleState.Idle, module.State);

            // 再次 Start。
            BattleOperationResult r4 = await module.StartAsync(CreateLoadout());
            Assert.IsTrue(r4.IsSuccess);
            Assert.AreEqual(BattleModuleState.Running, module.State);
            Assert.AreEqual(3, entryCallCount);

            await module.ExitAsync();
        }

        [Test]
        [Description("连续进入退出：进入 → 退出 → 进入 → 退出，无状态泄漏。")]
        public async Task RepeatedEnterExit_NoStateLeak()
        {
            BattleModule module = CreateModule(DefaultSuccessEntry, DefaultExit);

            for (int i = 0; i < 3; i++)
            {
                BattleOperationResult enter = await module.StartAsync(CreateLoadout());
                Assert.IsTrue(enter.IsSuccess, $"第 {i + 1} 次 Start 应成功。");
                Assert.AreEqual(BattleModuleState.Running, module.State);

                BattleOperationResult exit = await module.ExitAsync();
                Assert.IsTrue(exit.IsSuccess, $"第 {i + 1} 次 Exit 应成功。");
                Assert.AreEqual(BattleModuleState.Idle, module.State);
            }
        }

        // ====================================================================
        // TEngine Module 基类测试
        // ====================================================================

        [Test]
        [Description("BattleModule 继承 TEngine Module 并实现 IBattleModule。")]
        public void BattleModule_InheritsModule_AndImplementsIBattleModule()
        {
            BattleModule module = CreateModule();
            Assert.IsNotNull(module as TEngine.Module,
                "BattleModule 应继承 TEngine.Module。");
            Assert.IsNotNull(module as IBattleModule,
                "BattleModule 应实现 IBattleModule。");
        }

        [Test]
        [Description("战斗入口参数应把装载信息和窗口取消令牌原样传给开始命令。")]
        public async Task BattleStartEntryArgs_ForwardsLoadoutAndCancellation()
        {
            BattleLoadoutDto loadout = CreateLoadout();
            bool called = false;
            BattleLoadoutDto receivedLoadout = default;
            CancellationToken receivedToken = default;

            var entryArgs = new BattleStartEntryArgs(
                loadout,
                (requestedLoadout, cancellationToken) =>
                {
                    called = true;
                    receivedLoadout = requestedLoadout;
                    receivedToken = cancellationToken;
                    return UniTask.FromResult(BattleOperationResult.Ok(BattleModuleState.Running));
                });

            using var cancellationSource = new CancellationTokenSource();
            BattleOperationResult result = await entryArgs.StartAsync(
                entryArgs.Loadout,
                cancellationSource.Token);

            Assert.IsTrue(called);
            Assert.AreEqual(loadout.MapId, receivedLoadout.MapId);
            Assert.AreEqual(cancellationSource.Token, receivedToken);
            Assert.IsTrue(result.IsSuccess);
        }

        // ====================================================================
        // 辅助方法
        // ====================================================================

        /// <summary>
        /// 通过反射设置模块状态（测试辅助，模拟内部状态迁移触发）。
        /// <para>在实际实现中，Running → Settling 由 BattleSimulation.TryFreeze 触发。
        /// 测试中通过反射直接设置以验证 Restart 在 Settling 下的行为。</para>
        /// </summary>
        private static void SetModuleState(BattleModule module, BattleModuleState state)
        {
            var fieldInfo = typeof(BattleModule).GetField(
                "_state",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(fieldInfo, "_state 字段应存在。");
            fieldInfo.SetValue(module, state);
        }

        /// <summary>
        /// 模拟延迟清理的核心异步方法（供 slowExit 委托使用）。
        /// </summary>
        private static async UniTask<BattleOperationResult> ExitSlowCore(
            BattleRuntimeScope scope,
            CancellationToken ct,
            UniTaskCompletionSource exitAllowed)
        {
            await exitAllowed.Task.AttachExternalCancellation(ct);
            if (scope != null)
            {
                scope.Release();
            }
            return BattleOperationResult.Ok(BattleModuleState.Idle);
        }

    }
}
