namespace GameBattle
{
    // ============================================================================
    // 任务 4.4：Mob0Enemy —— 本期唯一敌人类型，合入原 NormalEnemyBase 本期所需能力
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Enemy/Mob0Enemy.cs）：
    //   本期唯一敌人类型；承接原 NormalEnemyBase 中最简链需要的数值初始化、死亡表现边界
    //   和动画清理，不移植灵魂投射与吹飞能力（design.md:175 / 决策 5 / task 1.5）。
    //
    // 来源证据（还原工程）：
    //   - Mob0Enemy.js:11-58：本期唯一敌人，resourcePath/typeIndex=0/visualPoolKey='mob'，
    //     init 取表现节点后调 super.init，startMoving/stopMoving 委托 presentation 呼吸动画，
    //     gameOver 先调 super.gameOver 再回收表现节点。
    //   - NormalEnemyBase.js:22-231：继承 EnemyBase，合入本期所需：
    //       * _initializeStatsAndAnimation（js:51-68）：从 gameData.resolveEnemyStats 读取
    //         health/speed，设置 maxHealthBase/baseMoveSpeed/healthText/healthBar。
    //       * beginDeath（js:82-97）：调 super.beginDeath + 播死亡表现 + onComplete 回调
    //         → 隐藏表现 → gameOver。灵魂投射 _tryDeliverSoul（js:104-112）本期不移植。
    //       * gameOver（js:204-230）：清理吹飞定时器/Tween/动画引用/表现 filters。
    //         吹飞 Xw/Gw/blowUpCurve/blowUpState（js:151-202）本期不移植。
    //   - EnemyBase.js:196-213 init / 314-353 move / 453-479 hit / 483-496 beginDeath /
    //     651-698 gameOver：路径移动/受击/死亡/Reset 由 EnemyBase（task 4.3/55）实现。
    //   - BattleDataCore.js:60-89 resolveNormalStats：health = healthByWave[wave] *
    //     strategyMultiplier * earlyMultiplier；speed = cfg.speed。
    //
    // 合入决策（design.md:316 / task 1.5）：
    //   NormalEnemyBase 本期合入 Mob0Enemy：只有一个普通敌人类型，不为未证明的第二适配器
    //   保留浅层继承 seam；灵魂投射和吹飞能力不移植。
    //
    // 数值初始化（task 39/40 EnemyStats 表）：
    //   healthByWave / speed / contactDamage / rewardGold 来自 Luban EnemyStats 表
    //   （GameConfig.battle.EnemyStats:18-58）。EnemyConfigSnapshot 已含 speed/
    //   healthByWave/contactDamage（task 3.3），rewardGold 由 EnemyStats 表提供。
    //   本类型通过 Mob0EnemyInitStats 结构接收这四个数值，由 EnemyFactory（task 4.5）
    //   或 EnemyManager 从配置快照注入。BaseMoveSpeed/MaxHealthBase 由 InitializeStats
    //   直接设置（EnemyBase 提供 protected setter）；CurrentHealth 由 EnemyBase.Init
    //   在出生时根据 maxHealth 参数设置（_currentHealth = maxHealth）。
    //
    // 死亡表现边界（NormalEnemyBase.js:82-97 合入）：
    //   原始 beginDeath 在 super.beginDeath 后调 presentation.playDeath，onComplete
    //   回调中隐藏表现并调 gameOver。C# 移植为纯逻辑层：不持有表现组件，只维护
    //   "死亡表现已开始/已完成"的规则层边界标记，供 Presenter（task 7.4）观察并
    //   驱动实际表现。表现回调通过 OnDeathPresentationCompleted 由表现层在完成时调用。
    //
    // 动画清理（NormalEnemyBase.js:204-230 合入，裁剪吹飞/Tween）：
    //   原 gameOver 清理吹飞定时器、Tween、animation 引用、visual filters。
    //   本期不移植吹飞，只保留动画引用清理：ResetState 时清除死亡表现边界标记。
    //   不持有 Unity Animator/Sprite/GameObject 引用。
    //
    // 继承 EnemyBase（task 4.3/55 已实现）：
    //   EnemyBase 提供：路径移动、受击、死亡（protected virtual BeginDeath）、
    //   回收（public virtual GameOver）、Reset（public virtual ResetState）、
    //   IPoolableBattleObject、IEnemyEntity 实现。
    //   Mob0Enemy 覆写 BeginDeath（加死亡表现边界）与 ResetState（加 Mob0 专属字段清理）。
    //
    // EnemyBase 契约（task 4.3/55 产物 EnemyBase.cs 实际签名）：
    //   - protected virtual void BeginDeath()：本类型 protected internal override 加表现边界
    //     （扩展访问性供测试 InternalsVisibleTo 调用）。
    //   - public virtual void ResetState()：IPoolableBattleObject 实现，本类型 public override 加 Mob0 字段清理。
    //   - public virtual bool GameOver()：基类回收入口，OnDeathPresentationCompleted 触发。
    //   - protected int MaxHealthBase { get; set; }：由 InitializeStats 设置。
    //   - protected int CurrentHealth（只读）：由 EnemyBase.Init 设置，Mob0Enemy 不直接写。
    //   - protected int BaseMoveSpeed { get; set; }：由 InitializeStats 设置。
    //   - internal bool InPool：是否已归还池，OnDeathPresentationCompleted 守卫使用。
    //   - public int Id：运行时 ID（IEnemyEntity.Id），由 EnemyBase.AssignRuntimeId 设置。
    //   - protected internal void AssignRuntimeId(int)：设置运行时 ID，由 EnemyFactory.Acquire 调用。
    //   本类型额外提供 internal RuntimeId 属性委托 Id，供 EnemyFactoryTests 使用。
    //
    // 不移植能力（design.md:316 / task 1.5）：
    //   - 灵魂投射 _tryDeliverSoul / sB（NormalEnemyBase.js:104-142）：属 Skill 范围，
    //     SoulSummonEffect 为明确排除项。soulTowerResolver / soulFlightManager 不注入。
    //   - 吹飞 Xw / Gw / blowUpCurve / blowUpState（NormalEnemyBase.js:151-202）：
    //     外部触发 API，本期无调用方。gameLoop.register/unregister 不引入。
    //
    // 不变量：
    //   1. 本期唯一敌人类型：不创建独立基类，NormalEnemyBase 能力直接合入。
    //   2. 纯逻辑层：不持有 Unity GameObject/Animator/Sprite 引用，表现通过端口同步。
    //   3. 死亡表现边界：BeginDeath 置 _deathStarted，表现完成后 OnDeathPresentationCompleted
    //      置 _deathPresentationCompleted，再由 EnemyBase.GameOver 回收。
    //   4. 动画清理：ResetState 清除死亡表现边界标记，不残留跨生命周期状态。
    //   5. 池复用无污染：ResetState 清除全部 Mob0 专属可变状态，回收后等价于新构造。
    // ============================================================================

