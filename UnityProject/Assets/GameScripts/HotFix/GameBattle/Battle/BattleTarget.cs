using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 4.2：BattleTarget —— 双方路径终点目标的纯逻辑受击与存活语义
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / Battle/BattleTarget.cs / design.md:173）：
    //   表示双方路径终点目标及受击语义，委托 BattleState 修改生命。
    //
    // 来源证据（BattleTarget.js:1-69 / EnemyBase.js:420-446）：
    //   还原工程 BattleTarget 是一个挂载在 Laya.Sprite 上的 aDou 节点，持有：
    //     - battleTargetState：CREATED → ACTIVE → DESTROYED/ENDED → POOLED 状态机
    //     - bindBattleTarget({ battleState, playerLaneTarget })：绑定 BattleState 与阵营
    //     - health getter：委托 battleState.playerHealth/opponentHealth
    //     - alive getter：health > 0
    //     - receiveEnemyContact(amount, sourceEnemy)：
    //         1. 校验 amount 为正数；
    //         2. 若 !alive 直接返回 false（拒绝迟到伤害）；
    //         3. 设置 battleState.contactOccurred = true；
    //         4. 扣减 playerHealth/opponentHealth；
    //         5. 记录 damageLog；
    //         6. 若归零则 battleTargetState = DESTROYED；
    //         7. 返回 true。
    //     - Td()（resetSkeletonForPool）：清空引用与 damageLog，置 POOLED。
    //     - gameOver()：置 ENDED。
    //   EnemyBase.attackBattleTarget()（EnemyBase.js:427-446）在 50ms 延迟后调用
    //   target.receiveEnemyContact(1, this) 造成固定 1 点接触伤害。
    //
    // C# 移植决策（design.md:173 / 决策 5 / spec battle-simulation / task 4.2 验证要求）：
    //   1. 纯逻辑：不持有 Laya.Sprite / Unity GameObject / 表现组件。原 aDou 节点的
    //      entityType/animationId/resourcePath/fastMode 等表现字段全部移到表现层
    //      （BattlePresenter / BattleViewRegistry，task 7.x），本类型只保留规则语义。
    //   2. 状态委托：生命值不在本类型内缓存，而是委托 BattleState 的 playerHealth/
    //      opponentHealth 作为权威源（对应原 JS health getter 委托 battleState）。
    //      受击经 BattleState.ApplyDamage 提交，保证 BattleState 仍是唯一权威状态根
    //      （task 3.7 不变量 2：状态修改只允许经 Apply* 方法提交）。
    //   3. 胜负提交：还原工程在 setter 中直接发送 BATTLE_FINISHED（BattleState.js:56-77）；
    //      C# 移植不在 State 内发送事件，胜负冻结经 BattleResultBuilder.TryFreeze 唯一入口
    //      （spec "Battle result is frozen once" / 决策 0.4）。本类型在 ApplyDamage 后
    //      检查受击方生命是否归零，归零时调用 BattleManager.CheckHealthFreeze 触发冻结，
    //      保持"伤害、死亡事实、奖励和胜负候选 MUST 在其发生点同步生效"
    //      （spec "Update phases are explicit and single-owned"）。
    //   4. 拒绝迟到伤害：目标死亡（alive=false）或胜负已冻结（BattleResultBuilder.IsFrozen）
    //      后，ApplyDamage 返回 false 且不修改生命。对应原 JS receiveEnemyContact 的
    //      `if (!this.alive) return false` 守卫，并扩展到冻结后（spec "Settling has no
    //      gameplay damage authority"：首次结果冻结后立即停止新伤害）。
    //   5. 接触标记：原 JS 设置 battleState.contactOccurred=true。C# 经
    //      BattleState.ApplyContactOccurred 提交，保持状态修改只经 Apply* 方法。
    //   6. 每局新建/销毁：不跨局复用，重开由 BattleRuntimeFactory 新建。池复用经
    //      Reset 清空引用与 damageLog，再由 Bind 重新绑定。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Unity GameObject / MonoBehaviour / 表现组件引用。
    //   2. 状态委托：生命值委托 BattleState，不在本类型缓存；受击经 ApplyDamage 提交。
    //   3. 胜负提交：生命归零时经 BattleManager.CheckHealthFreeze 触发 TryFreeze。
    //   4. 拒绝迟到伤害：alive=false 或 IsFrozen 后 ApplyDamage 返回 false 且不修改状态。
    //   5. 每局新建/销毁：不跨局复用，Reset 只清空本局引用供池复用。
    // ============================================================================

    /// <summary>
    /// 双方路径终点目标的纯逻辑受击与存活语义（task 4.2）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:173 BattleTarget）：</b>表示双方路径终点目标及受击语义，
    /// 委托 <see cref="BattleState"/> 修改生命。替代还原工程 <c>BattleTarget.js</c> 中
    /// 挂载在 Laya.Sprite 上的 aDou 节点（<c>BattleTarget.js:12-66</c>）。</para>
    ///
    /// <para><b>纯逻辑（task 4.2 验证要求 / design.md:9）：</b>不持有 Unity GameObject、
    /// MonoBehaviour 或任何表现组件。原 aDou 节点的 <c>entityType</c>/<c>animationId</c>/
    /// <c>resourcePath</c>/<c>fastMode</c> 等表现字段由表现层（<c>BattlePresenter</c>/
    /// <c>BattleViewRegistry</c>，task 7.x）持有，本类型只保留规则语义。</para>
    ///
    /// <para><b>状态委托（task 3.7 不变量 2 / design.md:173）：</b>生命值不在本类型缓存，
    /// 而是委托 <see cref="BattleState"/> 的 <c>PlayerHealth</c>/<c>OpponentHealth</c>
    /// 作为权威源（对应原 JS <c>health</c> getter 委托 <c>battleState</c>）。受击经
    /// <see cref="BattleState.ApplyDamage"/> 提交，保证 <see cref="BattleState"/> 仍是
    /// 唯一权威状态根。</para>
    ///
    /// <para><b>胜负提交（spec "Battle result is frozen once" / 决策 0.4）：</b>
    /// 还原工程在 <c>BattleState.js:56-77</c> setter 中直接发送 <c>BATTLE_FINISHED</c>。
    /// C# 移植不在 State 内发送事件，胜负冻结经 <see cref="BattleResultBuilder.TryFreeze"/>
    /// 唯一入口。本类型在 <see cref="ApplyDamage"/> 后检查受击方生命是否归零，归零时调用
    /// <see cref="BattleManager.CheckHealthFreeze"/> 触发冻结，保持"伤害、死亡事实、奖励和
    /// 胜负候选 MUST 在其发生点同步生效"（spec "Update phases are explicit and
    /// single-owned"）。</para>
    ///
    /// <para><b>拒绝迟到伤害（task 4.2 验证要求 / spec "Settling has no gameplay damage
    /// authority"）：</b>目标死亡（<see cref="IsAlive"/> 为 false）或胜负已冻结
    /// （<see cref="BattleResultBuilder.IsFrozen"/> 为 true）后，<see cref="ApplyDamage"/>
    /// 返回 false 且不修改生命。对应原 JS <c>receiveEnemyContact</c> 的
    /// <c>if (!this.alive) return false</c> 守卫（<c>BattleTarget.js:47</c>），并扩展到
    /// 冻结后（spec "Settling has no gameplay damage authority"：首次结果冻结后立即停止
    /// 新伤害）。</para>
    ///
    /// <para><b>每局新建/销毁（spec "Restart creates clean per-battle state"）：</b>
    /// 不跨局复用，重开由 <see cref="BattleRuntimeFactory"/> 新建。池复用经 <see cref="Reset"/>
    /// 清空引用与 damageLog，再由 <see cref="Bind"/> 重新绑定。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部规则服务（<c>EnemyManager</c>、
    /// <c>AttackResolver</c> 等）调用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class BattleTarget
    {
        // ====================================================================
        // 状态机（对应 BattleTarget.js:25 battleTargetState）
        // ====================================================================

        /// <summary>
        /// 目标生命周期状态（对应还原工程 <c>BattleTarget.js:25</c> 的
        /// <c>battleTargetState</c> 字段）。
        /// </summary>
        /// <remarks>
        /// 还原工程状态流转：<c>CREATED → ACTIVE → DESTROYED/ENDED → POOLED</c>。
        /// C# 移植保持同一状态机语义。
        /// </remarks>
        private enum TargetState
        {
            /// <summary>已创建未绑定（对应 CREATED）。池获取后尚未 Bind。</summary>
            Created = 0,

            /// <summary>已绑定 BattleState 并激活（对应 ACTIVE）。可受击。</summary>
            Active = 1,

            /// <summary>生命归零已摧毁（对应 DESTROYED）。拒绝受击。</summary>
            Destroyed = 2,

            /// <summary>战斗结束已标记（对应 ENDED）。拒绝受击。</summary>
            Ended = 3,

            /// <summary>已回池（对应 POOLED）。引用已清空，不可使用。</summary>
            Pooled = 4,
        }

        // ====================================================================
        // 只读依赖（Bind 后注入，Reset 后清空）
        // ====================================================================

        /// <summary>
        /// 关联的权威战斗状态。受击经其 <c>ApplyDamage</c> 提交，生命值委托其
        /// <c>PlayerHealth</c>/<c>OpponentHealth</c>。Reset 后置 null。
        /// </summary>
        private BattleState _state;

        /// <summary>
        /// 关联的战斗规则协调器。生命归零时调用其 <c>CheckHealthFreeze</c> 触发胜负冻结。
        /// Reset 后置 null。
        /// </summary>
        /// <remarks>
        /// 胜负提交经 <see cref="BattleManager.CheckHealthFreeze"/> →
        /// <see cref="BattleManager.TryFreezeResult"/> →
        /// <see cref="BattleResultBuilder.TryFreeze"/> 唯一入口，保证"第一个完成事实胜出"
        /// （决策 1.4 / spec "Battle result is frozen once"）。
        /// </remarks>
        private BattleManager _manager;

        /// <summary>
        /// 关联的结果冻结器。用于检查 <see cref="BattleResultBuilder.IsFrozen"/> 以拒绝
        /// 胜负冻结后的迟到伤害。Reset 后置 null。
        /// </summary>
        /// <remarks>
        /// 本类型不直接调用 <see cref="BattleResultBuilder.TryFreeze"/>，胜负提交经
        /// <see cref="BattleManager.CheckHealthFreeze"/> 唯一路径。只读取
        /// <see cref="BattleResultBuilder.IsFrozen"/> 做迟到伤害门控。
        /// </remarks>
        private BattleResultBuilder _resultBuilder;

        // ====================================================================
        // 单局可变状态
        // ====================================================================

        /// <summary>
        /// 是否为玩家方路径终点目标（对应 <c>isPlayerLaneTarget</c>）。
        /// true=玩家方目标（受击扣 playerHealth），false=对手方目标（受击扣 opponentHealth）。
        /// </summary>
        private bool _isPlayerLaneTarget;

        /// <summary>
        /// 目标生命周期状态。
        /// </summary>
        private TargetState _targetState;

        /// <summary>
        /// 伤害日志（对应 <c>BattleTarget.js:24 damageLog</c>）。
        /// 记录每次受击的伤害值、受击前后生命与来源敌人运行时 ID，供诊断与黄金轨迹对照。
        /// </summary>
        /// <remarks>
        /// 本字段为诊断用途，不影响规则判定。Reset 时清空。
        /// </remarks>
        private readonly List<TargetDamageRecord> _damageLog = new List<TargetDamageRecord>();

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造一个未绑定的目标。池获取后需调用 <see cref="Bind"/> 绑定阵营与状态。
        /// </summary>
        /// <remarks>
        /// 对应还原工程 <c>BattleTarget.js:25</c> <c>battleTargetState='CREATED'</c>。
        /// 由 <c>BattleRuntimeFactory</c>（或后续 EnemyManager 的目标注册步骤）在每局
        /// 创建两个实例：一个玩家方、一个对手方。
        /// </remarks>
        internal BattleTarget()
        {
            _state = null;
            _manager = null;
            _resultBuilder = null;
            _isPlayerLaneTarget = false;
            _targetState = TargetState.Created;
        }

        // ====================================================================
        // 只读属性
        // ====================================================================

        /// <summary>
        /// 当前生命值（对应 <c>BattleTarget.js:36-42 health getter</c>）。
        /// 委托 <see cref="BattleState"/> 的 <c>PlayerHealth</c>/<c>OpponentHealth</c>。
        /// </summary>
        /// <remarks>
        /// 未绑定（<see cref="IsBound"/> 为 false）时返回 0，对应原 JS
        /// <c>!this.battleState ? null</c> 的安全降级（C# 用 0 代替 null）。
        /// </remarks>
        internal int Health
        {
            get
            {
                if (_state == null)
                {
                    return 0;
                }

                return _isPlayerLaneTarget ? _state.PlayerHealth : _state.OpponentHealth;
            }
        }

        /// <summary>
        /// 最大生命值。委托 <see cref="BattleState"/> 的
        /// <c>PlayerMaxHealth</c>/<c>OpponentMaxHealth</c>。
        /// </summary>
        internal int MaxHealth
        {
            get
            {
                if (_state == null)
                {
                    return 0;
                }

                return _isPlayerLaneTarget ? _state.PlayerMaxHealth : _state.OpponentMaxHealth;
            }
        }

        /// <summary>
        /// 是否存活（对应 <c>BattleTarget.js:42 alive getter</c>）。
        /// </summary>
        /// <remarks>
        /// 对应原 JS <c>this.health == null ? false : this.health > 0</c>。
        /// C# 移植：<see cref="Health"/> 为 0 时返回 false（已绑定但归零）或未绑定时返回 false。
        /// </remarks>
        internal bool IsAlive => Health > 0;

        /// <summary>
        /// 是否已绑定到 BattleState 与阵营（对应 <c>battleTargetState === 'ACTIVE'</c>）。
        /// </summary>
        internal bool IsBound => _targetState == TargetState.Active && _state != null;

        /// <summary>
        /// 是否为玩家方路径终点目标。
        /// </summary>
        internal bool IsPlayerLaneTarget => _isPlayerLaneTarget;

        /// <summary>
        /// 是否已被摧毁（生命归零）。对应 <c>battleTargetState === 'DESTROYED'</c>。
        /// </summary>
        internal bool IsDestroyed => _targetState == TargetState.Destroyed;

        /// <summary>
        /// 是否已回池（引用已清空，不可使用）。对应 <c>battleTargetState === 'POOLED'</c>。
        /// </summary>
        internal bool IsPooled => _targetState == TargetState.Pooled;

        /// <summary>
        /// 伤害日志只读快照（对应 <c>damageLog</c>）。供诊断与黄金轨迹对照。
        /// </summary>
        /// <remarks>
        /// 返回列表副本，调用方修改不影响内部日志。本属性为诊断用途，
        /// 不参与规则判定。
        /// </remarks>
        internal IReadOnlyList<TargetDamageRecord> DamageLog => _damageLog;

        // ====================================================================
        // 绑定与重置（对应 BattleTarget.js:26-33 bindBattleTarget / 56-63 Td）
        // ====================================================================

        /// <summary>
        /// 绑定到 BattleState 与阵营，激活目标（对应 <c>bindBattleTarget</c>）。
        /// </summary>
        /// <param name="state">权威战斗状态（非 null）。</param>
        /// <param name="manager">战斗规则协调器（非 null），用于生命归零时触发胜负冻结。</param>
        /// <param name="resultBuilder">结果冻结器（非 null），用于检查 <see cref="BattleResultBuilder.IsFrozen"/> 拒绝迟到伤害。</param>
        /// <param name="isPlayerLaneTarget">true=玩家方路径终点目标，false=对手方。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="state"/>、<paramref name="manager"/> 或
        /// <paramref name="resultBuilder"/> 为 null。
        /// </exception>
        /// <remarks>
        /// <para>对应还原工程 <c>BattleTarget.js:26-33</c>：
        /// <code>
        /// bindBattleTarget({ battleState, playerLaneTarget }) {
        ///   this.battleState = battleState;
        ///   this.isPlayerLaneTarget = Boolean(playerLaneTarget);
        ///   this.side = this.isPlayerLaneTarget;
        ///   this.battleTargetState = 'ACTIVE';
        /// }
        /// </code></para>
        /// <para>C# 移植额外注入 <see cref="BattleManager"/> 与 <see cref="BattleResultBuilder"/>：
        /// <list type="bullet">
        /// <item><see cref="BattleManager"/>：胜负提交经
        /// <see cref="BattleManager.CheckHealthFreeze"/> → <see cref="BattleResultBuilder.TryFreeze"/>
        /// 唯一入口（决策 0.4 / spec "Battle result is frozen once"），而非在 State setter 中
        /// 直接发送 <c>BATTLE_FINISHED</c>。</item>
        /// <item><see cref="BattleResultBuilder"/>：读取 <see cref="BattleResultBuilder.IsFrozen"/>
        /// 拒绝胜负冻结后的迟到伤害（spec "Settling has no gameplay damage authority"）。
        /// 本类型不直接调用其 <c>TryFreeze</c>，胜负提交经
        /// <see cref="BattleManager.CheckHealthFreeze"/> 唯一路径。</item>
        /// </list></para>
        /// <para>池复用：Reset 后可重新 Bind 到新局 BattleState/Manager。</para>
        /// </remarks>
        internal void Bind(
            BattleState state,
            BattleManager manager,
            BattleResultBuilder resultBuilder,
            bool isPlayerLaneTarget)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _resultBuilder = resultBuilder ?? throw new ArgumentNullException(nameof(resultBuilder));
            _isPlayerLaneTarget = isPlayerLaneTarget;
            _targetState = TargetState.Active;
            _damageLog.Clear();
        }

        /// <summary>
        /// 重置目标到未绑定状态，供池复用（对应 <c>Td() / resetSkeletonForPool</c>）。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>BattleTarget.js:56-63</c>：
        /// <code>
        // * Td() {
        ///   this.removeSelf();
        ///   this.battleTargetState = 'POOLED';
        ///   this.battleState = null;
        ///   this.isPlayerLaneTarget = null;
        ///   this.side = null;
        ///   this.damageLog.length = 0;
        /// }
        /// </code></para>
        /// <para>C# 移植不持有 Laya.Sprite，故无 <c>removeSelf()</c>。其余语义一致：
        /// 置 <see cref="TargetState.Pooled"/>、清空引用与 damageLog。</para>
        /// <para>重开新局时由 <c>BattleRuntimeFactory</c> 新建 Target，不复用旧实例的
        /// 可变状态。池复用场景（task 4.1）在 Reset 后重新 <see cref="Bind"/>。</para>
        /// </remarks>
        internal void Reset()
        {
            _state = null;
            _manager = null;
            _resultBuilder = null;
            _isPlayerLaneTarget = false;
            _targetState = TargetState.Pooled;
            _damageLog.Clear();
        }

        /// <summary>
        /// 标记战斗结束（对应 <c>gameOver()</c>）。
        /// </summary>
        /// <remarks>
        /// 对应还原工程 <c>BattleTarget.js:64</c> <c>gameOver() { this.battleTargetState = 'ENDED'; }</c>。
        /// 由 <c>BattleRuntime</c> 在 Settling 静默清理时调用，标记目标已结束。
        /// 调用后 <see cref="ApplyDamage"/> 拒绝任何迟到伤害。
        /// </remarks>
        internal void MarkEnded()
        {
            if (_targetState == TargetState.Pooled)
            {
                // 已回池不再标记，避免状态倒退。
                return;
            }

            _targetState = TargetState.Ended;
        }

        // ====================================================================
        // 受击（对应 BattleTarget.js:44-55 receiveEnemyContact）
        // ====================================================================

        /// <summary>
        /// 对目标施加伤害（对应 <c>receiveEnemyContact</c>）。
        /// </summary>
        /// <param name="amount">伤害值（必须为正数）。</param>
        /// <param name="sourceRuntimeId">
        /// 来源敌人运行时 ID（用于伤害日志，0 表示未知来源）。对应原 JS
        /// <c>sourceEnemy ? sourceEnemy.id : null</c>。
        /// </param>
        /// <returns>
        /// true=本次伤害成功施加；false=被拒绝（目标已死亡、已结束或胜负已冻结）。
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// 目标未绑定（<see cref="IsBound"/> 为 false）。
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="amount"/> 非正数。对应原 JS <c>TypeError</c>。
        /// </exception>
        /// <remarks>
        /// <para><b>对应还原工程 <c>BattleTarget.js:44-55</c>：</b>
        /// <code>
        /// receiveEnemyContact(amount, sourceEnemy) {
        ///   if (!this.battleState) throw new Error('...');
        ///   if (!Number.isFinite(amount) || amount <= 0) throw new TypeError('...');
        ///   if (!this.alive) return false;
        ///   const before = this.health;
        ///   this.battleState.contactOccurred = true;
        ///   if (this.isPlayerLaneTarget) this.battleState.playerHealth -= amount;
        ///   else this.battleState.opponentHealth -= amount;
        ///   this.damageLog.push({ amount, before, after: this.health, sourceEnemyId: ... });
        ///   if (!this.alive) this.battleTargetState = 'DESTROYED';
        ///   return true;
        /// }
        /// </code></para>
        ///
        /// <para><b>状态委托（task 3.7 不变量 2 / design.md:173）：</b>
        /// 伤害经 <see cref="BattleState.ApplyDamage"/> 提交，不在本类型直接修改生命字段。
        /// 保证 <see cref="BattleState"/> 仍是唯一权威状态根。接触标记经
        /// <see cref="BattleState.ApplyContactOccurred"/> 提交，对应原 JS
        /// <c>this.battleState.contactOccurred = true</c>（<c>BattleTarget.js:49</c>）。</para>
        ///
        /// <para><b>拒绝迟到伤害（task 4.2 验证要求）：</b>
        /// 以下情况返回 false 且不修改任何状态：
        /// <list type="bullet">
        /// <item>目标已死亡（<see cref="IsAlive"/> 为 false）：对应原 JS
        /// <c>if (!this.alive) return false</c>。</item>
        /// <item>目标已结束（<see cref="_targetState"/> 为 <see cref="TargetState.Ended"/>）：
        /// 战斗已进入 Settling，目标不再受击。</item>
        /// <item>胜负已冻结（<see cref="BattleResultBuilder.IsFrozen"/> 为 true）：
        /// spec "Settling has no gameplay damage authority"——首次结果冻结后立即停止新伤害。
        /// 本检查扩展了原 JS 仅判 <c>alive</c> 的守卫，覆盖冻结后迟到伤害场景
        /// （如空中弹道在冻结后才命中）。</item>
        /// </list></para>
        ///
        /// <para><b>胜负提交（spec "Update phases are explicit and single-owned" /
        /// 决策 0.4）：</b>
        /// 伤害施加后检查受击方生命是否归零。归零时：
        /// <list type="bullet">
        /// <item>置 <see cref="TargetState.Destroyed"/>（对应原 JS
        /// <c>this.battleTargetState = 'DESTROYED'</c>）。</item>
        /// <item>调用 <see cref="BattleManager.CheckHealthFreeze"/> 触发胜负冻结。
        /// 对应还原工程在 <c>BattleState.js:56-77</c> setter 中直接发送
        /// <c>BATTLE_FINISHED</c>，C# 移植统一经 <see cref="BattleResultBuilder.TryFreeze"/>
        /// 唯一入口（spec "Battle result is frozen once"）。</item>
        /// <item><see cref="BattleManager.CheckHealthFreeze"/> 内部判断
        /// <c>health &lt;= 0</c> 后调用 <c>TryFreezeResult(playerWin: !isPlayerSide)</c>，
        /// 玩家方归零 → 玩家失败，对手方归零 → 玩家胜利。</item>
        /// </list>
        /// 伤害、死亡事实与胜负候选在受击发生点同步生效，不推迟到帧末
        /// （spec "Update phases are explicit and single-owned"）。</para>
        ///
        /// <para><b>不在伤害调用栈内重入销毁（spec "Freeze occurs inside a manager update"
        /// / 决策 0.4）：</b>
        /// 本方法只委托 <see cref="BattleManager.CheckHealthFreeze"/> 冻结结果，
        /// 不直接销毁 Manager 或集合。<see cref="BattleManager.TryFreezeResult"/> 只冻结
        /// 结果快照并置位标记，集合清理由 <c>BattleRuntime.EnterSettling</c> 在静默检查点
        /// 统一执行。本方法在 <see cref="BattleManager.CheckHealthFreeze"/> 返回后正常完成
        /// 当前同步提交并返回 true。</para>
        /// </remarks>
        internal bool ApplyDamage(int amount, int sourceRuntimeId = 0)
        {
            // 校验绑定状态：未绑定的目标不能受击。
            // 对应原 JS if (!this.battleState) throw new Error('...')（BattleTarget.js:45）。
            if (_state == null || _manager == null || _resultBuilder == null
                || _targetState != TargetState.Active)
            {
                throw new InvalidOperationException(
                    "[BattleTarget] 目标未绑定或非 Active 状态，不能受击。先调用 Bind。");
            }

            // 校验伤害值：必须为正数。
            // 对应原 JS if (!Number.isFinite(amount) || amount <= 0) throw new TypeError(...)。
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount),
                    "[BattleTarget] 伤害值必须为正数。");
            }

            // 拒绝迟到伤害：目标已死亡。
            // 对应原 JS if (!this.alive) return false（BattleTarget.js:47）。
            if (!IsAlive)
            {
                return false;
            }

            // 拒绝迟到伤害：胜负已冻结。
            // 扩展守卫：spec "Settling has no gameplay damage authority"——首次结果冻结后
            // 立即停止新伤害。覆盖空中弹道在冻结后才命中的迟到场景。
            // 决策 0.4：首次 TryFreeze 成功后只完成当前同步提交并中止剩余 phase/子步；
            // 后续同帧或相邻帧的完成事实被忽略。
            if (_resultBuilder.IsFrozen)
            {
                return false;
            }

            // 记录受击前生命（对应原 JS const before = this.health）。
            int before = Health;

            // 接触标记：经 BattleState.ApplyContactOccurred 提交。
            // 对应原 JS this.battleState.contactOccurred = true（BattleTarget.js:49）。
            // contactOccurred 用于防止重复接触，由规则服务经 Apply 方法提交。
            _state.ApplyContactOccurred();

            // 伤害提交：经 BattleState.ApplyDamage 委托，不在本类型直接修改生命。
            // 对应原 JS this.battleState.playerHealth -= amount / opponentHealth -= amount。
            // task 3.7 不变量 2：状态修改只允许经 Apply* 方法提交。
            _state.ApplyDamage(_isPlayerLaneTarget, amount);

            // 记录伤害日志（对应原 JS this.damageLog.push({...})）。
            int after = Health;
            _damageLog.Add(new TargetDamageRecord(
                amount: amount,
                before: before,
                after: after,
                sourceRuntimeId: sourceRuntimeId));

            // 生命归零：标记摧毁并触发胜负冻结。
            // 对应原 JS if (!this.alive) this.battleTargetState = 'DESTROYED'。
            if (!IsAlive)
            {
                _targetState = TargetState.Destroyed;

                // 胜负提交：经 BattleManager.CheckHealthFreeze 触发 TryFreeze 唯一入口。
                // 对应还原工程在 BattleState.js:56-77 setter 中直接发送 BATTLE_FINISHED；
                // C# 移植统一经 BattleResultBuilder.TryFreeze（spec "Battle result is frozen
                // once" / 决策 0.4）。CheckHealthFreeze 内部判断 health<=0 后调用
                // TryFreezeResult(playerWin: !isPlayerSide)。
                //
                // 不在伤害调用栈内重入销毁（spec "Freeze occurs inside a manager update"）：
                // CheckHealthFreeze 只委托 TryFreeze 冻结结果快照并置位标记，
                // 不销毁 Manager 或集合。本方法在 CheckHealthFreeze 返回后正常完成提交。
                _manager.CheckHealthFreeze(_isPlayerLaneTarget);
            }

            return true;
        }
    }

    // ========================================================================
    // 伤害记录（对应 BattleTarget.js:52 damageLog 条目）
    // ========================================================================

    /// <summary>
    /// 目标受击伤害记录，供诊断与黄金轨迹对照（对应还原工程
    /// <c>BattleTarget.js:52</c> 的 <c>damageLog</c> 条目）。
    /// </summary>
    /// <remarks>
    /// <para>字段映射（对应原 JS <c>{ amount, before, after, sourceEnemyId }</c>）：</para>
    /// <list type="bullet">
    /// <item><see cref="Amount"/>：伤害值。</item>
    /// <item><see cref="Before"/>：受击前生命。</item>
    /// <item><see cref="After"/>：受击后生命。</item>
    /// <item><see cref="SourceRuntimeId"/>：来源敌人运行时 ID（0 表示未知来源）。</item>
    /// </list>
    /// <para>本类型为诊断用途，不影响规则判定。Reset 时随 <see cref="BattleTarget"/>
    /// 一并清空。</para>
    /// </remarks>
    internal readonly struct TargetDamageRecord
    {
        /// <summary>伤害值（正数）。</summary>
        public readonly int Amount;

        /// <summary>受击前生命。</summary>
        public readonly int Before;

        /// <summary>受击后生命（不低于 0）。</summary>
        public readonly int After;

        /// <summary>
        /// 来源敌人运行时 ID（0 表示未知来源）。
        /// 对应原 JS <c>sourceEnemy ? sourceEnemy.id : null</c>，C# 用 0 代替 null。
        /// </summary>
        public readonly int SourceRuntimeId;

        /// <summary>
        /// 构造伤害记录。
        /// </summary>
        /// <param name="amount">伤害值。</param>
        /// <param name="before">受击前生命。</param>
        /// <param name="after">受击后生命。</param>
        /// <param name="sourceRuntimeId">来源敌人运行时 ID（0 表示未知）。</param>
        internal TargetDamageRecord(int amount, int before, int after, int sourceRuntimeId)
        {
            Amount = amount;
            Before = before;
            After = after;
            SourceRuntimeId = sourceRuntimeId;
        }
    }
}
