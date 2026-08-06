using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameCommon.Battle;
using GameConfig;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 2.9：BattleRuntimeFactory
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / specs/battle-runtime-lifecycle/spec.md）：
    //   校验输入与配置并以强类型构造注入组装一局运行时的全部依赖，替代还原工程
    //   字符串服务容器 CombatServices 与隐式全局单例 SingletonBase（design 决策 5）。
    //
    //   本类型只负责“组装依赖与初始化步骤”，不创建 BattleRuntime（task 2.10 范围）。
    //   组装产物通过 BattleRuntimeAssembly 返回，由 BattleRuntime（task 2.10）在构造时
    //   接管其所有权。Factory 不持有跨局状态，每次 Create 产生独立的作用域与产物。
    //
    // 强类型组装原则（替代字符串服务容器）：
    //   1. 所有依赖通过构造函数或 Create 参数显式注入，不通过字符串 key 查找服务。
    //   2. 不使用 static 可变单例、不使用 ServiceLocator 模式。
    //   3. 每个依赖都有明确的所有者：要么由调用方外部持有并注入，要么由本次组装的
    //      BattleRuntimeScope 登记所有权、随产物生命周期释放。
    //
    // 部分初始化回滚（spec "Partial initialization is recoverable"）：
    //   任一组装步骤失败时，只回滚本次 Create 已取得的所有权（通过 scope.Rollback()），
    //   不触碰调用方持有的外部注入对象。回滚后作用域终结，产物标记为失败，
    //   调用方（BattleModule）据错误码返回结构化失败结果，不留下半初始化运行时。
    //
    // 初始化步骤日志：
    //   每个关键组装步骤在开始与完成时通过 TEngine Log 记录，便于诊断部分初始化失败
    //   的具体阶段。日志只在 ENABLE_LOG 相关预编译选项下实际输出（见 Log.cs）。
    // ============================================================================

    /// <summary>
    /// 强类型一局运行时组装产物。
    /// <para>由 <see cref="BattleRuntimeFactory.Create"/> 产生，持有本次组装成功的全部
    /// 依赖与对应的所有权作用域。<see cref="BattleRuntime"/>（task 2.10）在构造时接管
    /// 本产物的所有权；若组装失败则产物不可用，调用方应丢弃并据错误码反馈。</para>
    /// <para>本类型为 internal，只供 GameBattle 内部 <c>BattleModule</c>/<c>BattleRuntime</c>
    /// 使用，不对其他程序集暴露。</para>
    /// </summary>
    internal sealed class BattleRuntimeAssembly
    {
        /// <summary>
        /// 本次组装是否成功。等价于 <see cref="ErrorCode"/> == <see cref="BattleErrorCode.None"/>。
        /// </summary>
        public bool IsSuccess => ErrorCode == BattleErrorCode.None;

        /// <summary>
        /// 组装失败的稳定错误码。成功时为 <see cref="BattleErrorCode.None"/>。
        /// 调用方以此做程序化判断，不依赖 <see cref="DiagnosticMessage"/> 文本。
        /// </summary>
        public readonly BattleErrorCode ErrorCode;

        /// <summary>
        /// 诊断信息（仅用于日志）。调用方 MUST NOT 解析此文本判断失败原因。
        /// 成功时为空串。
        /// </summary>
        public readonly string DiagnosticMessage;

        /// <summary>
        /// 本次组装取得的所有权作用域。成功时由 <c>BattleRuntime</c> 接管；
        /// 失败时已被 <see cref="BattleRuntimeFactory"/> 回滚释放，调用方不应再使用。
        /// </summary>
        /// <remarks>
        /// 失败回滚后作用域已 Dispose，访问其成员安全但无意义。此字段始终非 null，
        /// 便于调用方在成功路径统一接管。
        /// </remarks>
        public readonly BattleRuntimeScope Scope;

        /// <summary>
        /// 本次组装产生的强类型模拟器。由 Factory 构造并登记到 Scope。
        /// <see cref="BattleRuntime"/> 接管后持有并驱动其 <c>Advance</c>。
        /// </summary>
        public readonly BattleSimulation Simulation;

        /// <summary>
        /// 本次组装产生的装载信息（只读副本，供 Runtime 读取种子/地图/牌组预设）。
        /// </summary>
        public readonly BattleLoadoutDto Loadout;

        /// <summary>
        /// 本次组装产生的运行时取消令牌源。由 Factory 构造并登记到 Scope，
        /// 用于取消本局所有异步操作与表现回调。
        /// </summary>
        public readonly CancellationTokenSource RuntimeTokenSource;

        /// <summary>
        /// 本次组装从应用级 ConfigSystem.Tables 复制的不可变战斗配置快照
        /// （task 3.4 / specs/battle-config-snapshot "Runtime consumes an immutable
        /// configuration snapshot"）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 在组装阶段通过
        /// <see cref="LubanBattleConfigProvider"/> 从 <see cref="ConfigSystem.Instance.Tables"/>
        /// 复制一次，之后运行时只持有本快照，不再访问资源加载器或可变全局配置表，
        /// 也不由 <see cref="BattleRuntime"/> 卸载应用级配置资源（决策 0.11）。</para>
        /// </summary>
        public readonly BattleConfigSnapshot ConfigSnapshot;

        /// <summary>
        /// 本次组装产生的攻击效果管理器（task 5.3 产物）。
        /// <para>由 Factory 构造，<see cref="BattleRuntime"/> 接管后持有。
        /// Settling 静默清理步骤 3 调用 <c>Clear()</c> 取消全部活动攻击效果。</para>
        /// </summary>
        public readonly AttackEffectManager AttackEffectManager;

        /// <summary>
        /// 本次组装产生的投射物管理器（task 5.8 产物）。
        /// <para>由 Factory 构造，<see cref="BattleRuntime"/> 接管后持有。
        /// Settling 静默清理步骤 4 调用 <c>Clear()</c> 取消全部空中弹道。</para>
        /// </summary>
        public readonly ProjectileManager ProjectileManager;

        /// <summary>
        /// 本次组装产生的单位工厂（task 6.3 产物）。
        /// <para>由 Factory 构造，<see cref="BattleRuntime"/> 接管后持有。只识别四个强类型
        /// 兵种 ID（刀/弓/枪/骑），未知兵种显式失败。使用四个独立的
        /// <see cref="BattleObjectPool{T}"/> 池化四兵，Acquire 分配新 RuntimeId，
        /// Release 回收并 Reset。</para>
        /// <para>Settling 静默清理时由 <see cref="UnitRegistry"/> 经本工厂 Release 全部活动单位。</para>
        /// </summary>
        public readonly UnitFactory UnitFactory;

        /// <summary>
        /// 本次组装产生的单位注册表（task 6.3 产物）。
        /// <para>由 Factory 构造，<see cref="BattleRuntime"/> 接管后持有。管理单位注册、
        /// 放置、移除和战斗结束清理，维护稳定有序集合供 AttackScheduler 遍历。</para>
        /// <para>Settling 静默清理步骤 6 调用 <c>ClearForSettling()</c> 移除全部活动单位
        /// 并归还池（spec "Runtime quiescence and cleanup have one ordered owner"）。</para>
        /// </summary>
        public readonly UnitRegistry UnitRegistry;

        /// <summary>
        /// 本次组装产生的输入命令执行控制器（task 6.7 产物）。
        /// <para>由 Factory 构造，<see cref="BattleRuntime"/> 接管后持有。原子执行购买放置
        /// 和刷新命令，任一步失败按逆序补偿。随 Runtime 销毁，不跨局复用。</para>
        /// <para>注入依赖：UnitFactory / UnitRegistry / BattleEconomy / DeckManager /
        /// PlacementReservationRegistry / MapData / BattleConfigSnapshot（全部由本 Factory
        /// 在本次组装中已构造）。</para>
        /// </summary>
        public readonly BattleInputController InputController;

        private BattleRuntimeAssembly(
            BattleErrorCode errorCode,
            string diagnosticMessage,
            BattleRuntimeScope scope,
            BattleSimulation simulation,
            BattleLoadoutDto loadout,
            CancellationTokenSource runtimeTokenSource,
            BattleConfigSnapshot configSnapshot,
            AttackEffectManager attackEffectManager,
            ProjectileManager projectileManager,
            UnitFactory unitFactory,
            UnitRegistry unitRegistry,
            BattleInputController inputController)
        {
            ErrorCode = errorCode;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
            Scope = scope;
            Simulation = simulation;
            Loadout = loadout;
            RuntimeTokenSource = runtimeTokenSource;
            ConfigSnapshot = configSnapshot;
            AttackEffectManager = attackEffectManager;
            ProjectileManager = projectileManager;
            UnitFactory = unitFactory;
            UnitRegistry = unitRegistry;
            InputController = inputController;
        }

        /// <summary>
        /// 构造成功产物。
        /// </summary>
        internal static BattleRuntimeAssembly Ok(
            BattleRuntimeScope scope,
            BattleSimulation simulation,
            BattleLoadoutDto loadout,
            CancellationTokenSource runtimeTokenSource,
            BattleConfigSnapshot configSnapshot,
            AttackEffectManager attackEffectManager,
            ProjectileManager projectileManager,
            UnitFactory unitFactory,
            UnitRegistry unitRegistry,
            BattleInputController inputController)
            => new BattleRuntimeAssembly(
                BattleErrorCode.None,
                string.Empty,
                scope,
                simulation,
                loadout,
                runtimeTokenSource,
                configSnapshot,
                attackEffectManager,
                projectileManager,
                unitFactory,
                unitRegistry,
                inputController);

        /// <summary>
        /// 构造失败产物。失败时 scope 已被 Factory 回滚释放。
        /// </summary>
        internal static BattleRuntimeAssembly Fail(
            BattleErrorCode errorCode,
            string diagnosticMessage,
            BattleRuntimeScope rolledBackScope)
            => new BattleRuntimeAssembly(
                errorCode,
                diagnosticMessage,
                rolledBackScope,
                simulation: null,
                loadout: default,
                runtimeTokenSource: null,
                configSnapshot: null,
                attackEffectManager: null,
                projectileManager: null,
                unitFactory: null,
                unitRegistry: null,
                inputController: null);
    }

    /// <summary>
    /// 强类型一局运行时依赖组装工厂（task 2.9）。
    /// </summary>
    /// <remarks>
    /// <para><b>设计依据（design.md 决策 5 / specs/battle-runtime-lifecycle/spec.md
    /// "Partial initialization is recoverable"）：</b></para>
    /// <para>替代还原工程的字符串服务容器 <c>CombatServices</c> 与隐式全局单例
    /// <c>SingletonBase</c>。所有运行时依赖通过强类型构造注入组装，不通过字符串 key
    /// 查找服务，不依赖 static 可变全局状态。每个组装步骤登记所有权到
    /// <see cref="BattleRuntimeScope"/>，任一步骤失败时只回滚本次已取得的所有权
    /// （<see cref="BattleRuntimeScope.Rollback"/>），不触碰调用方持有的外部注入对象。</para>
    ///
    /// <para><b>职责边界（与 task 2.10 BattleRuntime 的分工）：</b></para>
    /// <list type="bullet">
    /// <item>Factory 只负责校验输入、构造模拟器/令牌等强类型依赖、登记所有权、记录初始化步骤日志。</item>
    /// <item>Factory 不创建 <c>BattleRuntime</c>；<c>BattleRuntime</c> 由 task 2.10 实现，
    /// 在构造时接管本工厂产生的 <see cref="BattleRuntimeAssembly"/> 所有权。</item>
    /// <item>当 task 2.10 尚未实现时，Factory 返回的 Assembly 携带已组装的依赖与 Scope，
    /// 调用方可据错误码判断成功/失败；task 2.10 实现后由 Runtime 连接这些依赖。</item>
    /// </list>
    ///
    /// <para><b>无隐式全局单例：</b>本类型为无状态工具类，不持有任何 static 可变字段。
    /// 每次 <see cref="Create"/> 产生独立的 Scope 与 Assembly，不跨调用共享状态。</para>
    ///
    /// <para><b>初始化步骤日志：</b>每个关键组装步骤通过 <see cref="Log"/> 记录 Info 级别日志，
    /// 失败步骤记录 Warning/Error。日志只在 ENABLE_LOG 预编译选项下实际输出。</para>
    /// </remarks>
    internal static class BattleRuntimeFactory
    {
        /// <summary>
        /// 日志标签前缀，便于在日志中筛选战斗运行时组装相关条目。
        /// </summary>
        private const string LogTag = "[BattleRuntimeFactory]";

        /// <summary>
        /// 强类型组装一局运行时依赖。
        /// </summary>
        /// <param name="loadout">
        /// 不可变战斗装载信息（地图、种子、配置版本/hash 占位、牌组预设）。
        /// 由调用方（<c>BattleModule</c>）在 Start/Restart 时传入。
        /// </param>
        /// <param name="cancellationToken">
        /// 组装过程取消令牌。组装本身为同步操作，但取消会中止后续步骤并回滚已取得的所有权。
        /// 取消时抛出 <see cref="OperationCanceledException"/>（决策 0.7：取消保留取消异常语义），
        /// 但已登记的所有权在抛出前已被回滚释放。
        /// </param>
        /// <returns>
        /// 组装产物 <see cref="BattleRuntimeAssembly"/>。成功时 <see cref="BattleRuntimeAssembly.IsSuccess"/>
        /// 为 true；失败时携带稳定错误码与诊断信息，且 Scope 已被回滚释放。
        /// </returns>
        /// <remarks>
        /// <para><b>组装步骤顺序（每步登记所有权到新建的 Scope）：</b></para>
        /// <list type="number">
        /// <item>创建 <see cref="BattleRuntimeScope"/>（本次组装的所有权根）。</item>
        /// <item>创建运行时 <see cref="CancellationTokenSource"/> 并登记到 Scope。</item>
        /// <item>构造 <see cref="BattleActionScheduler"/>（到期动作/冷却调度器）。</item>
        /// <item>构造 <see cref="BattleSimulation"/>（逻辑时钟入口），注入阶段回调占位与调度器。</item>
        /// <item>校验装载信息（牌组预设、配置版本占位），失败返回
        /// <see cref="BattleErrorCode.ConfigInvalid"/> 或 <see cref="BattleErrorCode.ConfigVersionMismatch"/>。
        /// 配置版本字段的权威校验在此步骤（详见 <see cref="TryValidateLoadout"/>）。</item>
        /// <item>从应用级 <see cref="ConfigSystem.Instance.Tables"/> 复制不可变配置快照
        /// （task 3.4 / battle-config-snapshot spec）。使用 <see cref="LubanBattleConfigProvider"/>
        /// 读取已加载 Tables 并经 <see cref="BattleConfigNormalizer"/> 规范化，不在模拟子步加载
        /// TextAsset，也不由 <see cref="BattleRuntime"/> 卸载应用级配置资源（决策 0.11）。</item>
        /// <item>校验配置快照（task 3.5 <see cref="BattleConfigValidator"/>），覆盖缺表、
        /// 缺字段、非法/空权重、未知兵种、非法时间/距离、地图尺寸、越界路径和缺失引用；
        /// 配置版本字段在步骤 5 loadout 校验中权威检查（<see cref="TryValidateLoadout"/>），
        /// Validator 在快照层做防御性 SourceTag 复查（详见 <see cref="BattleConfigValidator.ValidateVersion"/>）；
        /// route marker 若仅属表现不得按游戏路径规则误判。校验失败返回
        /// <see cref="BattleErrorCode.ConfigInvalid"/>/<see cref="BattleErrorCode.ConfigMissing"/>/
        /// <see cref="BattleErrorCode.ConfigVersionMismatch"/>，阻止运行时进入运行状态
        /// （spec "Invalid configuration blocks battle entry"）。</item>
        /// <item>构造 Phase 4 Manager（<see cref="AttackEffectManager"/> / <see cref="ProjectileManager"/>）
        /// 及其依赖链（<see cref="RuntimeIdAllocator"/> / <see cref="EnemyManager"/> /
        /// <see cref="AttackResolver"/> / <see cref="ProjectileFactory"/>）。phaseHandlers 接入
        /// 属于 task 6.10 闭环范畴，此处只构造实例。</item>
        /// <item>构造 Phase 5 <see cref="UnitFactory"/> / <see cref="UnitRegistry"/>（task 6.3 产物）。
        /// UnitFactory 只识别四个强类型兵种 ID，复用上一步的 idAllocator / enemyManager /
        /// attackResolver / attackEffectManager / projectileFactory / projectileManager，
        /// 并新建四个 <see cref="BattleObjectPool{T}"/> 池化四兵。UnitRegistry 管理单位注册、
        /// 放置、移除和战斗结束清理。Settling 静默清理步骤 6 调用
        /// <c>UnitRegistry.ClearForSettling()</c> 移除全部活动单位并归还池。</item>
        /// <item>构造 Phase 5 <see cref="BattleInputController"/>（task 6.7 产物）及其新增依赖
        /// <see cref="BattleState"/> / <see cref="BattleEconomy"/> / <see cref="DeckManager"/> /
        /// <see cref="SeededRandomSource"/> / <see cref="PlacementReservationRegistry"/>。
        /// BattleInputController 原子执行购买放置和刷新命令，任一步失败按逆序补偿。
        /// MapData 从 configSnapshot.Map 获取，BattleConfigSnapshot 直接注入。phaseHandlers
        /// 接入与 StartGame/GameOver 生命周期钩子调用属于 task 6.10 闭环范畴，此处只构造实例。</item>
        /// </list>
        /// <para>任一步骤失败：记录日志，调用 <see cref="BattleRuntimeScope.Rollback"/>
        /// 只释放本次已取得的所有权，返回失败产物。不抛出预期失败异常（决策 0.7）。</para>
        /// <para>调用方取消：先回滚已取得的所有权，再抛出
        /// <see cref="OperationCanceledException"/>，保留取消异常语义。</para>
        /// </remarks>
        internal static BattleRuntimeAssembly Create(
            BattleLoadoutDto loadout,
            CancellationToken cancellationToken = default)
        {
            // 步骤 1：创建本次组装的所有权作用域。这是本次 Create 取得全部所有权的根，
            // 失败时只回滚这个 Scope，不触碰调用方的外部对象。
            Log.Info($"{LogTag} 开始组装一局运行时依赖，mapId={loadout.MapId} round={loadout.Round} seed={loadout.RandomSeed}");
            BattleRuntimeScope scope = new BattleRuntimeScope();

            BattleSimulation simulation = null;
            CancellationTokenSource runtimeTokenSource = null;
            BattleActionScheduler actionScheduler = null;
            BattleConfigSnapshot configSnapshot = null;
            AttackEffectManager attackEffectManager = null;
            ProjectileManager projectileManager = null;
            UnitFactory unitFactory = null;
            UnitRegistry unitRegistry = null;
            BattleInputController inputController = null;

            try
            {
                // 步骤 2：创建运行时取消令牌源并登记所有权。
                // 该令牌用于取消本局所有异步操作与表现回调（spec "Exit releases battle-owned state"）。
                Log.Info($"{LogTag} 步骤 1/9：创建运行时取消令牌源");
                cancellationToken.ThrowIfCancellationRequested();
                runtimeTokenSource = new CancellationTokenSource();
                scope.TrackCancellationTokenSource(runtimeTokenSource, "RuntimeToken");

                // 步骤 3：构造到期动作/冷却调度器（task 2.11 产物，强类型注入，非字符串查找）。
                Log.Info($"{LogTag} 步骤 2/9：构造 BattleActionScheduler");
                cancellationToken.ThrowIfCancellationRequested();
                actionScheduler = new BattleActionScheduler();

                // 步骤 4：构造逻辑模拟器（task 2.11 产物）。
                // 阶段回调占位：task 2.10 BattleRuntime 实现后，由 Runtime 提供真实阶段回调
                // 并经 Assembly 连接到 Simulation。当前占位回调为空操作，保证 Simulation 可构造。
                // TryFreeze 回调占位：返回 false（未冻结），task 2.10 实现后由 BattleResultBuilder 提供。
                Log.Info($"{LogTag} 步骤 3/9：构造 BattleSimulation");
                cancellationToken.ThrowIfCancellationRequested();
                int phaseCount = Enum.GetValues(typeof(BattleUpdatePhase)).Length;
                Action<long, long, BattleUpdatePhase>[] phaseHandlers =
                    new Action<long, long, BattleUpdatePhase>[phaseCount];
                Func<bool> tryFreezePlaceholder = () => false;
                simulation = new BattleSimulation(phaseHandlers, tryFreezePlaceholder, actionScheduler);

                // 步骤 5：校验装载信息。预期校验失败返回结构化错误码，不抛异常（决策 0.7）。
                Log.Info($"{LogTag} 步骤 4/9：校验装载信息");
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryValidateLoadout(loadout, out BattleErrorCode validateError, out string validateMsg))
                {
                    // 校验失败：回滚本次已取得的所有权，返回失败产物。
                    Log.Warning($"{LogTag} 装载信息校验失败 code={validateError} msg={validateMsg}");
                    scope.Rollback();
                    return BattleRuntimeAssembly.Fail(validateError, validateMsg, scope);
                }

                // 步骤 6：从应用级 ConfigSystem.Tables 复制不可变配置快照（task 3.4）。
                // battle-config-snapshot spec "Runtime consumes an immutable configuration snapshot"：
                //   运行时只依赖快照，逻辑子步不得反复访问资源加载器或可变全局配置表。
                // decision 0.11：应用级 ConfigSystem/资源预加载持有配置数据，BattleRuntime 只持有
                //   不可变快照，不在模拟子步加载 TextAsset，也不由 BattleRuntime 卸载应用级配置资源。
                // LubanBattleConfigProvider 从已加载 Tables 读取并规范化，不触发同步 IO。
                Log.Info($"{LogTag} 步骤 5/9：从 ConfigSystem.Tables 复制战斗配置快照");
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Tables tables = ConfigSystem.Instance.Tables;
                    var provider = new LubanBattleConfigProvider(tables);
                    configSnapshot = provider.GetSnapshot();
                }
                catch (Exception configEx)
                {
                    // 配置快照复制失败：回滚本次已取得的所有权，返回配置错误。
                    // ConfigSystem.Tables getter 未 Load 时抛 InvalidOperationException，
                    // 由 GameApp.Entrance 中的 ConfigSystem.Instance.Load() 保证已就绪。
                    Log.Error($"{LogTag} 配置快照复制失败: {configEx}");
                    scope.Rollback();
                    return BattleRuntimeAssembly.Fail(
                        BattleErrorCode.ConfigInvalid,
                        $"配置快照复制失败: {configEx.GetType().Name}",
                        scope);
                }

                // 步骤 7：校验配置快照（task 3.5 BattleConfigValidator）。
                // battle-config-snapshot spec "Invalid configuration blocks battle entry"：
                //   缺失表、非法权重、错误地图尺寸、未知单位或不完整路径 MUST 返回可诊断错误，
                //   并阻止运行时进入运行状态。
                // 决策 0.7：预期失败返回结构化结果而非异常。Validator 返回结构化结果，
                //   校验失败时据错误码返回 ConfigInvalid/ConfigMissing/ConfigVersionMismatch，
                //   不创建半初始化实体，不进入运行状态。
                Log.Info($"{LogTag} 步骤 6/9：校验配置快照（BattleConfigValidator）");
                cancellationToken.ThrowIfCancellationRequested();
                BattleConfigValidationResult validationResult = BattleConfigValidator.Validate(configSnapshot);
                if (!validationResult.IsValid)
                {
                    // 校验失败：回滚本次已取得的所有权，返回 Validator 给出的稳定错误码。
                    // 不抛异常，不依赖诊断文本判断失败原因（决策 0.7）。
                    Log.Warning($"{LogTag} 配置校验失败 code={validationResult.ErrorCode} " +
                        $"errors={validationResult.Errors.Count} first={validationResult.Errors[0]}");
                    scope.Rollback();
                    return BattleRuntimeAssembly.Fail(
                        validationResult.ErrorCode,
                        validationResult.DiagnosticMessage,
                        scope);
                }

                // 步骤 8：构造 Phase 4 Manager（AttackEffectManager / ProjectileManager）。
                // 这些 Manager 当前只由 BattleRuntime 持有，用于 Settling 静默清理时调用 Clear()。
                // phaseHandlers 接入属于后续 task 6.10 闭环范畴，此处只构造实例并注入。
                // ProjectileManager 依赖 ProjectileFactory，后者依赖 RuntimeIdAllocator /
                // BattleObjectPool<SimpleDynamicArrow> / EnemyManager / cellSize。
                // EnemyManager 属于 Phase 3 产物，此处只构造实例供 ProjectileFactory 使用，
                // 不接入 phaseHandlers（task 6.10 范畴）。
                Log.Info($"{LogTag} 步骤 7/9：构造 AttackEffectManager / ProjectileManager");
                cancellationToken.ThrowIfCancellationRequested();
                attackEffectManager = new AttackEffectManager();

                // 构造 ProjectileManager 的依赖链。
                // gridSize / cellSize：本期固定 80（对应 map.gridWidth，EnemyManager.DefaultGridSize）。
                // 后续由配置快照暴露 cellWidth/cellHeight 字段后从快照读取。
                const int gridSize = EnemyManager.DefaultGridSize;
                const float cellSize = 80f;
                var idAllocator = new RuntimeIdAllocator();
                var arrowPool = new BattleObjectPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
                var enemyManager = new EnemyManager(gridSize);
                var attackResolver = new AttackResolver();
                var projectileFactory = new ProjectileFactory(idAllocator, arrowPool, enemyManager, cellSize);
                projectileManager = new ProjectileManager(projectileFactory);

                // 步骤 9：构造 Phase 5 UnitFactory / UnitRegistry（task 6.3 产物）。
                // UnitFactory 只识别四个强类型兵种 ID（刀/弓/枪/骑），使用四个独立的
                // BattleObjectPool<T> 池化四兵，Acquire 分配新 RuntimeId（复用上方 idAllocator），
                // Release 回收并 Reset。按兵种分支调用各士兵的 Configure：弓兵多
                // ProjectileFactory/ProjectileManager 参数（其余三兵种只传通用 5 参数）。
                // UnitRegistry 管理单位注册、放置、移除和战斗结束清理，维护稳定有序集合
                // （List<SoldierBase> + Dictionary<int,int>）供 AttackScheduler 遍历。
                // cellSize / opponentAttackMultiplier：本期固定 80 / 1（与上方 ProjectileFactory
                // 及 BattleState.OpponentAttackMultiplier 一致）；后续从配置快照读取。
                // 池实例：本期与 ProjectileFactory 一样在 Factory 内局部构造，待 BattlePoolScope
                // 接入 BattleModule（后续 Phase）后改为从跨局池作用域获取，复用空闲容量。
                Log.Info($"{LogTag} 步骤 8/9：构造 UnitFactory / UnitRegistry");
                cancellationToken.ThrowIfCancellationRequested();
                const int opponentAttackMultiplier = 1;
                var knifePool = new BattleObjectPool<KnifeSoldier>(() => new KnifeSoldier());
                var bowPool = new BattleObjectPool<BowSoldier>(() => new BowSoldier());
                var spearPool = new BattleObjectPool<SpearSoldier>(() => new SpearSoldier());
                var cavalryPool = new BattleObjectPool<CavalrySoldier>(() => new CavalrySoldier());

                unitFactory = new UnitFactory(
                    idAllocator,
                    knifePool, bowPool, spearPool, cavalryPool,
                    enemyManager, attackResolver, attackEffectManager,
                    projectileFactory, projectileManager,
                    cellSize, opponentAttackMultiplier);

                unitRegistry = new UnitRegistry(unitFactory, cellSize);

                // 步骤 10：构造 Phase 5 BattleInputController（task 6.7 产物）及其新增依赖。
                // BattleInputController 原子执行购买放置和刷新命令，任一步失败按逆序补偿。
                // 注入依赖（7 个）：
                //   - UnitFactory / UnitRegistry（步骤 8 已构造）
                //   - BattleEconomy / DeckManager / PlacementReservationRegistry（本步骤新建）
                //   - MapData（从 configSnapshot.Map 获取）
                //   - BattleConfigSnapshot（步骤 6 已复制）
                // BattleEconomy 依赖 BattleState + refreshCostIncrement（从配置快照 Economy 读取）。
                // DeckManager 依赖 IRandomSource + DeckConfigSnapshot（从配置快照 Deck 读取）。
                // SeededRandomSource 从 loadout.RandomSeed 构造，保证每局确定性可复现。
                // BattleState 无参构造，使用默认初始值（后续配置接入后从快照注入）。
                // phaseHandlers 接入与 BattleInputController 的 StartGame/GameOver 生命周期钩子
                // 调用属于 task 6.10 闭环范畴，此处只构造实例。
                Log.Info($"{LogTag} 步骤 9/9：构造 BattleEconomy / DeckManager / BattleInputController");
                cancellationToken.ThrowIfCancellationRequested();
                var battleState = new BattleState();
                int refreshCostIncrement = configSnapshot.Economy.RefreshCostIncrement;
                var economy = new BattleEconomy(battleState, refreshCostIncrement);

                var randomSource = new SeededRandomSource(loadout.RandomSeed);
                DeckConfigSnapshot deckConfig = configSnapshot.Deck;
                var deckManager = new DeckManager(randomSource, deckConfig);

                var reservationRegistry = new PlacementReservationRegistry();

                inputController = new BattleInputController(
                    unitFactory,
                    unitRegistry,
                    economy,
                    deckManager,
                    reservationRegistry,
                    configSnapshot.Map,
                    configSnapshot);

                // 组装成功：记录完成日志，返回成功产物。
                Log.Info($"{LogTag} 组装成功，返回 Assembly（task 2.10 BattleRuntime 将接管所有权）");
                return BattleRuntimeAssembly.Ok(
                    scope,
                    simulation,
                    loadout,
                    runtimeTokenSource,
                    configSnapshot,
                    attackEffectManager,
                    projectileManager,
                    unitFactory,
                    unitRegistry,
                    inputController);
            }
            catch (OperationCanceledException)
            {
                // 调用方取消：先回滚本次已取得的所有权，再重新抛出取消异常。
                // 取消不绕过内部清理（task 2.6 约束同理）：已登记的 CTS 等所有权在此释放。
                Log.Warning($"{LogTag} 组装被调用方取消，回滚已取得的所有权");
                scope.Rollback();
                throw;
            }
            catch (Exception ex)
            {
                // 非预期异常：回滚本次已取得的所有权，包装为 Unknown 错误码返回。
                // 不抛出，保证调用方收到结构化结果（决策 0.7：非预期异常包装为错误码）。
                Log.Error($"{LogTag} 组装过程发生非预期异常，回滚已取得的所有权: {ex}");
                scope.Rollback();
                return BattleRuntimeAssembly.Fail(
                    BattleErrorCode.Unknown,
                    $"组装过程非预期异常: {ex.GetType().Name}",
                    scope);
            }
        }

        /// <summary>
        /// 校验装载信息，返回结构化错误码而非抛异常（决策 0.7）。
        /// </summary>
        /// <param name="loadout">待校验的装载信息。</param>
        /// <param name="errorCode">校验失败时的稳定错误码。</param>
        /// <param name="diagnosticMessage">校验失败时的诊断信息（仅用于日志）。</param>
        /// <returns>校验通过返回 true；失败返回 false 并填充错误码与诊断信息。</returns>
        /// <remarks>
        /// <para>本期校验项（随着后续 Phase 依赖就绪逐步扩展）：</para>
        /// <list type="bullet">
        /// <item>牌组预设：本期只支持 <see cref="BattleDeckPreset.Normal"/>，其他值视为非法。</item>
        /// <item>配置版本（task 3.5 "覆盖配置版本"）：本校验是配置版本校验的权威位置。
        /// <see cref="BattleLoadoutDto.ConfigVersion"/> 为版本占位字段（1.8 审计：版本/hash 机制缺失，
        /// 由 task 3.2/3.5/8.2 新建）。本期未启用版本协商，固定零值 0 表示"占位但合法"；
        /// 负值违反 BattleLoadoutDto 的"明确零值，不得解释为任意版本"语义，视为非法版本，
        /// 返回 <see cref="BattleErrorCode.ConfigVersionMismatch"/>。
        /// <see cref="BattleConfigValidator.ValidateVersion"/> 在快照层做防御性复查（SourceTag 为空时
        /// 报告 InvalidVersion），版本字段的权威校验在此处（loadout 层）。</item>
        /// <item>配置 hash 占位：本期未启用 hash 机制，<see cref="BattleLoadoutDto.ConfigHash"/> 为空串即合法，
        /// 不在此校验。hash 校验待 task 8.2 接入。</item>
        /// <item>配置快照的内容校验（缺表/缺字段/权重/兵种/时间/距离/尺寸/路径/引用）由步骤 7
        /// <see cref="BattleConfigValidator"/> 承担（task 3.5）。</item>
        /// </list>
        /// </remarks>
        private static bool TryValidateLoadout(
            BattleLoadoutDto loadout,
            out BattleErrorCode errorCode,
            out string diagnosticMessage)
        {
            // 牌组预设校验：本期只支持 Normal。
            if (loadout.DeckPreset != BattleDeckPreset.Normal)
            {
                errorCode = BattleErrorCode.ConfigInvalid;
                diagnosticMessage = $"不支持的牌组预设 preset={loadout.DeckPreset}，本期只支持 Normal";
                return false;
            }

            // 配置版本校验（task 3.5 "覆盖配置版本"）。
            // BattleLoadoutDto.ConfigVersion 占位字段语义：0 = 占位但合法；负值 = 非法版本。
            // 本期未启用版本协商，不校验"版本号是否匹配预期基线"（待 task 8.2 接入对照工具后扩展），
            // 只校验占位字段未被误用为非法负值。负值返回 ConfigVersionMismatch，
            // 使调用方能区分版本问题与一般配置非法（决策 0.7 结构化错误码）。
            if (loadout.ConfigVersion < 0)
            {
                errorCode = BattleErrorCode.ConfigVersionMismatch;
                diagnosticMessage = $"配置版本号 ConfigVersion={loadout.ConfigVersion} 为负，" +
                    "本期占位字段只允许 0（未启用版本机制）；负值违反明确零值语义";
                return false;
            }

            // 后续校验（地图有效性等）由 task 3.5 BattleConfigValidator 承担，
            // Factory 只校验装载信息本身的结构合法性，不重复配置层校验。

            errorCode = BattleErrorCode.None;
            diagnosticMessage = string.Empty;
            return true;
        }
    }
}