    /// <summary>
    /// 本期唯一敌人类型；承接原 <c>NormalEnemyBase</c> 中最简链需要的数值初始化、
    /// 死亡表现边界和动画清理，不移植灵魂投射与吹飞能力。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>本期唯一敌人类型。NormalEnemyBase 合入
    /// 本类型（design.md:316 / task 1.5），不创建独立基类。</para>
    ///
    /// <para><b>数值初始化（task 39/40 EnemyStats 表）：</b>
    /// <see cref="InitializeStats"/> 接收 <see cref="Mob0EnemyInitStats"/>，从中读取
    /// healthByWave/speed/contactDamage/rewardGold，设置基础移动速度与最大血量基础值。
    /// 当前血量由 <see cref="EnemyBase.Init"/> 在出生时根据 maxHealth 参数设置。
    /// 数值来源为 Luban EnemyStats 表（GameConfig.battle.EnemyStats）。</para>
    ///
    /// <para><b>死亡表现边界（NormalEnemyBase.js:82-97 合入）：</b>
    /// <see cref="BeginDeath"/> 置死亡表现已开始标记，表现层在死亡动画播放完成后
    /// 调用 <see cref="OnDeathPresentationCompleted"/> 置已完成标记。本类型不持有表现组件，
    /// 只维护规则层边界，供 Presenter 观察并驱动实际表现。</para>
    ///
    /// <para><b>动画清理（NormalEnemyBase.js:204-230 合入，裁剪吹飞/Tween）：</b>
    /// <see cref="ResetState"/> 清除死亡表现边界标记，不残留跨生命周期状态。
    /// 本期不移植吹飞定时器/Tween 清理。</para>
    ///
    /// <para><b>不移植能力（design.md:316）：</b>
    /// 灵魂投射 <c>_tryDeliverSoul/sB</c> 属 Skill 范围；吹飞 <c>Xw/Gw</c> 本期无调用方。
    /// 两者均不引入。</para>
    ///
    /// <para><b>继承 EnemyBase（task 4.3/55）：</b>
    /// EnemyBase 提供路径移动、受击、死亡、Reset、IPoolableBattleObject.ResetState、
    /// IEnemyEntity 实现。本类型覆写 <see cref="BeginDeath"/> 与 <see cref="ResetState"/>，
    /// 添加 Mob0 专属的死亡表现边界与动画清理逻辑。</para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部 EnemyFactory / EnemyManager
    /// 使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class Mob0Enemy : EnemyBase
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>
        /// Mob0 敌人类型索引（对应 Mob0Enemy.js:16 typeIndex=0）。
        /// <para>用于从 EnemyStats 表索引数值，本期固定 0。</para>
        /// </summary>
        internal const int TypeIndex = 0;

