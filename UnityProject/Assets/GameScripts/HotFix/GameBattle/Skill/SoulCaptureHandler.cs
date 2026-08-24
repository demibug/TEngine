using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 6.1/6.3/6.4：SoulCaptureHandler —— 首个生产 Skill handler
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 4 / specs/zhang-liang-boss-runtime/spec.md
    //   "SoulCapture uses the Skill and Buff lifecycles"）：
    //   在激活 effect 时点对同路两格内、alive/current 的 Unit 快照按 runtimeId
    //   升序申请 Buff14/2000ms（source=Boss）。不直接写 Unit 攻击/移动/目标选择/
    //   表现状态，不拥有独立计时器、无 coroutine、不依赖 Unity Time。
    //
    // 生效时点守卫：
    //   - effect 前死亡取消：Effect 先经 EnemyManager 解析当前 Boss，须同 generation、
    //     且非 DEAD 才施加混乱（SkillRunner 的 cancel 在 effect 前死亡时取消激活）。
    //   - effect 后死亡不清已提交 Buff：已提交 Buff 由 Buff runtime 按自身 duration/
    //     目标/全局清理完成，source 离开不清除（Cancel 为 no-op）。
    // ============================================================================

    /// <summary>
    /// 张梁夺魄（SoulCapture）：对同路两格内存活单位按 runtimeId 升序施加 Buff14/2000ms。
    /// </summary>
    /// <remarks>
    /// <para><b>专用配置（design.md 决策 2 / task 4.2）：</b>范围/持续/效果 Buff 全部
    /// 读取 <see cref="SkillDefinitionSnapshot"/>（rangeTiles/effectBuffType/
    /// effectDurationMs），作为 SoulCapture 专用配置而非通用 Skill/Buff DSL。</para>
    /// <para><b>不持有独立计时器：</b>effect/complete/cancel 由
    /// <see cref="SkillRunner"/> 的两节点时间线驱动；时间只读
    /// <see cref="BattleActionScheduler.FrameNowMs"/>。</para>
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部
    /// <see cref="ZhangLiangBossWavePort"/> 注册使用。</para>
    /// </remarks>
    internal sealed class SoulCaptureHandler : ISkillHandler
    {
        // ====================================================================
        // 注入依赖（构造时注入，不在上下文访问全局 Runtime）
        // ====================================================================

        /// <summary>SoulCapture 技能定义（range/buffType/duration 专用配置）。</summary>
        private readonly SkillDefinitionSnapshot _skillDefinition;

        /// <summary>敌人管理器（effect 时解析当前 Boss 是否存活/同 generation）。</summary>
        private readonly EnemyManager _enemyManager;

        /// <summary>单位注册表（effect 时快照活动单位）。</summary>
        private readonly UnitRegistry _unitRegistry;

        /// <summary>Buff 所有者（提交 Buff14 的唯一入口）。</summary>
        private readonly BuffManager _buffManager;

        /// <summary>格子尺寸（px，用于地图格坐标换算）。</summary>
        private readonly float _cellSize;

        /// <summary>构造 SoulCapture handler。</summary>
        /// <param name="skillDefinition">SoulCapture 技能定义（不可为 null）。</param>
        /// <param name="enemyManager">敌人管理器（不可为 null）。</param>
        /// <param name="unitRegistry">单位注册表（不可为 null）。</param>
        /// <param name="buffManager">Buff 所有者（不可为 null）。</param>
        /// <param name="cellSize">格子尺寸（px）。</param>
        /// <exception cref="ArgumentNullException">任一必需参数为 null。</exception>
        internal SoulCaptureHandler(
            SkillDefinitionSnapshot skillDefinition,
            EnemyManager enemyManager,
            UnitRegistry unitRegistry,
            BuffManager buffManager,
            float cellSize)
        {
            _skillDefinition = skillDefinition ?? throw new ArgumentNullException(nameof(skillDefinition));
            _enemyManager = enemyManager ?? throw new ArgumentNullException(nameof(enemyManager));
            _unitRegistry = unitRegistry ?? throw new ArgumentNullException(nameof(unitRegistry));
            _buffManager = buffManager ?? throw new ArgumentNullException(nameof(buffManager));
            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), "格子尺寸必须为正，禁止运行时 fallback");
            }

            _cellSize = cellSize;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>在 effect 时点（激活 + 配置 effect 偏移，SoulCapture=500ms）执行：
        /// 解析当前 Boss → 筛选同路/两格/alive/current 的 Unit 快照 → 按 runtimeId
        /// 升序申请 Buff14/2000ms（Boss 为 source，每目标一次）。</para>
        /// <para><b>守卫：</b>Boss 已不存在 / 非 Boss kind / generation 不匹配（池复用
        /// 迟到）/ DEAD（effect 前死亡）时直接返回，不施加混乱。</para>
        /// </remarks>
        public void Effect(SkillActivationContext context)
        {
            if (context == null)
            {
                return;
            }

            // 解析当前 Boss：同 generation 且存活才生效（spec "Boss dies before effect"）。
            IEnemyEntity boss = _enemyManager.GetById(context.Owner.RuntimeId);
            if (!(boss is IWaveOwnedEnemyEntity waveOwned)
                || waveOwned.WaveKind != WaveEntityKind.Boss)
            {
                return;
            }

            if (waveOwned.CurrentLease.Generation != context.Owner.Generation)
            {
                return;
            }

            if (boss.CurrentState == (int)EnemyRuntimeState.Dead)
            {
                return;
            }

            if (!_skillDefinition.EffectBuffType.HasValue
                || !_skillDefinition.EffectDurationMs.HasValue
                || !_skillDefinition.RangeTiles.HasValue)
            {
                // 缺专用配置：启动校验已拦截，此处防御性 no-op（不 fallback）。
                return;
            }

            int buffType = _skillDefinition.EffectBuffType.Value;
            int durationMs = _skillDefinition.EffectDurationMs.Value;
            float range = _skillDefinition.RangeTiles.Value;

            // Boss 当前地图格（中心点所在格）。
            int bossCellX = (int)Math.Floor((boss.X + boss.Width / 2f) / _cellSize);
            int bossCellY = (int)Math.Floor((boss.Y + boss.Height / 2f) / _cellSize);

            // 快照活动单位并筛选：同 lane、alive/current、两 map cells（曼哈顿距离）。
            var targets = new List<SoldierBase>();
            IReadOnlyList<SoldierBase> units = _unitRegistry.GetActiveUnits();
            for (int i = 0; i < units.Count; i++)
            {
                SoldierBase unit = units[i];
                if (unit.Side != boss.IsPlayerLane)
                {
                    continue;
                }

                if (unit.InPool || !unit.IsActive)
                {
                    continue;
                }

                int dx = Math.Abs(bossCellX - unit.GridX);
                int dy = Math.Abs(bossCellY - unit.GridY);
                if (dx + dy > range)
                {
                    continue;
                }

                targets.Add(unit);
            }

            // 按 runtimeId 升序，保证请求顺序确定（spec "Eligible targets are unordered"）。
            targets.Sort((a, b) => a.Id.CompareTo(b.Id));

            // 以 Boss 为 source，每目标申请一次 Buff14/2000ms。
            var source = new BuffSourceHandle(context.Owner.RuntimeId, context.Owner.RuntimeId);
            for (int i = 0; i < targets.Count; i++)
            {
                _buffManager.Apply(new BuffApplyRequest(
                    buffType,
                    ((IBuffTarget)targets[i]).Handle,
                    source,
                    0d,
                    BuffValueMode.Flat,
                    BuffTimeMode.DurationMs,
                    durationMs));
            }
        }

        /// <inheritdoc/>
        /// <remarks>effect 已提交后无收尾动作（Buff runtime 负责剩余生命周期）。</remarks>
        public void Complete(SkillActivationContext context)
        {
            _ = context;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para><b>effect 前死亡（effectCommitted=false）：</b>激活被取消，不施加混乱
        /// （Effect 未执行自然无 Buff）。</para>
        /// <para><b>effect 后死亡（effectCommitted=true）：</b>已提交 Buff 继续按自身
        /// duration/目标/全局清理完成，本 handler 不清除（spec "Boss dies after effect"）。</para>
        /// </remarks>
        public void Cancel(SkillActivationContext context, bool effectCommitted)
        {
            _ = context;
            _ = effectCommitted;
        }
    }
}
