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
    /// 清理攻击效果 → 清理投射物 → 先停止 WaveManager 再清理敌人及空间索引（task 4.9：
    /// 波次停止必须前置，使 Forced 移除事实不促成波次完成或误判胜利）→ 清理单位及监听 →
    /// 清理波次/牌组/预留（含 WaveManager.Cleanup 与 Boss 端口）→ 解除剩余局部监听 →
    /// 断言无活动对象 → 发布已冻结结果。</para>
    /// <para>当前骨架实现已实现部分（停止模拟、取消 Token、释放 Scope），后续 Phase 接入 Manager 后
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

        /// <summary>本局确定性 Buff 所有者。</summary>
        public BuffManager BuffManager { get; }

        /// <summary>本局 Boss 技能生命周期所有者。</summary>
        internal SkillRunner SkillRunner { get; }

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
        /// <para>注入依赖：UnitFactory / UnitRegistry / BattleEconomy / UnitSlotBoard /
        /// RecruitManager / UnitLevelService / BattleConfigSnapshot。</para>
        /// <para>独占语义：不跨局复用；重开销毁旧 Runtime 时连同控制器一并销毁
        /// （BattleModule 新建 Runtime 时由 Factory 产生新控制器）。CommandId 去重缓存
        /// 由 task 6.8 在本类型内维护，随 Runtime 清理清空，不跨局保留。</para>
        /// <para>生命周期钩子：StartGame / GameOver 由 BattleRuntime / BattleManager 在战斗
        /// 开始/结束时调用（task 6.10 接入）。StartGame 后 Execute 接受命令；
        /// GameOver 后 Execute 返回失败。</para>
        /// </summary>
        public BattleInputController InputController { get; }

        // ====================================================================
        // task 6.10 闭环新增：闭环必需的 Manager / 状态 / 服务
        // --------------------------------------------------------------------
        // 以下属性在 task 6.10 接入时由 Assembly 注入，使 Runtime 能在 EnterSettling
        // 补全步骤 5-8 并在 phaseHandlers 接入实际 Manager 的 update 方法。
        // ====================================================================

        /// <summary>
        /// 本局权威战斗状态（task 3.7 产物）。
        /// <para>保存双方生命、金币、波次等权威可变状态，只由本局规则服务经 Apply* 方法修改。
        /// Runtime 在 EnterSettling 步骤 7 调用 GameOver 重置，并在 phaseHandlers 中供
        /// BattleManager 读取波次/生命等状态。</para>
        /// </summary>
        public BattleState BattleState { get; }

        /// <summary>
        /// 本局战斗规则协调器（task 3.10 产物）。
        /// <para>管理波次运行态、战斗规则和胜负条件。波次推进唯一由 WaveManager 在
        /// phaseHandlers 的 WaveSpawn 阶段经 <see cref="WaveManager.Update"/> 驱动，
        /// 本类型不再持有旧 UpdateSpawnState。Runtime 在 EnterSettling/Dispose 清理时
        /// 调用 <see cref="BattleManager.GameOver"/> 重置规则状态（内部幂等再停止
        /// WaveManager）。</para>
        /// </summary>
        public BattleManager BattleManager { get; }

        /// <summary>
        /// 本局波次管理器（task 3.9 产物，task 4.2-4.5 改造为有序波次状态机）。
        /// <para>消费 <see cref="OrderedWavePlanSnapshot"/> 逐行推进 Normal/Boss 波，
        /// 不再按固定 Mob0 计划刷怪。Runtime 在 EnterSettling/Dispose 清理时先调用
        /// <see cref="WaveManager.Stop"/> 停止推进，再在清理尾部调用
        /// <see cref="WaveManager.Cleanup"/> 释放本局波次所有权与 Boss 端口。</para>
        /// </summary>
        public WaveManager WaveManager { get; }

        /// <summary>
        /// 本局经济服务（task 3.8 产物）。
        /// <para>处理招募、刷新、击杀奖励的校验与余额变更。Runtime 在 EnterSettling 步骤 7
        /// 调用 <see cref="BattleEconomy.GameOver"/> 重置经济计数。</para>
        /// </summary>
        public BattleEconomy BattleEconomy { get; }

        /// <summary>
        /// 本局格子预留注册表（task 3.8 产物）。
        /// <para>管理购买放置事务中的临时格子预留。Runtime 在 EnterSettling 步骤 7
        /// 调用 <see cref="PlacementReservationRegistry.Clear"/> 清空全部预留。</para>
        /// </summary>
        public PlacementReservationRegistry ReservationRegistry { get; }

        /// <summary>
        /// 本局敌人管理器（task 4.6 产物）。
        /// <para>维护敌人集合和空间索引，提供稳定查询、伤害入口、生成与清理。
        /// Runtime 在 phaseHandlers 的 Enemy 阶段回调 <see cref="EnemyManager.Update"/>，
        /// 在 EnterSettling 步骤 5 调用 <see cref="EnemyManager.GameOver"/> 清理敌人实体、
        /// 接触 Timer 和空间索引。</para>
        /// </summary>
        public EnemyManager EnemyManager { get; }

        /// <summary>
        /// 本局攻击调度器（task 5.2 产物）。
        /// <para>每子步只推进一次单位冷却、选取目标并触发一次攻击。Runtime 在 phaseHandlers
        /// 的 UnitAttack 阶段回调 <see cref="AttackScheduler.Update"/>。</para>
        /// </summary>
        public AttackScheduler AttackScheduler { get; }

        /// <summary>
        /// 本局结果冻结器（task 3.11 产物）。
        /// <para>提供幂等 TryFreeze 唯一入口。Simulation 的 tryFreeze 回调指向
        /// <see cref="BattleResultBuilder.IsFrozen"/>。BattleManager 在完成事实发生点
        /// 调用 TryFreezeResult → resultBuilder.TryFreeze。</para>
        /// </summary>
        public BattleResultBuilder ResultBuilder { get; }

        // ====================================================================
        // task 7.4 表现层只读状态视图
        // --------------------------------------------------------------------
        // BattleReadModel 由 Factory 构造并经 Assembly 注入，本类型接管持有。
        // BattlePresenter 只读本属性获取只读状态视图，把状态/事实翻译成视图操作，
        // 不回写规则状态（design.md:217 / spec "Settling has no gameplay damage authority"）。
        // ====================================================================

        /// <summary>
        /// 本局只读状态视图（task 3.7 产物，task 7.4 接入 Presenter）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 在构造 BattleResultBuilder 时一并构造，
        /// 经 Assembly 注入本类型。<see cref="BattlePresenter"/>（task 7.4）只读本属性
        /// 访问 BattleState 的只读快照，不直接访问 BattleState 或 Manager。</para>
        /// <para>spec "Settling has no gameplay damage authority"：Settling 只允许
        /// 发布不可变结果、生成只读快照和执行不回写规则状态的表现收尾。Presenter 经
        /// 本属性生成只读快照属 Settling 允许范畴。</para>
        /// <para>独占语义：不跨局复用；随 Runtime 销毁（ReadModel 不持有可释放资源，
        /// 只持有 BattleState 引用，随 Runtime Dispose 释放）。</para>
        /// </summary>
        public BattleReadModel ReadModel { get; }

        // ====================================================================
        // Phase 6 内部信号中枢（task 7.1 产物）
        // ====================================================================

        /// <summary>
        /// 本局内部低频一对多事实信号中枢（task 7.1 产物）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 构造并经 Assembly 注入，本类型接管持有。
        /// 仅承载需要一对多的单局低频内部事实（如 ROUND_SPAWN_PREPARED、HEALTH_CHANGED），
        /// 不承担核心一致性（敌人注册、空间索引、伤害等继续使用直接调用），
        /// 不承担跨程序集通信（开始/完成等走 <c>IBattlePublicEvent</c>）。</para>
        /// <para>订阅由 <see cref="Scope"/> 经 <see cref="BattleRuntimeScope.TrackSignalHub"/>
        /// 批量登记，在 Settling 静默清理、失败回滚或 Dispose 时由
        /// <see cref="BattleInternalSignalHub.Clear"/> 一次性解除
        /// （spec "Event subscriptions follow runtime lifetime"）。</para>
        /// <para>独占语义：不跨局复用；重开销毁旧 Runtime 时连同 SignalHub 一并清理
        /// （BattleModule 新建 Runtime 时由 Factory 产生新 SignalHub）。</para>
        /// </summary>
        public BattleInternalSignalHub SignalHub { get; }

        // ====================================================================
        // Phase 6 事件桥接器（task 7.2 产物）
        // ====================================================================

        /// <summary>
        /// 本局事件桥接器（task 7.2 产物）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 构造并经 Assembly 注入，本类型接管持有。
        /// 把 <see cref="SignalHub"/> 的单局低频内部信号桥接到 <c>IBattleUiEvent</c> UI 通知，
        /// 把开始/结果等少量事实转换成 <c>GameCommon</c> 公共 DTO 并发送 TEngine <c>GameEvent</c>
        /// （design.md Events/BattleEventBridge.cs / spec battle-event-boundary）。</para>
        /// <para>订阅生命周期：Bridge 订阅 SignalHub 的四个信号，由
        /// <see cref="Scope"/> 经 <see cref="BattleRuntimeScope.TrackDisposable"/> 登记，
        /// 在 Settling 静默清理、失败回滚或 Dispose 时调用 <c>BattleEventBridge.Dispose</c>
        /// 批量退订（spec "Event subscriptions follow runtime lifetime"）。</para>
        /// <para>跨程序集只传 GameCommon 不可变 DTO（spec "Cross-assembly events use immutable
        /// common contracts"）：<c>PublishBattleStarted</c>/<c>PublishBattleFinished</c>
        /// 只接受 <c>BattleLoadoutDto</c>/<c>BattleResultDto</c>（readonly struct），
        /// 不暴露 GameBattle 内部实体。</para>
        /// <para>独占语义：不跨局复用；重开销毁旧 Runtime 时连同 Bridge 一并 Dispose
        /// （BattleModule 新建 Runtime 时由 Factory 产生新 Bridge）。</para>
        /// </summary>
        public BattleEventBridge EventBridge { get; }

        // ====================================================================
        // task 7.4 表现层组装产物
        // --------------------------------------------------------------------
        // BattlePresenter 由 Factory 在组装阶段构造并注入，本类型接管持有。
        // Presenter 只读 ReadModel，把只读状态/事实翻译成视图操作，不回写规则状态
        // （design.md:217）。Presenter 持有 ViewRegistry / ViewSynchronizer / InputAdapter
        // 三个协作者，经 IBattleViewPort/IBattleAudioPort/IBattleVfxPort 端口接收逻辑层事实。
        //
        // 当前 Presenter 的三个端口默认为 Null 实现（task 7.3 产物），使纯逻辑闭环
        // 不依赖 Unity 表现层。生产环境由 BattleModule / 真实实现替换为 Unity 真实端口
        // （task 7.5/7.6 接入 FairyGUI / Unity 表现层后）。
        // ====================================================================

        /// <summary>
        /// 本局表现层组装器（task 7.4 产物）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 构造并经 Assembly 注入，本类型接管持有。
        /// 把只读状态/事实翻译成视图操作，不回写规则状态（design.md:217）。</para>
        /// <para>持有 <see cref="BattleViewRegistry"/> / <see cref="BattleViewSynchronizer"/> /
        /// <see cref="BattleInputAdapter"/> 三个协作者，经 IBattleViewPort /
        /// IBattleAudioPort / IBattleVfxPort 端口接收逻辑层事实。</para>
        /// <para>独占语义：不跨局复用；随 Runtime Dispose 调用 Presenter.Dispose
        /// 清理表现对象与监听（经 Scope 登记释放）。</para>
        /// </summary>
        public BattlePresenter Presenter { get; }

        // ====================================================================
        // 最终方案新增：槽位面板 / 征兵服务 / 等级数值服务
        // ====================================================================

        /// <summary>
        /// 本局槽位面板（最终方案"核心架构"）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 构造并经 Assembly 注入，集中维护槽、
        /// 单位和换槽不变量。UI、DeckManager、UnitRegistry 不再分别保存一份"单位在哪里"的状态。</para>
        /// </summary>
        public UnitSlotBoard SlotBoard { get; }

        /// <summary>
        /// 本局征兵服务（最终方案 Recruit/RecruitManager）。
        /// <para>只负责随机生成一整批 1 级四兵，不保存手牌和槽位状态，不负责上场。</para>
        /// </summary>
        public RecruitManager RecruitManager { get; }

        /// <summary>
        /// 本局等级数值服务（最终方案 Unit/UnitLevelService）。
        /// <para>校验最大等级、从 UnitLevelConfigSnapshot 解析伤害及攻速倍率、统一应用等级数值。</para>
        /// </summary>
        public UnitLevelService LevelService { get; }

        // ====================================================================
        // 预留扩展点（后续 Phase 产物，当前为骨架占位）
        // ====================================================================

        // task 6.10 闭环已接入的 Manager / 状态 / 服务（见上方 public 属性）：
        //   - BattleState（task 3.7）—— 双方生命、金币、波次等权威可变状态
        //   - BattleResultBuilder（task 3.11）—— 唯一结果冻结点
        //   - BattleManager（task 3.10）—— 战斗规则、波次、胜负
        //   - WaveManager（task 3.9）—— 确定性波次计划
        //   - BattleEconomy（task 3.8）—— 经济校验与余额变更
        //   - DeckManager（task 6.5）—— 牌组抽牌/补牌/刷新
        //   - PlacementReservationRegistry（task 3.8）—— 格子预留
        //   - EnemyManager（task 4.6）—— 敌人集合、空间索引、伤害入口
        //   - AttackScheduler（task 5.2）—— 单位攻击调度
        //   - AttackEffectManager（task 5.3）—— 攻击效果推进
        //   - ProjectileManager（task 5.8）—— 投射物推进
        //   - UnitFactory（task 6.3）—— 单位工厂
        //   - UnitRegistry（task 6.3）—— 单位注册表
        //   - BattleInputController（task 6.7）—— 输入命令执行

        // task 7.1 闭环已接入：BattleInternalSignalHub —— 单局低频一对多内部事实信号中枢。
        //   独占语义：每局由 Factory 新建，随 Runtime 销毁而 Clear，不跨局复用。
        //   订阅由 Scope 经 TrackSignalHub 批量登记，Dispose 时一次性 Clear。

        // task 7.2 闭环已接入：BattleEventBridge —— 单局事件桥接器。
        //   职责：SignalHub 信号→IBattleUiEvent UI 事件；GameCommon DTO→IBattlePublicEvent 跨程序集。
        //   独占语义：每局由 Factory 新建，随 Runtime 销毁而 Dispose，不跨局复用。
        //   订阅由 Scope 经 TrackDisposable 登记，Dispose 时批量退订 SignalHub 信号。

        // TODO Phase 3 task 4.1：BattlePoolScope —— 可跨局复用池容量与逐局清空活动对象。
        // TODO Phase 2/3：SeededRandomSource —— 确定性随机源（Ports/SeededRandomSource.cs）。
        //   独占语义：每局从 Loadout.RandomSeed 构造新实例，不沿用旧局随机进度。
        //   当前由 BattleRuntimeFactory 构造并经 DeckManager / WaveManager 持有。

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
            BuffManager = assembly.BuffManager;
            SkillRunner = assembly.SkillRunner;
            AttackEffectManager = assembly.AttackEffectManager;
            ProjectileManager = assembly.ProjectileManager;
            UnitFactory = assembly.UnitFactory;
            UnitRegistry = assembly.UnitRegistry;
            InputController = assembly.InputController;
            // task 6.10 闭环新增 Manager / 状态 / 服务
            BattleState = assembly.BattleState;
            BattleManager = assembly.BattleManager;
            WaveManager = assembly.WaveManager;
            BattleEconomy = assembly.BattleEconomy;
            ReservationRegistry = assembly.ReservationRegistry;
            EnemyManager = assembly.EnemyManager;
            AttackScheduler = assembly.AttackScheduler;
            ResultBuilder = assembly.ResultBuilder;
            // task 7.4 闭环新增：表现层只读状态视图（供 Presenter 只读访问）。
            ReadModel = assembly.ReadModel;
            // task 7.1 闭环新增：内部信号中枢。
            SignalHub = assembly.SignalHub;
            // task 7.2 闭环新增：事件桥接器。
            EventBridge = assembly.EventBridge;
            // task 7.4 闭环新增：表现层组装器。
            Presenter = assembly.Presenter;
            // 最终方案新增：槽位面板 / 征兵服务 / 等级数值服务。
            SlotBoard = assembly.SlotBoard;
            RecruitManager = assembly.RecruitManager;
            LevelService = assembly.LevelService;

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
        /// <item>先停止 <see cref="WaveManager"/>（task 4.9：Stop 置 stopped、清待出生与 handle、
        /// 幂等停止 Boss 端口），再清理 EnemyManager 的接触 Timer、实体和空间索引——
        /// 波次停止必须前置，使 Forced 移除事实不促成波次完成或误判胜利。</item>
        /// <item>清理 UnitRegistry 的监听、Timer 和实体（task 6.3 接入）。</item>
        /// <item>清理波次、牌组、预留及其他单局注册表：BattleManager.GameOver 重置规则状态，
        /// WaveManager.Cleanup（含 <c>IBossWavePort.Cleanup</c>）释放本局波次所有权，
        /// BattleEconomy.GameOver 重置经济计数，SlotBoard.GameOver 清空槽位，
        /// PlacementReservationRegistry.Clear 清空预留。</item>
        /// <item>解除剩余局部监听（task 7.1 接入：BattleInternalSignalHub.Clear）。</item>
        /// <item>断言没有活动 Timer、回调或租借对象。</item>
        /// <item>完成静默后发布一次已冻结的不可变结果。</item>
        /// </list>
        /// <para>当前实现已执行步骤 1（标记 Settling + 关闭 InputController + 清空
        /// CommandId 缓存 task 6.8）、步骤 2（停止模拟 + 取消 Token + 冻结调度器）、
        /// 步骤 3（清理 AttackEffectManager）、步骤 4（清理 ProjectileManager）、步骤 5
        /// （先停止 WaveManager 再清理 EnemyManager，task 4.9）、步骤 6（清理 UnitRegistry）、
        /// 步骤 7（清理 BattleManager/WaveManager/BattleEconomy/SlotBoard/PlacementReservationRegistry）
        /// 和步骤 8（清理 BattleInternalSignalHub）。步骤 9-10（断言与结果发布）
        /// 在后续 Phase 接入对应 Manager 后补充。</para>
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
            // 步骤 5-7：先停止波次，再清理敌人/单位/波次所有权（task 4.9）。
            // 顺序契约（task 4.9/4.10）：
            //   1. 先停止 WaveManager：Stop 先置 stopped，清待出生与活动 handle，并幂等停止
            //      Boss 端口。EnterSettling 一旦置 settling/停止推进，必须在任何
            //      EnemyManager.GameOver/实体批量移除前先 Stop，使 EnemyManager 清理时发布的
            //      Forced 移除事实不会令已停止的 WaveManager 完成或误判胜利。
            //   2. 再清理 EnemyManager（逐个 gameOver → 清集合/空间索引 → 归还对象池）。
            //   3. 清理 UnitRegistry（监听/Timer/实体回池）。
            //   4. BattleManager.GameOver 重置规则状态（内部幂等再停止 WaveManager）。
            //   5. WaveManager.Cleanup 释放本局波次所有权并调用 IBossWavePort.Cleanup。
            // 全部步骤幂等，单步异常只记录 warning 不阻断后续（保留原清理异常隔离）。
            // spec "Runtime quiescence and cleanup have one ordered owner"：
            //   清理敌人/单位/波次的接触 Timer、实体与空间索引。
            // ----------------------------------------------------------
            StopWavesAndClearCombatants();

            // 经济计数重置与槽位/预留清理（独立于波次/敌人/单位的清理契约）。
            try
            {
                BattleEconomy?.GameOver();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Settling 清理 BattleEconomy 异常: {ex}");
            }

            // 最终方案：清理槽位面板（清空槽、单位及运行时映射，不写存档）。
            try
            {
                SlotBoard?.GameOver();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Settling 清理 UnitSlotBoard 异常: {ex}");
            }

            try
            {
                ReservationRegistry?.Clear();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Settling 清理 PlacementReservationRegistry 异常: {ex}");
            }

            // ----------------------------------------------------------
            // 步骤 8：解除剩余局部监听（task 7.1 接入）。
            // BattleInternalSignalHub.Clear 一次性解除全部单局低频信号订阅，
            // 保证 Settling 中发布冻结结果时不会回调到已清理的订阅者，
            // 也保证重开后旧订阅不会回调旧运行时对象
            // （spec "Event subscriptions follow runtime lifetime" /
            ///  spec "Restart after listeners were registered"）。
            // Scope 已经 TrackSignalHub 登记，Dispose 时会再次幂等 Clear；
            // 此处在静默阶段先 Clear，使步骤 9 断言与步骤 10 结果发布前订阅已解除。
            // ----------------------------------------------------------
            try
            {
                SignalHub?.Clear();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Settling 清理 SignalHub 异常: {ex}");
            }

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
            if (!IsResultPublished)
            {
                if (ResultBuilder != null && ResultBuilder.IsFrozen)
                {
                    BattleResultDto result = ResultBuilder.GetFrozenResult();

                    // 表现层只消费不可变结果，不回写规则状态。
                    Presenter?.NotifyBattleFinished(result);

                    // 跨程序集只发布 GameCommon 的不可变 DTO。
                    EventBridge?.PublishBattleFinished(result);
                    IsResultPublished = true;
                }
                else
                {
                    Log.Warning($"{LogTag} EnterSettling 时结果尚未冻结，跳过完成事实发布");
                }
            }

            Log.Info($"{LogTag} Settling 静默清理完成（已停止模拟、取消 Token、冻结调度器、" +
                "先停止 WaveManager 再清理 Enemy/UnitRegistry，清理 BattleManager/WaveManager/" +
                "BattleEconomy/SlotBoard/PlacementReservationRegistry）");
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
        /// Dispose（如 Exit 从 Running 状态直接退出），本方法仍会释放全部所有权：先停止
        /// WaveManager、清理敌人/单位实体并归还对象池、完成 BattleManager/Wave/Boss cleanup
        /// （task 4.9：不能只依赖 Scope 强清导致 callbacks/ownership 残留），再取消 Token、
        /// 冻结调度器、逆序释放 Scope。</para>
        /// <para><b>已进入 Settling 后 Dispose：</b>EnterSettling 已完成波次/实体清理，
        /// 本方法跳过重复清理（幂等），只逆序释放 Scope，不重复产生副作用（task 4.9/4.10）。</para>
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

                // task 4.9：未进入 Settling 的 Dispose（取消/退出/异常清理）也必须先停止
                // WaveManager，再 EnemyManager.GameOver 清活动实体/回池，再完成
                // BattleManager/Wave/Boss cleanup。不能只依赖 Scope 强清导致 callbacks/
                // ownership 残留（敌人/单位池租借不在 BattleRuntimeScope 登记）。
                StopWavesAndClearCombatants();
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

        // ====================================================================
        // 波次/实体清理（task 4.9 顺序契约）
        // ====================================================================

        /// <summary>
        /// 停止波次并清理敌人/单位/波次所有权（EnterSettling 与未 Settling 的 Dispose 共用）。
        /// </summary>
        /// <remarks>
        /// <para><b>顺序契约（task 4.9/4.10）：</b>
        /// <list type="number">
        /// <item>先 <see cref="WaveManager.Stop"/>：置 stopped、清待出生与活动 handle、幂等停止
        /// Boss 端口。EnterSettling 一旦置 settling/停止推进，必须在任何
        /// <see cref="EnemyManager.GameOver"/> / 实体批量移除前先 Stop，使 Forced 移除事实
        /// 不会令已停止的 WaveManager 完成或误判胜利。</item>
        /// <item><see cref="EnemyManager.GameOver"/>：逐个通知敌人 gameOver、清空集合与空间索引、
        /// 归还对象池（此时 WaveManager 已停止，Forced 移除事实为幂等空操作）。</item>
        /// <item><see cref="UnitRegistry.ClearForSettling"/>：移除全部活动单位并归还池。</item>
        /// <item><see cref="BattleManager.GameOver"/>：重置规则状态（内部幂等再停止 WaveManager）。</item>
        /// <item><see cref="WaveManager.Cleanup"/>：释放本局波次所有权、调用
        /// <c>IBossWavePort.Cleanup</c>；不重启、不发布完成。</item>
        /// </list></para>
        /// <para><b>幂等与异常隔离：</b>所有步骤幂等，重复调用安全；单步异常只记录 Error，
        /// 不阻断后续步骤（保留原清理异常隔离），不改变外部事件顺序、Scope ownership 或
        /// 结果发布条件。</para>
        /// </remarks>
        private void StopWavesAndClearCombatants()
        {
            // 1. 先停止 WaveManager（波次停止前置，task 4.9）。
            try
            {
                WaveManager?.Stop();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 清理先停止 WaveManager 异常: {ex}");
            }

            // 2. scheduler 已由调用方冻结；先取消全部技能时间线与 owner。
            try
            {
                SkillRunner?.Clear();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 清理 SkillRunner 异常: {ex}");
            }

            // 3. 先移除 Boss，确保池归还前已解除技能所有权。
            try
            {
                EnemyManager?.ForceRemoveBosses();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 强制移除 Boss 异常: {ex}");
            }

            // 4. 清理 Buff；此时普通目标仍有效，且 scheduler 已冻结。
            try
            {
                BuffManager?.GameOver();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 清理 BuffManager 异常: {ex}");
            }

            // 5. 清理剩余敌人实体/回池。
            try
            {
                EnemyManager?.GameOver();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 清理 EnemyManager 异常: {ex}");
            }

            // 6. 清理单位注册表（监听/Timer/实体回池）。
            try
            {
                UnitRegistry?.ClearForSettling();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 清理 UnitRegistry 异常: {ex}");
            }

            // 7. 规则协调器 GameOver（重置权威状态；内部再次 Stop WaveManager 幂等）。
            try
            {
                BattleManager?.GameOver();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 清理 BattleManager 异常: {ex}");
            }

            // 8. WaveManager Cleanup（Boss 端口再次清理为空操作）。
            try
            {
                WaveManager?.Cleanup();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 清理 WaveManager 所有权/Boss 端口异常: {ex}");
            }

            if (BuffManager != null
                && (BuffManager.ActiveInstanceCount != 0
                    || BuffManager.RegisteredTargetCount != 0
                    || BuffManager.OwnedScheduleCount != 0))
            {
                Log.Error(
                    $"{LogTag} Buff 清理后仍有残留：instances={BuffManager.ActiveInstanceCount} " +
                    $"targets={BuffManager.RegisteredTargetCount} schedules={BuffManager.OwnedScheduleCount}");
            }

            if (SkillRunner != null
                && (SkillRunner.OwnerCount != 0 || SkillRunner.RunningActivationCount != 0))
            {
                Log.Error(
                    $"{LogTag} Skill 清理后仍有残留：owners={SkillRunner.OwnerCount} " +
                    $"running={SkillRunner.RunningActivationCount}");
            }
        }
    }
}