        // ====================================================================
        // Mob0 专属可变状态（NormalEnemyBase 合入，裁剪吹飞/灵魂）
        // ====================================================================

        /// <summary>
        /// 已初始化数值（对应 NormalEnemyBase.js:52-67 _initializeStatsAndAnimation 产物）。
        /// <para>null 表示未初始化；InitializeStats 后非 null。ResetState 置 null。</para>
        /// </summary>
        private Mob0EnemyInitStats? _stats;

        /// <summary>
        /// 死亡表现已开始标记（对应 NormalEnemyBase.js:83-85 beginDeath 守卫）。
        /// <para>BeginDeath 置 true；ResetState 置 false。用于防止重复触发死亡表现。
        /// 注意：EnemyBase 自身也有 _deathStarted 字段，本字段是 Mob0 专属的"表现层
        /// 死亡表现"边界标记，与 EnemyBase 的规则层 deathStarted 区分。</para>
        /// </summary>
        private bool _deathPresentationStarted;

        /// <summary>
        /// 死亡表现已完成标记（对应 NormalEnemyBase.js:88-96 onComplete 回调边界）。
        /// <para>表现层在死亡动画播放完成后调用 OnDeathPresentationCompleted 置 true。
        /// 纯逻辑层只维护边界标记，不持有表现组件。ResetState 置 false。</para>
        /// </summary>
        private bool _deathPresentationCompleted;

        /// <summary>
        /// 死亡表现已调度标记（对应 NormalEnemyBase.js:86 _deathScheduled）。
        /// <para>BeginDeath 置 true 表示已请求表现层播放死亡动画；表现完成后置 false。
        /// 防止重复调度。ResetState 置 false。</para>
        /// </summary>
        private bool _deathScheduled;

        // ====================================================================
        // 构造（对应 Mob0Enemy.js:12-17 constructor）
        // ====================================================================

        /// <summary>
        /// 构造 Mob0Enemy 实例。无参构造供 <see cref="BattleObjectPool{Mob0Enemy}"/> 工厂委托使用。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>Mob0Enemy constructor</c>（Mob0Enemy.js:12-17）：
        /// 设置 resourcePath/typeIndex/visualPoolKey。C# 移植不持有表现资源路径，
        /// 只记录 TypeIndex 常量；表现资源由 Presenter（task 7.4）通过端口加载。</para>
        /// <para>由 <see cref="EnemyFactory"/> 经 <c>BattleObjectPool&lt;Mob0Enemy&gt;</c>
        /// 工厂委托 <c>() =&gt; new Mob0Enemy()</c> 调用（task 4.5 EnemyFactoryTests 依赖此契约）。</para>
        /// </remarks>
        internal Mob0Enemy()
        {
            _stats = null;
            _deathPresentationStarted = false;
            _deathPresentationCompleted = false;
            _deathScheduled = false;
        }

