using System;
using System.Collections.Generic;
using GameCommon.Battle;
using GameBattle.Weapon;
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
        /// 本次组装从应用级 ConfigSystem.Tables 复制的不可变战斗配置快照
        /// （task 3.4 / specs/battle-config-snapshot "Runtime consumes an immutable
        /// configuration snapshot"）。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 在组装阶段通过
        /// <see cref="LubanBattleConfigProvider"/> 从 <see cref="Tables"/>
        /// 复制一次，之后运行时只持有本快照，不再访问资源加载器或可变全局配置表，
        /// 也不由 <see cref="BattleRuntime"/> 卸载应用级配置资源（决策 0.11）。</para>
        /// </summary>
        public readonly BattleConfigSnapshot ConfigSnapshot;

        /// <summary>本局确定性 Buff 所有者。</summary>
        public readonly BuffManager BuffManager;

        /// <summary>本局 Boss 技能运行器。</summary>
        public readonly SkillRunner SkillRunner;

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

        /// <summary>本局可选的本地对手 AI；OpponentMode=None 时为 null。</summary>
        public readonly OpponentAI OpponentAI;

        // ====================================================================
        // task 6.10 新增：闭环必需的 Manager / 状态 / 服务
        // --------------------------------------------------------------------
        // 以下字段在 task 6.10 闭环接入时由 Factory 构造并经 Assembly 传递给
        /// <see cref="BattleRuntime"/>，使 Runtime 能在 EnterSettling 补全步骤 5-8
        /// 并在 phaseHandlers 接入实际 Manager 的 update 方法。
        // ====================================================================

        /// <summary>
        /// 本次组装产生的权威战斗状态（task 3.7 产物）。
        /// <para>由 Factory 构造，供 BattleManager/WaveManager/BattleEconomy 等规则服务
        /// 经 Apply* 方法提交变更。Runtime 通过此属性在 EnterSettling 步骤 7 调用
        /// BattleState 重置并供 phaseHandlers 读取波次/生命等状态。</para>
        /// </summary>
        public readonly BattleState BattleState;

        /// <summary>
        /// 本次组装产生的战斗规则协调器（task 3.10 产物）。
        /// <para>由 Factory 构造，持有 WaveManager/BattleEconomy/BattleResultBuilder 引用。
        /// 波次推进由 WaveSpawn phase 唯一调用 <c>waveManager.Update(stepMs)</c>（任务 4.6），
        /// Runtime 在 EnterSettling 步骤 7 调用
        /// <see cref="BattleManager.GameOver"/> 重置规则状态。</para>
        /// </summary>
        public readonly BattleManager BattleManager;

        /// <summary>
        /// 本次组装产生的波次管理器（task 3.9 产物）。
        /// <para>由 Factory 构造，提供确定性 Mob0 波次计划。Runtime 在 EnterSettling
        /// 步骤先调用 <see cref="WaveManager.Stop"/> 停止推进，再在清理尾部调用
        /// <see cref="WaveManager.Cleanup"/> 释放本局波次所有权与 Boss 端口。</para>
        /// </summary>
        public readonly WaveManager WaveManager;

        /// <summary>
        /// 本次组装产生的经济服务（task 3.8 产物）。
        /// <para>由 Factory 构造，处理招募/刷新/击杀奖励的校验与余额变更。Runtime 在
        /// EnterSettling 步骤 7 调用 <see cref="BattleEconomy.GameOver"/> 重置经济计数。</para>
        /// </summary>
        public readonly BattleEconomy BattleEconomy;

        /// <summary>
        /// 本次组装产生的格子预留注册表（task 3.8 产物）。
        /// <para>由 Factory 构造，管理购买放置事务的临时格子预留。Runtime 在 EnterSettling
        /// 步骤 7 调用 <see cref="PlacementReservationRegistry.Clear"/> 清空全部预留。</para>
        /// </summary>
        public readonly PlacementReservationRegistry ReservationRegistry;

        /// <summary>
        /// 本次组装产生的敌人管理器（task 4.6 产物）。
        /// <para>由 Factory 构造，维护敌人集合与空间索引。Runtime 在 phaseHandlers 的
        /// Enemy 阶段回调 <see cref="EnemyManager.Update"/>，在 EnterSettling 步骤 5
        /// 调用 <see cref="EnemyManager.GameOver"/> 清理敌人实体、接触 Timer 与空间索引。</para>
        /// </summary>
        public readonly EnemyManager EnemyManager;

        /// <summary>
        /// 本次组装产生的攻击调度器（task 5.2 产物）。
        /// <para>由 Factory 构造，每子步只推进一次单位冷却、选取目标并触发一次攻击。
        /// Runtime 在 phaseHandlers 的 UnitAttack 阶段回调
        /// <see cref="AttackScheduler.Update"/>。</para>
        /// </summary>
        public readonly AttackScheduler AttackScheduler;

        /// <summary>
        /// 本次组装产生的结果冻结器（task 3.11 产物）。
        /// <para>由 Factory 构造，提供幂等 TryFreeze 唯一入口。Simulation 的 tryFreeze
        /// 回调指向 <see cref="BattleResultBuilder.TryFreeze"/>。</para>
        /// </summary>
        public readonly BattleResultBuilder ResultBuilder;

        /// <summary>
        /// 本次组装产生的单局内部信号中枢（task 7.1 产物）。
        /// <para>由 Factory 构造并登记到 Scope（<see cref="BattleRuntimeScope.TrackSignalHub"/>），
        /// <see cref="BattleRuntime"/> 接管后持有。仅承载需要一对多的单局低频内部事实，
        /// 不承担核心一致性（直接调用）与跨程序集通信（<c>IBattlePublicEvent</c>）。
        /// 订阅随 Scope 批量解除（spec "Event subscriptions follow runtime lifetime"）。</para>
        /// </summary>
        public readonly BattleInternalSignalHub SignalHub;

        /// <summary>
        /// 本次组装产生的单局事件桥接器（task 7.2 产物）。
        /// <para>由 Factory 在构造 SignalHub 之后立即构造，订阅 SignalHub 的四个信号，
        /// 桥接到 <c>IBattleUiEvent</c> UI 通知；并提供 <c>PublishBattleStarted</c>/
        /// <c>PublishBattleFinished</c> 跨程序集发送（经 <c>IBattlePublicEvent</c>）。</para>
        /// <para>Bridge 的 SignalHub 订阅由自身持有退订句柄，通过
        /// <see cref="BattleRuntimeScope.TrackDisposable"/> 登记到 Scope，
        /// 在失败回滚/Settling/Dispose 时调用 <c>BattleEventBridge.Dispose</c> 批量退订
        /// （spec "Event subscriptions follow runtime lifetime"）。</para>
        /// <para>前提：<c>GameEventHelper.Init()</c> 已在 <c>GameApp.Entrance</c> 第一行
        /// 最先调用（1.6 spike §3.2），保证全局 EventMgr 就绪。Factory 不调用 Init()。</para>
        /// </summary>
        public readonly BattleEventBridge EventBridge;

        // ====================================================================
        // task 7.3 返工新增：表现端口（View / Audio / Vfx）
        // --------------------------------------------------------------------
        // 三个表现端口由 Factory 在组装阶段以 Null 实现注入作为默认值，使纯逻辑
        // EditMode 测试与无表现逻辑闭环不依赖 Unity 表现层（design.md:9,215,216）。
        // 生产环境由 BattleModule / BattlePresenter 在 Entering 阶段替换为 Unity
        // 真实实现（UnityBattleAudioPort / UnityBattleVfxPort / FairyGUI ViewPort，
        /// task 7.4/7.5/7.6 接入）。
        //
        // 当前默认注入 NullBattleViewPort / NullBattleAudioPort / NullBattleVfxPort，
        // 保证 task 7.3 的 Null 实现被实际接入并验证可实例化（校验报告"Null 实现未接入"）。
        // ====================================================================

        /// <summary>
        /// 本次组装产生的视图表现端口（task 7.3 产物）。
        /// <para>默认为 <see cref="NullBattleViewPort"/>，使逻辑闭环不依赖 Unity/FairyGUI 表现层。
        /// 生产环境由 BattleModule/BattlePresenter 在 Entering 阶段替换为 FairyGUI 真实实现
        /// （task 7.4/7.5，受 FairyGUI 程序集未引用限制推迟到 7.5）。</para>
        /// <para>逻辑层经本端口向表现层发送实体生成/移除/状态变化等低频事实
        /// （design.md:214）。表现层实现本端口，把事实翻译成视图操作，不回写规则状态
        /// （design.md:217）。</para>
        /// </summary>
        public readonly IBattleViewPort ViewPort;

        /// <summary>
        /// 本次组装产生的音频表现端口（task 7.3 产物）。
        /// <para>默认为 <see cref="NullBattleAudioPort"/>，使逻辑闭环不依赖 Unity 音频。
        /// 生产环境由 BattleModule/BattlePresenter 替换为 <see cref="UnityBattleAudioPort"/>
        /// （基于 TEngine IAudioModule，不受 asmdef 限制）。</para>
        /// <para>逻辑层经本端口发送 BGM/SFX 播放停止意图（design.md:215）。</para>
        /// </summary>
        public readonly IBattleAudioPort AudioPort;

        /// <summary>
        /// 本次组装产生的特效表现端口（task 7.3 产物）。
        /// <para>默认为 <see cref="NullBattleVfxPort"/>，使逻辑闭环不依赖 Unity 特效。
        /// 生产环境由 BattleModule/BattlePresenter 替换为 <see cref="UnityBattleVfxPort"/>
        /// （基于 UnityEngine.ParticleSystem + 对象池，不受 asmdef 限制）。</para>
        /// <para>逻辑层经本端口发送特效播放意图（design.md:216）。</para>
        /// </summary>
        public readonly IBattleVfxPort VfxPort;

        // ====================================================================
        // task 7.4 表现层组装产物
        // --------------------------------------------------------------------
        // BattleReadModel 与 BattlePresenter 由 Factory 在组装阶段构造并经 Assembly
        // 注入。ReadModel 在步骤 7 构造（与 BattleResultBuilder 共享同一实例），
        // Presenter 在步骤 11 之后构造（依赖 ReadModel + ViewPort/AudioPort/VfxPort +
        // InputController）。Presenter 持有 ViewRegistry/ViewSynchronizer/InputAdapter
        // 三个协作者，把只读状态/事实翻译成视图操作，不回写规则状态（design.md:217）。
        // ====================================================================

        /// <summary>
        /// 本次组装产生的只读状态视图（task 3.7 产物，task 7.4 接入 Presenter）。
        /// <para>与 <see cref="ResultBuilder"/> 共享同一实例（task 3.11 时由 Factory 构造并
        /// 注入 ResultBuilder）。task 7.4 起经 Assembly 暴露给
        /// <see cref="BattleRuntime"/>/<see cref="BattlePresenter"/>，使 Presenter 只读访问
        /// BattleState 快照，不直接访问 BattleState 或 Manager。</para>
        /// </summary>
        public readonly BattleReadModel ReadModel;

        /// <summary>
        /// 本次组装产生的表现层组装器（task 7.4 产物）。
        /// <para>由 Factory 在构造 ReadModel + 三个端口 + InputController 之后构造，
        /// 持有 ViewRegistry / ViewSynchronizer / BattleInputAdapter 三个协作者。
        /// Presenter 只读 ReadModel，把只读状态/事实翻译成视图操作，不回写规则状态
        /// （design.md:217）。</para>
        /// <para>三个端口默认为 Null 实现（task 7.3），生产环境由 BattleModule 替换为
        /// Unity 真实实现（task 7.5/7.6 接入 FairyGUI / Unity 表现层）。</para>
        /// <para>Presenter 经 <see cref="BattleRuntimeScope.TrackDisposable"/> 登记到 Scope，
        /// 在失败回滚/Settling/Dispose 时调用 <c>Presenter.Dispose</c> 清理表现对象与监听。</para>
        /// </summary>
        public readonly BattlePresenter Presenter;

        /// <summary>
        /// 本次组装产生的槽位面板（最终方案"核心架构"）。
        /// <para>由 Factory 构造，集中维护槽、单位和换槽不变量。UI、DeckManager、
        /// UnitRegistry 不再分别保存一份"单位在哪里"的状态。</para>
        /// </summary>
        public readonly UnitSlotBoard SlotBoard;

        /// <summary>
        /// 本次组装产生的征兵服务（最终方案 Recruit/RecruitManager）。
        /// <para>只负责随机生成一整批 1 级四兵，不保存手牌和槽位状态，不负责上场。</para>
        /// </summary>
        public readonly RecruitManager RecruitManager;

        /// <summary>
        /// 本次组装产生的等级数值服务（最终方案 Unit/UnitLevelService）。
        /// <para>校验最大等级、从 UnitLevelConfigSnapshot 解析伤害及攻速倍率、统一应用等级数值。</para>
        /// </summary>
        public readonly UnitLevelService LevelService;

        private BattleRuntimeAssembly(
            BattleErrorCode errorCode,
            string diagnosticMessage,
            BattleRuntimeScope scope,
            BattleSimulation simulation,
            BattleLoadoutDto loadout,
            BattleConfigSnapshot configSnapshot,
            BuffManager buffManager,
            SkillRunner skillRunner,
            AttackEffectManager attackEffectManager,
            ProjectileManager projectileManager,
            UnitFactory unitFactory,
            UnitRegistry unitRegistry,
            BattleInputController inputController,
            OpponentAI opponentAI,
            BattleState battleState,
            BattleManager battleManager,
            WaveManager waveManager,
            BattleEconomy battleEconomy,
            PlacementReservationRegistry reservationRegistry,
            EnemyManager enemyManager,
            AttackScheduler attackScheduler,
            BattleResultBuilder resultBuilder,
            BattleInternalSignalHub signalHub,
            BattleEventBridge eventBridge,
            IBattleViewPort viewPort,
            IBattleAudioPort audioPort,
            IBattleVfxPort vfxPort,
            BattleReadModel readModel,
            BattlePresenter presenter,
            UnitSlotBoard slotBoard,
            RecruitManager recruitManager,
            UnitLevelService levelService)
        {
            ErrorCode = errorCode;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
            Scope = scope;
            Simulation = simulation;
            Loadout = loadout;
            ConfigSnapshot = configSnapshot;
            BuffManager = buffManager;
            SkillRunner = skillRunner;
            AttackEffectManager = attackEffectManager;
            ProjectileManager = projectileManager;
            UnitFactory = unitFactory;
            UnitRegistry = unitRegistry;
            InputController = inputController;
            OpponentAI = opponentAI;
            BattleState = battleState;
            BattleManager = battleManager;
            WaveManager = waveManager;
            BattleEconomy = battleEconomy;
            ReservationRegistry = reservationRegistry;
            EnemyManager = enemyManager;
            AttackScheduler = attackScheduler;
            ResultBuilder = resultBuilder;
            SignalHub = signalHub;
            EventBridge = eventBridge;
            ViewPort = viewPort;
            AudioPort = audioPort;
            VfxPort = vfxPort;
            ReadModel = readModel;
            Presenter = presenter;
            SlotBoard = slotBoard;
            RecruitManager = recruitManager;
            LevelService = levelService;
        }

        /// <summary>
        /// 构造成功产物。
        /// </summary>
        internal static BattleRuntimeAssembly Ok(
            BattleRuntimeScope scope,
            BattleSimulation simulation,
            BattleLoadoutDto loadout,
            BattleConfigSnapshot configSnapshot,
            BuffManager buffManager,
            SkillRunner skillRunner,
            AttackEffectManager attackEffectManager,
            ProjectileManager projectileManager,
            UnitFactory unitFactory,
            UnitRegistry unitRegistry,
            BattleInputController inputController,
            OpponentAI opponentAI,
            BattleState battleState,
            BattleManager battleManager,
            WaveManager waveManager,
            BattleEconomy battleEconomy,
            PlacementReservationRegistry reservationRegistry,
            EnemyManager enemyManager,
            AttackScheduler attackScheduler,
            BattleResultBuilder resultBuilder,
            BattleInternalSignalHub signalHub,
            BattleEventBridge eventBridge,
            IBattleViewPort viewPort,
            IBattleAudioPort audioPort,
            IBattleVfxPort vfxPort,
            BattleReadModel readModel,
            BattlePresenter presenter,
            UnitSlotBoard slotBoard,
            RecruitManager recruitManager,
            UnitLevelService levelService)
            => new BattleRuntimeAssembly(
                BattleErrorCode.None,
                string.Empty,
                scope,
                simulation,
                loadout,
                configSnapshot,
                buffManager,
                skillRunner,
                attackEffectManager,
                projectileManager,
                unitFactory,
                unitRegistry,
                inputController,
                opponentAI,
                battleState,
                battleManager,
                waveManager,
                battleEconomy,
                reservationRegistry,
                enemyManager,
                attackScheduler,
                resultBuilder,
                signalHub,
                eventBridge,
                viewPort,
                audioPort,
                vfxPort,
                readModel,
                presenter,
                slotBoard,
                recruitManager,
                levelService);

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
                configSnapshot: null,
                buffManager: null,
                skillRunner: null,
                attackEffectManager: null,
                projectileManager: null,
                unitFactory: null,
                unitRegistry: null,
                inputController: null,
                opponentAI: null,
                battleState: null,
                battleManager: null,
                waveManager: null,
                battleEconomy: null,
                reservationRegistry: null,
                enemyManager: null,
                attackScheduler: null,
                resultBuilder: null,
                signalHub: null,
                eventBridge: null,
                viewPort: null,
                audioPort: null,
                vfxPort: null,
                readModel: null,
                presenter: null,
                slotBoard: null,
                recruitManager: null,
                levelService: null);
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
    /// <item>Factory 只负责校验输入、构造模拟器等强类型依赖、登记所有权、记录初始化步骤日志。</item>
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
        /// <returns>
        /// 组装产物 <see cref="BattleRuntimeAssembly"/>。成功时 <see cref="BattleRuntimeAssembly.IsSuccess"/>
        /// 为 true；失败时携带稳定错误码与诊断信息，且 Scope 已被回滚释放。
        /// </returns>
        /// <remarks>
        /// <para><b>组装步骤顺序（每步登记所有权到新建的 Scope）：</b></para>
        /// <list type="number">
        /// <item>创建 <see cref="BattleRuntimeScope"/>（本次组装的所有权根）。</item>
        /// <item>构造 <see cref="BattleActionScheduler"/>（到期动作/冷却调度器）。</item>
        /// <item>构造 <see cref="BattleSimulation"/>（逻辑时钟入口），注入阶段回调占位与调度器。</item>
        /// <item>校验装载信息（牌组预设、配置版本占位），失败返回
        /// <see cref="BattleErrorCode.ConfigInvalid"/> 或 <see cref="BattleErrorCode.ConfigVersionMismatch"/>。
        /// 配置版本字段的权威校验在此步骤（详见 <see cref="TryValidateLoadout"/>）。</item>
        /// <item>从应用级 <see cref="Tables"/> 复制不可变配置快照
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
        /// <item>构造 Phase 6 <see cref="BattleInternalSignalHub"/>（task 7.1 产物）并登记到 Scope
        /// （<see cref="BattleRuntimeScope.TrackSignalHub"/>）。订阅随 Scope 批量解除
        /// （spec "Event subscriptions follow runtime lifetime"）。</item>
        /// </list>
        /// <para>任一步骤失败：记录日志，调用 <see cref="BattleRuntimeScope.Rollback"/>
        /// 只释放本次已取得的所有权，返回失败产物。不抛出预期失败异常（决策 0.7）。</para>
        /// </remarks>
        internal static BattleRuntimeAssembly Create(
            BattleLoadoutDto loadout,
            BattlePoolScope poolScope = null)
        {
            return Create(loadout, poolScope, bindings: null, configSnapshot: null);
        }

        /// <summary>
        /// 以已校验的地图绑定组装战斗运行时。
        /// </summary>
        /// <remarks>
        /// 只有提供地图绑定时才创建 Unity 表现端口；旧的三参数调用保持 Null 端口，
        /// 以保留纯逻辑测试与尚未接入 BattleWorldHost 的调用语义。
        /// </remarks>
        internal static BattleRuntimeAssembly Create(
            BattleLoadoutDto loadout,
            BattlePoolScope poolScope,
            BattleMapBindings bindings)
        {
            return Create(loadout, poolScope, bindings, configSnapshot: null);
        }

        /// <summary>
        /// 以已准备的地图绑定与配置快照组装战斗运行时（生产入口）。
        /// </summary>
        /// <remarks>
        /// <para><b>消费已准备 Context（design.md 决策 3 / task 3.5）：</b>
        /// <paramref name="configSnapshot"/> 由 <see cref="BattleStartupContext.Prepare"/>
        /// 一次性解析并校验；本方法直接复用，避免二次读取配置。为 null 时保持
        /// 旧兼容路径：从 ConfigSystem.Tables 内部读取（测试/兼容调用）。</para>
        /// </remarks>
        internal static BattleRuntimeAssembly Create(
            BattleLoadoutDto loadout,
            BattlePoolScope poolScope,
            BattleMapBindings bindings,
            BattleConfigSnapshot configSnapshot,
            OpponentAiReplayLog opponentAiReplayLog = null)
        {
            // 步骤 1：创建本次组装的所有权作用域。这是本次 Create 取得全部所有权的根，
            // 失败时只回滚这个 Scope，不触碰调用方的外部对象。
            Log.Info($"{LogTag} 开始组装一局运行时依赖，mapId={loadout.MapId} round={loadout.Round} seed={loadout.RandomSeed}");
            BattleRuntimeScope scope = new BattleRuntimeScope();
            // 生产路径由 BattleModule 注入跨局池作用域；独立 Factory 测试未注入时
            // 使用本次组装私有作用域，随 Runtime 不可达后由 GC 回收。
            BattlePoolScope effectivePoolScope = poolScope ?? new BattlePoolScope();

            BattleSimulation simulation = null;
            BattleActionScheduler actionScheduler = null;
            BuffManager buffManager = null;
            SkillRunner skillRunner = null;
            AttackEffectManager attackEffectManager = null;
            ProjectileManager projectileManager = null;
            UnitFactory unitFactory = null;
            UnitRegistry unitRegistry = null;
            BattleInputController inputController = null;
            OpponentAI opponentAI = null;
            OpponentAiProfileSnapshot opponentAiProfile = null;
            BattleCommandIdAllocator commandIdAllocator = null;
            BattleRandomStreams randomStreams = null;

            // task 6.10 闭环新增的 Manager / 状态 / 服务
            BattleState battleState = null;
            BattleManager battleManager = null;
            WaveManager waveManager = null;
            BattleEconomy battleEconomy = null;
            PlacementReservationRegistry reservationRegistry = null;
            EnemyManager enemyManager = null;
            AttackScheduler attackScheduler = null;
            BattleResultBuilder resultBuilder = null;

            // task 7.1 闭环新增：单局内部信号中枢。
            BattleInternalSignalHub signalHub = null;

            // task 7.2 闭环新增：单局事件桥接器。
            BattleEventBridge eventBridge = null;

            // task 7.3 返工新增：表现端口（View / Audio / Vfx）。
            // 默认以 Null 实现注入，使纯逻辑 EditMode 测试与无表现逻辑闭环不依赖
            // Unity/FairyGUI 表现层（design.md:9,215,216）。生产环境由 BattleModule /
            // BattlePresenter 在 Entering 阶段替换为 Unity 真实实现。
            IBattleViewPort viewPort = null;
            IBattleAudioPort audioPort = null;
            IBattleVfxPort vfxPort = null;

            // task 7.4 闭环新增：表现层只读状态视图与组装器。
            BattleReadModel readModel = null;
            BattlePresenter presenter = null;

            // 最终方案新增：槽位面板、征兵服务、等级数值服务。
            UnitSlotBoard slotBoard = null;
            RecruitManager recruitManager = null;
            UnitLevelService levelService = null;
            OpponentDeckManager opponentDeck = null;
            OpponentHand opponentHand = null;

            try
            {
                // task 7.1 闭环：构造单局内部信号中枢并登记到 Scope。
                // SignalHub 仅承载需要一对多的单局低频内部事实；订阅由 Scope 经
                // TrackSignalHub 批量登记，在失败回滚/Settling/Dispose 时一次性 Clear
                // （spec "Event subscriptions follow runtime lifetime"）。
                // 尽早构造以保证后续 WaveManager 桥接、表现层订阅都能使用同一实例。
                signalHub = new BattleInternalSignalHub();
                scope.TrackSignalHub(signalHub, "InternalSignalHub");

                // task 7.2 闭环：构造单局事件桥接器并登记到 Scope。
                // BattleEventBridge 订阅 SignalHub 的四个信号，桥接到 IBattleUiEvent UI 事件；
                // 并提供 PublishBattleStarted/PublishBattleFinished 跨程序集发送
                // （经 GameCommon IBattlePublicEvent + TEngine GameEvent）。
                // 前提：GameEventHelper.Init() 已在 GameApp.Entrance 第一行最先调用
                // （1.6 spike §3.2），本 Factory 不调用 Init()。
                // Bridge 的 SignalHub 订阅由自身持有退订句柄，通过 TrackDisposable 登记，
                // 在失败回滚/Settling/Dispose 时调用 BattleEventBridge.Dispose 批量退订
                // （spec "Event subscriptions follow runtime lifetime"）。
                eventBridge = new BattleEventBridge(signalHub);
                scope.TrackDisposable(eventBridge, "EventBridge");

                // 步骤 3：构造到期动作/冷却调度器（task 2.11 产物，强类型注入，非字符串查找）。
                Log.Info($"{LogTag} 步骤 2/12：构造 BattleActionScheduler");
                actionScheduler = new BattleActionScheduler();

                // 步骤 4：构造逻辑模拟器（task 2.11 产物）。
                // task 6.10 闭环：phaseHandlers 与 tryFreeze 回调连接到实际 Manager 的 update 方法。
                // 但 Simulation 依赖的 Manager 尚未构造（步骤 8-10），此处先创建 Simulation
                // 的依赖顺序需要调整：先构造 Manager（步骤 5-10），再创建带真实 phaseHandlers
                // 的 Simulation。为保持步骤日志编号一致且不改变所有权登记顺序，此处先创建
                // Simulation 的占位实例（使步骤编号不变），在全部 Manager 构造完成后（步骤 10）
                // 用真实 phaseHandlers 重建 Simulation。
                //
                // 实际实现：此处先不创建 Simulation，推迟到步骤 10。步骤 3 只构造
                // BattleActionScheduler（Simulation 的依赖），Simulation 在步骤 11 构造。
                Log.Info($"{LogTag} 步骤 3/12：BattleActionScheduler 已构造，Simulation 推迟到步骤 10");

                // 步骤 5：校验装载信息。预期校验失败返回结构化错误码，不抛异常（决策 0.7）。
                Log.Info($"{LogTag} 步骤 4/12：校验装载信息");
                if (!TryValidateLoadout(loadout, out BattleErrorCode validateError, out string validateMsg))
                {
                    // 校验失败：回滚本次已取得的所有权，返回失败产物。
                    Log.Warning($"{LogTag} 装载信息校验失败 code={validateError} msg={validateMsg}");
                    scope.Rollback();
                    return BattleRuntimeAssembly.Fail(validateError, validateMsg, scope);
                }

                // 步骤 6：从应用级 ConfigSystem.Tables 复制不可变配置快照（task 3.4）。
                // task 3.5：生产路径由 BattleModule 经 BattleStartupContext.Prepare 一次性
                // 解析并校验配置快照并传入本参数，此处直接复用避免二次读取；
                // 为 null 时（测试/兼容调用）保持旧路径内部读取。
                Log.Info($"{LogTag} 步骤 5/12：获取战斗配置快照（已准备则复用）");
                try
                {
                    if (configSnapshot == null)
                    {
                        Tables tables = ConfigSystem.Instance.Tables;
                        var provider = new LubanBattleConfigProvider(tables);
                        configSnapshot = provider.GetSnapshot(loadout.MapId);
                    }
                }
                catch (BattleMapConfigMissingException configMissingEx)
                {
                    // 指定 MapId 的地图行缺失：返回 ConfigMissing（spec "Reject an unknown map"）。
                    Log.Error($"{LogTag} 配置快照复制失败（地图行缺失）: {configMissingEx}");
                    scope.Rollback();
                    return BattleRuntimeAssembly.Fail(
                        BattleErrorCode.ConfigMissing,
                        $"MapId={configMissingEx.MapId} 无对应地图行",
                        scope);
                }
                catch (Exception configEx)
                {
                    // 配置快照复制失败：回滚本次已取得的所有权，返回配置错误。
                    Log.Error($"{LogTag} 配置快照复制失败: {configEx}");
                    scope.Rollback();
                    return BattleRuntimeAssembly.Fail(
                        BattleErrorCode.ConfigInvalid,
                        $"配置快照复制失败: {configEx.GetType().Name}",
                        scope);
                }

                // 步骤 7：校验配置快照（task 3.5 BattleConfigValidator）。
                Log.Info($"{LogTag} 步骤 6/12：校验配置快照（BattleConfigValidator）");
                BattleConfigValidationResult validationResult =
                    BattleConfigValidator.Validate(
                        configSnapshot, BattleRuntimeCapabilities.Production);
                if (!validationResult.IsValid)
                {
                    Log.Warning($"{LogTag} 配置校验失败 code={validationResult.ErrorCode} " +
                        $"errors={validationResult.Errors.Count} first={validationResult.Errors[0]}");
                    scope.Rollback();
                    return BattleRuntimeAssembly.Fail(
                        validationResult.ErrorCode,
                        validationResult.DiagnosticMessage,
                        scope);
                }

                if (loadout.OpponentMode == BattleOpponentMode.LocalAI)
                {
                    int difficulty = (int)loadout.OpponentAiDifficulty;
                    if (configSnapshot.OpponentAiProfiles == null
                        || !configSnapshot.OpponentAiProfiles.TryGet(
                            difficulty, out opponentAiProfile))
                    {
                        Log.Warning($"{LogTag} 对手 AI 难度配置缺失 difficulty={difficulty}");
                        scope.Rollback();
                        return BattleRuntimeAssembly.Fail(
                            BattleErrorCode.ConfigMissing,
                            $"对手 AI 难度配置缺失 difficulty={difficulty}",
                            scope);
                    }
                }

                bool hasSelectedBoss = false;
                foreach (WavePlanEntry row in configSnapshot.OrderedWavePlan.Rows)
                {
                    if (row.Kind == WavePlanKind.Boss)
                    {
                        hasSelectedBoss = true;
                        break;
                    }
                }

                buffManager = new BuffManager(configSnapshot.BuffCatalog, actionScheduler);
                scope.TrackDisposable(buffManager, "BuffManager");

                // 步骤 8：构造 BattleState / BattleReadModel / BattleResultBuilder（task 3.7/3.11 产物）。
                // task 6.10 闭环：这些状态/服务在构造 Manager 前先构造，因为 BattleManager 依赖
                // BattleResultBuilder，BattleResultBuilder 依赖 BattleReadModel，BattleReadModel 依赖
                // BattleState + RuntimeIdAllocator。
                // task 7.4 闭环：BattleReadModel 经 Assembly 暴露给 BattlePresenter，使 Presenter
                // 只读访问 BattleState 快照，不直接访问 BattleState 或 Manager。
                Log.Info($"{LogTag} 步骤 7/12：构造 BattleState / BattleReadModel / BattleResultBuilder");
                const int gridSize = EnemyManager.DefaultGridSize;
                const float cellSize = 80f;
                var idAllocator = new RuntimeIdAllocator();
                battleState = new BattleState();

                // 最终方案：构造等级数值服务与槽位面板（在 readModel 之前，供其注入）。
                // LevelService 无状态；SlotBoard 每局新建并初始化固定战场槽与待上场槽。
                // 修复 P0：待上场槽数量统一读取配置（RecruitManager 与 SlotBoard 共用同一值）。
                int reserveSlotCount = loadout.OpponentMode == BattleOpponentMode.LocalAI
                    && opponentAiProfile != null
                    && opponentAiProfile.HandSize > 0
                    ? opponentAiProfile.HandSize
                    : configSnapshot.Deck?.HandSize > 0
                    ? configSnapshot.Deck.HandSize
                    : RecruitDefinitions.ReserveSlotCount;
                levelService = new UnitLevelService(configSnapshot.UnitLevel);
                slotBoard = new UnitSlotBoard(levelService.MaxLevel, configSnapshot.GeneralCatalog);
                slotBoard.Initialize(configSnapshot.Map, reserveSlotCount);

                // 注意：readModel 同时被 ResultBuilder 与 Presenter 共享只读访问。
                // 两个消费者都只读不写，共享同一实例安全。
                readModel = new BattleReadModel(battleState, idAllocator, slotBoard);
                resultBuilder = new BattleResultBuilder(readModel);

                // 步骤 9：构造 EnemyManager / AttackResolver / AttackEffectManager / ProjectileManager
                // 及其依赖链（Phase 3/4 产物）。
                Log.Info($"{LogTag} 步骤 8/12：构造 EnemyManager / AttackEffectManager / ProjectileManager");
                enemyManager = new EnemyManager(gridSize, buffManager: buffManager);
                attackEffectManager = new AttackEffectManager();
                var attackResolver = new AttackResolver();
                BattleObjectPool<SimpleDynamicArrow> arrowPool =
                    effectivePoolScope.GetPool(() => new SimpleDynamicArrow());
                var projectileFactory = new ProjectileFactory(idAllocator, arrowPool, enemyManager, cellSize);
                projectileManager = new ProjectileManager(projectileFactory);

                // task 3.4/3.5：用 configSnapshot.EnemyCatalog + 本次池作用域构造类型化
                // EnemyFactory（封闭 key 注册表 + 四个独立类型池）。生产路径绝不再走
                // 旧链（EnemyFactory(RuntimeIdAllocator, BattleObjectPool<Mob0Enemy>)）。
                // 目录缺失时构造抛 ArgumentNullException，落入外层 catch 返回结构化失败。
                var enemyFactory = new EnemyFactory(idAllocator, configSnapshot.EnemyCatalog, effectivePoolScope);

                BossFactory bossFactory = null;
                if (hasSelectedBoss)
                {
                    if (!configSnapshot.EnemyCatalog.TryGetByTypeIndex(
                            configSnapshot.Map.EnemyTypeIndex, out EnemyDefinitionSnapshot bossBaseline))
                    {
                        throw new InvalidOperationException(
                            $"{LogTag} 无法按地图 EnemyTypeIndex={configSnapshot.Map.EnemyTypeIndex} 解析 Boss 基线敌人");
                    }

                    bossFactory = new BossFactory(
                        idAllocator, configSnapshot.BossCatalog, bossBaseline, effectivePoolScope);
                }

                // 敌军回收桥接：EnemyManager 统一移除时（Killed/ReachedEndPoint/Forced）
                // 经本回调按 ConfiguredEnemyBase/实际 key 归还到正确类型池，保证每次
                // Acquire 恰好对应一次 Release。禁止 (Mob0Enemy) 强转（四类型独立池）。
                enemyManager.ReleaseEnemy = enemy =>
                {
                    if (enemy is BossBase boss)
                    {
                        if (bossFactory == null || !bossFactory.Release(boss))
                        {
                            Log.Error($"{LogTag} Boss 回收失败 key='{boss.ResName}'");
                        }
                    }
                    else if (enemy is ConfiguredEnemyBase configured)
                    {
                        enemyFactory.Release(configured);
                    }
                };

                // 步骤 10：构造 UnitFactory / UnitRegistry / BattleEconomy / DeckManager /
                // PlacementReservationRegistry / BattleInputController / WaveManager / BattleManager /
                // AttackScheduler（Phase 2/3/5 产物）。
                Log.Info($"{LogTag} 步骤 9/12：构造 UnitFactory / UnitRegistry / Economy / Deck / Input / Wave / BattleManager");
                const int opponentAttackMultiplier = 1;
                BattleObjectPool<KnifeSoldier> knifePool =
                    effectivePoolScope.GetPool(() => new KnifeSoldier());
                BattleObjectPool<BowSoldier> bowPool =
                    effectivePoolScope.GetPool(() => new BowSoldier());
                BattleObjectPool<SpearSoldier> spearPool =
                    effectivePoolScope.GetPool(() => new SpearSoldier());
                BattleObjectPool<CavalrySoldier> cavalryPool =
                    effectivePoolScope.GetPool(() => new CavalrySoldier());

                randomStreams = new BattleRandomStreams(loadout.RandomSeed);
                commandIdAllocator = new BattleCommandIdAllocator();

                // 玩家仍使用兼容的四兵随机征兵；对手使用独立牌库 + 手牌，
                // 再由 RecruitManager 转换为 Reserve 投影。
                if (loadout.OpponentMode == BattleOpponentMode.LocalAI)
                {
                    opponentDeck = new OpponentDeckManager(
                        randomStreams.OpponentRecruit,
                        allowGeneralParts: opponentAiProfile.AllowGeneralParts,
                        allowFarmer: opponentAiProfile.AllowFarmer,
                        // The original 108-card pool always contains its shovel cards.
                        // DangerResponse controls the strategy, not the base-pool shape.
                        includeShovels: true,
                        // EnableValueEvaluation is an AI scoring switch. It must not
                        // alter the deck's copyGeneralChars/duplicate state. The deck
                        // owns that state; false is the explicit safe fallback until
                        // the raw copy event is wired in.
                        allowGeneralPartDuplicates: false);

                    // DeckManager.startGame resets the mutable pools before injecting
                    // extra shovels and drawing the initial hand. Keep that lifecycle
                    // explicit at the per-runtime boundary. Reset is non-random, so
                    // the independent OpponentRecruit stream remains unchanged.
                    opponentDeck.Reset();

                    // The raw injectShovel count is floor(playerShovelCount / 5), but
                    // this assembly currently has no safe account-day/player-shovel
                    // snapshot. Do not infer it from loadout.Round (combat wave !=
                    // player roundDay); use the documented zero-injection fallback.
                    const int defaultInjectedOpponentShovels = 0;
                    opponentDeck.InjectShovels(defaultInjectedOpponentShovels);
                    Log.Warning(
                        $"{LogTag} 对手牌库未获得玩家 roundDay/shovelCount 快照，" +
                        "按原始生命周期使用额外铲子注入默认值 0；待 BattleStartupContext 提供账户快照后接入 floor(count/5)");
                    opponentHand = new OpponentHand(opponentAiProfile.HandSize);
                    recruitManager = new RecruitManager(
                        randomStreams.PlayerRecruit,
                        randomStreams.OpponentRecruit,
                        slotBoard,
                        reserveSlotCount,
                        configSnapshot.GeneralCatalog.PartRecruitEntries,
                        includeInitialPlayerShovel: true,
                        opponentDeck,
                        opponentHand);
                }
                else
                {
                    recruitManager = new RecruitManager(
                        randomStreams.PlayerRecruit,
                        randomStreams.OpponentRecruit,
                        slotBoard,
                        reserveSlotCount,
                        configSnapshot.GeneralCatalog.PartRecruitEntries,
                        includeInitialPlayerShovel: true);
                }

                // 最终方案：开局免费生成第一批待上场单位，填满待上场槽。
                // 此后只有点击征兵扣馒头（扣费由 BattleInputController.ExecuteRecruit 完成）。
                // 修复 P0：开局免费填充失败时终止组装并回滚，不留下空局继续。
                bool initialFillOk =
                    slotBoard.ReplaceReserve(true, recruitManager.GenerateBatch(isPlayerSide: true))
                    && slotBoard.ReplaceReserve(false, recruitManager.GenerateBatch(isPlayerSide: false));
                if (!initialFillOk)
                {
                    Log.Error($"{LogTag} 开局免费填满待上场槽失败（批次数量与槽位数不一致），终止组装");
                    scope.Rollback();
                    return BattleRuntimeAssembly.Fail(
                        BattleErrorCode.ConfigInvalid,
                        "开局免费填满待上场槽失败：征兵批次数量与待上场槽数量不一致",
                        scope);
                }

                unitFactory = new UnitFactory(
                    idAllocator,
                    knifePool, bowPool, spearPool, cavalryPool,
                    enemyManager, attackResolver, attackEffectManager,
                    projectileFactory, projectileManager,
                    cellSize, opponentAttackMultiplier,
                    levelService);

                // 玩家默认武器（本 change 起）：从配置快照的 WeaponCatalog 构造
                // BasicWeaponResolver 并注入 UnitRegistry。生产 Luban Provider 恒构造
                // 目录（且步骤 7 校验已保证启用集合合法），因此生产路径一定装配真实
                // resolver；玩家目录缺失或不兼容默认 MUST 失败而非 fallback——
                // resolver 构造在启用集合非法时抛错，落入外层 catch 终止组装。
                // 旧兼容快照 WeaponCatalog 为 null 时不装配（保持旧测试行为）。
                BasicWeaponResolver weaponResolver = configSnapshot.WeaponCatalog != null
                    ? new BasicWeaponResolver(configSnapshot.WeaponCatalog, loadout.Weapons)
                    : null;

                unitRegistry = new UnitRegistry(unitFactory, cellSize, buffManager, weaponResolver);

                int refreshCostIncrement = configSnapshot.Economy.RefreshCostIncrement;
                battleEconomy = new BattleEconomy(battleState, refreshCostIncrement);

                reservationRegistry = new PlacementReservationRegistry();

                // ==================================================================
                // 武将主动技能 + 张梁 SoulCapture 共享 SkillRunner 装配
                // ------------------------------------------------------------------
                // 只创建一个 SkillHandlerRegistry 和一个 SkillRunner，供武将主动技能
                // 与张梁 Boss SoulCapture 共享。无武将技能且无张梁 Boss 时保持
                // skillRunner=null 的旧路径（AttackScheduler 也不注入 runtime）。
                //
                // 装配顺序：所有依赖（unitRegistry/enemyManager/attackResolver/buffManager/
                // projectileFactory/projectileManager/attackEffectManager/randomSource/cellSize）
                // 均已构造完成，此处在 AttackScheduler 之前一次性装配。
                //
                // SkillRunner 只 TrackDisposable 一次；GeneralSkillRuntime 由
                // UnitRegistry.ClearForSettling 清理，不额外重复处置。
                // ==================================================================
                bool hasGeneralSkill = false;
                if (configSnapshot.GeneralCatalog != null)
                {
                    IReadOnlyList<GeneralConfigSnapshot> generalDefinitions =
                        configSnapshot.GeneralCatalog.Definitions;
                    for (int i = 0; i < generalDefinitions.Count; i++)
                    {
                        if (generalDefinitions[i].SkillId.HasValue)
                        {
                            hasGeneralSkill = true;
                            break;
                        }
                    }
                }

                bool needSkillRunner = hasGeneralSkill || hasSelectedBoss;
                GeneralSkillRuntime generalSkillRuntime = null;
                if (needSkillRunner)
                {
                    var skillHandlers = new SkillHandlerRegistry();

                    // 张梁 SoulCapture 注册（Boss 选中时），保持原有行为，避免重复注册。
                    if (hasSelectedBoss)
                    {
                        WavePlanEntry bossRow = null;
                        foreach (WavePlanEntry row in configSnapshot.OrderedWavePlan.Rows)
                        {
                            if (row.Kind == WavePlanKind.Boss) { bossRow = row; break; }
                        }
                        if (bossRow == null || !bossRow.BossId.HasValue
                            || !configSnapshot.BossCatalog.TryGetById(bossRow.BossId.Value, out BossDefinitionSnapshot bossDefinition)
                            || !configSnapshot.SkillCatalog.TryGetById(
                                bossDefinition.SkillId, out SkillDefinitionSnapshot soulCaptureDefinition))
                        {
                            throw new InvalidOperationException($"{LogTag} 缺少 SoulCapture 技能定义");
                        }

                        skillHandlers.Register(
                            "SoulCapture",
                            new SoulCaptureHandler(
                                soulCaptureDefinition, enemyManager, unitRegistry, buffManager, cellSize));
                    }

                    // 武将主动技能 handler 注册：被武将 SkillKey 引用时注册对应 handler。
                    // 缺少被引用的技能定义时抛清晰 InvalidOperationException，不引入 fallback。
                    if (hasGeneralSkill && configSnapshot.GeneralCatalog != null)
                    {
                        IReadOnlyList<GeneralConfigSnapshot> generalDefinitions =
                            configSnapshot.GeneralCatalog.Definitions;
                        bool battleShoutRegistered = false;
                        bool fireArrowBarrageRegistered = false;
                        for (int i = 0; i < generalDefinitions.Count; i++)
                        {
                            int? skillId = generalDefinitions[i].SkillId;
                            if (!skillId.HasValue)
                            {
                                continue;
                            }

                            if (!configSnapshot.SkillCatalog.TryGetById(
                                    skillId.Value, out SkillDefinitionSnapshot generalSkillDefinition))
                            {
                                throw new InvalidOperationException(
                                    $"{LogTag} 武将技能定义缺失：SkillId={skillId.Value} 在 SkillCatalog 中不存在");
                            }

                            // 决策：switch/注册键统一使用 generalSkillDefinition.HandlerKey
                            // （与 SkillRunner 查询键一致），不使用武将引用的 SkillKey。
                            // 未实现的 HandlerKey 立即抛清晰异常，同时包含 SkillKey 与
                            // HandlerKey，不静默跳过到 ActivateBattleUnit 才失败。
                            string generalHandlerKey = generalSkillDefinition.HandlerKey;
                            switch (generalHandlerKey)
                            {
                                case "BattleShout":
                                    if (!battleShoutRegistered)
                                    {
                                        skillHandlers.Register(
                                            generalHandlerKey,
                                            new BattleShoutHandler(
                                                generalSkillDefinition,
                                                unitRegistry,
                                                enemyManager,
                                                attackResolver,
                                                buffManager,
                                                cellSize));
                                        battleShoutRegistered = true;
                                    }
                                    break;
                                case "FireArrowBarrage":
                                    if (!fireArrowBarrageRegistered)
                                    {
                                        skillHandlers.Register(
                                            generalHandlerKey,
                                            new FireArrowBarrageHandler(
                                                generalSkillDefinition,
                                                unitRegistry,
                                                enemyManager,
                                                attackResolver,
                                                projectileFactory,
                                                projectileManager,
                                                attackEffectManager,
                                                randomStreams.CombatAndSkills,
                                                cellSize));
                                        fireArrowBarrageRegistered = true;
                                    }
                                    break;
                                default:
                                    throw new InvalidOperationException(
                                        $"{LogTag} 未实现的武将技能 HandlerKey：" +
                                        $"SkillId={skillId.Value}, HandlerKey='{generalHandlerKey}'");
                            }
                        }
                    }

                    skillRunner = new SkillRunner(
                        configSnapshot.SkillCatalog, skillHandlers, actionScheduler);
                    scope.TrackDisposable(skillRunner, "SkillRunner");

                    // 有武将 SkillKey 时创建 GeneralSkillRuntime 并装配到 UnitRegistry，
                    // AttackScheduler 构造时注入使其能在攻击槽替换为技能激活。
                    if (hasGeneralSkill)
                    {
                        generalSkillRuntime = new GeneralSkillRuntime(
                            skillRunner, configSnapshot.SkillCatalog);
                        unitRegistry.AssembleGeneralSkillRuntime(generalSkillRuntime);
                    }
                }

                inputController = new BattleInputController(
                    slotBoard,
                    recruitManager,
                    levelService,
                    battleEconomy,
                    unitRegistry,
                    configSnapshot,
                    signalHub);

                // WaveManager 新生产链（task 4.6）：直接消费 OrderedWavePlanSnapshot +
                // Normal spawn handler + 生产默认不可用 Boss 端口，构造契约固定为
                // (OrderedWavePlanSnapshot, NormalWaveSpawnHandler, IBossWavePort)。
                //
                // normalSpawnHandler：把 WaveManager 解析出的 NormalWaveSpawnRequest 翻译为
                // EnemySpawnRequest（补齐地图/终点/回调）后经 EnemyFactory.Acquire → Register
                // 走新生产链，登记失败归还正确池；成功返回 WaveEntityHandle.FromEnemyLease。
                // playerTarget/opponentTarget 在 BattleManager 之后绑定，handler 通过闭包捕获
                // 变量，实际出生发生在模拟 phase（此时两个 target 均已绑定）。
                BattleTarget playerTarget = null;
                BattleTarget opponentTarget = null;
                NormalWaveSpawnHandler normalSpawnHandler = request =>
                {
                    // 从目录确认定义（未知 key 显式失败，禁止占位）。
                    if (!configSnapshot.EnemyCatalog.TryGetById(
                            request.EnemyId, out EnemyDefinitionSnapshot definition))
                    {
                        throw new InvalidOperationException(
                            $"{LogTag} 波次出生未知敌人 id={request.EnemyId}（order={request.WaveOrder}）");
                    }

                    // 选择正确 lane endpoint：玩家路 → 玩家方目标，电脑路 → 对手方目标。
                    IEnemyEndPointAttackTarget endPointTarget =
                        request.IsPlayerLane ? playerTarget : opponentTarget;

                    var spawnRequest = new EnemySpawnRequest(
                        enemyId: request.EnemyId,
                        isPlayerLane: request.IsPlayerLane,
                        waveOrder: request.WaveOrder,
                        difficultyIndex: request.DifficultyIndex,
                        strategyProfile: request.StrategyProfile,
                        map: configSnapshot.Map,
                        cellSize: cellSize,
                        endPointTarget: endPointTarget,
                        onEnemyKilled: (killedId, attackerId, reward, lane) =>
                        {
                            // 击杀奖励仍由 EnemyBase 在死亡点提交（KillRewardValue），本 handler 不重复发奖。
                        },
                        onDeathRequested: (killedId, reason) => enemyManager.RequestRemoveEnemy(killedId, reason),
                        width: 40f,
                        height: 40f);

                    // 新生产链单点契约（decision 3）：acquire → ConfiguredInit → Register；
                    // 初始化或登记失败都归还本次租借到正确池后重新抛出。
                    ConfiguredEnemyBase enemy = enemyFactory.Acquire(spawnRequest);
                    try
                    {
                        enemyManager.Register(enemy);
                    }
                    catch
                    {
                        enemyFactory.Release(enemy);
                        throw;
                    }

                    return WaveEntityHandle.FromEnemyLease(enemy.CurrentLease);
                };

                IBossWavePort bossWavePort = UnavailableBossWavePort.Instance;
                if (hasSelectedBoss)
                {
                    bossWavePort = new ZhangLiangBossWavePort(
                        configSnapshot.BossCatalog,
                        configSnapshot.SkillCatalog,
                        bossFactory,
                        enemyManager,
                        skillRunner,
                        configSnapshot.Map,
                        cellSize,
                        isPlayerLane => isPlayerLane ? playerTarget : opponentTarget,
                        (rewardGold, isPlayerLane) =>
                        {
                            battleEconomy.Award(isPlayerLane, rewardGold, "kill");
                            battleState.ApplyEnemyKill();
                            battleState.ApplyBossKill();
                        });
                }

                waveManager = new WaveManager(
                    configSnapshot.OrderedWavePlan, normalSpawnHandler, bossWavePort);

                // 任务 5.2：EnemyManager 的波次所有权移除事实接到 waveManager.OnEntityRemoved(handle)。
                // reason（Killed/ReachedEndPoint/Forced）不改变波次计数；handle 携带完整
                // runtimeId/generation/waveOrder/kind（普通敌人=Normal、Boss=Boss），
                // WaveManager 以完整值幂等匹配，无有效 waveOrder 自然不命中。
                // 不得轮询 EnemyManager.Count。订阅接线放在 Factory（Enemy 实现/Manager 语义不修改）。
                enemyManager.WaveEntityRemoved += (handle, _) => waveManager.OnEntityRemoved(handle);

                // BattleManager 依赖 configSnapshot / battleState / waveManager / battleEconomy / resultBuilder。
                // 构造时订阅 waveManager.WaveStarted（CurrentRound 同步）与
                // AllConfiguredWavesCompleted（唯一成功闸 → TryFreeze(true)）。
                battleManager = new BattleManager(
                    configSnapshot, battleState, waveManager, battleEconomy, resultBuilder);

                if (loadout.OpponentMode == BattleOpponentMode.LocalAI)
                {
                    opponentAI = new OpponentAI(
                        opponentAiProfile,
                        slotBoard,
                        inputController,
                        commandIdAllocator,
                        battleEconomy,
                        waveManager,
                        configSnapshot.Map,
                        randomStreams.OpponentStrategy,
                        levelService,
                        opponentHand,
                        configSnapshot.GeneralCatalog,
                        opponentAiReplayLog);
                }

                // task 6.10 闭环返工：构造 BattleTarget 并绑定到 BattleState/BattleManager/ResultBuilder，
                // 使敌人抵达终点时能通过 IEnemyEndPointAttackTarget.ReceiveEndPointAttack →
                // BattleTarget.ApplyDamage → BattleState.ApplyDamage → BattleManager.CheckHealthFreeze
                // → ResultBuilder.TryFreeze 完成实际战斗结算（而非 maxRounds 超时触发冻结）。
                // 玩家方目标（isPlayerLaneTarget=true）受击 → CheckHealthFreeze(true) → 玩家败；
                // 对手方目标（isPlayerLaneTarget=false）受击 → CheckHealthFreeze(false) → 只保留状态，
                // 不直接成功（成功必须等 AllConfiguredWavesCompleted）。
                playerTarget = new BattleTarget();
                playerTarget.Bind(
                    battleState,
                    battleManager,
                    resultBuilder,
                    isPlayerLaneTarget: true,
                    signalHub: signalHub);
                opponentTarget = new BattleTarget();
                opponentTarget.Bind(
                    battleState,
                    battleManager,
                    resultBuilder,
                    isPlayerLaneTarget: false,
                    signalHub: signalHub);

                // AttackScheduler 依赖 actionScheduler / attackResolver / cellWidth / cellHeight。
                // 有武将主动技能时注入 GeneralSkillRuntime，使技能成功时替换攻击槽；
                // 无武将技能时 generalSkillRuntime 为 null，保持旧测试行为。
                attackScheduler = new AttackScheduler(
                    actionScheduler, attackResolver, cellSize, cellSize, generalSkillRuntime);

                // 步骤 11：构造 BattleSimulation，phaseHandlers 连接到实际 Manager 的 update 方法，
                // tryFreeze 回调连接到 BattleResultBuilder.TryFreeze（task 6.10 闭环核心接入）。
                // phaseHandlers 按 BattleUpdatePhase 顺序注册：
                //   DueActionsAndInput → ActionScheduler.FlushDueActions（到期动作）
                //   Enemy → EnemyManager.Update
                //   Projectile → ProjectileManager.Update
                //   AttackRelease → 空操作（攻击释放由单位攻击调度内的效果/投射物创建同步完成）
                //   WaveSpawn → waveManager.Update（有序波次状态机唯一时间推进入口，
                //     任务 4.6：不再调用 BattleManager 旧 UpdateSpawnState）
                //   UnitAttack → AttackScheduler.Update（遍历 UnitRegistry 活动单位）
                //   AttackEffect → AttackEffectManager.Update
                Log.Info($"{LogTag} 步骤 10/12：构造 BattleSimulation（phaseHandlers 接入实际 Manager）");
                int phaseCount = Enum.GetValues(typeof(BattleUpdatePhase)).Length;
                Action<long, long, BattleUpdatePhase>[] phaseHandlers =
                    new Action<long, long, BattleUpdatePhase>[phaseCount];

                // DueActionsAndInput：处理本帧已到期的规则回调（接触伤害、刀兵命中、攻击释放等）。
                phaseHandlers[(int)BattleUpdatePhase.DueActionsAndInput] =
                    (frameNow, step, phase) => actionScheduler.FlushDueActions(step);

                // Enemy：敌人沿路径移动、接触目标并同步维护空间事实。
                phaseHandlers[(int)BattleUpdatePhase.Enemy] =
                    (frameNow, step, phase) => enemyManager.Update(step);

                // Projectile：推进已存在的投射物移动、命中判定与逆序移除。
                phaseHandlers[(int)BattleUpdatePhase.Projectile] =
                    (frameNow, step, phase) => projectileManager.Update(frameNow, step);

                // AttackRelease：攻击释放时序阶段。本期攻击释放由单位攻击调度内的
                // 效果/投射物创建同步完成，此阶段为空操作占位（保持阶段顺序一致）。
                phaseHandlers[(int)BattleUpdatePhase.AttackRelease] =
                    (frameNow, step, phase) => { };

                // WaveSpawn：波次/生成阶段。WaveManager 有序波次状态机是逐波唯一 owner，
                // 只在本 phase 经 Update(stepMs) 推进（任务 4.6）。保持七 phase 顺序与唯一时间源不变。
                phaseHandlers[(int)BattleUpdatePhase.WaveSpawn] =
                    (frameNow, step, phase) => waveManager.Update(step);

                // UnitAttack：单位攻击调度，每子步只推进一次单位冷却、选取目标并触发一次攻击。
                phaseHandlers[(int)BattleUpdatePhase.UnitAttack] =
                    (frameNow, step, phase) =>
                    {
                        // 原 GameLoop 注册顺序映射：BattleManager/Wave 先于 AI，AI 新放置的
                        // 单位随后可在同一子步进入单位攻击调度。
                        opponentAI?.Update(step);

                        // 获取活动单位只读列表（稳定有序，按放置顺序）。
                        // AttackScheduler.Update 内部有冻结守卫，冻结后不调度。
                        IReadOnlyList<SoldierBase> units = unitRegistry.GetActiveUnits();
                        attackScheduler.Update(units, enemyManager);
                    };

                // AttackEffect：推进近战/范围攻击效果累计。
                phaseHandlers[(int)BattleUpdatePhase.AttackEffect] =
                    (frameNow, step, phase) => attackEffectManager.Update(step);

                // TryFreeze 回调连接到 BattleResultBuilder.TryFreeze。
                // 注意：BattleManager.TryFreezeResult(playerWin) 内部调用 resultBuilder.TryFreeze，
                // 但 Simulation 的 _tryFreezeHandler 是无参 bool 回调，用于检查"是否已冻结"
                // 并在检查点中止。此处提供 () => resultBuilder.IsFrozen 作为冻结检查：
                // BattleManager 在完成事实发生点调用 TryFreezeResult → resultBuilder.TryFreeze，
                // 成功后 resultBuilder.IsFrozen 为 true，Simulation 在检查点检测到并中止。
                // 但 Simulation.TryFreeze() 方法会调用 _tryFreezeHandler() 并据返回值置位
                // IsFrozen + Freeze ActionScheduler。为了正确驱动，_tryFreezeHandler 应返回
                // resultBuilder.IsFrozen（已被 BattleManager 在完成事实点设置）。
                Func<bool> tryFreezeHandler = () => resultBuilder.IsFrozen;

                simulation = new BattleSimulation(phaseHandlers, tryFreezeHandler, actionScheduler);

                // task 7.3 返工：注入表现端口。
                // task 7.6 接入：使用 Unity 真实实现（UnityBattleViewPort / UnityBattleAudioPort /
                // UnityBattleVfxPort），使生产环境具备 Unity 表现层能力。
                // 纯逻辑 EditMode 测试通过注入 Null 实现委托（通过 BattleModule 的可注入构造）
                // 或测试专用入口验证；本 Factory 使用 Unity 真实实现作为生产默认值。
                // 真实端口在 PreloadAsync 中持有 AssetHandle，并随 Presenter.Dispose 的
                // Clear 路径统一释放，避免模块 Scope 与端口双重 Release。
                // IBattleViewPort 的 FairyGUI 真实实现因 GameBattle asmdef 未引用 FairyGUI 程序集，
                // 推迟到 task 7.5（FairyGUI Change 冻结公共注册契约后接入）。
                // 当前 UnityBattleViewPort 基于 UnityEngine GameObject，不依赖 FairyGUI。
                if (bindings != null)
                {
                    viewPort = new UnityBattleViewPort(bindings);
                    audioPort = new UnityBattleAudioPort();
                    vfxPort = new UnityBattleVfxPort(bindings);
                }
                else
                {
                    viewPort = new NullBattleViewPort();
                    audioPort = new NullBattleAudioPort();
                    vfxPort = new NullBattleVfxPort();
                }

                // task 7.4 闭环：构造表现层组装器 BattlePresenter。
                // task 7.6 接入：传入 enemyManager/unitRegistry/projectileManager，
                // 使 Presenter 内部的 Synchronizer 能查询真实逻辑位置。
                // Presenter 依赖：readModel（只读状态视图）+ viewPort/audioPort/vfxPort（三个端口）+
                // inputController（供 BattleInputAdapter 提交命令）+
                // enemyManager/unitRegistry/projectileManager（供 RuntimeReadModelProvider 查询位置）。
                // Presenter 只读 readModel，把只读状态/事实翻译成视图操作，不回写规则状态
                // （design.md:217）。
                // 三个端口当前为 Unity 真实实现（task 7.6），使生产环境具备 Unity 表现层能力。
                // Presenter 经 Scope.TrackDisposable 登记释放，在失败回滚/Settling/Dispose 时
                // 调用 Presenter.Dispose 清理表现对象与监听。
                Log.Info($"{LogTag} 步骤 11/12：构造 BattlePresenter（task 7.4/7.6 产物）");
                presenter = new BattlePresenter(
                    readModel,
                    viewPort,
                    audioPort,
                    vfxPort,
                    inputController,
                    enemyManager,
                    unitRegistry,
                    projectileManager,
                    bindings,
                    signalHub,
                    commandIdAllocator);
                scope.TrackDisposable(presenter, "BattlePresenter");

                // 组装成功：记录完成日志，返回成功产物。
                Log.Info($"{LogTag} 步骤 12/12：组装成功，返回 Assembly（BattleRuntime 将接管所有权）");
                return BattleRuntimeAssembly.Ok(
                    scope,
                    simulation,
                    loadout,
                    configSnapshot,
                    buffManager,
                    skillRunner,
                    attackEffectManager,
                    projectileManager,
                    unitFactory,
                    unitRegistry,
                    inputController,
                    opponentAI,
                    battleState,
                    battleManager,
                    waveManager,
                    battleEconomy,
                    reservationRegistry,
                    enemyManager,
                    attackScheduler,
                    resultBuilder,
                    signalHub,
                    eventBridge,
                    viewPort,
                    audioPort,
                    vfxPort,
                    readModel,
                    presenter,
                    slotBoard,
                    recruitManager,
                    levelService);
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
        /// <para>内部可见：<see cref="BattleStartupContext.Prepare"/> 在配置准备阶段
        /// 复用本校验，保证在世界加载前拒绝非法 loadout（design.md 决策 3）。</para>
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
        internal static bool TryValidateLoadout(
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

            if (loadout.OpponentMode != BattleOpponentMode.None
                && loadout.OpponentMode != BattleOpponentMode.LocalAI)
            {
                errorCode = BattleErrorCode.ConfigInvalid;
                diagnosticMessage = $"不支持的对手模式 mode={loadout.OpponentMode}";
                return false;
            }

            if (loadout.Weapons.BowWeaponId <= 0
                || loadout.Weapons.SpearWeaponId <= 0
                || loadout.Weapons.KnifeWeaponId <= 0
                || loadout.Weapons.SwordWeaponId <= 0)
            {
                errorCode = BattleErrorCode.ConfigInvalid;
                diagnosticMessage = "四个武器槽位都必须提供有效的正整数武器 id";
                return false;
            }

            int aiDifficulty = (int)loadout.OpponentAiDifficulty;
            if (aiDifficulty < 0 || aiDifficulty > 3)
            {
                errorCode = BattleErrorCode.ConfigInvalid;
                diagnosticMessage = $"对手 AI 难度越界 difficulty={aiDifficulty}";
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
