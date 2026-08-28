using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 4.3：EnemyBase —— 纯逻辑敌人生命周期
    // ----------------------------------------------------------------------------
    // 职责（design.md:174 / Enemy/EnemyBase.cs）：
    //   纯逻辑敌人生命周期：初始化、沿路径移动、受击、接触目标、死亡、重置。
    //   规则层不持有 Unity GameObject 或表现组件。
    //
    // 来源证据（还原工程 EnemyBase.js:1-714）：
    //   - 路径移动：_advanceAlongPath(deltaMs) 按 path[currentPathIndex] 的网格坐标
    //     转换为像素坐标，沿方向以 baseMoveSpeed (px/s) 推进，到达后递增索引。
    //   - 空间格变化：_updateGridMembership() 以 (centerX, centerY) 计算所在格，
    //     与上次不同时更新并触发空间索引事件（C# 移植由 EnemyManager 直接调用刷新）。
    //   - 接触目标：_handlePathIndexChanged() 在 currentPathIndex >= length 时调用
    //     attackBattleTarget()，50ms 延迟后对 BattleTarget.receiveEnemyContact(1)。
    //   - 受击：hit(damage, attacker) 扣血、记录贡献者，血量归零进入 DEAD。
    //   - 死亡：_beginDeath() 触发 reward 1（普通）/10（特殊），回收表现由子类/工厂承担。
    //   - Reset：gameOver()/resetForPool() 清除全部可变字段，等价于新构造。
    //
    // 决策依据：
    //   - design.md 决策 5：删除 SingletonBase/EnemyEventProxy/CombatServices，
    //     EnemyBase 改为强类型注入的 internal 类，不再继承 Laya Sprite 或事件代理。
    //   - design.md 决策 4 / spec battle-event-boundary：敌人注册、空间索引、伤害、
    //     回收等一致性操作使用直接调用，不通过全局事件总线。原 EventBus 事件
    //     (ENEMY_GRID_LEFT/ENTERED/ENTITY_ENTERED/APPROACH/FINAL/KILLED_BY) 改为
    //     回调委托或直接调用 EnemyManager 方法。
    //   - design.md 第 9 行：逻辑层不依赖 MonoBehaviour、FairyGUI、Scene 对象或
    //     Time.deltaTime；表现通过端口和 Presenter 同步。
    //   - 决策 0.5：列优先 grid[x][y]，通过 GetCell/IsInside 访问。本类型不直接
    //     访问嵌套数组，路径点通过 MapData.GetPathForSide 获取只读 GridPosition 列表。
    //   - task 4.1：实现 IPoolableBattleObject 以支持池化，ResetState 清除全部可变状态。
    //   - task 4.3 约束：路径移动、空间格变化、接触目标、受击、死亡和 Reset；
    //     规则层不持有 Unity GameObject 或表现组件。
    //
    // 与并行任务的关系：
    //   - Mob0Enemy（task 4.4）继承本类，承接数值初始化与死亡表现边界，不移植灵魂投射/吹飞。
    //   - EnemyFactory（task 4.5）通过 BattleObjectPool<Mob0Enemy> 池化，Acquire 后
    //     调用 AssignRuntimeId 分配新 ID。本类提供 protected AssignRuntimeId 供子类继承。
    //   - EnemyManager（task 4.6）通过 IEnemyEntity 契约访问敌人，本类实现该接口。
    //   - BattleTarget（task 4.2）提供 receiveEnemyContact 入口，本类通过注入的
    //     IBattleContactTarget 回调委托接触伤害，不直接引用 BattleTarget 类型。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Unity GameObject/MonoBehaviour/Sprite/FairyGUI 组件。
    //   2. 池化友好：实现 IPoolableBattleObject，ResetState 后等价于新构造。
    //   3. 路径确定性：相同 stepMs 与路径产生相同位移，不依赖真实时间。
    //   4. 状态机：SPAWNING → MOVING → DEAD 为主路径，SKILL/STUNNED 本期不触发。
    //   5. 接触冷却：使用注入的 frameNowMs（同帧固定），不使用 stepMs。
    // ============================================================================
    //
    // 注：本类型不依赖 UnityEngine，纯逻辑对象可在 EditMode 无需 Scene 测试。
    // ============================================================================

    /// <summary>
    /// 敌人运行时状态枚举（对应 EnemyBase.js:12-18 EnemyRuntimeState）。
    /// </summary>
    /// <remarks>
    /// <para>数值和进入/退出行为来自 bundle.strings-decoded.js:19941-19962,20629-20654。
    /// 本期只覆盖 SPAWNING/MOVING/DEAD 主路径；SKILL/STUNNED 保留枚举但不触发。</para>
    /// </remarks>
    internal enum EnemyRuntimeState
    {
        /// <summary>出生中（对应 SPAWNING=0）。targetable=false，不参与移动与攻击。</summary>
        Spawning = 0,

        /// <summary>移动中（对应 MOVING=1）。沿路径推进，可被攻击。</summary>
        Moving = 1,

        /// <summary>技能中（对应 SKILL=2）。本期 Mob0 不触发，保留枚举。</summary>
        Skill = 2,

        /// <summary>眩晕中（对应 STUNNED=3）。本期 Mob0 不触发，保留枚举。</summary>
        Stunned = 3,

        /// <summary>已死亡（对应 DEAD=4）。targetable=false，等待回收。</summary>
        Dead = 4,
    }

    /// <summary>
    /// 敌人击杀奖励回调（对应还原工程 rewardService.onEnemyKilled + ENEMY_KILLED_BY 事件）。
    /// </summary>
    /// <remarks>
    /// <para>EnemyBase 在死亡点通过本委托提交奖励，避免直接引用 BattleEconomy/BattleState
    /// 与全局事件总线（design 决策 4：一致性操作使用直接调用）。</para>
    /// <para>参数：killedEnemyId、attackerId、experienceReward、isPlayerLane。
    /// experienceReward=10（特殊敌人）或 1（普通敌人），对应 EnemyBase.js:474,489。</para>
    /// </remarks>
    internal delegate void EnemyKilledHandler(int killedEnemyId, int attackerId, int experienceReward, bool isPlayerLane);

    /// <summary>
    /// 敌人死亡请求移除回调（对应还原工程 gameOver/回收通知）。
    /// </summary>
    /// <remarks>
    /// <para>EnemyBase 在血量归零进入 DEAD 或抵达路径终点时通过本委托通知管理器请求移除，
    /// 避免直接引用 EnemyManager（design 决策 4：一致性操作使用直接调用）。
    /// 由 BattleRuntimeFactory 装配时委托到 EnemyManager.RequestRemoveEnemy。</para>
    /// <para>参数：killedEnemyId（已死亡或到终点的敌人运行时 ID）、reason（移除原因，驱动
    /// 表现与回收语义，见 <see cref="EnemyRemovalReason"/>）。</para>
    /// </remarks>
    internal delegate void EnemyDeathRequestHandler(int killedEnemyId, EnemyRemovalReason reason);

    /// <summary>
    /// 纯逻辑敌人生命周期基类：初始化、沿路径移动、受击、接触目标、死亡、重置。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:174）：</b>纯逻辑敌人生命周期。规则层不持有 Unity GameObject
    /// 或表现组件，替代还原工程 <c>EnemyBase.js</c>（EnemyBase.js:1-714）。</para>
    ///
    /// <para><b>不持有表现组件（design.md 第 9 行）：</b>
    /// 还原工程 EnemyBase 持有 <c>this.visual</c>（Laya Sprite）作为表现节点，位置读写经
    /// <c>this.visual.x/y</c>。C# 移植改为纯逻辑字段 <see cref="_x"/>/<see cref="_y"/>，
    /// 表现层通过端口同步，规则层不接触 GameObject/MonoBehaviour/FairyGUI。</para>
    ///
    /// <para><b>路径移动（EnemyBase.js:332-353 _advanceAlongPath）：</b>
    /// 路径点为 <see cref="GridPosition"/> 网格坐标，通过 <see cref="MapData.GetPathForSide"/>
    /// 获取只读列表。像素坐标 = gridCoord * <see cref="_cellSize"/>（对应还原工程
    /// map.gridWidth=80）。沿当前路径点方向以 <see cref="_baseMoveSpeed"/>（px/s）推进，
    /// 到达后递增索引。决策 0.5：业务层不暴露嵌套数组，统一通过坐标 API 访问。</para>
    ///
    /// <para><b>空间格变化（EnemyBase.js:604-619 _updateGridMembership）：</b>
    /// 以中心点 (x + width/2, y + height/2) 计算所在格，与上次不同时更新 gridX/gridY。
    /// C# 移植由 <see cref="EnemyManager.RefreshCellIndex"/> 在 Update 后直接调用刷新
    /// 空间索引（design 决策 4：直接调用），不再经 EventBus 发送 GRID_LEFT/ENTERED 事件。</para>
    ///
    /// <para><b>终点攻击（EnemyBase.js:406-446 接触目标裁剪）：</b>
    /// 路径索引达到末尾（真正抵达路径终点）时调用 <see cref="AttemptEndPointAttackOnce"/>，
    /// 对本车道阿斗发起严格一次性的 1 点终点攻击（固定 <see cref="EndPointContactDamage"/>），
    /// 随后经 <see cref="_onDeathRequested"/> 请求以 <see cref="EnemyRemovalReason.ReachedEndPoint"/>
    /// 原因回收。不再依赖 500ms 接触冷却，也不再在 length-1 提前攻击。攻击在发生点同步生效，
    /// 符合 spec "伤害、死亡事实 MUST 在其发生点同步生效"。</para>
    ///
    /// <para><b>受击（EnemyBase.js:453-479 hit）：</b>
    /// 扣血、记录伤害贡献者，血量归零进入 DEAD 并通过 <see cref="_onEnemyKilled"/>
    /// 提交奖励。对应 ENEMY_KILLED_BY 事件改为直接回调（design 决策 4）。</para>
    ///
    /// <para><b>池化（task 4.1）：</b>
    /// 实现 <see cref="IPoolableBattleObject"/>，<see cref="ResetState"/> 清除全部可变状态，
    /// 回收后等价于新构造。子类 Mob0Enemy（task 4.4）可 override 扩展 Reset 行为。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 EnemyManager/EnemyFactory
    /// 与子类 Mob0Enemy 使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal class EnemyBase : IEnemyEntity, IPoolableBattleObject, IBuffTarget
    {
        private static readonly BuffTargetCapabilities BuffCapabilities =
            new BuffTargetCapabilities(
                new[]
                {
                    BuffNumericChannel.MoveSpeed,
                    BuffNumericChannel.MaxHealth,
                    BuffNumericChannel.CurrentHealth,
                },
                new[]
                {
                    BuffStateChannel.MovementDisabled,
                    BuffStateChannel.AttackDisabled,
                    BuffStateChannel.Suppressed,
                    BuffStateChannel.MovementLocked,
                });

        // ====================================================================
        // 常量（对应 EnemyBase.js:20-23）
        // ====================================================================

        /// <summary>基础移动速度（px/s，对应 ENEMY_BASE_SPEED=50，EnemyBase.js:20）。</summary>
        /// <remarks>
        /// 还原工程 speed=50 来自 BattleDataCore 硬编码，配置快照 EnemyConfigSnapshot.Speed=50。
        /// 本基类使用该默认值；子类 Mob0Enemy 可在初始化时覆盖。
        /// </remarks>
        protected internal const int BaseMoveSpeedDefault = 50;

        /// <summary>时间单位转换常量（毫秒/秒，对应 TIME_UNIT_MS=1000，EnemyBase.js:23）。</summary>
        protected internal const float TimeUnitMs = 1000f;

        /// <summary>终点攻击伤害值（固定 1，对应 BattleDataCore 硬编码接触伤害）。</summary>
        protected internal const int EndPointContactDamage = 1;

        /// <summary>到达当前路径点的距离阈值（对应 EnemyBase.js:339 distance &lt; 1）。</summary>
        protected internal const float PathPointReachedThreshold = 1f;

        // ====================================================================
        // 可变状态字段（对应 EnemyBase.js:80-131 _constructOnce）
        // ====================================================================

        // --- 标识与阵营 ---

        /// <summary>运行时 ID（对应 id，EnemyBase.js:83）。由 EnemyFactory.Acquire 分配。</summary>
        private int _id;

        /// <summary>是否玩家方车道（对应 isPlayerLane/nm，EnemyBase.js:81）。</summary>
        private bool _isPlayerLane;

        /// <summary>是否特殊敌人（对应 isSpecial/om，EnemyBase.js:85）。特殊敌人奖励 10，普通 1。</summary>
        private bool _isSpecial;

        // --- 生命 ---

        /// <summary>基础最大血量（对应 maxHealthBase/Km，EnemyBase.js:113）。</summary>
        private int _maxHealthBase;

        /// <summary>Buff 聚合后的有效最大血量。</summary>
        private int _maxHealth;

        /// <summary>当前血量（对应 currentHealth/mi/Zi，EnemyBase.js:114）。</summary>
        private int _currentHealth;

        /// <summary>上次提交的 CurrentHealth Buff 聚合修饰量。</summary>
        private int _healthBuffModifier;

        // --- 移动 ---

        /// <summary>逻辑位置 X（对应 visual.x，C# 移植为纯逻辑字段）。</summary>
        private float _x;

        /// <summary>逻辑位置 Y（对应 visual.y）。</summary>
        private float _y;

        /// <summary>逻辑宽度（对应 visual.width，用于中心点计算）。</summary>
        private float _width;

        /// <summary>逻辑高度（对应 visual.height）。</summary>
        private float _height;

        /// <summary>基础移动速度（px/s，对应 baseMoveSpeed/Sm，EnemyBase.js:97）。</summary>
        private int _baseMoveSpeed;

        /// <summary>Buff 聚合后的有效移动速度。</summary>
        private int _moveSpeed;

        /// <summary>当前路径点索引（对应 currentPathIndex/Lm，EnemyBase.js:92）。</summary>
        private int _currentPathIndex;

        /// <summary>上次路径点索引（对应 lastPathIndex/Hm，EnemyBase.js:117，用于检测索引变化）。</summary>
        private int _lastPathIndex;

        /// <summary>剩余路径距离（对应 remainingPathDistance/Bm，EnemyBase.js:102）。</summary>
        private float _remainingPathDistance;

        /// <summary>移动方向 X（对应 movementDirection.x/wm，EnemyBase.js:93）。</summary>
        private float _movementDirectionX;

        /// <summary>移动方向 Y（对应 movementDirection.y）。</summary>
        private float _movementDirectionY;

        /// <summary>是否停止移动（对应 stopMovement/Nm，EnemyBase.js:106，受 buff 影响）。</summary>
        private bool _stopMovement;

        private bool _buffMovementDisabled;
        private bool _buffAttackDisabled;
        private bool _buffSuppressed;
        private bool _buffMovementLocked;

        /// <summary>技能激活期间的移动暂停（由 Boss 技能成功激活时设置，不归 Buff 系统管理）。</summary>
        private bool _skillMovementPause;

        // --- 网格坐标 ---

        /// <summary>当前网格 X（对应 gridX/Aw，EnemyBase.js:115）。</summary>
        private int _gridX;

        /// <summary>当前网格 Y（对应 gridY/Ew，EnemyBase.js:116）。</summary>
        private int _gridY;

        /// <summary>上次网格 X（对应 previousGridX/Am，EnemyBase.js:100）。</summary>
        private int _previousGridX;

        /// <summary>上次网格 Y（对应 previousGridY/Em，EnemyBase.js:101）。</summary>
        private int _previousGridY;

        // --- 状态机 ---

        /// <summary>当前运行时状态（对应 currentState/curState，EnemyBase.js:107-108）。</summary>
        private EnemyRuntimeState _currentState;

        /// <summary>是否可被攻击（对应 targetable/rm，EnemyBase.js:82）。</summary>
        private bool _targetable;

        /// <summary>死亡是否已开始（对应 deathStarted/Cm，EnemyBase.js:103，防止重复触发）。</summary>
        private bool _deathStarted;

        // --- 终点攻击 ---

        /// <summary>
        /// 是否已尝试过终点攻击（严格一次性到达事件）。
        /// <para>即使目标已死亡、冻结或拒绝伤害，也视为已尝试；重置时复位。</para>
        /// </summary>
        private bool _hasAttemptedEndPointAttack;

        // --- 受击 ---

        /// <summary>伤害贡献者列表（对应 damageContributors/Ym，EnemyBase.js:104）。</summary>
        private readonly List<int> _damageContributors = new List<int>();

        // --- 池化标记 ---

        /// <summary>是否已回收（对应 inPool/pm，EnemyBase.js:88）。</summary>
        private bool _inPool;

        // ====================================================================
        // 注入依赖（Configure 时设置）
        // ====================================================================

        /// <summary>地图数据（只读），提供路径与坐标 API。</summary>
        private MapData _map;

        /// <summary>格子尺寸（px，对应 map.gridWidth=80）。路径点网格坐标 → 像素坐标的转换系数。</summary>
        private float _cellSize;

        /// <summary>终点攻击目标（IEnemyEndPointAttackTarget，按车道绑定阿斗，避免直接引用）。</summary>
        private IEnemyEndPointAttackTarget _endPointTarget;

        /// <summary>敌人击杀奖励回调（委托到 BattleEconomy/BattleManager，避免直接引用）。</summary>
        private EnemyKilledHandler _onEnemyKilled;

        /// <summary>敌人死亡请求移除回调（委托到 EnemyManager.RequestRemoveEnemy，避免直接引用）。</summary>
        private EnemyDeathRequestHandler _onDeathRequested;

        /// <summary>是否已 Configure（对应 _configured，EnemyBase.js:122）。</summary>
        private bool _configured;

        /// <summary>
        /// 血量变化回调（受击扣血成功后触发，供 EnemyManager 转发低频表现事实）。
        /// <para>参数依次为：运行时 ID / 当前血量 / 最大血量 / 变化量（负=受伤）/
        /// 显示伤害（本次 Hit 原始伤害，非 Hit 路径如 Buff 生命变化时为 0）。
        /// 由 <see cref="SetHealthChangedCallback"/> 注入，避免本类型直接引用 EnemyManager。</para>
        /// </summary>
        private Action<int, int, int, int, int> _onHealthChanged;

        // ====================================================================
        // IEnemyEntity 只读属性（供 EnemyManager 访问）
        // ====================================================================

        /// <inheritdoc/>
        public int Id => _id;

        /// <inheritdoc/>
        public bool IsPlayerLane => _isPlayerLane;

        /// <inheritdoc/>
        /// <remarks>以 int 暴露，避免与 EnemyManager 的 StateSpawning/StateDead 常量产生枚举耦合。</remarks>
        public int CurrentState => (int)_currentState;

        /// <inheritdoc/>
        public float X => _x;

        /// <inheritdoc/>
        public float Y => _y;

        /// <inheritdoc/>
        public float Width => _width;

        /// <inheritdoc/>
        public float Height => _height;

        /// <inheritdoc/>
        /// <remarks>默认 0（矩形左边缘为瞄准点 X）。子类按体型/命中锚点 override。</remarks>
        public virtual float ProjectileAimOffsetX => 0f;

        /// <inheritdoc/>
        /// <remarks>默认 0（矩形上边缘为瞄准点 Y）。子类按体型/命中锚点 override。</remarks>
        public virtual float ProjectileAimOffsetY => 0f;

        /// <inheritdoc/>
        public float RemainingPathDistance => _remainingPathDistance;

        /// <inheritdoc/>
        public int CurrentPathIndex => _currentPathIndex;

        /// <inheritdoc/>
        public int Health => _currentHealth;

        /// <inheritdoc/>
        /// <remarks>供表现层计算真实血量比例（current / max）。由子类 InitializeStats 设置。</remarks>
        public int MaxHealth => _maxHealth;

        BuffTargetHandle IBuffTarget.Handle =>
            new BuffTargetHandle(BuffEntityKind.Enemy, Id, BuffGeneration);

        bool IBuffTarget.IsAvailable => Id > 0
            && !_inPool
            && _currentState != EnemyRuntimeState.Dead;

        BuffTargetCapabilities IBuffTarget.Capabilities => BuffCapabilities;

        // ====================================================================
        // 保护属性（供子类 Mob0Enemy 访问）
        // ====================================================================

        /// <summary>当前运行时状态（枚举形式，供子类访问）。</summary>
        protected EnemyRuntimeState CurrentStateEnum => _currentState;

        /// <summary>是否已死亡。</summary>
        protected bool IsDead => _currentState == EnemyRuntimeState.Dead;

        /// <summary>基础最大血量（供子类初始化设置）。</summary>
        protected int MaxHealthBase
        {
            get => _maxHealthBase;
            set
            {
                _maxHealthBase = value;
                _maxHealth = value;
            }
        }

        /// <summary>当前血量（供子类访问，由 Init 设置）。</summary>
        /// <remarks>
        /// 子类（如 Mob0Enemy）在 Init 中通过 base.Init 设置当前血量，
        /// 不直接写 CurrentHealth（EnemyBase 未暴露 protected setter）。
        /// </remarks>
        protected int CurrentHealth => _currentHealth;

        /// <summary>是否特殊敌人（供子类初始化设置）。</summary>
        protected bool IsSpecial
        {
            get => _isSpecial;
            set => _isSpecial = value;
        }

        /// <summary>基础移动速度（供子类初始化设置）。</summary>
        protected int BaseMoveSpeed
        {
            get => _baseMoveSpeed;
            set
            {
                _baseMoveSpeed = value;
                _moveSpeed = value;
            }
        }

        /// <summary>当前类型可复用的租借世代；仅配置化敌人提供有效值。</summary>
        protected virtual long BuffGeneration => 0;

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

        /// <summary>逻辑位置 X（供子类设置出生位置）。</summary>
        protected float XField
        {
            get => _x;
            set => _x = value;
        }

        /// <summary>逻辑位置 Y（供子类设置出生位置）。</summary>
        protected float YField
        {
            get => _y;
            set => _y = value;
        }

        /// <summary>当前网格 X（供 EnemyManager 空间索引访问）。</summary>
        internal int GridX => _gridX;

        /// <summary>当前网格 Y（供 EnemyManager 空间索引访问）。</summary>
        internal int GridY => _gridY;

        /// <summary>是否已死亡开始（诊断用）。</summary>
        internal bool DeathStarted => _deathStarted;

        /// <summary>是否已入池（诊断用，对应 inPool）。</summary>
        /// <remarks>
        /// <para>供子类（如 ConfiguredEnemyBase）在 OnDeathPresentationCompleted 中守卫已回收对象
        /// （对应 NormalEnemyBase.js:88 <c>if (this.inPool) return</c>）。</para>
        /// </remarks>
        internal bool InPool => _inPool;

        internal int EffectiveMoveSpeedForTest => _moveSpeed;

        internal bool MovementStoppedForTest => _stopMovement;

        /// <summary>Buff 聚合的移动禁用状态是否生效（诊断/测试用）。</summary>
        internal bool BuffMovementDisabledForTest => _buffMovementDisabled;

        /// <summary>Buff 聚合的攻击禁用状态是否生效（诊断/测试用）。</summary>
        internal bool BuffAttackDisabledForTest => _buffAttackDisabled;

        /// <summary>
        /// 击杀奖励值（供 <see cref="Hit"/> 在死亡点提交，默认特殊 10 / 普通 1）。
        /// </summary>
        /// <remarks>
        /// <para>奖励提交仍由 <see cref="EnemyBase"/> 在死亡点唯一执行（不转移所有权）；
        /// 本虚属性只替换数值来源。配置化敌人（<see cref="ConfiguredEnemyBase"/>）按
        /// <c>EnemyDefinitionSnapshot.RewardGold</c> override，避免在基类复制奖励逻辑。</para>
        /// </remarks>
        protected virtual int KillRewardValue => _isSpecial ? 10 : 1;

        /// <summary>
        /// 终点攻击伤害值（供 <see cref="AttemptEndPointAttackOnce"/> 提交，默认 1）。
        /// </summary>
        /// <remarks>
        /// <para>终点攻击事实仍由 <see cref="EnemyBase"/> 严格一次性发起（不转移所有权）；
        /// 本虚属性只替换伤害数值来源。配置化敌人按 <c>EnemyDefinitionSnapshot.ContactDamage</c>
        /// override。</para>
        /// </remarks>
        protected virtual int EndPointAttackDamageValue => EndPointContactDamage;

        // ====================================================================
        // 构造（对应 EnemyBase.js:75-131 constructor + _constructOnce）
        // ====================================================================

        /// <summary>
        /// 构造一个纯逻辑敌人基类。字段初始化为默认值，需经 <see cref="Configure"/>
        /// 注入依赖后再由子类 <see cref="Init"/> 初始化状态。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>EnemyBase.constructor</c> + <c>_constructOnce</c>
        /// （EnemyBase.js:75-131）。所有字段初始化为默认值，等价于"新构造"状态。</para>
        /// <para>池复用时 <see cref="ResetState"/> 将字段恢复到本构造后的状态，
        /// 保证池复用无污染（task 4.1）。</para>
        /// </remarks>
        protected EnemyBase()
        {
            ResetToDefaults();
        }

        /// <summary>
        /// 把全部可变字段重置为默认值（等价于新构造后的状态）。
        /// </summary>
        /// <remarks>
        /// <para>供构造函数与 <see cref="ResetState"/> 共用，保证两处初始化一致。
        /// 对应还原工程 <c>_constructOnce</c> 与 <c>_prepareForSpawn</c> 的并集。</para>
        /// <para>不重置 <see cref="_damageContributors"/> 列表实例引用，只 Clear，
        /// 避免每次 Reset 创建新 List 产生 GC（task 4.1 池化友好）。</para>
        /// </remarks>
        private void ResetToDefaults()
        {
            _id = 0;
            _isPlayerLane = true;
            _isSpecial = false;
            _maxHealthBase = 0;
            _maxHealth = 0;
            _currentHealth = 0;
            _healthBuffModifier = 0;

            _x = 0f;
            _y = 0f;
            _width = 0f;
            _height = 0f;
            _baseMoveSpeed = BaseMoveSpeedDefault;
            _moveSpeed = BaseMoveSpeedDefault;
            _currentPathIndex = 0;
            _lastPathIndex = 0;
            _remainingPathDistance = float.PositiveInfinity;
            _movementDirectionX = 0f;
            _movementDirectionY = 0f;
            _stopMovement = false;
            _buffMovementDisabled = false;
            _buffAttackDisabled = false;
            _buffSuppressed = false;
            _buffMovementLocked = false;
            _skillMovementPause = false;

            _gridX = 0;
            _gridY = 0;
            _previousGridX = 0;
            _previousGridY = 0;

            _currentState = EnemyRuntimeState.Spawning;
            _targetable = false;
            _deathStarted = false;

            _hasAttemptedEndPointAttack = false;

            _damageContributors.Clear();

            _inPool = false;

            _map = null;
            _cellSize = 0f;
            _endPointTarget = null;
            _onEnemyKilled = null;
            _onDeathRequested = null;
            _configured = false;
            _onHealthChanged = null;
        }

        // ====================================================================
        // Configure —— 注入依赖（对应 EnemyBase.js:133-182 configure）
        // ====================================================================

        /// <summary>
        /// 注入运行时依赖。必须在 <see cref="Init"/> 之前调用。
        /// </summary>
        /// <param name="map">地图数据，提供路径与坐标 API（不可为 null）。</param>
        /// <param name="cellSize">格子尺寸（px，对应 map.gridWidth=80）。</param>
        /// <param name="endPointTarget">终点攻击目标（不可为 null，按车道绑定阿斗）。</param>
        /// <param name="onEnemyKilled">击杀奖励回调（不可为 null，委托到 BattleEconomy/BattleManager）。</param>
        /// <param name="onDeathRequested">死亡请求移除回调（不可为 null，委托到 EnemyManager）。</param>
        /// <exception cref="ArgumentNullException">任一必需参数为 null。</exception>
        /// <remarks>
        /// <para>对应还原工程 <c>EnemyBase.configure({...})</c>（EnemyBase.js:133-182）。
        /// C# 移植删除了 laya/eventBus/gameData/enemyFactory/objectPool/presentation/audio/
        /// effects/rewardService 等 Laya/全局依赖，只保留规则层必需的地图、终点攻击目标、
        /// 击杀奖励回调。表现层由 Presenter 通过端口同步，规则层不持有。</para>
        /// <para><b>不持有 Unity GameObject（task 4.3 约束）：</b>
        /// 还原工程 configure 注入 parentResolver/presentation/audio/effects，
        /// C# 移植全部删除，规则层纯逻辑。</para>
        /// </remarks>
        protected void Configure(
            MapData map,
            float cellSize,
            IEnemyEndPointAttackTarget endPointTarget,
            EnemyKilledHandler onEnemyKilled,
            EnemyDeathRequestHandler onDeathRequested)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (endPointTarget == null)
            {
                throw new ArgumentNullException(nameof(endPointTarget));
            }

            if (onEnemyKilled == null)
            {
                throw new ArgumentNullException(nameof(onEnemyKilled));
            }

            if (onDeathRequested == null)
            {
                throw new ArgumentNullException(nameof(onDeathRequested));
            }

            _map = map;
            _cellSize = cellSize > 0 ? cellSize : 80f;
            _endPointTarget = endPointTarget;
            _onEnemyKilled = onEnemyKilled;
            _onDeathRequested = onDeathRequested;
            _configured = true;
        }

        /// <summary>
        /// 注入血量变化回调。由 EnemyManager 在登记敌人后调用，受击扣血成功后触发。
        /// </summary>
        /// <param name="onHealthChanged">
        /// 血量变化回调（参数：运行时 ID / 当前血量 / 最大血量 / 变化量 / 显示伤害）。
        /// 传 null 表示清除回调（池回收时）。</param>
        /// <remarks>
        /// <para>不并入 <see cref="Configure"/>：Configure 在 spawn 时由外部反射调用，
        /// 而本回调在登记阶段（EnemyManager.Register）注入，二者职责分离。
        /// 回调只读血量值，不回写规则状态（design 决策 4：一致性操作使用直接调用）。</para>
        /// </remarks>
        internal void SetHealthChangedCallback(Action<int, int, int, int, int> onHealthChanged)
        {
            _onHealthChanged = onHealthChanged;
        }

        bool IBuffTarget.TryGetNumericBase(BuffNumericChannel channel, out double value)
        {
            switch (channel)
            {
                case BuffNumericChannel.MoveSpeed:
                    value = _baseMoveSpeed;
                    return true;
                case BuffNumericChannel.MaxHealth:
                    value = _maxHealthBase;
                    return true;
                case BuffNumericChannel.CurrentHealth:
                    value = 0d;
                    return true;
                default:
                    value = 0d;
                    return false;
            }
        }

        void IBuffTarget.CommitNumericAggregate(
            BuffNumericChannel channel,
            double effectiveValue,
            BuffSourceHandle source)
        {
            switch (channel)
            {
                case BuffNumericChannel.MoveSpeed:
                    _moveSpeed = ClampToInt(effectiveValue, 0);
                    break;
                case BuffNumericChannel.MaxHealth:
                    CommitMaximumHealth(ClampToInt(effectiveValue, 1), source.AttackerRuntimeId);
                    break;
                case BuffNumericChannel.CurrentHealth:
                    CommitCurrentHealthModifier(
                        ClampToInt(effectiveValue, int.MinValue),
                        source.AttackerRuntimeId);
                    break;
            }
        }

        void IBuffTarget.CommitStateAggregate(
            BuffStateChannel channel,
            bool active,
            BuffInstanceSnapshot payloadSource)
        {
            _ = payloadSource;
            switch (channel)
            {
                case BuffStateChannel.MovementDisabled:
                    _buffMovementDisabled = active;
                    break;
                case BuffStateChannel.AttackDisabled:
                    _buffAttackDisabled = active;
                    break;
                case BuffStateChannel.Suppressed:
                    _buffSuppressed = active;
                    break;
                case BuffStateChannel.MovementLocked:
                    _buffMovementLocked = active;
                    break;
                default:
                    return;
            }

            RecomputeStopMovement();
        }

        void IBuffTarget.ClearBuffAggregates()
        {
            int oldModifier = _healthBuffModifier;
            int missingHealth = Math.Max(0, _maxHealth - _currentHealth);
            _maxHealth = Math.Max(1, _maxHealthBase);
            if (_currentHealth > 0)
            {
                long withoutMaximumBuff = Math.Max(0, _maxHealth - missingHealth);
                _currentHealth = ClampHealth(withoutMaximumBuff - oldModifier);
            }

            _healthBuffModifier = 0;
            _moveSpeed = _baseMoveSpeed;
            _buffMovementDisabled = false;
            _buffAttackDisabled = false;
            _buffSuppressed = false;
            _buffMovementLocked = false;
            RecomputeStopMovement();
        }

        /// <summary>
        /// 设置技能激活期间的移动暂停（Boss 技能成功激活时置 true，完成/取消后置 false）。
        /// </summary>
        /// <remarks>
        /// <para>供 <see cref="BossBase"/> 在技能运行期间暂停移动；与 Buff 状态互不干扰，
        /// 恢复时只还原技能暂停位，不清除 Buff 聚合状态。重置时由 ResetState 清除。</para>
        /// </remarks>
        protected void SetSkillMovementPause(bool paused)
        {
            _skillMovementPause = paused;
            RecomputeStopMovement();
        }

        /// <summary>重算有效停止移动标志（Buff 控制通道 + 技能暂停）。</summary>
        private void RecomputeStopMovement()
        {
            _stopMovement = _buffMovementDisabled || _buffSuppressed || _buffMovementLocked || _skillMovementPause;
        }

        private void CommitMaximumHealth(int maximumHealth, int attackerId)
        {
            int previousHealth = _currentHealth;
            int missingHealth = Math.Max(0, _maxHealth - _currentHealth);
            _maxHealth = maximumHealth;
            _currentHealth = previousHealth <= 0
                ? 0
                : Math.Max(0, _maxHealth - missingHealth);
            CommitHealthChange(previousHealth, attackerId, 0);
        }

        private void CommitCurrentHealthModifier(int modifier, int attackerId)
        {
            int previousHealth = _currentHealth;
            long delta = (long)modifier - _healthBuffModifier;
            _healthBuffModifier = modifier;
            if (previousHealth > 0)
            {
                _currentHealth = ClampHealth((long)previousHealth + delta);
            }

            CommitHealthChange(previousHealth, attackerId, 0);
        }

        private int ClampHealth(long value)
        {
            if (value <= 0)
            {
                return 0;
            }

            return value >= _maxHealth ? _maxHealth : (int)value;
        }

        private static int ClampToInt(double value, int minimum)
        {
            if (value <= minimum)
            {
                return minimum;
            }

            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        // ====================================================================
        // AssignRuntimeId —— 由 EnemyFactory.Acquire 调用分配新 ID
        // ====================================================================

        /// <summary>
        /// 写入运行时 ID。由 <see cref="EnemyFactory.Acquire"/> 在池取出后调用，
        /// 保证池复用不复用旧 ID（task 4.5）。
        /// </summary>
        /// <param name="newId">新分配的运行时 ID（必须 > 0）。</param>
        /// <exception cref="ArgumentOutOfRangeException">newId &lt;= 0。</exception>
        /// <remarks>
        /// <para>对应还原工程 <c>EnemyBase.js:200 this.id = gameData.allocateRuntimeId()</c>。
        /// C# 移植将 ID 分配从 init 提到 Factory.Acquire，使池复用契约更显式。</para>
        /// <para>本方法为 protected internal，供子类 Mob0Enemy 继承并由 EnemyFactory 调用。</para>
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
        // Init —— 初始化出生状态（对应 EnemyBase.js:196-213 init）
        // ====================================================================

        /// <summary>
        /// 初始化出生状态：分配阵营、设置出生位置、进入 SPAWNING 状态。
        /// </summary>
        /// <param name="isPlayerLane">是否玩家方车道。</param>
        /// <param name="maxHealth">最大血量。</param>
        /// <param name="width">逻辑宽度（用于中心点计算）。</param>
        /// <param name="height">逻辑高度。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>EnemyBase.init(playerLane)</c>（EnemyBase.js:196-213）。
        /// C# 移植将 ID 分配提到 Factory.Acquire，本方法只负责出生状态初始化。</para>
        /// <para><b>出生位置计算（EnemyBase.js:279-288 _resolveSpawnCoordinates）：</b>
        /// 玩家方出生点 = playerEntry * cellSize；对手方 = opponentEntry * cellSize。
        /// 路径起点（playerStart/opponentStart）用于首段路径中心计算，本类型保留
        /// 出生位置 = entry * cellSize，与原工程一致。</para>
        /// <para>子类 Mob0Enemy 在 override 时应先调用 base.Init 再设置数值初始化。</para>
        /// </remarks>
        protected internal virtual void Init(bool isPlayerLane, int maxHealth, float width, float height)
        {
            RequireConfigured();

            _isPlayerLane = isPlayerLane;
            _maxHealthBase = maxHealth;
            _maxHealth = maxHealth;
            _currentHealth = maxHealth;
            _healthBuffModifier = 0;
            _moveSpeed = _baseMoveSpeed;
            _width = width;
            _height = height;

            // 出生位置：entry * cellSize（对应 EnemyBase.js:284-285）。
            GridPosition entry = isPlayerLane ? GetPlayerEntry() : GetOpponentEntry();
            _x = entry.X * _cellSize;
            _y = entry.Y * _cellSize;

            // 进入 SPAWNING 状态（对应 EnemyBase.js:203 changeState(SPAWNING)）。
            ChangeState(EnemyRuntimeState.Spawning);

            // 出生时立即计算所在格（对应 _updateGridMembership 首次调用）。
            UpdateGridMembership();
        }

        /// <summary>
        /// 开始移动：从 SPAWNING 切换到 MOVING（对应 _enterState MOVING）。
        /// </summary>
        /// <remarks>
        /// <para>还原工程在 SPAWNING → MOVING 时设置 targetable=true 并调用 startMovingAnimation。
        /// C# 移植由外部（EnemyManager/WaveManager）在适当时机调用本方法切换状态。</para>
        /// <para>本期不移植动画回调，startMoving/stopMoving 由表现端口承担。</para>
        /// </remarks>
        internal void BeginMoving()
        {
            if (_currentState == EnemyRuntimeState.Spawning)
            {
                ChangeState(EnemyRuntimeState.Moving);
            }
        }

        // ====================================================================
        // 路径访问（对应 EnemyBase.js:308-312 getPath）
        // ====================================================================

        /// <summary>
        /// 获取当前阵营的路径点只读列表（对应 EnemyBase.js:308-312 getPath）。
        /// </summary>
        /// <returns>路径点只读列表，通过 MapData.GetPathForSide 获取（决策 0.5）。</returns>
        /// <exception cref="InvalidOperationException">未 Configure 或路径为空。</exception>
        /// <remarks>
        /// <para>路径点为 <see cref="GridPosition"/> 网格坐标，像素坐标 = gridCoord * cellSize。
        /// 对应还原工程 <c>this.path = gameData.map.pathForSide(isPlayerLane)</c>。</para>
        /// <para>决策 0.5：业务层不暴露嵌套数组，统一通过 MapData 坐标 API 访问。</para>
        /// </remarks>
        private IReadOnlyList<GridPosition> GetPath()
        {
            RequireConfigured();
            return _map.GetPathForSide(_isPlayerLane);
        }

        /// <summary>获取玩家方入口坐标（对应 map.playerEntry）。</summary>
        private GridPosition GetPlayerEntry()
        {
            // MapData 当前未暴露 Entry 属性，使用 PlayerStart 近似（出生点）。
            // 注：还原工程 entry 与 start 不同（entry 为出生瞬间的位置，start 为首段路径起点），
            // 但本期 MapData 只暴露 PlayerStart/PlayerEnd/OpponentStart/OpponentEnd。
            // 出生位置使用 PlayerStart（与路径首点一致），保证敌人从路径起点出发。
            return _map.PlayerStart;
        }

        /// <summary>获取对手方入口坐标（对应 map.opponentEntry）。</summary>
        private GridPosition GetOpponentEntry()
        {
            return _map.OpponentStart;
        }

        // ====================================================================
        // 状态机（对应 EnemyBase.js:498-523 changeState/_enterState/_exitState）
        // ====================================================================

        /// <summary>
        /// 切换运行时状态（对应 EnemyBase.js:498-505 changeState）。
        /// </summary>
        /// <param name="nextState">目标状态。</param>
        /// <returns>true=状态已切换；false=状态相同未切换。</returns>
        /// <remarks>
        /// <para>进入/退出行为（EnemyBase.js:507-523）：</para>
        /// <list type="bullet">
        /// <item>SPAWNING 退出：targetable=true（EnemyBase.js:508）。</item>
        /// <item>MOVING 进入：startMovingAnimation（表现层，本期不移植）。</item>
        /// <item>DEAD 进入：targetable=false，beginDeath（EnemyBase.js:519-522）。</item>
        /// </list>
        /// </remarks>
        protected bool ChangeState(EnemyRuntimeState nextState)
        {
            if (_currentState == nextState)
            {
                return false;
            }

            EnemyRuntimeState previous = _currentState;

            // 退出行为（EnemyBase.js:507-512）。
            switch (previous)
            {
                case EnemyRuntimeState.Spawning:
                    // SPAWNING 退出时 targetable=true（EnemyBase.js:508）。
                    _targetable = true;
                    break;
                case EnemyRuntimeState.Moving:
                    // MOVING 退出时 stopMovingAnimation（表现层，本期不移植）。
                    break;
            }

            _currentState = nextState;

            // 进入行为（EnemyBase.js:514-523）。
            switch (nextState)
            {
                case EnemyRuntimeState.Spawning:
                    _targetable = false;
                    break;
                case EnemyRuntimeState.Moving:
                    // startMovingAnimation 委托表现层。
                    break;
                case EnemyRuntimeState.Dead:
                    _targetable = false;
                    BeginDeath();
                    break;
            }

            return true;
        }

        // ====================================================================
        // Update —— 推进一帧（对应 EnemyBase.js:314-320 update）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para>对应还原工程 <c>EnemyBase.update(deltaMs)</c>（EnemyBase.js:314-320）。
        /// 只在 MOVING 状态推进移动；其他状态不推进。</para>
        /// <para><b>stepMs 驱动移动（决策 0.9）：</b>deltaMs 为子步时长，驱动位移累计。</para>
        /// </remarks>
        public virtual void Update(long deltaMs)
        {
            if (deltaMs <= 0)
            {
                return;
            }

            if (_currentState == EnemyRuntimeState.Moving)
            {
                Move(deltaMs);
            }
        }

        // ====================================================================
        // Move —— 沿路径移动（对应 EnemyBase.js:322-330 move）
        // ====================================================================

        /// <summary>
        /// 沿路径移动一帧（对应 EnemyBase.js:322-330 move）。
        /// </summary>
        /// <param name="deltaMs">子步时长（毫秒）。</param>
        /// <remarks>
        /// <para>还原工程 move 内部包含 knockback/movementLocked 分支（EnemyBase.js:324-327）。
        /// 本期 Mob0 不移植 buff/knockback，只保留 _advanceAlongPath 主路径。</para>
        /// <para>路径索引变化时触发 <see cref="HandlePathIndexChanged"/>（对应 EnemyBase.js:328），
        /// 空间格变化由 <see cref="UpdateGridMembership"/> 处理（EnemyBase.js:329）。</para>
        /// </remarks>
        private void Move(long deltaMs)
        {
            _lastPathIndex = _currentPathIndex;

            if (!_stopMovement)
            {
                AdvanceAlongPath(deltaMs);
            }

            if (_lastPathIndex != _currentPathIndex)
            {
                HandlePathIndexChanged();
            }

            UpdateGridMembership();
        }

        // ====================================================================
        // AdvanceAlongPath —— 推进路径（对应 EnemyBase.js:332-353 _advanceAlongPath）
        // ====================================================================

        /// <summary>
        /// 沿当前路径点方向推进位移（对应 EnemyBase.js:332-353 _advanceAlongPath）。
        /// </summary>
        /// <param name="deltaMs">子步时长（毫秒）。</param>
        /// <remarks>
        /// <para>路径点为网格坐标，像素坐标 = gridCoord * cellSize（对应 EnemyBase.js:336-337
        /// point.x * map.gridWidth）。</para>
        /// <para>到达当前路径点（距离 &lt; 1px）时递增索引（EnemyBase.js:339-340）。
        /// 否则沿方向以 moveSpeed (px/s) * deltaMs/1000 推进（EnemyBase.js:347-348）。</para>
        /// <para>剩余路径距离 = 当前段距离 + 剩余段数 * cellSize（EnemyBase.js:351），
        /// 用于目标选择（closestToEnd 选距离最小者）。</para>
        /// </remarks>
        private void AdvanceAlongPath(long deltaMs)
        {
            IReadOnlyList<GridPosition> path = GetPath();
            if (_currentPathIndex < 0 || _currentPathIndex >= path.Count)
            {
                return;
            }

            if (path.Count < 2)
            {
                _currentPathIndex = path.Count;
                _remainingPathDistance = 0f;
                return;
            }

            float remainingStep = _moveSpeed * deltaMs / TimeUnitMs;
            while (remainingStep > 0f && _currentPathIndex < path.Count - 1)
            {
                GridPosition point = path[_currentPathIndex + 1];
                float targetX = point.X * _cellSize;
                float targetY = point.Y * _cellSize;
                float dx = targetX - _x;
                float dy = targetY - _y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);

                if (distance <= PathPointReachedThreshold || remainingStep >= distance)
                {
                    _x = targetX;
                    _y = targetY;
                    remainingStep = Math.Max(0f, remainingStep - distance);
                    _currentPathIndex += 1;
                    continue;
                }

                float dirX = dx / distance;
                float dirY = dy / distance;
                _movementDirectionX = dirX;
                _movementDirectionY = dirY;
                _x += dirX * remainingStep;
                _y += dirY * remainingStep;
                remainingStep = 0f;
            }

            if (_currentPathIndex >= path.Count - 1)
            {
                _currentPathIndex = path.Count;
                _remainingPathDistance = 0f;
                return;
            }

            GridPosition next = path[_currentPathIndex + 1];
            float nextDx = next.X * _cellSize - _x;
            float nextDy = next.Y * _cellSize - _y;
            float distanceToNext = (float)Math.Sqrt(nextDx * nextDx + nextDy * nextDy);
            int remainingSegments = path.Count - 2 - _currentPathIndex;
            _remainingPathDistance = distanceToNext + Math.Max(0, remainingSegments) * _cellSize;
        }

        // ====================================================================
        // HandlePathIndexChanged —— 路径索引变化处理（对应 EnemyBase.js:406-419）
        // ====================================================================

        /// <summary>
        /// 路径索引变化时触发接近警告、最终警告或接触目标
        /// （对应 EnemyBase.js:406-419 _handlePathIndexChanged）。
        /// </summary>
        /// <remarks>
        /// <para>还原工程在索引达到 length-3/length-2 时发送 ENEMY_APPROACH_WARNING/
        /// ENEMY_FINAL_WARNING 事件，达到 length-1 时调用 attackBattleTarget，
        /// 达到 length 时调用 gameOver。</para>
        /// <para>C# 移植（design 决策 4）：</para>
        /// <list type="bullet">
        /// <item>接近/最终警告事件改为表现端口同步，本期 EnemyBase 不发送事件。</item>
        /// <item>索引达到 length（真正抵达路径终点）时调用 <see cref="AttemptEndPointAttackOnce"/>
        /// 发起严格一次性的终点攻击并请求回收。</item>
        /// </list>
        /// </remarks>
        private void HandlePathIndexChanged()
        {
            IReadOnlyList<GridPosition> path = GetPath();
            int length = path.Count;

            if (_currentPathIndex >= length)
            {
                // 真正抵达路径终点：一次性终点攻击 + 请求以 ReachedEndPoint 原因回收
                // （对应 EnemyBase.js:411-418 索引达 length 时 gameOver，改为由管理器统一移除）。
                AttemptEndPointAttackOnce();
            }
            // 接近/最终警告（length-3/length-2）本期不发送事件，由表现端口自行检测。
            // 不再在 length-1 提前攻击：终点攻击是一次到达事件，只在真正抵达终点时触发。
        }

        // ====================================================================
        // AttemptEndPointAttackOnce —— 终点攻击（严格一次性到达事件）
        // ====================================================================

        /// <summary>
        /// 对本车道阿斗发起严格一次性的终点攻击（固定 1 点），随后请求回收。
        /// </summary>
        /// <remarks>
        /// <para><b>严格一次性：</b>仅通过 <see cref="_hasAttemptedEndPointAttack"/> 保证
        /// 只尝试一次。即使目标已死亡、冻结或拒绝伤害，也视为已经完成一次攻击尝试。
        /// 不再依赖 500ms 普通攻击冷却（终点接触是一次到达事件，必须只尝试一次）。</para>
        /// <para><b>攻击后立即回收：</b>攻击完成立即经 <see cref="_onDeathRequested"/> 请求
        /// 以 <see cref="EnemyRemovalReason.ReachedEndPoint"/> 原因移除，不继续停留、移动
        /// 或再次攻击。回收由 EnemyManager 统一完成（注销 + 表现通知 + 池归还）。</para>
        /// <para><b>同步生效：</b>攻击在发生点同步提交，符合 spec "伤害、死亡事实 MUST
        /// 在其发生点同步生效"。</para>
        /// </remarks>
        private void AttemptEndPointAttackOnce()
        {
            if (_hasAttemptedEndPointAttack)
            {
                return;
            }

            _hasAttemptedEndPointAttack = true;

            // 路径不足 2 点时不攻击（对应 EnemyBase.js:430 接触守卫）：退化路径没有
            // 真正的终点接触语义。但即使如此仍请求回收，避免敌人滞留在终点。
            IReadOnlyList<GridPosition> path = GetPath();
            if (path.Count >= 2)
            {
                _endPointTarget?.ReceiveEndPointAttack(
                    new EndPointAttackRequest(_id, _isPlayerLane, EndPointAttackDamageValue));
            }

            _onDeathRequested?.Invoke(_id, EnemyRemovalReason.ReachedEndPoint);
        }

        // ====================================================================
        // UpdateGridMembership —— 空间格变化检测（对应 EnemyBase.js:604-619）
        // ====================================================================

        /// <summary>
        /// 检测并更新所在空间格（对应 EnemyBase.js:604-619 _updateGridMembership）。
        /// </summary>
        /// <remarks>
        /// <para>以中心点 (x + width/2, y + height/2) 计算所在格（EnemyBase.js:606-607）。
        /// 与上次不同时更新 gridX/gridY 并记录 previousGridX/Y。</para>
        /// <para><b>C# 移植不发送事件（design 决策 4）：</b>
        /// 还原工程经 EventBus 发送 ENEMY_GRID_LEFT/ENEMY_GRID_ENTERED/
        /// ENEMY_GRID_ENTITY_ENTERED 事件。C# 移植由 EnemyManager 在 Update 后
        /// 直接调用 RefreshCellIndex 刷新空间索引，不经事件总线。</para>
        /// <para>格索引直接由当前中心点计算。确定性步进可能一次跨过格边界，不能再用
        /// “左上角必须贴近格原点”的表现层阈值阻止索引更新。</para>
        /// </remarks>
        private void UpdateGridMembership()
        {
            if (_cellSize <= 0)
            {
                return;
            }

            // 中心点所在格（EnemyBase.js:606-607）。
            float centerX = _x + _width / 2f;
            float centerY = _y + _height / 2f;
            int nextX = (int)Math.Floor(centerX / _cellSize);
            int nextY = (int)Math.Floor(centerY / _cellSize);

            if (nextX == _gridX && nextY == _gridY)
            {
                return;
            }

            _previousGridX = _gridX;
            _previousGridY = _gridY;
            _gridX = nextX;
            _gridY = nextY;
        }

        // ====================================================================
        // Hit —— 受击（对应 EnemyBase.js:453-479 hit）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para>对应还原工程 <c>EnemyBase.hit(damage, attacker)</c>（EnemyBase.js:453-479）。</para>
        /// <para><b>流程：</b></para>
        /// <list type="number">
        /// <item>已死亡（health &lt;= 0）返回 false（EnemyBase.js:454）。</item>
        /// <item>扣血，不低于 0（EnemyBase.js:460-461）。</item>
        /// <item>血量归零进入 DEAD 状态（EnemyBase.js:470）。</item>
        /// <item>记录伤害贡献者（EnemyBase.js:471-472）。</item>
        /// <item>血量归零时通过 <see cref="_onEnemyKilled"/> 提交奖励（EnemyBase.js:473-476）。</item>
        /// </list>
        /// <para><b>奖励值（EnemyBase.js:474）：</b>特殊敌人 10，普通敌人 1。</para>
        /// <para><b>贡献者去重（EnemyBase.js:472）：</b>同一攻击者只记录一次。</para>
        /// </remarks>
        public virtual bool Hit(int damage, int attackerId)
        {
            if (_currentHealth <= 0)
            {
                return false;
            }

            if (damage <= 0)
            {
                return false;
            }

            // 扣血，不低于 0（EnemyBase.js:460-461）。
            // displayDamage=damage：飘字显示 Hit 原始伤害（含过量伤害），而非实际生命 delta。
            int previousHealth = _currentHealth;
            _currentHealth = Math.Max(0, _currentHealth - damage);
            CommitHealthChange(previousHealth, attackerId, damage);

            return true;
        }

        /// <summary>
        /// takeDamage 别名（对应 EnemyBase.js:481 takeDamage）。
        /// </summary>
        internal bool TakeDamage(int damage, int attackerId) => Hit(damage, attackerId);

        private void CommitHealthChange(int previousHealth, int attackerId, int displayDamage)
        {
            if (_currentHealth < previousHealth
                && attackerId > 0
                && !_damageContributors.Contains(attackerId))
            {
                _damageContributors.Add(attackerId);
            }

            // 顺序契约：HealthChanged（含显示伤害）→ 死亡/击杀/移除。
            // displayDamage=0 表示非 Hit 路径（Buff 生命变化），表现层据此不飘字。
            _onHealthChanged?.Invoke(
                _id,
                _currentHealth,
                _maxHealth,
                _currentHealth - previousHealth,
                displayDamage);

            if (previousHealth <= 0 || _currentHealth > 0)
            {
                return;
            }

            ChangeState(EnemyRuntimeState.Dead);
            _onEnemyKilled?.Invoke(_id, attackerId, KillRewardValue, _isPlayerLane);
            _onDeathRequested?.Invoke(_id, EnemyRemovalReason.Killed);
        }

        // ====================================================================
        // BeginDeath —— 死亡开始（对应 EnemyBase.js:483-493 _beginDeath）
        // ====================================================================

        /// <summary>
        /// 死亡开始：标记 deathStarted 并提交奖励（对应 EnemyBase.js:483-493 _beginDeath）。
        /// </summary>
        /// <remarks>
        /// <para>还原工程 _beginDeath 触发 onDead 事件、rewardService.onEnemyKilled、
        /// audio.playDeath、effects.showDeath。C# 移植（design 决策 4）：</para>
        /// <list type="bullet">
        /// <item>奖励经 Hit 内的 _onEnemyKilled 已提交，本方法只标记 deathStarted。</item>
        /// <item>表现/音效由表现端口承担，规则层不调用。</item>
        /// </list>
        /// <para>本方法由 <see cref="ChangeState"/> 在进入 DEAD 时调用，幂等
        /// （deathStarted 防重复，EnemyBase.js:484）。</para>
        /// <para>子类（如 Mob0Enemy）可 override 本方法扩展死亡表现边界，但 MUST 调用
        /// base.BeginDeath 保证基类死亡逻辑执行。</para>
        /// </remarks>
        internal virtual void BeginDeath()
        {
            if (_deathStarted)
            {
                return;
            }

            _deathStarted = true;
        }

        // ====================================================================
        // IsTargetableBy —— 是否可被攻击（对应 EnemyBase.js:531-536）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para>对应还原工程 <c>EnemyBase.isTargetableBy(playerLane)</c>（EnemyBase.js:531-536）。
        /// 非 SPAWNING、非 DEAD、阵营匹配且 targetable 时返回 true。</para>
        /// </remarks>
        public bool IsTargetableBy(bool playerSide)
        {
            return _currentState != EnemyRuntimeState.Spawning
                && _currentState != EnemyRuntimeState.Dead
                && _isPlayerLane == playerSide
                && _targetable;
        }

        // ====================================================================
        // GameOver —— 回收（对应 EnemyBase.js:651-698 gameOver）
        // ====================================================================

        /// <summary>
        /// 强制结束并回收。幂等：重复调用返回 false（对应 EnemyBase.js:651-698 gameOver）。
        /// </summary>
        /// <returns>true=首次回收成功；false=已回收。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>EnemyBase.gameOver()</c>（EnemyBase.js:651-698）。
        /// 还原工程 gameOver 重置全部字段、回收表现节点、enemyFactory.recover。
        /// C# 移植将回收委托给 <see cref="EnemyFactory.Release"/>，本方法只负责状态重置。</para>
        /// <para><b>幂等（EnemyBase.js:652）：</b>已入池（inPool）返回 false。</para>
        /// <para><b>不在本方法中调用池 Release：</b>池回收由 EnemyManager.ForceRemove
        /// → EnemyFactory.Release 统一处理。本方法只标记 inPool 并重置状态，
        /// 避免在 GameOver 内重入池回收导致重复 Release。</para>
        /// </remarks>
        public virtual bool GameOver()
        {
            if (_inPool)
            {
                return false;
            }

            // 重置到回收状态（保留 _id 供 EnemyManager 注销，由 Release 时 ResetState 清除）。
            _targetable = false;
            _stopMovement = true;
            _remainingPathDistance = float.PositiveInfinity;
            _currentPathIndex = 0;
            _lastPathIndex = 0;
            _currentState = EnemyRuntimeState.Spawning;
            _deathStarted = false;
            _movementDirectionX = 0f;
            _movementDirectionY = 0f;
            _damageContributors.Clear();

            _inPool = true;
            return true;
        }

        // ====================================================================
        // IPoolableBattleObject.ResetState —— 池回收前重置（task 4.1）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para><b>调用时机（task 4.1）：</b>由 <see cref="BattleObjectPool{T}.Release"/>
        /// 在归还对象前调用。Acquire 复用对象时不再调用。</para>
        ///
        /// <para><b>完整性要求（还原工程 enemy-pool-reset-contract.md）：</b>
        /// 清除全部可变状态，使对象等价于新构造。包括：
        /// 运行时 ID、阵营、特殊标记、位置、路径索引、剩余路径距离、网格坐标、
        /// 生命、最大生命、速度、攻击冷却、接触回调标记、移动方向、死亡标记、
        /// 伤害贡献者数组、池化标记。</para>
        ///
        /// <para><b>幂等性：</b>多次调用安全，结果状态相同（IPoolableBattleObject 契约）。</para>
        ///
        /// <para><b>不抛出（IPoolableBattleObject 契约）：</b>实现 MUST NOT 抛出异常。</para>
        ///
        /// <para><b>子类扩展：</b>子类 Mob0Enemy 可 override 本方法扩展 Reset 行为，
        /// 但 MUST 调用 base.ResetState 保证基类字段清除。</para>
        /// </remarks>
        public virtual void ResetState()
        {
            ResetToDefaults();
        }

        // ====================================================================
        // 辅助
        // ====================================================================

        /// <summary>
        /// 校验已 Configure（对应 EnemyBase.js:702-704 _requireConfigured）。
        /// </summary>
        /// <exception cref="InvalidOperationException">未 Configure。</exception>
        private void RequireConfigured()
        {
            if (!_configured)
            {
                throw new InvalidOperationException("EnemyBase.Configure() 必须在 Init() 之前调用");
            }
        }
    }
}