        // ====================================================================
        // 数值初始化（NormalEnemyBase.js:51-68 _initializeStatsAndAnimation 合入）
        // ====================================================================

        /// <summary>
        /// 初始化 Mob0 数值：从 <see cref="Mob0EnemyInitStats"/> 设置移动速度与最大血量基础值。
        /// </summary>
        /// <param name="stats">
        /// Mob0 数值快照，包含 healthByWave/speed/contactDamage/rewardGold。
        /// 数值来源为 Luban EnemyStats 表（task 39/40）。
        /// </param>
        /// <remarks>
        /// <para><b>来源（NormalEnemyBase.js:51-68）：</b>
        /// 原 <c>_initializeStatsAndAnimation</c> 从 <c>gameData.resolveEnemyStats</c>
        /// 读取 <c>ph</c>（血量）与 <c>speed</c>，设置 <c>health/maxHealthBase/baseMoveSpeed</c>。
        /// C# 移植从强类型 <see cref="Mob0EnemyInitStats"/> 读取，不依赖 gameData 单例。</para>
        ///
        /// <para><b>数值来源（task 39/40 EnemyStats 表）：</b>
        /// healthByWave/speed/contactDamage/rewardGold 来自 Luban EnemyStats 表
        /// （GameConfig.battle.EnemyStats:18-58）。EnemyFactory 或 EnemyManager 从配置快照
        /// 构造 <see cref="Mob0EnemyInitStats"/> 注入。</para>
        ///
        /// <para><b>血量设置：</b>
        /// <see cref="EnemyBase.MaxHealthBase"/> 由本方法直接设置（EnemyBase 提供 protected setter）。
        /// 当前血量（<see cref="EnemyBase.CurrentHealth"/>）由 <see cref="EnemyBase.Init"/>
        /// 在出生时根据 maxHealth 参数设置（<c>_currentHealth = maxHealth</c>），
        /// 本方法不直接写 CurrentHealth（EnemyBase 未暴露 protected setter）。
        /// 调用方应在 InitializeStats 后调用 <c>Init(isPlayerLane, GetInitialHealth(), width, height)</c>
        /// 完成血量初始化。</para>
        ///
        /// <para><b>不设置表现字段：</b>
        /// 原 <c>_initializeStatsAndAnimation</c> 还设置 healthText/healthBar/animation。
        /// C# 移植为纯逻辑层，不持有表现组件；血条/动画由 Presenter 通过端口同步。</para>
        ///
        /// <para><b>池复用安全：</b>
        /// <see cref="ResetState"/> 置 <c>_stats = null</c>，下次 Acquire 后必须重新调用
        /// 本方法初始化数值，保证池复用无污染。</para>
        /// </remarks>
        internal void InitializeStats(Mob0EnemyInitStats stats)
        {
            _stats = stats;

            // 设置基础移动速度（对应 NormalEnemyBase.js:55 this.baseMoveSpeed = stats.speed）。
            BaseMoveSpeed = stats.Speed;

            // 设置最大血量基础值（对应 NormalEnemyBase.js:54 this.maxHealthBase = stats.ph）。
            // 当前血量（CurrentHealth）由 EnemyBase.Init 在出生时设置，此处只设 MaxHealthBase。
            int baseHealth = stats.HealthByWave.Count > 0 ? stats.HealthByWave[0] : 0;
            MaxHealthBase = baseHealth;
        }

        /// <summary>
        /// 获取初始血量（healthByWave[0]），供调用方在 <see cref="InitializeStats"/> 后
        /// 传给 <see cref="EnemyBase.Init"/> 完成血量初始化。
        /// </summary>
        /// <returns>第一波基础血量；未初始化或空数组返回 0。</returns>
        /// <remarks>
        /// <para>对应 NormalEnemyBase.js:53 <c>this.health = stats.ph</c>。
        /// EnemyBase.Init 的 maxHealth 参数应传本返回值，使 <c>_currentHealth = maxHealth</c>。</para>
        /// </remarks>
        internal int GetInitialHealth()
        {
            if (!_stats.HasValue)
            {
                return 0;
            }
            var hw = _stats.Value.HealthByWave;
            return hw.Count > 0 ? hw[0] : 0;
        }

