using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.4：KnifeAttackEffect —— 500ms 刀兵延迟命中效果
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Combat/Effects/KnifeAttackEffect.cs）：
    //   以逻辑时间实现刀兵 500ms 时序；吸收原 KnifeAttackTimeline.js，
    //   避免双计时器（design 决策 5 / task 5.5）。
    //
    // 来源证据：
    //   - KnifeAttackEffect.js:1-95：由 AttackEffectManager 驱动的刀兵延迟命中效果。
    //     核心字段：owner/timeline/target/damage/record/elapsed/delayMs/active/usesTimer
    //     update(deltaMs): usesTimer 分支仅 return active；回退分支 elapsed+=delta;
    //                       elapsed>=delayMs → timeline.resolve(this); active=false
    //     launch({owner, timeline, target, damage, record, delayMs}):
    //                       检测 timeline.laya.timer.once 存在则 usesTimer=true 注册 Laya timer
    //   - KnifeAttackTimeline.js:1-111：刀兵攻击时序管理器（本 change 合入 KnifeAttackEffect）。
    //     KNIFE_HIT_DELAY_BASE_MS = 500 (hu[176])
    //     start({attacker, target, damage}): delayMs = 500/playbackRate; 创建 record;
    //       attackEffectManager.create(KnifeAttackEffect).launch({owner, timeline:this, target,
    //       damage, record, delayMs}); attackEffectManager.add(effect)
    //     resolve(effectOrContext): 取 owner/target/damage/record;
    //       if (record.settled || record.cancelled) return;
    //       if (owner 失效: lifecycleGeneration 不匹配 || inPool || destroyed || !isActive)
    //         → record.cancelled=true; return;
    //       enemy = enemyManager.getById(target.id);
    //       if (!enemy || !enemy.isTargetableBy(owner.side)) → record.cancelled=true; return;
    //       enemy.hit(damage, owner); effects.showKnifeHit(...); record.settled=true
    //     cancel(effect): record.cancelled=true
    //
    // 决策依据：
    //   - design.md 决策 5 / 目录表："KnifeAttackTimeline 合入 KnifeAttackEffect，
    //     只保留一个逻辑计时源。" task 5.5 进一步要求禁止 TEngine Timer/Laya Timer
    //     与 Manager 双轨推进。本类型在 task 5.4 即完整实现刀兵时序，不创建单独
    //     KnifeAttackTimeline 类，5.5 无需再合并。
    //   - task 5.4 约束：500ms KnifeAttackEffect（damage at hit point per JS timing）；
    //     实现 IAttackEffect + IPoolableBattleObject.ResetState；伤害经 AttackResolver；
    //     尊重 Settling/freeze；中文注释、UTF-8、LF。
    //   - design.md 决策 0.4 / spec "Battle result is frozen once"：Cancel 标记非活动，
    //     不造成伤害。Settling 静默清理经 AttackEffectManager.Clear 调 Cancel。
    //   - spec battle-simulation "Effect manager participates in a substep"：效果每子步
    //     只累计一次 stepMs，由 AttackEffectManager 唯一推进，不注册 Timer。
    //
    // C# 与 JS 的差异：
    //   1. 合入 Timeline：JS KnifeAttackTimeline.start/resolve/cancel 逻辑合入本类型。
    //      不再依赖 Laya.timer（usesTimer 分支删除）——唯一计时源为 AttackEffectManager.Update。
    //      这满足 task 5.5 "禁止 TEngine Timer/Laya Timer 与 Manager 双轨推进"。
    //   2. 单目标命中：JS KnifeAttackEffect 针对单个 target（target.id），resolve 时
    //      enemyManager.getById 查找并 isTargetableBy 守卫。C# 保持单目标语义：
    //      Launch 接收 targetId，Resolve 时 GetById + IsTargetableBy + resolver.Hit。
    //   3. owner 失效守卫：JS 检查 lifecycleGeneration/inPool/destroyed/isActive。
    //      C# 简化为 owner!=null（Cancel 会置 null）+ target IsTargetableBy/Hit 死亡守卫。
    //      完整 lifecycleGeneration 检查由单位（task 6.2）的 CancelOwner 在回池时取消效果保证。
    //   4. record 简化：JS record（settled/cancelled/generation）用于诊断与跨 timer 回调。
    //      C# 由 Manager 唯一推进，不需要 record 跨回调防重入，以 _resolved 标志替代。
    //   5. delayMs 固定 500ms：JS delayMs=500/playbackRate。task 5.4 指定 500ms，
    //      playbackRate 默认 1.0，本类型固定 _delayMs=500（不支持 playbackRate 变速，
    //      与 task 5.4 最简移植一致；正式变速由后续 change 处理）。
    //
    // 不变量：
    //   1. 唯一推进：Update 只由 AttackEffectManager.Update 调用，不注册 Timer。
    //   2. 500ms 命中：elapsed >= 500ms 时 Resolve（查找目标、守卫、命中），随即完成。
    //   3. 单目标：KnifeAttackEffect 针对单个目标 ID，不查询范围。
    //   4. 伤害经 resolver：Resolve 调 resolver.Hit，不直接 IEnemyEntity.Hit。
    //   5. Cancel 不伤害：Cancel 只置 active=false、释放引用。
    //   6. ResetState 完整清空：回收后等价新构造。
    //   7. 幂等 Cancel/ResetState。
    // ============================================================================

    /// <summary>
    /// 刀兵 500ms 延迟命中效果：吸收原 KnifeAttackTimeline，以逻辑时间实现命中时序（task 5.4）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>以逻辑时间实现刀兵 500ms 时序；吸收原
    /// <c>KnifeAttackTimeline.js</c>，避免双计时器。替代还原工程
    /// <c>KnifeAttackEffect.js</c> + <c>KnifeAttackTimeline.js</c>。</para>
    ///
    /// <para><b>500ms 时序（task 5.4 "500ms KnifeAttackEffect"）：</b>
    /// <see cref="Update"/> 累计 <c>elapsed</c>，<c>elapsed &gt;= 500ms</c> 时触发
    /// <see cref="Resolve"/>（查找目标、守卫、命中），随即标记非活动完成。
    /// 对应 JS <c>delayMs = KNIFE_HIT_DELAY_BASE_MS(500)</c> 与
    /// <c>update</c> 回退分支 <c>elapsed &gt;= delayMs → resolve</c>。</para>
    ///
    /// <para><b>合入 Timeline（design 决策 5 / task 5.5）：</b>
    /// 原 <c>KnifeAttackTimeline.start/resolve/cancel</c> 逻辑合入本类型。
    /// 不依赖 Laya.timer（删除 usesTimer 分支）——唯一计时源为
    /// <see cref="AttackEffectManager.Update"/>，满足"禁止双轨推进"。
    /// task 5.5 无需再合并（本类型已完整实现）。</para>
    ///
    /// <para><b>单目标命中：</b>
    /// 刀兵针对单个目标（JS <c>target.id</c>）。<see cref="Resolve"/> 时
    /// <c>enemyManager.GetById(targetId)</c> 查找，<c>IsTargetableBy(owner.Side)</c>
    /// 守卫，经 <see cref="AttackResolver.Hit"/> 提交伤害。不查询范围。</para>
    ///
    /// <para><b>Cancel 不伤害（spec "Settling has no gameplay damage authority"）：</b>
    /// <see cref="Cancel"/> 只置 <c>active=false</c>、释放引用，不调 <see cref="Resolve"/>。</para>
    ///
    /// <para><b>池复用（task 4.1）：</b>
    /// <see cref="ResetState"/> 清空全部可变状态，回收后等价新构造。</para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部使用。</para>
    /// </remarks>
    internal sealed class KnifeAttackEffect : IAttackEffect
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>刀兵命中延迟基线（对应 JS <c>KNIFE_HIT_DELAY_BASE_MS = 500</c>，hu[176]）。</summary>
        private const long KnifeHitDelayMs = 500;

        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>日志标签前缀。</summary>
        private const string LogTag = "[KnifeAttackEffect]";

        // ====================================================================
        // 活动状态与所有者（IAttackEffect 契约）
        // ====================================================================

        /// <summary>是否活动（对应 <c>this.active</c>）。</summary>
        private bool _active;

        /// <summary>所有者引用（对应 <c>this.owner</c>），用于 CancelOwner 匹配与阵营/ID 读取。</summary>
        private IAttackEffectOwner _owner;

        // ====================================================================
        // 外部依赖
        // ====================================================================

        /// <summary>攻击解析服务，Resolve 时提交伤害。</summary>
        private AttackResolver _resolver;

        /// <summary>敌人管理器（对应 JS <c>this.enemyManager</c> / timeline.enemyManager），Resolve 时按 ID 查找目标。</summary>
        private EnemyManager _enemyManager;

        // ====================================================================
        // 攻击参数
        // ====================================================================

        /// <summary>目标敌人运行时 ID（对应 JS <c>this.target.id</c>）。Resolve 时 GetById 查找。</summary>
        private int _targetId;

        /// <summary>伤害值（对应 <c>this.damage</c>）。</summary>
        private int _damage;

        // ====================================================================
        // 时序状态
        // ====================================================================

        /// <summary>已累计时长（对应 <c>this.elapsed</c>，毫秒）。</summary>
        private long _elapsed;

        /// <summary>命中延迟（对应 <c>this.delayMs</c>）。固定 500ms（task 5.4）。</summary>
        private long _delayMs;

        /// <summary>是否已结算（合并 JS record.settled/cancelled 语义），防止重复 Resolve。</summary>
        private bool _resolved;

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

        /// <summary>
        /// 构造刀兵攻击效果。池工厂调用。
        /// </summary>
        internal KnifeAttackEffect()
        {
            ResetState();
        }

        // ====================================================================
        // Launch —— 由单位在 Acquire 后调用，配置参数并激活
        // --------------------------------------------------------------------
        // 对应 JS KnifeAttackEffect.launch + KnifeAttackTimeline.start 的合并入口。
        // 不注册 Laya timer（删除 usesTimer 分支），唯一计时源为 Manager.Update。
        // ====================================================================

        /// <summary>
        /// 启动刀兵延迟命中效果：配置参数并激活。
        /// </summary>
        /// <param name="owner">所有者（非 null），提供阵营/运行时 ID。</param>
        /// <param name="resolver">攻击解析服务（非 null），Resolve 时提交伤害。</param>
        /// <param name="enemyManager">敌人管理器（非 null），Resolve 时按 ID 查找目标。</param>
        /// <param name="targetId">目标敌人运行时 ID（对应 <c>target.id</c>）。</param>
        /// <param name="damage">伤害值（正数）。</param>
        /// <returns>this（便于链式调用）。</returns>
        /// <remarks>
        /// <para><b>合并 Timeline.start（task 5.5 预防性合入）：</b>
        /// JS <c>KnifeAttackTimeline.start</c> 计算 delayMs=500/playbackRate、创建 record、
        /// launch effect、add to manager。C# 将 delayMs 固定为 500ms（playbackRate=1.0），
        /// record 简化为 <c>_resolved</c> 标志，Manager 推进唯一计时。</para>
        /// <para><b>不注册 Timer（task 5.5）：</b>JS usesTimer 分支注册 Laya.timer.once。
        /// C# 删除该分支——唯一计时源为 <see cref="AttackEffectManager.Update"/>。</para>
        /// </remarks>
        internal KnifeAttackEffect Launch(
            IAttackEffectOwner owner,
            AttackResolver resolver,
            EnemyManager enemyManager,
            int targetId,
            int damage)
        {
            _owner = owner;
            _resolver = resolver;
            _enemyManager = enemyManager;
            _targetId = targetId;
            _damage = damage;
            _delayMs = KnifeHitDelayMs;
            _elapsed = 0;
            _resolved = false;
            _active = true;
            return this;
        }

        // ====================================================================
        // Update —— 唯一推进入口，累计 elapsed，到 500ms 时 Resolve
        // ====================================================================

        /// <inheritdoc />
        /// <summary>
        /// 推进一帧：累计 elapsed，到达 500ms 时 Resolve（命中），随即标记非活动完成。
        /// </summary>
        /// <param name="deltaMs">子步时长（毫秒）。</param>
        /// <remarks>
        /// <para><b>对应 JS <c>update</c> 回退分支（usesTimer=false）：</b>
        /// <code>
        /// this.elapsed += deltaMs;
        /// if (this.elapsed >= this.delayMs) { this.timeline.resolve(this); this.active = false; }
        /// </code></para>
        /// <para><b>不使用 usesTimer 分支：</b>C# 唯一计时源为 Manager.Update，
        /// 删除 JS usesTimer 分支（仅 return active 的 Laya timer 路径）。</para>
        /// <para><b>命中作为同步副作用：</b>Resolve 在本方法内同步调用，伤害立即生效。</para>
        /// </remarks>
        public void Update(long deltaMs)
        {
            if (!_active)
            {
                return;
            }

            // 累计逻辑时间（对应 JS this.elapsed += Math.max(0, deltaMs)）。
            if (deltaMs > 0)
            {
                _elapsed += deltaMs;
            }

            // 命中时机判定：elapsed >= delayMs(500) → Resolve 并完成。
            // 对应 JS if (this.elapsed >= this.delayMs) { timeline.resolve(this); active=false }。
            if (_elapsed >= _delayMs)
            {
                Resolve();
                _active = false;
            }
        }

        // ====================================================================
        // Resolve —— 合并 KnifeAttackTimeline.resolve：查找目标、守卫、命中
        // --------------------------------------------------------------------
        // 对应 JS KnifeAttackTimeline.resolve:
        //   if (record.settled || record.cancelled) return;
        //   if (owner 失效) → cancelled; return;
        //   enemy = enemyManager.getById(target.id);
        //   if (!enemy || !enemy.isTargetableBy(owner.side)) → cancelled; return;
        //   enemy.hit(damage, owner); record.settled=true
        // C# 伤害经 resolver.Hit（不直接 IEnemyEntity.Hit），统一死亡守卫。
        // ====================================================================

        /// <summary>
        /// 命中结算：查找目标、守卫、经 <see cref="AttackResolver"/> 提交伤害
        /// （合并 JS <c>KnifeAttackTimeline.resolve</c>）。
        /// </summary>
        /// <remarks>
        /// <para><b>对应 JS <c>resolve</c>：</b>
        /// 检查 owner 有效性 → GetById 查找目标 → IsTargetableBy 守卫 → 提交伤害。</para>
        /// <para><b>伤害经 resolver（task 5.4 约束）：</b>不直接 <see cref="IEnemyEntity.Hit"/>，
        /// 经 <see cref="AttackResolver.Hit"/> 统一死亡守卫。</para>
        /// <para><b>幂等：</b><c>_resolved</c> 标志防止重复 Resolve（合并 JS record.settled 语义）。</para>
        /// <para><b>目标失效处理：</b>owner 为 null（已 Cancel）或目标不存在/不可攻击时，
        /// 标记 <c>_resolved=true</c> 不命中（对应 JS record.cancelled）。效果仍将由 Manager
        /// 在下一 Update 检测到非活动后移除——本方法由 Update 在 elapsed&gt;=delayMs 时调用，
        /// 调用后 Update 立即置 active=false。</para>
        /// </remarks>
        private void Resolve()
        {
            // 幂等守卫：已结算不重复（对应 JS if (record.settled || record.cancelled) return）。
            if (_resolved)
            {
                return;
            }
            _resolved = true;

            // owner 失效守卫（对应 JS if owner 失效 → cancelled; return）。
            // C# 简化：owner 为 null（已 Cancel/Reset）即失效。
            // 完整 lifecycleGeneration/inPool 检查由单位 CancelOwner 在回池时取消效果保证。
            if (_owner == null)
            {
                return;
            }

            // 查找目标（对应 JS enemy = enemyManager.getById(target.id)）。
            if (_enemyManager == null)
            {
                return;
            }
            IEnemyEntity enemy = _enemyManager.GetById(_targetId);

            // 目标守卫：不存在或不可被本方攻击 → 不命中（对应 JS if (!enemy || !isTargetableBy) cancelled; return）。
            if (enemy == null || !enemy.IsTargetableBy(_owner.Side))
            {
                return;
            }

            // 经 resolver 提交伤害（对应 JS enemy.hit(damage, owner)）。
            // C# attackerId = owner.RuntimeId。resolver.Hit 内部死亡守卫拒绝迟到伤害。
            if (_resolver != null && _damage > 0)
            {
                _resolver.Hit(enemy, _damage, _owner.RuntimeId);
            }
        }

        // ====================================================================
        // Cancel —— 取消并清理（不造成伤害）
        // ====================================================================

        /// <inheritdoc />
        /// <summary>
        /// 取消并清理效果（对应 JS <c>cleanup()</c>）。不造成伤害，幂等。
        /// </summary>
        /// <param name="reason">取消原因，供诊断。</param>
        /// <remarks>
        /// <para><b>不造成伤害：</b>只置 <c>active=false</c>、释放引用，不调 <see cref="Resolve"/>。</para>
        /// <para>对应 JS cleanup: 清理 Laya timer（C# 无 timer）+ timeline.cancel + 释放引用。
        /// C# 无 timer/timeline，只释放 owner/resolver/enemyManager/targetId/damage。</para>
        /// </remarks>
        public void Cancel(string reason)
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            _owner = null;
            _resolver = null;
            _enemyManager = null;
            // _resolved 保持原值——Cancel 不重置已结算状态（ResetState 完整清空）。
            // targetId/damage 等配置字段由 ResetState 清空。
        }

        // ====================================================================
        // ResetState —— 池回收时清空全部可变状态
        // ====================================================================

        /// <inheritdoc />
        /// <summary>
        /// 重置对象到等价于新构造的状态（对应 JS <c>reset()</c>）。
        /// </summary>
        public void ResetState()
        {
            _active = false;
            _owner = null;
            _resolver = null;
            _enemyManager = null;
            _targetId = 0;
            _damage = 0;
            _elapsed = 0;
            _delayMs = 0;
            _resolved = false;
        }
    }
}
