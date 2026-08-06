using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 4.1：BattlePoolScope —— 区分可跨局复用的池容量与必须逐局清空的活动对象
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Pooling/BattlePoolScope.cs）：
    //   管理多个 BattleObjectPool 的生命周期作用域，区分：
    //   1. 可跨局复用的池容量（空闲列表中的已清空对象）—— 重开时保留。
    //   2. 必须逐局清空的活动对象（活动租借）—— Settling 静默清理时全部归还。
    //
    // 来源证据（还原工程池策略）：
    //   - ObjectPool.js clear()：清空 localKeyPools / localClassPools，
    //     但还原工程没有显式区分"同会话复用"与"返回主界面清空"。
    //   - 11_POOLING_AND_OWNERSHIP.md：Manager 是唯一注册/注销入口；
    //     Domain object pool 与 View pool 独立。
    //   - enemy-pool-reset-contract.md：回收顺序固定，已验证不泄漏。
    //   本类型在还原工程基础上显式区分两种清空语义，对应 task 4.1 约束。
    //
    // 决策依据：
    //   - design.md 决策 3 / spec "Runtime quiescence and cleanup"：
    //     对象 MUST 先完成 Reset 并归还池，之后才能在重开时复用池容量；
    //     返回主界面或 Shutdown 时 SHALL 在销毁 Runtime 后进一步清空池容量。
    //   - design.md 决策 3：BattleModule 跨局持有已清空的池容量；
    //     BattleRuntime 独占活动实体。
    //   - task 4.1 约束：
    //     * 不预热、不硬编码容量。
    //     * 只在同次战斗会话内复用已清空的高水位容量。
    //     * 返回主界面或 Shutdown 时清空。
    //
    // 所有权（design.md 决策 3）：
    //   BattlePoolScope 由 BattleModule 跨局持有（同 Scene/UI 宿主、资源句柄一起）。
    //   - 重开一局：BattleRuntime 在 Settling 静默清理时归还全部活动租借，
    //     BattleModule 调用 ClearForNewBattle 保留空闲容量，再新建 Runtime。
    //   - 返回主界面：BattleModule 调用 ClearAll 清空全部池容量，再释放宿主资源。
    //   - Shutdown：BattleModule 调用 ClearAll 清空全部池容量。
    //
    // 注：本类型当前为骨架实现，只提供注册/获取池、ClearForNewBattle/ClearAll 语义。
    //   后续 Phase 3 的 EnemyFactory / Phase 4 的 ProjectileFactory / Phase 5 的
    //   UnitFactory 接入时通过 GetPool<T>() 获取具体池实例。
    // ============================================================================

    /// <summary>
    /// 战斗对象池作用域：管理多个 <see cref="BattleObjectPool{T}"/>，区分可跨局复用
    /// 的池容量与必须逐局清空的活动对象。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表）：</b>区分可跨局复用的池容量与必须逐局清空
    /// 的活动对象。管理多个按类型的 <see cref="BattleObjectPool{T}"/>，提供统一的
    /// 重开保留与退出清空入口。</para>
    ///
    /// <para><b>所有权（design.md 决策 3）：</b>由 <c>BattleModule</c> 跨局持有，
    /// 同 Scene/UI 宿主、资源句柄一起。BattleRuntime 独占活动实体，通过
    /// <c>BattleRuntimeScope.TrackPoolRental</c> 登记每个 Acquire 的归还动作；
    /// Settling 静默清理时全部归还后，BattleModule 调用
    /// <see cref="ClearForNewBattle"/> 保留空闲容量。</para>
    ///
    /// <para><b>同会话复用（task 4.1）：</b>
    /// <list type="bullet">
    /// <item><see cref="ClearForNewBattle"/>：重开一局时调用，断言全部池的活动租借为 0，
    /// 保留空闲容量供下局复用。对应 spec "Restart creates clean per-battle state"
    /// 允许复用的池容量。</item>
    /// <item><see cref="ClearAll"/>：返回主界面或 Shutdown 时调用，清空全部池的全部容量。
    /// 对应 spec "Exit releases battle-owned state" 与 task 4.1 清空要求。</item>
    /// </list></para>
    ///
    /// <para><b>不预热、不硬编码容量（task 4.1）：</b>池在首次 Acquire 时才创建对象，
    /// 空闲列表无上限，高水位容量由实际使用自然形成。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 BattleModule / Factory 使用，
    /// 不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class BattlePoolScope
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>
        /// 日志标签前缀，便于在日志中筛选池作用域相关条目。
        /// </summary>
        private const string LogTag = "[BattlePoolScope]";

        // ====================================================================
        // 按类型注册的池实例
        // ====================================================================

        /// <summary>
        /// 按对象类型注册的池实例映射。
        /// <para>每个类型最多一个池实例。首次 <see cref="GetPool{T}"/> 时惰性创建。</para>
        /// <para>使用 <see cref="Dictionary{TKey, TValue}"/> 按 <c>typeof(T)</c> 查找；
        /// 池实例在会话内稳定，不因遍历顺序影响行为。</para>
        /// <para>值为 <see cref="IBattleObjectPool"/>（非泛型接口），便于统一调用
        /// ClearForNewBattle / ClearAll / ActiveCount，避免 dynamic 或反射
        /// （HybridCLR 热更环境下不依赖 Microsoft.CSharp 程序集）。</para>
        /// </summary>
        private readonly Dictionary<Type, IBattleObjectPool> _pools =
            new Dictionary<Type, IBattleObjectPool>();

        // ====================================================================
        // GetPool —— 获取指定类型的池（惰性创建）
        // ====================================================================

        /// <summary>
        /// 获取指定类型的 <see cref="BattleObjectPool{T}"/>。若不存在则惰性创建。
        /// </summary>
        /// <typeparam name="T">池化对象类型，MUST 实现 <see cref="IPoolableBattleObject"/>。</typeparam>
        /// <param name="createFactory">
        /// 新对象创建工厂。仅在该类型首次注册时使用，后续调用返回已缓存的池实例。
        /// 对应还原工程 <c>ObjectPool.registerKey(key, create)</c> 的 create 参数。
        /// 不可为 null。
        /// </param>
        /// <returns>该类型的唯一池实例。</returns>
        /// <remarks>
        /// <para><b>惰性创建：</b>首次调用时创建池实例，后续返回同一实例。不预热，
        /// 池在首次 <see cref="BattleObjectPool{T}.Acquire"/> 时才创建对象（task 4.1）。</para>
        ///
        /// <para><b>类型唯一：</b>每个类型最多一个池实例。Factory 通过本方法获取池，
        /// 不自行创建池实例，保证 Manager 是唯一注册/注销入口
        /// （11_POOLING_AND_OWNERSHIP.md）。</para>
        /// </remarks>
        internal BattleObjectPool<T> GetPool<T>(Func<T> createFactory)
            where T : class, IPoolableBattleObject
        {
            Type key = typeof(T);
            if (!_pools.TryGetValue(key, out IBattleObjectPool raw))
            {
                var pool = new BattleObjectPool<T>(createFactory);
                _pools[key] = pool;
                return pool;
            }
            return (BattleObjectPool<T>)raw;
        }

        // ====================================================================
        // ClearForNewBattle —— 重开一局：保留空闲容量，断言活动租借为 0
        // ====================================================================

        /// <summary>
        /// 重开一局时调用：断言全部池的活动租借已归还，保留空闲容量供下局复用。
        /// </summary>
        /// <returns>全部池的活动租借为 0 返回 true；任一池仍有未归还对象返回 false。</returns>
        /// <remarks>
        /// <para><b>同会话复用（task 4.1）：</b>同次战斗会话内复用已清空的高水位容量。
        /// 不清空空闲列表，只断言上一局的活动对象已全部 Reset 并归还。</para>
        ///
        /// <para><b>断言依据（spec "Runtime quiescence and cleanup"）：</b>
        /// 对象 MUST 先完成 Reset 并归还池，之后才能在重开时复用池容量。
        /// 若任一池 ActiveCount != 0 说明 Settling 静默清理未完成，不应进入下局。</para>
        ///
        /// <para><b>调用方（BattleModule）：</b>在重开流程中，旧 Runtime 的 Settling
        /// 静默清理完成（全部活动对象归还池）后调用本方法，再新建 Runtime。</para>
        /// </remarks>
        internal bool ClearForNewBattle()
        {
            bool allClean = true;
            foreach (var pair in _pools)
            {
                if (!pair.Value.ClearForNewBattle())
                {
                    allClean = false;
                }
            }
            if (!allClean)
            {
                Log.Error($"{LogTag} ClearForNewBattle 发现仍有活动租借未归还，Settling 静默清理可能未完成");
            }
            return allClean;
        }

        // ====================================================================
        // ClearAll —— 返回主界面 / Shutdown：清空全部池的全部容量
        // ====================================================================

        /// <summary>
        /// 返回主界面或 Shutdown 时调用：清空全部池的全部空闲容量，重置活动计数。
        /// </summary>
        /// <remarks>
        /// <para><b>清空时机（task 4.1）：</b>返回主界面或 Shutdown 时清空。
        /// 对应 spec "Exit releases battle-owned state" 与 design 决策 3
        /// "返回主界面必须进一步清空池容量"。</para>
        ///
        /// <para><b>调用方（BattleModule）：</b>在退出流程中，Runtime 销毁后调用本方法，
        /// 再释放 Scene/UI/资源句柄。</para>
        ///
        /// <para><b>活动租借处理：</b>若仍有活动租借，记录错误但仍清空。正常流程应先
        /// Settling 静默清理归还全部活动对象。</para>
        /// </remarks>
        internal void ClearAll()
        {
            foreach (var pair in _pools)
            {
                pair.Value.ClearAll();
            }
        }

        // ====================================================================
        // 诊断 API
        // ====================================================================

        /// <summary>
        /// 断言全部池的活动租借为 0。
        /// </summary>
        /// <returns>全部池 ActiveCount=0 返回 true；任一池仍有活动租借返回 false。</returns>
        /// <remarks>
        /// <para>供 Settling 静默清理断言使用（spec "断言没有活动 Timer、回调或租借对象"）。
        /// BattleRuntime.EnterSettling 在各 Manager 清理后调用本方法验证。</para>
        /// </remarks>
        internal bool AssertAllActiveReleased()
        {
            foreach (var pair in _pools)
            {
                if (pair.Value.ActiveCount != 0)
                {
                    Log.Error(
                        $"{LogTag} 池 {pair.Key.Name} 仍有 {pair.Value.ActiveCount} 个活动租借未归还");
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 已注册的池类型数量（诊断用）。
        /// </summary>
        internal int PoolCount => _pools.Count;
    }
}
