using System;

namespace GameBattle
{
    // ============================================================================
    // 任务 6.1：UnitBase —— 纯逻辑单位生命周期基类
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Unit/UnitBase.cs）：
    //   纯逻辑单位的初始化、放置、等级和回收生命周期，不继承输入/表现类。
    //   规则层不持有 Unity GameObject 或表现组件（design.md 第 9 行）。
    //
    // 来源证据（还原工程 UnitBase.js:1-336）：
    //   UnitBase 继承 UnitDragBase（Laya拖拽表现），持有：
    //     - level / experience / previousContainerType / containerType
    //     - previousGridPosition / gridPosition / placementPhase / placementTween
    //     - baseAttackPower / baseAttackRange / baseAttackIntervalSeconds
    //     - disabled / secondaryDisabled / currentState / displayObject / animation
    //     - id / unitText / side / isActive / destroyed / inPool
    //     - _lifecycleGeneration / _configured / buff* 字段
    //   核心方法：
    //     - configure({...})：注入 laya/gameData/gameLoop/eventBus/objectPool/presentation/audio
    //     - initialize(unitText, side)：分配 ID、取表现对象、注册 gameLoop、init state
    //     - setPlacement(containerType, gridX, gridY)
    //     - onMoved()：isActive = (containerType==BATTLE); changeState(IDLE)
    //     - changeState(next)：onExitState → currentState=next → onEnterState → event
    //     - update(deltaMs)：no-op（子类 SoldierBase 覆写为 idle）
    //     - gameOver()：回收表现对象 + resetData + recoverByClass
    //     - resetData()：清空 level/Experience/container/gridPos/id/side/disabled 等
    //
    // 决策依据：
    //   - design.md 决策 5：删除 SingletonBase/CombatServices/GameObjectEventProxy，
    //     UnitBase 改为强类型注入的 internal 抽象类，不再继承 Laya Sprite 或
    //     UnitDragBase（拖拽属表现层，由 Presentation/BattleInputAdapter 承担）。
    //   - design.md 第 9 行：逻辑层不依赖 MonoBehaviour、FairyGUI、Scene 对象或
    //     Time.deltaTime；表现通过端口和 Presenter 同步。
    //   - design.md 决策 4 / spec battle-event-boundary：单位注册、状态切换、回收等
    //     一致性操作使用直接调用，不通过全局事件总线。原 EventBus event('onStateChange')
    //     改为直接方法调用或局部回调。
    //   - design.md 决策 0.4 / spec "Battle result is frozen once"：首次 TryFreeze 成功后
    //     只完成当前同步提交并中止剩余 phase/子步。本类型不直接引用 BattleResultBuilder，
    //     冻结后的单位行为由 AttackScheduler 的 IsFrozen 守卫与上级 UnitRegistry 清理保证。
    //   - task 4.1：实现 IPoolableBattleObject 以支持池化，ResetState 清除全部可变状态。
    //   - task 6.1 约束：实现纯逻辑 UnitBase，回收后不得保留目标、冷却、事件、命令或
    //     表现引用。
    //
    // 与 AttackScheduler 的契约（task 5.2 AttackScheduler.cs IAttackUnit）：
    //   AttackScheduler 每子步对每个活动单位调用 Update（列表遍历），需要单位暴露：
    //     IsActive / Disabled / InPool / Side / CenterX / CenterY / AttackRange /
    //     AttackIntervalSeconds / LastAttackTimeMs(get;set) / CurrentState /
    //     SetState(AttackUnitState) / Attack()。
    //   本类型实现 IAttackUnit 接口，成员对应 JS UnitBase 字段：
    //     - IsActive ← unit.isActive（是否在战场活动）
    //     - Disabled ← unit.disabled（是否被禁用）
    //     - InPool ← unit.inPool（是否已回池）
    //     - Side ← unit.side（true=玩家方，false=对手方）
    //     - CenterX/CenterY ← displayObject.x + width/2 等（C# 移植为纯逻辑中心坐标）
    //     - AttackRange ← unit.attackRange（攻击范围，逻辑距离）
    //     - AttackIntervalSeconds ← unit.attackIntervalSeconds（攻击冷却间隔，秒）
    //     - LastAttackTimeMs ← unit.lastAttackTime（上次攻击时间戳，毫秒，读/写）
    //     - CurrentState ← unit.currentState（单位状态枚举值）
    //     - SetState ← unit.changeState(state)（切换状态）
    //     - Attack ← unit.attack()（触发攻击，内部创建效果/投射物）
    //
    // 与并行任务的关系：
    //   - SoldierBase（本任务同文件级 sibling）：继承本类，承接攻击数值、冷却与目标范围
    //     契约，实现 IAttackEffectOwner 供攻击效果读取位置/阵营/运行时 ID。
    //   - 4 兵种（task 6.2）：KnifeSoldier/BowSoldier/SpearSoldier/CavalrySoldier
    //     继承 SoldierBase，覆写 PerformAttack 创建各自攻击效果。
    //   - UnitFactory（task 6.3）：通过 BattleObjectPool<SoldierBase 子类> 池化，
    //     Acquire 后调用 AssignRuntimeId 分配新 ID。
    //   - UnitRegistry（task 6.3）：管理单位注册/放置/移除，提供稳定有序列表供
    //     AttackScheduler 遍历（task 4.6 约束）。
    //   - AttackEffectManager（task 5.3，已实现）：士兵创建的攻击效果经此管理器推进。
    //   - AttackResolver（task 5.1，已实现）：士兵攻击效果经此查询目标与提交伤害。
    //
    // C# 与 JS 的差异：
    //   1. 不继承 UnitDragBase：JS UnitBase extends UnitDragBase（Laya拖拽）。
    //      C# 移植删除拖拽基类，拖拽属表现层由 BattleInputAdapter 承担（design 决策 5）。
    //   2. 表现对象 displayObject/animation 删除：JS 持有 Laya Sprite 与 Spine 动画。
    //      C# 移植改为纯逻辑字段 _x/_y/_width/_height，表现通过端口同步
    //      （design.md 第 9 行）。
    //   3. gameLoop/eventBus/objectPool/presentation/audio 删除：JS 经 configure 注入。
    //      C# 移植删除全部 Laya/全局依赖，单位不需要 gameLoop 注册（AttackScheduler
    //      在阶段回调中驱动），不需要 eventBus（design 决策 4 直接调用）。
    //   4. 删除还原工程散落的 buff* 字段；确定性 Buff 聚合由 SoldierBase 的
    //      IBuffTarget 窄接口与每局 BuffManager 统一拥有。
    //   5. placementTween/placementPhase 删除：放置缓动属表现层，规则层只记录
    //      网格坐标与容器类型。
    //   6. currentState 枚举：JS 用字符串 'none'/'skip'/'UnitIdle'/'UnitAttack'。
    //      C# 复用 AttackScheduler 定义的 AttackUnitState（Idle/Attack 两个攻击调度
    //      相关态）。NONE/PLACING 在 C# 由 IsActive=false 等价表达。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Unity GameObject/MonoBehaviour/Sprite/FairyGUI 组件。
    //   2. 池化友好：实现 IPoolableBattleObject，ResetState 后等价于新构造。
    //   3. IAttackUnit 契约：AttackScheduler 只读 IsActive/Disabled/InPool 守卫，
    //      读 CenterX/CenterY/AttackRange/AttackIntervalSeconds/CurrentState，
    //      读写 LastAttackTimeMs，调 SetState/Attack。
    //   4. Acquire/Release Reset 契约：ResetState 后无目标/冷却/事件/命令/表现引用。
    //   5. 生命周期代号：_lifecycleGeneration 每次 Init 递增，供表现层回调守卫
    //      过期回调（对应 JS _lifecycleGeneration）。
    // ============================================================================
    //
    // 注：本类型不依赖 UnityEngine，纯逻辑对象可在 EditMode 无需 Scene 测试。
    // ============================================================================

