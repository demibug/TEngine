using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.4：CavalrySweepEffect —— 150ms 骑兵横扫命中效果
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Combat/Effects/CavalrySweepEffect.cs）：
    //   以逻辑时间实现骑兵 150ms 两段伤害。
    //
    // 来源证据（还原工程 CavalrySweepEffect.js:1-80 + CavalrySoldier.js:35-62）：
    //   CavalrySweepEffect 继承 MeleeAttackEffect，核心常量：
    //     CAVALRY_SWEEP_DELAY_MS = 150   (bundle.strings-decoded.js:24825)
    //   launch({owner, target, enemyManager, damage, multiplier=1, radius=96,
    //           delayMs=150, presentation}):
    //     super.launch({owner, enemyManager, damage, multiplier, radius,
    //                   hitAtMs: delay, durationMs: delay+120})   // hitAt=150, dur=270
    //   update/cleanup: 继承 MeleeAttackEffect（elapsed 累计、hit、duration-complete）
    //
    //   骑兵"双段"语义（CavalrySoldier.js:41-58）：
    //     CavalrySoldier.attack() 创建两个 CavalrySweepEffect 实例：
    //       实例1: multiplier=0.5, radius=attackRange/2, delayMs=150
    //       实例2: multiplier=0.5, radius=attackRange,   delayMs=150
    //     两个实例均由 AttackEffectManager 推进，各自在 150ms 时命中（范围查询+去重），
    //     在 270ms 时完成。每个实例独立 hitSet，可命中同一敌人（两段伤害）。
    //     总伤害 = damage*0.5 + damage*0.5 = damage（分两段）。
    //   "双段"由 CavalrySoldier（task 6.2 单位层）创建两个本类型实例实现，
    //   本类型（效果层）为单段 150ms 命中——与 JS CavalrySweepEffect 语义一致。
    //
    // 决策依据：
    //   - design.md 目录表："以逻辑时间实现骑兵 150ms 两段伤害。"
    //   - task 5.4 约束：150ms 双段 CavalrySweepEffect（two damage applications per JS）。
    //     "两段伤害"由单位层创建两个本类型实例实现（匹配 JS CavalrySoldier.attack），
    //     本类型为单段 150ms 命中效果（匹配 JS CavalrySweepEffect）。
    //   - 实现 IAttackEffect + IPoolableBattleObject.ResetState；伤害经 AttackResolver；
    //     尊重 Settling/freeze；中文注释、UTF-8、LF。
    //   - C# 因 IAttackEffect 接口约束与 sealed 设计，不继承 MeleeAttackEffect（JS 继承），
    //     改为独立 sealed class 复用 MeleeAttackEffect 的命中/时序逻辑蓝本。
    //
    // 不变量：
    //   1. 唯一推进：Update 只由 AttackEffectManager.Update 调用。
    //   2. 150ms 命中：elapsed >= 150ms 时 Hit（范围查询+去重+resolver 提交）。
    //   3. 270ms 完成：elapsed >= 270ms 时标记非活动（命中后保留 120ms 回收段）。
    //   4. 伤害经 resolver：Hit 调 resolver.QueryEnemyObjects + resolver.Hit。
    //   5. hitSet 去重：同效果实例内同一敌人只命中一次；两个实例独立 hitSet 可命中同一敌人。
    //   6. multiplier：支持伤害倍率（骑兵双段各 0.5，对应 JS multiplier）。
    //   7. Cancel 不伤害；ResetState 完整清空；幂等。
    // ============================================================================

    /// <summary>
    /// 骑兵 150ms 横扫命中效果：以逻辑时间实现命中时序（task 5.4）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>以逻辑时间实现骑兵 150ms 两段伤害。
    /// 替代还原工程 <c>CavalrySweepEffect.js</c>（继承 MeleeAttackEffect，C# 改为独立 sealed）。</para>
    ///
    /// <para><b>时序常量（对应 JS <c>CAVALRY_SWEEP_DELAY_MS</c>）：</b>
    /// <list type="bullet">
    /// <item><see cref="CavalrySweepDelayMs"/>=150：命中延迟（bundle.strings-decoded.js:24825）</item>
    /// <item>durationMs=270：总持续时间=150+120ms 回收段（对应 JS <c>delay+120</c>）</item>
    /// </list></para>
    ///
    /// <para><b>150ms 命中（task 5.4 "150ms 双段 CavalrySweepEffect"）：</b>
    /// <see cref="Update"/> 累计 <c>elapsed</c>，<c>elapsed &gt;= 150ms</c> 时触发
    /// <see cref="Hit"/>（范围查询+去重+resolver 提交）。<c>elapsed &gt;= 270ms</c> 时
    /// 标记非活动完成。</para>
    ///
    /// <para><b>"双段"语义（对应 JS CavalrySoldier.attack）：</b>
    /// 骑兵单位（task 6.2 CavalrySoldier）创建两个本类型实例——
    /// 实例1：<c>multiplier=0.5, radius=attackRange/2</c>；
    /// 实例2：<c>multiplier=0.5, radius=attackRange</c>。
    /// 两个实例各自在 150ms 命中，独立 hitSet 可命中同一敌人，合计两段伤害。
    /// 本类型为单段 150ms 命中效果，"双段"由单位层创建两实例实现。</para>
    ///
    /// <para><b>伤害经 AttackResolver（task 5.4 约束）：</b>
    /// <see cref="Hit"/> 经 <see cref="AttackResolver.QueryEnemyObjects"/> 查询、
    /// <see cref="AttackResolver.Hit"/> 提交。最终伤害 = damage * multiplier。</para>
    ///
    /// <para><b>Cancel 不伤害（spec "Settling has no gameplay damage authority"）。</b></para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部使用。</para>
    /// </remarks>
    internal sealed class CavalrySweepEffect : IAttackEffect
    {
        // ====================================================================
        // 时序常量（对应 JS CAVALRY_SWEEP_DELAY_MS，bundle.strings-decoded.js:24825）
        // ====================================================================

        /// <summary>骑兵横扫命中延迟（对应 <c>CAVALRY_SWEEP_DELAY_MS=150</c>）。</summary>
        private const long CavalrySweepDelayMs = 150;

        /// <summary>骑兵横扫总持续时间=命中延迟+120ms 回收段（对应 JS <c>delay+120=270</c>）。</summary>
        private const long CavalrySweepDurationMs = CavalrySweepDelayMs + 120; // 270

        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>日志标签前缀。</summary>
        private const string LogTag = "[CavalrySweepEffect]";

        // ====================================================================
        // 活动状态与所有者
        // ====================================================================

        /// <summary>是否活动。</summary>
        private bool _active;

        /// <summary>所有者引用。</summary>
        private IAttackEffectOwner _owner;

        // ====================================================================
        // 外部依赖
        // ====================================================================

        /// <summary>攻击解析服务。</summary>
        private AttackResolver _resolver;

        /// <summary>敌人管理器。</summary>
        private EnemyManager _enemyManager;

        // ====================================================================
        // 攻击参数
        // ====================================================================

        /// <summary>基础伤害值。</summary>
        private int _damage;

        /// <summary>伤害倍率（骑兵双段各 0.5，对应 JS <c>multiplier</c>）。</summary>
        private float _multiplier;

        /// <summary>命中半径（默认 96，对应 JS <c>radius=96</c>；双段实例分别为 attackRange/2 与 attackRange）。</summary>
        private float _radius;

        /// <summary>敌人格子宽。</summary>
        private float _cellWidth;

        /// <summary>敌人格子高。</summary>
        private float _cellHeight;

        // ====================================================================
        // 时序状态
        // ====================================================================

        /// <summary>已累计时长（毫秒）。</summary>
        private long _elapsed;

        /// <summary>是否已命中，防止重复命中。</summary>
        private bool _hitTriggered;

        // ====================================================================
        // 已命中敌人 ID 集合（对应 JS 继承的 hitSet）
        // ====================================================================

        /// <summary>已命中敌人 ID 集合，同效果实例内同一敌人只命中一次。</summary>
        private readonly HashSet<int> _hitSet = new HashSet<int>();

        // ====================================================================
        // IAttackEffect 属性
        // ====================================================================

        /// <inheritdoc />
        public bool Active => _active;

        /// <inheritdoc />
        public object Owner => _owner;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>构造骑兵横扫效果。池工厂调用。</summary>
        internal CavalrySweepEffect()
        {
            ResetState();
        }

        // ====================================================================
        // Launch —— 由单位在 Acquire 后调用
        // --------------------------------------------------------------------
        // 对应 JS CavalrySweepEffect.launch（简化：去掉 presentation port）。
        // hitAtMs 固定 150，durationMs 固定 270。骑兵双段由单位创建两实例（不同 multiplier/radius）。
        // ====================================================================

        /// <summary>
        /// 启动骑兵横扫效果：配置参数并激活。
        /// </summary>
        /// <param name="owner">所有者（非 null）。</param>
        /// <param name="resolver">攻击解析服务（非 null）。</param>
        /// <param name="enemyManager">敌人管理器（非 null）。</param>
        /// <param name="damage">基础伤害值（正数）。</param>
        /// <param name="cellWidth">敌人格子宽。</param>
        /// <param name="cellHeight">敌人格子高。</param>
        /// <param name="multiplier">伤害倍率（骑兵双段各 0.5，对应 JS <c>multiplier</c>）。</param>
        /// <param name="radius">命中半径（对应 JS <c>radius</c>；双段实例不同）。</param>
        /// <returns>this。</returns>
        /// <remarks>
        /// <para><b>对应 JS <c>launch</c>：</b>固定 <c>hitAtMs=CavalrySweepDelayMs(150)</c>、
        /// <c>durationMs=CavalrySweepDurationMs(270)</c>。presentation port（JS DEFERRED 桩）
        /// 本期不移植——规则层命中由 update→hit 驱动，不依赖视觉对象。</para>
        /// <para><b>骑兵双段（对应 JS CavalrySoldier.attack）：</b>单位创建两个本类型实例：
        /// 实例1 multiplier=0.5 radius=attackRange/2；实例2 multiplier=0.5 radius=attackRange。
        /// 两实例各自 150ms 命中，独立 hitSet，合计两段伤害。</para>
        /// </remarks>
        internal CavalrySweepEffect Launch(
            IAttackEffectOwner owner,
            AttackResolver resolver,
            EnemyManager enemyManager,
            int damage,
            float cellWidth,
            float cellHeight,
            float multiplier = 1f,
            float radius = 96f)
        {
            _owner = owner;
            _resolver = resolver;
            _enemyManager = enemyManager;
            _damage = damage;
            _multiplier = multiplier;
            _radius = radius;
            _cellWidth = cellWidth;
            _cellHeight = cellHeight;
            _elapsed = 0;
            _hitTriggered = false;
            _hitSet.Clear();
            _active = true;
            return this;
        }

        // ====================================================================
        // Update —— 唯一推进入口
        // ====================================================================

        /// <inheritdoc />
        /// <summary>
        /// 推进一帧：累计 elapsed，到达 150ms 触发命中，到达 270ms 标记非活动完成。
        /// </summary>
        /// <param name="deltaMs">子步时长（毫秒）。</param>
        /// <remarks>
        /// <para><b>对应 JS 继承的 <c>update</c>：</b>
        /// elapsed += delta; elapsed&gt;=hitAtMs(150) 且未命中 → hit();
        /// elapsed&gt;=durationMs(270) → cleanup（C# 置 active=false）。</para>
        /// </remarks>
        public void Update(long deltaMs)
        {
            if (!_active)
            {
                return;
            }

            if (deltaMs > 0)
            {
                _elapsed += deltaMs;
            }

            // 命中时机：elapsed >= 150ms → 触发命中（对应 JS hitAtMs=CAVALRY_SWEEP_DELAY_MS）。
            if (!_hitTriggered && _elapsed >= CavalrySweepDelayMs)
            {
                _hitTriggered = true;
                Hit();
            }

            // 完成判定：elapsed >= 270ms → 标记非活动（对应 JS durationMs=delay+120）。
            if (_elapsed >= CavalrySweepDurationMs)
            {
                _active = false;
            }
        }

        // ====================================================================
        // Hit —— 经 AttackResolver 查询目标并提交伤害（复用 MeleeAttackEffect 蓝本）
        // ====================================================================

        /// <summary>
        /// 命中结算：查询半径内敌人，经 <see cref="AttackResolver"/> 提交伤害。
        /// </summary>
        /// <remarks>
        /// <para>逻辑与 <see cref="MeleeAttackEffect"/> 的 Hit 一致（JS 中 CavalrySweepEffect
        /// 继承 MeleeAttackEffect.hit）。查询 center 周围 radius 内可攻击目标，hitSet 去重，
        /// resolver.Hit 提交。最终伤害 = damage * multiplier（骑兵双段各 0.5）。</para>
        /// </remarks>
        private void Hit()
        {
            if (!_active || _owner == null || _enemyManager == null || _resolver == null)
            {
                return;
            }

            List<IEnemyEntity> targets = _resolver.QueryEnemyObjects(
                _enemyManager,
                _owner.CenterX, _owner.CenterY,
                _radius,
                _owner.Side,
                _cellWidth, _cellHeight,
                null);

            if (targets == null || targets.Count == 0)
            {
                return;
            }

            // 最终伤害 = damage * multiplier（对应 JS this.damage * this.multiplier）。
            int finalDamage = (int)(_damage * _multiplier);

            int count = targets.Count;
            for (int i = 0; i < count; i++)
            {
                IEnemyEntity target = targets[i];
                if (target == null)
                {
                    continue;
                }

                if (_hitSet.Contains(target.Id))
                {
                    continue;
                }

                _hitSet.Add(target.Id);
                _resolver.Hit(target, finalDamage, _owner.RuntimeId);
            }
        }

        // ====================================================================
        // Cancel —— 取消并清理（不造成伤害）
        // ====================================================================

        /// <inheritdoc />
        public void Cancel(string reason)
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            _owner = null;
            _enemyManager = null;
            _resolver = null;
            _hitSet.Clear();
        }

        // ====================================================================
        // ResetState —— 池回收时清空全部可变状态
        // ====================================================================

        /// <inheritdoc />
        public void ResetState()
        {
            _active = false;
            _owner = null;
            _resolver = null;
            _enemyManager = null;
            _damage = 0;
            _multiplier = 0f;
            _radius = 0f;
            _cellWidth = 0f;
            _cellHeight = 0f;
            _elapsed = 0;
            _hitTriggered = false;
            _hitSet.Clear();
        }
    }
}
