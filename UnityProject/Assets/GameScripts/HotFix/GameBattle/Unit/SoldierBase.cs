using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 6.1：SoldierBase —— 四种士兵共享攻击数值、冷却与目标范围契约的抽象基类
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Unit/SoldierBase.cs）：
    //   四种士兵共享攻击数值、冷却和目标范围契约。
    //   规则层不持有 Unity GameObject 或表现组件（design.md 第 9 行）。
    //
    // 来源证据（还原工程 SoldierBase.js:1-201）：
    //   SoldierBase 继承 UnitBase，持有：
    //     - objectType = 1
    //     - addAttackPower / rangeBonusCells / attackSpeedBonus / animationPlaybackRate
    //     - level = 1 / lastAttackTime = 0 / targets = [] / typeIndex = -1 / animationKey = null
    //   核心方法：
    //     - configure(options)：super.configure + 注入 enemyManager/attackTimeline/projectileManager
    //     - initializeUnit(text)：从 friendlyUnits.getByText 读取配置，设置 typeIndex/id/
    //       baseAttackRange/baseAttackPower/baseAttackIntervalSeconds/animationKey/createAnimation
    //     - levelUp(delta)：升级时重算 baseAttackIntervalSeconds/baseAttackPower
    //     - onEnterState(IDLE→playIdleAnimation, ATTACK→applyAttackPlaybackRate)
    //     - onExitState(ATTACK→onAttackStateExit)
    //     - update(deltaMs)：IDLE 态调 idle(deltaMs)（no-op）
    //     - addStatModifier(type, amount)：type 0=attackPower, 2=rangeCells, 1=attackSpeed
    //     - getStat(type)：返回 baseAttackPower/baseAttackRange/gridWidth/1
    //     - attackDamage getter：baseAttackPower + addAttackPower（对手方 * opponentAttackMultiplier）
    //     - attackRange getter：baseAttackRange + rangeBonusCells * gridWidth
    //     - attackIntervalSeconds getter：base / (1 + attackSpeedBonus)
    //     - resetData()：super.resetData + 清 rangeBonusCells/attackSpeedBonus/addAttackPower/
    //       animationPlaybackRate/lastAttackTime/targets/typeIndex/baseAttack*
    //     - gameOver()：cancelOwner + 清 animation/displayObject
    //     - receiveDamage()：显式抛错（友军无伤害契约）
    //
    // 决策依据：
    //   - design.md 决策 5：删除 SingletonBase/CombatServices，SoldierBase 改为强类型注入的
    //     internal 抽象类，不再继承 Laya Sprite 或持有 presentation/audio。
    //   - design.md 第 9 行：逻辑层不依赖 MonoBehaviour、FairyGUI、Scene 对象或
    //     Time.deltaTime；表现通过端口和 Presenter 同步。
    //   - design.md 决策 4 / spec battle-event-boundary：单位攻击、状态切换、回收等
    //     一致性操作使用直接调用，不通过全局事件总线。原 EventBus event('onLevelChange')
    //     改为直接方法调用或局部回调。
    //   - task 4.1：实现 IPoolableBattleObject（经 UnitBase 继承），ResetState 清除全部可变状态。
    //   - task 6.1 约束：实现纯逻辑 SoldierBase，回收后不得保留目标、冷却、事件、命令或
    //     表现引用。暴露攻击触发钩子供 task 6.2 兵种连接攻击效果。
    //   - task 1.5 延后：UnitLevelService/UnitMergeService 本期不创建。Level 固定 1，
    //     levelUp 不触发数值重算。addStatModifier/rangeBonusCells/attackSpeedMultiplier 保留
    //     字段但本期 Mob0 无 buff 场景，ResetState 仍清零保证池复用无污染。
    //
    // 与攻击效果的契约（task 5.4 MeleeAttackEffect.cs IAttackEffectOwner）：
    //   攻击效果在命中时读取所有者的位置、阵营与运行时 ID：
    //     - RuntimeId ← owner.id（作为 attackerId 透传给 IEnemyEntity.Hit）
    //     - Side ← owner.side（true=玩家方，用于目标查询阵营过滤）
    //     - CenterX/CenterY ← displayObject.x + width/2 等（命中查询中心）
    //   本类型实现 IAttackEffectOwner，供 MeleeAttackEffect/PikeAttackEffect/
    //   CavalrySweepEffect/ProjectileAttackEffect（task 5.4）读取。
    //
    // 与并行任务的关系：
    //   - 4 兵种（task 6.2）：KnifeSoldier/BowSoldier/SpearSoldier/CavalrySoldier
    //     继承本类，覆写 PerformAttack 创建各自攻击效果。本类提供通用攻击触发入口
    ///     <see cref="Attack"/>（IAttackUnit 契约），内部委托抽象 <see cref="PerformAttack"/>。
    //   - UnitFactory（task 6.3）：通过 BattleObjectPool<具体兵种> 池化，Acquire 后
    //     调用 AssignRuntimeId + Configure + Init + InitializeStats。
    //   - UnitRegistry（task 6.3）：管理单位注册/放置/移除。
    //   - AttackEffectManager（task 5.3，已实现）：士兵创建的攻击效果经此管理器推进。
    //   - AttackResolver（task 5.1，已实现）：士兵攻击效果经此查询目标与提交伤害。
    //   - EnemyManager（task 4.6，已实现）：攻击效果经此查询目标。
    //
    // C# 与 JS 的差异：
    //   1. createAnimation 删除：JS 从 presentation.createAnimation 创建 Spine 动画。
    //      C# 移植为纯逻辑层，不持有动画引用，表现由 Presenter 通过端口同步。
    //   2. attackTimeline/projectileManager 注入：JS 在 configure 注入。
    //      C# 移植改为 SoldierBase.Configure 注入 attackResolver/attackEffectManager/
    //      enemyManager/cellSize，具体投射物管理器由 4 兵种（task 6.2）各自 Configure 覆写注入。
    //   3. attackDamage 对手方倍率：JS <c>side ? value : value * opponentAttackMultiplier</c>。
    //      C# 移植保留倍率，由 <see cref="_opponentAttackMultiplier"/> 注入（本期固定 1，
    ///      BattleState.OpponentAttackMultiplier）。
    //   4. animationPlaybackRate：JS 计算并写入动画。C# 移植保留字段供表现端口读取，
    //      规则层不持有动画引用。
    //   5. attackIntervalSeconds getter 副作用：JS getter 内重算 animationPlaybackRate 并
    //      写入 animation。C# 移植改为只读计算（无副作用），animationPlaybackRate 由
    //      表现端口自行根据 AttackIntervalSeconds 推导。
    //   6. levelUp 数值重算：JS 调 friendlyUnits.resolveLevelStats 重算 baseAttack*。
    //      C# 本期 Level 固定 1（task 1.5 延后 UnitLevelService），levelUp 不重算数值。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Unity GameObject/MonoBehaviour/Sprite/FairyGUI/Spine 组件。
    //   2. 池化友好：ResetState（override）清除 SoldierBase 专属字段 + 调 base.ResetState。
    //   3. IAttackEffectOwner 契约：RuntimeId/Side/CenterX/CenterY 供攻击效果读取。
    //   4. Acquire/Release Reset 契约：ResetState 后无目标/冷却/攻击效果引用。
    //   5. Attack 触发：Attack() 守卫活动状态，委托 PerformAttack 供子类实现效果创建。
    //   6. 友军无伤害契约：士兵不暴露 HP/takeDamage/死亡状态（对应 JS receiveDamage 抛错）。
    // ============================================================================
    //
    // 注：本类型不依赖 UnityEngine，纯逻辑对象可在 EditMode 无需 Scene 测试。
    // ============================================================================

    /// <summary>
    /// 四种士兵共享攻击数值、冷却与目标范围契约的抽象基类（task 6.1）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>四种士兵共享攻击数值、冷却和目标范围契约。
    /// 替代还原工程 <c>SoldierBase.js</c>（SoldierBase.js:1-201）。</para>
    ///
    /// <para><b>继承 UnitBase（task 6.1）：</b>
    /// UnitBase 提供 IAttackUnit 契约（IsActive/Disabled/InPool/Side/CenterX/CenterY/
    /// AttackRange/AttackIntervalSeconds/LastAttackTimeMs/CurrentState/SetState/Attack）、
    /// 纯逻辑位置/网格坐标/生命周期代号、Init/Configure/AssignRuntimeId/GameOver/ResetState。
    /// 本类型覆写 <see cref="ResetState"/> 扩展 SoldierBase 专属字段清理，
    /// 覆写 <see cref="Attack"/> 提供通用攻击触发委托。</para>
    ///
    /// <para><b>IAttackEffectOwner 契约（task 5.4）：</b>
    /// 实现 <see cref="IAttackEffectOwner"/> 供攻击效果（MeleeAttackEffect 等）读取
    /// RuntimeId/Side/CenterX/CenterY。RuntimeId 委托 <see cref="UnitBase.Id"/>，
    /// Side 委托 <see cref="UnitBase.Side"/>，CenterX/CenterY 委托 UnitBase.CenterX/CenterY。</para>
    ///
    /// <para><b>攻击触发钩子（供 task 6.2 兵种连接）：</b>
    /// <see cref="Attack"/> 提供 IAttackUnit 契约入口，守卫活动状态后委托抽象
    /// <see cref="PerformAttack"/>。4 兵种（task 6.2）覆写 PerformAttack 创建各自
    /// 攻击效果（KnifeAttackEffect/ProjectileAttackEffect/PikeAttackEffect/CavalrySweepEffect）。</para>
    ///
    /// <para><b>池化（task 4.1）：</b>
    /// 经 UnitBase 继承 IPoolableBattleObject。本类型覆写 <see cref="ResetState"/>
    /// 清除 SoldierBase 专属字段（targets/addAttackPower/rangeBonusCells/attackSpeedMultiplier/
    /// typeIndex/baseAttack*），再调 base.ResetState 清除基类字段。</para>
    ///
    /// <para><b>本类型为 internal abstract：</b>只供 GameBattle 内部 4 兵种（task 6.2）
    /// 继承使用，不对其他程序集暴露，不可直接实例化。</para>
    /// </remarks>
    internal abstract class SoldierBase : UnitBase, IAttackEffectOwner
    {
        // ====================================================================
        // 可变状态字段（对应 SoldierBase.js:12-25 constructor）
        // ====================================================================

        // --- 攻击数值 ---

        /// <summary>基础攻击力（对应 baseAttackPower/m_，UnitBase.js:44）。由 InitializeStats 设置。</summary>
        private int _baseAttackPower;

        /// <summary>1 级基础攻击力（最终方案：保存 1 级基础值供 ApplyLevel 重算）。</summary>
        private int _baseAttackPowerLevel1;

        /// <summary>附加攻击力（对应 addAttackPower，SoldierBase.js:16）。本期 buff 暂缓，ResetState 清零。</summary>
        private int _addAttackPower;

        /// <summary>范围加成格数（对应 rangeBonusCells，SoldierBase.js:17）。本期 buff 暂缓，ResetState 清零。</summary>
        private float _rangeBonusCells;

        /// <summary>攻击速度倍率。1 表示基础攻速，值越大攻击间隔越短。</summary>
        private float _attackSpeedMultiplier = 1f;

        /// <summary>基础攻击范围（像素，对应 baseAttackRange/w_，UnitBase.js:45）。由 InitializeStats 设置。</summary>
        /// <remarks>
        /// JS 中 baseAttackRange = config.rangeCells * map.gridWidth。
        /// C# 移植由 InitializeStats 从配置 RangeCells * cellSize 计算并设置。
        /// </remarks>
        private float _baseAttackRange;

        /// <summary>基础攻击间隔（秒，对应 baseAttackIntervalSeconds/v_，UnitBase.js:46）。</summary>
        private float _baseAttackIntervalSeconds;

        /// <summary>1 级基础攻击间隔（秒，最终方案：保存 1 级基础值供 ApplyLevel 重算）。</summary>
        private float _baseAttackIntervalSecondsLevel1;

        /// <summary>等级数值服务（最终方案：由 ConfigureLevel 注入，供 ApplyLevel 重算倍率）。</summary>
        private UnitLevelService _levelService;

        // --- 目标与兵种标识 ---

        /// <summary>
        /// 当前目标列表（对应 targets/lx，SoldierBase.js:22）。
        /// AttackScheduler 每次调度查询目标，单位不缓存跨子步目标。
        /// </summary>
        /// <remarks>
        /// <para>保留列表供具体兵种在 <see cref="PerformAttack"/> 中二次查询与选择目标
        /// （对应 JS <c>this.targets = resolver.queryTargets(...)</c>）。</para>
        /// <para><b>池复用清理：</b>ResetState 清空列表（Clear，不释放实例引用，避免 GC）。</para>
        /// </remarks>
        private readonly List<EnemyTargetDto> _targets = new List<EnemyTargetDto>();

        /// <summary>兵种索引（对应 typeIndex，SoldierBase.js:24）。0=刀, 1=弓, 2=枪, 3=骑。-1 表示未初始化。</summary>
        private int _typeIndex;

        /// <summary>动画键（对应 animationKey，SoldierBase.js:25）。如 "knife"/"bow"/"pike"/"cavalry"。</summary>
        private string _animationKey;

        // --- 纯表现状态（供表现层读取，不持有 Unity 引用） ---

        /// <summary>角色本体是否朝右（弓兵攻击转向用，false=朝左）。</summary>
        private bool _bodyFacingRight;

        /// <summary>角色本体是否已设置朝向（弓兵攻击转向用，false=未攻击保持默认）。</summary>
        private bool _hasBodyFacing;

        /// <summary>武器是否已有朝向角度（枪兵武器瞄准用，false=无目标保持默认）。</summary>
        private bool _hasWeaponAim;

        /// <summary>武器瞄准角度（度，DisplayAngle 语义：0°朝上/90°朝右）。</summary>
        private float _weaponAimDegrees;

        // --- 注入依赖（Configure 时设置） ---

        /// <summary>敌人管理器（对应 enemyManager，SoldierBase.js:33）。供攻击效果查询目标。</summary>
        private EnemyManager _enemyManager;

        /// <summary>攻击解析服务（task 5.1）。供攻击效果查询目标与提交伤害。</summary>
        private AttackResolver _attackResolver;

        /// <summary>攻击效果管理器（task 5.3）。供士兵创建并登记攻击效果。</summary>
        private AttackEffectManager _attackEffectManager;

        /// <summary>格子尺寸（像素，对应 map.gridWidth=80）。攻击范围 = RangeCells * cellSize。</summary>
        private float _cellSize;

        /// <summary>对手方攻击倍率（对应 opponentAttackMultiplier）。本期固定 1。</summary>
        private int _opponentAttackMultiplier;

        // ====================================================================
        // IAttackEffectOwner 属性（供攻击效果读取）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>运行时 ID（对应 owner.id），作为 attackerId 透传给伤害提交。</summary>
        int IAttackEffectOwner.RuntimeId => Id;

        /// <inheritdoc/>
        /// <summary>阵营（对应 owner.side），用于目标查询阵营过滤。</summary>
        bool IAttackEffectOwner.Side => Side;

        /// <inheritdoc/>
        /// <summary>逻辑中心 X（对应 displayObject.x + width/2）。</summary>
        float IAttackEffectOwner.CenterX => CenterX;

        /// <inheritdoc/>
        /// <summary>逻辑中心 Y（对应 displayObject.y + height/2）。</summary>
        float IAttackEffectOwner.CenterY => CenterY;

        // ====================================================================
        // 保护属性（供 4 兵种 task 6.2 访问）
        // ====================================================================

        /// <summary>基础攻击力（供子类初始化设置）。</summary>
        protected int BaseAttackPower
        {
            get => _baseAttackPower;
            set => _baseAttackPower = value;
        }

        /// <summary>基础攻击范围（像素，供子类初始化设置）。</summary>
        protected float BaseAttackRange
        {
            get => _baseAttackRange;
            set => _baseAttackRange = value;
        }

        /// <summary>基础攻击间隔（秒，供子类初始化设置）。</summary>
        protected float BaseAttackIntervalSeconds
        {
            get => _baseAttackIntervalSeconds;
            set => _baseAttackIntervalSeconds = value;
        }

        /// <summary>兵种索引（供子类初始化设置）。</summary>
        protected int TypeIndex
        {
            get => _typeIndex;
            set => _typeIndex = value;
        }

        /// <summary>动画键（供子类初始化设置）。</summary>
        protected string AnimationKey
        {
            get => _animationKey;
            set => _animationKey = value;
        }

        /// <summary>角色本体是否朝右（供表现层读取；弓兵攻击时由 <see cref="SetBodyFacing"/> 设置）。</summary>
        internal bool BodyFacingRight => _bodyFacingRight;

        /// <summary>角色本体是否已设置朝向（供表现层读取；false=未攻击过，保持 Prefab 默认朝向）。</summary>
        internal bool HasBodyFacing => _hasBodyFacing;

        /// <summary>武器是否已有朝向角度（供表现层读取；枪兵攻击时由 <see cref="SetWeaponAim"/> 设置）。</summary>
        internal bool HasWeaponAim => _hasWeaponAim;

        /// <summary>武器瞄准角度（度，DisplayAngle 语义：0°朝上/90°朝右；供表现层读取）。</summary>
        internal float WeaponAimDegrees => _weaponAimDegrees;

        /// <summary>
        /// 设置角色本体朝向（弓兵攻击转向用）。
        /// </summary>
        /// <param name="facingRight">true=朝右；false=朝左。</param>
        /// <remarks>
        /// <para>纯表现状态：不持有 Unity 引用，只记录朝向供表现层同步。
        /// 由弓兵在 <see cref="BowSoldier.PerformAttack"/> 选中目标后调用。</para>
        /// </remarks>
        protected internal void SetBodyFacing(bool facingRight)
        {
            _bodyFacingRight = facingRight;
            _hasBodyFacing = true;
        }

        /// <summary>
        /// 设置武器瞄准角度（枪兵武器朝向用）。
        /// </summary>
        /// <param name="angleDegrees">角度（度，DisplayAngle 语义：0°朝上/90°朝右）。</param>
        /// <remarks>
        /// <para>纯表现状态：不持有目标引用或 Unity Transform，只记录角度供表现层同步。
        /// 由枪兵在 <see cref="SpearSoldier.PerformAttack"/> 选中目标后调用。</para>
        /// </remarks>
        protected internal void SetWeaponAim(float angleDegrees)
        {
            _weaponAimDegrees = angleDegrees;
            _hasWeaponAim = true;
        }

        /// <summary>当前攻击伤害（对应 SoldierBase.js:125-128 attackDamage getter）。</summary>
        /// <remarks>
        /// <para>对应 JS <c>attackDamage = (baseAttackPower + addAttackPower) * (side ? 1 : opponentAttackMultiplier)</c>。</para>
        /// <para>本期 opponentAttackMultiplier 固定 1（BattleState.OpponentAttackMultiplier），
        /// 故玩家方与对手方伤害相同。保留倍率字段供后续配置注入。</para>
        /// </remarks>
        protected int AttackDamage
        {
            get
            {
                int baseValue = _baseAttackPower + _addAttackPower;
                return Side ? baseValue : baseValue * _opponentAttackMultiplier;
            }
        }

        /// <summary>当前攻击伤害（测试访问器，供等级数值测试断言）。</summary>
        internal int AttackDamageForTest => AttackDamage;

        /// <summary>敌人管理器（供 4 兵种在 PerformAttack 中查询目标）。</summary>
        protected EnemyManager EnemyManager => _enemyManager;

        /// <summary>攻击解析服务（供 4 兵种创建攻击效果时注入）。</summary>
        protected AttackResolver AttackResolver => _attackResolver;

        /// <summary>攻击效果管理器（供 4 兵种创建并登记攻击效果）。</summary>
        protected AttackEffectManager AttackEffectManager => _attackEffectManager;

        /// <summary>格子尺寸（像素，供 4 兵种计算攻击范围与命中半径）。</summary>
        protected float CellSize => _cellSize;

        /// <summary>当前目标列表（供 4 兵种在 PerformAttack 中二次查询与选择目标）。</summary>
        /// <remarks>
        /// 供子类读取 AttackScheduler 已查询的目标，或在 PerformAttack 中自行查询后赋值。
        /// 对应 JS <c>this.targets</c>。
        /// </remarks>
        protected List<EnemyTargetDto> Targets => _targets;

        // ====================================================================
        // 构造（对应 SoldierBase.js:12-25 constructor）
        // ====================================================================

        /// <summary>
        /// 构造士兵基类。字段初始化为默认值，需经 <see cref="Configure"/> 注入依赖后
        /// 再由 <see cref="Init"/> + <see cref="InitializeStats"/> 初始化状态。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>SoldierBase.constructor</c>（SoldierBase.js:12-25）。
        /// 所有字段初始化为默认值，等价于"新构造"状态。</para>
        /// <para>池复用时 <see cref="ResetState"/> 将字段恢复到本构造后的状态。</para>
        /// <para>本类型为 abstract，不可直接实例化。由 4 兵种（task 6.2）构造函数调用。</para>
        /// </remarks>
        protected SoldierBase()
        {
            ResetSoldierDefaults();
        }

        /// <summary>
        /// 把 SoldierBase 专属可变字段重置为默认值（等价于新构造后的状态）。
        /// </summary>
        /// <remarks>
        /// <para>供构造函数与 <see cref="ResetState"/> 共用。基类字段由
        /// <see cref="UnitBase.ResetState"/> → ResetToDefaults 清除。</para>
        /// <para>不重置 <see cref="_targets"/> 列表实例引用，只 Clear，
        /// 避免每次 Reset 创建新 List 产生 GC（task 4.1 池化友好）。</para>
        /// </remarks>
        private void ResetSoldierDefaults()
        {
            _baseAttackPower = 0;
            _baseAttackPowerLevel1 = 0;
            _addAttackPower = 0;
            _rangeBonusCells = 0f;
            _attackSpeedMultiplier = 1f;
            _baseAttackRange = 0f;
            _baseAttackIntervalSeconds = 1f;
            _baseAttackIntervalSecondsLevel1 = 1f;

            _targets.Clear();

            _typeIndex = -1;
            _animationKey = null;
            _bodyFacingRight = false;
            _hasBodyFacing = false;
            _hasWeaponAim = false;
            _weaponAimDegrees = 0f;

            _enemyManager = null;
            _attackResolver = null;
            _attackEffectManager = null;
            _levelService = null;
            _cellSize = 0f;
            _opponentAttackMultiplier = 1;
        }

        // ====================================================================
        // Configure —— 注入运行时依赖（对应 SoldierBase.js:27-35 configure）
        // ====================================================================

        /// <summary>
        /// 注入士兵运行时依赖。必须在 <see cref="Init"/> 之前调用。
        /// </summary>
        /// <param name="enemyManager">敌人管理器（非 null），供攻击效果查询目标。</param>
        /// <param name="attackResolver">攻击解析服务（非 null），供攻击效果查询目标与提交伤害。</param>
        /// <param name="attackEffectManager">攻击效果管理器（非 null），供士兵创建并登记攻击效果。</param>
        /// <param name="cellSize">格子尺寸（像素，对应 map.gridWidth=80）。</param>
        /// <param name="opponentAttackMultiplier">对手方攻击倍率（本期固定 1）。</param>
        /// <exception cref="ArgumentNullException">任一必需参数为 null。</exception>
        /// <remarks>
        /// <para>对应还原工程 <c>SoldierBase.configure(options)</c>（SoldierBase.js:27-35）。
        /// C# 移植删除 presentation/audio/attackTimeline（表现层与双计时器，design 决策 5/4），
        /// 改为注入 attackResolver/attackEffectManager（task 5.1/5.3 产物）。</para>
        /// <para><b>具体兵种扩展（task 6.2）：</b>4 兵种可覆写本方法扩展注入
        /// （如 BowSoldier 注入 projectileManager），但 MUST 调用 base.Configure。</para>
        /// <para><b>池复用安全：</b>ResetState 清空全部注入依赖，每次 Acquire 后必须重新 Configure。</para>
        /// </remarks>
        protected void Configure(
            EnemyManager enemyManager,
            AttackResolver attackResolver,
            AttackEffectManager attackEffectManager,
            float cellSize,
            int opponentAttackMultiplier)
        {
            if (enemyManager == null)
            {
                throw new ArgumentNullException(nameof(enemyManager));
            }

            if (attackResolver == null)
            {
                throw new ArgumentNullException(nameof(attackResolver));
            }

            if (attackEffectManager == null)
            {
                throw new ArgumentNullException(nameof(attackEffectManager));
            }

            base.Configure();

            _enemyManager = enemyManager;
            _attackResolver = attackResolver;
            _attackEffectManager = attackEffectManager;
            _cellSize = cellSize > 0 ? cellSize : 80f;
            _opponentAttackMultiplier = opponentAttackMultiplier > 0 ? opponentAttackMultiplier : 1;
        }

        // ====================================================================
        // ConfigureLevel —— 注入等级数值服务（最终方案）
        // ====================================================================

        /// <summary>
        /// 注入等级数值服务，供 <see cref="ApplyLevel"/> 重算伤害/攻速倍率。
        /// </summary>
        /// <param name="levelService">等级数值服务（可为 null；null 时等级应用退化为无倍率）。</param>
        /// <remarks>
        /// 由 <see cref="UnitFactory.Acquire"/> 在 Configure 之后调用（四兵统一注入）。
        /// </remarks>
        protected internal void ConfigureLevel(UnitLevelService levelService)
        {
            _levelService = levelService;
        }

        // ====================================================================
        // InitializeStats —— 从配置初始化攻击数值
        // --------------------------------------------------------------------
        // 对应 SoldierBase.js:37-49 initializeUnit（从 friendlyUnits.getByText 读取配置）。
        // C# 移植改为从强类型 UnitConfigSnapshot 读取，不依赖 gameData 单例。
        // ====================================================================

        /// <summary>
        /// 从配置快照初始化攻击数值：设置兵种索引、攻击范围、攻击力、攻击间隔、动画键。
        /// </summary>
        /// <param name="config">单位配置快照（task 3.3 UnitConfigSnapshot）。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>SoldierBase.initializeUnit(text)</c>（SoldierBase.js:37-49）。
        /// JS 从 <c>gameData.friendlyUnits.getByText(text)</c> 读取配置，C# 移植从
        /// 强类型 <see cref="UnitConfigSnapshot"/> 读取。</para>
        /// <para><b>数值计算：</b></para>
        /// <list type="bullet">
        /// <item>typeIndex = config.Index（0=刀, 1=弓, 2=枪, 3=骑）</item>
        /// <item>baseAttackRange = config.RangeCells * cellSize（像素距离）</item>
        /// <item>baseAttackPower = config.AttackDamage</item>
        /// <item>baseAttackIntervalSeconds = config.AttackIntervalSeconds</item>
        /// <item>animationKey = config.AnimationKey</item>
        /// </list>
        /// <para><b>同步 UnitBase 字段：</b>本方法同步设置 UnitBase 的 AttackRangeField/
        /// AttackIntervalSecondsField，使 IAttackUnit 契约返回正确值。
        /// AttackRange = baseAttackRange + rangeBonusCells * cellSize（本期 rangeBonusCells=0）。
        /// AttackIntervalSeconds = baseAttackIntervalSeconds / attackSpeedMultiplier。
        /// 初始 attackSpeedMultiplier=1。</para>
        /// <para><b>调用顺序：</b>UnitFactory.Acquire → AssignRuntimeId → Configure →
        /// Init(unitText, side, width, height) → InitializeStats(config) →
        /// SetPlacement/ActivatePlacement（task 6.3）。</para>
        /// <para><b>池复用安全：</b>ResetState 清空全部数值，每次 Acquire 后必须重新调用本方法。</para>
        /// </remarks>
        protected internal virtual void InitializeStats(UnitConfigSnapshot config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            RequireConfigured();

            _typeIndex = config.Index;
            _animationKey = config.AnimationKey;
            _baseAttackPower = config.AttackDamage;
            _baseAttackPowerLevel1 = config.AttackDamage;
            _baseAttackRange = config.RangeCells * _cellSize;
            _baseAttackIntervalSeconds = config.AttackIntervalSeconds;
            _baseAttackIntervalSecondsLevel1 = config.AttackIntervalSeconds;

            // 同步 UnitBase 的 IAttackUnit 契约字段。
            // AttackRange = baseAttackRange + rangeBonusCells * cellSize（本期 rangeBonusCells=0）。
            AttackRangeField = _baseAttackRange + _rangeBonusCells * _cellSize;
            // AttackIntervalSeconds = base / attackSpeedMultiplier（初始为 1）。
            AttackIntervalSecondsField = ComputeAttackIntervalSeconds();
        }

        // ====================================================================
        // ApplyLevel —— 应用等级数值（最终方案）
        // ====================================================================

        /// <summary>
        /// 按当前等级立即重算伤害与攻击间隔（最终方案"统一应用等级数值"）。
        /// </summary>
        /// <param name="level">目标等级（1..MaxLevel）。</param>
        /// <remarks>
        /// <para><b>公式（最终方案）：</b></para>
        /// <code>
        /// damage   = baseDamage(1 级) × DamageMultiplier[level-1]
        /// interval = baseInterval(1 级) ÷ AttackSpeedMultiplier[level-1]
        /// </code>
        /// <para><b>整数伤害四舍五入：</b>C# 整数伤害统一采用
        /// <see cref="UnitLevelService.ResolveDamage"/>（MidpointRounding.AwayFromZero），
        /// 避免各兵种自行转换。</para>
        /// <para><b>同步 UnitBase 等级字段：</b>调用 <see cref="UnitBase.SetUnitLevel"/>。
        /// 上下场不重置已有攻击冷却（冷却由 UnitBase 单独导入导出）。</para>
        /// </remarks>
        internal void ApplyLevel(int level)
        {
            SetUnitLevel(level);

            int damage = _baseAttackPowerLevel1;
            float interval = _baseAttackIntervalSecondsLevel1;
            if (_levelService != null)
            {
                damage = _levelService.ResolveDamage(_baseAttackPowerLevel1, level);
                interval = _levelService.ResolveAttackInterval(_baseAttackIntervalSecondsLevel1, level);
            }

            // 基础攻击力（供 AttackDamage 计算）。_addAttackPower 本期恒 0。
            _baseAttackPower = damage;
            _baseAttackIntervalSeconds = interval;
            AttackRangeField = _baseAttackRange + _rangeBonusCells * _cellSize;
            AttackIntervalSecondsField = ComputeAttackIntervalSeconds();
        }

        /// <summary>
        /// 取消尚未释放的攻击（近战延迟效果、弓兵发射延迟等），保留已发射投射物。
        /// </summary>
        /// <remarks>
        /// <para><b>最终方案"上下场或成为合并源时取消尚未释放的攻击"：</b></para>
        /// <para>单位上下场或成为合并源被移除时，尚未结算的攻击（如近战延迟效果、
        /// <see cref="BowReleaseEffect"/> 释放延迟）全部取消；已发射的箭矢由
        /// <see cref="ProjectileManager"/> 继续推进，不因源单位回池而消失。</para>
        /// <para>经 <see cref="AttackEffectManager.CancelOwner"/> 取消全部以本单位为
        /// Owner 的效果。取消的是未结算效果（Cancel 不造成伤害），不触碰已发射投射物
        /// （ProjectileAttackEffect.Cancel 会在发射后解除对源单位的引用，见 task 5.8）。</para>
        /// </remarks>
        internal void CancelUnreleasedAttacks()
        {
            if (_attackEffectManager != null)
            {
                _attackEffectManager.CancelOwner(this, "unit-slot-moved");
            }
        }

        /// <summary>
        /// 计算当前攻击间隔（秒，对应 SoldierBase.js:134-141 attackIntervalSeconds getter）。
        /// </summary>
        /// <returns>攻击间隔（秒）= baseAttackIntervalSeconds / attackSpeedMultiplier。</returns>
        /// <remarks>
        /// <para>逻辑层只维护攻击间隔；表现层通过同步该值计算攻击动画逐帧时长。</para>
        /// </remarks>
        private float ComputeAttackIntervalSeconds()
        {
            float multiplier = _attackSpeedMultiplier > 0f ? _attackSpeedMultiplier : 1f;
            return _baseAttackIntervalSeconds / multiplier;
        }

        /// <summary>
        /// 设置当前攻击速度倍率，并立即重算有效攻击间隔。
        /// </summary>
        /// <remarks>
        /// <para>供升级、Buff 等运行时数值系统调用。表现层在下一次同步时读取
        /// <see cref="UnitBase.AttackIntervalSeconds"/>，使后续攻击动画和出箭时序使用新倍率。</para>
        /// </remarks>
        /// <param name="multiplier">攻击速度倍率；小于等于 0 时按 1 处理。</param>
        internal void SetAttackSpeedMultiplier(float multiplier)
        {
            _attackSpeedMultiplier = multiplier > 0f ? multiplier : 1f;
            AttackIntervalSecondsField = ComputeAttackIntervalSeconds();
        }

        // ====================================================================
        // Attack —— IAttackUnit 契约入口，通用攻击触发（对应各兵种 attack()）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>
        /// 触发攻击（IAttackUnit 契约）。守卫活动状态后委托抽象 <see cref="PerformAttack"/>。
        /// </summary>
        /// <remarks>
        /// <para><b>调用前保证（AttackScheduler，task 5.2）：</b>
        /// AttackScheduler 在 <c>ScheduleUnitAttack</c> 中已守卫 IsActive/Disabled/InPool，
        /// 检查冷却完毕，查询目标确认存在，然后调用本方法。本方法内不重复冷却判断。</para>
        ///
        /// <para><b>通用攻击触发（对应 JS 各兵种 <c>attack()</c>）：</b>
        /// JS 中 KnifeSoldier.attack()→performKnifeAttack()、BowSoldier.attack()→selectTarget+launch、
        /// SpearSoldier.attack()→create PikeAttackEffect、CavalrySoldier.attack()→create 2 CavalrySweepEffect。
        /// C# 移植将通用守卫提取到本方法，具体效果创建委托抽象 <see cref="PerformAttack"/>。</para>
        ///
        /// <para><b>子类实现职责（task 6.2）：</b>4 兵种覆写 <see cref="PerformAttack"/>：
        /// </para>
        /// <list type="number">
        /// <item>查询目标（经 AttackResolver.QueryTargets，二次查询是各兵种既定行为）。</item>
        /// <item>创建攻击效果（MeleeAttackEffect/KnifeAttackEffect/PikeAttackEffect/
        ///   CavalrySweepEffect/ProjectileAttackEffect，task 5.4）。</item>
        /// <item>经 AttackEffectManager.Add 登记效果（含回收委托）。</item>
        /// </list>
        /// </remarks>
        public override void Attack()
        {
            // 守卫：非活动/禁用/回池不攻击（AttackScheduler 已守卫，此处防御性二次检查）。
            if (!IsActive || Disabled || InPool)
            {
                return;
            }

            PerformAttack();
        }

        /// <summary>
        /// 执行具体兵种攻击效果创建（对应各兵种 performKnifeAttack/launchArrow 等）。
        /// 由 4 兵种（task 6.2）覆写。
        /// </summary>
        /// <remarks>
        /// <para><b>调用时机：</b>由 <see cref="Attack"/> 在守卫通过后调用。
        /// AttackScheduler 已保证冷却完毕且存在目标。</para>
        /// <para><b>子类实现：</b></para>
        /// <list type="bullet">
        /// <item>KnifeSoldier：查询目标 → 创建 KnifeAttackEffect → AttackEffectManager.Add。</item>
        /// <item>BowSoldier：查询目标 → 创建 ProjectileAttackEffect → AttackEffectManager.Add。</item>
        /// <item>SpearSoldier：查询目标 → 创建 PikeAttackEffect → AttackEffectManager.Add。</item>
        /// <item>CavalrySoldier：查询目标 → 创建 2 个 CavalrySweepEffect → AttackEffectManager.Add。</item>
        /// </list>
        /// <para>本基类不提供默认实现（abstract），强制子类覆写。</para>
        /// </remarks>
        protected internal abstract void PerformAttack();

        // ====================================================================
        // 状态机回调（对应 SoldierBase.js:79-98 onEnterState/onExitState）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>状态进入回调（对应 SoldierBase.js:79-82 onEnterState）。</summary>
        /// <param name="nextState">进入的状态。</param>
        /// <remarks>
        /// <para>对应 JS：<c>IDLE→playIdleAnimation</c>、<c>ATTACK→applyAttackPlaybackRate</c>。
        /// C# 移植为纯逻辑层，不持有动画引用，具体动画播放由表现端口根据状态同步。</para>
        /// <para><b>进入 Idle 清除表现意图：</b>AttackScheduler 在目标丢失时
        /// <see cref="UnitBase.SetState(AttackUnitState.Idle)"/>，本方法清除
        /// <see cref="_hasBodyFacing"/> 与 <see cref="_hasWeaponAim"/>，使表现层
        /// 下次同步时不再收到旧朝向/旧瞄准角，配合 BattleViewSynchronizer 的状态边沿
        /// 复位完成待机表现恢复。不清理战斗数值或攻击冷却。</para>
        /// <para>子类（4 兵种，task 6.2）可覆写本方法扩展状态进入行为，但 MUST 调用 base。</para>
        /// </remarks>
        protected override void OnEnterState(AttackUnitState nextState)
        {
            // 进入待机：清除朝向/瞄准等纯表现意图，避免旧表现残留（纯逻辑层不持有动画引用）。
            // JS 的 playIdleAnimation/applyAttackPlaybackRate 由表现端口承担。
            if (nextState == AttackUnitState.Idle)
            {
                _hasBodyFacing = false;
                _hasWeaponAim = false;
            }
        }

        /// <inheritdoc/>
        /// <summary>状态退出回调（对应 SoldierBase.js:84-86 onExitState）。</summary>
        /// <param name="previousState">退出的状态。</param>
        /// <remarks>
        /// <para>对应 JS：<c>ATTACK→onAttackStateExit</c>。C# 移植为纯逻辑层，
        /// 具体动画清理由表现端口承担。</para>
        /// <para>子类（4 兵种，task 6.2）可覆写本方法扩展状态退出行为（如 BowSoldier
        /// 清理 STOPPED 监听），但 MUST 调用 base。</para>
        /// </remarks>
        protected override void OnExitState(AttackUnitState previousState)
        {
            // 纯逻辑层：不持有动画引用。
            // JS 的 onAttackStateExit（清理 STOPPED 监听等）由表现端口承担。
        }

        // ====================================================================
        // Update —— 推进一帧（对应 SoldierBase.js:104-108 update/idle）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>
        /// 推进一帧（对应 SoldierBase.js:104-108 update）。Idle 态为 no-op。
        /// </summary>
        /// <param name="deltaMs">子步时长（毫秒）。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>SoldierBase.update(deltaMs)</c>：IDLE 态调 <c>idle(deltaMs)</c>，
        /// idle 为 no-op（SoldierBase.js:108）。攻击由 AttackScheduler 驱动，不由单位自身 Update。</para>
        /// <para>本方法保留供 UnitRegistry 在每子步统一调用（如有需要），当前为 no-op。</para>
        /// </remarks>
        internal override void Update(long deltaMs)
        {
            _ = deltaMs;
            // Idle 态 no-op（对应 SoldierBase.js:108 idle(deltaMs) { void deltaMs; }）。
        }

        // ====================================================================
        // GameOver —— 回收（对应 SoldierBase.js:182-198 gameOver）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>
        /// 强制结束并回收。先取消本单位发起的活动攻击效果，再调基类回收（对应 SoldierBase.js:182-198）。
        /// </summary>
        /// <returns>true=首次回收成功；false=已回收。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>SoldierBase.gameOver()</c>（SoldierBase.js:182-198）。
        /// JS gameOver 先 cancelOwner 攻击效果，再 super.gameOver，最后清 animation/displayObject。</para>
        /// <para>C# 移植：</para>
        /// <list type="bullet">
        /// <item>保留 cancelOwner：经 AttackEffectManager.CancelOwner 取消本单位发起的活动攻击效果
        ///   （对应 JS <c>attackEffectManager.cancelOwner(this)</c>）。</item>
        /// <item>删除 animation/displayObject 清理（纯逻辑层）。</item>
        /// <item>清空 targets 列表（对应 JS resetData 中 targets.length=0）。</item>
        /// <item>调 base.GameOver 标记 inPool/destroyed 与重置基类运行时状态。</item>
        /// </list>
        /// <para><b>幂等：</b>已入池返回 false（base.GameOver 守卫）。</para>
        /// <para>子类（4 兵种，task 6.2）可覆写本方法扩展回收边界（如 BowSoldier 清理
        /// pendingInitialAngle），但 MUST 调用 base.GameOver。</para>
        /// </remarks>
        public override bool GameOver()
        {
            if (InPool)
            {
                return false;
            }

            // 取消本单位发起的活动攻击效果（对应 JS attackEffectManager.cancelOwner(this)）。
            // 防止回收后攻击效果仍持有本单位引用造成池复用污染。
            if (_attackEffectManager != null)
            {
                _attackEffectManager.CancelOwner(this, "unit-game-over");
            }

            // 清空目标列表（对应 JS resetData targets.length=0）。
            _targets.Clear();

            return base.GameOver();
        }

        // ====================================================================
        // IPoolableBattleObject.ResetState —— 池回收前重置（override 扩展）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>
        /// 重置对象到等价于新构造的状态。清除 SoldierBase 专属字段 + 调 base.ResetState。
        /// </summary>
        /// <remarks>
        /// <para><b>调用时机（task 4.1）：</b>由 <see cref="BattleObjectPool{T}.Release"/>
        /// 在归还对象前调用。</para>
        ///
        /// <para><b>Acquire/Release Reset 契约（task 6.1 核心要求）：</b>
        /// 清除全部可变状态，使对象等价于新构造。回收后不得保留：
        /// </para>
        /// <list type="bullet">
        /// <item><b>目标引用：</b>_targets 列表 Clear（对应 JS targets.length=0）。
        ///   AttackScheduler 每次调度查询目标，不缓存在单位上；_targets 只在 PerformAttack
        ///   内临时使用，ResetState 清空保证无残留。</item>
        /// <item><b>冷却时间戳：</b>由 base.ResetState 清除（_lastAttackTimeMs=0）。</item>
        /// <item><b>攻击效果引用：</b>_attackEffectManager/_attackResolver/_enemyManager 置 null。
        ///   本单位发起的活动攻击效果已在 GameOver 中经 CancelOwner 取消，此处只清引用。</item>
        /// <item><b>表现引用：</b>本类型不持有 animation/displayObject（纯逻辑）。</item>
        /// <item><b>攻击数值：</b>_baseAttackPower/_addAttackPower/_baseAttackRange/
        ///   _baseAttackIntervalSeconds/_rangeBonusCells/_attackSpeedMultiplier 清零/默认。</item>
        /// <item><b>兵种标识：</b>_typeIndex=-1, _animationKey=null。</item>
        /// </list>
        /// <para><b>幂等性：</b>多次调用安全。</para>
        /// <para><b>不抛出：</b>实现 MUST NOT 抛出异常。</para>
        /// <para><b>子类扩展：</b>4 兵种（task 6.2）可 override 本方法扩展 Reset 行为
        /// （如 BowSoldier 清理 targetId/pendingInitialAngle），但 MUST 调用 base.ResetState。</para>
        /// <para><b>对应还原工程契约：</b>friendly-unit-pool-reset-contract.md 要求复位
        /// 攻击力、目标数组、攻击冷却、定时器、事件监听。本方法清除 SoldierBase 专属字段；
        /// 基类字段由 UnitBase.ResetState 清除。</para>
        /// </remarks>
        public override void ResetState()
        {
            ResetSoldierDefaults();
            base.ResetState();
        }
    }
}