    /// <summary>
    /// 纯逻辑单位生命周期基类：初始化、放置、状态切换与回收（task 6.1）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>纯逻辑单位的初始化、放置、等级和回收生命周期，
    /// 不继承输入/表现类。替代还原工程 <c>UnitBase.js</c>（UnitBase.js:1-336）。</para>
    ///
    /// <para><b>不持有表现组件（design.md 第 9 行）：</b>
    /// 还原工程 UnitBase 持有 <c>this.displayObject</c>（Laya Sprite）与
    /// <c>this.animation</c>（Spine 动画），位置读写经 <c>displayObject.x/y/width/height</c>。
    /// C# 移植改为纯逻辑字段 <see cref="_x"/>/<see cref="_y"/>/<see cref="_width"/>/
    /// <see cref="_height"/>，表现层通过端口同步，规则层不接触 GameObject/MonoBehaviour/FairyGUI。</para>
    ///
    /// <para><b>IAttackUnit 契约（task 5.2 AttackScheduler）：</b>
    /// 实现 <see cref="IAttackUnit"/> 接口，AttackScheduler 据此驱动单位攻击调度。
    /// 接口成员与 JS UnitBase 字段一一对应（见 AttackScheduler.cs:83-112 文档）。</para>
    ///
    /// <para><b>池化（task 4.1）：</b>
    /// 实现 <see cref="IPoolableBattleObject"/>，<see cref="ResetState"/> 清除全部可变状态，
    /// 回收后等价于新构造。子类 SoldierBase（本任务）/ 4 兵种（task 6.2）可 override
    /// 扩展 Reset 行为，但 MUST 调用 base.ResetState 保证基类字段清除。</para>
    ///
    /// <para><b>Acquire/Release Reset 契约（task 6.1）：</b>
    /// ResetState 后不得保留目标、冷却、事件、命令或表现引用。具体清理清单见
    /// <see cref="ResetState"/> 文档与还原工程 friendly-unit-pool-reset-contract.md。</para>
    ///
    /// <para><b>本类型为 internal abstract：</b>只供 GameBattle 内部 SoldierBase（本任务）
    /// 与 4 兵种（task 6.2）继承使用，不对其他程序集暴露，不可直接实例化。</para>
    /// </remarks>
    internal abstract class UnitBase : IAttackUnit, IPoolableBattleObject
    {
        // ====================================================================
        // 可变状态字段（对应 UnitBase.js:26-67 constructor）
        // ====================================================================