        // ====================================================================
        // 死亡表现边界（NormalEnemyBase.js:82-97 beginDeath 合入，裁剪灵魂投射）
        // ====================================================================

        /// <summary>
        /// 开始死亡表现：调用基类死亡逻辑并置死亡表现已开始标记。
        /// </summary>
        /// <remarks>
        /// <para><b>来源（NormalEnemyBase.js:82-97）：</b>
        /// 原 <c>beginDeath</c> 调 <c>super.beginDeath()</c> 后调
        /// <c>presentation.playDeath(this, color, onComplete)</c>，onComplete 回调中
        /// 隐藏表现 → <c>_tryDeliverSoul</c>（本期不移植）→ <c>gameOver()</c>。</para>
        ///
        /// <para><b>C# 移植为规则层边界：</b>
        /// 本方法先调用 <c>base.BeginDeath()</c>（EnemyBase 死亡逻辑：置 deathStarted、
        /// 经 _onEnemyKilled 提交奖励），再置 Mob0 专属的死亡表现边界标记
        /// （_deathPresentationStarted/_deathScheduled），供 Presenter（task 7.4）观察
        /// 并驱动实际死亡表现。表现层在死亡动画播放完成后调用
        /// <see cref="OnDeathPresentationCompleted"/> 通知本类型。</para>
        ///
        /// <para><b>不移植灵魂投射（design.md:316）：</b>
        /// 原 onComplete 中的 <c>_tryDeliverSoul</c> 属 Skill 范围，本期不引入。
        /// soulTowerResolver / soulFlightManager 不注入。</para>
        ///
        /// <para><b>幂等：</b>EnemyBase.BeginDeath 自身有 deathStarted 守卫（js:83），
        /// 本方法额外用 _deathPresentationStarted 守卫 Mob0 表现层死亡表现边界。</para>
        /// </remarks>
        internal override void BeginDeath()
        {
            if (_deathPresentationStarted)
            {
                return;
            }

            // 调用基类死亡逻辑（EnemyBase.BeginDeath：置 _deathStarted、经 _onEnemyKilled 提交奖励）。
            base.BeginDeath();

            _deathPresentationStarted = true;
            _deathScheduled = true;
        }

        /// <summary>
        /// 表现层通知死亡动画播放完成。置已完成标记，触发后续 GameOver 回收。
        /// </summary>
        /// <remarks>
        /// <para><b>来源（NormalEnemyBase.js:87-96 onComplete 回调）：</b>
        /// 原 onComplete 回调在死亡表现完成后执行：隐藏表现 → 灵魂投射（本期不移植）→ gameOver。
        /// C# 移植由表现层（Presenter）在死亡动画播放完成时调用本方法，本方法置
        /// <c>_deathPresentationCompleted</c> 标记并触发 <see cref="EnemyBase.GameOver"/>。</para>
        ///
        /// <para><b>生命周期守卫：</b>
        /// 若对象已回收（InPool）或未调度死亡表现（!_deathScheduled），本方法为空操作，
        /// 防止过期回调修改已回收对象。对应 NormalEnemyBase.js:88
        /// <c>if (generation !== this._lifecycleGeneration || this.inPool) return</c>。</para>
        ///
        /// <para><b>不移植灵魂投射：</b>
        /// 原 onComplete 中 <c>_tryDeliverSoul</c>（js:94）本期不引入。</para>
        /// </remarks>
        internal void OnDeathPresentationCompleted()
        {
            // 生命周期守卫：已回收或未调度时不执行（对应 js:88 generation/inPool 检查）。
            if (InPool || !_deathScheduled)
            {
                return;
            }

            _deathScheduled = false;
            _deathPresentationCompleted = true;

            // 触发回收（对应 js:95 gameOver()）。EnemyBase.GameOver 是幂等的。
            GameOver();
        }

        // ====================================================================
        // 动画清理（NormalEnemyBase.js:204-230 gameOver 合入，裁剪吹飞/Tween）
        // ====================================================================

