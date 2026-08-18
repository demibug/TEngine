namespace GameBattle
{
    // ============================================================================
    // 任务 3.1/3.6：ConfiguredEnemyBase —— 配置化普通敌人共享基类
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 6 / specs/configured-enemy-spawning/spec.md）：
    //   集中当前 Mob0 的配置数值、出生、死亡表现边界、presentation-completed 守卫
    //   与 Reset 契约；Mob0～Mob3 是只固定 enemyKey/typeIndex 的薄类型。
    //   EnemyBase 仍拥有受击、终点攻击、死亡事实与击杀奖励提交（所有权不转移、
    //   奖励不重复）；本类型只提供数值来源（KillRewardValue / EndPointAttackDamageValue）
    //   与普通敌人共享的出生/表现/回收边界。
    //
    // 与 EnemyBase 的边界（design.md:125 / spec "Mob0 to Mob3 follow one lifecycle contract"）：
    //   - EnemyBase：路径移动、受击、终点攻击、死亡事实、击杀奖励提交、空间格。
    //   - 本类型：配置数值初始化、generation/waveOrder 租借身份、死亡表现边界、
    //     OnDeathPresentationCompleted 守卫、Reset 契约。
    //
    // 租借身份（design.md 决策 5 / task 3.6）：
    //   - 每次租借 generation 单调递增（_generation，跨租借不重置），并携带 waveOrder；
    //   - runtimeId 仍由 RuntimeIdAllocator 每次租借分配（EnemyBase.AssignRuntimeId）；
    //   - 当前租借身份以不可变 <see cref="EnemyLeaseIdentity"/> 值对象暴露，
    //     供下一波 EnemyManager 幂等匹配并抵抗迟到回调。
    //
    // Reset 契约（task 3.6）：
    //   ResetState 清空本次租借的数值、waveOrder、死亡表现标记与 presentation 世代；
    //   不重置 _generation（保持单调，供迟到回调的世代守卫使用）。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Unity GameObject/表现组件。
    //   2. 死亡表现边界：BeginDeath 置已开始/已调度，完成回调带世代守卫。
    //   3. Reset 后等价于新构造（除 _generation 单调计数）。
    // ============================================================================

    /// <summary>
    /// 配置化普通敌人共享基类：集中配置数值、出生、死亡表现边界、
    /// presentation-completed 守卫与 Reset 契约。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 决策 6）：</b>承接当前 Mob0 的配置数值、出生、死亡表现
    /// 边界与 Reset 契约；Mob0～Mob3 是只固定 <see cref="EnemyKey"/>/<see cref="TypeIndex"/>
    /// 的薄类型（<see cref="Mob0Enemy"/>/<see cref="Mob1Enemy"/>/<see cref="Mob2Enemy"/>/
    /// <see cref="Mob3Enemy"/>）。技能召唤类不接入本类型链。</para>
    ///
    /// <para><b>EnemyBase 所有权不转移（spec "Mob0 to Mob3 follow one lifecycle contract"）：</b>
    /// 受击、终点攻击、死亡事实与击杀奖励提交仍由 <see cref="EnemyBase"/> 唯一执行；
    /// 本类型只 override <see cref="EnemyBase.KillRewardValue"/> 与
    /// <see cref="EnemyBase.EndPointAttackDamageValue"/> 的数值来源，不复制或重复奖励逻辑。</para>
    ///
    /// <para><b>租借身份（design.md 决策 5 / task 3.6）：</b>
    /// <see cref="ConfiguredInit"/> 每次租借递增 generation 并携带 waveOrder；当前租借身份以
    /// <see cref="CurrentLease"/>（<see cref="EnemyLeaseIdentity"/>）暴露。Reset/Release 清空
    /// 本次 callbacks（EnemyBase.ResetState 置 null）、wave ownership 与迟到 presentation 状态。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 EnemyFactory / EnemyManager
    /// 与四个 Mob 薄类型使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal abstract class ConfiguredEnemyBase : EnemyBase, IWaveOwnedEnemyEntity
    {
        // ====================================================================
        // 每类型固定身份（薄类型 override）
        // ====================================================================

        /// <summary>普通敌人键（Mob0/Mob1/Mob2/Mob3）。</summary>
        internal abstract string EnemyKey { get; }

        /// <summary>普通敌人类型索引（0～3）。</summary>
        internal abstract int TypeIndex { get; }

        // ====================================================================
        // 租借状态（task 3.6 / task 5.4）
        // ====================================================================

        /// <summary>
        /// 本次租借解析后的实例数值（null=未初始化）。
        /// <para>由 <see cref="EnemyStatsResolver"/> 解析并经 <see cref="SetResolvedStats"/>
        /// 注入；ResetState 置 null。</para>
        /// </summary>
        private ConfiguredEnemyResolvedStats? _stats;

        /// <summary>
        /// 本次租借的资源地址（YooAsset location，表现预加载池键）。
        /// <para>与 <see cref="_stats"/> 来自同一 <see cref="EnemyDefinitionSnapshot"/>，
        /// 由 <see cref="EnemyFactory"/> 在 <see cref="ConfiguredInit"/> 注入；ResetState 置空，
        /// 池复用不残留旧地址（design.md 决策 7 / task 5.4）。</para>
        /// </summary>
        private string _resourceAddress;

        /// <summary>
        /// 租借世代（单调递增，跨租借不重置）。
        /// <para>供死亡表现完成回调的世代守卫：迟到回调携带旧世代，与当前世代不同则忽略。</para>
        /// </summary>
        private long _generation;

        /// <summary>本次租借的波次所有权（waveOrder）。ResetState 清空。</summary>
        private int _waveOrder;

        // ====================================================================
        // 死亡表现边界（NormalEnemyBase.js:82-97 合入，供所有配置化敌人共享）
        // ====================================================================

        /// <summary>死亡表现已开始标记。</summary>
        private bool _deathPresentationStarted;

        /// <summary>死亡表现已完成标记。</summary>
        private bool _deathPresentationCompleted;

        /// <summary>死亡表现已调度标记（已请求表现层播放死亡动画）。</summary>
        private bool _deathScheduled;

        /// <summary>调度死亡表现时的世代（用于完成回调的世代守卫）。</summary>
        private long _presentationGeneration;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造配置化敌人基类。字段默认等价于新构造，需经
        /// <see cref="ConfiguredInit"/>（新链）或旧链的
        /// <c>InitializeStats + Init</c> 初始化。
        /// </summary>
        protected ConfiguredEnemyBase()
        {
            _stats = null;
            _resourceAddress = null;
            _generation = 0;
            _waveOrder = 0;
            _deathPresentationStarted = false;
            _deathPresentationCompleted = false;
            _deathScheduled = false;
            _presentationGeneration = 0;
        }

        // ====================================================================
        // 只读诊断属性（供工厂 / 测试观察）
        // ====================================================================

        /// <summary>本次租借解析后的实例数值（null=未初始化）。</summary>
        internal ConfiguredEnemyResolvedStats? Stats => _stats;

        /// <summary>
        /// 本次租借的资源地址（YooAsset location；Reset 后为 null/空串）。
        /// <para>与 stats 来自同一 <see cref="EnemyDefinitionSnapshot"/>，由工厂注入，
        /// 薄类型不得硬编码（task 5.4）。</para>
        /// </summary>
        internal string ResourceAddress => _resourceAddress ?? string.Empty;

        /// <summary>运行时 ID（委托 <see cref="EnemyBase.Id"/>）。</summary>
        internal int RuntimeId => Id;

        /// <summary>基础移动速度（供测试观察）。</summary>
        internal int BaseMoveSpeedValue => BaseMoveSpeed;

        /// <summary>最大血量基础值（供测试观察）。</summary>
        internal int MaxHealthBaseValue => MaxHealthBase;

        /// <summary>当前租借世代（诊断用）。</summary>
        internal long Generation => _generation;

        protected override long BuffGeneration => _generation;

        /// <summary>当前租借的 waveOrder（Reset 后为 0）。</summary>
        internal int WaveOrder => _waveOrder;

        /// <summary>当前租借身份（不可变值对象）：runtimeId + generation + waveOrder。
        /// <para>供下一波 EnemyManager 追踪波次所有权；Reset 后
        /// <see cref="EnemyLeaseIdentity.IsValid"/> 为 false。</para>
        /// </summary>
        internal EnemyLeaseIdentity CurrentLease => new EnemyLeaseIdentity(Id, _generation, _waveOrder);

        /// <inheritdoc/>
        EnemyLeaseIdentity IWaveOwnedEnemyEntity.CurrentLease => CurrentLease;

        /// <inheritdoc/>
        WaveEntityKind IWaveOwnedEnemyEntity.WaveKind => WaveEntityKind.Normal;

        /// <summary>是否已开始死亡表现。</summary>
        internal bool IsDeathPresentationStarted => _deathPresentationStarted;

        /// <summary>死亡表现是否已调度。</summary>
        internal bool IsDeathScheduled => _deathScheduled;

        /// <summary>死亡表现是否已完成。</summary>
        internal bool IsDeathPresentationCompleted => _deathPresentationCompleted;

        // ====================================================================
        // 数值注入
        // ====================================================================

        /// <summary>
        /// 注入解析后的实例数值：设置移动速度、最大血量基础值与数值快照。
        /// </summary>
        /// <remarks>
        /// <para>由 <see cref="ConfiguredInit"/>（新链）调用；旧链
        /// <c>Mob0Enemy.InitializeStats</c> 也经本方法注入（最大血量取 healthByWave[0]）。
        /// 当前血量由 <see cref="EnemyBase.Init"/> 在出生时按 maxHealth 参数设置。</para>
        /// </remarks>
        protected internal void SetResolvedStats(ConfiguredEnemyResolvedStats stats)
        {
            _stats = stats;
            MaxHealthBase = stats.MaxHealth;
            BaseMoveSpeed = stats.MoveSpeed;
        }

        // ====================================================================
        // ConfiguredInit —— 新链出生入口（EnemyFactory.Acquire(request) 调用）
        // ====================================================================

        /// <summary>
        /// 新链出生入口：递增世代、注入 waveOrder、注入数值与运行时依赖、初始化出生状态。
        /// </summary>
        /// <param name="map">地图数据（不可为 null）。</param>
        /// <param name="cellSize">格子尺寸（px）。</param>
        /// <param name="endPointTarget">终点攻击目标（按车道绑定阿斗）。</param>
        /// <param name="onEnemyKilled">击杀奖励回调。</param>
        /// <param name="onDeathRequested">死亡请求移除回调。</param>
        /// <param name="stats">解析后的实例数值。</param>
        /// <param name="resourceAddress">本次租借的资源地址（与 <paramref name="stats"/> 同一
        /// <see cref="EnemyDefinitionSnapshot"/>，由工厂注入；Reset 后清空）。</param>
        /// <param name="isPlayerLane">是否玩家方车道。</param>
        /// <param name="waveOrder">波次所有权（不可变，随租借携带）。</param>
        /// <param name="width">逻辑宽度。</param>
        /// <param name="height">逻辑高度。</param>
        /// <remarks>
        /// <para><b>世代（task 3.6）：</b>每次租借调用本方法都递增 <see cref="_generation"/>，
        /// 使迟到回调无法命中旧世代。waveOrder 在本次租借内稳定。</para>
        /// <para><b>调用顺序：</b>先注入数值与依赖（Configure），再 Init 初始化出生状态。
        /// 之后由工厂调用 <see cref="EnemyBase.BeginMoving"/> 开始移动。</para>
        /// </remarks>
        internal void ConfiguredInit(
            MapData map,
            float cellSize,
            IEnemyEndPointAttackTarget endPointTarget,
            EnemyKilledHandler onEnemyKilled,
            EnemyDeathRequestHandler onDeathRequested,
            ConfiguredEnemyResolvedStats stats,
            string resourceAddress,
            bool isPlayerLane,
            int waveOrder,
            float width,
            float height)
        {
            _generation += 1;
            _waveOrder = waveOrder;
            _resourceAddress = resourceAddress ?? string.Empty;
            SetResolvedStats(stats);
            Configure(map, cellSize, endPointTarget, onEnemyKilled, onDeathRequested);
            Init(isPlayerLane, stats.MaxHealth, width, height);
        }

        // ====================================================================
        // 死亡表现边界（NormalEnemyBase.js:82-97 合入，裁剪灵魂投射/吹飞）
        // ====================================================================

        /// <summary>
        /// 开始死亡表现：调用基类死亡逻辑并置死亡表现边界与世代守卫。
        /// </summary>
        /// <remarks>
        /// <para>先调用 <see cref="EnemyBase.BeginDeath"/>（置 deathStarted、经
        /// <see cref="EnemyBase.KillRewardValue"/> 提交奖励），再置本类型表现边界。
        /// 记录调度世代（<see cref="_presentationGeneration"/>），供完成回调抵抗
        /// 池复用后的迟到回调。</para>
        /// <para><b>幂等：</b>以 <see cref="_deathPresentationStarted"/> 守卫，重复调用为空操作。</para>
        /// </remarks>
        internal override void BeginDeath()
        {
            if (_deathPresentationStarted)
            {
                return;
            }

            base.BeginDeath();

            _deathPresentationStarted = true;
            _deathScheduled = true;
            _presentationGeneration = _generation;
        }

        /// <summary>
        /// 表现层通知死亡动画播放完成。置已完成标记并触发 <see cref="EnemyBase.GameOver"/>。
        /// </summary>
        /// <remarks>
        /// <para><b>守卫（task 3.6）：</b>已回收（InPool）、未调度（!_deathScheduled）或
        /// 世代不符（迟到回调）时为空操作，防止过期回调修改已回收/已复用对象。
        /// 对应 NormalEnemyBase.js:88 <c>if (generation !== this._lifecycleGeneration || this.inPool) return</c>。</para>
        /// </remarks>
        internal void OnDeathPresentationCompleted()
        {
            if (InPool || !_deathScheduled || _presentationGeneration != _generation)
            {
                return;
            }

            _deathScheduled = false;
            _deathPresentationCompleted = true;
            GameOver();
        }

        // ====================================================================
        // 数值来源 override（所有权仍在 EnemyBase）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>只按 <see cref="ConfiguredEnemyResolvedStats.RewardGold"/> 提供；未注入配置即为装配错误，不使用固定 fallback。</remarks>
        protected override int KillRewardValue => RequireResolvedStats().RewardGold;

        /// <inheritdoc/>
        /// <remarks>只按 <see cref="ConfiguredEnemyResolvedStats.ContactDamage"/> 提供；未注入配置即为装配错误，不使用固定 fallback。</remarks>
        protected override int EndPointAttackDamageValue => RequireResolvedStats().ContactDamage;

        private ConfiguredEnemyResolvedStats RequireResolvedStats()
        {
            if (!_stats.HasValue)
            {
                throw new System.InvalidOperationException(
                    $"{EnemyKey} 尚未注入配置数值，禁止使用固定奖励或接触伤害 fallback");
            }

            return _stats.Value;
        }

        // ====================================================================
        // IPoolableBattleObject.ResetState —— 清空本次租借状态
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para><b>清空本次租借（task 3.6 / task 5.4）：</b>数值、资源地址、waveOrder、
        /// 死亡表现边界、presentation 世代均复位；EnemyBase.ResetState 清除运行时 ID、
        /// 阵营、生命、路径与 callbacks（_onEnemyKilled/_onDeathRequested/_onHealthChanged
        /// 置 null）。池复用不残留旧资源地址。</para>
        /// <para><b>不重置 <see cref="_generation"/>：</b>保持跨租借单调，供世代守卫识别
        /// 迟到回调。回收后对象除世代计数外等价于新构造。</para>
        /// <para><b>幂等且不抛出：</b>IPoolableBattleObject 契约。</para>
        /// </remarks>
        public override void ResetState()
        {
            _stats = null;
            _resourceAddress = null;
            _waveOrder = 0;
            _deathPresentationStarted = false;
            _deathPresentationCompleted = false;
            _deathScheduled = false;
            _presentationGeneration = 0;

            base.ResetState();
        }
    }
}
