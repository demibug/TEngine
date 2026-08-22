using System;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.3：BossBase —— Boss 实体基类（复用 Enemy 生命周期 + Boss 身份与技能持有）
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 2 / specs/zhang-liang-boss-runtime/spec.md
    //   "Boss reuses the enemy combat lifecycle with Boss identity"）：
    //   通过 EnemyBase 复用现有敌人移动/终点接触/空间定位/受击/死亡/移除队列/
    //   运行时标识与池世代语义；额外承载 BossKey、定义、租借身份（generation +
    //   waveOrder）、技能 owner 与 auto-cast 时间推进。
    //
    // 技能生命周期（design.md 决策 4）：
    //   - AttachSkill：注册 owner 并 attach SoulCapture（SkillRunner 唯一时间源）。
    //   - Update 内 auto-cast：alive/moving 且 battle clock 到达 firstReady
    //     （spawn+8000ms）后请求 Activate；成功激活暂停移动（SetSkillMovementPause）。
    //   - 完成/取消只恢复同 generation 存活 Boss：每次 Update 检查
    //     SkillRunner.TryGetState，运行中保持暂停，运行结束恢复移动；池复用后
    //     generation 变化使旧句柄 StaleOwner，旧 Boss 不会恢复新租借。
    //   - 死亡/回收：UnregisterOwner 取消运行中激活（effect 前死亡取消混乱，
    //     effect 后不清已提交 Buff）；effect 由 SoulCaptureHandler 提交。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Unity GameObject/表现组件。
    //   2. 池化友好：ResetState 先清技能所有权再复位全部可变字段。
    //   3. 数值无 fallback：maxHealth=roundAwayFromZero(H×healthMultiplier)，
    //      速度/接触伤害/奖励/逻辑尺寸全部来自 Boss 定义。
    // ============================================================================

    /// <summary>
    /// Boss 实体基类：复用 EnemyBase 战斗生命周期，持有 Boss 身份、租借世代与
    /// 最小 SkillRunner auto-cast。
    /// </summary>
    /// <remarks>
    /// <para><b>身份（design.md 决策 2）：</b>薄子类只固定
    /// <see cref="BossKey"/>；定义快照在出生时注入。租借世代/波次所有权以
    /// <see cref="CurrentLease"/> 暴露，供 EnemyManager 幂等移除（
    /// <see cref="IWaveOwnedEnemyEntity.WaveKind"/> 恒为 Boss）。</para>
    /// <para><b>不重复实现奖励/接触伤害（spec "Kill reward commits once"）：</b>
    /// 受击/终点攻击/死亡事实与击杀奖励提交仍由 <see cref="EnemyBase"/> 唯一执行；
    /// 本类型只 override 数值来源（<see cref="EnemyBase.KillRewardValue"/> /
    /// <see cref="EnemyBase.EndPointAttackDamageValue"/>）。</para>
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 Boss 工厂/端口使用。</para>
    /// </remarks>
    internal abstract class BossBase : EnemyBase, IWaveOwnedEnemyEntity
    {
        // ====================================================================
        // 每类型固定身份（薄子类 override）
        // ====================================================================

        /// <summary>Boss 主键（与 BossDefinitionSnapshot.Key 一致）。</summary>
        internal abstract string BossKey { get; }

        // ====================================================================
        // 本次租借状态（BossConfiguredInit 注入；ResetState 清空）
        // ====================================================================

        /// <summary>本次租借的 Boss 定义快照（ResetState 置 null）。</summary>
        private BossDefinitionSnapshot _definition;

        /// <summary>本次租借解析后的实例数值（null=未初始化）。</summary>
        private ConfiguredEnemyResolvedStats? _stats;

        /// <summary>本次租借的资源路径（YooAsset location；ResetState 置空）。</summary>
        private string _resourceAddress;

        /// <summary>租借世代（单调递增，跨租借不重置；供世代守卫）。</summary>
        private long _generation;

        /// <summary>本次租借的波次所有权（waveOrder）。ResetState 清空。</summary>
        private int _waveOrder;

        // ====================================================================
        // 技能持有（AttachSkill 注入；ResetState 清空）
        // ====================================================================

        /// <summary>最小技能运行器（唯一时间源；null=未 attach）。</summary>
        private SkillRunner _skillRunner;

        /// <summary>当前租借的技能 owner 句柄（runtimeId + generation）。</summary>
        private SkillOwnerHandle _owner;

        /// <summary>attach 的技能键（SoulCapture）。</summary>
        private string _skillKey;

        /// <summary>首次可激活的 battle clock 时间戳（spawn + cooldown）。</summary>
        private long _firstReadyMs;

        /// <summary>激活计划（effect/complete 偏移来自 Boss 时间轴配置）。</summary>
        private SkillActivationPlan _plan;

        /// <summary>是否已 attach 技能（幂等守卫）。</summary>
        private bool _skillAttached;

        /// <summary>是否因当前技能运行而暂停移动。</summary>
        private bool _skillPaused;

        /// <summary>Boss 技能动画意图；由 EnemyManager 在登记期订阅。</summary>
        internal event Action<int, string, bool> SkillIntentChanged;

        // ====================================================================
        // 只读属性
        // ====================================================================

        /// <summary>本次租借的 Boss 定义快照（null=未初始化）。</summary>
        internal BossDefinitionSnapshot Definition => _definition;

        /// <summary>本次租借的资源路径（Reset 后为空串）。</summary>
        internal string ResourceAddress => _resourceAddress ?? string.Empty;

        /// <summary>当前租借世代（诊断用）。</summary>
        internal long Generation => _generation;

        /// <summary>当前租借的 waveOrder（Reset 后为 0）。</summary>
        internal int WaveOrder => _waveOrder;

        /// <summary>本次租借解析后的实例数值（null=未初始化）。</summary>
        internal ConfiguredEnemyResolvedStats? Stats => _stats;

        /// <summary>当前租借身份（不可变值对象）：runtimeId + generation + waveOrder。</summary>
        internal EnemyLeaseIdentity CurrentLease => new EnemyLeaseIdentity(Id, _generation, _waveOrder);

        /// <inheritdoc/>
        EnemyLeaseIdentity IWaveOwnedEnemyEntity.CurrentLease => CurrentLease;

        /// <inheritdoc/>
        WaveEntityKind IWaveOwnedEnemyEntity.WaveKind => WaveEntityKind.Boss;

        /// <inheritdoc/>
        protected override long BuffGeneration => _generation;

        // ====================================================================
        // 数值来源 override（所有权仍在 EnemyBase）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>只按 Boss 定义 RewardGold 提供；未注入配置即为装配错误，不使用固定 fallback。</remarks>
        protected override int KillRewardValue => RequireStats().RewardGold;

        /// <inheritdoc/>
        /// <remarks>只按 Boss 定义 ContactDamage 提供；未注入配置即为装配错误，不使用固定 fallback。</remarks>
        protected override int EndPointAttackDamageValue => RequireStats().ContactDamage;

        // ====================================================================
        // 出生初始化（BossFactory.Acquire 调用）
        // ====================================================================

        /// <summary>
        /// 新链出生入口：递增世代、注入 waveOrder/定义/数值与运行时依赖、初始化出生状态。
        /// </summary>
        /// <param name="map">地图数据（不可为 null）。</param>
        /// <param name="cellSize">格子尺寸（px）。</param>
        /// <param name="endPointTarget">终点攻击目标（按车道绑定阿斗）。</param>
        /// <param name="onEnemyKilled">击杀奖励回调（Boss 首次死亡恰好一次）。</param>
        /// <param name="onDeathRequested">死亡请求移除回调。</param>
        /// <param name="stats">解析后的 Boss 数值。</param>
        /// <param name="definition">Boss 定义快照。</param>
        /// <param name="resourceAddress">本次租借的资源路径（与定义同一来源）。</param>
        /// <param name="isPlayerLane">是否玩家方车道。</param>
        /// <param name="waveOrder">波次所有权。</param>
        /// <remarks>
        /// <para><b>世代（task 5.3）：</b>每次租借调用本方法都递增 <see cref="_generation"/>，
        /// 使迟到回调无法命中旧世代。waveOrder 在本次租借内稳定。</para>
        /// <para><b>调用顺序：</b>先注入依赖（Configure），再 Init 初始化出生状态
        /// （出生位置/血量/逻辑尺寸），之后由工厂调用 <see cref="EnemyBase.BeginMoving"/>。</para>
        /// </remarks>
        internal void BossConfiguredInit(
            MapData map,
            float cellSize,
            IEnemyEndPointAttackTarget endPointTarget,
            EnemyKilledHandler onEnemyKilled,
            EnemyDeathRequestHandler onDeathRequested,
            ConfiguredEnemyResolvedStats stats,
            BossDefinitionSnapshot definition,
            string resourceAddress,
            bool isPlayerLane,
            int waveOrder)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            _generation += 1;
            _waveOrder = waveOrder;
            _resourceAddress = resourceAddress ?? string.Empty;
            _stats = stats;
            _definition = definition;
            // 写入基础移动速度（与 ConfiguredEnemyBase.SetResolvedStats 一致），
            // Init 内部 _moveSpeed = _baseMoveSpeed，否则 Boss 会用默认 50 而非配置值。
            BaseMoveSpeed = stats.MoveSpeed;
            Configure(map, cellSize, endPointTarget, onEnemyKilled, onDeathRequested);
            Init(isPlayerLane, stats.MaxHealth, definition.LogicalWidth, definition.LogicalHeight);
        }

        // ====================================================================
        // 技能 attach / 清理
        // ====================================================================

        /// <summary>
        /// 注册 skill owner 并 attach 指定技能（Boss 出生后由端口调用）。
        /// </summary>
        /// <param name="skillRunner">最小技能运行器（不可为 null）。</param>
        /// <param name="skillKey">要 attach 的技能键（如 SoulCapture）。</param>
        /// <param name="firstReadyMs">首次可激活的 battle clock 时间戳（spawn + cooldown）。</param>
        /// <returns>成功（或已 attach 幂等成功）返回 true；注册/attach 失败返回 false。</returns>
        /// <remarks>
        /// <para>以当前 (Id, generation) 建立 owner 租期。失败时先解除已注册 owner，
        /// 返回 false 供端口回滚出生事务。</para>
        /// </remarks>
        internal bool AttachSkill(SkillRunner skillRunner, string skillKey, long firstReadyMs)
        {
            if (skillRunner == null)
            {
                throw new ArgumentNullException(nameof(skillRunner));
            }

            if (_skillAttached)
            {
                return true;
            }

            if (_definition == null || _definition.Timeline == null)
            {
                return false;
            }

            _skillRunner = skillRunner;
            _skillKey = skillKey;
            _owner = new SkillOwnerHandle(Id, _generation);
            _firstReadyMs = firstReadyMs;
            _plan = new SkillActivationPlan(_definition.Timeline.EffectAtMs, _definition.Timeline.CompleteAtMs);

            SkillOperationResult register = skillRunner.RegisterOwner(_owner);
            if (!register.IsSuccess)
            {
                ClearSkillFields();
                return false;
            }

            SkillOperationResult attach = skillRunner.Attach(_owner, skillKey);
            if (!attach.IsSuccess)
            {
                skillRunner.UnregisterOwner(_owner);
                ClearSkillFields();
                return false;
            }

            _skillAttached = true;
            return true;
        }

        /// <summary>
        /// 清除技能所有权：取消运行中激活后清空技能字段（出生回滚/回收前调用）。
        /// </summary>
        /// <remarks>
        /// <para>design.md 决策 7：Boss 租借在 reset/release 前必须清除 Skill 所有权。
        /// 由 <see cref="BossFactory.Release"/> 与 <see cref="ResetState"/> 调用，幂等。</para>
        /// </remarks>
        internal void ClearSkillOwnership()
        {
            if (_skillRunner != null && _skillAttached)
            {
                _skillRunner.UnregisterOwner(_owner);
            }

            ClearSkillFields();
        }

        /// <summary>清空技能字段（不触发 UnregisterOwner，供内部与 Reset 使用）。</summary>
        private void ClearSkillFields()
        {
            if (_skillPaused)
            {
                SkillIntentChanged?.Invoke(Id, _definition?.IdleAnimation ?? string.Empty, false);
            }

            _skillRunner = null;
            _skillKey = null;
            _owner = default;
            _firstReadyMs = 0;
            _plan = null;
            _skillAttached = false;
            _skillPaused = false;
            SetSkillMovementPause(false);
        }

        // ====================================================================
        // Update —— 每帧推进（含 auto-cast）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para><b>auto-cast（design.md 决策 4）：</b>只在 MOVING（alive/moving）状态执行；
        /// EnemyManager 跳过 DEAD/SPAWNING，故死亡/未出生 Boss 不会推进技能。</para>
        /// <para><b>暂停/恢复移动：</b>成功 Activate 暂停移动；每次 Update 经
        /// <see cref="SkillRunner.TryGetState"/> 检查运行态，运行结束（complete/cancel）
        /// 恢复移动。池复用后 generation 变化使旧句柄 StaleOwner，不恢复新租借。</para>
        /// </remarks>
        public override void Update(long deltaMs)
        {
            if (deltaMs <= 0)
            {
                return;
            }

            if (_skillAttached && CurrentStateEnum == EnemyRuntimeState.Moving)
            {
                UpdateAutoCast();
            }

            base.Update(deltaMs);
        }

        /// <summary>auto-cast：首次就绪后请求 Activate，运行期间保持暂停，结束后恢复移动。</summary>
        private void UpdateAutoCast()
        {
            if (_skillRunner == null)
            {
                return;
            }

            if (_skillPaused)
            {
                // 运行中 → 保持暂停；完成/取消 → 只恢复同 generation 存活 Boss 的移动。
                if (_skillRunner.TryGetState(_owner, _skillKey, out SkillStateSnapshot state)
                    && state.IsRunning)
                {
                    return;
                }

                _skillPaused = false;
                SetSkillMovementPause(false);
                SkillIntentChanged?.Invoke(Id, _definition?.IdleAnimation ?? string.Empty, false);
                return;
            }

            long now = _skillRunner.Scheduler.FrameNowMs;
            if (now < _firstReadyMs)
            {
                return;
            }

            SkillOperationResult result = _skillRunner.Activate(_owner, _skillKey, _plan);
            if (result.IsSuccess)
            {
                _skillPaused = true;
                SetSkillMovementPause(true);
                SkillIntentChanged?.Invoke(Id, _definition?.AttackAnimation ?? string.Empty, true);
            }
            // OnCooldown/Busy/StaleOwner 等：本次不暂停，下个 Update 重试。
        }

        // ====================================================================
        // 池化 Reset
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para><b>先清技能所有权（design.md 决策 7）再复位字段：</b>
        /// <see cref="SkillRunner.UnregisterOwner"/> 取消运行中激活（handler.Cancel：
        /// effect 未提交不施加混乱，effect 已提交不清已提交 Buff），随后清空
        /// 定义/数值/资源路径/waveOrder 并委托 <see cref="EnemyBase.ResetState"/>。</para>
        /// <para><b>不重置 <see cref="_generation"/>：</b>保持跨租借单调，供世代守卫
        /// 识别迟到回调。回收后对象除世代计数外等价于新构造。</para>
        /// <para><b>幂等且不抛出：</b>IPoolableBattleObject 契约。</para>
        /// </remarks>
        public override void ResetState()
        {
            if (_skillRunner != null && _skillAttached)
            {
                _skillRunner.UnregisterOwner(_owner);
            }

            _skillRunner = null;
            _skillKey = null;
            _owner = default;
            _firstReadyMs = 0;
            _plan = null;
            _skillAttached = false;
            _skillPaused = false;
            SetSkillMovementPause(false);
            SkillIntentChanged = null;

            _definition = null;
            _stats = null;
            _resourceAddress = null;
            _waveOrder = 0;

            base.ResetState();
        }

        // ====================================================================
        // 辅助
        // ====================================================================

        /// <summary>读取本次租借数值；未注入配置即为装配错误，禁止固定 fallback。</summary>
        private ConfiguredEnemyResolvedStats RequireStats()
        {
            if (!_stats.HasValue)
            {
                throw new InvalidOperationException(
                    $"{BossKey} 尚未注入配置数值，禁止使用固定奖励或接触伤害 fallback");
            }

            return _stats.Value;
        }
    }
}