        /// <summary>
        /// 重置 Mob0 专属可变状态到等价于新构造的状态。
        /// </summary>
        /// <remarks>
        /// <para><b>调用时机：</b>由 <see cref="BattleObjectPool{Mob0Enemy}.Release"/>
        /// 在归还对象前调用（IPoolableBattleObject 契约）。Acquire 复用对象时不再调用。</para>
        ///
        /// <para><b>来源（NormalEnemyBase.js:204-230 gameOver + EnemyBase.js:651-698 gameOver）：</b>
        /// 原 gameOver 清理吹飞定时器、Tween、animation 引用、visual filters。
        /// 本期不移植吹飞，只保留 Mob0 专属字段清理：
        /// 死亡表现边界标记（_deathPresentationStarted/_deathPresentationCompleted/_deathScheduled）
        /// 与数值快照（_stats）。运行时 ID、阵营、生命、路径等由 EnemyBase.ResetState 清除
        /// （EnemyBase.ResetToDefaults 将 _id 置 0，task 4.5 契约：Release 后旧 ID 失效）。</para>
        ///
        /// <para><b>不持有表现组件：</b>
        /// 原 gameOver 清理 animation/visual/filters。C# 移植为纯逻辑层，不持有这些引用；
        /// 表现对象的回收由 Presenter（task 7.4）通过端口完成。</para>
        ///
        /// <para><b>幂等：</b>多次调用安全，结果状态相同。不抛出异常。</para>
        /// </remarks>
        public override void ResetState()
        {
            // 清除 Mob0 专属可变状态。
            _stats = null;
            _deathPresentationStarted = false;
            _deathPresentationCompleted = false;
            _deathScheduled = false;

            // 调用基类 ResetState 清除运行时 ID（_id 置 0）、阵营、生命、路径、目标引用等
            // （对应 EnemyBase.ResetToDefaults + enemy-pool-reset-contract.md）。
            // 基类 ResetState 将 _id 置 0（task 4.5 契约：Release 后旧 ID 失效）。
            base.ResetState();
        }

        // ====================================================================
        // 只读诊断属性（供 Presenter / 测试观察死亡表现边界与数值）
        // ====================================================================

        /// <summary>
        /// 是否已开始死亡表现（对应 NormalEnemyBase.js:83 deathStarted）。
        /// <para>Mob0 专属的表现层死亡边界标记，与 EnemyBase.DeathStarted（规则层）区分。
        /// 供 Presenter 观察是否需要播放死亡动画。</para>
        /// </summary>
        internal bool IsDeathPresentationStarted => _deathPresentationStarted;

        /// <summary>
        /// 死亡表现是否已调度（对应 NormalEnemyBase.js:86 _deathScheduled）。
        /// <para>true 表示已请求表现层播放死亡动画但尚未收到完成回调。</para>
        /// </summary>
        internal bool IsDeathScheduled => _deathScheduled;

        /// <summary>
        /// 死亡表现是否已完成（对应 NormalEnemyBase.js:88-96 onComplete 边界）。
        /// <para>true 表示表现层已通知死亡动画播放完成。</para>
        /// </summary>
        internal bool IsDeathPresentationCompleted => _deathPresentationCompleted;

        /// <summary>
        /// 已初始化的 Mob0 数值快照（null 表示未初始化）。
        /// <para>供 EnemyManager / Presenter 读取 contactDamage/rewardGold 等数值。</para>
        /// </summary>
        internal Mob0EnemyInitStats? Stats => _stats;

        /// <summary>
        /// 运行时 ID（委托 <see cref="EnemyBase.Id"/>，供 EnemyFactoryTests 使用）。
        /// <para>EnemyBase 通过 <c>IEnemyEntity.Id</c> 暴露运行时 ID；本属性提供
        /// EnemyFactoryTests 期望的 RuntimeId 别名。</para>
        /// </summary>
        internal int RuntimeId => Id;

        /// <summary>
        /// 基础移动速度诊断属性（委托 <see cref="EnemyBase.BaseMoveSpeed"/>，供测试观察）。
        /// <para>EnemyBase.BaseMoveSpeed 为 protected，本属性提供 internal 别名供测试验证。</para>
        /// </summary>
        internal int BaseMoveSpeedValue => BaseMoveSpeed;

