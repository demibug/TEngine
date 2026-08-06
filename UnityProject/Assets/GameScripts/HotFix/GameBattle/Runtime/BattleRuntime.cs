using System;
using System.Threading;
using GameCommon.Battle;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 2.10：BattleRuntime —— 一局战斗的所有权根
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 3 节 / specs/battle-runtime-lifecycle/spec.md）：
    //   BattleRuntime 是一局战斗可变状态的唯一所有权根。它独占本局的配置快照、
    //   BattleState、所有 Manager、活动实体、输入队列、局部事件订阅、逻辑计时、
    //   随机进度和最终结果。重开一局 MUST 销毁旧 Runtime 并新建，不跨局复用
    //   （决策 0.2：重开重建 Runtime）。
    //
    //   本类型证明“同一时刻最多一个活动 Runtime”不变量：由 BattleModule（task 2.6）
    //   在串行门保护下只持有一个当前活动 Runtime 引用（后续接入时由 _activeScope 升级为
    //   BattleRuntime 引用）；重复 Start 返回 AlreadyActive，不创建第二个 Runtime
    //   （spec "Duplicate start returns AlreadyActive"）。本类型自身不强制“唯一性”，
    //   唯一性由 BattleModule 的状态机与串行门保证。
    //
    //   本类型为 internal，只供 GameBattle 内部 BattleModule 使用，不对其他程序集暴露。
    //   对外公共生命周期经 IBattleModule / BattleModule 转发。
    //
    // 已实现依赖（Phase 1/2 产物）：
    //   - BattleRuntimeScope（task 2.8）：所有权跟踪与幂等逆序释放。
    //   - BattleSimulation（task 2.11）：逻辑时钟、500ms 截断、80ms 子步、阶段调度、
    //     TryFreeze 冻结与中止检查点。
    //   - BattleRuntimeFactory / BattleRuntimeAssembly（task 2.9）：强类型组装产物，
    //     由 Factory 构造后交由本类型接管所有权。
    //   - BattleConfigSnapshot（task 3.3/3.4）：不可变本局配置快照，由 Factory 从
    //     应用级 ConfigSystem.Tables 复制后经 Assembly 注入，本类型只持有快照，
    //     不访问资源加载器或卸载应用级配置资源（决策 0.11）。
    //
    // 预留扩展点（后续 Phase 产物，当前为骨架占位）：
    //   - BattleState / BattleResultBuilder（Phase 2 task 3.7/3.11）：权威可变状态与
    //     唯一结果冻结。
    //   - BattleManager / WaveManager / EnemyManager / UnitRegistry 等 Manager
    //     （Phase 2/3/4/5）：战斗规则、波次、敌人、单位、攻击效果、投射物等。
    //   - 输入队列与 BattleInputController（Phase 5 task 6.6/6.7）。
    //   - 局部事件 BattleInternalSignalHub（Phase 6 task 7.1）。
    //   - 随机源 SeededRandomSource（Phase 2/3 task 3.x/4.x）。
    //   这些扩展点在本骨架中以注释或占位字段标注，后续 Phase 接入时替换为真实实现。
    //
    // 不变量（spec "Runtime quiescence and cleanup have one ordered owner"）：
    //   1. 独占单局状态：快照、状态、Manager、活动实体、输入、局部事件、逻辑计时、
    //      随机进度和结果均归属本实例，不跨局复用。
    //   2. 每局新建/销毁：BattleModule 重开时销毁旧 Runtime（Dispose），新建 Runtime。
    //   3. 同一时刻最多一个活动 Runtime：BattleModule 只持有一个当前活动 Runtime。
    // ============================================================================

    /// <summary>
    /// 一局战斗的所有权根，独占全部单局可变状态。
    /// </summary>
    /// <remarks>
    /// <para><b>所有权独占（design.md 决策 3 / spec "Restart creates clean per-battle state"）：</b></para>
    /// <para>每局 Runtime 独占配置快照、BattleState、所有 Manager、活动实体、输入队列、
    /// 局部事件订阅、逻辑计时、随机进度和最终结果。重开 MUST 销毁旧 Runtime 并新建，
    /// 不复用 Manager、活动对象、订阅、命令、计时或结果状态。返回主界面 MUST 进一步
    /// 清空池容量并释放战斗专属宿主资源（由 BattleModule 负责）。</para>
    ///
    /// <para><b>同一时刻最多一个活动 Runtime（spec "Battle module exposes one authoritative lifecycle"）：</b></para>
    /// <para>由 BattleModule 在串行门保护下只持有一个当前活动 Runtime 引用
    /// （后续接入时由 <c>_activeScope</c> 升级为 BattleRuntime 引用）。
    /// 重复 Start 返回 <see cref="BattleErrorCode.AlreadyActive"/>，不创建第二个 Runtime。
    /// 本类型自身不强制“唯一性”，唯一性由 BattleModule 的状态机与串行门保证。</para>
    ///
    /// <para><b>与 BattleRuntimeFactory 的连接（design.md 决策 1 / task 2.9）：</b></para>
    /// <para><see cref="BattleRuntimeFactory.Create"/> 产生 <see cref="BattleRuntimeAssembly"/>，
    /// 本类型在构造时接管 Assembly 的全部所有权（Scope、Simulation、Token 等）。
    /// Factory 负责组装依赖，Runtime 负责持有与驱动。若组装失败，Assembly 已被回滚，
    /// 不应构造 Runtime（调用方据错误码返回结构化失败结果，不留下半初始化运行时）。</para>
    ///
    /// <para><b>逻辑计时（design.md 第 2 节 / spec battle-simulation）：</b></para>
    /// <para>本类型持有 <see cref="BattleSimulation"/> 并通过 <see cref="Advance"/> 转交帧时间。
    /// Simulation 是唯一逻辑时钟入口，执行 500ms 截断、80ms 子步拆分、显式阶段调度与
    /// TryFreeze 冻结。本类型不自行拆步或推进时间。</para>
    ///
    /// <para><b>Settling 静默清理（spec "Runtime quiescence and cleanup have one ordered owner"）：</b></para>
    /// <para>首次 TryFreeze 成功后，BattleModule 进入 Settling 状态，调用 <see cref="EnterSettling"/>
    /// 执行幂等静默清理，按依赖顺序：关闭命令和生产入口 → 停止模拟并取消 Token/到期动作/回调 →
    /// 清理攻击效果 → 清理投射物 → 清理敌人及空间索引 → 清理单位及监听 → 清理波次/牌组/预留 →
    /// 解除剩余局部监听 → 断言无活动对象 → 发布已冻结结果。
    /// 当前骨架实现已实现部分（停止模拟、取消 Token、释放 Scope），后续 Phase 接入 Manager 后
    /// 在对应清理步骤处补充。</para>
    /// </remarks>
    internal sealed class BattleRuntime : IDisposable
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>
        /// 日志标签前缀，便于在日志中筛选战斗运行时相关条目。
        /// </summary>
        private const string LogTag = "[BattleRuntime]";

        // ====================================================================
        // 已实现的所有权持有（Phase 1 产物）
        // ====================================================================

        /// <summary>
        /// 本局运行时所有权作用域（task 2.8 产物）。
        /// <para>跟踪本局取得的全部可释放所有权（CTS、GameEvent 监听、到期动作、表现回调、
        /// 资源租约、池租借），提供幂等逆序释放与失败初始化回滚。由
        /// <see cref="BattleRuntimeFactory"/> 组装并登记，本类型接管持有。</para>
        /// <para>Dispose 时通过 Scope 一次性逆序释放全部登记所有权。</para>
        /// </summary>
        public BattleRuntimeScope Scope { get; }

        /// <summary>
        /// 本局逻辑时钟入口（task 2.11 产物）。
        /// <para>以 <c>elapseSeconds</c> 为唯一逻辑时间源，执行 500ms 截断、最大 80ms 子步、
        /// 显式 <see cref="BattleUpdatePhase"/> 阶段调度、结果冻结与冻结后中止检查点。
        /// 本类型持有 Simulation 并经 <see cref="Advance"/> 转交帧时间。</para>
        /// <para>独占语义：Simulation 不跨局复用；重开销毁旧 Runtime 时连同 Simulation
        /// 一并销毁（BattleModule 新建 Runtime 时由 Factory 产生新 Simulation）。</para>
        /// </summary>
        public BattleSimulation Simulation { get; }

        /// <summary>
        /// 本局运行时取消令牌源。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 创建并登记到 Scope，本类型接管持有。
        /// 用于取消本局所有异步操作与表现回调。Settling 静默清理时 Cancel，
        /// 使迟到回调因 Token 失效（spec "Exit releases battle-owned state"）。</para>
        /// </summary>
        public CancellationTokenSource RuntimeTokenSource { get; }

        /// <summary>
        /// 本局不可变装载信息（只读副本）。
        /// <para>由调用方（BattleModule）在 Start/Restart 时传入，经
        /// <see cref="BattleRuntimeFactory"/> 组装后由本类型持有。包含地图、种子、
        /// 配置版本/hash 占位、牌组预设等局外输入信息。</para>
        /// </summary>
        public BattleLoadoutDto Loadout { get; }

        /// <summary>
        /// 本局不可变战斗配置快照（task 3.4 产物）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 在组装阶段从应用级
        /// <c>ConfigSystem.Instance.Tables</c> 经 <see cref="LubanBattleConfigProvider"/>
        /// 复制一次，之后本类型只持有该快照，不再访问资源加载器或可变全局配置表
        /// （battle-config-snapshot spec "Runtime consumes an immutable configuration snapshot"）。</para>
        /// <para>决策 0.11：应用级 ConfigSystem/资源预加载持有配置数据，
        /// BattleRuntime 只持有不可变快照，不在模拟子步加载 TextAsset，
        /// 也不由本类型卸载应用级配置资源。</para>
        /// </summary>
        public BattleConfigSnapshot ConfigSnapshot { get; }

        // ====================================================================
        // Phase 4 Manager（task 5.3 / 5.8 产物）
        // ====================================================================

        /// <summary>
        /// 本局攻击效果管理器（task 5.3 产物）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 构造并经 Assembly 注入，本类型接管持有。
        /// Settling 静默清理步骤 3 调用 <c>Clear()</c> 取消全部活动攻击效果
        /// （spec "Runtime quiescence and cleanup have one ordered owner"）。</para>
        /// <para>独占语义：不跨局复用；重开销毁旧 Runtime 时连同 Manager 一并销毁
        /// （BattleModule 新建 Runtime 时由 Factory 产生新 Manager）。</para>
        /// </summary>
        public AttackEffectManager AttackEffectManager { get; }

        /// <summary>
        /// 本局投射物管理器（task 5.8 产物）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 构造并经 Assembly 注入，本类型接管持有。
        /// Settling 静默清理步骤 4 调用 <c>Clear()</c> 取消全部空中弹道
        /// （spec "Runtime quiescence and cleanup have one ordered owner"）。</para>
        /// <para>独占语义：不跨局复用；重开销毁旧 Runtime 时连同 Manager 一并销毁
        /// （BattleModule 新建 Runtime 时由 Factory 产生新 Manager）。</para>
        /// </summary>
        public ProjectileManager ProjectileManager { get; }

        // ====================================================================
        // Phase 5 Manager（task 6.3 产物）
        // ====================================================================

        /// <summary>
        /// 本局单位工厂（task 6.3 产物）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 构造并经 Assembly 注入，本类型接管持有。
        /// 只识别四个强类型兵种 ID（刀/弓/枪/骑），未知兵种显式失败。使用四个独立的
        /// <see cref="BattleObjectPool{T}"/> 池化四兵，Acquire 分配新 RuntimeId，
        /// Release 回收并 Reset。按兵种分支调用各士兵的 Configure。</para>
        /// <para>独占语义：不跨局复用；重开销毁旧 Runtime 时连同工厂一并销毁
        /// （BattleModule 新建 Runtime 时由 Factory 产生新工厂）。池容量跨局复用由
        /// BattlePoolScope 管理（待后续 Phase 接入 BattleModule）。</para>
        /// <para>Settling 静默清理时由 <see cref="UnitRegistry"/> 经本工厂 Release 全部活动单位。</para>
        /// </summary>
        public UnitFactory UnitFactory { get; }

        /// <summary>
        /// 本局单位注册表（task 6.3 产物）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 构造并经 Assembly 注入，本类型接管持有。
        /// 管理单位注册、放置、移除和战斗结束清理，维护稳定有序集合供 AttackScheduler 遍历。</para>
        /// <para>独占语义：不跨局复用；重开销毁旧 Runtime 时连同注册表一并销毁
        /// （BattleModule 新建 Runtime 时由 Factory 产生新注册表）。</para>
        /// <para>Settling 静默清理步骤 6 调用 <c>ClearForSettling()</c> 移除全部活动单位
        /// 并归还池（spec "Runtime quiescence and cleanup have one ordered owner"）。</para>
        /// </summary>
        public UnitRegistry UnitRegistry { get; }

        /// <summary>
        /// 本局输入命令执行控制器（task 6.7 产物）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 构造并经 Assembly 注入，本类型接管持有。
        /// 原子执行购买放置和刷新命令，任一步失败按逆序补偿到调用前状态
        /// （spec "Input commands are atomic"）。</para>
        /// <para>注入依赖：UnitFactory / UnitRegistry / BattleEconomy / DeckManager /
        /// PlacementReservationRegistry / MapData / BattleConfigSnapshot。</para>
        /// <para>独占语义：不跨局复用；重开销毁旧 Runtime 时连同控制器一并销毁
        /// （BattleModule 新建 Runtime 时由 Factory 产生新控制器）。CommandId 去重缓存
        /// 由 task 6.8 在本类型内维护，随 Runtime 清理清空，不跨局保留。</para>
        /// <para>生命周期钩子：StartGame / GameOver 由 BattleRuntime / BattleManager 在战斗
        /// 开始/结束时调用（task 6.10 接入）。StartGame 后 Execute 接受命令；
        /// GameOver 后 Execute 返回失败。</para>
        /// </summary>
        public BattleInputController InputController { get; }

        // ====================================================================
        // 预留扩展点（后续 Phase 产物，当前为骨架占位）
        // ====================================================================

        // TODO Phase 2 task 3.7：BattleState —— 双方生命、金币、波次等权威可变状态。
        //   独占语义：只由本局逻辑修改，不跨局复用。
        //   当前由 BattleRuntimeFactory 构造并经 BattleEconomy 持有，但未在 BattleRuntime
        //   上公开属性（待 task 6.10 接入 phaseHandlers 时补全）。
        //   public BattleState State { get; }

        // TODO Phase 2 task 3.11：BattleResultBuilder —— 唯一结果冻结点。
        //   独占语义：首次 TryFreeze 冻结一次最终结果 DTO，冻结后不可修改。
        //   public BattleResultBuilder ResultBuilder { get; }

        // TODO Phase 2 task 3.9/3.10：BattleManager / WaveManager —— 战斗规则、波次、胜负。
        // TODO Phase 3 task 4.6：EnemyManager —— 敌人集合、空间索引、伤害入口。
        //   当前由 BattleRuntimeFactory 构造并经 ProjectileFactory 传递给 UnitFactory，
        //   但未在 BattleRuntime 上公开属性（待 task 6.10 接入 phaseHandlers 时补全）。
        // TODO Phase 3 task 4.1：BattlePoolScope —— 可跨局复用池容量与逐局清空活动对象。
        // Phase 4 task 5.3/5.8：AttackEffectManager / ProjectileManager 已接入（见上方属性）。
        // Phase 5 task 6.3：UnitFactory / UnitRegistry 已接入（见上方属性）。
        // Phase 5 task 6.7：BattleInputController 已接入（见上方属性）。
        // TODO Phase 5 task 6.5：DeckManager —— 牌组抽牌/补牌/刷新。
        //   当前由 BattleRuntimeFactory 构造并经 BattleInputController 持有，但未在
        //   BattleRuntime 上公开属性（待 task 6.10 接入 phaseHandlers 时补全）。
        // TODO Phase 6 task 7.1：BattleInternalSignalHub —— 单局低频局部事件。
        // TODO Phase 2/3：SeededRandomSource —— 确定性随机源（Ports/SeededRandomSource.cs）。
        //   独占语义：每局从 Loadout.RandomSeed 构造新实例，不沿用旧局随机进度。
        //   当前由 BattleRuntimeFactory 构造并经 DeckManager 持有。

        // ====================================================================
        // 生命周期状态标记
        // ====================================================================

        /// <summary>
        /// 是否已进入 Settling（结果冻结后的静默清理阶段）。
        /// <para>由 <see cref="EnterSettling"/> 置位，置位后 <see cref="Advance"/> 不再推进模拟。
        /// 幂等：重复调用 <see cref="EnterSettling"/> 安全。</para>
        /// </summary>
        public bool IsSettling { get; private set; }

        /// <summary>
        /// 是否已 Dispose（Runtime 已销毁）。
        /// <para>Dispose 后所有公共 API 不再推进逻辑，重复 Dispose 幂等。
        /// BattleModule 在重开/退出时调用 Dispose 销毁旧 Runtime。</para>
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// 是否已发布最终结果（Settling 静默清理完成后发布一次）。
        /// <para>对应 spec "完成静默后发布一次已冻结的不可变结果"。
        /// 幂等：重复发布返回同一结果，不重复发布事件。</para>
        /// </summary>
        public bool IsResultPublished { get; private set; }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造一局战斗运行时，接管 <see cref="BattleRuntimeAssembly"/> 的全部所有权。
        /// </summary>
        /// <param name="assembly">
        /// 由 <see cref="BattleRuntimeFactory.Create"/> 产生的成功组装产物。
        /// 调用方 MUST 确认 <see cref="BattleRuntimeAssembly.IsSuccess"/> 为 true 后再构造本类型；
        /// 失败产物不应构造 Runtime（调用方据错误码返回结构化失败结果）。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="assembly"/> 为 null。
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="assembly"/> 不是成功产物（<see cref="BattleRuntimeAssembly.IsSuccess"/>
        /// 为 false）。调用方应在构造前检查，此异常为防御性校验。
        /// </exception>
        /// <remarks>
        /// <para>构造后本类型接管 Assembly 携带的 Scope、Simulation、RuntimeTokenSource、Loadout
        /// 所有权。Dispose 时通过 Scope 逆序释放全部登记所有权。</para>
        /// <para>本构造函数不执行加载步骤（加载由 Factory 组装阶段完成），只接管已组装的产物。
        /// 这保证 Runtime 构造后立即可用，不留下半初始化状态。</para>
        /// </remarks>
        internal BattleRuntime(BattleRuntimeAssembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            if (!assembly.IsSuccess)
            {
                // 防御性校验：失败产物不应构造 Runtime。
                // 调用方（BattleModule）应检查 IsSuccess 后再构造，此异常防止误用。
                throw new InvalidOperationException(
                    $"不能从失败的 BattleRuntimeAssembly 构造 BattleRuntime，" +
                    $"errorCode={assembly.ErrorCode} msg={assembly.DiagnosticMessage}");
            }

            // 接管 Assembly 的全部所有权。Assembly 的 Scope 此时持有已登记的所有权
            // （CTS、后续 Phase 的资源句柄等），由本类型持有并在 Dispose 时释放。
            Scope = assembly.Scope;
            Simulation = assembly.Simulation;
            RuntimeTokenSource = assembly.RuntimeTokenSource;
            Loadout = assembly.Loadout;
            ConfigSnapshot = assembly.ConfigSnapshot;
            AttackEffectManager = assembly.AttackEffectManager;
            ProjectileManager = assembly.ProjectileManager;
            UnitFactory = assembly.UnitFactory;
            UnitRegistry = assembly.UnitRegistry;
            InputController = assembly.InputController;

            Log.Info(
                $"{LogTag} 构造完成，mapId={Loadout.MapId} round={Loadout.Round} seed={Loadout.RandomSeed}");
        }

        // ====================================================================
        // 帧推进入口
        // ====================================================================

        /// <summary>
        /// 累计外部帧时间戳（毫秒）。由每次 <see cref="Advance"/> 累加，作为
        /// <see cref="BattleSimulation.Advance"/> 的 <c>frameNowMs</c> 参数。
        /// <para>对应还原工程 <c>Laya.timer.currTimer</c> 累计语义。同帧所有子步观察同一值。</para>
        /// </summary>
        private long _frameNowMs;

        /// <summary>
        /// 推进一个外部帧，将帧时间增量累加为绝对时间戳后转交给 <see cref="BattleSimulation"/>。
        /// </summary>
        /// <param name="deltaMilliseconds">
        /// 本外部帧的逻辑时间增量（毫秒），来自 TEngine <c>elapseSeconds * 1000</c>。
        /// 500ms 截断与 80ms 子步拆分由 Simulation 内部执行。
        /// </param>
        /// <remarks>
        /// <para><b>所有权链（design.md 第 2 节）：</b></para>
        /// <code>
        /// TEngine Update
        ///   -> BattleModule.OnUpdate(elapseSeconds)
        ///     -> BattleRuntime.Advance(deltaMilliseconds)
        ///       -> BattleSimulation.Advance(frameNowMs)
        ///         -> explicit ordered phases
        /// </code>
        /// <para>本方法将增量累加为绝对时间戳（<c>_frameNowMs += deltaMilliseconds</c>），
        /// 再转交给 Simulation。Simulation 以 <c>remaining = frameNowMs - lastTimer</c>
        /// 计算本帧推进量。本方法不自行拆步或推进时间。</para>
        ///
        /// <para><b>Settling 不推进（spec "Settling has no gameplay damage authority"）：</b></para>
        /// <para>进入 Settling 后本方法直接返回，不调用 Simulation.Advance。
        /// TEngine 全局更新驱动仍可继续，但本局 Simulation 不再推进任何子步。</para>
        ///
        /// <para><b>Dispose 后不推进：</b></para>
        /// <para>Runtime 销毁后本方法为空操作，防止迟到帧更新访问已释放资源。</para>
        /// </remarks>
        internal void Advance(long deltaMilliseconds)
        {
            if (IsDisposed)
            {
                // Runtime 已销毁，迟到帧更新为空操作。
                return;
            }

            if (IsSettling)
            {
                // Settling 中：BattleSimulation 不再推进任何子步（spec "Module receives updates while settling"）。
                return;
            }

            // 将帧时间增量累加为绝对时间戳，再转交 Simulation。
            // Simulation 内部以 remaining = frameNowMs - lastTimer 计算推进量，
            // 执行 500ms 截断、80ms 子步拆分与阶段调度。
            _frameNowMs += deltaMilliseconds;
            Simulation.Advance(_frameNowMs);
        }

        // ====================================================================
        // Settling 静默清理入口
        // ====================================================================

        /// <summary>
        /// 进入 Settling：执行幂等静默清理，按依赖顺序取消残余规则伤害与回收活动对象。
        /// </summary>
        /// <remarks>
        /// <para><b>触发时机（spec "Settling has no gameplay damage authority" / 决策 0.4）：</b></para>
        /// <para>首次 <see cref="BattleSimulation.TryFreeze"/> 成功后，BattleModule 在状态机
        /// 迁移到 Settling 时调用本方法。本方法幂等：重复调用安全，后续调用为空操作。</para>
        ///
        /// <para><b>静默清理顺序（spec "Runtime quiescence and cleanup have one ordered owner"）：</b></para>
        /// <list type="number">
        /// <item>关闭命令和生产入口（停止接收新输入、停止新生成、停止新攻击）。</item>
        /// <item>停止 <see cref="BattleSimulation"/> 并取消运行时 Token、到期动作和动画回调。</item>
        /// <item>清理 AttackEffectManager（task 5.3 接入）。</item>
        /// <item>清理 ProjectileManager（task 5.8 接入）。</item>
        /// <item>清理 EnemyManager 的接触 Timer、实体和空间索引（待 task 6.10 接入属性后补充）。</item>
        /// <item>清理 UnitRegistry 的监听、Timer 和实体（task 6.3 接入）。</item>
        /// <item>清理波次、牌组、预留及其他单局注册表（后续 Phase 2/5 接入）。</item>
        /// <item>解除剩余局部监听（后续 Phase 6 接入）。</item>
        /// <item>断言没有活动 Timer、回调或租借对象。</item>
        /// <item>完成静默后发布一次已冻结的不可变结果。</item>
        /// </list>
        /// <para>当前实现已执行步骤 1（标记 Settling + 关闭 InputController + 清空
        /// CommandId 缓存 task 6.8）、步骤 2（停止模拟 + 取消 Token + 冻结调度器）、
        /// 步骤 3（清理 AttackEffectManager）、步骤 4（清理 ProjectileManager）和步骤 6
        /// （清理 UnitRegistry）。步骤 5（EnemyManager）、步骤 7-8（波次/牌组/预留/
        /// 局部监听）和步骤 9-10（断言与结果发布）在后续 Phase 接入对应 Manager 后补充。</para>
        /// </remarks>
        internal void EnterSettling()
        {
            if (IsDisposed)
            {
                // 已销毁的 Runtime 不再执行静默清理。
                return;
            }

            if (IsSettling)
            {
                // 幂等：已进入 Settling，重复调用为空操作。
                return;
            }

            IsSettling = true;
            Log.Info($"{LogTag} 进入 Settling，开始静默清理");

            // ----------------------------------------------------------
            // 步骤 1：关闭命令和生产入口。
            // Settling 标记已置位，Advance 不再推进 Simulation，新输入不再被处理。
            // BattleInputController（task 6.7 接入）：调用 GameOver 关闭输入接收，
            // 后续 Execute 返回失败。DeckManager 也经 InputController 间接持有，
            // GameOver 钩子由 InputController 转发（当前 InputController.GameOver 只
            // 标记自身 _started=false，DeckManager.GameOver 待 task 6.10 接入时调用）。
            // 后续 Phase 2/4 接入 WaveManager/AttackEffectManager 后，在此显式关闭
            // 新生成和新攻击入口。
            //
            // task 6.8：调用 ClearProcessedCommands 清空已处理 CommandId 缓存，
            // 确保不跨局保留（spec "Restart creates clean per-battle state"）。
            // 放在 GameOver 之后，使本局后续 Execute（返回未启动失败）不再依赖缓存。
            // ----------------------------------------------------------
            try
            {
                InputController?.GameOver();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Settling 关闭 InputController 异常: {ex}");
            }

            try
            {
                InputController?.ClearProcessedCommands();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Settling 清空 InputController CommandId 缓存异常: {ex}");
            }

            // ----------------------------------------------------------
            // 步骤 2：停止模拟并取消运行时 Token、到期动作和动画回调。
            // ----------------------------------------------------------

            // 取消运行时 Token：使所有挂起异步操作与表现回调失效。
            // spec "Exit releases battle-owned state"：迟到回调因 Token 失效。
            try
            {
                if (RuntimeTokenSource != null && !RuntimeTokenSource.IsCancellationRequested)
                {
                    RuntimeTokenSource.Cancel();
                }
            }
            catch (Exception ex)
            {
                // ObjectDisposedException 等不阻断静默清理。
                Log.Error($"{LogTag} Settling 取消运行时 Token 异常: {ex}");
            }

            // 冻结调度器：停止推进冷却、停止触发/注册新到期动作。
            // BattleActionScheduler.Freeze 幂等（task 2.11 产物）。
            try
            {
                Simulation?.ActionScheduler?.Freeze();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Settling 冻结 ActionScheduler 异常: {ex}");
            }

            // Simulation 在 IsSettling 后 Advance 直接返回，不再推进子步。
            // Simulation 自身的 IsFrozen 由 TryFreeze 置位，此处不重复设置。

            // ----------------------------------------------------------
            // 步骤 3：清理 AttackEffectManager（task 5.3 接入）。
            // 取消全部活动攻击效果并回收，不造成伤害（只调 Cancel，不调 Update/Hit）。
            // Clear 幂等，含 try-catch 防御，不阻断后续清理步骤。
            // ----------------------------------------------------------
            try
            {
                AttackEffectManager?.Clear();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Settling 清理 AttackEffectManager 异常: {ex}");
            }

            // ----------------------------------------------------------
            // 步骤 4：清理 ProjectileManager（task 5.8 接入）。
            // 取消全部空中弹道并回收，不造成伤害（只回收，不调 Advance/Hit）。
            // Clear 幂等，含 try-catch 防御，不阻断后续清理步骤。
            // ----------------------------------------------------------
            try
            {
                ProjectileManager?.Clear();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Settling 清理 ProjectileManager 异常: {ex}");
            }

            // ----------------------------------------------------------
            // 步骤 5：清理 EnemyManager 的接触 Timer、实体和空间索引。
            // ----------------------------------------------------------
            // EnemyManager 当前由 BattleRuntimeFactory 构造并经 ProjectileFactory /
            // UnitFactory 持有，但未在 BattleRuntime 上公开属性。EnemyManager 的清理
            // （Clear/GameOver）待 task 6.10 接入 phaseHandlers 时在 BattleRuntime 上
            // 公开属性并在此调用。当前 EnemyManager 的活动敌人由 ProjectileManager.Clear
            // 与 UnitRegistry.ClearForSettling 间接处理（敌人不再被攻击/移动）。
            // TODO task 6.10：EnemyManager.GameOver() —— 清理敌人实体、接触 Timer、空间索引。

            // ----------------------------------------------------------
            // 步骤 6：清理 UnitRegistry 的监听、Timer 和实体（task 6.3 接入）。
            // 移除全部活动单位并归还池：每个单位先调 GameOver（取消本单位发起的活动
            // 攻击效果并标记 inPool/destroyed），再从集合移除并经 UnitFactory.Release
            // 归还池。ClearForSettling 幂等，等价于 GameOver，重复调用安全。
            // spec "Runtime quiescence and cleanup have one ordered owner"：
            //   清理 UnitRegistry 的监听、Timer 和实体。
            // ----------------------------------------------------------
            try
            {
                UnitRegistry?.ClearForSettling();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Settling 清理 UnitRegistry 异常: {ex}");
            }

            // ----------------------------------------------------------
            // 步骤 7-8：清理波次、牌组、预留及其他单局注册表；解除剩余局部监听。
            // ----------------------------------------------------------
            // TODO Phase 2/5：WaveManager / DeckManager / PlacementReservationRegistry.Clear()。
            // TODO Phase 6：BattleInternalSignalHub 解除全部局部订阅（由 Scope 批量释放）。

            // ----------------------------------------------------------
            // 步骤 9：断言没有活动 Timer、回调或租借对象。
            // ----------------------------------------------------------
            // 注意：此时 Scope 尚未 Dispose（Scope 在 Dispose 时才逆序释放）。
            // 此处断言检查的是"Manager 清理后是否仍有活动登记"。
            // 当前骨架无 Manager，跳过 Manager 清理后的断言。
            // Dispose 时的 AssertAllReleased 才是最终断言。
            // ----------------------------------------------------------

            // ----------------------------------------------------------
            // 步骤 10：发布一次已冻结的不可变结果。
            // ----------------------------------------------------------
            // TODO Phase 2 task 3.11：BattleResultBuilder.TryFreeze 已在 Simulation 冻结点
            //   冻结结果。此处应在静默清理完成后发布一次 IBattlePublicEvent.OnBattleFinished。
            //   当前骨架未接入 ResultBuilder 与 EventBridge，占位。
            // ----------------------------------------------------------

            Log.Info($"{LogTag} Settling 静默清理完成（已停止模拟、取消 Token、冻结调度器、清理 AttackEffect/Projectile Manager、清理 UnitRegistry）");
        }

        // ====================================================================
        // Dispose —— 销毁 Runtime，释放全部单局所有权
        // ====================================================================

        /// <summary>
        /// 销毁本局运行时，逆序释放全部登记的所有权。
        /// </summary>
        /// <remarks>
        /// <para><b>销毁时机（design.md 决策 3 / spec "Restart creates clean per-battle state"）：</b></para>
        /// <list type="bullet">
        /// <item>重开（Restart）：BattleModule 销毁旧 Runtime（Dispose）后新建 Runtime。</item>
        /// <item>退出（Exit）：BattleModule 销毁当前 Runtime（Dispose），再释放战斗专属宿主资源。</item>
        /// <item>加载失败回滚：BattleModule 在部分初始化回滚时释放 Scope（经 Factory 回滚），
        /// 不构造 Runtime，故不调用本方法。</item>
        /// </list>
        ///
        /// <para><b>幂等：</b>重复 Dispose 安全。首次调用逆序释放 Scope 全部登记所有权，
        /// 后续调用为空操作。</para>
        ///
        /// <para><b>不跨局复用：</b>Dispose 后本实例不再可用。重开由 BattleModule 新建
        /// Runtime（经 Factory 产生新 Assembly），不复用本实例的 Simulation、Scope 或状态。</para>
        ///
        /// <para><b>Settling 未完成时 Dispose：</b>若未调用 <see cref="EnterSettling"/> 就直接
        /// Dispose（如 Exit 从 Running 状态直接退出），本方法仍会释放全部所有权，
        /// 包括取消 Token、冻结调度器、逆序释放 Scope。</para>
        /// </remarks>
        public void Dispose()
        {
            if (IsDisposed)
            {
                // 幂等：已销毁则直接返回。
                return;
            }

            Log.Info($"{LogTag} Dispose，开始逆序释放全部所有权");

            // 若未进入 Settling，先执行最小静默（取消 Token、冻结调度器），
            // 保证退出时迟到回调与到期动作不继续触发。
            // task 6.8：同时清空 InputController 的 CommandId 缓存，确保不跨局保留
            // （spec "Restart creates clean per-battle state"）。若已进入 Settling，
            // 缓存已在 EnterSettling 步骤 1 清空，此处跳过。
            if (!IsSettling)
            {
                try
                {
                    InputController?.GameOver();
                }
                catch (Exception ex)
                {
                    Log.Error($"{LogTag} Dispose 关闭 InputController 异常: {ex}");
                }

                try
                {
                    InputController?.ClearProcessedCommands();
                }
                catch (Exception ex)
                {
                    Log.Error($"{LogTag} Dispose 清空 InputController CommandId 缓存异常: {ex}");
                }

                try
                {
                    if (RuntimeTokenSource != null && !RuntimeTokenSource.IsCancellationRequested)
                    {
                        RuntimeTokenSource.Cancel();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"{LogTag} Dispose 取消运行时 Token 异常: {ex}");
                }

                try
                {
                    Simulation?.ActionScheduler?.Freeze();
                }
                catch (Exception ex)
                {
                    Log.Error($"{LogTag} Dispose 冻结 ActionScheduler 异常: {ex}");
                }
            }

            // 逆序释放 Scope 全部登记所有权（CTS Dispose、GameEvent Clear、资源句柄 Release、
            // 池租借归还等）。BattleRuntimeScope.Dispose 幂等逆序，单条异常不阻断后续释放。
            try
            {
                Scope?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Dispose 释放 Scope 异常: {ex}");
            }

            // 断言全部所有权已释放（诊断用）。
            // 若 Scope 仍有活动登记，记录 Error 便于诊断泄漏（不抛异常）。
            if (Scope != null && !Scope.AssertAllReleased())
            {
                Log.Error($"{LogTag} Dispose 后仍有活动所有权未释放，可能存在资源泄漏");
            }

            IsDisposed = true;
            Log.Info($"{LogTag} Dispose 完成，Runtime 已销毁");
        }
    }
}
