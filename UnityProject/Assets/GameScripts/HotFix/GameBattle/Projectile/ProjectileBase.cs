namespace GameBattle
{
    // ============================================================================
    // 任务 5.7：ProjectileBase —— 投射物发射、推进、命中、失效和回收生命周期
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 195 行 / Projectile/ProjectileBase.cs）：
    //   定义发射、推进、命中、失效和回收生命周期。纯逻辑基类，不持有 Unity GameObject。
    //
    // 来源证据（还原工程 ProjectileBase.js:1-259）：
    //   - 原始符号 qY，重建状态 COMPLETE_FOR_SIMPLE_DYNAMIC_ARROW
    //   - fire()：active=true → onFire() → 记录 startPosition → movement.onFire()
    //   - update(deltaMs)：委托 onUpdate(deltaMs)（子类扩展）
    //   - hit(enemy)：去重 hitEnemyIds → applyHit(enemy) → 标记已命中
    //   - requestRemove(immediate)：requestedRemoval=true；immediateRemoval 标记
    //   - recover()：幂等回收，清除全部状态，movement/hitStrategy/attacker 引用置空
    //   - damage getter：未显式指定时取 attacker.attackDamage
    //
    // 决策依据：
    //   - design.md 第 9 行：逻辑层不依赖 MonoBehaviour/Time.deltaTime。
    //   - design.md 决策 5：删除 SingletonBase/CombatServices，改为强类型注入的 internal 类。
    //   - design.md 第 282 行：新箭因 Projectile 阶段已过去，下一子步才移动。
    //     ProjectileBase 只提供 Advance(stepMs) 接口；时序由 5.8 ProjectileManager 保证。
    //   - spec battle-simulation "Projectile is launched after projectile phase"：
    //     新投射物直到下一子步才首次移动。
    //   - projectile-pool-reset-contract.md：必须清除 projectileId、目标、起点、进度、
    //     命中集合、状态标记；重复回收返回 false。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Unity GameObject/MonoBehaviour/RenderNode。
    //   2. 逻辑时间：使用 stepMs（毫秒），不使用 Time.deltaTime。
    //   3. 池化友好：实现 IPoolableBattleObject，ResetState 后等价于新构造。
    //   4. 幂等回收：recover() 重复调用返回 false。
    //   5. 命中去重：hitEnemyIds 防止同一敌人被同一投射物多次命中。
    // ============================================================================

    /// <summary>
    /// 投射物纯逻辑生命周期基类：发射、推进、命中、失效和回收。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 195 行）：</b>定义发射、推进、命中、失效和回收生命周期。
    /// 替代还原工程 <c>ProjectileBase.js</c>（ProjectileBase.js:1-259，原始符号 qY）。</para>
    ///
    /// <para><b>纯逻辑（design.md 第 9 行）：</b>不持有 Unity GameObject/MonoBehaviour/RenderNode。
    /// 位置/旋转使用逻辑字段 <see cref="X"/>/<see cref="Y"/>/<see cref="Rotation"/>，
    /// 表现层通过端口同步。</para>
    ///
    /// <para><b>逻辑时间（决策 0.9）：</b>使用 <c>stepMs</c>（毫秒）驱动移动，
    /// 不使用 <c>Time.deltaTime</c>。新箭因 Projectile 阶段已过去，下一子步才首次移动
    /// （design.md 第 282 行）；本基类只提供 <see cref="Advance"/> 接口，时序由
    /// ProjectileManager（task 5.8）保证。</para>
    ///
    /// <para><b>池化（task 4.1）：</b>实现 <see cref="IPoolableBattleObject"/>，
    /// <see cref="ResetState"/> 清除全部可变状态，回收后等价于新构造。</para>
    ///
    /// <para><b>本期不预建接口：</b>不创建 IProjectileMovement/IProjectileHitStrategy 接口，
    /// 移动和命中策略以具体类组合注入（task 5.7 约束）。出现第二个获准投射物时再提取接口。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 ProjectileFactory/ProjectileManager
    /// 与子类 SimpleDynamicArrow 使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal abstract class ProjectileBase : IPoolableBattleObject
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>未分配的投射物 ID 哨兵（对应 JS projectileId = -1）。</summary>
        public const int InvalidId = -1;

        // ====================================================================
        // 可变状态字段（对应 ProjectileBase.js:24-51 constructor）
        // ====================================================================

        // --- 标识与攻击者 ---

        /// <summary>投射物运行时 ID（对应 projectileId，ProjectileBase.js:26）。由 ProjectileFactory.Acquire 分配。</summary>
        private int _projectileId;

        /// <summary>攻击者运行时 ID（对应 attacker，ProjectileBase.js:28）。用于伤害提交时标识来源。</summary>
        private int _attackerId;

        /// <summary>攻击者攻击力（对应 attacker.attackDamage，通过 damage getter 惰性取值）。</summary>
        private int _attackerDamage;

        // --- 伤害 ---

        /// <summary>伤害值（对应 damageValue，ProjectileBase.js:29）。</summary>
        private int _damageValue;

        /// <summary>是否显式指定伤害（对应 explicitDamage，ProjectileBase.js:30）。</summary>
        private bool _explicitDamage;

        // --- 运动参数 ---

        /// <summary>速度缩放（对应 speedScale，ProjectileBase.js:31）。传入 movement.update。</summary>
        private float _speedScale;

        /// <summary>是否启用旋转（对应 rotationEnabled，ProjectileBase.js:32）。</summary>
        private bool _rotationEnabled;

        /// <summary>是否启用命中（对应 hitEnabled，ProjectileBase.js:33）。</summary>
        private bool _hitEnabled;

        // --- 生命周期标记 ---

        /// <summary>是否已激活/飞行中（对应 active，ProjectileBase.js:34）。</summary>
        private bool _active;

        /// <summary>是否已请求移除（对应 requestedRemoval，ProjectileBase.js:35）。</summary>
        private bool _requestedRemoval;

        /// <summary>是否立即移除（对应 immediateRemoval，ProjectileBase.js:36）。</summary>
        private bool _immediateRemoval;

        /// <summary>是否已回收（对应 recovered，ProjectileBase.js:37）。</summary>
        private bool _recovered;

        /// <summary>是否重置无效（对应 invalidReset，ProjectileBase.js:38）。</summary>
        private bool _invalidReset;

        // --- 新箭首帧守卫（task 2.11/0.9：新箭从下一子步开始移动） ---
        // 还原工程中 ProjectileManager.update 只遍历已存在的 activeProjectiles，
        // 新创建的箭矢在当前子步不会被推进。C# 移植在 ProjectileBase 内增加显式
        // 创建帧标记，Advance 首次调用时若发现仍在创建帧则跳过推进，作为防御性
        // 双保险（Primary 保证在 ProjectileManager 5.8 的迭代顺序）。

        /// <summary>
        /// 创建该投射物时的帧时间戳（frameNowMs）。
        /// <para>由 <see cref="MarkCreationFrame"/> 在 Acquire 后设置。
        /// <see cref="Advance"/> 首次调用时若 frameNowMs 等于创建帧则跳过推进，
        /// 保证新箭从下一子步才开始移动（spec "Projectile is launched after projectile phase"）。</para>
        /// </summary>
        private long _creationFrameMs;

        /// <summary>是否已通过首次 Advance（即已跨越创建帧，后续子步正常推进）。</summary>
        private bool _firstAdvancePassed;

        // --- 移除延迟（对应 removeDelayMs/removeDelayRemainingMs，ProjectileBase.js:39-40） ---

        /// <summary>移除延迟（毫秒，对应 removeDelayMs）。0=立即移除。</summary>
        private long _removeDelayMs;

        /// <summary>移除延迟剩余时间（毫秒，对应 removeDelayRemainingMs）。</summary>
        private long _removeDelayRemainingMs;

        // --- 逻辑位置与旋转（替代 JS renderNode.x/y/rotation） ---

        /// <summary>逻辑位置 X（替代 renderNode.x）。</summary>
        private float _x;

        /// <summary>逻辑位置 Y（替代 renderNode.y）。</summary>
        private float _y;

        /// <summary>逻辑旋转角度（度，替代 renderNode.rotation）。0° 朝上，90° 朝右。</summary>
        private float _rotation;

        // --- 起始位置（对应 startPosition，ProjectileBase.js:47） ---

        /// <summary>发射时起始位置 X（对应 startPosition.x）。</summary>
        private float _startX;

        /// <summary>发射时起始位置 Y（对应 startPosition.y）。</summary>
        private float _startY;

        // --- 命中去重（对应 hitEnemyIds，ProjectileBase.js:46） ---

        /// <summary>已命中敌人 ID 集合（对应 hitEnemyIds: Set）。防止同一敌人被多次命中。</summary>
        private readonly System.Collections.Generic.HashSet<int> _hitEnemyIds =
            new System.Collections.Generic.HashSet<int>();

        // ====================================================================
        // 注入依赖（Configure 时设置）
        // ====================================================================

        /// <summary>敌人管理器引用（对应 enemyManager）。供命中策略查询目标。</summary>
        private EnemyManager _enemyManager;

        /// <summary>是否已 Configure（对应 _configured，ProjectileBase.js:50）。</summary>
        private bool _configured;

        // ====================================================================
        // 只读属性
        // ====================================================================

        /// <summary>投射物运行时 ID（对应 projectileId）。</summary>
        internal int ProjectileId => _projectileId;

        /// <summary>攻击者运行时 ID（对应 attacker.id）。无攻击者传 -1。</summary>
        internal int AttackerId => _attackerId;

        /// <summary>
        /// 目标敌人运行时 ID（供轨迹记录采集，task 8.1）。
        /// <para>基类返回 <see cref="InvalidId"/>（-1）；由具体子类（如
        /// <see cref="SimpleDynamicArrow"/>）重写为从移动策略暴露真实目标 ID。
        /// 无目标或目标已失效时返回 -1，供黄金轨迹对照工具判断目标失效场景
        /// （spec battle-parity-verification "Arrow target dies during flight"）。</para>
        /// </summary>
        internal virtual int TargetId => InvalidId;

        /// <summary>是否已激活/飞行中（对应 active）。</summary>
        internal bool IsActive => _active;

        /// <summary>是否已请求移除（对应 requestedRemoval）。</summary>
        internal bool IsRemovalRequested => _requestedRemoval;

        /// <summary>是否立即移除（对应 immediateRemoval）。</summary>
        internal bool IsImmediateRemoval => _immediateRemoval;

        /// <summary>是否已回收（对应 recovered）。</summary>
        internal bool IsRecovered => _recovered;

        /// <summary>是否重置无效（对应 invalidReset）。</summary>
        internal bool IsInvalidReset => _invalidReset;

        /// <summary>是否启用命中（对应 hitEnabled）。</summary>
        internal bool HitEnabled
        {
            get => _hitEnabled;
            set => _hitEnabled = value;
        }

        /// <summary>是否启用旋转（对应 rotationEnabled）。</summary>
        internal bool RotationEnabled => _rotationEnabled;

        /// <summary>速度缩放（对应 speedScale）。</summary>
        internal float SpeedScale => _speedScale;

        /// <summary>逻辑位置 X（替代 renderNode.x）。</summary>
        internal float X => _x;

        /// <summary>逻辑位置 Y（替代 renderNode.y）。</summary>
        internal float Y => _y;

        /// <summary>逻辑旋转角度（度，替代 renderNode.rotation）。</summary>
        internal float Rotation => _rotation;

        /// <summary>发射时起始位置 X（对应 startPosition.x）。</summary>
        internal float StartX => _startX;

        /// <summary>发射时起始位置 Y（对应 startPosition.y）。</summary>
        internal float StartY => _startY;

        /// <summary>移除延迟（毫秒）。</summary>
        internal long RemoveDelayMs => _removeDelayMs;

        /// <summary>移除延迟剩余时间（毫秒）。</summary>
        internal long RemoveDelayRemainingMs => _removeDelayRemainingMs;

        /// <summary>
        /// 当前伤害值（对应 JS damage getter）。
        /// <para>未显式指定时取 <see cref="_attackerDamage"/>（对应 attacker.attackDamage）。</para>
        /// </summary>
        internal int Damage
        {
            get
            {
                if (!_explicitDamage)
                {
                    _damageValue = _attackerDamage;
                }
                return _damageValue;
            }
        }

        /// <summary>敌人管理器引用（供子类和移动/命中策略访问）。</summary>
        internal EnemyManager EnemyManager => _enemyManager;

        /// <summary>已命中敌人 ID 集合的只读视图（诊断用）。</summary>
        internal System.Collections.Generic.IReadOnlyCollection<int> HitEnemyIds => _hitEnemyIds;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造一个纯逻辑投射物基类。字段初始化为默认值，需经 <see cref="Configure"/>
        /// 注入依赖后再由子类 <see cref="ResetData"/> 初始化状态。
        /// </summary>
        protected ProjectileBase()
        {
            ResetToDefaults();
        }

        /// <summary>
        /// 把全部可变字段重置为默认值（等价于新构造后的状态）。
        /// </summary>
        private void ResetToDefaults()
        {
            _projectileId = InvalidId;
            _attackerId = -1;
            _attackerDamage = 0;
            _damageValue = 0;
            _explicitDamage = false;
            _speedScale = 1f;
            _rotationEnabled = true;
            _hitEnabled = true;
            _active = false;
            _requestedRemoval = false;
            _immediateRemoval = false;
            _recovered = false;
            _invalidReset = false;
            _creationFrameMs = 0;
            _firstAdvancePassed = false;
            _removeDelayMs = 0;
            _removeDelayRemainingMs = 0;
            _x = 0f;
            _y = 0f;
            _rotation = 0f;
            _startX = 0f;
            _startY = 0f;
            _hitEnemyIds.Clear();
            _enemyManager = null;
            _configured = false;
        }

        // ====================================================================
        // Configure —— 注入依赖
        // ====================================================================

        /// <summary>
        /// 注入运行时依赖。必须在 <see cref="ResetData"/> 之前调用。
        /// </summary>
        /// <param name="enemyManager">敌人管理器，供命中策略查询目标。不可为 null。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>ProjectileBase.configure({ enemyManager, ... })</c>
        /// （ProjectileBase.js:53-60）。C# 移植删除了 laya/gameData/effects/logger 等依赖，
        /// 只保留规则层必需的 enemyManager。</para>
        /// </remarks>
        internal void Configure(EnemyManager enemyManager)
        {
            if (enemyManager == null)
            {
                throw new System.ArgumentNullException(nameof(enemyManager));
            }

            _enemyManager = enemyManager;
            _configured = true;
        }

        // ====================================================================
        // AssignProjectileId —— 由 ProjectileFactory.Acquire 调用分配新 ID
        // ====================================================================

        /// <summary>
        /// 分配投射物运行时 ID。由 <see cref="ProjectileFactory.Acquire"/> 在从池获取后调用。
        /// </summary>
        /// <param name="newId">新运行时 ID（由 <see cref="RuntimeIdAllocator.Allocate"/> 分配）。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>projectile.projectileId = this.nextProjectileId++</c>
        /// （ProjectileFactory.js:121）。池复用不复用旧 ID：Release 时 ResetState 清除旧 ID，
        /// Acquire 时分配新 ID（design.md 目录表 RuntimeIdAllocator）。</para>
        /// </remarks>
        internal void AssignProjectileId(int newId)
        {
            _projectileId = newId;
        }

        // ====================================================================
        // MarkCreationFrame —— 标记创建帧（新箭下一子步才移动守卫）
        // ====================================================================

        /// <summary>
        /// 标记投射物的创建帧时间戳。由 <see cref="ProjectileFactory.Acquire"/> 在创建后调用。
        /// </summary>
        /// <param name="frameNowMs">创建时的帧时间戳（毫秒）。</param>
        /// <remarks>
        /// <para><b>新箭下一子步才移动（task 2.11 / 0.9 / spec "Projectile is launched after projectile phase"）：</b>
        /// 攻击释放时序在既有弹道推进之后创建新投射物，故新箭直到下一子步才首次移动。
        /// ProjectileManager（task 5.8）通过迭代顺序保证新箭不在创建子步被推进；
        /// 本标记作为防御性双保险——<see cref="Advance"/> 首次调用时若 frameNowMs 等于创建帧则跳过。</para>
        /// </remarks>
        internal void MarkCreationFrame(long frameNowMs)
        {
            _creationFrameMs = frameNowMs;
            _firstAdvancePassed = false;
        }

        // ====================================================================
        // ResetData —— 重置并注入发射配置（对应 ProjectileBase.js:81-112 resetData）
        // ====================================================================

        /// <summary>
        /// 重置投射物到待发射状态，注入攻击者、伤害、速度和移除延迟等配置。
        /// </summary>
        /// <param name="attackerId">攻击者运行时 ID（无攻击者传 -1）。</param>
        /// <param name="attackerDamage">攻击者攻击力（未显式指定伤害时使用）。</param>
        /// <param name="explicitDamage">是否显式指定伤害值。</param>
        /// <param name="damage">显式伤害值（仅 <paramref name="explicitDamage"/> 为 true 时生效）。</param>
        /// <param name="speedScale">速度缩放（默认 1）。</param>
        /// <param name="removeDelayMs">移除延迟（毫秒，默认 0=立即移除）。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>resetData(config)</c>（ProjectileBase.js:81-112）。
        /// 清除生命周期标记、命中集合，注入攻击者与伤害配置。</para>
        /// <para>子类通过 <see cref="OnReset"/> 扩展重置行为。</para>
        /// </remarks>
        internal void ResetData(
            int attackerId,
            int attackerDamage,
            bool explicitDamage,
            int damage,
            float speedScale,
            long removeDelayMs)
        {
            RequireConfigured();

            _recovered = false;
            _invalidReset = false;
            _requestedRemoval = false;
            _immediateRemoval = false;
            _active = false;
            _hitEnabled = true;

            _attackerId = attackerId;
            _attackerDamage = attackerDamage;
            _explicitDamage = explicitDamage;
            if (_explicitDamage)
            {
                _damageValue = damage;
            }
            _speedScale = speedScale;
            _removeDelayMs = removeDelayMs;
            _removeDelayRemainingMs = _removeDelayMs;

            _hitEnemyIds.Clear();

            // 子类扩展重置行为。
            OnReset();
        }

        /// <summary>
        /// 子类重置扩展点（对应 JS onReset）。默认空操作。
        /// </summary>
        protected virtual void OnReset() { }

        // ====================================================================
        // Fire —— 发射（对应 ProjectileBase.js:114-123 fire）
        // ====================================================================

        /// <summary>
        /// 激活投射物并记录起始位置。仅在待发射状态可调用。
        /// </summary>
        /// <param name="startX">发射起点 X。</param>
        /// <param name="startY">发射起点 Y。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>fire()</c>（ProjectileBase.js:114-123）：
        /// active=true → onFire() → 记录 startPosition → movement.onFire()。</para>
        /// <para>若已激活/已回收/无效/已请求移除则跳过（对应 JS 守卫）。</para>
        /// </remarks>
        internal void Fire(float startX, float startY)
        {
            if (_active || _recovered || _invalidReset || _requestedRemoval)
            {
                return;
            }

            _active = true;
            _x = startX;
            _y = startY;
            _startX = startX;
            _startY = startY;

            OnFire();
        }

        /// <summary>
        /// 子类发射扩展点（对应 JS onFire）。默认空操作。
        /// </summary>
        protected virtual void OnFire() { }

        // ====================================================================
        // Advance —— 推进一帧（对应 ProjectileBase.js:125 update + movement.update）
        // ====================================================================

        /// <summary>
        /// 推进一帧：调用移动策略更新位置，再调用 <see cref="OnUpdate"/>。
        /// </summary>
        /// <param name="frameNowMs">当前外部帧时间戳（毫秒）。同帧所有子步观察同一值。
        /// 用于新箭下一子步才移动守卫：首次调用若 frameNowMs 等于创建帧则跳过推进。</param>
        /// <param name="stepMs">子步时长（毫秒），驱动移动与弹道累计。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>ProjectileManager.update</c> 中对每个投射物的推进
        /// （ProjectileManager.js:79-81）：<c>movement.update(deltaMs, speedScale)</c>
        /// → <c>projectile.update(deltaMs)</c>。</para>
        /// <para><b>新箭下一子步才移动（task 2.11 / 0.9）：</b>
        /// 首次调用 Advance 时若 <paramref name="frameNowMs"/> 等于
        /// <see cref="_creationFrameMs"/>，说明仍在创建帧，跳过推进。
        /// 后续子步正常推进。ProjectileManager（task 5.8）的迭代顺序是 Primary 保证，
        /// 此守卫为防御性双保险。</para>
        /// <para>仅活动投射物推进（对应 JS <c>if (projectile.active)</c> 守卫）。</para>
        /// </remarks>
        internal void Advance(long frameNowMs, long stepMs)
        {
            if (!_active || stepMs <= 0)
            {
                return;
            }

            // 新箭下一子步才移动守卫（task 2.11 / 0.9）。
            // 首次 Advance 若仍在创建帧则跳过，标记已通过后后续子步正常推进。
            if (!_firstAdvancePassed)
            {
                if (frameNowMs == _creationFrameMs)
                {
                    return;
                }
                _firstAdvancePassed = true;
            }

            OnUpdate(stepMs);
        }

        /// <summary>
        /// 子类帧更新扩展点（对应 JS onUpdate）。子类在此调用移动策略更新位置与旋转。
        /// </summary>
        /// <param name="stepMs">子步时长（毫秒）。</param>
        protected virtual void OnUpdate(long stepMs) { }

        // ====================================================================
        // 位置/旋转写入（供移动策略调用，替代 renderNode.pos/rotation）
        // ====================================================================

        /// <summary>
        /// 设置逻辑位置（对应 JS renderNode.pos(x, y)）。
        /// </summary>
        internal void SetPosition(float x, float y)
        {
            _x = x;
            _y = y;
        }

        /// <summary>
        /// 设置逻辑旋转角度（对应 JS renderNode.rotation = value）。
        /// </summary>
        internal void SetRotation(float rotation)
        {
            _rotation = rotation;
        }

        // ====================================================================
        // Hit —— 命中敌人（对应 ProjectileBase.js:127-133 hit）
        // ====================================================================

        /// <summary>
        /// 命中一个敌人。去重后委托 <see cref="ApplyHit"/> 执行实际伤害。
        /// </summary>
        /// <param name="targetId">目标敌人运行时 ID。</param>
        /// <returns>true=本次命中生效（未去重且 ApplyHit 返回 true）；false=已命中过或未生效。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>hit(enemy)</c>（ProjectileBase.js:127-133）：
        /// 去重 hitEnemyIds → applyHit(enemy) → 标记已命中。</para>
        /// <para>C# 移植以 targetId 查找敌人实体，避免暴露 IEnemyEntity 引用给外部。</para>
        /// </remarks>
        internal bool Hit(int targetId)
        {
            if (targetId <= 0 || _hitEnemyIds.Contains(targetId) || !CanHit())
            {
                return false;
            }

            IEnemyEntity enemy = _enemyManager.GetById(targetId);
            if (enemy == null)
            {
                return false;
            }

            bool applied = ApplyHit(enemy);
            _hitEnemyIds.Add(targetId);
            return applied;
        }

        /// <summary>
        /// 是否可命中（对应 JS canHit）。默认 true，子类可覆盖。
        /// </summary>
        protected virtual bool CanHit() => true;

        /// <summary>
        /// 执行实际命中逻辑（对应 JS applyHit）。子类 MUST 实现。
        /// </summary>
        /// <param name="enemy">目标敌人实体。</param>
        /// <returns>true=命中生效（伤害提交成功）。</returns>
        protected abstract bool ApplyHit(IEnemyEntity enemy);

        // ====================================================================
        // RequestRemove —— 请求移除（对应 ProjectileBase.js:204-208 requestRemove）
        // ====================================================================

        /// <summary>
        /// 请求移除投射物。已回收则忽略。
        /// </summary>
        /// <param name="immediate">是否立即移除（跳过移除延迟）。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>requestRemove(immediate)</c>（ProjectileBase.js:204-208）：
        /// recovered 守卫 → immediateRemoval = immediate → requestedRemoval = true。</para>
        /// </remarks>
        internal void RequestRemove(bool immediate = false)
        {
            if (_recovered)
            {
                return;
            }

            _immediateRemoval = immediate;
            _requestedRemoval = true;
        }

        /// <summary>
        /// 推进移除延迟计数器。返回是否已完成延迟等待。
        /// </summary>
        /// <param name="stepMs">子步时长（毫秒）。</param>
        /// <returns>true=延迟已到期或无延迟，可执行移除；false=仍在延迟中。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>ProjectileManager.update</c> 中的移除延迟逻辑
        /// （ProjectileManager.js:119-124）。</para>
        /// </remarks>
        internal bool TickRemoveDelay(long stepMs)
        {
            if (_removeDelayMs == 0 || _immediateRemoval)
            {
                return true;
            }

            _removeDelayRemainingMs -= stepMs;
            return _removeDelayRemainingMs <= 0;
        }

        // ====================================================================
        // Recover —— 回收（对应 ProjectileBase.js:222-252 recover）
        // ====================================================================

        /// <summary>
        /// 回收投射物到待重置状态。幂等：重复调用返回 false。
        /// </summary>
        /// <returns>true=首次回收成功；false=已回收。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>recover()</c>（ProjectileBase.js:222-252）：
        /// recovered 守卫 → onRecover() → 清除全部状态 → recovered=true。</para>
        /// <para><b>不在此处调用池 Release：</b>池回收由 ProjectileFactory.Release 统一处理。
        /// 本方法只负责状态重置。</para>
        /// </remarks>
        internal bool Recover()
        {
            if (_recovered)
            {
                return false;
            }

            OnRecover();

            _recovered = true;
            _active = false;
            _hitEnabled = true;
            _requestedRemoval = false;
            _immediateRemoval = false;
            _invalidReset = false;
            _creationFrameMs = 0;
            _firstAdvancePassed = false;
            _hitEnemyIds.Clear();
            _attackerId = -1;
            _attackerDamage = 0;
            _damageValue = 0;
            _explicitDamage = false;
            _speedScale = 1f;
            _removeDelayMs = 0;
            _removeDelayRemainingMs = 0;
            _projectileId = InvalidId;
            _startX = 0f;
            _startY = 0f;

            return true;
        }

        /// <summary>
        /// 子类回收扩展点（对应 JS onRecover）。默认空操作。
        /// </summary>
        protected virtual void OnRecover() { }

        // ====================================================================
        // IPoolableBattleObject.ResetState —— 池回收前重置（task 4.1）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para><b>调用时机（task 4.1）：</b>由 <see cref="BattleObjectPool{T}.Release"/>
        /// 在归还对象前调用。</para>
        /// <para><b>完整性要求（projectile-pool-reset-contract.md）：</b>
        /// 清除全部可变状态，使对象等价于新构造。包括：投射物 ID、攻击者、伤害、
        /// 速度、旋转/命中标记、活动/移除/回收标记、位置/旋转、起始位置、命中集合。</para>
        /// <para><b>幂等性：</b>多次调用安全，结果状态相同。</para>
        /// <para><b>不抛出：</b>实现 MUST NOT 抛出异常。</para>
        /// </remarks>
        public virtual void ResetState()
        {
            ResetToDefaults();
        }

        // ====================================================================
        // 辅助
        // ====================================================================

        /// <summary>
        /// 校验已 Configure。
        /// </summary>
        private void RequireConfigured()
        {
            if (!_configured)
            {
                throw new System.InvalidOperationException(
                    "ProjectileBase.Configure() 必须在 ResetData() 之前调用");
            }
        }
    }
}