        /// <summary>
        /// 最大血量基础值诊断属性（委托 <see cref="EnemyBase.MaxHealthBase"/>，供测试观察）。
        /// <para>EnemyBase.MaxHealthBase 为 protected，本属性提供 internal 别名供测试验证。</para>
        /// </summary>
        internal int MaxHealthBaseValue => MaxHealthBase;

        /// <inheritdoc/>
        /// <remarks>
        /// <para>Mob0 命中锚点（Prefab HitEffectPoint=(0,+0.5) 世界单位）反推的逻辑偏移：
        /// Unity 世界向上 +0.5 → 逻辑 Y-down 坐标 -40px，X 为 0。</para>
        /// <para>TODO：迁移到敌人表现配置/Luban（ProjectileAimOffsetX/Y 按体型独立配置）。</para>
        /// </remarks>
        public override float ProjectileAimOffsetX => 0f;

        /// <inheritdoc/>
        /// <remarks>见 <see cref="ProjectileAimOffsetX"/> 注释；Y 偏移 -40 对应命中点低于矩形上边缘。</remarks>
        public override float ProjectileAimOffsetY => -40f;
    }

    // ========================================================================
    // Mob0EnemyInitStats —— Mob0 数值初始化快照（task 39/40 EnemyStats 表产物）
    // ========================================================================

    /// <summary>
    /// Mob0 敌人数值初始化快照，包含 healthByWave/speed/contactDamage/rewardGold。
    /// </summary>
    /// <remarks>
    /// <para><b>数值来源（task 39/40 EnemyStats 表）：</b>
    /// 四个字段来自 Luban EnemyStats 表（GameConfig.battle.EnemyStats:18-58）：
    /// <list type="bullet">
    /// <item><see cref="HealthByWave"/> ← EnemyStats.HealthByWave（各波次基础血量，20 项）</item>
    /// <item><see cref="Speed"/> ← EnemyStats.MoveSpeed（移动速度 px/s，Mob0=50）</item>
    /// <item><see cref="ContactDamage"/> ← EnemyStats.ContactDamage（接触目标伤害，Mob0=1）</item>
    /// <item><see cref="RewardGold"/> ← EnemyStats.RewardGold（击杀奖励金币）</item>
    /// </list></para>
    ///
    /// <para><b>与 EnemyConfigSnapshot 的关系：</b>
    /// EnemyConfigSnapshot（task 3.3）已含 Speed/HealthByWave/ContactDamage，但缺 RewardGold
    /// （EnemyStats 表新增字段，task 39/40）。本结构补充 RewardGold，由 EnemyFactory 或
    /// EnemyManager 从 EnemyStats 表或配置快照构造注入。</para>
    ///
    /// <para><b>不可变值类型：</b>构造后不修改，池复用时整体替换（InitializeStats）。</para>
    /// </remarks>
    internal readonly struct Mob0EnemyInitStats
    {
        /// <summary>各波次基础血量（对应 EnemyStats.HealthByWave / BattleDataCore.normalEnemyHealthByWave）。</summary>
        public readonly System.Collections.Generic.IReadOnlyList<int> HealthByWave;

        /// <summary>移动速度 px/s（对应 EnemyStats.MoveSpeed / BattleDataCore speed=50）。</summary>
        public readonly int Speed;

        /// <summary>接触目标伤害（对应 EnemyStats.ContactDamage，Mob0=1）。</summary>
        public readonly int ContactDamage;

        /// <summary>击杀奖励金币（对应 EnemyStats.RewardGold，task 39/40 新增）。</summary>
        public readonly int RewardGold;

        /// <summary>
        /// 构造 Mob0 数值初始化快照。
        /// </summary>
        /// <param name="healthByWave">各波次基础血量只读列表（不可为 null）。</param>
        /// <param name="speed">移动速度 px/s。</param>
        /// <param name="contactDamage">接触目标伤害。</param>
        /// <param name="rewardGold">击杀奖励金币。</param>
        internal Mob0EnemyInitStats(
            System.Collections.Generic.IReadOnlyList<int> healthByWave,
            int speed,
            int contactDamage,
            int rewardGold)
        {
            HealthByWave = healthByWave
                ?? throw new System.ArgumentNullException(nameof(healthByWave));
            Speed = speed;
            ContactDamage = contactDamage;
            RewardGold = rewardGold;
        }
    }
}
