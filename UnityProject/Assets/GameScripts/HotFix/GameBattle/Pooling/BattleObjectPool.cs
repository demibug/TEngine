using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 4.1：BattleObjectPool —— 按明确类型池化纯逻辑对象
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Pooling/BattleObjectPool.cs）：
    //   按明确类型池化纯逻辑对象，替代全局反射式对象池（design 决策 5）。
    //   管理 Acquire/Release 对称租借、活动租借计数和完整 Reset 契约。
    //
    // 来源证据（还原工程 ObjectPool.js）：
    //   - takeByClass / recoverByClass：按 class 的逻辑对象池。
    //   - recoverByClass 防重复：`if (!value || value.__InPool) return false;`
    //   - 回收顺序：先 reset（若注册 resetter），再入池。
    //   - clear：清空 localClassPools，takeLog/recoverLog。
    //   ObjectPool.js 同时支持按 key 的表现对象池，但本类型只负责纯逻辑对象池；
    //   表现对象池由后续 Phase 6 的 View 侧独立管理（11_POOLING_AND_OWNERSHIP.md
    //   建议保持 Domain pool 与 View pool 独立）。
    //
    // 决策依据：
    //   - design.md 决策 5：删除 SingletonBase / CombatServices，池化改为按明确类型
    //     的强类型对象池，不使用反射或字符串键查找。
    //   - design.md 决策 3 / spec "Runtime quiescence and cleanup"：对象 MUST 先完成
    //     Reset 并归还池，之后才能在重开时复用池容量。
    //   - task 4.1 约束：
    //     * Acquire/Release 对称，活动租借计数。
    //     * 完整 Reset 契约（回收后无残留状态）。
    //     * 不预热、不硬编码容量。
    //     * 同次战斗会话内复用已清空的高水位容量。
    //     * 返回主界面或 Shutdown 时清空。
    //
    // 不变量：
    //   1. Acquire/Release 对称：每次 Acquire 必须对应恰好一次 Release。
    //   2. 活动租借计数：ActiveCount 等于已 Acquire 未 Release 的对象数。
    //   3. 完整 Reset：Release 前调用 ResetState，回收后无残留状态。
    //   4. 重复 Release 安全：已回收对象再次 Release 返回 false（对象级 _inPool 查重，
    //      对齐 ObjectPool.js __InPool 语义，在任意 ActiveCount 状态下均能拒绝）。
    //   5. 不预热：构造时空闲列表为空，不预先创建对象。
    //   6. 不硬编码容量：空闲列表无上限，高水位容量由实际使用自然形成。
    //   7. 同会话复用：ClearForNewBattle 只清空活动租借计数，保留空闲容量供下局复用。
    //   8. 返回主界面/Shutdown 清空：ClearAll 清空全部容量，释放全部对象。
    // ============================================================================

    /// <summary>
    /// 战斗对象池的非泛型接口，供 <see cref="BattlePoolScope"/> 统一管理多个泛型池。
    /// </summary>
    /// <remarks>
    /// <para><b>存在原因：</b><see cref="BattleObjectPool{T}"/> 是泛型类型，
    /// <see cref="BattlePoolScope"/> 作为非泛型容器需要统一接口调用
    /// ClearForNewBattle / ClearAll / ActiveCount。本接口避免使用 dynamic 或反射，
    /// 在 HybridCLR 热更环境下更安全（不依赖 Microsoft.CSharp 程序集）。</para>
    /// </remarks>
    internal interface IBattleObjectPool
    {
        /// <summary>
        /// 当前活动（已 Acquire 未 Release）租借对象数量。
        /// </summary>
        int ActiveCount { get; }

        /// <summary>
        /// 重开一局时调用：断言活动租借已全部归还，保留空闲容量供下局复用。
        /// </summary>
        /// <returns>活动租借为 0 返回 true；仍有未归还对象返回 false。</returns>
        bool ClearForNewBattle();

        /// <summary>
        /// 返回主界面或 Shutdown 时调用：清空全部空闲容量，重置活动计数。
        /// </summary>
        void ClearAll();
    }

    /// <summary>
    /// 按明确类型池化纯逻辑对象的强类型对象池。
    /// </summary>
    /// <typeparam name="T">池化对象类型，MUST 实现 <see cref="IPoolableBattleObject"/>。</typeparam>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>按明确类型池化纯逻辑对象，替代还原工程
    /// <c>ObjectPool.takeByClass / recoverByClass</c>（ObjectPool.js）。管理 Acquire/Release
    /// 对称租借、活动租借计数和完整 Reset 契约。</para>
    ///
    /// <para><b>工厂注入（对应 ObjectPool.js registerKey 的 create 参数）：</b>
    /// 构造时注入 <see cref="Func{T}"/> 工厂委托，由 Factory（EnemyFactory /
    /// ProjectileFactory 等）提供。不使用 <c>new()</c> 约束，避免要求池化类型具有
    /// public 无参构造，同时与还原工程 create 函数语义一致。</para>
    ///
    /// <para><b>Acquire/Release 对称（task 4.1）：</b>
    /// 每次 <see cref="Acquire"/> MUST 对应恰好一次 <see cref="Release"/>。
    /// <see cref="ActiveCount"/> 追踪未归还的租借对象数量，用于 Settling 静默清理断言
    /// （spec "断言没有活动 Timer、回调或租借对象"）。</para>
    ///
    /// <para><b>完整 Reset 契约（task 4.1 / 还原工程池复位契约）：</b>
    /// <see cref="Release"/> 在归还对象前调用 <see cref="IPoolableBattleObject.ResetState"/>，
    /// 清除全部可变状态。回收后对象等价于新构造，保证池复用无污染。</para>
    ///
    /// <para><b>不预热、不硬编码容量（task 4.1）：</b>
    /// 构造时空闲列表为空，不预先创建对象。空闲列表无上限，高水位容量由实际使用
    /// 自然形成（同次战斗会话内曾达到的最大活动对象数）。</para>
    ///
    /// <para><b>同会话复用与清空策略（task 4.1）：</b>
    /// <list type="bullet">
    /// <item><see cref="ClearForNewBattle"/>：重开一局时调用，只断言活动租借为 0，
    /// 保留空闲容量供下局复用。对应 spec "Restart creates clean per-battle state"
    /// 允许复用的池容量。</item>
    /// <item><see cref="ClearAll"/>：返回主界面或 Shutdown 时调用，清空全部容量。
    /// 对应 spec "Exit releases battle-owned state" 与 task 4.1 清空要求。</item>
    /// </list></para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 Manager/Factory 使用，
    /// 不对其他程序集暴露。Manager 是唯一注册/注销入口
    /// （11_POOLING_AND_OWNERSHIP.md：UI 和 View 不得直接销毁 Domain 对象）。</para>
    /// </remarks>
    internal sealed class BattleObjectPool<T> : IBattleObjectPool
        where T : class, IPoolableBattleObject
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>
        /// 日志标签前缀，便于在日志中筛选对象池相关条目。
        /// </summary>
        private const string LogTag = "[BattleObjectPool]";

        // ====================================================================
        // 空闲对象列表（栈式复用，LIFO）
        // ====================================================================

        /// <summary>
        /// 空闲对象列表。Acquire 优先从尾部取出（LIFO），Release 归还到尾部。
        /// <para>不预热：构造时为空。不硬编码容量：无上限，高水位由实际使用形成。</para>
        /// <para>使用 <see cref="List{T}"/> 而非 <c>Stack{T}</c> 以便诊断时按索引枚举。</para>
        /// </summary>
        private readonly List<T> _free = new List<T>();

        /// <summary>
        /// 当前在 <see cref="_free"/> 中的引用集合，用于对象级入池标记。
        /// <para>对应还原工程 ObjectPool.js 的 <c>value.__InPool</c> 语义（line 55/85）：
        /// 防止同一引用被重复 Release 并成功入池（池污染/重复入池）。</para>
        /// <para>为何不修改 <see cref="IPoolableBattleObject"/> 接口：
        /// 接口不应承载可变状态；由池内部用 HashSet 跟踪更内聚，
        /// 且与 JS 原版 <c>__InPool</c> 同为"对象当前是否在池中"的查重语义。</para>
        /// <para>Acquire 从 <see cref="_free"/> 取出时同步移除本集合中的标记；
        /// Release 入 <see cref="_free"/> 前先查本集合，若已存在则拒绝（返回 false）。</para>
        /// </summary>
        private readonly HashSet<T> _inPool = new HashSet<T>();

        // ====================================================================
        // 新对象工厂（对应 ObjectPool.js registerKey 的 create 参数）
        // ====================================================================

        /// <summary>
        /// 新对象创建工厂。Acquire 在空闲列表为空时调用本工厂创建新对象。
        /// <para>对应还原工程 <c>ObjectPool.registerKey(key, create)</c> 的 create 参数：
        /// Factory（EnemyFactory / ProjectileFactory 等）提供创建函数，池负责复用。</para>
        /// <para>不使用 <c>new()</c> 约束，避免要求池化类型具有 public 无参构造，
        /// 同时允许 Factory 注入带参数的构造逻辑。</para>
        /// </summary>
        private readonly Func<T> _createFactory;

        // ====================================================================
        // 活动租借计数
        // ====================================================================

        /// <summary>
        /// 活动（已 Acquire 未 Release）租借对象计数。
        /// <para>对应 task 4.1 活动租借计数要求。Settling 静默清理断言
        /// （spec "断言没有活动 Timer、回调或租借对象"）在全部归还后应为 0。</para>
        /// <para>类型为 int 而非 long：单局活动实体数量不会超过 int 范围。</para>
        /// </summary>
        private int _activeCount;

        // ====================================================================
        // 高水位标记（诊断用）
        // ====================================================================

        /// <summary>
        /// 本池曾达到的最大活动对象数量（高水位）。
        /// <para>诊断用：观察同次战斗会话内的池使用峰值。不硬编码容量，
        /// 高水位由实际使用自然形成（task 4.1）。</para>
        /// </summary>
        internal int HighWaterMark { get; private set; }

        // ====================================================================
        // 诊断属性
        // ====================================================================

        /// <summary>
        /// 当前活动（已 Acquire 未 Release）租借对象数量。
        /// <para>task 4.1 活动租借计数。Settling / Exit 断言应为 0。</para>
        /// </summary>
        public int ActiveCount => _activeCount;

        /// <summary>
        /// 当前空闲对象数量（可立即复用的容量）。
        /// <para>同次战斗会话内复用的已清空高水位容量（task 4.1）。</para>
        /// </summary>
        internal int FreeCount => _free.Count;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造一个按明确类型池化纯逻辑对象的对象池。
        /// </summary>
        /// <param name="createFactory">
        /// 新对象创建工厂。Acquire 在空闲列表为空时调用本工厂创建新对象。
        /// 对应还原工程 <c>ObjectPool.registerKey(key, create)</c> 的 create 参数。
        /// 不可为 null。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="createFactory"/> 为 null。
        /// </exception>
        /// <remarks>
        /// <para>构造时空闲列表为空，不预热（task 4.1）。首次 Acquire 时调用工厂创建
        /// 第一个对象，Release 后后续 Acquire 优先复用空闲对象。</para>
        /// <para>Factory（EnemyFactory / ProjectileFactory 等）提供创建函数，
        /// 池负责复用与 Reset。Manager 是唯一注册/注销入口。</para>
        /// </remarks>
        internal BattleObjectPool(Func<T> createFactory)
        {
            _createFactory = createFactory ?? throw new ArgumentNullException(nameof(createFactory));
        }

        // ====================================================================
        // Acquire —— 获取一个对象（优先复用空闲，否则新建）
        // ====================================================================

        /// <summary>
        /// 获取一个池化对象。优先复用空闲对象，否则新建。
        /// </summary>
        /// <returns>已 Acquire 的对象，MUST 由调用方在不再使用时 <see cref="Release"/> 归还。</returns>
        /// <remarks>
        /// <para><b>对称契约（task 4.1）：</b>每次 Acquire MUST 对应恰好一次 Release。
        /// 调用方（Manager/Factory）负责登记到 <see cref="BattleRuntimeScope.TrackPoolRental"/>
        /// 以保证 Settling/Exit 时自动归还。</para>
        ///
        /// <para><b>不预热（task 4.1）：</b>首次 Acquire 必定新建（空闲列表为空）。
        /// 后续 Acquire 优先复用已 Release 归还的空闲对象。</para>
        ///
        /// <para><b>复用无污染保证：</b>空闲对象在 Release 时已执行 ResetState，
        /// Acquire 取得的对象状态等价于新构造。调用方 Acquire 后需重新初始化
        /// （如分配新运行时 ID、设置阵营、路径等）。</para>
        ///
        /// <para><b>活动计数：</b>Acquire 后 <see cref="ActiveCount"/> 递增，
        /// Release 后递减。HighWaterMark 记录峰值。</para>
        /// </remarks>
        internal T Acquire()
        {
            T obj;
            if (_free.Count > 0)
            {
                // LIFO：从尾部取出，O(1)。
                int last = _free.Count - 1;
                obj = _free[last];
                _free.RemoveAt(last);
                // 同步移除对象级入池标记（对应 ObjectPool.js takeByClass line 77
                // `value.__InPool = false;` 的语义）。
                _inPool.Remove(obj);
            }
            else
            {
                // 空闲列表为空，调用工厂创建新对象。不预热，不硬编码容量。
                // 对应 ObjectPool.js registerKey 的 create 参数。
                obj = _createFactory();
            }

            ++_activeCount;
            if (_activeCount > HighWaterMark)
            {
                HighWaterMark = _activeCount;
            }

            return obj;
        }

        // ====================================================================
        // Release —— 归还对象到池（先 Reset 再入池）
        // ====================================================================

        /// <summary>
        /// 归还一个已 Acquire 的对象到池。先执行 <see cref="IPoolableBattleObject.ResetState"/>，
        /// 再放入空闲列表。
        /// </summary>
        /// <param name="obj">要归还的对象。null 或已归还的对象返回 false。</param>
        /// <returns>成功归还返回 true；null 或已归还（重复 Release）返回 false。</returns>
        /// <remarks>
        /// <para><b>对称契约（task 4.1）：</b>每次 Acquire MUST 对应恰好一次 Release。
        /// 重复 Release 返回 false，不重复入池（对应 ObjectPool.js
        /// <c>if (!value || value.__InPool) return false;</c>）。</para>
        ///
        /// <para><b>完整 Reset 契约（task 4.1 / 还原工程池复位契约）：</b>
        /// Release 前调用 <see cref="IPoolableBattleObject.ResetState"/>，清除全部可变状态。
        /// 回收后对象等价于新构造，保证下次 Acquire 复用无污染。</para>
        ///
        /// <para><b>Reset 异常不阻断：</b>若 ResetState 抛出异常，记录日志但仍入池，
        /// 保证池回收不因单个对象清理失败而中断。调用方应检查日志诊断污染。</para>
        ///
        /// <para><b>活动计数：</b>成功归还后 <see cref="ActiveCount"/> 递减。</para>
        /// </remarks>
        internal bool Release(T obj)
        {
            if (obj == null)
            {
                // null 对象不归还（对应 ObjectPool.js `if (!value) return false;`）。
                return false;
            }

            // 对象级入池标记查重（对应 ObjectPool.js line 55/85
            // `if (!value || value.__InPool) return false;` 语义）。
            // 防止多租约场景下已归还对象被重复 Release 并成功入池（池污染）：
            //   复现（修复前）：Acquire A, Acquire B（ActiveCount=2）
            //     → Release A（A 入 _free）→ Release A 再次
            //     → 旧守卫 ActiveCount=1>0 通过 → A 再次入 _free → A 在 _free 中出现两次。
            // 本查重在任意 ActiveCount 状态下都能拒绝已归还对象。
            if (_inPool.Contains(obj))
            {
                Log.Warning(
                    $"{LogTag} Release 对象已在池中（重复归还），忽略 type={typeof(T).Name}");
                return false;
            }

            if (_activeCount <= 0)
            {
                // 活动计数为 0 说明没有已 Acquire 的对象可归还。
                // 可能是未 Acquire 直接 Release（孤儿对象），记录警告不入池。
                // 注：此处不再承担"重复 Release"主防线（已由上方 _inPool 查重承担），
                // 但仍保留以拒绝从未经本池 Acquire 的对象。
                Log.Warning($"{LogTag} Release 时 ActiveCount=0，疑似孤儿对象，忽略 type={typeof(T).Name}");
                return false;
            }

            // 执行 Reset：清除全部可变状态，使对象等价于新构造。
            // ResetState 实现约定不抛出，但防御性捕获保证池回收不中断。
            try
            {
                obj.ResetState();
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} Release 时 ResetState 异常 type={typeof(T).Name}: {ex}");
            }

            // 归还到空闲列表尾部（LIFO 复用），并打上对象级入池标记。
            _inPool.Add(obj);
            _free.Add(obj);
            --_activeCount;
            return true;
        }

        // ====================================================================
        // ClearForNewBattle —— 重开一局：保留空闲容量，断言活动租借为 0
        // ====================================================================

        /// <summary>
        /// 重开一局时调用：断言活动租借已全部归还，保留空闲容量供下局复用。
        /// </summary>
        /// <returns>活动租借为 0 返回 true；仍有未归还对象返回 false。</returns>
        /// <remarks>
        /// <para><b>同会话复用（task 4.1）：</b>同次战斗会话内复用已清空的高水位容量。
        /// 重开一局不清空空闲列表，只断言上一局的活动对象已全部 Reset 并归还。</para>
        ///
        /// <para><b>断言依据（spec "Runtime quiescence and cleanup"）：</b>
        /// 对象 MUST 先完成 Reset 并归还池，之后才能在重开时复用池容量。
        /// 若 ActiveCount != 0 说明 Settling 静默清理未完成，不应进入下局。</para>
        ///
        /// <para><b>不清空空闲列表：</b>空闲列表中的对象已通过 ResetState 清空状态，
        /// 可安全复用。清空空闲列表会丢失高水位容量，违反 task 4.1 复用要求。</para>
        /// </remarks>
        public bool ClearForNewBattle()
        {
            if (_activeCount != 0)
            {
                Log.Error(
                    $"{LogTag} ClearForNewBattle 时仍有 {_activeCount} 个活动租借未归还 " +
                    $"type={typeof(T).Name}，Settling 静默清理可能未完成");
                return false;
            }
            // 保留 _free 不变：同会话复用已清空的高水位容量。
            return true;
        }

        // ====================================================================
        // ClearAll —— 返回主界面 / Shutdown：清空全部容量
        // ====================================================================

        /// <summary>
        /// 返回主界面或 Shutdown 时调用：清空全部空闲容量，重置活动计数。
        /// </summary>
        /// <remarks>
        /// <para><b>清空时机（task 4.1）：</b>返回主界面或 Shutdown 时清空。
        /// 对应 spec "Exit releases battle-owned state" 与 design 决策 3
        /// "返回主界面必须进一步清空池容量"。</para>
        ///
        /// <para><b>活动租借处理：</b>若仍有活动租借（如 Exit 从 Running 状态直接退出），
        /// 记录错误但仍清空，防止泄漏。正常流程应先 Settling 静默清理归还全部活动对象。</para>
        ///
        /// <para><b>不清空对象内部状态：</b>ClearAll 后对象不再使用，无需 ResetState。
        /// 仅清空列表引用，让 GC 回收。HighWaterMark 不重置（诊断保留会话峰值）。</para>
        /// </remarks>
        public void ClearAll()
        {
            if (_activeCount != 0)
            {
                Log.Error(
                    $"{LogTag} ClearAll 时仍有 {_activeCount} 个活动租借未归还 " +
                    $"type={typeof(T).Name}，可能存在资源泄漏");
            }

            _free.Clear();
            _inPool.Clear();
            _activeCount = 0;
            // HighWaterMark 保留：记录本次会话峰值，供退出后诊断。
        }
    }
}
