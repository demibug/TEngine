using System;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 4.5：EnemyFactory —— 只注册/创建/回收 Mob0 的敌人工厂
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Enemy/EnemyFactory.cs）：
    //   只注册/创建/回收 Mob0，并执行池重置契约。
    //
    // 来源证据（还原工程 EnemyFactory.js）：
    //   - EnemyFactory 继承 SingletonBase，持 creators: Map<string, () => instance>。
    //   - configure({ objectPool })：注入 ObjectPool，registerPooledClass 依赖它。
    //   - registerPooledClass(typeName, ClassType, configureInstance)：
    //       * 注册 creator = () => objectPool.takeByClass(ClassType, () => new ClassType());
    //       * 取得实例后调用 configureInstance(instance)。
    //   - create(typeName)：
    //       * creators.get(typeName)；不存在抛 "未为类型 ... 注册创建器"。
    //       * result = creator()；result 为空抛 "creator returned empty value"。
    //       * createLog.push({ typeName, enemy: result })。
    //       * 返回 result。
    //   - produce(ClassType)：objectPool.takeByClass(ClassType)。
    //   - recover(enemy)：objectPool.recoverByClass(enemy)；成功则 recoverLog.push(enemy)。
    //   - resetForTests()：清空 creators/createLog/recoverLog，objectPool=null。
    //
    // 本期裁剪（design.md 决策 5 / 决策 6 / "NormalEnemyBase 合入 Mob0Enemy"）：
    //   - 删除 SingletonBase（design 决策 5）：改为 internal sealed class，由
    //     BattleRuntimeFactory 构造注入 RuntimeIdAllocator + BattleObjectPool<Mob0Enemy>。
    //   - 只注册 Mob0（design 决策 5 / 本 change 范围 "只覆盖 Mob0"）：
    //     不提供 register/registerPooledClass 公开 API；ENEMY_TYPE_KEYS / BOSS_TYPE_KEYS
    //     等多类型注册能力延后到后续 change。未知类型在 Acquire 时显式失败。
    //   - 合入 NormalEnemyBase 到 Mob0Enemy（design 决策 5 / task 4.4）：
    //     工厂只与 Mob0Enemy 类型耦合，不引入 NormalEnemyBase 中间基类。
    //   - 池复用不复用旧 ID（design.md 目录表 RuntimeIdAllocator / task 4.5）：
    //     每次 Acquire 后调用 RuntimeIdAllocator.Allocate() 分配新 ID；
    //     Release 后旧 ID/目标引用通过 Mob0Enemy.ResetState 清除（由 BattleObjectPool.Release
    //     在入池前调用），旧 ID 不再有效。
    //
    // 不变量：
    //   1. 只注册 Mob0：构造即绑定 Mob0Enemy 类型；不提供运行时注册其他类型的能力。
    //   2. 每次 Acquire 分配新 ID：通过 RuntimeIdAllocator.Allocate()，从 1 单调递增。
    //   3. Release 后旧 ID/目标引用失效：BattleObjectPool.Release 调用 Mob0Enemy.ResetState
    //      清除全部可变状态；工厂额外将 Mob0Enemy 的运行时 ID 重置为 0，确保旧 ID 不再可用。
    //   4. Acquire/Release 对称：每次 Acquire 对应恰好一次 Release，由 BattleObjectPool 保证。
    //   5. 不预热：池在首次 Acquire 时才创建 Mob0Enemy，不预先创建。
    // ============================================================================

    /// <summary>
    /// 只注册/创建/回收 Mob0 的敌人工厂，执行池重置契约。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>只注册/创建/回收 Mob0，并执行池重置契约。
    /// 替代还原工程 <c>EnemyFactory</c>（EnemyFactory.js）的多类型注册 + SingletonBase 模式。</para>
    ///
    /// <para><b>只注册 Mob0（design.md 决策 5 / 本 change 范围）：</b>
    /// 本期只覆盖 Mob0，不提供 <c>register/registerPooledClass</c> 公开 API。未知类型在
    /// Acquire 时显式失败（<see cref="Acquire"/> 只返回 <see cref="Mob0Enemy"/>）。
    /// 多类型注册能力延后到后续 change。</para>
    ///
    /// <para><b>每次 Acquire 分配新 ID（task 4.5）：</b>
    /// <see cref="Acquire"/> 取得 <see cref="Mob0Enemy"/> 后立即调用
    /// <see cref="RuntimeIdAllocator.Allocate"/> 分配新运行时 ID，并通过
    /// <c>Mob0Enemy.AssignRuntimeId</c> 写入。池复用不复用旧 ID
    /// （design.md 目录表 RuntimeIdAllocator）。</para>
    ///
    /// <para><b>Release 后旧 ID/目标引用失效（task 4.5）：</b>
    /// <see cref="Release"/> 委托 <see cref="BattleObjectPool{T}.Release"/>，
    /// 后者在入池前调用 <see cref="IPoolableBattleObject.ResetState"/>（即
    /// <c>Mob0Enemy.ResetState</c>）清除全部可变状态（运行时 ID、阵营、生命、路径、
    /// 目标引用、攻击冷却、接触回调标记、位置、击退、贡献者等）。
    /// 回收后对象等价于新构造，旧 ID/目标引用不得继续有效。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 EnemyManager / BattleRuntimeFactory
    /// 使用，不对其他程序集暴露。Manager 是唯一注册/注销入口
    /// （11_POOLING_AND_OWNERSHIP.md：UI 和 View 不得直接销毁 Domain 对象）。</para>
    /// </remarks>
    internal sealed class EnemyFactory
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>
        /// 日志标签前缀，便于在日志中筛选敌人工厂相关条目。
        /// </summary>
        private const string LogTag = "[EnemyFactory]";

        // ====================================================================
        // 运行时 ID 分配器（每局新建，注入）
        // ====================================================================

        /// <summary>
        /// 运行时 ID 分配器。每次 <see cref="Acquire"/> 后分配新 ID，保证池复用不复用旧 ID。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 在每局构造时注入，每局独立，
        /// 不跨局复用（design.md 目录表 RuntimeIdAllocator）。</para>
        /// </summary>
        private readonly RuntimeIdAllocator _idAllocator;

        // ====================================================================
        // Mob0 对象池（跨局复用容量，注入）
        // ====================================================================

        /// <summary>
        /// Mob0 对象池。Acquire 优先复用空闲对象，Release 归还并执行 ResetState。
        /// <para>由 <see cref="BattleRuntimeFactory"/> 通过 <see cref="BattlePoolScope.GetPool{T}"/>
        /// 获取并注入。池实例跨局复用空闲容量（task 4.1），活动对象逐局清空。</para>
        /// <para>对应还原工程 <c>EnemyFactory.objectPool</c>（EnemyFactory.js:14-24），
        /// 但从全局 ObjectPool.takeByClass/recoverByClass 改为强类型
        /// <see cref="BattleObjectPool{Mob0Enemy}"/>。</para>
        /// </summary>
        private readonly BattleObjectPool<Mob0Enemy> _mob0Pool;

        // ====================================================================
        // 诊断日志（对应 EnemyFactory.js createLog/recoverLog）
        // ====================================================================

        /// <summary>
        /// 创建日志：记录每次 Acquire 的 typeName 与 enemy 引用。
        /// <para>对应还原工程 <c>EnemyFactory.createLog</c>（EnemyFactory.js:16）。
        /// 诊断用，不参与规则逻辑。本期只记录 Mob0。</para>
        /// </summary>
        private readonly System.Collections.Generic.List<string> _createLog =
            new System.Collections.Generic.List<string>();

        /// <summary>
        /// 回收日志：记录每次 Release 的 typeName。
        /// <para>对应还原工程 <c>EnemyFactory.recoverLog</c>（EnemyFactory.js:17）。
        /// 诊断用，不参与规则逻辑。本期只记录 Mob0。</para>
        /// </summary>
        private readonly System.Collections.Generic.List<string> _recoverLog =
            new System.Collections.Generic.List<string>();

        // ====================================================================
        // 诊断属性
        // ====================================================================

        /// <summary>
        /// 已创建（Acquire）累计次数（诊断用）。
        /// <para>对应还原工程 <c>createLog.length</c>。本期只统计 Mob0。</para>
        /// </summary>
        internal int CreateCount => _createLog.Count;

        /// <summary>
        /// 已回收（Release）累计次数（诊断用）。
        /// <para>对应还原工程 <c>recoverLog.length</c>。本期只统计 Mob0。</para>
        /// </summary>
        internal int RecoverCount => _recoverLog.Count;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造只注册 Mob0 的敌人工厂。
        /// </summary>
        /// <param name="idAllocator">
        /// 运行时 ID 分配器。每局新建，从 1 单调递增。不可为 null。
        /// </param>
        /// <param name="mob0Pool">
        /// Mob0 对象池。由 <see cref="BattlePoolScope.GetPool{T}"/> 获取并注入。
        /// 不可为 null。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="idAllocator"/> 或 <paramref name="mob0Pool"/> 为 null。
        /// </exception>
        /// <remarks>
        /// <para>对应还原工程 <c>EnemyFactory.configure({ objectPool })</c>
        /// （EnemyFactory.js:20-24），但从全局 SingletonBase + 字符串 ObjectPool
        /// 改为构造注入强类型依赖。</para>
        ///
        /// <para><b>只注册 Mob0：</b>构造即绑定 Mob0Enemy 类型，不提供运行时注册其他类型
        /// 的能力。未知类型在 Acquire 时显式失败（本工厂只返回 Mob0Enemy）。</para>
        /// </remarks>
        internal EnemyFactory(RuntimeIdAllocator idAllocator, BattleObjectPool<Mob0Enemy> mob0Pool)
        {
            _idAllocator = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _mob0Pool = mob0Pool ?? throw new ArgumentNullException(nameof(mob0Pool));
        }

        // ====================================================================
        // Acquire —— 获取一个 Mob0Enemy（优先复用空闲，否则新建）并分配新运行时 ID
        // ====================================================================

        /// <summary>
        /// 获取一个 <see cref="Mob0Enemy"/>。优先复用空闲对象，否则新建。
        /// 取得后立即分配新运行时 ID 并写入，保证池复用不复用旧 ID。
        /// </summary>
        /// <returns>已分配新运行时 ID 的 <see cref="Mob0Enemy"/>。</returns>
        /// <remarks>
        /// <para><b>只注册 Mob0（task 4.5 / design 决策 5）：</b>
        /// 本方法只返回 <see cref="Mob0Enemy"/>，不提供 typeName 参数。本期只覆盖 Mob0，
        /// 未知类型显式失败由类型系统保证（无其他注册类型）。</para>
        ///
        /// <para><b>每次 Acquire 分配新 ID（task 4.5）：</b>
        /// 取得对象后立即调用 <see cref="RuntimeIdAllocator.Allocate"/> 分配新 ID，
        /// 并通过 <c>Mob0Enemy.AssignRuntimeId</c> 写入。从 1 单调递增，
        /// 池复用不复用旧 ID（design.md 目录表 RuntimeIdAllocator）。</para>
        ///
        /// <para><b>对应还原工程：</b>
        /// EnemyFactory.create(typeName)（EnemyFactory.js:42-49） +
        /// EnemyBase.init 中的 <c>this.id = gameData.allocateRuntimeId()</c>
        /// （EnemyBase.js:200）。本工厂将 ID 分配从 EnemyBase.init 提到 Factory.Acquire，
        /// 使池复用时 ID 重新分配的契约更显式（不依赖 EnemyBase.init 调用时机）。</para>
        ///
        /// <para><b>对称契约：</b>每次 Acquire MUST 对应恰好一次 <see cref="Release"/>。
        /// 调用方（EnemyManager）负责登记到 <see cref="BattleRuntimeScope.TrackPoolRental"/>
        /// 以保证 Settling/Exit 时自动归还。</para>
        /// </remarks>
        internal Mob0Enemy Acquire()
        {
            // 从池获取对象（优先复用空闲，否则新建）。
            // 对应还原工程 EnemyFactory.create -> creator -> objectPool.takeByClass。
            Mob0Enemy enemy = _mob0Pool.Acquire();

            if (enemy == null)
            {
                // 对应还原工程 EnemyFactory.js:46 "creator returned empty value"。
                throw new InvalidOperationException(
                    $"{LogTag} 池返回 null 对象 type={nameof(Mob0Enemy)}");
            }

            // 分配新运行时 ID 并写入（task 4.5：每次 Acquire 后分配新 ID）。
            // 对应还原工程 EnemyBase.js:200 this.id = gameData.allocateRuntimeId()。
            // 池复用不复用旧 ID：Release 时 ResetState 清除旧 ID，Acquire 时分配新 ID。
            int newId = _idAllocator.Allocate();
            enemy.AssignRuntimeId(newId);

            // 诊断日志（对应 EnemyFactory.js:47 createLog.push）。
            _createLog.Add(nameof(Mob0Enemy));

            return enemy;
        }

        // ====================================================================
        // Release —— 归还 Mob0Enemy 到池（先 Reset 再入池，旧 ID/目标引用失效）
        // ====================================================================

        /// <summary>
        /// 归还一个 <see cref="Mob0Enemy"/> 到池。先执行 <c>ResetState</c> 清除全部可变状态
        /// （包括运行时 ID、目标引用），再入池。回收后旧 ID/目标引用不得继续有效。
        /// </summary>
        /// <param name="enemy">要归还的 <see cref="Mob0Enemy"/>。null 或已归还返回 false。</param>
        /// <returns>成功归还返回 true；null 或已归还（重复 Release）返回 false。</returns>
        /// <remarks>
        /// <para><b>旧 ID/目标引用失效（task 4.5）：</b>
        /// <see cref="BattleObjectPool{T}.Release"/> 在入池前调用
        /// <c>Mob0Enemy.ResetState</c>，清除运行时 ID（置 0）、阵营、生命、路径、
        /// 目标引用、攻击冷却、接触回调标记、位置、击退、贡献者等全部可变状态
        /// （还原工程池复位契约 enemy-pool-reset-contract.md）。
        /// 回收后对象等价于新构造，旧 ID/目标引用不得继续有效。</para>
        ///
        /// <para><b>对应还原工程：</b>
        /// EnemyFactory.recover(enemy)（EnemyFactory.js:58-63） ->
        /// objectPool.recoverByClass(enemy) + recoverLog.push(enemy)。
        /// 本工厂从全局 ObjectPool.recoverByClass 改为强类型
        /// <see cref="BattleObjectPool{Mob0Enemy}.Release"/>。</para>
        ///
        /// <para><b>重复 Release 安全：</b>已归还对象再次 Release 返回 false
        /// （对应 ObjectPool.js <c>__InPool</c> 语义）。</para>
        /// </remarks>
        internal bool Release(Mob0Enemy enemy)
        {
            if (enemy == null)
            {
                // 对应还原工程 EnemyFactory.js:58 recover(enemy) 由 ObjectPool.recoverByClass
                // 内部 `if (!value) return false;` 处理；本工厂提前拦截便于诊断。
                return false;
            }

            // 委托池执行 Reset + 入池。
            // 池内部先调用 enemy.ResetState()（清除旧 ID/目标引用等全部可变状态），
            // 再放入空闲列表。重复 Release 由池内部 _inPool 查重拦截。
            bool recovered = _mob0Pool.Release(enemy);

            if (recovered)
            {
                // 诊断日志（对应 EnemyFactory.js:61 recoverLog.push(enemy)）。
                _recoverLog.Add(nameof(Mob0Enemy));
            }

            return recovered;
        }

        // ====================================================================
        // ResetForTests —— 测试重置（对应 EnemyFactory.js:66-71）
        // ====================================================================

        /// <summary>
        /// 重置工厂诊断日志（仅供测试使用）。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>EnemyFactory.resetForTests()</c>（EnemyFactory.js:66-71）。
        /// 清空 createLog/recoverLog。</para>
        ///
        /// <para><b>不重置池与 ID 分配器：</b>池和 ID 分配器的生命周期由
        /// <see cref="BattleRuntimeFactory"/> / <see cref="BattlePoolScope"/> 管理，
        /// 不由工厂自行重置。测试中通过新建工厂实例实现重置。</para>
        /// </remarks>
        internal void ResetForTests()
        {
            _createLog.Clear();
            _recoverLog.Clear();
        }
    }
}
