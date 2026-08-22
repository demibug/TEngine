using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 黄忠 FireArrowBarrage（火箭烈）专用 Skill handler
    // ----------------------------------------------------------------------------
    // 职责：
    //   在激活 effect 时点重新解析当前 owner（generation-safe），以 owner 中心、
    //   RangeTiles*cellSize 半径、owner.Side 经 AttackResolver.QueryTargets 稳定查询
    //   当前敌方目标；按返回稳定顺序每目标计算一次随机箭数并逐箭发射
    //   SimpleDynamicArrow（显式伤害 = EffectiveAttackDamage × EffectDamageMultiplier）。
    //
    // 生效时点守卫（与 BattleShoutHandler/SoulCaptureHandler 一致的 generation-safe 模式）：
    //   - effect 前通过 UnitRegistry.GetUnit(context.Owner.RuntimeId) 重新解析当前
    //     SoldierBase，校验 active/not pooled、LifecycleGeneration 与 owner.Generation
    //     一致；任一不满足则 no-op（池复用迟到 / owner 已离场）。
    //   - effect 后 owner 离场不取消已发射箭矢：ProjectileAttackEffect.Cancel 在发射后
    //     只解除对源单位的引用，已发射投射物继续由 ProjectileManager 推进（task 5.8）。
    //
    // 随机箭数（确定性，spec 固定公式）：
    //   每目标箭数 = IRandomSource.Next(1, 3) * max(1, (owner.UnitLevel - 1) / 2)
    //   的整数除法（(owner.UnitLevel-1)/2 为 C# 整数除法）。
    //   Lv1-4 每目标 1 或 2 箭，Lv5 为 2 或 4 箭；每次随机调用每目标一次，
    //   稳定目标顺序决定随机消费顺序。
    //
    // 每箭伤害：
    //   damage = EffectiveAttackDamage × EffectDamageMultiplier，使用项目一致的确定性
    //   整数取整（MidpointRounding.AwayFromZero，checked/明确溢出保护）。
    //
    // 不持有独立计时器：effect/complete/cancel 由 SkillRunner 的两节点时间线驱动。
    // Complete/Cancel no-op；effect 后 owner 离场不取消已发射箭。
    // ============================================================================

    /// <summary>
    /// 黄忠火箭烈（FireArrowBarrage）：对范围内敌方目标按稳定顺序逐目标随机箭数发射箭矢。
    /// </summary>
    /// <remarks>
    /// <para><b>专用配置：</b>范围/倍率全部读取
    /// <see cref="SkillDefinitionSnapshot"/>（RangeTiles=5.5 / EffectDamageMultiplier=2.0），
    /// 作为 FireArrowBarrage 专用配置而非通用 Skill/效果 DSL。</para>
    /// <para><b>不持有独立计时器：</b>effect/complete/cancel 由
    /// <see cref="SkillRunner"/> 的两节点时间线驱动。</para>
    /// <para><b>每箭复用弓兵既有投射物链：</b><see cref="ProjectileFactory.Acquire"/>
    /// → <c>arrow.Fire(owner.CenterX, owner.CenterY)</c> →
    /// <see cref="ProjectileAttackEffect.Launch"/> → <see cref="AttackEffectManager.Add"/>。
    /// 不直接 Hit/扣血，不用 UnityEngine.Random，不加 Buff，不重定向。</para>
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部注册使用。</para>
    /// </remarks>
    internal sealed class FireArrowBarrageHandler : ISkillHandler
    {
        // ====================================================================
        // 注入依赖（构造时注入，不在上下文访问全局 Runtime）
        // ====================================================================

        /// <summary>FireArrowBarrage 技能定义（range/multiplier 专用配置）。</summary>
        private readonly SkillDefinitionSnapshot _skillDefinition;

        /// <summary>单位注册表（effect 时重新解析当前 owner SoldierBase）。</summary>
        private readonly UnitRegistry _unitRegistry;

        /// <summary>敌人管理器（effect 时供 AttackResolver 查询敌方目标）。</summary>
        private readonly EnemyManager _enemyManager;

        /// <summary>攻击解析服务（稳定查询敌方目标，不提交伤害）。</summary>
        private readonly AttackResolver _attackResolver;

        /// <summary>投射物工厂（逐箭 Acquire SimpleDynamicArrow）。</summary>
        private readonly ProjectileFactory _projectileFactory;

        /// <summary>投射物管理器（供 ProjectileAttackEffect 登记箭矢）。</summary>
        private readonly ProjectileManager _projectileManager;

        /// <summary>攻击效果管理器（登记每支箭的 ProjectileAttackEffect）。</summary>
        private readonly AttackEffectManager _attackEffectManager;

        /// <summary>逻辑随机端口（每目标一次 Next(1, 3)，确定性可复现）。</summary>
        private readonly IRandomSource _randomSource;

        /// <summary>格子尺寸（px，用于 RangeTiles → 像素半径换算）。</summary>
        private readonly float _cellSize;

        /// <summary>构造 FireArrowBarrage handler。</summary>
        /// <param name="skillDefinition">FireArrowBarrage 技能定义（不可为 null）。</param>
        /// <param name="unitRegistry">单位注册表（不可为 null）。</param>
        /// <param name="enemyManager">敌人管理器（不可为 null）。</param>
        /// <param name="attackResolver">攻击解析服务（不可为 null）。</param>
        /// <param name="projectileFactory">投射物工厂（不可为 null）。</param>
        /// <param name="projectileManager">投射物管理器（不可为 null）。</param>
        /// <param name="attackEffectManager">攻击效果管理器（不可为 null）。</param>
        /// <param name="randomSource">逻辑随机端口（不可为 null）。</param>
        /// <param name="cellSize">格子尺寸（px，必须为正）。</param>
        /// <exception cref="ArgumentNullException">任一必需参数为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="cellSize"/> 非正。</exception>
        internal FireArrowBarrageHandler(
            SkillDefinitionSnapshot skillDefinition,
            UnitRegistry unitRegistry,
            EnemyManager enemyManager,
            AttackResolver attackResolver,
            ProjectileFactory projectileFactory,
            ProjectileManager projectileManager,
            AttackEffectManager attackEffectManager,
            IRandomSource randomSource,
            float cellSize)
        {
            _skillDefinition = skillDefinition
                ?? throw new ArgumentNullException(nameof(skillDefinition));
            _unitRegistry = unitRegistry ?? throw new ArgumentNullException(nameof(unitRegistry));
            _enemyManager = enemyManager ?? throw new ArgumentNullException(nameof(enemyManager));
            _attackResolver = attackResolver ?? throw new ArgumentNullException(nameof(attackResolver));
            _projectileFactory = projectileFactory
                ?? throw new ArgumentNullException(nameof(projectileFactory));
            _projectileManager = projectileManager
                ?? throw new ArgumentNullException(nameof(projectileManager));
            _attackEffectManager = attackEffectManager
                ?? throw new ArgumentNullException(nameof(attackEffectManager));
            _randomSource = randomSource ?? throw new ArgumentNullException(nameof(randomSource));
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
        /// AttackResolver.QueryTargets 稳定查询敌方 → 按返回稳定顺序每目标计算一次
        /// 随机箭数（IRandomSource.Next(1, 3) × (UnitLevel-1)/2 整数除法）→ 每箭
        /// ProjectileFactory.Acquire(targetId, owner.Id, attackerDamage, explicitDamage:true,
        /// damage) → arrow.Fire(owner.CenterX, owner.CenterY) →
        /// ProjectileAttackEffect.Launch → AttackEffectManager.Add。</para>
        /// <para><b>守卫：</b>owner 不存在 / generation 不匹配（池复用迟到）/
        /// not active / in pool 时直接返回。缺专用配置（RangeTiles/EffectDamageMultiplier）
        /// 时防御性 no-op（启动 Validator 已拦截）。</para>
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

            if (!_skillDefinition.RangeTiles.HasValue
                || !_skillDefinition.EffectDamageMultiplier.HasValue)
            {
                // 缺专用配置：启动校验已拦截，此处防御性 no-op（不 fallback）。
                return;
            }

            float range = _skillDefinition.RangeTiles.Value * _cellSize;
            float multiplier = _skillDefinition.EffectDamageMultiplier.Value;

            // 以 owner 中心、range 半径、owner.Side 查询敌方目标（稳定顺序）。
            List<EnemyTargetDto> targets = _attackResolver.QueryTargets(
                _enemyManager,
                owner.CenterX,
                owner.CenterY,
                range,
                owner.Side,
                _cellSize,
                _cellSize);

            // 单箭伤害 = 当前有效攻击力 × 技能倍率（确定性整数取整 + 溢出保护）。
            int perArrowDamage = MultiplyDamage(owner.EffectiveAttackDamage, multiplier);

            // 等级因子 = max(1, (UnitLevel - 1) / 2 的整数除法)。
            int levelFactor = owner.UnitLevel > 1 ? (owner.UnitLevel - 1) / 2 : 1;
            if (levelFactor < 1)
            {
                levelFactor = 1;
            }

            // 按返回稳定顺序每目标计算一次随机箭数；每次随机调用每目标一次。
            // 随机消费顺序由稳定目标顺序决定（确定性可复现）。
            float centerX = owner.CenterX;
            float centerY = owner.CenterY;
            int attackerId = owner.Id;
            for (int i = 0; i < targets.Count; i++)
            {
                int roll = _randomSource.Next(1, 3);
                int arrowCount = roll * levelFactor;
                int targetId = targets[i].Id;
                for (int j = 0; j < arrowCount; j++)
                {
                    LaunchArrow(owner, targetId, attackerId, perArrowDamage, centerX, centerY);
                }
            }
        }

        /// <inheritdoc/>
        /// <remarks>effect 已提交后无收尾动作（箭矢生命周期由 ProjectileManager 负责）。</remarks>
        public void Complete(SkillActivationContext context)
        {
            _ = context;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para><b>effect 前死亡（effectCommitted=false）：</b>激活被取消，不发射箭矢
        /// （Effect 未执行自然无箭）。</para>
        /// <para><b>effect 后死亡（effectCommitted=true）：</b>已发射箭矢继续按现有
        /// projectile lifecycle 飞行/命中，本 handler 不取消（ProjectileAttackEffect.Cancel
        /// 在发射后解除对源单位的引用，见 task 5.8）。</para>
        /// </remarks>
        public void Cancel(SkillActivationContext context, bool effectCommitted)
        {
            _ = context;
            _ = effectCommitted;
        }

        // ====================================================================
        // 内部实现
        // ====================================================================

        /// <summary>
        /// 计算单箭伤害 = 有效攻击力 × 倍率（确定性整数取整 + 溢出保护）。
        /// </summary>
        /// <param name="attackDamage">当前有效攻击力。</param>
        /// <param name="multiplier">技能伤害倍率（正数）。</param>
        /// <returns>四舍五入后的单箭伤害（不小于 0）。</returns>
        /// <remarks>
        /// <para>与项目一致的确定性整数取整：<see cref="MidpointRounding.AwayFromZero"/>
        /// （对应 <see cref="UnitLevelService.ResolveDamage"/> 的统一取整约定）。</para>
        /// <para>溢出保护：倍率乘积以 double 计算并 clamp 到 int 范围，负值归 0，
        /// 不依赖未检查的 float→int 转换。</para>
        /// </remarks>
        private static int MultiplyDamage(int attackDamage, float multiplier)
        {
            double raw = (double)attackDamage * multiplier;
            if (raw <= 0d)
            {
                return 0;
            }

            double rounded = Math.Round(raw, MidpointRounding.AwayFromZero);
            return rounded >= int.MaxValue ? int.MaxValue : (int)rounded;
        }

        /// <summary>
        /// 发射单支箭矢并登记效果（严格复用弓兵既有投射物链）。
        /// </summary>
        /// <param name="owner">技能所有者（作为 ProjectileAttackEffect 的 owner 与发射起点）。</param>
        /// <param name="targetId">目标敌人运行时 ID。</param>
        /// <param name="attackerId">攻击者运行时 ID（owner.Id）。</param>
        /// <param name="damage">单箭显式伤害（EffectiveAttackDamage × 倍率）。</param>
        /// <param name="centerX">发射起点逻辑 X（owner 中心）。</param>
        /// <param name="centerY">发射起点逻辑 Y（owner 中心）。</param>
        /// <remarks>
        /// <para>每箭严格复用弓兵既有链：
        /// <see cref="ProjectileFactory.Acquire"/>（attackerDamage=当前有效攻击力，
        /// explicitDamage:true, damage=乘倍率后的单箭伤害）→
        /// <c>arrow.Fire</c> → <see cref="ProjectileAttackEffect.Launch"/> →
        /// <see cref="AttackEffectManager.Add"/>。不直接 Hit/扣血，不加 Buff，不重定向。</para>
        /// </remarks>
        private void LaunchArrow(
            SoldierBase owner,
            int targetId,
            int attackerId,
            int damage,
            float centerX,
            float centerY)
        {
            SimpleDynamicArrow arrow = _projectileFactory.Acquire(
                targetId,
                attackerId,
                attackerDamage: owner.EffectiveAttackDamage,
                explicitDamage: true,
                damage: damage);

            arrow.Fire(centerX, centerY);

            var effect = new ProjectileAttackEffect();
            effect.Launch(owner, _projectileManager, arrow);

            _attackEffectManager.Add(effect);
        }
    }
}