        // --- 标识与阵营 ---

        /// <summary>运行时 ID（对应 id，UnitBase.js:55）。由 UnitFactory.Acquire 分配。</summary>
        /// <remarks>
        /// 初始 -1（对应 JS <c>this.id = -1</c>）。池复用时由 AssignRuntimeId 分配新 ID，
        /// 保证不复用旧 ID（task 4.5 对称契约）。
        /// </remarks>
        private int _id;

        /// <summary>是否玩家方（对应 side/nm，UnitBase.js:57）。true=玩家方，false=对手方。</summary>
        private bool _side;

        /// <summary>单位显示名（对应 unitText/P_，UnitBase.js:56）。如 "刀"/"弓"/"枪"/"骑"。</summary>
        private string _unitText;

        /// <summary>是否在战场活动（对应 isActive/q_，UnitBase.js:58）。</summary>
        /// <remarks>
        /// <c>containerType == BATTLE</c> 时为 true（对应 JS <c>onMoved</c>）。
        /// AttackScheduler 守卫：<c>!IsActive</c> 的单位跳过攻击调度。
        /// </remarks>
        private bool _isActive;

        /// <summary>是否已回收（对应 inPool，UnitBase.js:60）。重复回收守卫。</summary>
        private bool _inPool;

        /// <summary>是否已销毁（对应 destroyed，UnitBase.js:59）。</summary>
        /// <remarks>
        /// gameOver 时置 true（对应 JS <c>this.destroyed = true</c>）。
        /// ResetState 置 false 以支持池复用。
        /// </remarks>
        private bool _destroyed;

        /// <summary>是否被禁用（对应 disabled/__，UnitBase.js:47）。</summary>
        /// <remarks>
        /// AttackScheduler 守卫：<c>Disabled</c> 的单位跳过攻击调度并切回 IDLE。
        /// 本期 Mob0 无 buff 禁用场景，字段保留供后续 buff 系统使用。
        /// </remarks>
        private bool _disabled;

        // --- 等级与经验 ---

        /// <summary>当前等级（对应 level，UnitBase.js:28）。本期固定 1（task 1.5 延后 UnitLevelService）。</summary>
        private int _level;

        /// <summary>当前经验（对应 experience/r_，UnitBase.js:29）。本期固定 0。</summary>
        private int _experience;

        // --- 位置与尺寸（纯逻辑，替代 displayObject.x/y/width/height） ---

        /// <summary>逻辑位置 X（对应 displayObject.x，C# 移植为纯逻辑字段）。</summary>
        private float _x;

        /// <summary>逻辑位置 Y（对应 displayObject.y）。</summary>
        private float _y;

        /// <summary>逻辑宽度（对应 displayObject.width，用于中心点计算）。</summary>
        private float _width;

        /// <summary>逻辑高度（对应 displayObject.height）。</summary>
        private float _height;

        // --- 攻击参数（由 SoldierBase 初始化，AttackScheduler 读取） ---

        /// <summary>攻击范围（对应 unit.attackRange，逻辑距离）。</summary>
        /// <remarks>
        /// JS 中 <c>attackRange = baseAttackRange + rangeBonusCells * gridWidth</c>，
        /// 即已含格子尺寸的像素距离。C# 移植保持同一语义：由 SoldierBase.InitializeStats
        /// 从配置 RangeCells * cellSize 计算并设置。
        /// </remarks>
        private float _attackRange;

        /// <summary>攻击冷却间隔（秒，对应 unit.attackIntervalSeconds）。</summary>
        private float _attackIntervalSeconds;

        /// <summary>上次攻击时间戳（毫秒，对应 unit.lastAttackTime/Wm）。</summary>
        /// <remarks>
        /// AttackScheduler 读写此字段：冷却完毕时写回 frameNowMs，冷却判断时读取。
        /// 读 frameNowMs（同帧所有子步不变，决策 0.9）。
        /// </remarks>
        private long _lastAttackTimeMs;

        // --- 状态机 ---

        /// <summary>当前单位状态（对应 currentState，UnitBase.js:52）。</summary>
        /// <remarks>
        /// 使用 AttackScheduler 定义的 <see cref="AttackUnitState"/>（Idle/Attack）。
        /// JS 的 NONE/PLACING 在 C# 由 <c>IsActive=false</c> 等价表达（AttackScheduler
        /// 守卫 <c>!IsActive</c> 跳过）。
        /// </remarks>
        private AttackUnitState _currentState;

        // --- 网格坐标与容器类型 ---

        /// <summary>当前网格 X（对应 gridPosition.x/u_，UnitBase.js:33）。-1 表示未放置。</summary>
        private int _gridX;

        /// <summary>当前网格 Y（对应 gridPosition.y）。-1 表示未放置。</summary>
        private int _gridY;

