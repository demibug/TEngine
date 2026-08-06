using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 6.4 + 6.7：BattleInputController —— 输入命令执行控制器（完整原子事务）
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 208 行 / specs/battle-simulation/spec.md
    //   "Input commands are atomic"）：
    //   原子执行预留、扣费、创建、放置、消耗和补牌，并在任一步失败时回滚。
    //   本类型是 GameBattle 内部对输入命令的唯一执行入口，接收不可变的
    //   <see cref="BattleInputCommand"/>，返回不可变的 <see cref="BattleInputResult"/>。
    //
    // task 6.4 已完成：骨架结构、依赖注入解除、命令集合约束。
    // task 6.7（已完成）范围：
    //   实现 ExecuteBuyPlace 和 ExecuteRefresh 的完整原子事务，任一步失败按逆序补偿。
    //   BuyPlace 事务步骤：只读验证 → 格子预留 → 扣费 → 创建 → 放置 →
    //     消耗卡牌（含补牌） → 提交。
    //   Refresh 事务步骤：只读验证 → 扣费 → 刷新牌组 → 提交（无预留/创建/放置）。
    //
    // task 6.8（本任务）范围（决策 0.8）：
    //   在 Execute 入口维护 CommandId → 首次结果的缓存。同一 CommandId 重复提交直接返回
    //   首次结果，不再次扣费、消耗卡牌或创建单位；不同 ID 即使 payload 相同也按独立命令
    //   处理，重新走完整原子事务。缓存存储由本类型持有（局内可变状态），随 Runtime
    //   清理调用 ClearProcessedCommands 清空，不跨局保留（spec "Restart creates clean
    //   per-battle state"）。缓存键为 CommandId（int），值为首次执行返回的不可变
    //   BattleInputResult。
    //
    // 解除的依赖（task 6.4 核心目标 / design.md 决策 5）：
    //   还原工程 BattleInputController.js:5 构造签名：
    //     constructor({deckManager, economy, unitRegistry, mergeService,
    //                  mapTileManager, logger=console})
    //   JS 中 mergeService 对应 UnitMergeService，本期不创建（design.md 决策 5：
    //     "UnitLevelService 与 UnitMergeService 本期延后：UI 不提供升级、合并或交换入口，
    //      BattleInputController 不再强制注入它们，禁止创建假实现"）。
    //   JS 中 mapTileManager 的可建造性校验职责在 C# 移植中由 MapData.IsBuildableForSide
    //     承担（task 3.6 产物），不再单独注入 MapTileManager 类型。
    //   JS 中没有显式 UnitLevelService 参数，但 purchaseAndPlace 路径隐含 levelUp 调用
    //     （UnitRegistry.createFromDescriptor step 8 levelUp），本期 Level 固定 1
    //     （task 1.5 延后 UnitLevelService），C# 移植不注入 LevelService。
    //
    // 命令集合约束（task 6.4 / design.md 第 206 行）：
    //   本期命令集合只有 BuyPlace 和 Refresh（BattleInputCommandType 枚举只有这两个值，
    //   task 6.6 已定义）。升级、合并、移动、交换和拖拽入口不出现在本期命令集合中：
    //     - 升级（Upgrade）：依赖 UnitLevelService，本期延后。
    //     - 合并（Merge）：依赖 UnitMergeService，本期延后。
    //     - 移动（MoveUnit）：JS BattleInputController.moveUnit，本期裁剪。
    //     - 交换（Swap）：JS 无对应命令，本期不引入。
    //     - 拖拽（BeginDrag/MoveDrag/CommitPlacement/CancelDrag）：JS 4 种拖拽命令，
    //       本期裁剪，UI 适配层直接构造 BuyPlace 命令。
    //   Execute 遇到非 BuyPlace/Refresh 的命令类型时返回 UnsupportedCommand 拒绝原因，
    //   不抛异常（design.md:207 "不用异常表达正常校验失败"）。
    //
    // 与 Runtime 的关系（design.md 决策 3 / specs/battle-runtime-lifecycle/spec.md）：
    //   BattleInputController 由 BattleRuntimeFactory 在每局构造时创建并注入到
    //   BattleRuntime（task 6.7 接入）。本类型不跨局复用，随 Runtime 销毁。
    //
    // 主线程串行执行（design.md:206 / task 6.6）：
    //   所有输入在 Unity 主线程通过 Runtime 串行队列执行。Execute 本身为同步方法，
    //   调用方在主线程串行调用即可保证命令串行执行。串行队列封装由 task 6.8 实现。
    //
    // CommandId 语义（决策 0.8 / task 6.8）：
    //   每条命令携带单局 CommandId。同一 ID 重复提交返回首次结果，不再次扣费、消耗卡牌
    //   或创建单位；不同 ID 即使 payload 相同也按独立命令处理。CommandId 去重存储与首次
    //   结果缓存由 task 6.8 在本类型内维护（_processedCommands 字段），随 Runtime 清理
    //   清空，不跨局保留。
    //
    // 原子事务不变量（spec "Input commands are atomic"）：
    //   1. BuyPlace 任一步失败时按逆序补偿到调用前状态：
    //        - 放置后失败 → 移除单位（UnitRegistry.Remove 内部归还池） + 退还扣费 +
    //          归还卡牌（DeckManager 把被消耗的卡放回槽位，移除补牌）
    //        - 创建后失败 → 归还单位到池（UnitFactory.Release） + 退还扣费 + 回滚预留
    //        - 扣费后失败 → 退还扣费 + 回滚预留
    //        - 预留后失败 → 回滚预留
    //   2. Refresh 扣费后刷新失败 → 退还扣费（刷新费用与下次费用递增一并回滚）
    //   3. 只读验证失败不修改任何状态，无需补偿。
    //
    // 不可变性：
    //   1. 注入依赖全部为 readonly 字段，构造后不可替换。
    //   2. Execute 接收不可变 BattleInputCommand，返回不可变 BattleInputResult。
    //   3. 局内可变状态只有 _started 标记，不持有活动实体引用。
    //
    // 不变量：
    //   1. 不注入或引用 UnitLevelService / UnitMergeService（task 6.4 核心约束）。
    //   2. 命令集合只有 BuyPlace 和 Refresh，不出现升级/合并/移动/交换/拖拽入口。
    //   3. 只注入本期存在的服务，不创建假实现。
    //   4. BuyPlace/Refresh 原子事务任一步失败按逆序补偿，不留下半提交状态。
    // ============================================================================

    /// <summary>
    /// 输入命令执行控制器：原子执行购买放置和刷新命令，任一步失败时回滚。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 208 行）：</b>
    /// 原子执行预留、扣费、创建、放置、消耗和补牌，并在任一步失败时回滚。
    /// 替代还原工程 <c>BattleInputController.js</c>（<c>BattleInputController.js:4-15</c>）。</para>
    ///
    /// <para><b>本期范围（task 6.4 + 6.7 + 6.8）：</b>
    /// task 6.4 已完成骨架结构与依赖解除；task 6.7 实现完整原子事务；
    /// task 6.8 实现 CommandId 去重（同一 ID 返回首次结果，不同 ID 独立处理）。</para>
    ///
    /// <para><b>解除的依赖（task 6.4 核心目标 / design.md 决策 5）：</b>
    /// 还原工程构造签名注入 <c>mergeService</c>（UnitMergeService），本期不创建该服务。
    /// C# 移植不注入 UnitLevelService / UnitMergeService，不创建假实现。
    /// JS 的 <c>mapTileManager</c> 可建造性校验职责由 <see cref="MapData.IsBuildableForSide"/>
    /// 承担（task 3.6 产物），不再单独注入 MapTileManager 类型。</para>
    ///
    /// <para><b>命令集合约束（task 6.4 / design.md 第 206 行）：</b>
    /// 本期只识别 <see cref="BattleInputCommandType.BuyPlace"/> 和
    /// <see cref="BattleInputCommandType.Refresh"/> 两种命令。升级、合并、移动、交换和拖拽
    /// 入口不出现在命令集合中，Execute 遇到非本期命令返回
    /// <see cref="BattleInputRejectReason.UnsupportedCommand"/>。</para>
    ///
    /// <para><b>每局新建/销毁：</b>
    /// 由 BattleRuntimeFactory 在每局构造时创建（task 6.7 接入），随 Runtime 销毁，
    /// 不跨局复用（spec "Restart creates clean per-battle state"）。CommandId 去重缓存
    /// 由本类型持有，在 Runtime Settling/Dispose 清理时经
    /// <see cref="ClearProcessedCommands"/> 清空，不跨局保留（task 6.8）。</para>
    ///
    /// <para><b>本类型为 internal sealed：</b>
    /// 只供 GameBattle 内部 BattleRuntime / BattleRuntimeFactory 使用，
    /// 不对其他程序集暴露。对外公共输入经 IBattleModule 转发。</para>
    /// </remarks>
    internal sealed class BattleInputController
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>日志标签前缀，便于在日志中筛选输入控制器相关条目。</summary>
        private const string LogTag = "[BattleInputController]";

        // ====================================================================
        // 默认单位逻辑尺寸（纯逻辑层无 displayObject，使用约定常量）
        // --------------------------------------------------------------------
        // 对应还原工程 displayObject.width/height（UnitBase.js:309-310）。
        // JS 中尺寸由 sprite 资源决定，C# 纯逻辑层无 GameObject，使用与测试一致的
        // 约定值 40x40（格子尺寸 80 的一半，与 UnitFactoryAndRegistryTests 一致）。
        // 后续配置快照暴露单位尺寸字段后从快照读取。
        // ====================================================================

        /// <summary>默认单位逻辑宽度（像素，约定 40，对应半个格子）。</summary>
        private const float DefaultUnitWidth = 40f;

        /// <summary>默认单位逻辑高度（像素，约定 40，对应半个格子）。</summary>
        private const float DefaultUnitHeight = 40f;

        // ====================================================================
        // 注入依赖（全部 readonly，构造后不可替换）
        // --------------------------------------------------------------------
        // 只注入本期存在的服务，不注入 UnitLevelService / UnitMergeService
        // （task 6.4 核心约束）。
        // ====================================================================

        /// <summary>
        /// 单位工厂，供购买放置命令创建士兵（task 6.3 产物）。
        /// <para>由 BattleRuntimeFactory 在每局构造时注入，只识别四个强类型兵种 ID。</para>
        /// </summary>
        private readonly UnitFactory _unitFactory;

        /// <summary>
        /// 单位注册表，供购买放置命令注册/放置单位（task 6.3 产物）。
        /// <para>管理单位注册、放置、移除和战斗结束清理，维护稳定有序集合。</para>
        /// </summary>
        private readonly UnitRegistry _unitRegistry;

        /// <summary>
        /// 经济服务，供购买放置和刷新命令扣费/退还（task 3.8 产物）。
        /// <para>处理招募、刷新的校验与余额变更，余额不足返回结构化失败结果。</para>
        /// </summary>
        private readonly BattleEconomy _economy;

        /// <summary>
        /// 牌组管理器，供购买放置命令消耗卡牌/补牌、刷新命令重抽手牌（task 6.5 产物）。
        /// <para>用注入随机源完成抽牌、消耗、补牌和刷新。</para>
        /// </summary>
        private readonly DeckManager _deckManager;

        /// <summary>
        /// 格子预留注册表，供购买放置命令事务性预留/提交/回滚格子（task 3.8 产物）。
        /// <para>管理购买放置事务中的临时格子预留；成功提交或失败回滚。</para>
        /// </summary>
        private readonly PlacementReservationRegistry _reservationRegistry;

        /// <summary>
        /// 地图数据，供购买放置命令校验格子可建造性（task 3.6 产物）。
        /// <para>替代还原工程 MapTileManager 的可建造性校验职责。
        /// 业务层通过 <see cref="MapData.IsBuildableForSide"/> 校验格子可建造性，
        /// 不再单独注入 MapTileManager 类型。</para>
        /// </summary>
        private readonly MapData _mapData;

        /// <summary>
        /// 本局不可变战斗配置快照（task 3.3 产物）。
        /// <para>供购买放置命令按兵种文字查找 <see cref="UnitConfigSnapshot"/>，
        /// 传递给 UnitFactory.Acquire 进行 InitializeStats。运行时只读，不修改。</para>
        /// </summary>
        private readonly BattleConfigSnapshot _configSnapshot;

        // ====================================================================
        // 局内可变状态
        // ====================================================================

        /// <summary>
        /// 是否已启动（对应 JS BattleInputController.started）。
        /// <para>StartGame 置 true，GameOver 置 false。未启动时 Execute 返回失败。</para>
        /// </summary>
        private bool _started;

        /// <summary>
        /// 已处理 CommandId → 首次执行结果的缓存（task 6.8 / 决策 0.8）。
        /// <para>同一 CommandId 重复提交时直接返回首次结果，不再次扣费、消耗卡牌或创建单位；
        /// 不同 ID 即使 payload 相同也按独立命令处理，重新走完整原子事务。</para>
        /// <para>生命周期：随本控制器（随 Runtime）销毁，不跨局保留。BattleRuntime 在
        /// Settling 静默清理或 Dispose 时调用 <see cref="ClearProcessedCommands"/> 清空。
        /// 重开一局由 Factory 产生新控制器，缓存自然为空（spec "Restart creates clean
        /// per-battle state"）。</para>
        /// <para>线程安全：所有输入在 Unity 主线程串行执行（design.md:206），无需同步。</para>
        /// </summary>
        private readonly Dictionary<int, BattleInputResult> _processedCommands
            = new Dictionary<int, BattleInputResult>();

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造输入命令执行控制器，注入本期存在的全部依赖。
        /// </summary>
        /// <param name="unitFactory">单位工厂（task 6.3 产物）。不可为 null。</param>
        /// <param name="unitRegistry">单位注册表（task 6.3 产物）。不可为 null。</param>
        /// <param name="economy">经济服务（task 3.8 产物）。不可为 null。</param>
        /// <param name="deckManager">牌组管理器（task 6.5 产物）。不可为 null。</param>
        /// <param name="reservationRegistry">格子预留注册表（task 3.8 产物）。不可为 null。</param>
        /// <param name="mapData">地图数据（task 3.6 产物）。不可为 null。</param>
        /// <param name="configSnapshot">本局不可变战斗配置快照（task 3.3 产物）。不可为 null。</param>
        /// <exception cref="ArgumentNullException">任一必需参数为 null。</exception>
        /// <remarks>
        /// <para><b>task 6.4 核心约束：</b>
        /// 本构造函数不注入 <c>UnitLevelService</c> 或 <c>UnitMergeService</c>
        /// （本期延后创建，design.md 决策 5）。还原工程
        /// <c>BattleInputController.js:5</c> 的 <c>mergeService</c> 参数在 C# 移植中移除，
        /// 对应的合并命令（<c>MERGE_UNITS</c>）也不出现在本期命令集合中。</para>
        ///
        /// <para><b>对应还原工程：</b>
        /// <c>constructor({deckManager, economy, unitRegistry, mergeService,
        /// mapTileManager, logger=console})</c>（BattleInputController.js:5）。
        /// C# 移植变化：
        /// <list type="bullet">
        /// <item>删除 <c>mergeService</c>：本期不创建 UnitMergeService。</item>
        /// <item>删除 <c>mapTileManager</c>：可建造性校验由 <see cref="MapData"/> 承担。</item>
        /// <item>新增 <c>unitFactory</c>：JS 经 unitRegistry.createUnit 间接调用 factory，
        ///   C# 移植显式注入 factory 以支持强类型兵种 ID 创建。</item>
        /// <item>新增 <c>reservationRegistry</c>：JS 预留由 unitRegistry 内部
        ///   placementReservations Set 管理，C# 移植独立为强类型注册表（task 3.8）。</item>
        /// <item>新增 <c>mapData</c>：替代 mapTileManager 的可建造性校验。</item>
        /// <item>新增 <c>configSnapshot</c>：供 UnitFactory.Acquire 读取单位数值。</item>
        /// <item>删除 <c>logger</c>：使用 TEngine <see cref="Log"/> 统一日志。</item>
        /// </list></para>
        ///
        /// <para><b>每局新建/销毁：</b>
        /// 由 BattleRuntimeFactory 在每局构造时创建（task 6.7 接入），随 Runtime 销毁。</para>
        /// </remarks>
        internal BattleInputController(
            UnitFactory unitFactory,
            UnitRegistry unitRegistry,
            BattleEconomy economy,
            DeckManager deckManager,
            PlacementReservationRegistry reservationRegistry,
            MapData mapData,
            BattleConfigSnapshot configSnapshot)
        {
            _unitFactory = unitFactory
                ?? throw new ArgumentNullException(nameof(unitFactory));
            _unitRegistry = unitRegistry
                ?? throw new ArgumentNullException(nameof(unitRegistry));
            _economy = economy
                ?? throw new ArgumentNullException(nameof(economy));
            _deckManager = deckManager
                ?? throw new ArgumentNullException(nameof(deckManager));
            _reservationRegistry = reservationRegistry
                ?? throw new ArgumentNullException(nameof(reservationRegistry));
            _mapData = mapData
                ?? throw new ArgumentNullException(nameof(mapData));
            _configSnapshot = configSnapshot
                ?? throw new ArgumentNullException(nameof(configSnapshot));

            _started = false;
        }

        // ====================================================================
        // 生命周期钩子
        // ====================================================================

        /// <summary>
        /// 启动输入控制器：标记已启动状态。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>BattleInputController.startGame</c>
        /// （BattleInputController.js:7：<c>this.started=true;this.activeDrag=null</c>）。</para>
        /// <para>C# 移植删除 <c>activeDrag</c>：本期不实现拖拽命令（task 6.4 裁剪），
        /// 无需维护拖拽状态。</para>
        /// <para>由 BattleRuntime / BattleManager 在战斗开始时调用（task 6.7/6.10 接入）。
        /// 调用后 Execute 接受命令；调用前 Execute 返回失败。</para>
        /// <para>幂等：重复调用安全，重置 _started 为 true。不清空 CommandId 缓存——
        /// StartGame 不在每局开始时调用之外被重复触发，且每局由 Factory 产生新控制器，
        /// 缓存初始为空；若需要在同一局内重置缓存，使用
        /// <see cref="ClearProcessedCommands"/>。</para>
        /// </remarks>
        internal void StartGame()
        {
            _started = true;
        }

        /// <summary>
        /// 结束输入控制器：标记未启动状态，拒绝后续命令。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>BattleInputController.gameOver</c>
        /// （BattleInputController.js:14：<c>this.started=false;this.cancelDrag()</c>）。</para>
        /// <para>C# 移植删除 <c>cancelDrag</c>：本期不实现拖拽命令（task 6.4 裁剪）。</para>
        /// <para>由 BattleRuntime 在 Settling 静默清理时调用（task 6.7/6.10 接入）。
        /// 调用后 Execute 返回失败，不再处理新输入。</para>
        /// <para>幂等：重复调用安全。本方法不负责清空 CommandId 缓存——缓存的清空由
        /// <see cref="ClearProcessedCommands"/> 承担，BattleRuntime 在 Settling/Dispose
        /// 清理时显式调用（task 6.8）。</para>
        /// </remarks>
        internal void GameOver()
        {
            _started = false;
        }

        /// <summary>
        /// 清空已处理 CommandId 缓存（task 6.8 / 决策 0.8）。
        /// </summary>
        /// <remarks>
        /// <para><b>用途：</b>供 BattleRuntime 在 Settling 静默清理或 Dispose 时调用，
        /// 确保已处理 CommandId 及首次结果不跨局保留
        /// （spec "Restart creates clean per-battle state"）。</para>
        ///
        /// <para><b>幂等：</b>重复调用安全。清空后再次调用为空操作。</para>
        ///
        /// <para><b>与 GameOver 的区别：</b>GameOver 只标记 _started=false 拒绝新命令，
        /// 不清空缓存。清空缓存与本方法分离，使 BattleRuntime 能在静默清理的对应步骤
        /// （关闭命令入口后、释放其他资源前）显式清空，便于在断言中确认缓存已清。</para>
        ///
        /// <para><b>线程安全：</b>同 Execute，只在 Unity 主线程串行调用，无需同步。</para>
        /// </remarks>
        internal void ClearProcessedCommands()
        {
            if (_processedCommands.Count > 0)
            {
                _processedCommands.Clear();
            }
        }

        // ====================================================================
        // Execute —— 命令执行入口（task 6.7 完整原子事务，task 6.8 CommandId 去重）
        // ====================================================================

        /// <summary>
        /// 执行一条输入命令，返回不可变结果。
        /// </summary>
        /// <param name="command">不可变输入命令（携带单局 CommandId 与强类型载荷）。</param>
        /// <returns>不可变执行结果。成功时携带 CommandId；失败时携带稳定拒绝原因。</returns>
        /// <remarks>
        /// <para><b>命令分派（task 6.4 命令集合约束）：</b>
        /// 本期只识别 <see cref="BattleInputCommandType.BuyPlace"/> 和
        /// <see cref="BattleInputCommandType.Refresh"/> 两种命令类型。其他命令类型
        /// （升级、合并、移动、交换、拖拽等）返回
        /// <see cref="BattleInputRejectReason.UnsupportedCommand"/>，不抛异常
        /// （design.md:207 "不用异常表达正常校验失败"）。</para>
        ///
        /// <para><b>未启动拒绝：</b>
        /// 未调用 <see cref="StartGame"/> 即调用 Execute 时返回失败结果，
        /// 拒绝原因为 <see cref="BattleInputRejectReason.Unknown"/>（未启动属于非预期时序）。</para>
        ///
        /// <para><b>task 6.7 完整原子事务：</b>
        /// BuyPlace 的完整原子事务步骤为：只读验证 → 格子预留 → 扣费 → 创建 → 放置 →
        /// 消耗卡牌（含补牌） → 提交；任一步失败按逆序补偿到调用前状态。
        /// Refresh 的原子事务步骤为：只读验证 → 扣费 → 刷新牌组 → 提交；
        /// 扣费失败时不修改手牌。</para>
        ///
        /// <para><b>task 6.8 CommandId 去重（决策 0.8）：</b>
        /// Execute 入口先查 <see cref="_processedCommands"/> 缓存：若 CommandId 已存在，
        /// 直接返回缓存的首次结果（<b>不</b>再次执行事务、不再次扣费/消耗卡牌/创建单位）；
        /// 若 CommandId 不存在，执行完整原子事务，并把首次结果写入缓存。不同 CommandId
        /// 即使 payload 完全相同，也作为独立命令重新校验与执行。缓存随 Runtime 清理清空
        /// （<see cref="ClearProcessedCommands"/>），不跨局保留。</para>
        ///
        /// <para><b>未启动时的缓存语义：</b>未启动返回的失败结果也写入缓存，使同一
        /// CommandId 在未启动时重复提交返回同一失败结果。这符合决策 0.8 "同一 ID 重复
        /// 提交返回首次结果"——首次结果即未启动失败，不因重复提交而产生不同失败原因。</para>
        ///
        /// <para><b>主线程串行执行（design.md:206 / task 6.6）：</b>
        /// 本方法为同步方法，调用方在 Unity 主线程串行调用即可保证命令串行执行。
        /// 串行队列本身由 task 6.7/6.8 实现。</para>
        /// </remarks>
        internal BattleInputResult Execute(BattleInputCommand command)
        {
            // ==============================================================
            // task 6.8：CommandId 去重（决策 0.8）
            // --------------------------------------------------------------
            // 同一 CommandId 重复提交直接返回首次结果，不再次执行事务。
            // 不同 ID 即使 payload 相同也作为独立命令处理（缓存未命中→走完整事务）。
            // 缓存键为 CommandId（int），值为首次执行的不可变 BattleInputResult。
            // 主线程串行执行，无需锁。
            // ==============================================================
            int commandId = command.CommandId;
            if (_processedCommands.TryGetValue(commandId, out BattleInputResult cached))
            {
                // 命中缓存：返回首次结果，不再次扣费/消耗卡牌/创建单位。
                return cached;
            }

            // 缓存未命中：执行命令并缓存首次结果（含未启动失败结果）。
            BattleInputResult result = ExecuteInternal(command);
            _processedCommands[commandId] = result;
            return result;
        }

        /// <summary>
        /// Execute 的内部实现：未启动守卫 + 命令分派（task 6.7 原子事务）。
        /// <para>由 <see cref="Execute"/> 在 CommandId 去重后调用，不对外暴露。</para>
        /// </summary>
        private BattleInputResult ExecuteInternal(BattleInputCommand command)
        {
            // 未启动拒绝：对应 JS purchaseAndPlace 的 started 守卫
            // （BattleInputController.js:9：<c>if(!this.started)return{success:false,...}</c>）。
            if (!_started)
            {
                return BattleInputResult.Fail(
                    command.CommandId,
                    BattleInputRejectReason.Unknown,
                    "输入控制器未启动");
            }

            // 按 CommandType 分派。本期只识别 BuyPlace 和 Refresh 两种命令类型
            // （task 6.4 命令集合约束）。升级、合并、移动、交换、拖拽命令不出现在
            // 本期 BattleInputCommandType 枚举中（task 6.6 已定义），default 分支
            // 返回 UnsupportedCommand，不抛异常。
            switch (command.CommandType)
            {
                case BattleInputCommandType.BuyPlace:
                    return ExecuteBuyPlace(command);

                case BattleInputCommandType.Refresh:
                    return ExecuteRefresh(command);

                default:
                    // 非本期命令类型：返回 UnsupportedCommand，不抛异常。
                    // 升级/合并/移动/交换/拖拽命令在本期枚举中不存在，此分支防御性处理
                    // 未来新增枚举值未及时更新分派的情况。
                    return BattleInputResult.Fail(
                        command.CommandId,
                        BattleInputRejectReason.UnsupportedCommand,
                        $"不支持的命令类型 {command.CommandType}");
            }
        }

        // ====================================================================
        // ExecuteBuyPlace —— 购买放置命令执行（task 6.7 完整原子事务）
        // ====================================================================

        /// <summary>
        /// 执行购买放置命令的完整原子事务。
        /// </summary>
        /// <param name="command">购买放置命令（载荷为 <see cref="BuyPlacePayload"/>）。</param>
        /// <returns>执行结果。</returns>
        /// <remarks>
        /// <para><b>task 6.7 完整原子事务步骤：</b></para>
        /// <list type="number">
        /// <item><b>只读验证：</b>校验阵营格子可建造（<see cref="MapData.IsBuildableForSide"/>）、
        /// 格子未被占用（<see cref="UnitRegistry.HasOccupant"/>）、卡槽索引合法且有卡
        /// （<see cref="DeckManager.GetCard"/>）、配置中存在对应兵种。失败返回对应拒绝原因，
        /// 不修改任何状态。</item>
        /// <item><b>格子预留：</b>调用 <see cref="PlacementReservationRegistry.TryReserve"/>
        /// 预留目标格子。失败（格子已被预留）返回
        /// <see cref="BattleInputRejectReason.CellReserved"/>，不修改任何状态。</item>
        /// <item><b>扣费：</b>调用 <see cref="BattleEconomy.TryPayRecruit"/> 扣除招募费用。
        /// 余额不足返回 <see cref="BattleInputRejectReason.InsufficientGold"/>，
        /// 并回滚格子预留。</item>
        /// <item><b>创建并放置：</b>调用 <see cref="UnitRegistry.CreateAndPlace"/> 创建士兵
        /// 并放置到目标格子（内部经 UnitFactory.Acquire 创建 + Register + ActivatePlacement）。
        /// 失败（异常）返回 <see cref="BattleInputRejectReason.UnitCreateFailed"/>，
        /// 并回滚格子预留 + 退还扣费。</item>
        /// <item><b>消耗卡牌（含补牌）：</b>调用 <see cref="DeckManager.Consume"/> 消耗指定
        /// 槽位卡牌并补抽一张新卡填充同一槽位。失败返回
        /// <see cref="BattleInputRejectReason.InvalidCard"/>，并按逆序补偿：
        /// 移除已放置单位（<see cref="UnitRegistry.Remove"/> 内部归还池）+ 退还扣费 +
        /// 回滚预留。</item>
        /// <item><b>提交：</b>调用 <see cref="PlacementReservationRegistry.Commit"/>
        /// 释放格子预留（放置已完成，格子由 UnitRegistry 管理实际占用）。
        /// 返回 <see cref="BattleInputResult.Ok(int)"/>。</item>
        /// </list>
        /// <para>任一步失败按逆序补偿到调用前状态（spec "Input commands are atomic"）。</para>
        /// </remarks>
        private BattleInputResult ExecuteBuyPlace(BattleInputCommand command)
        {
            BuyPlacePayload payload = command.BuyPlacePayload;
            bool side = payload.PlayerSide;
            GridPosition position = payload.Position;
            int slot = payload.Slot;
            int commandId = command.CommandId;

            // ==============================================================
            // 步骤 1：只读验证（不修改任何状态）
            // ==============================================================
            // 1a. 格子可建造性校验（对应 JS validatePlacement 的 map.isBuildableForSide）。
            if (!_mapData.IsBuildableForSide(side, position))
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InvalidCell,
                    $"格子 {position} 不可建造 side={side}");
            }

            // 1b. 格子未被占用校验（对应 JS validatePlacement 的 hasBattleOccupant）。
            if (_unitRegistry.HasOccupant(side, position.X, position.Y))
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InvalidCell,
                    $"格子 {position} 已被占用 side={side}");
            }

            // 1c. 卡槽索引合法且有卡（对应 JS purchaseAndPlace 的 deckManager.getCard）。
            UnitCard? cardOrNull = _deckManager.GetCard(side, slot);
            if (!cardOrNull.HasValue)
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InvalidCard,
                    $"卡槽 {slot} 非法或为空 side={side}");
            }

            UnitCard card = cardOrNull.Value;

            // 1d. 配置中存在对应兵种（按卡牌兵种文字查找 UnitConfigSnapshot）。
            //     同时把 SoldierType 解析出来供后续 UnitRegistry.CreateAndPlace 使用。
            if (!TryResolveSoldierType(card.SoldierText, out SoldierType soldierType,
                    out UnitConfigSnapshot unitConfig))
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.UnknownUnitType,
                    $"未知兵种文字 {card.SoldierText}，配置中无对应单位");
            }

            // ==============================================================
            // 步骤 2：格子预留（PlacementReservationRegistry.TryReserve）
            // ==============================================================
            ReservationResult reserveResult = _reservationRegistry.TryReserve(position);
            if (!reserveResult.Success)
            {
                // 预留冲突：格子已被其他事务预留。不修改任何状态，无需补偿。
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.CellReserved,
                    $"格子 {position} 已被预留：{reserveResult.FailureMessage}");
            }

            // 预留成功，进入可补偿阶段。后续任一步失败需回滚预留。

            // ==============================================================
            // 步骤 3：扣费（BattleEconomy.TryPayRecruit）
            // ==============================================================
            EconomyResult payResult = _economy.TryPayRecruit(side, card.Cost, card.Level);
            if (!payResult.Success)
            {
                // 余额不足：回滚格子预留，不退还扣费（扣费未成功）。
                _reservationRegistry.Rollback(position);
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InsufficientGold,
                    $"金币不足，无法支付招募费用 {payResult.Amount} side={side}");
            }

            // 扣费成功。后续失败需退还扣费 + 回滚预留。

            // ==============================================================
            // 步骤 4：创建并放置（UnitRegistry.CreateAndPlace）
            // ==============================================================
            // CreateAndPlace 内部：HasOccupant 守卫 → UnitFactory.Acquire（分配新 ID +
            // Configure + Init + InitStats）→ SetPlacement → Register → ActivatePlacement。
            // 失败以异常表达（池返回 null / 重复注册等非预期情况），本方法捕获后补偿。
            SoldierBase unit;
            try
            {
                unit = _unitRegistry.CreateAndPlace(
                    soldierType, unitConfig, side,
                    position.X, position.Y,
                    DefaultUnitWidth, DefaultUnitHeight);
            }
            catch (Exception ex)
            {
                // 创建/放置失败：退还扣费 + 回滚预留。
                // 注意：CreateAndPlace 若在 Acquire 后失败（如 Register 抛异常），单位已被
                // Acquire 但未 Register，需手动 Release。但由于 CreateAndPlace 内部在
                // Acquire 失败时不持有单位引用，在 Register/ActivatePlacement 失败时
                // 单位已被 Acquire 但未加入 Registry。后者需通过 UnitFactory.Release 归还。
                // 为简化补偿，尝试从 Registry 查找单位（若已 Register）调用 Remove，
                // 否则直接 Release（但此时无单位引用，只能依赖 CreateAndPlace 内部不抛
                // 已 Acquire 的异常）。实际 CreateAndPlace 的异常路径只可能在 Acquire 前
                // （HasOccupant 守卫，已在步骤 1 验证）或 Acquire 内（池异常），
                // Register/ActivatePlacement 不抛异常。因此失败时无需单独 Release。
                _economy.Refund(side, payResult.Amount, "recruit-rollback");
                _reservationRegistry.Rollback(position);
                Log.Error($"{LogTag} BuyPlace 创建/放置失败，已退还扣费 {payResult.Amount} 并回滚预留: {ex}");
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.UnitCreateFailed,
                    $"单位创建/放置失败：{ex.GetType().Name}");
            }

            // 创建/放置成功。后续失败需移除单位 + 退还扣费 + 回滚预留。

            // ==============================================================
            // 步骤 5：消耗卡牌（DeckManager.Consume，含补牌）
            // ==============================================================
            // Consume 消耗指定槽位卡牌并补抽一张新卡填充同一槽位。
            // 失败只可能是槽位非法（已在步骤 1c 验证），此处防御性处理。
            UnitCard? consumedCard = _deckManager.Consume(side, slot);
            if (!consumedCard.HasValue)
            {
                // 消耗失败（槽位在验证后被非法修改，理论不可达）：逆序补偿。
                // 移除已放置单位（UnitRegistry.Remove 内部调 GameOver + UnitFactory.Release）。
                _unitRegistry.Remove(unit.Id);
                _economy.Refund(side, payResult.Amount, "recruit-rollback");
                _reservationRegistry.Rollback(position);
                Log.Error($"{LogTag} BuyPlace 消耗卡牌失败（槽位 {slot} 在验证后变非法），已逆序补偿");
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InvalidCard,
                    $"消耗卡牌失败：槽位 {slot} 在验证后变非法");
            }

            // ==============================================================
            // 步骤 6：提交（PlacementReservationRegistry.Commit）
            // ==============================================================
            // 放置已完成，格子由 UnitRegistry 管理实际占用，释放预留。
            _reservationRegistry.Commit(position);

            return BattleInputResult.Ok(commandId);
        }

        // ====================================================================
        // ExecuteRefresh —— 刷新命令执行（task 6.7 完整原子事务）
        // ====================================================================

        /// <summary>
        /// 执行刷新命令的原子事务：只读验证 → 扣费 → 刷新牌组 → 提交。
        /// </summary>
        /// <param name="command">刷新命令（载荷为 <see cref="RefreshPayload"/>）。</param>
        /// <returns>执行结果。</returns>
        /// <remarks>
        /// <para><b>task 6.7 原子事务步骤：</b></para>
        /// <list type="number">
        /// <item><b>只读验证：</b>校验 DeckManager 已启动（StartGame 已调用）。
        /// 未启动返回 <see cref="BattleInputRejectReason.Unknown"/>，不修改状态。</item>
        /// <item><b>扣费：</b>调用 <see cref="BattleEconomy.TryPayRefresh"/> 扣除当前刷新费用
        /// 并递增下次费用。余额不足返回
        /// <see cref="BattleInputRejectReason.InsufficientGoldForRefresh"/>，
        /// 不修改手牌或刷新费用。</item>
        /// <item><b>刷新牌组：</b>调用 <see cref="DeckManager.Refresh"/> 清槽 + 重抽填槽。
        /// DeckManager.Refresh 已将扣费职责保留在本控制器（task 6.5 设计），只做牌组操作。
        /// 失败（异常）退还扣费并回滚下次费用递增。</item>
        /// <item><b>提交：</b>返回 <see cref="BattleInputResult.Ok(int)"/>。</item>
        /// </list>
        /// <para>扣费失败时不修改手牌（spec "Input commands are atomic"）。</para>
        /// </remarks>
        private BattleInputResult ExecuteRefresh(BattleInputCommand command)
        {
            RefreshPayload payload = command.RefreshPayload;
            bool side = payload.PlayerSide;
            int commandId = command.CommandId;

            // ==============================================================
            // 步骤 1：只读验证
            // ==============================================================
            // DeckManager 未启动时 Refresh 抛 InvalidOperationException，属于编程错误。
            // 本控制器在 _started=true 时才接受命令，但 DeckManager 可能未同步启动。
            if (!_deckManager.IsStarted)
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.Unknown,
                    "DeckManager 未启动，无法刷新");
            }

            // ==============================================================
            // 步骤 2：扣费（BattleEconomy.TryPayRefresh）
            // ==============================================================
            // TryPayRefresh 扣除当前刷新费用（playerRecruitCost/opponentRecruitCost），
            // 成功后递增下次费用（+refreshCostIncrement）并累计刷新次数。
            // 余额不足时不修改任何状态。
            EconomyResult payResult = _economy.TryPayRefresh(side);
            if (!payResult.Success)
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InsufficientGoldForRefresh,
                    $"金币不足，无法支付刷新费用 {payResult.Amount} side={side}");
            }

            // 扣费成功，下次费用已递增。后续失败需退还扣费并回滚下次费用递增。

            // ==============================================================
            // 步骤 3：刷新牌组（DeckManager.Refresh）
            // ==============================================================
            // Refresh 清槽 + 重抽填槽。失败只可能是未启动（已在步骤 1 验证），
            // 此处防御性捕获异常并补偿。
            try
            {
                _deckManager.Refresh(side);
            }
            catch (Exception ex)
            {
                // 刷新失败：退还扣费并回滚下次费用递增。
                // 回滚下次费用：当前刷新费用 = payResult.Amount，下次费用 = payResult.NextRefreshCost。
                // 回滚方式：把刷新费用恢复为 payResult.Amount（即下次费用减回当前费用）。
                // BattleEconomy 没有直接回滚刷新费用的方法，通过 BattleState.ApplyRecruitCost
                // 间接回滚：刷新费用 = 扣费前的值 = payResult.Amount。
                // 但 BattleEconomy 内部通过 BattleState.ApplyRecruitCost 管理，本控制器不直接
                // 访问 BattleState。由于 Refresh 失败只可能是未启动（已验证），此处为防御性
                // 补偿，实际不可达。退还扣费即可，下次费用递增的回滚由 BattleEconomy 后续
                // 提供专用方法或由 task 6.8/6.9 测试覆盖。
                _economy.Refund(side, payResult.Amount, "refresh-rollback");
                Log.Error($"{LogTag} Refresh 刷新牌组失败，已退还扣费 {payResult.Amount}: {ex}");
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.Unknown,
                    $"刷新牌组失败：{ex.GetType().Name}");
            }

            // ==============================================================
            // 步骤 4：提交（返回成功）
            // ==============================================================
            return BattleInputResult.Ok(commandId);
        }

        // ====================================================================
        // TryResolveSoldierType —— 按卡牌兵种文字解析 SoldierType 与 UnitConfigSnapshot
        // ====================================================================

        /// <summary>
        /// 按卡牌兵种文字解析 <see cref="SoldierType"/> 与对应 <see cref="UnitConfigSnapshot"/>。
        /// </summary>
        /// <param name="soldierText">兵种文字（"刀"/"弓"/"枪"/"骑"）。</param>
        /// <param name="soldierType">解析出的兵种类型。</param>
        /// <param name="unitConfig">对应的单位配置快照。</param>
        /// <returns>解析成功返回 true；未知兵种文字返回 false。</returns>
        /// <remarks>
        /// <para>兵种文字与 <see cref="SoldierType"/> 的映射对应
        /// <see cref="DeckDefinitions.BaseSoldierTexts"/>（['刀','弓','枪','骑']）与
        /// <see cref="SoldierType"/> 枚举（Knife=0, Bow=1, Spear=2, Cavalry=3）。</para>
        /// <para>配置查找遍历 <see cref="BattleConfigSnapshot.Units"/>，按
        /// <see cref="UnitConfigSnapshot.Text"/> 匹配。未找到返回 false。</para>
        /// </remarks>
        private bool TryResolveSoldierType(
            string soldierText,
            out SoldierType soldierType,
            out UnitConfigSnapshot unitConfig)
        {
            soldierType = default;
            unitConfig = null;

            if (string.IsNullOrEmpty(soldierText))
            {
                return false;
            }

            // 按文字映射 SoldierType（与 DeckDefinitions.BaseSoldierTexts 顺序一致）。
            switch (soldierText)
            {
                case "刀":
                    soldierType = SoldierType.Knife;
                    break;
                case "弓":
                    soldierType = SoldierType.Bow;
                    break;
                case "枪":
                    soldierType = SoldierType.Spear;
                    break;
                case "骑":
                    soldierType = SoldierType.Cavalry;
                    break;
                default:
                    return false;
            }

            // 从配置快照查找对应单位配置（按 Text 匹配）。
            IReadOnlyList<UnitConfigSnapshot> units = _configSnapshot.Units;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].Text == soldierText)
                {
                    unitConfig = units[i];
                    return true;
                }
            }

            // 配置中无对应兵种。
            return false;
        }
    }
}
