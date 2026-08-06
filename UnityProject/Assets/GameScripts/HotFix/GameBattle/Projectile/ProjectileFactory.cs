using System;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.7：ProjectileFactory —— 创建/回收 SimpleDynamicArrow 并验证重置
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 197 行 / Projectile/ProjectileFactory.cs）：
    //   创建/回收 SimpleDynamicArrow 并验证重置。
    //
    // 来源证据（还原工程 ProjectileFactory.js:1-174）：
    //   - 原始符号 vj/vk，重建状态 COMPLETE_FOR_CURRENT_REGISTERED_TYPES
    //   - 构造时注册 SimpleDynamicArrow（ProjectileFactory.js:52）
    //   - produce(config)：
    //       * 从池 takeByKey(poolKey)
    //       * configure + initialize + initializeAppearance
    //       * projectileId = nextProjectileId++
    //       * 返回 projectile
    //   - recover(projectile)：
    //       * projectile.recover() → objectPool.recoverByKey
    //       * 幂等：已 recovered 返回 false
    //
    // 决策依据：
    //   - design.md 决策 5：删除 SingletonBase/全局 ObjectPool，改为强类型注入。
    //   - design.md 第 197 行：只创建/回收 SimpleDynamicArrow。
    //   - task 4.1：Acquire/Release 对称，完整 Reset 契约。
    //   - projectile-pool-reset-contract.md：每次 Acquire 必须复位 ID、目标、起点、
    //     进度、命中集合、状态标记。
    //
    // 不变量：
    //   1. 只创建 SimpleDynamicArrow：不提供运行时注册其他类型的能力。
    //   2. 每次 Acquire 分配新 ID：通过 RuntimeIdAllocator，从 1 单调递增。
    //   3. Release 后旧 ID/目标引用失效：ResetState 清除全部可变状态。
    //   4. Acquire/Release 对称：每次 Acquire 对应恰好一次 Release。
    //   5. 不预热：池在首次 Acquire 时才创建对象。
    // ============================================================================

    /// <summary>
    /// 创建/回收 <see cref="SimpleDynamicArrow"/> 并验证重置的投射物工厂。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 197 行）：</b>创建/回收 SimpleDynamicArrow 并验证重置。
    /// 替代还原工程 <c>ProjectileFactory</c>（ProjectileFactory.js:1-174）。</para>
    ///
    /// <para><b>只创建 SimpleDynamicArrow（task 5.7 范围）：</b>
    /// 本期只覆盖 SimpleDynamicArrow，不提供 register 公开 API。多类型注册能力延后。
    /// 对应还原工程构造时 <c>register(SimpleDynamicArrow.projectileTypeKey, SimpleDynamicArrow)</c>。</para>
    ///
    /// <para><b>每次 Acquire 分配新 ID：</b>
    /// <see cref="Acquire"/> 取得 <see cref="SimpleDynamicArrow"/> 后立即调用
    /// <see cref="RuntimeIdAllocator.Allocate"/> 分配新投射物 ID，并创建/绑定移动与命中策略。
    /// 池复用不复用旧 ID。</para>
    ///
    /// <para><b>Release 后旧 ID/目标引用失效：</b>
    /// <see cref="Release"/> 委托 <see cref="BattleObjectPool{T}.Release"/>，
    /// 后者在入池前调用 <see cref="IPoolableBattleObject.ResetState"/> 清除全部可变状态。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 ProjectileManager/BattleRuntimeFactory
    /// 使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class ProjectileFactory
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>日志标签前缀。</summary>
        private const string LogTag = "[ProjectileFactory]";

        // ====================================================================
        // 运行时 ID 分配器（每局新建，注入）
        // ====================================================================

        /// <summary>运行时 ID 分配器。每次 Acquire 后分配新投射物 ID。</summary>
        private readonly RuntimeIdAllocator _idAllocator;

        // ====================================================================
        // SimpleDynamicArrow 对象池（跨局复用容量，注入）
        // ====================================================================

        /// <summary>SimpleDynamicArrow 对象池。</summary>
        private readonly BattleObjectPool<SimpleDynamicArrow> _arrowPool;

        // ====================================================================
        // 敌人管理器与格子尺寸（注入，供策略 Configure）
        // ====================================================================

        /// <summary>敌人管理器，供移动策略查询目标。</summary>
        private readonly EnemyManager _enemyManager;

        /// <summary>格子尺寸（px，对应 map.gridWidth=80）。目标中心点计算用。</summary>
        private readonly float _cellSize;

        // ====================================================================
        // 诊断日志
        // ====================================================================

        /// <summary>创建日志（诊断用）。</summary>
        private readonly System.Collections.Generic.List<string> _createLog =
            new System.Collections.Generic.List<string>();

        /// <summary>回收日志（诊断用）。</summary>
        private readonly System.Collections.Generic.List<string> _recoverLog =
            new System.Collections.Generic.List<string>();

        // ====================================================================
        // 诊断属性
        // ====================================================================

        /// <summary>已创建累计次数（诊断用）。</summary>
        internal int CreateCount => _createLog.Count;

        /// <summary>已回收累计次数（诊断用）。</summary>
        internal int RecoverCount => _recoverLog.Count;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造只创建 SimpleDynamicArrow 的投射物工厂。
        /// </summary>
        /// <param name="idAllocator">运行时 ID 分配器。每局新建。不可为 null。</param>
        /// <param name="arrowPool">SimpleDynamicArrow 对象池。不可为 null。</param>
        /// <param name="enemyManager">敌人管理器。不可为 null。</param>
        /// <param name="cellSize">格子尺寸（px，对应 map.gridWidth=80）。</param>
        /// <exception cref="ArgumentNullException">任一必需参数为 null。</exception>
        internal ProjectileFactory(
            RuntimeIdAllocator idAllocator,
            BattleObjectPool<SimpleDynamicArrow> arrowPool,
            EnemyManager enemyManager,
            float cellSize)
        {
            _idAllocator = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _arrowPool = arrowPool ?? throw new ArgumentNullException(nameof(arrowPool));
            _enemyManager = enemyManager ?? throw new ArgumentNullException(nameof(enemyManager));
            _cellSize = cellSize > 0 ? cellSize : 80f;
        }

        // ====================================================================
        // Acquire —— 获取一个 SimpleDynamicArrow 并分配新 ID、绑定策略
        // ====================================================================

        /// <summary>
        /// 获取一个 <see cref="SimpleDynamicArrow"/>，分配新运行时 ID，
        /// 创建并绑定移动与命中策略。调用方需随后调用 <see cref="Configure"/> 和
        /// <see cref="ProjectileBase.ResetData"/> 完成发射前初始化。
        /// </summary>
        /// <param name="targetId">目标敌人运行时 ID。</param>
        /// <param name="attackerId">攻击者运行时 ID（无攻击者传 -1）。</param>
        /// <param name="attackerDamage">攻击者攻击力（未显式指定伤害时使用）。</param>
        /// <param name="explicitDamage">是否显式指定伤害值。</param>
        /// <param name="damage">显式伤害值（仅 <paramref name="explicitDamage"/> 为 true 时生效）。</param>
        /// <param name="speedScale">速度缩放（默认 1.75，对应 BowSoldier.projectileSpeedScale）。</param>
        /// <param name="curveHeight">贝塞尔弧高（默认 120，对应 BowSoldier launchArrow curveHeight:120）。</param>
        /// <param name="creationFrameMs">创建时的帧时间戳（毫秒），用于新箭下一子步才移动守卫。</param>
        /// <returns>已分配 ID 并绑定策略的 <see cref="SimpleDynamicArrow"/>。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>produce(config)</c>（ProjectileFactory.js:86-130）：
        /// 从池取出 → configure → initialize → 分配 projectileId → 返回。</para>
        /// <para><b>策略创建：</b>每次 Acquire 创建新的 TargetEnemyBezierMovement 和
        /// HitEnemyStrategy 实例并绑定。本期策略对象不池化（简化），
        /// 策略随投射物 Release 时由 SimpleDynamicArrow.ResetState 清除引用。</para>
        /// <para><b>新箭下一子步才移动（task 2.11/0.9）：</b>
        /// <see cref="creationFrameMs"/> 通过 <see cref="ProjectileBase.MarkCreationFrame"/>
        /// 记录创建帧，<see cref="ProjectileBase.Advance"/> 首次调用时若仍在创建帧则跳过推进。</para>
        /// </remarks>
        internal SimpleDynamicArrow Acquire(
            int targetId,
            int attackerId,
            int attackerDamage,
            bool explicitDamage,
            int damage,
            float speedScale = 1.75f,
            float curveHeight = 120f,
            long creationFrameMs = 0)
        {
            SimpleDynamicArrow arrow = _arrowPool.Acquire();

            if (arrow == null)
            {
                throw new InvalidOperationException(
                    $"{LogTag} 池返回 null 对象 type={nameof(SimpleDynamicArrow)}");
            }

            // 分配新投射物 ID（对应 JS nextProjectileId++）。
            int newId = _idAllocator.Allocate();
            arrow.AssignProjectileId(newId);
            arrow.Configure(_enemyManager);

            // 标记创建帧，用于新箭下一子步才移动守卫（task 2.11/0.9）。
            arrow.MarkCreationFrame(creationFrameMs);

            // 创建移动策略并注入目标。
            var movement = new TargetEnemyBezierMovement();
            movement.Configure(_enemyManager, _cellSize);
            movement.ResetParameters(curveHeight, true, false, true);
            movement.SetTargetId(targetId);

            // 创建命中策略（requestRemove 模式，命中后移除）。
            var hitStrategy = new HitEnemyStrategy();
            hitStrategy.Reset(targetId, null, 0, true, "requestRemove");

            // 绑定策略到投射物。
            arrow.BindStrategies(movement, hitStrategy);

            // 注入发射配置（对应 JS resetData）。
            arrow.ResetData(attackerId, attackerDamage, explicitDamage, damage, speedScale, 0);

            // 绑定移动策略到投射物（计算命中半径等）。
            movement.Attach(arrow, SimpleDynamicArrow.DefaultHeight);

            _createLog.Add(nameof(SimpleDynamicArrow));
            return arrow;
        }

        // ====================================================================
        // Release —— 归还 SimpleDynamicArrow 到池
        // ====================================================================

        /// <summary>
        /// 归还一个 <see cref="SimpleDynamicArrow"/> 到池。先执行 Recover 和 ResetState，
        /// 再入池。回收后旧 ID/目标引用不得继续有效。
        /// </summary>
        /// <param name="arrow">要归还的箭矢。null 或已归还返回 false。</param>
        /// <returns>成功归还返回 true；null 或已归还（重复 Release）返回 false。</returns>
        /// <remarks>
        /// <para>对应还原工程 <c>recover(projectile)</c>（ProjectileFactory.js:132-142）：
        /// <c>projectile.recover()</c> → <c>objectPool.recoverByKey</c>。</para>
        /// <para>本工厂先调用 <see cref="ProjectileBase.Recover"/>（投射物自身状态回收），
        /// 再委托 <see cref="BattleObjectPool{T}.Release"/>（池入池 + ResetState）。</para>
        /// </remarks>
        internal bool Release(SimpleDynamicArrow arrow)
        {
            if (arrow == null)
            {
                return false;
            }

            // 先执行投射物自身回收（清除活动标记、命中集合等）。
            arrow.Recover();

            // 委托池执行 Reset + 入池。
            bool recovered = _arrowPool.Release(arrow);

            if (recovered)
            {
                _recoverLog.Add(nameof(SimpleDynamicArrow));
            }

            return recovered;
        }

        // ====================================================================
        // ResetForTests —— 测试重置
        // ====================================================================

        /// <summary>
        /// 重置工厂诊断日志（仅供测试使用）。
        /// </summary>
        internal void ResetForTests()
        {
            _createLog.Clear();
            _recoverLog.Clear();
        }
    }
}