        /// <summary>上次网格 X（对应 previousGridPosition.x/c_，UnitBase.js:32）。</summary>
        private int _previousGridX;

        /// <summary>上次网格 Y（对应 previousGridPosition.y）。</summary>
        private int _previousGridY;

        // --- 生命周期代号 ---

        /// <summary>生命周期代号（对应 _lifecycleGeneration，UnitBase.js:61）。</summary>
        /// <remarks>
        /// 每次 Init 递增，供表现层回调守卫过期回调（对应 JS <c>generation !== this._lifecycleGeneration</c>）。
        /// ResetState 不重置（保留递增语义），但 Acquire 后 Init 会递增到新值。
        /// </remarks>
        private int _lifecycleGeneration;

        /// <summary>是否已 Configure（对应 _configured，UnitBase.js:62）。</summary>
        private bool _configured;

        // ====================================================================
        // IAttackUnit 只读属性（供 AttackScheduler 读取）
        // ====================================================================

        /// <summary>运行时 ID（对应 unit.id）。</summary>
        public int Id => _id;

        /// <inheritdoc/>
        /// <summary>是否在战场活动（对应 unit.isActive）。AttackScheduler 守卫字段。</summary>
        public bool IsActive => _isActive;

        /// <inheritdoc/>
        /// <summary>是否被禁用（对应 unit.disabled）。AttackScheduler 守卫字段。</summary>
        public bool Disabled => _disabled;

        /// <inheritdoc/>
        /// <summary>是否已回池（对应 unit.inPool）。AttackScheduler 守卫字段。</summary>
        public bool InPool => _inPool;

        /// <inheritdoc/>
        /// <summary>阵营：true=玩家方，false=对手方（对应 unit.side）。</summary>
        public bool Side => _side;

        /// <inheritdoc/>
        /// <summary>
        /// 逻辑中心 X（对应 <c>displayObject.x + width/2</c>）。
        /// <para>供 AttackScheduler 查询目标与攻击效果命中查询使用。</para>
        /// </summary>
        public float CenterX => _x + _width * 0.5f;

        /// <inheritdoc/>
        /// <summary>逻辑中心 Y（对应 <c>displayObject.y + height/2</c>）。</summary>
        public float CenterY => _y + _height * 0.5f;

        /// <inheritdoc/>
        /// <summary>攻击范围（对应 unit.attackRange，逻辑距离）。</summary>
        public float AttackRange => _attackRange;

        /// <inheritdoc/>
        /// <summary>攻击冷却间隔（秒，对应 unit.attackIntervalSeconds）。</summary>
        public float AttackIntervalSeconds => _attackIntervalSeconds;

        /// <inheritdoc/>
        /// <summary>上次攻击时间戳（毫秒，对应 unit.lastAttackTime）。可读写。</summary>
        /// <remarks>
        /// AttackScheduler 在冷却完毕时写回 frameNowMs（同帧固定，决策 0.9）。
        /// </remarks>
        public long LastAttackTimeMs
        {
            get => _lastAttackTimeMs;
            set => _lastAttackTimeMs = value;
        }

        /// <inheritdoc/>
        /// <summary>当前单位状态（对应 unit.currentState）。</summary>
        public AttackUnitState CurrentState => _currentState;

        // ====================================================================
        // 保护属性（供子类 SoldierBase / 4 兵种访问）
        // ====================================================================

        /// <summary>单位显示名（对应 unitText）。</summary>
        protected string UnitText => _unitText;

        /// <summary>
        /// 局内单位等级（最终方案"开放局内只读等级"）。
        /// <para>由 <see cref="SetUnitLevel"/> 设置，供表现层/等级服务读取。
        /// 等级变化通过等级服务应用数值倍率（伤害/攻速）。</para>
        /// </summary>
        internal int UnitLevel => _level;

        /// <summary>
        /// 设置局内单位等级（最终方案"初始化时接受指定等级"）。
        /// </summary>
        /// <param name="level">目标等级（至少 1）。</param>
        internal void SetUnitLevel(int level)
        {
            _level = level > 0 ? level : 1;
        }

        /// <summary>
        /// 导出攻击冷却状态（毫秒），供上下场时保存。
        /// </summary>
        internal long ExportAttackCooldown() => _lastAttackTimeMs;

        /// <summary>
        /// 导入攻击冷却状态（毫秒），供重新上场时恢复。
        /// </summary>
        /// <param name="lastAttackTimeMs">上次攻击时间戳（毫秒）。</param>
        internal void ImportAttackCooldown(long lastAttackTimeMs)
        {
            _lastAttackTimeMs = lastAttackTimeMs;
        }

        /// <summary>逻辑位置 X（供子类设置放置位置）。</summary>
        protected float XField
        {
            get => _x;
            set => _x = value;
        }

        /// <summary>逻辑位置 Y（供子类设置放置位置）。</summary>
        protected float YField
        {
            get => _y;
            set => _y = value;
        }

        /// <summary>逻辑宽度（供子类初始化设置）。</summary>
        protected float WidthField
        {
            get => _width;
            set => _width = value;
        }

