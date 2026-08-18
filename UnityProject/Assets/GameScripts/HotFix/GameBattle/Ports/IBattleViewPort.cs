using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.3：IBattleViewPort —— 逻辑实体生成、状态和回收到表现层的最小端口
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Ports/IBattleViewPort.cs）：
    //   逻辑实体生成、状态和回收到表现层的最小端口。逻辑层通过本端口向表现层
    //   发送"实体已生成 / 实体已移除 / 状态已变化"等低频事实，不直接持有 Unity
    //   GameObject、FairyGUI 组件或任何表现对象。表现层（BattlePresenter /
    //   BattleViewRegistry / BattleViewSynchronizer，task 7.4）实现本端口，把
    //   逻辑事实翻译成视图操作，且不得回写规则状态（design.md 第 1 节：
    //   "BattlePresenter 把只读状态/事实翻译成视图操作，不回写规则状态"）。
    //
    // 来源证据（design.md 决策 4 / specs/battle-event-boundary/spec.md）：
    //   - design.md:9 "逻辑层不依赖 MonoBehaviour、FairyGUI、Scene 对象或
    //     Time.deltaTime；表现通过端口和 Presenter 同步"
    //   - design.md:214 "逻辑实体生成、状态和回收到表现层的最小端口"
    //   - design.md:217 "BattlePresenter 把只读状态/事实翻译成视图操作，
    //     不回写规则状态"
    //   - spec battle-event-boundary "UI receives typed battle facts"：
    //     系统以强类型事实通知战斗 UI，提供当前值或不可变快照
    //   - spec battle-runtime-lifecycle "Exit releases battle-owned state"：
    //     退出时取消异步操作和表现回调
    //
    // 异步与取消语义（任务 7.3 要求）：
    //   所有异步操作接收 Runtime 或 Module CancellationToken。Runtime Token 由
    //   BattleRuntime.RuntimeTokenSource 提供（task 2.10），在 Settling 静默清理
    //   与 Exit 时 Cancel，使迟到的表现回调因 Token 失效（spec "Exit releases
    //   battle-owned state"）。Null 实现的异步方法立即返回，不执行任何 IO。
    //
    // 与事件边界的关系（design.md 第 4 节 / spec battle-event-boundary）：
    //   本端口属于"表现端口"层，不是事件总线。它只承载逻辑→表现的单向事实
    //   通知，不承担内部一致性（直接调用）、单局低频局部信号（SignalHub）或
    //   跨程序集公共事实（GameEvent）职责。高频逐实体推进（如每子步位置插值）
    //   不应通过本端口驱动，而由 BattleViewSynchronizer 在 Unity 帧中读取
    //   BattleReadModel 完成（design.md 第 4 节 / task 7.9 性能 profile 要求）。
    //
    // 不变量：
    //   1. 逻辑层单向调用：逻辑层只调本端口通知事实，不从端口读规则状态。
    //   2. 不持有表现对象引用：接口参数只包含逻辑标量与值类型（RuntimeId / 坐标 /
    //      阵营 / 血量等），不传 GameObject / FairyGUI 组件 / Entity 引用。
    //   3. 异步操作可取消：所有 async 方法接收 CancellationToken，取消时抛出
    //      OperationCanceledException，保留取消异常语义（与 IBattleModule 一致）。
    //   4. 线程安全不要求：所有调用在 Unity 主线程的 Runtime 串行队列中执行
    //      （design.md:206 / task 6.6）。
    // ============================================================================

    /// <summary>
    /// 逻辑实体生成、状态和回收到表现层的最小端口（design.md Ports/IBattleViewPort.cs）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:214）：</b>逻辑层通过本端口向表现层发送实体生成/移除/
    /// 状态变化等低频事实。表现层实现本端口，把事实翻译成视图操作，不回写规则状态
    /// （design.md:217）。</para>
    ///
    /// <para><b>逻辑层不依赖表现（design.md:9）：</b>逻辑层只调本端口的通知方法，
    /// 不直接持有 Unity GameObject、FairyGUI 组件或任何表现对象。端口参数只包含
    /// 逻辑标量与值类型。</para>
    ///
    /// <para><b>异步与取消（任务 7.3）：</b>所有异步操作接收 Runtime 或 Module
    /// CancellationToken。Runtime Token 在 Settling / Exit 时 Cancel，使迟到回调失效
    /// （spec "Exit releases battle-owned state"）。</para>
    ///
    /// <para><b>与事件边界的关系（design.md 第 4 节）：</b>本端口是表现端口，不是事件总线。
    /// 高频逐实体推进不应通过本端口驱动，而由 BattleViewSynchronizer 在 Unity 帧中读取
    /// BattleReadModel 完成（task 7.9 性能 profile）。</para>
    ///
    /// <para><b>线程安全：</b>不要求。所有调用在 Unity 主线程的 Runtime 串行队列中执行。</para>
    ///
    /// <para><b>实现：</b></para>
    /// <list type="bullet">
    /// <item><see cref="NullBattleViewPort"/> —— 纯逻辑 Null/Test 实现，用于 EditMode 测试
    /// （design.md:215 "逻辑测试可使用空实现"）。</item>
    /// <item>TODO Phase 6 task 7.4：Unity/FairyGUI 真实实现（由 BattlePresenter /
    /// BattleViewRegistry 组合实现），用于 PlayMode/生产。真实实现将通过
    /// GameModule.Resource / GameFUI 异步加载表现资源，并登记到 BattleRuntimeScope
    /// 随局释放。当前 asmdef 未引用 FairyGUI，真实实现待 FairyGUI Change 冻结公共
    /// 注册契约后（task 7.5）由对应文件接入。</item>
    /// </list>
    /// </remarks>
    public interface IBattleViewPort
    {
        // ====================================================================
        // 生命周期
        // ====================================================================

        /// <summary>
        /// 异步预加载本端口所需的表现资源（Prefab/图集/材质等）。
        /// </summary>
        /// <param name="enemyResourceAddresses">
        /// 本局所选计划解析后会使用的去重普通敌人资源地址（YooAsset location）。
        /// 由 <see cref="BattlePresentationResourcePlan"/> 从所选配置快照收集；
        /// 表现层只为这些地址加载 Prefab 并建立表现池，不再固定预加载 Mob0。
        /// </param>
        /// <param name="cancellationToken">
        /// Runtime 或 Module 取消令牌。取消时抛出 <see cref="System.OperationCanceledException"/>。
        /// Runtime Token 由 <c>BattleRuntime.RuntimeTokenSource</c> 提供，在 Settling / Exit 时 Cancel。
        /// </param>
        /// <remarks>
        /// <para>由 BattleRuntimeFactory 在组装阶段调用（或由 BattleModule 在 Entering 阶段调用），
        /// 在进入 Running 前完成表现资源预加载。加载失败应向上传播为结构化
        /// <c>BattleOperationResult</c>（由调用方包装），使部分初始化回滚
        /// （spec "Partial initialization is recoverable"）。</para>
        /// <para>Null 实现立即返回 <see cref="UniTask.CompletedTask"/>，不执行任何 IO。</para>
        /// <para>真实实现将通过 <c>GameModule.Resource.LoadAssetAsync</c> 加载资源并登记到
        /// <see cref="BattleRuntimeScope"/>（task 7.6）。</para>
        /// </remarks>
        UniTask PreloadAsync(
            IReadOnlyList<string> enemyResourceAddresses,
            CancellationToken cancellationToken);

        /// <summary>
        /// 通知表现层：战斗已开始，可初始化场景视图与 UI。
        /// </summary>
        /// <param name="maxRounds">最大波次数（供 UI 显示进度）。</param>
        /// <param name="playerMaxHealth">玩家方最大生命（供 UI 初始化血条）。</param>
        /// <param name="opponentMaxHealth">对手方最大生命。</param>
        /// <remarks>
        /// <para>由 BattleRuntime / BattlePresenter 在进入 Running 时调用。
        /// Null 实现为空操作。</para>
        /// </remarks>
        void OnBattleStarted(int maxRounds, int playerMaxHealth, int opponentMaxHealth);

        /// <summary>
        /// 通知表现层：战斗已结束，进入结算表现。
        /// </summary>
        /// <param name="playerWin">true=玩家方获胜；false=对手方获胜。</param>
        /// <param name="resultStar">结果星级（0~3）。</param>
        /// <remarks>
        /// <para>由 BattleRuntime 在 Settling 静默清理完成、发布结果后调用。
        /// 表现层据此播放结算动画，但不得回写规则状态（design.md:217）。
        /// Null 实现为空操作。</para>
        /// </remarks>
        void OnBattleFinished(bool playerWin, int resultStar);

        // ====================================================================
        // 实体生成与回收（design.md:214 "逻辑实体生成、状态和回收"）
        // ====================================================================

        /// <summary>
        /// 通知表现层：一个敌人已生成，需创建对应表现对象。
        /// </summary>
        /// <param name="dto">
        /// 不可变出生表现 DTO：runtimeId + enemyKey/resourceAddress + 车道与逻辑坐标。
        /// 表现层以 <paramref name="dto"/>.ResourceAddress 为表现池键，不再固定 Mob0。
        /// </param>
        /// <remarks>
        /// <para>对应逻辑层 EnemyManager 生成敌人事实（task 5.1 起携带
        /// <see cref="EnemySpawnViewData"/>）。表现层据此实例化敌人 Prefab / FUI 组件
        /// 并注册到 <c>BattleViewRegistry</c>（task 7.4）。</para>
        /// <para>高频调用：每波次可能批量生成多个敌人。真实实现应使用对象池复用表现对象，
        /// 不在每次调用时同步加载资源（资源预加载在 <see cref="PreloadAsync"/> 完成）。</para>
        /// </remarks>
        void OnEnemySpawned(EnemySpawnViewData dto);

        /// <summary>
        /// 通知表现层：一个敌人已移除（死亡或回收），需销毁/隐藏对应表现对象。
        /// </summary>
        /// <param name="runtimeId">敌人运行时 ID。</param>
        /// <param name="playDeathEffect">是否播放死亡表现（true=正常死亡；false=战斗结束清理回收）。</param>
        void OnEnemyRemoved(int runtimeId, bool playDeathEffect);

        /// <summary>通知表现层播放 Boss 技能动画或恢复待机动画。</summary>
        void OnBossSkillIntent(int runtimeId, string animationKey, bool active);

        /// <summary>
        /// 通知表现层：一个单位已放置，需创建对应表现对象。
        /// </summary>
        /// <param name="runtimeId">单位运行时 ID。</param>
        /// <param name="isPlayerSide">是否玩家方。</param>
        /// <param name="soldierType">兵种类型（0=刀, 1=弓, 2=枪, 3=骑）。</param>
        /// <param name="gridX">放置格子列索引。</param>
        /// <param name="gridY">放置格子行索引。</param>
        /// <param name="level">单位当前等级（首次上场显示真实等级，修复 P0）。</param>
        void OnUnitPlaced(int runtimeId, bool isPlayerSide, int soldierType, int gridX, int gridY, int level);

        /// <summary>
        /// 通知表现层：一个单位已移除（卖出或战斗结束回收），需销毁/隐藏对应表现对象。
        /// </summary>
        /// <param name="runtimeId">单位运行时 ID。</param>
        void OnUnitRemoved(int runtimeId);

        /// <summary>
        /// 通知表现层：一个战场单位移动到新战场格（最终方案"战场槽换位"）。
        /// </summary>
        /// <param name="runtimeId">单位运行时 ID。</param>
        /// <param name="gridX">新战场格子列索引。</param>
        /// <param name="gridY">新战场格子行索引。</param>
        /// <remarks>
        /// 战场槽换位时复用同一战斗实例，只更新表现位置（不重建表现对象）。
        /// </remarks>
        void OnUnitMoved(int runtimeId, int gridX, int gridY);

        /// <summary>
        /// 通知表现层：一个战场单位等级变化（最终方案"等级数值和等级表现"）。
        /// </summary>
        /// <param name="runtimeId">单位运行时 ID。</param>
        /// <param name="newLevel">新等级。</param>
        void OnUnitLevelChanged(int runtimeId, int newLevel);

        /// <summary>
        /// 通知表现层：一个投射物已发射，需创建对应表现对象。
        /// </summary>
        /// <param name="runtimeId">投射物运行时 ID。</param>
        /// <param name="fromX">发射点逻辑 X。</param>
        /// <param name="fromY">发射点逻辑 Y。</param>
        /// <param name="isPlayerSide">是否玩家方发射。</param>
        void OnProjectileFired(int runtimeId, float fromX, float fromY, bool isPlayerSide);

        /// <summary>
        /// 通知表现层：一个投射物已移除（命中、失效或回收），需销毁/隐藏对应表现对象。
        /// </summary>
        /// <param name="runtimeId">投射物运行时 ID。</param>
        void OnProjectileRemoved(int runtimeId);

        // ====================================================================
        // 状态同步（design.md:214 "状态" / spec battle-event-boundary "UI receives typed battle facts"）
        // ====================================================================

        /// <summary>
        /// 通知表现层：任一战斗目标生命变化。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方生命变化；false=对手方。</param>
        /// <param name="currentHealth">变化后当前生命。</param>
        /// <param name="maxHealth">最大生命。</param>
        /// <param name="delta">变化量（正=回复，负=受伤，0=满血重置等）。</param>
        /// <remarks>
        /// <para>对应 spec battle-event-boundary "Health changes" 场景：UI 接收包含目标侧、
        /// 当前生命和变化量的类型安全通知。</para>
        /// </remarks>
        void OnHealthChanged(bool isPlayerSide, int currentHealth, int maxHealth, int delta);

        /// <summary>
        /// 通知表现层：任一方金币变化。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方；false=对手方。</param>
        /// <param name="currentGold">变化后当前金币。</param>
        /// <param name="delta">变化量（正=奖励/卖出，负=招募/刷新）。</param>
        void OnGoldChanged(bool isPlayerSide, int currentGold, int delta);

        /// <summary>
        /// 通知表现层：波次变化。
        /// </summary>
        /// <param name="currentRound">当前波次。</param>
        /// <param name="maxRounds">最大波次（-1 表示无尽模式）。</param>
        void OnRoundChanged(int currentRound, int maxRounds);

        /// <summary>
        /// 通知表现层：手牌已更新（抽牌、消耗、补牌或刷新）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方；false=对手方。</param>
        /// <param name="handSlotCount">手牌槽位数。</param>
        /// <remarks>
        /// <para>表现层据此刷新手牌 UI。具体每槽的卡牌内容由表现层通过
        /// <c>BattleReadModel</c> 查询（避免本端口传递卡牌详情快照造成高频分配）。</para>
        /// </remarks>
        void OnHandUpdated(bool isPlayerSide, int handSlotCount);

        // ====================================================================
        // 清理
        // ====================================================================

        /// <summary>
        /// 清理本端口持有的全部表现对象与监听，用于 Settling 静默清理与 Exit。
        /// </summary>
        /// <remarks>
        /// <para>由 BattleRuntime 在 Settling 静默清理或 BattleModule 在 Exit 时调用。
        /// 真实实现应销毁/回收全部表现对象、解除 UI 监听、停止动画回调。
        /// 幂等：重复调用安全。</para>
        /// <para>Null 实现为空操作。</para>
        /// <para>异步资源卸载（如 YooAsset 句柄 Release）不在此方法同步执行，
        /// 而由 <see cref="BattleRuntimeScope"/> 逆序释放已登记的资源租约
        /// （design.md 第 3 节 / task 2.8）。</para>
        /// </remarks>
        void Clear();
    }
}
