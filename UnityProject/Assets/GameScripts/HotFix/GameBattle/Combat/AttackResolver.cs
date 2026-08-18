using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.1：AttackResolver —— 统一稳定目标查询与伤害提交的内部具体服务
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / Combat/AttackResolver.cs / design.md:186）：
    //   统一稳定目标查询与伤害提交，隔离攻击效果对 EnemyManager 的直接知识。
    //
    // 来源证据（还原工程 AttackResolver.js:1-28）：
    //   还原工程 AttackResolver 是一个无状态纯逻辑对象，只依赖 EnemyManager 的纯逻辑
    //   查询接口，不承载动画或对象池状态。三个方法：
    //     - queryTargets({ enemyManager, center, range, side }) → enemyManager.queryTargets
    //     - queryEnemyObjects({ enemyManager, center, range, side }) → enemyManager.queryEnemyObjects
    //     - hit(target, damage, attacker) → target.hit(damage, attacker) / target.takeDamage(...)
    //   消费方：AttackScheduler（AttackScheduler.js:8-11 注入 resolver）、
    //           MeleeAttackEffect（MeleeAttackEffect.js:3-9 注入 resolver）。
    //   MinimalBattleBootstrap.js:463 在组装时 `new AttackResolver()` 并注入到 Scheduler/Effect。
    //
    // 决策依据：
    //   - design.md:186 文件职责表："统一稳定目标查询与伤害提交，隔离攻击效果对
    //     EnemyManager 的直接知识。"
    //   - task 5.1 约束：本期不为只有一个生产实现的 Resolver 额外创建公共接口——
    //     concrete class only，不创建 IAttackResolver。
    //   - task 4.6 约束（稳定有序集合）：目标查询使用 EnemyManager 的稳定有序集合
    //     （_orderedIds，按 spawn 顺序）与空间索引（List<int> 而非 HashSet），
    //     禁止依赖 Dictionary/HashSet 的未定义遍历顺序决定目标。AttackResolver 只委托
    //     EnemyManager 的查询接口，不自行遍历任何集合，天然继承其稳定有序保证。
    //   - task 4.2 契约（死亡/冻结守卫）：目标死亡或战斗冻结后拒绝迟到伤害。
    //     EnemyBase.Hit 内部已有 `if (health <= 0) return false` 死亡守卫
    //     （EnemyBase.cs:990）。EnemyManager.QueryTargets/QueryEnemyObjects 经
    //     IsTargetableBy 过滤已死亡敌人（EnemyManager.cs:708-711）。本类型在 hit 入口
    //     额外检查目标有效性（null / IsTargetableBy），保证迟到伤害被拒绝。
    //   - design.md 决策 4 / spec battle-event-boundary：敌人注册、空间索引、伤害、回收等
    //     一致性操作使用直接调用，不通过全局事件总线。AttackResolver 只做直接方法调用。
    //   - design.md 决策 0.4 / spec "Battle result is frozen once"：首次 TryFreeze 成功后
    //     只完成当前同步提交并中止剩余 phase/子步。AttackResolver 的 hit 在伤害提交后
    //     正常返回，不重入销毁集合；冻结后的迟到伤害由 EnemyBase.Hit 的死亡守卫与
    //     EnemyManager 查询的 IsTargetableBy 过滤共同拒绝（冻结后敌人会被 ForceRemove/
    //     GameOver 清理，IsTargetableBy 返回 false）。
    //
    // C# 与 JS 的差异：
    //   1. attacker 参数：JS 传对象引用（target.hit(damage, attacker)），C# 移植改为
    //      int attackerId（对应 IEnemyEntity.Hit(int damage, int attackerId)），
    //      与 EnemyBase/EnemyManager 的伤害提交契约一致（EnemyManager.ApplyDamage
    //      同样以 attackerId 传入）。
    //   2. cellWidth/cellHeight：JS EnemyManager.queryTargets 从 this.gameData.map 读取
    //      gridWidth/gridHeight。C# EnemyManager.QueryTargets 显式接收 cellWidth/cellHeight
    //      参数（EnemyManager.cs:685-686），故 AttackResolver 也需接收并透传。
    //      这不增加 AttackResolver 对 MapData 的耦合——调用方（AttackScheduler/Effect）
    //      从配置或 MapData 注入格子尺寸，AttackResolver 只做透传。
    //   3. 无状态：与 JS 一致，本类型不持有任何可变状态，构造后所有方法纯委托。
    //     不需要 Reset/池化。每次战斗由 BattleRuntimeFactory 构造新实例（与 JS
    //     MinimalBattleBootstrap.js:463 `new AttackResolver()` 一致）。
    //
    // 不变量：
    //   1. 无状态：不持有可变字段，所有方法为纯委托。
    //   2. 稳定目标查询：只委托 EnemyManager 的稳定有序查询接口，不自行遍历集合。
    //   3. 死亡守卫：hit 入口检查目标有效性，已死亡目标返回 false 不提交伤害。
    //   4. 不重入销毁：hit 只调用 target.Hit，不修改 EnemyManager 集合。
    //   5. 具体类无公共接口：internal sealed class，不创建 IAttackResolver。
    // ============================================================================

    /// <summary>
    /// 统一稳定目标查询与伤害提交的内部具体服务（task 5.1）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:186）：</b>统一稳定目标查询与伤害提交，隔离攻击效果对
    /// <see cref="EnemyManager"/> 的直接知识。替代还原工程 <c>AttackResolver.js</c> 的
    /// 无状态纯逻辑对象（<c>AttackResolver.js:7-26</c>）。</para>
    ///
    /// <para><b>稳定目标查询（task 4.6 约束）：</b>
    /// <see cref="QueryTargets"/> 与 <see cref="QueryEnemyObjects"/> 只委托
    /// <see cref="EnemyManager"/> 的查询接口，不自行遍历任何集合。<see cref="EnemyManager"/>
    /// 的目标查询基于 <c>_orderedIds</c>（按 spawn 顺序的 List）与空间索引
    /// （<c>List&lt;int&gt;</c> 而非 HashSet），保证遍历顺序确定，不依赖 Dictionary/HashSet
    /// 的未定义遍历顺序决定目标。</para>
    ///
    /// <para><b>伤害提交与死亡守卫（task 4.2 契约）：</b>
    /// <see cref="Hit"/> 在入口检查目标有效性（null 或已死亡返回 false），
    /// 再委托 <see cref="IEnemyEntity.Hit"/> 提交伤害。<see cref="IEnemyEntity.Hit"/>
    /// 内部已有 <c>if (health &lt;= 0) return false</c> 死亡守卫
    /// （<c>EnemyBase.cs:990</c>），本类型在入口做前置检查以尽早拒绝迟到伤害，
    /// 避免已死亡目标参与不必要的伤害计算。</para>
    ///
    /// <para><b>冻结守卫（决策 0.4 / spec "Battle result is frozen once"）：</b>
    /// 战斗冻结后敌人会被 <see cref="EnemyManager.ForceRemove"/> 或
    /// <see cref="EnemyManager.GameOver"/> 清理，<see cref="IEnemyEntity.IsTargetableBy"/>
    /// 对已死亡/已清理敌人返回 false。因此 <see cref="QueryTargets"/>/
    /// <see cref="QueryEnemyObjects"/> 在冻结后返回空结果，<see cref="Hit"/> 在冻结后
    /// 因目标已不在活动集合中（或已死亡）而被拒绝。本类型不直接引用
    /// <see cref="BattleResultBuilder.IsFrozen"/>——冻结后的迟到伤害拒绝由
    /// EnemyManager 查询过滤与 EnemyBase.Hit 死亡守卫共同保证，
    /// 保持 AttackResolver 与结果冻结器的解耦。</para>
    ///
    /// <para><b>不创建公共接口（task 5.1 约束）：</b>
    /// 本期只有一个生产实现，不创建 <c>IAttackResolver</c> 接口。本类型为
    /// <c>internal sealed</c>，只供 GameBattle 内部 AttackScheduler（task 5.2）、
    /// MeleeAttackEffect（task 5.4）等消费方使用。出现第二个获批准实现时再提取接口。</para>
    ///
    /// <para><b>无状态：</b>与 JS 原版一致，本类型不持有任何可变状态，所有方法为纯委托。
    /// 不需要 Reset/池化。每次战斗由 <see cref="BattleRuntimeFactory"/> 构造新实例
    /// （与 JS <c>MinimalBattleBootstrap.js:463 new AttackResolver()</c> 一致）。</para>
    /// </remarks>
    internal sealed class AttackResolver
    {
        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造无状态的攻击解析服务。
        /// </summary>
        /// <remarks>
        /// <para>与 JS 原版 <c>AttackResolver.js:7</c> 一致，本类型无构造参数与可变状态。
        /// 由 <see cref="BattleRuntimeFactory"/> 在每次 Create 时构造新实例，
        /// 并注入到 AttackScheduler（task 5.2）与攻击效果（task 5.4）。</para>
        /// <para>不跨局复用——虽然本类型无状态可安全复用，但遵循"每局新建/销毁"统一约定
        /// （spec "Restart creates clean per-battle state"），避免隐式跨局引用。</para>
        /// </remarks>
        internal AttackResolver()
        {
        }

        // ====================================================================
        // 稳定目标查询 —— 委托 EnemyManager 的稳定有序查询接口
        // ====================================================================

        /// <summary>
        /// 查询指定中心与半径内的可攻击目标 DTO 列表（对应 JS
        /// <c>AttackResolver.js:8-12 queryTargets</c>）。
        /// </summary>
        /// <param name="enemyManager">敌人管理器（非 null），提供稳定有序查询。</param>
        /// <param name="centerX">查询中心 X（逻辑坐标）。</param>
        /// <param name="centerY">查询中心 Y（逻辑坐标）。</param>
        /// <param name="range">查询半径。</param>
        /// <param name="playerSide">
        /// 攻击者阵营：<c>true</c>=玩家方攻击者（查询对手方敌人），<c>false</c>=对手方。
        /// 对应 JS <c>side</c>，透传为 <see cref="EnemyManager.QueryTargets"/> 的
        /// <c>playerSide</c> 参数。
        /// </param>
        /// <param name="cellWidth">
        /// 敌人格子宽（对应 <c>map.gridWidth</c>，用于 <c>circleIntersectsRect</c> 精筛）。
        /// 透传给 <see cref="EnemyManager.QueryTargets"/>。
        /// </param>
        /// <param name="cellHeight">
        /// 敌人格子高（对应 <c>map.gridHeight</c>）。透传给
        /// <see cref="EnemyManager.QueryTargets"/>。
        /// </param>
        /// <returns>
        /// 目标 DTO 列表（按空间单元扫描顺序，不排序——对应 JS 行为）。
        /// <see cref="EnemyManager"/> 无活动敌人或 <paramref name="enemyManager"/> 为 null
        /// 时返回空列表。
        /// </returns>
        /// <remarks>
        /// <para><b>对应 JS <c>AttackResolver.js:8-12</c>：</b>
        /// <code>
        /// queryTargets({ enemyManager, center, range, side } = {}) {
        ///   if (!enemyManager || typeof enemyManager.queryTargets !== 'function') return [];
        ///   const point = center || { x: 0, y: 0 };
        ///   return enemyManager.queryTargets(point.x, point.y, range, side) || [];
        /// }
        /// </code></para>
        ///
        /// <para><b>稳定有序（task 4.6）：</b>
        /// 委托 <see cref="EnemyManager.QueryTargets"/>，其遍历基于 <c>_orderedIds</c>
        /// （spawn 顺序）与空间单元内的 <c>List&lt;int&gt;</c>，不依赖 Dictionary/HashSet
        /// 的未定义遍历顺序。最终结果经 <see cref="EnemyManager.CircleIntersectsRect"/>
        /// 精筛，顺序确定。</para>
        ///
        /// <para><b>死亡/冻结过滤：</b>
        /// <see cref="EnemyManager.QueryTargets"/> 经 <see cref="IEnemyEntity.IsTargetableBy"/>
        /// 过滤已死亡（DEAD）与出生中（SPAWNING）的敌人。冻结后敌人被清理，
        /// <c>IsTargetableBy</c> 返回 false，查询返回空结果。</para>
        /// </remarks>
        internal List<EnemyTargetDto> QueryTargets(
            EnemyManager enemyManager,
            float centerX,
            float centerY,
            float range,
            bool playerSide,
            float cellWidth,
            float cellHeight)
        {
            // 对应 JS if (!enemyManager || typeof enemyManager.queryTargets !== 'function') return [];
            if (enemyManager == null)
            {
                return new List<EnemyTargetDto>();
            }

            // 委托 EnemyManager 的稳定有序查询接口。
            // 对应 JS enemyManager.queryTargets(point.x, point.y, range, side)。
            return enemyManager.QueryTargets(centerX, centerY, range, playerSide, cellWidth, cellHeight);
        }

        /// <summary>
        /// 查询指定中心与半径内的敌人对象列表（对应 JS
        /// <c>AttackResolver.js:14-18 queryEnemyObjects</c>）。
        /// </summary>
        /// <param name="enemyManager">敌人管理器（非 null），提供稳定有序查询。</param>
        /// <param name="centerX">查询中心 X（逻辑坐标）。</param>
        /// <param name="centerY">查询中心 Y（逻辑坐标）。</param>
        /// <param name="range">查询半径。</param>
        /// <param name="playerSide">攻击者阵营。</param>
        /// <param name="cellWidth">敌人格子宽（对应 <c>map.gridWidth</c>）。</param>
        /// <param name="cellHeight">敌人格子高（对应 <c>map.gridHeight</c>）。</param>
        /// <param name="output">
        /// 输出列表（复用缓冲，对应 JS <c>output</c> 参数）。若为 null 则内部新建。
        /// 传入非 null 列表时追加结果，避免每次查询分配新 List。
        /// </param>
        /// <returns>敌人对象列表（追加到 <paramref name="output"/>）。</returns>
        /// <remarks>
        /// <para><b>对应 JS <c>AttackResolver.js:14-18</c>：</b>
        /// <code>
        /// queryEnemyObjects({ enemyManager, center, range, side } = {}) {
        ///   if (!enemyManager || typeof enemyManager.queryEnemyObjects !== 'function') return [];
        ///   const point = center || { x: 0, y: 0 };
        ///   return enemyManager.queryEnemyObjects(point.x, point.y, range, side, []) || [];
        /// }
        /// </code></para>
        ///
        /// <para><b>与 QueryTargets 的区别：</b>
        /// 本方法返回敌人对象引用（<see cref="IEnemyEntity"/>），供攻击效果（如
        /// <c>MeleeAttackEffect</c>）直接调用 <see cref="Hit"/> 提交伤害，
        /// 避免 DTO→对象 的二次查找。<see cref="QueryTargets"/> 返回值类型 DTO，
        /// 供攻击调度只读选取目标。</para>
        ///
        /// <para><b>稳定有序（task 4.6）：</b>
        /// 同 <see cref="QueryTargets"/>，委托 <see cref="EnemyManager.QueryEnemyObjects"/>，
        /// 继承其稳定有序保证。</para>
        /// </remarks>
        internal List<IEnemyEntity> QueryEnemyObjects(
            EnemyManager enemyManager,
            float centerX,
            float centerY,
            float range,
            bool playerSide,
            float cellWidth,
            float cellHeight,
            List<IEnemyEntity> output)
        {
            // 对应 JS if (!enemyManager || typeof enemyManager.queryEnemyObjects !== 'function') return [];
            if (enemyManager == null)
            {
                return output ?? new List<IEnemyEntity>();
            }

            // 委托 EnemyManager 的稳定有序查询接口。
            // 对应 JS enemyManager.queryEnemyObjects(point.x, point.y, range, side, [])。
            return enemyManager.QueryEnemyObjects(
                centerX, centerY, range, playerSide, cellWidth, cellHeight, output);
        }

        // ====================================================================
        // 伤害提交 —— 委托目标自身的 hit，入口做死亡守卫
        // ====================================================================

        /// <summary>
        /// 对单个目标提交伤害（对应 JS <c>AttackResolver.js:20-25 hit</c>）。
        /// </summary>
        /// <param name="target">目标敌人实体（可为 null，对应 JS <c>if (!target) return false</c>）。</param>
        /// <param name="damage">伤害值（正数）。</param>
        /// <param name="attackerId">
        /// 攻击者运行时 ID（对应 JS <c>attacker</c> 对象引用，C# 移植改为 int）。
        /// 无攻击者传 -1。透传给 <see cref="IEnemyEntity.Hit"/>。
        /// </param>
        /// <returns>
        /// <c>true</c>=本次伤害生效（目标存活且扣血成功）；
        /// <c>false</c>=未生效（目标为 null、已死亡或伤害非正）。
        /// 对应 JS <c>target.hit(damage, attacker)</c> 的返回值。
        /// </returns>
        /// <remarks>
        /// <para><b>对应 JS <c>AttackResolver.js:20-25</c>：</b>
        /// <code>
        /// hit(target, damage, attacker) {
        ///   if (!target) return false;
        ///   if (typeof target.hit === 'function') return target.hit(damage, attacker);
        ///   if (typeof target.takeDamage === 'function') return target.takeDamage(damage, attacker);
        ///   return false;
        /// }
        /// </code></para>
        ///
        /// <para><b>C# 移植差异：</b>
        /// JS 原版同时支持 <c>hit</c> 与 <c>takeDamage</c> 两个方法名（<c>EnemyBase.js:481</c>
        /// <c>takeDamage</c> 是 <c>hit</c> 的别名）。C# <see cref="IEnemyEntity"/> 只暴露
        /// <see cref="IEnemyEntity.Hit"/>，故不需要双路径检测。</para>
        ///
        /// <para><b>死亡守卫（task 4.2 契约）：</b>
        /// 入口检查 <paramref name="target"/> 为 null 时返回 false（对应 JS
        /// <c>if (!target) return false</c>）。目标已死亡时 <see cref="IEnemyEntity.Hit"/>
        /// 内部 <c>if (health &lt;= 0) return false</c>（<c>EnemyBase.cs:990</c>）拒绝伤害。
        /// 本类型不重复检查 <see cref="IEnemyEntity.CurrentState"/>——死亡守卫的唯一权威
        /// 在 <see cref="EnemyBase.Hit"/>，避免多处判断产生不一致。</para>
        ///
        /// <para><b>冻结守卫（决策 0.4）：</b>
        /// 战斗冻结后敌人会被 <see cref="EnemyManager.ForceRemove"/> 或
        /// <see cref="EnemyManager.GameOver"/> 清理，不再在活动集合中。攻击效果若仍持有
        /// 旧目标引用并调用本方法，<see cref="IEnemyEntity.Hit"/> 的死亡守卫
        /// （<c>health &lt;= 0</c> 或 <c>CurrentState == Dead</c>）会拒绝伤害。
        /// 本类型不直接引用 <see cref="BattleResultBuilder.IsFrozen"/>，
        /// 保持与结果冻结器解耦——冻结后的伤害拒绝由 EnemyBase 自身状态保证。</para>
        ///
        /// <para><b>不重入销毁（spec "Freeze occurs inside a manager update"）：</b>
        /// 本方法只调用 <see cref="IEnemyEntity.Hit"/>，不修改 <see cref="EnemyManager"/>
        /// 集合。若伤害导致目标死亡，死亡敌人的移除由 <see cref="EnemyManager"/> 的
        /// 移除队列在遍历结束后统一处理（<c>EnemyManager.cs:1059 ProcessRemoveQueue</c>），
        /// 不在伤害调用栈内重入销毁集合。</para>
        /// </remarks>
        internal bool Hit(IEnemyEntity target, int damage, int attackerId)
        {
            // 对应 JS if (!target) return false。
            if (target == null)
            {
                return false;
            }

            // 伤害非正时不提交（对应 EnemyBase.Hit 内部的 damage <= 0 守卫，
            // 前置检查避免无谓的方法调用）。
            if (damage <= 0)
            {
                return false;
            }

            // 委托目标自身的 hit 提交伤害。
            // 对应 JS target.hit(damage, attacker) / target.takeDamage(damage, attacker)。
            // EnemyBase.Hit 内部有完整的死亡守卫（health <= 0 返回 false）与
            // 血量归零进入 DEAD 的逻辑，本方法只做透传。
            return target.Hit(damage, attackerId);
        }

        // ====================================================================
        // 批量伤害提交 —— 对应 EnemyManager.ApplyDamage 的便捷入口
        // --------------------------------------------------------------------
        // JS 原版 AttackResolver 没有批量 hit 方法（MeleeAttackEffect 在循环中逐个调
        // resolver.hit）。但 C# EnemyManager 已提供 ApplyDamage(damage, targetDtos,
        // attackerId) 批量入口。为避免攻击效果既持有 resolver 又持有 enemyManager
        // 造成知识泄漏，本类型提供批量委托入口，内部调用 EnemyManager.ApplyDamage。
        // ====================================================================

        /// <summary>
        /// 对一组目标 DTO 批量提交伤害（便捷入口，委托
        /// <see cref="EnemyManager.ApplyDamage"/>）。
        /// </summary>
        /// <param name="enemyManager">敌人管理器（非 null），提供按 ID 查找与伤害提交。</param>
        /// <param name="damage">伤害值（正数）。</param>
        /// <param name="targetDtos">目标 DTO 列表（按 Id 查找敌人）。</param>
        /// <param name="attackerId">攻击者运行时 ID（无攻击者传 -1）。</param>
        /// <remarks>
        /// <para>对应 <see cref="EnemyManager.ApplyDamage"/>（<c>EnemyManager.cs:1007</c>），
        /// 其源自 <c>EnemyManager.js:235-240 applyDamage</c>。
        /// JS 原版 AttackResolver 未提供此方法，但 C# 移植为统一攻击效果对
        /// EnemyManager 的知识隔离，提供此便捷入口。</para>
        ///
        /// <para><b>死亡守卫：</b>
        /// <see cref="EnemyManager.ApplyDamage"/> 按 DTO.Id 查找敌人，不存在的 Id 静默跳过；
        /// 找到的敌人经 <see cref="IEnemyEntity.Hit"/> 的死亡守卫拒绝迟到伤害。</para>
        ///
        /// <para><b>不重入销毁：</b>
        /// 同 <see cref="Hit"/>，不在伤害调用栈内修改 <see cref="EnemyManager"/> 集合。</para>
        /// </remarks>
        internal void ApplyDamage(
            EnemyManager enemyManager,
            int damage,
            List<EnemyTargetDto> targetDtos,
            int attackerId)
        {
            if (enemyManager == null || damage <= 0 || targetDtos == null)
            {
                return;
            }

            enemyManager.ApplyDamage(damage, targetDtos, attackerId);
        }

        // ====================================================================
        // 首选目标验证与稳定回退 —— 供锁定型攻击在命中点/释放点有限重选
        // --------------------------------------------------------------------
        // design 决策 3：有限重选复用 AttackResolver，不抽独立 Targeting 模块。
        // 该操作不提交伤害、不改变冷却、不保存状态，只返回解析后的目标 DTO。
        // ====================================================================

        /// <summary>
        /// 解析首选目标或稳定回退一次：供锁定型攻击在命中点/释放点目标失效时有限重选
        /// （design 决策 3/4）。
        /// </summary>
        /// <param name="enemyManager">敌人管理器（非 null），提供按 ID 查找与稳定查询。</param>
        /// <param name="preferredTargetId">本次攻击的初始目标 ID（调度器单次选择并传入）。</param>
        /// <param name="centerX">攻击者当前中心 X（回退查询中心）。</param>
        /// <param name="centerY">攻击者当前中心 Y（回退查询中心）。</param>
        /// <param name="range">攻击范围（回退查询半径，首选目标不强制在范围内）。</param>
        /// <param name="playerSide">攻击者阵营。</param>
        /// <param name="cellWidth">敌人格子宽（透传给稳定查询）。</param>
        /// <param name="cellHeight">敌人格子高（透传给稳定查询）。</param>
        /// <param name="resolvedTarget">解析成功时输出目标 DTO；失败时输出 <see cref="EnemyTargetDto.Invalid"/>。</param>
        /// <returns>true=解析到有效目标；false=首选失效且范围内无替代目标。</returns>
        /// <remarks>
        /// <para><b>解析规则（design 决策 3）：</b></para>
        /// <list type="number">
        /// <item>按首选 ID 获取目标；目标存在且仍可被攻击方攻击时直接返回该目标。
        ///   首选目标仅以"存在且可攻击"判定有效，保持锁定攻击对存活移动目标的现有语义，
        ///   不强制首选目标位于当前攻击范围内。</item>
        /// <item>首选目标失效（不存在或不可攻击）时，使用攻击者当前中心、攻击范围、阵营和
        ///   格子尺寸执行一次既有稳定查询（<see cref="QueryTargets"/>）。</item>
        /// <item>返回稳定顺序中的第一个有效候选；无候选返回失败。</item>
        /// </list>
        /// <para><b>不提交伤害、不改变冷却、不保存状态：</b>本操作只读取与查询，
        /// 不调用 <see cref="Hit"/>、不写回 <c>LastAttackTimeMs</c>、不缓存目标。</para>
        /// <para><b>稳定顺序：</b>回退查询委托 <see cref="QueryTargets"/> →
        /// <see cref="EnemyManager.QueryTargets"/>，继承 <c>_orderedIds</c>（spawn 顺序）
        /// 与空间索引的稳定有序保证，结果确定。</para>
        /// </remarks>
        internal bool TryResolvePreferredOrFallback(
            EnemyManager enemyManager,
            int preferredTargetId,
            float centerX,
            float centerY,
            float range,
            bool playerSide,
            float cellWidth,
            float cellHeight,
            out EnemyTargetDto resolvedTarget)
        {
            resolvedTarget = EnemyTargetDto.Invalid;

            if (enemyManager == null)
            {
                return false;
            }

            // 1. 首选目标验证：存在且可被本方攻击 → 直接返回。
            //    首选目标不强制在当前攻击范围内（锁定攻击对存活移动目标的现有语义）。
            IEnemyEntity preferred = enemyManager.GetById(preferredTargetId);
            if (preferred != null && preferred.IsTargetableBy(playerSide))
            {
                resolvedTarget = new EnemyTargetDto(
                    preferred.Id, preferred.X, preferred.Y, preferred.RemainingPathDistance);
                return true;
            }

            // 2. 首选失效 → 按当前攻击范围执行一次稳定查询。
            //    委托 QueryTargets，继承 EnemyManager 的稳定有序保证。
            List<EnemyTargetDto> candidates = QueryTargets(
                enemyManager, centerX, centerY, range, playerSide, cellWidth, cellHeight);

            // 3. 返回稳定顺序中的第一个有效候选；无候选返回失败。
            if (candidates != null && candidates.Count > 0)
            {
                resolvedTarget = candidates[0];
                return true;
            }

            return false;
        }
    }
}
