using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 张飞 BattleShout（大喝）专用 Skill handler
    // ----------------------------------------------------------------------------
    // 职责：
    //   在激活 effect 时点对 owner.CenterX/CenterY、RangeTiles*cellSize 半径内、
    //   当前有效的敌方 IBuffTarget 按 AttackResolver 返回的稳定顺序申请一次
    //   Buff9/2000ms（source=owner runtime id，Flat value 0）。不直接改 Enemy
    //   状态/计时，不造成伤害。
    //
    // 生效时点守卫（与 SoulCaptureHandler 一致的 generation-safe 模式）：
    //   - effect 前通过 UnitRegistry.GetUnit(context.Owner.RuntimeId) 重新解析当前
    //     SoldierBase，校验 active/not pooled、LifecycleGeneration 与 owner.Generation
    //     一致；任一不满足则 no-op（池复用迟到 / owner 已离场）。
    //   - effect 后 owner 离场不清已提交 Buff：已提交 Buff 由 BuffManager 生命周期负责。
    //
    // 不持有独立计时器：effect/complete/cancel 由 SkillRunner 的两节点时间线驱动。
    // ============================================================================

    /// <summary>
    /// 张飞大喝（BattleShout）：对范围内敌方目标施加 Buff9（移动禁用+攻击禁用）/2000ms。
    /// </summary>
    /// <remarks>
    /// <para><b>专用配置：</b>范围/持续/效果 Buff 全部读取
    /// <see cref="SkillDefinitionSnapshot"/>（RangeTiles=3.5 / EffectBuffType=9 /
    /// EffectDurationMs=2000），作为 BattleShout 专用配置而非通用 Skill/Buff DSL。</para>
    /// <para><b>不持有独立计时器：</b>effect/complete/cancel 由
    /// <see cref="SkillRunner"/> 的两节点时间线驱动。</para>
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部注册使用。</para>
    /// </remarks>
    internal sealed class BattleShoutHandler : ISkillHandler
    {
        // ====================================================================
        // 注入依赖（构造时注入，不在上下文访问全局 Runtime）
        // ====================================================================

        /// <summary>BattleShout 技能定义（range/buffType/duration 专用配置）。</summary>
        private readonly SkillDefinitionSnapshot _skillDefinition;

        /// <summary>单位注册表（effect 时重新解析当前 owner SoldierBase）。</summary>
        private readonly UnitRegistry _unitRegistry;

        /// <summary>敌人管理器（effect 时供 AttackResolver 查询敌方目标）。</summary>
        private readonly EnemyManager _enemyManager;

        /// <summary>攻击解析服务（稳定查询敌方目标，不提交伤害）。</summary>
        private readonly AttackResolver _attackResolver;

        /// <summary>Buff 所有者（提交 Buff9 的唯一入口）。</summary>
        private readonly BuffManager _buffManager;

        /// <summary>格子尺寸（px，用于 RangeTiles → 像素半径换算）。</summary>
        private readonly float _cellSize;

        /// <summary>构造 BattleShout handler。</summary>
        /// <param name="skillDefinition">BattleShout 技能定义（不可为 null）。</param>
        /// <param name="unitRegistry">单位注册表（不可为 null）。</param>
        /// <param name="enemyManager">敌人管理器（不可为 null）。</param>
        /// <param name="attackResolver">攻击解析服务（不可为 null）。</param>
        /// <param name="buffManager">Buff 所有者（不可为 null）。</param>
        /// <param name="cellSize">格子尺寸（px，必须为正）。</param>
        /// <exception cref="ArgumentNullException">任一必需参数为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="cellSize"/> 非正。</exception>
        internal BattleShoutHandler(
            SkillDefinitionSnapshot skillDefinition,
            UnitRegistry unitRegistry,
            EnemyManager enemyManager,
            AttackResolver attackResolver,
            BuffManager buffManager,
            float cellSize)
        {
            _skillDefinition = skillDefinition
                ?? throw new ArgumentNullException(nameof(skillDefinition));
            _unitRegistry = unitRegistry ?? throw new ArgumentNullException(nameof(unitRegistry));
            _enemyManager = enemyManager ?? throw new ArgumentNullException(nameof(enemyManager));
            _attackResolver = attackResolver ?? throw new ArgumentNullException(nameof(attackResolver));
            _buffManager = buffManager ?? throw new ArgumentNullException(nameof(buffManager));
            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize), "格子尺寸必须为正，禁止运行时 fallback");
            }

            _cellSize = cellSize;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>在 effect 时点执行：重新解析当前 owner → 校验 generation/active →
        /// 以 owner.CenterX/CenterY、RangeTiles*cellSize、owner.Side 调
        /// AttackResolver.QueryEnemyObjects 稳定查询敌方 → 按返回稳定顺序对当前有效
        /// IBuffTarget 调 BuffManager.Apply，每目标一次 Buff9/2000ms（Flat value 0）。</para>
        /// <para><b>守卫：</b>owner 不存在 / generation 不匹配（池复用迟到）/
        /// not active / in pool 时直接返回。缺专用配置时防御性 no-op（启动 Validator 已拦截）。</para>
        /// </remarks>
        public void Effect(SkillActivationContext context)
        {
            if (context == null)
            {
                return;
            }

            // 重新解析当前 owner：同 generation 且 active/not pooled 才生效。
            SoldierBase owner = _unitRegistry.GetUnit(context.Owner.RuntimeId);
            if (owner == null)
            {
                return;
            }

            if (owner.InPool || !owner.IsActive)
            {
                return;
            }

            if (owner.LifecycleGeneration != context.Owner.Generation)
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
            float range = _skillDefinition.RangeTiles.Value * _cellSize;

            // 以 owner 中心、range 半径、owner.Side 查询敌方目标（稳定顺序）。
            // playerSide=owner.Side：玩家方 owner 查询对手方敌人，反之亦然。
            List<IEnemyEntity> enemies = _attackResolver.QueryEnemyObjects(
                _enemyManager,
                owner.CenterX,
                owner.CenterY,
                range,
                owner.Side,
                _cellSize,
                _cellSize,
                null);

            // 按返回稳定顺序对当前有效 IBuffTarget 调 BuffManager.Apply。
            // source 使用 owner runtime id；每目标一次 Buff9，Flat value 0。
            var source = new BuffSourceHandle(context.Owner.RuntimeId, context.Owner.RuntimeId);
            for (int i = 0; i < enemies.Count; i++)
            {
                if (!(enemies[i] is IBuffTarget target) || !target.IsAvailable)
                {
                    continue;
                }

                _buffManager.Apply(new BuffApplyRequest(
                    buffType,
                    target.Handle,
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
        /// <para><b>effect 前死亡（effectCommitted=false）：</b>激活被取消，不施加 Buff
        /// （Effect 未执行自然无 Buff）。</para>
        /// <para><b>effect 后死亡（effectCommitted=true）：</b>已提交 Buff 继续按自身
        /// duration/目标/全局清理完成，本 handler 不清除（owner 后续离场不撤销）。</para>
        /// </remarks>
        public void Cancel(SkillActivationContext context, bool effectCommitted)
        {
            _ = context;
            _ = effectCommitted;
        }
    }
}
