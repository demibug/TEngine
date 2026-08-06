using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.7：HitEnemyStrategy —— 对有效敌人提交单体伤害并请求移除箭矢
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 200 行 / Projectile/Hit/HitEnemyStrategy.cs）：
    //   对有效敌人提交单体伤害并请求移除箭矢。
    //   本期为内部具体类，不创建 IProjectileHitStrategy 接口（task 5.7 约束）。
    //
    // 来源证据（还原工程 HitEnemyStrategy.js:1-52 + ProjectileManager.js:85-106）：
    //   - 原始符号 rW/oE；策略编号 100
    //   - reset({ targetId, targetIds, delayMs, removeAfterHit, triggerMode })：
    //       设置目标 ID 列表、延迟、是否命中后移除、触发模式
    //   - 池化：_pool 数组，create() 取池或新建
    //   - recover()：清空 targetIds 并入池
    //   - ProjectileManager.update 中的触发逻辑（ProjectileManager.js:85-106）：
    //       * shouldRemove 且 triggerMode 为 requestRemove/both → 触发
    //       * hitEnabled 且 triggerMode 为 hitEnable/both → 触发
    //       * delayMs > 0：延迟倒计时；否则立即 _applyTargetStrategy
    //       * _applyTargetStrategy：遍历 targetIds，对每个存活敌人调用 projectile.hit(enemy)
    //       * removeAfterHit=true → shouldRemove=true
    //       * strategy.completed = true
    //
    // 决策依据：
    //   - design.md 第 9 行：纯逻辑，不持有 Unity GameObject。
    //   - design.md 决策 4：伤害提交使用直接调用 EnemyManager.ApplyDamage。
    //   - spec battle-simulation "Battle result is frozen once"：伤害在发生点同步生效。
    //   - projectile-pool-reset-contract.md：recover 清除全部状态。
    //
    // 不变量：
    //   1. 纯逻辑：通过 ProjectileBase.Hit 提交伤害，不直接持有敌人引用。
    //   2. 使用逻辑时间 stepMs。
    //   3. 命中后标记 completed，防止重复触发。
    // ============================================================================

    /// <summary>
    /// 投射物单体命中策略：对有效敌人提交伤害并请求移除箭矢。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 200 行）：</b>对有效敌人提交单体伤害并请求移除箭矢。
    /// 替代还原工程 <c>HitEnemyStrategy.js</c>（原始符号 rW/oE，策略编号 100）。</para>
    ///
    /// <para><b>触发模式（HitEnemyStrategy.js:20-30）：</b>
    /// <c>requestRemove</c>=投射物请求移除时触发；
    /// <c>hitEnable</c>=投射物 hitEnabled 时触发；
    /// <c>both</c>=两者任一满足时触发。
    /// 本期 SimpleDynamicArrow 使用 <c>requestRemove</c>（默认），即箭矢到达目标位置后触发命中。</para>
    ///
    /// <para><b>伤害提交流程（ProjectileManager.js:148-159 _applyTargetStrategy）：</b>
    /// 遍历 <see cref="_targetIds"/>，对每个 ID 调用 <see cref="ProjectileBase.Hit"/>。
    /// 命中任一目标后调用 finishHit。removeAfterHit=true 时请求移除。
    /// 策略标记 completed 防止重复触发。</para>
    ///
    /// <para><b>本期为内部具体类（task 5.7 约束）：</b>不创建 IProjectileHitStrategy 接口。
    /// 出现第二个获准命中策略时再提取接口。</para>
    /// </remarks>
    internal sealed class HitEnemyStrategy
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>策略类型编号（对应 HitEnemyStrategy.TYPE_CODE = 100）。</summary>
        internal const int TypeCode = 100;

        // ====================================================================
        // 可变状态字段（对应 HitEnemyStrategy.js:9-18）
        // ====================================================================

        /// <summary>目标敌人 ID 列表（对应 targetIds）。</summary>
        private readonly List<int> _targetIds = new List<int>();

        /// <summary>命中延迟（毫秒，对应 delayMs）。0=立即触发。</summary>
        private long _delayMs;

        /// <summary>命中后是否移除投射物（对应 removeAfterHit，默认 true）。</summary>
        private bool _removeAfterHit;

        /// <summary>触发模式（对应 triggerMode，默认 requestRemove）。</summary>
        private string _triggerMode;

        /// <summary>延迟是否已开始（对应 delayStarted）。</summary>
        private bool _delayStarted;

        /// <summary>延迟剩余时间（毫秒，对应 ProjectileManager.js:93-95 hitDelayRemainingMs）。</summary>
        private long _delayRemainingMs;

        /// <summary>是否已完成命中（对应 completed）。</summary>
        private bool _completed;

        // ====================================================================
        // 只读属性
        // ====================================================================

        /// <summary>是否已完成命中（对应 completed）。</summary>
        internal bool IsCompleted => _completed;

        /// <summary>触发模式（对应 triggerMode）。</summary>
        internal string TriggerMode => _triggerMode;

        /// <summary>命中后是否移除投射物（对应 removeAfterHit）。</summary>
        internal bool RemoveAfterHit => _removeAfterHit;

        /// <summary>命中延迟（毫秒）。</summary>
        internal long DelayMs => _delayMs;

        /// <summary>目标 ID 列表只读视图。</summary>
        internal IReadOnlyList<int> TargetIds => _targetIds;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造一个单体命中策略。字段初始化为默认值。
        /// </summary>
        internal HitEnemyStrategy()
        {
            Reset(-1, null, 0, true, "requestRemove");
        }

        // ====================================================================
        // Reset —— 重置策略配置（对应 HitEnemyStrategy.js:20-30 reset）
        // ====================================================================

        /// <summary>
        /// 重置命中策略到待触发状态。
        /// </summary>
        /// <param name="targetId">单个目标 ID（与 <paramref name="targetIds"/> 互斥，传 -1 表示不使用单目标）。</param>
        /// <param name="targetIds">多目标 ID 列表（若非 null 则覆盖 <paramref name="targetId"/>）。</param>
        /// <param name="delayMs">命中延迟（毫秒，0=立即触发）。</param>
        /// <param name="removeAfterHit">命中后是否移除投射物（默认 true）。</param>
        /// <param name="triggerMode">触发模式（默认 requestRemove）。</param>
        internal void Reset(
            int targetId,
            List<int> targetIds,
            long delayMs,
            bool removeAfterHit,
            string triggerMode)
        {
            _targetIds.Clear();
            if (targetIds != null && targetIds.Count > 0)
            {
                _targetIds.AddRange(targetIds);
            }
            else if (targetId > 0)
            {
                _targetIds.Add(targetId);
            }

            _delayMs = delayMs > 0 ? delayMs : 0;
            _removeAfterHit = removeAfterHit;
            _triggerMode = string.IsNullOrEmpty(triggerMode) ? "requestRemove" : triggerMode;
            _delayStarted = false;
            _delayRemainingMs = 0;
            _completed = false;
        }

        // ====================================================================
        // ShouldTrigger —— 判断是否应触发命中（对应 ProjectileManager.js:87-88）
        // ====================================================================

        /// <summary>
        /// 判断当前条件是否应触发命中策略。
        /// </summary>
        /// <param name="shouldRemove">投射物是否已请求移除。</param>
        /// <param name="hitEnabled">投射物是否启用命中。</param>
        /// <returns>true=应触发命中。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>ProjectileManager.update</c> 中的触发判断
        /// （ProjectileManager.js:87-88）：</para>
        /// <list type="bullet">
        /// <item>requestRemove：shouldRemove 为 true 时触发</item>
        /// <item>hitEnable：hitEnabled 为 true 时触发</item>
        /// <item>both：任一为 true 时触发</item>
        /// </list></para>
        /// </remarks>
        internal bool ShouldTrigger(bool shouldRemove, bool hitEnabled)
        {
            if (_completed)
            {
                return false;
            }

            if (_triggerMode == "requestRemove" || _triggerMode == "both")
            {
                if (shouldRemove)
                {
                    return true;
                }
            }

            if (_triggerMode == "hitEnable" || _triggerMode == "both")
            {
                if (hitEnabled)
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================
        // TickDelay —— 推进命中延迟倒计时（对应 ProjectileManager.js:90-100）
        // ====================================================================

        /// <summary>
        /// 推进命中延迟倒计时。返回是否延迟已到期（可执行命中）。
        /// </summary>
        /// <param name="stepMs">子步时长（毫秒）。</param>
        /// <returns>true=延迟已到期或无延迟，可执行命中；false=仍在延迟中。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>ProjectileManager.update</c> 中的延迟逻辑
        /// （ProjectileManager.js:90-100）：首次调用设置 delayStarted=true 并初始化倒计时，
        /// 后续调用递减倒计时，到期后返回 true。</para>
        /// </remarks>
        internal bool TickDelay(long stepMs)
        {
            if (_delayMs <= 0)
            {
                return true;
            }

            if (!_delayStarted)
            {
                _delayStarted = true;
                _delayRemainingMs = _delayMs;
                return false;
            }

            _delayRemainingMs -= stepMs;
            return _delayRemainingMs <= 0;
        }

        // ====================================================================
        // Apply —— 执行命中（对应 ProjectileManager.js:148-159 _applyTargetStrategy）
        // ====================================================================

        /// <summary>
        /// 对全部目标 ID 执行命中，标记完成并返回是否应移除投射物。
        /// </summary>
        /// <param name="projectile">执行命中的投射物。</param>
        /// <returns>true=命中后应移除投射物（removeAfterHit）；false=不移除。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>_applyTargetStrategy(projectile, strategy)</c>
        /// （ProjectileManager.js:148-159）：遍历 targetIds，对每个 ID 调用
        /// <c>projectile.hit(enemy)</c>。命中任一目标后调用 finishHit。
        /// 策略标记 completed 防止重复触发。</para>
        /// <para>目标不存在的 ID 静默跳过（对应 JS <c>if (enemy)</c> 守卫）。</para>
        /// </remarks>
        internal bool Apply(ProjectileBase projectile)
        {
            bool hitAny = false;
            foreach (int targetId in _targetIds)
            {
                if (projectile.Hit(targetId))
                {
                    hitAny = true;
                }
            }

            _completed = true;
            return _removeAfterHit;
        }

        // ====================================================================
        // Recover —— 回收（对应 HitEnemyStrategy.js:32-40 recover）
        // ====================================================================

        /// <summary>
        /// 回收策略到待重置状态。清除全部目标与状态。
        /// </summary>
        internal void Recover()
        {
            _targetIds.Clear();
            _delayMs = -1;
            _removeAfterHit = true;
            _triggerMode = "requestRemove";
            _delayStarted = false;
            _delayRemainingMs = 0;
            _completed = false;
        }
    }
}
