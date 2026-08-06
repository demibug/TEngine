using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.4：PikeAttackEffect —— 360ms 枪兵延迟命中效果
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Combat/Effects/PikeAttackEffect.cs）：
    //   以逻辑时间实现枪兵 360ms 命中。
    //
    // 来源证据（还原工程 PikeAttackEffect.js:1-111）：
    //   PikeAttackEffect 继承 MeleeAttackEffect，核心常量：
    //     PIKE_ATTACK_ROTATE_MS = 90      旋转段
    //     PIKE_ATTACK_THRUST_MS = 270     突刺段
    //     PIKE_HIT_DELAY_MS = 90+270 = 360   命中时机（旋转+突刺后）
    //     PIKE_EFFECT_DURATION_MS = 360+120 = 480   总持续时间（命中后保留 120ms 回收）
    //   launch({owner, target, enemyManager, damage, radius=48, playbackRate=1,
    //           durationMs=null, animationEventTimingProvider, pikeTipPresentation}):
    //     hitAtMs = PIKE_HIT_DELAY_MS / playbackRate   (=360 when rate=1)
    //     duration = durationMs ?? PIKE_EFFECT_DURATION_MS / playbackRate   (=480 when rate=1)
    //     super.launch({owner, enemyManager, damage, radius, durationMs:duration, hitAtMs})
    //   update/cleanup: 继承 MeleeAttackEffect（elapsed 累计、hit、duration-complete）
    //   calibrateHitTiming: 动画事件校准钩子（正式 Spine 接入用，当前 no-op 基线）
    //
    // 决策依据：
    //   - design.md 目录表："以逻辑时间实现枪兵 360ms 命中。"
    //   - task 5.4 约束：360ms PikeAttackEffect；实现 IAttackEffect +
    //     IPoolableBattleObject.ResetState；伤害经 AttackResolver；尊重 Settling/freeze；
    //     中文注释、UTF-8、LF。
    //   - C# 因 IAttackEffect 接口约束与 sealed 设计，PikeAttackEffect 不继承
    //     MeleeAttackEffect（JS 继承），改为独立 sealed class 复用 MeleeAttackEffect 的
    //     命中/时序逻辑蓝本。共享逻辑（hit 查询+去重+resolver 提交）在本类型内独立实现。
    //   - task 5.4 playbackRate：task 指定 360ms 固定值，playbackRate 默认 1.0，
    //     本类型固定 hitAtMs=360/durationMs=480，不支持变速（与最简移植一致）。
    //
    // 不变量：
    //   1. 唯一推进：Update 只由 AttackEffectManager.Update 调用。
    //   2. 360ms 命中：elapsed >= 360ms 时 Hit（范围查询+去重+resolver 提交）。
    //   3. 480ms 完成：elapsed >= 480ms 时标记非活动（命中后保留 120ms 回收段）。
    //   4. 伤害经 resolver：Hit 调 resolver.QueryEnemyObjects + resolver.Hit。
    //   5. hitSet 去重：同效果内同一敌人只命中一次。
    //   6. Cancel 不伤害；ResetState 完整清空；幂等。
    // ============================================================================

    /// <summary>
    /// 枪兵 360ms 延迟命中效果：以逻辑时间实现命中时序（task 5.4）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>以逻辑时间实现枪兵 360ms 命中。
    /// 替代还原工程 <c>PikeAttackEffect.js</c>（继承 MeleeAttackEffect，C# 改为独立 sealed）。</para>
    ///
    /// <para><b>时序常量（对应 JS <c>PIKE_*_MS</c>）：</b>
    /// <list type="bullet">
    /// <item><see cref="PikeAttackRotateMs"/>=90：旋转段（bundle 段1）</item>
    /// <item><see cref="PikeAttackThrustMs"/>=270：突刺段（bundle 段2）</item>
    /// <item><see cref="PikeHitDelayMs"/>=360：命中时机=旋转+突刺（段3 onStart 等价常量）</item>
    /// <item><see cref="PikeEffectDurationMs"/>=480：总持续时间=命中+120ms 回收段</item>
    /// </list></para>
    ///
    /// <para><b>360ms 命中（task 5.4 "360ms PikeAttackEffect"）：</b>
    /// <see cref="Update"/> 累计 <c>elapsed</c>，<c>elapsed &gt;= 360ms</c> 时触发
    /// <see cref="Hit"/>（范围查询+去重+resolver 提交）。<c>elapsed &gt;= 480ms</c> 时
    /// 标记非活动完成（命中后保留 120ms 回收段，对应 JS <c>PIKE_EFFECT_DURATION_MS</c>）。</para>
    ///
    /// <para><b>伤害经 AttackResolver（task 5.4 约束）：</b>
    /// <see cref="Hit"/> 经 <see cref="AttackResolver.QueryEnemyObjects"/> 查询、
    /// <see cref="AttackResolver.Hit"/> 提交，不直接 <see cref="IEnemyEntity.Hit"/>。</para>
    ///
    /// <para><b>hitSet 去重：</b>同效果内同一敌人只命中一次（对应 JS 继承的 hitSet）。</para>
    ///
    /// <para><b>Cancel 不伤害（spec "Settling has no gameplay damage authority"）。</b></para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部使用。</para>
    /// </remarks>
    internal sealed class PikeAttackEffect : IAttackEffect
    {
        // ====================================================================
        // 时序常量（对应 JS PIKE_*_MS，bundle.strings-decoded.js:24705-24749）
        // ====================================================================

        /// <summary>枪兵攻击旋转段时长（对应 <c>PIKE_ATTACK_ROTATE_MS=90</c>，bundle 段1）。</summary>
        private const long PikeAttackRotateMs = 90;

        /// <summary>枪兵攻击突刺段时长（对应 <c>PIKE_ATTACK_THRUST_MS=270</c>，bundle 段2）。</summary>
        private const long PikeAttackThrustMs = 270;

        /// <summary>枪兵命中延迟=旋转+突刺（对应 <c>PIKE_HIT_DELAY_MS=360</c>，段3 onStart 等价常量）。</summary>
        private const long PikeHitDelayMs = PikeAttackRotateMs + PikeAttackThrustMs; // 360

        /// <summary>枪兵效果总持续时间=命中延迟+120ms 回收段（对应 <c>PIKE_EFFECT_DURATION_MS=480</c>）。</summary>
        private const long PikeEffectDurationMs = PikeHitDelayMs + 120; // 480

        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>日志标签前缀。</summary>
        private const string LogTag = "[PikeAttackEffect]";

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

        /// <summary>命中半径（默认 48，对应 JS <c>radius=48</c>）。</summary>
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

        /// <summary>已命中敌人 ID 集合，同效果内同一敌人只命中一次。</summary>
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

        /// <summary>构造枪兵攻击效果。池工厂调用。</summary>
        internal PikeAttackEffect()
        {
            ResetState();
        }

        // ====================================================================
        // Launch —— 由单位在 Acquire 后调用
        // --------------------------------------------------------------------
        // 对应 JS PikeAttackEffect.launch（简化：去掉 playbackRate/动画校准/枪尖表现 port）。
        // hitAtMs 固定 360，durationMs 固定 480（task 5.4 最简移植）。
        // ====================================================================

        /// <summary>
        /// 启动枪兵延迟命中效果：配置参数并激活。
        /// </summary>
        /// <param name="owner">所有者（非 null）。</param>
        /// <param name="resolver">攻击解析服务（非 null）。</param>
        /// <param name="enemyManager">敌人管理器（非 null）。</param>
        /// <param name="damage">基础伤害值（正数）。</param>
        /// <param name="cellWidth">敌人格子宽。</param>
        /// <param name="cellHeight">敌人格子高。</param>
        /// <param name="radius">命中半径（默认 48）。</param>
        /// <returns>this。</returns>
        /// <remarks>
        /// <para><b>对应 JS <c>launch</c>：</b>固定 <c>hitAtMs=PikeHitDelayMs(360)</c>、
        /// <c>durationMs=PikeEffectDurationMs(480)</c>。JS 按 playbackRate 缩放，C# 按 task 5.4
        /// 固定 360/480（playbackRate=1.0）。动画事件校准 provider 与枪尖 Qx 表现 port
        /// （JS DEFERRED 桩）本期不移植——规则层命中由 update→hit 驱动，不依赖视觉对象。</para>
        /// </remarks>
        internal PikeAttackEffect Launch(
            IAttackEffectOwner owner,
            AttackResolver resolver,
            EnemyManager enemyManager,
            int damage,
            float cellWidth,
            float cellHeight,
            float radius = 48f)
        {
            _owner = owner;
            _resolver = resolver;
            _enemyManager = enemyManager;
            _damage = damage;
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
        /// 推进一帧：累计 elapsed，到达 360ms 触发命中，到达 480ms 标记非活动完成。
        /// </summary>
        /// <param name="deltaMs">子步时长（毫秒）。</param>
        /// <remarks>
        /// <para><b>对应 JS 继承的 <c>update</c>：</b>
        /// elapsed += delta; elapsed&gt;=hitAtMs(360) 且未命中 → hit();
        /// elapsed&gt;=durationMs(480) → cleanup（C# 置 active=false）。</para>
        /// <para><b>命中作为同步副作用</b>，伤害立即生效。</para>
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

            // 命中时机：elapsed >= 360ms → 触发命中（对应 JS hitAtMs=PIKE_HIT_DELAY_MS）。
            if (!_hitTriggered && _elapsed >= PikeHitDelayMs)
            {
                _hitTriggered = true;
                Hit();
            }

            // 完成判定：elapsed >= 480ms → 标记非活动（对应 JS durationMs=PIKE_EFFECT_DURATION_MS）。
            if (_elapsed >= PikeEffectDurationMs)
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
        /// <para>逻辑与 <see cref="MeleeAttackEffect"/> 的 Hit 一致（JS 中 PikeAttackEffect
        /// 继承 MeleeAttackEffect.hit）。查询 center 周围 radius 内可攻击目标，hitSet 去重，
        /// resolver.Hit 提交。枪兵无 multiplier（JS 默认 1）。</para>
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

            // 枪兵无 multiplier（JS super.launch 未传 multiplier，默认 1）。
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
                _resolver.Hit(target, _damage, _owner.RuntimeId);
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
            _radius = 0f;
            _cellWidth = 0f;
            _cellHeight = 0f;
            _elapsed = 0;
            _hitTriggered = false;
            _hitSet.Clear();
        }
    }
}