        /// <summary>逻辑高度（供子类初始化设置）。</summary>
        protected float HeightField
        {
            get => _height;
            set => _height = value;
        }

        /// <summary>攻击范围（供子类初始化设置）。</summary>
        protected float AttackRangeField
        {
            get => _attackRange;
            set => _attackRange = value;
        }

        /// <summary>攻击冷却间隔（供子类初始化设置）。</summary>
        protected float AttackIntervalSecondsField
        {
            get => _attackIntervalSeconds;
            set => _attackIntervalSeconds = value;
        }

        /// <summary>提交 Buff 聚合后的禁用状态。</summary>
        protected void SetBuffDisabled(bool disabled)
        {
            _disabled = disabled;
        }

        /// <summary>当前网格 X（供 UnitRegistry 放置访问）。</summary>
        internal int GridX => _gridX;

        /// <summary>当前网格 Y（供 UnitRegistry 放置访问）。</summary>
        internal int GridY => _gridY;

        /// <summary>上次网格 X（诊断用）。</summary>
        internal int PreviousGridX => _previousGridX;

        /// <summary>上次网格 Y（诊断用）。</summary>
        internal int PreviousGridY => _previousGridY;

        /// <summary>生命周期代号（供表现层回调守卫过期回调）。</summary>
        internal int LifecycleGeneration => _lifecycleGeneration;

        /// <summary>是否已 Configure（诊断用）。</summary>
        internal bool Configured => _configured;

        // ====================================================================
        // 构造（对应 UnitBase.js:26-67 constructor）
        // ====================================================================

        /// <summary>
        /// 构造一个纯逻辑单位基类。字段初始化为默认值，需经 <see cref="Configure"/>
        /// 注入依赖后再由子类 <see cref="Init"/> 初始化状态。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitBase.constructor</c>（UnitBase.js:26-67）。
        /// 所有字段初始化为默认值，等价于"新构造"状态。</para>
        /// <para>池复用时 <see cref="ResetState"/> 将字段恢复到本构造后的状态，
        /// 保证池复用无污染（task 4.1）。</para>
        /// <para>本类型为 abstract，不可直接实例化。由子类 SoldierBase（本任务）/
        /// 4 兵种（task 6.2）的构造函数调用。</para>
        /// </remarks>
        protected UnitBase()
        {
            ResetToDefaults();
        }

        /// <summary>
        /// 把全部可变字段重置为默认值（等价于新构造后的状态）。
        /// </summary>
        /// <remarks>
        /// <para>供构造函数与 <see cref="ResetState"/> 共用，保证两处初始化一致。
        /// 对应还原工程 <c>_constructOnce</c> 与 <c>resetData</c> 的并集。</para>
        /// <para><b>Acquire/Release Reset 契约（task 6.1）：</b>
        /// 清除全部可变状态，使对象等价于新构造。具体清理：</para>
        /// <list type="bullet">
        /// <item>运行时 ID、阵营、显示名、活动/禁用/回池/销毁标记</item>
        /// <item>等级、经验</item>
        /// <item>位置、尺寸、网格坐标</item>
        /// <item>攻击范围、冷却间隔、上次攻击时间戳（冷却）</item>
        /// <item>当前状态（回到 Idle）</item>
        /// <item>生命周期代号保留递增语义（不重置为 0）</item>
        /// </list>
        /// <para>不重置 <see cref="_configured"/> —— Configure 由调用方在每次 Acquire 后重新调用。
        /// 但为支持"池复用无污染"，仍重置为 false，使 Acquire 后必须重新 Configure。</para>
        /// </remarks>
        private void ResetToDefaults()
        {
            // --- 标识与阵营 ---
            _id = -1;
            _side = true;
            _unitText = null;
            _isActive = false;
            _inPool = false;
            _destroyed = false;
            _disabled = false;

            // --- 等级与经验 ---
            _level = 1;
            _experience = 0;

            // --- 位置与尺寸 ---
            _x = 0f;
            _y = 0f;
            _width = 0f;
            _height = 0f;

            // --- 攻击参数 ---
            _attackRange = 0f;
            _attackIntervalSeconds = 1f;
            _lastAttackTimeMs = 0;

            // --- 状态机 ---
            _currentState = AttackUnitState.Idle;

            // --- 网格坐标 ---
            _gridX = -1;
            _gridY = -1;
            _previousGridX = -1;
            _previousGridY = -1;

            // --- Configure 标记（重置以强制重新 Configure） ---
            _configured = false;

            // 注：_lifecycleGeneration 不重置，保留递增语义。
        }

        // ====================================================================
        // Configure —— 注入运行时依赖（对应 UnitBase.js:69-94 configure）
        // ====================================================================

