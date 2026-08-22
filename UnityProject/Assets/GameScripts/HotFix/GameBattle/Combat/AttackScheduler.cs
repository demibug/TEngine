using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.2：AttackScheduler —— 每子步只推进一次单位冷却、选取目标并触发一次攻击
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / Combat/AttackScheduler.cs / design.md:187）：
    //   每个子步只推进一次单位冷却、选取目标并触发攻击。
    //
    // 来源证据（还原工程 AttackScheduler.js:1-44）：
    //   还原工程 AttackScheduler 是一个无状态纯逻辑对象，由 BattleManager 在每子步
    //   _updateUnitAttacks 中对每个活动单位调用一次 scheduler.update(unit, opts)。
    //   核心逻辑：
    //     1. 守卫：!unit || !unit.isActive || unit.disabled || unit.inPool → 跳过。
    //     2. 计算单位中心点 centerX/Y = displayObject.x + width/2, y + height/2。
    //     3. currentTime = now()（同帧所有子步相同，对应 frameNowMs）。
    //     4. intervalMs = 1000 * (unit.attackIntervalSeconds ?? unit.attackIntervalScale)。
    //     5. 状态机：
    //        - 非 ATTACK 态：查询目标，若存在目标且冷却完毕 → 切换 ATTACK 态（本子步不攻击）。
    //        - ATTACK 态：若冷却未完毕 → 等待；若冷却完毕 → lastAttackTime = currentTime，
    //          重新查询目标，无目标 → 切换 IDLE，有目标 → 调用 unit.attack() 触发攻击。
    //   消费方：BattleManager._updateUnitAttacks（BattleManager.js:155-168）遍历 soldiers，
    //   对每个活动单位调 attackScheduler.update(unit, { enemyManager, now: () => currentTime })。
    //   MinimalBattleBootstrap.js:464 构造 new AttackScheduler({ enemyManager, resolver })。
    //
    // 决策依据：
    //   - design.md:187 文件职责表："每子步只推进一次单位冷却、选目标并触发攻击。"
    //   - design.md 第 2 节 / 决策 0.9 / spec "Frame time and substep time preserve source behavior"：
    //     冷却判断只读 frameNowMs（同帧所有子步观察同一值），不读 stepMs。
    //     550ms 外部帧拆成 7 子步时，所有子步的攻击冷却判断时间戳相同（=550），
    //     避免卡顿帧因时间戳递增而多触发攻击。
    //   - spec "Update phases are explicit and single-owned"：每个系统每子步最多更新一次。
    //     AttackScheduler 在 BattleUpdatePhase.UnitAttack 阶段由 BattleSimulation 回调一次，
    //     内部遍历全部活动单位，每个单位每子步只调用一次 Update，只触发一次攻击。
    //   - task 4.6 约束（稳定有序集合）：单位遍历顺序必须确定。本类型不自行维护单位集合，
    //     由调用方（UnitRegistry，task 6.3）提供稳定有序的单位列表（按放置顺序）。
    //     本类型只按传入列表顺序遍历，不依赖 Dictionary/HashSet 的未定义遍历顺序。
    //   - design.md 决策 4 / spec battle-event-boundary：攻击调度使用直接调用，不通过全局事件。
    //   - design.md 决策 0.4 / spec "Battle result is frozen once"：首次 TryFreeze 成功后
    //     只完成当前同步提交并中止剩余 phase/子步。本类型在遍历中若检测到冻结标志，
    //     停止剩余单位迭代。攻击触发后正常返回，不重入销毁集合；冻结后的迟到攻击由
    //     AttackResolver/EnemyManager 的死亡守卫与冻结守卫共同拒绝。
    //
    // 复用优先（tengine-dev skill 红线：先搜索现有代码）：
    //   1. BattleActionScheduler.IsAttackCooldownReady(lastAttackTimeMs, intervalMs)
    //      （BattleActionScheduler.cs:84-87）—— 冷却判断原语，读 FrameNowMs，同帧不变。
    //      本类型复用此方法，不重复实现冷却算术，保证双时钟语义一致。
    //   2. BattleActionScheduler.FrameNowMs（BattleActionScheduler.cs:27）—— 帧级时间戳。
    //      本类型通过此属性获取冷却判断时间戳，不直接读 BattleSimulation.FrameNowMs，
    //      保持单一冷却时钟源。BattleSimulation 在 BeginFrame 时同步 ActionScheduler.FrameNowMs
    //      （BattleSimulation.cs:173），两者一致。
    //   3. AttackResolver.QueryTargets（AttackResolver.cs:178-196）—— 稳定目标查询。
    //      本类型委托此方法查询目标，不直接访问 EnemyManager，隔离对 EnemyManager 的直接知识。
    //
    // C# 与 JS 的差异：
    //   1. 单位迭代：JS BattleManager._updateUnitAttacks 遍历 unitManager.soldiers.values()
    //      （Map 插入顺序）。C# 移植由调用方（UnitRegistry，task 6.3）提供稳定有序的
    //      IReadOnlyList<IAttackUnit>，本类型按列表顺序遍历，不自行维护单位集合。
    //   2. now() 注入：JS 通过 opts.now 注入时钟函数。C# 移植改为从 BattleActionScheduler
    //      读取 FrameNowMs，保持单一冷却时钟源，避免多源时钟不一致。
    //   3. displayObject.x/y/width/height：JS 从单位表现对象读位置。C# 移植为纯逻辑属性
    //      IAttackUnit.CenterX/CenterY，由单位实现提供逻辑中心坐标。
    //   4. weapon.attack vs unit.attack：JS 支持 unit.weapon.attack(targets[0]) 或 unit.attack()。
    //      C# 移植统一为 IAttackUnit.Attack()，具体兵种在 Attack 内部处理武器/投射物/效果创建。
    //   5. 无状态：与 JS 一致，本类型不持有任何可变状态，构造后所有方法为纯委托。
    //     每次战斗由 BattleRuntimeFactory 构造新实例（与 JS
    //     MinimalBattleBootstrap.js:464 new AttackScheduler({ enemyManager, resolver }) 一致）。
    //
    // 不变量：
    //   1. 每子步每单位只推进一次：Update 在每子步对每个单位只调用一次，内部最多触发一次攻击。
    //   2. 冷却用 frameNowMs：冷却判断只读 BattleActionScheduler.FrameNowMs，不读 stepMs 或真实时间。
    //   3. 稳定遍历：按调用方传入的列表顺序遍历单位，不自行排序或依赖无序集合。
    //   4. 冻结中止：遍历中若检测到冻结标志，停止剩余单位迭代。
    //   5. 不重入销毁：攻击触发只调用 unit.Attack()，不修改单位集合。
    //   6. 具体类无公共接口：internal sealed class。
    // ============================================================================

    /// <summary>
    /// 单位攻击调度最小契约：AttackScheduler 依赖的单位属性与行为。
    /// </summary>
    /// <remarks>
    /// <para><b>契约来源（UnitBase.js / SoldierBase.js 推断）：</b>
    /// UnitBase / SoldierBase（task 6.1/6.2 并行创建）将实现本接口。接口成员对应
    /// JS UnitBase/SoldierBase 的字段：
    /// <list type="bullet">
    /// <item><see cref="IsActive"/> ← <c>unit.isActive</c>（是否在战场活动）</item>
    /// <item><see cref="Disabled"/> ← <c>unit.disabled</c>（是否被禁用）</item>
    /// <item><see cref="InPool"/> ← <c>unit.inPool</c>（是否已回池）</item>
    /// <item><see cref="Side"/> ← <c>unit.side</c>（true=玩家方，false=对手方）</item>
    /// <item><see cref="CenterX"/>/<see cref="CenterY"/> ← <c>displayObject.x + width/2</c>
    ///   等（C# 移植为纯逻辑中心坐标）</item>
    /// <item><see cref="AttackRange"/> ← <c>unit.attackRange</c>（攻击范围，逻辑距离）</item>
    /// <item><see cref="AttackIntervalSeconds"/> ← <c>unit.attackIntervalSeconds</c>
    ///   （攻击冷却间隔，秒）</item>
    /// <item><see cref="LastAttackTimeMs"/> ← <c>unit.lastAttackTime</c>
    ///   （上次攻击时间戳，毫秒，读/写）</item>
    /// <item><see cref="CurrentState"/> ← <c>unit.currentState</c>
    ///   （单位状态枚举值，见 <see cref="AttackUnitState"/>）</item>
    /// <item><see cref="SetState"/> ← <c>unit.changeState(state)</c>（切换状态）</item>
    /// <item><see cref="Attack"/> ← <c>unit.attack()</c>（触发攻击，内部创建效果/投射物）</item>
    /// </list></para>
    ///
    /// <para><b>本接口为 internal：</b>只供 GameBattle 内部 AttackScheduler 与测试使用。
    /// task 6.1 的 UnitBase 将实现本接口；出现第二个调度消费方时再提取更宽接口。</para>
    ///
    /// <para><b>并行任务契约推断：</b>UnitBase（task 6.1）/ SoldierBase（task 6.2）/
    /// UnitRegistry（task 6.3）当前尚未存在。为使 AttackScheduler 可独立编译与测试，
    /// 定义此最小契约接口。接口成员依据 UnitBase.js / SoldierBase.js 推断，
    /// task 6.1 实现时将实现本接口。</para>
    /// </remarks>
    internal interface IAttackUnit
    {
        /// <summary>是否在战场活动（对应 <c>unit.isActive</c>）。</summary>
        bool IsActive { get; }

        /// <summary>是否被禁用（对应 <c>unit.disabled</c>）。</summary>
        bool Disabled { get; }

        /// <summary>是否已回池（对应 <c>unit.inPool</c>）。</summary>
        bool InPool { get; }

        /// <summary>阵营：true=玩家方，false=对手方（对应 <c>unit.side</c>）。</summary>
        bool Side { get; }

        /// <summary>逻辑中心 X（对应 <c>displayObject.x + width/2</c>）。</summary>
        float CenterX { get; }

        /// <summary>逻辑中心 Y（对应 <c>displayObject.y + height/2</c>）。</summary>
        float CenterY { get; }

        /// <summary>攻击范围（对应 <c>unit.attackRange</c>，逻辑距离）。</summary>
        float AttackRange { get; }

        /// <summary>攻击冷却间隔（秒，对应 <c>unit.attackIntervalSeconds</c>）。</summary>
        float AttackIntervalSeconds { get; }

        /// <summary>上次攻击时间戳（毫秒，对应 <c>unit.lastAttackTime</c>）。可读写。</summary>
        long LastAttackTimeMs { get; set; }

        /// <summary>当前单位状态（对应 <c>unit.currentState</c>）。</summary>
        AttackUnitState CurrentState { get; }

        /// <summary>切换单位状态（对应 <c>unit.changeState(state)</c>）。</summary>
        /// <param name="state">目标状态。</param>
        void SetState(AttackUnitState state);

        /// <summary>
        /// 触发一次攻击（对应 <c>unit.attack()</c>），接收调度器为本次攻击选定的唯一初始目标。
        /// 内部由具体兵种实现创建攻击效果 / 投射物 / 延迟命中等。
        /// </summary>
        /// <param name="initialTarget">
        /// 调度器为本次攻击选定的初始目标快照（对应 JS <c>targets[0]</c>）。
        /// 一次攻击只选择一次初始目标，兵种 MUST NOT 再独立执行第二次选敌查询。
        /// </param>
        /// <remarks>
        /// 调用前 AttackScheduler 已保证冷却完毕且存在目标，并已把稳定查询的第一个目标
        /// 作为本次攻击的初始目标传入。兵种使用该目标创建攻击效果或计算朝向；
        /// 锁定型攻击（刀兵/弓兵）在命中点/释放点目标失效时由各自 Effect 经
        /// <see cref="AttackResolver"/> 执行有限稳定重选，不再由兵种二次查询。
        /// </remarks>
        void Attack(EnemyTargetDto initialTarget);
    }

    /// <summary>
    /// 单位攻击状态枚举（对应还原工程 <c>UnitBase.js:12-17 UnitState</c>）。
    /// </summary>
    /// <remarks>
    /// <para>只保留 AttackScheduler 关心的状态子集。还原工程 UnitState 还包含
    /// NONE/PLACING 等放置态，但这些状态在攻击调度阶段已被 <see cref="IAttackUnit.IsActive"/>
    /// 守卫过滤，不会进入攻击状态机。本枚举只覆盖攻击调度涉及的两个态。</para>
    /// <para><b>与 JS 的对应：</b>NONE/PLACING 在 C# 中由 <c>IsActive=false</c> 等价表达，
    /// 不需要独立枚举值。</para>
    /// </remarks>
    internal enum AttackUnitState
    {
        /// <summary>
        /// 空闲态（对应 JS <c>UnitState.IDLE='UnitIdle'</c>）。
        /// <para>单位在战场活动但未进入攻击状态，等待目标与冷却就绪后切换到攻击态。</para>
        /// </summary>
        Idle = 0,

        /// <summary>
        /// 攻击态（对应 JS <c>UnitState.ATTACK='UnitAttack'</c>）。
        /// <para>单位已锁定目标并进入攻击循环，每子步检查冷却，冷却完毕后触发攻击。</para>
        /// </summary>
        Attack = 1,
    }

    /// <summary>
    /// 每子步推进一次单位冷却、选取目标并触发攻击的内部具体服务（task 5.2）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:187）：</b>每子步只推进一次单位冷却、选目标并触发攻击。
    /// 替代还原工程 <c>AttackScheduler.js</c> 的无状态纯逻辑对象
    /// （<c>AttackScheduler.js:7-42</c>）。</para>
    ///
    /// <para><b>双时钟语义（决策 0.9 / spec "Frame time and substep time preserve source behavior"）：</b>
    /// 冷却判断只读 <see cref="BattleActionScheduler.FrameNowMs"/>（外部帧时间戳，同一外部帧
    /// 的所有子步观察同一值），<b>不</b>读 <c>stepMs</c>。这保证：
    /// <list type="bullet">
    /// <item>同一外部帧多次子步的冷却判断时间戳不变，避免卡顿帧（如 550ms 拆成 7 子步）
    /// 因时间戳递增而多触发攻击。</item>
    /// <item>同时间戳的重复攻击被冷却拒绝（还原工程 <c>AttackScheduler.js:18,23,30,31</c>
    /// 的"same-timestamp duplicate attack is rejected"证据）。</item>
    /// </list>
    /// 本类型通过 <see cref="BattleActionScheduler.IsAttackCooldownReady"/> 复用已实现的
    /// 冷却判断原语，保证双时钟语义一致。</para>
    ///
    /// <para><b>每子步每单位只推进一次（spec "Update phases are explicit and single-owned"）：</b>
    /// 本类型在 <see cref="BattleUpdatePhase.UnitAttack"/> 阶段由
    /// <see cref="BattleSimulation"/> 回调一次 <see cref="Update"/>，内部遍历全部活动单位，
    /// 每个单位每子步只调用一次调度，只触发一次攻击。遍历按调用方传入的稳定有序列表
    /// （由 UnitRegistry 提供，按放置顺序），不依赖 Dictionary/HashSet 的未定义遍历顺序。</para>
    ///
    /// <para><b>稳定遍历（task 4.6 约束）：</b>本类型不自行维护单位集合，由调用方
    /// （UnitRegistry，task 6.3）提供稳定有序的 <c>IReadOnlyList&lt;IAttackUnit&gt;</c>。
    /// 本类型按列表顺序遍历，保证单位攻击调度顺序确定。</para>
    ///
    /// <para><b>冻结中止（决策 0.4 / spec "Battle result is frozen once"）：</b>
    /// 遍历中若检测到 <see cref="BattleActionScheduler.IsFrozen"/> 为 true，停止剩余单位迭代。
    /// 攻击触发后正常返回，不重入销毁集合；冻结后的迟到攻击由
    /// <see cref="AttackResolver"/>/<see cref="EnemyManager"/> 的死亡守卫与冻结守卫共同拒绝。</para>
    ///
    /// <para><b>目标查询委托（task 5.1 复用）：</b>
    /// 目标查询委托 <see cref="AttackResolver.QueryTargets"/>，不直接访问
    /// <see cref="EnemyManager"/>，隔离攻击调度对 EnemyManager 的直接知识。
    /// <see cref="AttackResolver"/> 只委托 <see cref="EnemyManager"/> 的稳定有序查询接口，
    /// 继承其稳定有序保证。</para>
    ///
    /// <para><b>无状态：</b>与 JS 原版一致，本类型不持有任何可变状态，所有方法为纯委托。
    /// 不需要 Reset/池化。每次战斗由 <see cref="BattleRuntimeFactory"/> 构造新实例
    /// （与 JS <c>MinimalBattleBootstrap.js:464 new AttackScheduler(...)</c> 一致）。</para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部
    /// <see cref="BattleRuntime"/> 在阶段回调中调用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class AttackScheduler
    {
        // ====================================================================
        // 构造依赖（不可变，构造时注入）
        // ====================================================================

        /// <summary>
        /// 到期动作/冷却调度器（task 2.11 产物）。
        /// <para>提供 <see cref="BattleActionScheduler.FrameNowMs"/>（同帧不变的帧级时间戳）
        /// 与 <see cref="BattleActionScheduler.IsAttackCooldownReady"/>（冷却判断原语）。
        /// 本类型通过此依赖获取冷却判断时间戳，保持单一冷却时钟源。</para>
        /// </summary>
        private readonly BattleActionScheduler _actionScheduler;

        /// <summary>
        /// 攻击解析服务（task 5.1 产物）。
        /// <para>提供稳定目标查询 <see cref="AttackResolver.QueryTargets"/>。本类型委托此方法
        /// 查询目标，不直接访问 <see cref="EnemyManager"/>。</para>
        /// </summary>
        private readonly AttackResolver _resolver;

        /// <summary>
        /// 敌人格子宽（对应 <c>map.gridWidth</c>，用于 <c>circleIntersectsRect</c> 精筛）。
        /// <para>透传给 <see cref="AttackResolver.QueryTargets"/>。由调用方
        /// （BattleRuntimeFactory）从配置或 MapData 注入。</para>
        /// </summary>
        private readonly float _cellWidth;

        /// <summary>
        /// 敌人格子高（对应 <c>map.gridHeight</c>）。透传给 <see cref="AttackResolver.QueryTargets"/>。
        /// </summary>
        private readonly float _cellHeight;

        /// <summary>
        /// 武将主动技能运行时（可为 null；null 时保持旧测试行为，不替换攻击槽）。
        /// <para>非 null 时，冷却完毕且有合法目标的武将攻击槽会先尝试技能激活：
        /// 成功则消费槽并跳过普通攻击，失败则普通攻击后累计 AttackCount。</para>
        /// </summary>
        private readonly GeneralSkillRuntime _skillRuntime;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造攻击调度器，注入冷却时钟源、目标查询服务与格子尺寸。
        /// </summary>
        /// <param name="actionScheduler">
        /// 到期动作/冷却调度器（非 null）。提供 <see cref="BattleActionScheduler.FrameNowMs"/>
        /// 与 <see cref="BattleActionScheduler.IsAttackCooldownReady"/>。
        /// </param>
        /// <param name="resolver">
        /// 攻击解析服务（非 null）。提供 <see cref="AttackResolver.QueryTargets"/>。
        /// 对应 JS <c>AttackScheduler.js:8-11</c> 注入 resolver。
        /// </param>
        /// <param name="cellWidth">
        /// 敌人格子宽（对应 <c>map.gridWidth</c>）。由调用方从配置或 MapData 注入。
        /// </param>
        /// <param name="cellHeight">敌人格子高（对应 <c>map.gridHeight</c>）。</param>
        /// <param name="skillRuntime">
        /// 武将主动技能运行时（可选；null 时保持旧测试行为，不替换攻击槽）。
        /// 非空时，武将攻击槽在冷却完毕且有合法目标时先尝试技能激活；
        /// 激活成功消费槽并跳过普通攻击，失败则普通攻击后累计计数。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="actionScheduler"/> 或 <paramref name="resolver"/> 为 null。
        /// </exception>
        internal AttackScheduler(
            BattleActionScheduler actionScheduler,
            AttackResolver resolver,
            float cellWidth,
            float cellHeight,
            GeneralSkillRuntime skillRuntime = null)
        {
            _actionScheduler = actionScheduler ?? throw new ArgumentNullException(nameof(actionScheduler));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _cellWidth = cellWidth;
            _cellHeight = cellHeight;
            _skillRuntime = skillRuntime;
        }

        // ====================================================================
        // 每子步调度入口 —— 由 BattleSimulation 在 UnitAttack 阶段回调
        // ====================================================================

        /// <summary>
        /// 推进一个子步的单位攻击调度：遍历全部活动单位，检查冷却，触发攻击。
        /// </summary>
        /// <param name="units">
        /// 本子步参与攻击调度的单位列表（稳定有序，由 UnitRegistry 提供按放置顺序）。
        /// 本类型按列表顺序遍历，不自行排序。列表可包含非活动单位，内部会被守卫跳过。
        /// </param>
        /// <param name="enemyManager">
        /// 敌人管理器（非 null），供 <see cref="AttackResolver.QueryTargets"/> 查询目标。
        /// 对应 JS <c>update(unit, { enemyManager })</c> 的 enemyManager 参数。
        /// </param>
        /// <returns>本子步实际触发攻击的单位数量。</returns>
        /// <remarks>
        /// <para><b>阶段（决策 0.3 / spec "Update phases are explicit"）：</b>
        /// 由 <see cref="BattleSimulation"/> 在 <see cref="BattleUpdatePhase.UnitAttack"/>
        /// 阶段回调，每子步一次。对应还原工程 <c>BattleManager._updateUnitAttacks</c>
        /// （<c>BattleManager.js:155-168</c>）。</para>
        ///
        /// <para><b>每子步每单位只推进一次（spec "Update phases are explicit and single-owned"）：</b>
        /// 遍历列表中每个单位一次，<see cref="ScheduleUnitAttack"/> 内部最多触发一次攻击。
        /// 不重复遍历，不嵌套调用。</para>
        ///
        /// <para><b>冷却用 frameNowMs（决策 0.9 / spec "Frame time and substep time preserve source behavior"）：</b>
        /// 冷却判断通过 <see cref="BattleActionScheduler.IsAttackCooldownReady"/> 读取
        /// <see cref="BattleActionScheduler.FrameNowMs"/>（同帧所有子步不变），不读 stepMs。
        /// 550ms 外部帧拆成 7 子步时，所有子步的冷却判断时间戳相同（=550）。</para>
        ///
        /// <para><b>冻结中止（决策 0.4 / spec "Battle result is frozen once"）：</b>
        /// 遍历中若检测到 <see cref="BattleActionScheduler.IsFrozen"/> 为 true，
        /// 停止剩余单位迭代。已触发的攻击作为同步副作用正常生效，后续单位不再调度。</para>
        ///
        /// <para><b>稳定遍历（task 4.6 约束）：</b>
        /// 按 <paramref name="units"/> 列表顺序遍历，不依赖 Dictionary/HashSet 的未定义遍历顺序。
        /// 列表顺序由 UnitRegistry 保证（按放置顺序）。</para>
        /// </remarks>
        internal int Update(IReadOnlyList<IAttackUnit> units, EnemyManager enemyManager)
        {
            if (units == null || units.Count == 0)
            {
                return 0;
            }

            // 冻结守卫：已冻结（Settling）不再调度任何攻击。
            // 对应决策 0.4：Settling 关闭新攻击入口。
            if (_actionScheduler.IsFrozen)
            {
                return 0;
            }

            int attackCount = 0;
            int count = units.Count;
            for (int i = 0; i < count; i++)
            {
                // 遍历中若冻结则停止剩余单位迭代（决策 0.4）。
                // 攻击触发可能导致目标死亡 → 移除 → TryFreeze，需在每次迭代前检查。
                if (_actionScheduler.IsFrozen)
                {
                    break;
                }

                IAttackUnit unit = units[i];
                if (ScheduleUnitAttack(unit, enemyManager))
                {
                    attackCount++;
                }
            }

            return attackCount;
        }

        // ====================================================================
        // 单单位攻击调度 —— 对应 JS AttackScheduler.js:13-41 update(unit, opts)
        // --------------------------------------------------------------------
        // JS 原版以单个 unit 为参数，BattleManager 在循环中对每个 unit 调用。
        // C# 移植保持同一粒度，ScheduleUnitAttack 是单单位调度，Update 是批量遍历入口。
        // ====================================================================

        /// <summary>
        /// 调度单个单位的攻击：检查守卫、冷却、目标，触发攻击。
        /// </summary>
        /// <param name="unit">单位（可为 null，守卫跳过）。</param>
        /// <param name="enemyManager">敌人管理器（非 null）。</param>
        /// <returns>true=本子步触发了攻击；false=未触发（守卫跳过、冷却未到、无目标）。</returns>
        /// <remarks>
        /// <para><b>对应 JS <c>AttackScheduler.js:13-41 update(unit, opts)</c>：</b>
        /// <code>
        /// update(unit, { enemyManager, now }) {
        ///   if (!unit || !unit.isActive || unit.disabled || unit.inPool) return { attacked: false };
        ///   const centerX = unit.displayObject.x + unit.displayObject.width / 2;
        ///   const centerY = unit.displayObject.y + unit.displayObject.height / 2;
        ///   const currentTime = now();
        ///   const intervalMs = 1000 * (unit.attackIntervalSeconds ?? unit.attackIntervalScale);
        ///   if (unit.currentState !== ATTACK) {
        ///     unit.targets = resolver.queryTargets(...);
        ///     if (targets.length > 0 && currentTime - lastAttackTime >= intervalMs)
        ///       unit.changeState(ATTACK);
        ///     return { attacked: false };
        ///   }
        ///   if (disabled || inPool) { unit.changeState(IDLE); return { attacked: false }; }
        ///   if (currentTime - lastAttackTime < intervalMs) return { attacked: false, reason: 'cooldown' };
        ///   unit.lastAttackTime = currentTime;
        ///   unit.targets = resolver.queryTargets(...);
        ///   if (!targets || targets.length === 0) { unit.changeState(IDLE); return { attacked: false }; }
        ///   unit.weapon ? unit.weapon.attack(targets[0]) : unit.attack();
        ///   return { attacked: true };
        /// }
        /// </code></para>
        ///
        /// <para><b>状态机：</b>
        /// <list type="number">
        /// <item>守卫：单位为 null / 非活动 / 禁用 / 回池 → 跳过（对应 JS 第 14 行）。</item>
        /// <item>非 ATTACK 态：查询目标，若存在目标且冷却完毕 → 切换 ATTACK 态（本子步不攻击）。
        /// 对应 JS 第 21-25 行——首次锁定目标只切状态，下一子步才在 ATTACK 态中检查冷却并攻击。</item>
        /// <item>ATTACK 态中再次检查禁用/回池：若已禁用/回池 → 切换 IDLE（对应 JS 第 26-29 行）。</item>
        /// <item>ATTACK 态冷却检查：若冷却未完毕 → 等待（对应 JS 第 30 行）。</item>
        /// <item>ATTACK 态冷却完毕：写回 lastAttackTime = frameNowMs，重新查询目标，
        /// 无目标 → 切换 IDLE，有目标 → 调用 unit.Attack() 触发攻击（对应 JS 第 31-40 行）。</item>
        /// </list></para>
        ///
        /// <para><b>冷却时间戳写回：</b>冷却完毕时 <c>unit.LastAttackTimeMs = FrameNowMs</c>，
        /// 保证同帧后续子步的冷却判断时间戳不变（对应 JS
        /// <c>unit.lastAttackTime = currentTime</c>，<c>currentTime = now()</c> 同帧不变）。
        /// 这是"同帧固定 frameNowMs 判断冷却"的核心——写回与判断都读同一帧时间戳。</para>
        ///
        /// <para><b>目标查询委托（task 5.1 复用）：</b>
        /// 目标查询委托 <see cref="AttackResolver.QueryTargets"/>，不直接访问
        /// <see cref="EnemyManager"/>。<see cref="AttackResolver.QueryTargets"/> 返回新
        /// <see cref="List{T}"/>（与 JS 返回新数组一致），本类型不缓存查询结果，
        /// 每次调度查询都基于当前帧的敌人状态。</para>
        /// </remarks>
        private bool ScheduleUnitAttack(IAttackUnit unit, EnemyManager enemyManager)
        {
            // 守卫：单位为 null / 非活动 / 禁用 / 回池 → 跳过。
            // 对应 JS if (!unit || !unit.isActive || unit.disabled || unit.inPool) return。
            if (unit == null || !unit.IsActive)
            {
                return false;
            }

            // Attack 态被禁用或回池时仍需显式退出到 Idle。
            // 该转换必须位于通用跳过逻辑之前，否则下方 Attack 态守卫永远不可达。
            if (unit.CurrentState == AttackUnitState.Attack && (unit.Disabled || unit.InPool))
            {
                unit.SetState(AttackUnitState.Idle);
                return false;
            }

            // 非 Attack 态的禁用/回池单位直接跳过，不产生状态或攻击副作用。
            if (unit.Disabled || unit.InPool)
            {
                return false;
            }

            // 冷却间隔（毫秒）= 1000 * attackIntervalSeconds。
            // 对应 JS intervalMs = 1000 * (unit.attackIntervalSeconds ?? unit.attackIntervalScale)。
            // C# 移植统一用 AttackIntervalSeconds（SoldierBase.attackIntervalSeconds getter）。
            long intervalMs = (long)(1000f * unit.AttackIntervalSeconds);

            // 复用 BattleActionScheduler 的冷却判断原语（读 FrameNowMs，同帧不变）。
            // 对应 JS currentTime - unit.lastAttackTime >= intervalMs。
            long frameNowMs = _actionScheduler.FrameNowMs;
            bool cooldownReady = _actionScheduler.IsAttackCooldownReady(unit.LastAttackTimeMs, intervalMs);

            // ----------------------------------------------------------------
            // 非 ATTACK 态：查询目标，若存在目标且冷却完毕 → 切换 ATTACK 态。
            // 对应 JS 第 21-25 行。本子步不攻击，下一子步在 ATTACK 态中检查冷却。
            // ----------------------------------------------------------------
            if (unit.CurrentState != AttackUnitState.Attack)
            {
                // 查询目标（委托 AttackResolver）。QueryTargets 返回新 List（与 JS 返回新数组一致）。
                List<EnemyTargetDto> targets = _resolver.QueryTargets(
                    enemyManager,
                    unit.CenterX, unit.CenterY,
                    unit.AttackRange,
                    unit.Side,
                    _cellWidth, _cellHeight);

                // 有目标且冷却完毕 → 切换 ATTACK 态。
                // 对应 JS if (targets.length > 0 && currentTime - lastAttackTime >= intervalMs)
                //   unit.changeState(UnitState.ATTACK)。
                if (targets != null && targets.Count > 0 && cooldownReady)
                {
                    unit.SetState(AttackUnitState.Attack);
                }

                // 非 ATTACK 态本子步不攻击（对应 JS return { attacked: false }）。
                return false;
            }

            // ----------------------------------------------------------------
            // ATTACK 态冷却检查：若冷却未完毕 → 等待。
            // 对应 JS if (currentTime - lastAttackTime < intervalMs) return { reason: 'cooldown' }。
            // ----------------------------------------------------------------
            if (!cooldownReady)
            {
                return false;
            }

            // ----------------------------------------------------------------
            // 冷却完毕：写回 lastAttackTime = frameNowMs，重新查询目标，触发攻击。
            // 对应 JS 第 31-40 行。
            // ----------------------------------------------------------------

            // 写回攻击时间戳（同帧固定 frameNowMs，保证同帧后续子步冷却判断不变）。
            // 对应 JS unit.lastAttackTime = currentTime。
            unit.LastAttackTimeMs = frameNowMs;

            // 重新查询目标（委托 AttackResolver）。
            // 对应 JS unit.targets = resolver.queryTargets({ enemyManager, center, range, side })。
            List<EnemyTargetDto> attackTargets = _resolver.QueryTargets(
                enemyManager,
                unit.CenterX, unit.CenterY,
                unit.AttackRange,
                unit.Side,
                _cellWidth, _cellHeight);

            // 无目标 → 切换 IDLE，不攻击。
            // 对应 JS if (!targets || targets.length === 0) { unit.changeState(IDLE); return; }。
            if (attackTargets == null || attackTargets.Count == 0)
            {
                unit.SetState(AttackUnitState.Idle);
                return false;
            }

            // ----------------------------------------------------------------
            // 武将技能替换攻击槽（Wave 3）：
            // 有合法目标时，先尝试用武将主动技能替换本攻击槽；成功消费槽并跳过普通攻击，
            // 失败则普通攻击后累计 AttackCount。普通兵或未装配 runtime 时保持旧行为。
            // ----------------------------------------------------------------
            if (_skillRuntime != null && unit is SoldierBase soldier)
            {
                if (_skillRuntime.TryActivateInsteadOfAttack(soldier))
                {
                    // 技能激活成功：消费本攻击槽，跳过普通攻击（不累计 AttackCount）。
                    return true;
                }

                // 技能未激活（未绑定/未达阈值/Busy/OnCooldown 等）：普通攻击后累计。
                unit.Attack(attackTargets[0]);
                _skillRuntime.OnBasicAttack(soldier);
                return true;
            }

            // 有目标 → 触发攻击。
            // 对应 JS unit.weapon ? unit.weapon.attack(targets[0]) : unit.attack()。
            // C# 移植把调度器选定的第一个目标作为本次攻击的唯一初始目标显式传入单位，
            // 消除调度器与兵种对同一次攻击的重复选敌（design 决策 2）。
            unit.Attack(attackTargets[0]);
            return true;
        }
    }
}