        /// <summary>
        /// 标记单位已注入运行时依赖。必须在 <see cref="Init"/> 之前调用。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitBase.configure({...})</c>（UnitBase.js:69-94）。
        /// C# 移植删除了 laya/gameData/gameLoop/eventBus/objectPool/presentation/audio
        /// 等 Laya/全局依赖（design 决策 5），规则层纯逻辑。具体攻击依赖（enemyManager、
        /// attackResolver、attackEffectManager、projectileManager）由 SoldierBase.Configure
        /// 注入（本任务同 sibling 文件）。</para>
        /// <para><b>不持有 Unity GameObject（task 6.1 约束）：</b>
        /// 还原工程 configure 注入 presentation/audio，C# 移植全部删除，规则层纯逻辑。</para>
        /// <para>本基类只标记 _configured=true，具体依赖由子类 Configure 覆写注入。</para>
        /// </remarks>
        protected void Configure()
        {
            _configured = true;
        }

        /// <summary>
        /// 校验已 Configure（对应 UnitBase.js:331-333 _requireConfigured）。
        /// </summary>
        /// <exception cref="InvalidOperationException">未 Configure。</exception>
        protected void RequireConfigured()
        {
            if (!_configured)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name}.Configure() 必须在 Init() 之前调用");
            }
        }

        // ====================================================================
        // AssignRuntimeId —— 由 UnitFactory.Acquire 调用分配新 ID
        // ====================================================================

        /// <summary>
        /// 写入运行时 ID。由 <see cref="UnitFactory"/>（task 6.3）在池取出后调用，
        /// 保证池复用不复用旧 ID（task 4.5 对称契约，与 EnemyBase.AssignRuntimeId 一致）。
        /// </summary>
        /// <param name="newId">新分配的运行时 ID（必须 > 0）。</param>
        /// <exception cref="ArgumentOutOfRangeException">newId &lt;= 0。</exception>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitBase.js:135 this.id = gameData.allocateRuntimeId()</c>。
        /// C# 移植将 ID 分配从 initialize 提到 Factory.Acquire，使池复用契约更显式
        /// （与 EnemyBase.AssignRuntimeId 模式一致，task 4.5）。</para>
        /// </remarks>
        protected internal void AssignRuntimeId(int newId)
        {
            if (newId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(newId), $"运行时 ID 必须 > 0，实际 {newId}");
            }

            _id = newId;
        }

        // ====================================================================
        // Init —— 初始化出生状态（对应 UnitBase.js:113-137 initialize）
        // ====================================================================

        /// <summary>
        /// 初始化单位状态：设置显示名、阵营、尺寸，进入 Idle 状态。
        /// </summary>
        /// <param name="unitText">单位显示名（如 "刀"/"弓"/"枪"/"骑"）。</param>
        /// <param name="side">阵营：true=玩家方，false=对手方。</param>
        /// <param name="width">逻辑宽度（用于中心点计算）。</param>
        /// <param name="height">逻辑高度。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitBase.initialize(unitText, side)</c>（UnitBase.js:113-137）。
        /// C# 移植将 ID 分配提到 Factory.Acquire（<see cref="AssignRuntimeId"/>），
        /// 表现对象创建删除（纯逻辑），gameLoop.register 删除（AttackScheduler 驱动）。</para>
        /// <para><b>生命周期代号递增（对应 JS _lifecycleGeneration += 1）：</b>
        /// 每次 Init 递增，供表现层回调守卫过期回调。池复用 Acquire 后 Init 递增到新代号，
        /// 旧代号回调因不匹配而被拒绝。</para>
        /// <para><b>状态初始化：</b>JS 根据 containerType 决定 NONE 或 IDLE。
        /// C# 移植统一进入 <see cref="AttackUnitState.Idle"/>（ IsActive=false 时
        /// AttackScheduler 守卫跳过，等价于 JS 的 NONE/PLACING）。</para>
        /// <para>子类（SoldierBase / 4 兵种）在 override 时应先调用 base.Init 再设置
        /// 攻击数值初始化（attackRange/attackIntervalSeconds 等）。</para>
        /// </remarks>
        protected internal virtual void Init(string unitText, bool side, float width, float height)
        {
            RequireConfigured();

            _lifecycleGeneration += 1;
            _inPool = false;
            _destroyed = false;
            _side = side;
            _unitText = unitText;
            _width = width;
            _height = height;

            // 进入 Idle 状态（对应 JS containerType!=BATTLE 时 IDLE）。
            //IsActive 在 OnMoved/Activate 时才置 true（对应 JS onMoved）。
            _currentState = AttackUnitState.Idle;
            _isActive = false;
        }

        // ====================================================================
        // 放置与激活（对应 UnitBase.js:102-157 setPlacement/activatePlacement/onMoved）
        // ====================================================================

        /// <summary>
        /// 设置网格坐标（对应 UnitBase.js:102-110 setPlacement）。
        /// </summary>
        /// <param name="gridX">网格 X。</param>
        /// <param name="gridY">网格 Y。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>setPlacement(containerType, gridX, gridY)</c>。
        /// C# 移植只记录网格坐标，containerType 由 UnitRegistry 管理（本期只有 BATTLE 场景）。</para>
        /// <para>记录 previousGridPosition 供移除时还原（对应 JS previousGridPosition）。</para>
        /// </remarks>
        internal void SetPlacement(int gridX, int gridY)
        {
            _previousGridX = _gridX;
            _previousGridY = _gridY;
            _gridX = gridX;
            _gridY = gridY;
        }

        /// <summary>
        /// 激活放置：设置逻辑像素位置并标记活动（对应 UnitBase.js:145-157 activatePlacement/onMoved）。
        /// </summary>
        /// <param name="pixelX">逻辑像素 X（由网格坐标 * cellSize 计算）。</param>
        /// <param name="pixelY">逻辑像素 Y。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>activatePlacement({parent, pixelX, pixelY})</c> +
        /// <c>onMoved()</c>。C# 移植删除 parent/addChild（表现层），只设置逻辑位置
        /// 并标记 <c>IsActive=true</c>（对应 JS <c>isActive = containerType==BATTLE</c>）。</para>
        /// <para>激活后进入 <see cref="AttackUnitState.Idle"/>，等待 AttackScheduler
        /// 在冷却完毕且有目标时切换到 Attack。</para>
        /// </remarks>
        internal void ActivatePlacement(float pixelX, float pixelY)
        {
            _x = pixelX;
            _y = pixelY;
            _isActive = true;
            SetState(AttackUnitState.Idle);
        }

        // ====================================================================
        // 状态机（对应 UnitBase.js:159-167 changeState/onExitState/onEnterState）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>
        /// 切换单位状态（对应 UnitBase.js:159-164 changeState）。
        /// </summary>
        /// <param name="state">目标状态。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>changeState(nextState)</c>：onExitState → currentState=next → onEnterState。
        /// C# 移植删除 <c>event('onStateChange')</c>（design 决策 4 直接调用），
        /// 状态切换通知由表现端口同步。</para>
        /// <para>AttackScheduler 在目标锁定/丢失时调用本方法切换 Idle/Attack。
        /// 子类 SoldierBase 可覆写 <see cref="OnEnterState"/>/<see cref="OnExitState"/>
        /// 扩展状态进入/退出行为（如播放攻击动画）。</para>
        /// </remarks>
        public void SetState(AttackUnitState state)
        {
            if (_currentState == state)
            {
                return;
            }

            OnExitState(_currentState);
            _currentState = state;
            OnEnterState(state);
        }

        /// <summary>状态退出回调（对应 UnitBase.js:166 onExitState）。子类可覆写扩展。</summary>
        /// <param name="previousState">退出的状态。</param>
        protected virtual void OnExitState(AttackUnitState previousState)
        {
        }

        /// <summary>状态进入回调（对应 UnitBase.js:167 onEnterState）。子类可覆写扩展。</summary>
        /// <param name="nextState">进入的状态。</param>
        protected virtual void OnEnterState(AttackUnitState nextState)
        {
        }

        // ====================================================================
        // Update —— 推进一帧（对应 UnitBase.js:181-183 update）
        // ====================================================================

        /// <summary>
        /// 推进一帧（对应 UnitBase.js:181-183 update）。基类为 no-op。
        /// </summary>
        /// <param name="deltaMs">子步时长（毫秒）。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitBase.update(deltaMs)</c> 为 no-op。
        /// SoldierBase 覆写为 <c>idle(deltaMs)</c>（SoldierBase.js:104-108），
        /// 本期 idle 同样为 no-op（攻击由 AttackScheduler 驱动，不由单位自身 Update）。</para>
        /// <para>本方法保留供 UnitRegistry 在每子步统一调用（如有需要），与 EnemyManager.update
        /// 模式对称。当前 AttackScheduler 已在 UnitAttack 阶段驱动攻击，单位自身无需 Update。</para>
        /// </remarks>
        internal virtual void Update(long deltaMs)
        {
            // 基类为 no-op（对应 UnitBase.js:181-183 update 为空）。
            // 参数保留以匹配子类签名；基类不使用 deltaMs。
            _ = deltaMs;
        }

        // ====================================================================
        // Attack —— IAttackUnit 契约，由具体兵种实现（task 6.2）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>
        /// 触发一次攻击（对应 unit.attack()），接收调度器为本次攻击选定的唯一初始目标。
        /// 由具体兵种（task 6.2）覆写，内部创建攻击效果/投射物。
        /// </summary>
        /// <param name="initialTarget">调度器为本次攻击选定的初始目标快照。</param>
        /// <remarks>
        /// <para><b>调用前保证（AttackScheduler）：</b>冷却完毕且存在目标。
        /// AttackScheduler 在 <c>ScheduleUnitAttack</c> 中守卫 <c>IsActive/Disabled/InPool</c>，
        /// 检查冷却，查询目标，确认有目标后把第一个目标作为本次攻击初始目标传入。</para>
        /// <para><b>子类实现职责：</b>具体兵种（KnifeSoldier/BowSoldier/SpearSoldier/CavalrySoldier，
        /// task 6.2）覆写本方法，内部：</para>
        /// <list type="number">
        /// <item>使用传入的初始目标创建攻击效果或计算朝向（不再独立二次查询目标）。</item>
        /// <item>创建攻击效果（MeleeAttackEffect/KnifeAttackEffect/PikeAttackEffect/
        ///   CavalrySweepEffect/ProjectileAttackEffect）。</item>
        /// <item>经 AttackEffectManager.Add 登记效果。</item>
        /// </list>
        /// <para>本基类不提供默认实现（abstract），强制子类覆写。</para>
        /// </remarks>
        public abstract void Attack(EnemyTargetDto initialTarget);

        // ====================================================================
        // GameOver —— 回收（对应 UnitBase.js:189-222 gameOver）
        // ====================================================================

        /// <summary>
        /// 强制结束并回收。幂等：重复调用返回 false（对应 UnitBase.js:189-222 gameOver）。
        /// </summary>
        /// <returns>true=首次回收成功；false=已回收。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>UnitBase.gameOver()</c>（UnitBase.js:189-222）。
        /// 还原工程 gameOver 回收表现对象、gameLoop.unregister、eventBus.offAllCaller、
        /// resetData、objectPool.recoverByClass。C# 移植：</para>
        /// <list type="bullet">
        /// <item>删除表现对象回收（纯逻辑，由 Presenter 通过端口同步）。</item>
        /// <item>删除 gameLoop.unregister/eventBus.offAllCaller（design 决策 4/5）。</item>
        /// <item>池回收由 UnitFactory.Release 统一处理（与 EnemyBase 模式一致）。</item>
        /// <item>本方法只标记 inPool/destroyed 与重置运行时状态，ResetState 由池 Release 调用。</item>
        /// </list>
        /// <para><b>幂等（EnemyBase.js:652 / UnitBase.js:190）：</b>已入池（inPool）返回 false。</para>
        /// <para><b>不在本方法中调用池 Release：</b>池回收由 UnitRegistry.Remove
        /// → UnitFactory.Release 统一处理（task 6.3）。本方法只标记 inPool 并重置运行时状态，
        /// 避免在 GameOver 内重入池回收导致重复 Release（与 EnemyBase.GameOver 模式一致）。</para>
        /// <para>子类（SoldierBase / 4 兵种）可 override 本方法扩展回收边界（如
        /// cancelOwner 攻击效果），但 MUST 调用 base.GameOver 保证基类回收逻辑执行。</para>
        /// </remarks>
        public virtual bool GameOver()
        {
            if (_inPool)
            {
                return false;
            }

            _isActive = false;
            _destroyed = true;
            _currentState = AttackUnitState.Idle;
            _lastAttackTimeMs = 0;

            // 标记已回池。池 Release 会随后调用 ResetState 完整清空状态。
            _inPool = true;
            return true;
        }

        // ====================================================================
        // IPoolableBattleObject.ResetState —— 池回收前重置（task 4.1 / task 6.1）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para><b>调用时机（task 4.1）：</b>由 <see cref="BattleObjectPool{T}.Release"/>
        /// 在归还对象前调用。Acquire 复用对象时不再调用。</para>
        ///
        /// <para><b>Acquire/Release Reset 契约（task 6.1 核心要求）：</b>
        /// 清除全部可变状态，使对象等价于新构造。回收后不得保留：
        /// </para>
        /// <list type="bullet">
        /// <item><b>目标引用：</b>本基类不持有目标引用（AttackScheduler 每次调度查询目标，
        ///   不缓存在单位上）。子类 SoldierBase 的 _targets 数组在子类 ResetState 清除。</item>
        /// <item><b>冷却时间戳：</b>_lastAttackTimeMs 重置为 0。</item>
        /// <item><b>事件/命令引用：</b>本基类不持有 EventBus 订阅（design 决策 4 直接调用）。
        ///   子类的攻击效果引用在子类 ResetState/GameOver 清除。</item>
        /// <item><b>表现引用：</b>本基类不持有 displayObject/animation（纯逻辑）。</item>
        /// <item><b>标识/阵营/状态：</b>_id=-1, _side=true, _isActive=false, _inPool=false,
        ///   _destroyed=false, _disabled=false, _currentState=Idle。</item>
        /// <item><b>位置/网格：</b>_x/_y/_width/_height=0, _gridX/_gridY=-1。</item>
        /// <item><b>攻击参数：</b>_attackRange=0, _attackIntervalSeconds=1（默认）。</item>
        /// </list>
        /// <para><b>幂等性：</b>多次调用安全，结果状态相同（IPoolableBattleObject 契约）。</para>
        /// <para><b>不抛出：</b>实现 MUST NOT 抛出异常（IPoolableBattleObject 契约）。</para>
        /// <para><b>子类扩展：</b>子类 SoldierBase / 4 兵种可 override 本方法扩展 Reset 行为，
        /// 但 MUST 调用 base.ResetState 保证基类字段清除。</para>
        /// <para><b>对应还原工程契约：</b>friendly-unit-pool-reset-contract.md 要求复位
        /// 运行时 ID、阵营、等级、攻击力、目标数组、攻击冷却、定时器、事件监听。
        /// 本基类清除基类字段；目标数组/攻击力/事件监听由 SoldierBase.ResetState 清除。</para>
        /// </remarks>
        public virtual void ResetState()
        {
            ResetToDefaults();
        }
    }
}
