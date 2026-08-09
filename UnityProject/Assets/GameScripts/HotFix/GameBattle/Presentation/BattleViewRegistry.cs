using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.4：BattleViewRegistry —— 运行时 ID 到表现对象的映射
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 Presentation/BattleViewRegistry.cs）：
    //   维护运行时 ID 到 Unity/FairyGUI 表现对象的映射。
    //
    //   本类型是表现层内部的 ID→对象注册表。逻辑层通过 IBattleViewPort.OnEnemySpawned
    //   等方法通知"某 runtimeId 的实体已生成"，表现层（BattlePresenter）据此创建
    //   表现对象并登记到本注册表；后续 BattleViewSynchronizer 按 runtimeId 查找
    //   表现对象做插值/同步；实体移除时从注册表注销。
    //
    // 框架解耦（design.md:9 "逻辑层不依赖 MonoBehaviour、FairyGUI、Scene 对象"）：
    //   GameBattle asmdef 不引用 UnityEngine 或 FairyGUI 程序集（task 7.5 待 FairyGUI
    //   Change 冻结公共注册契约后接入）。本注册表使用 object 桶位存储表现对象引用，
    //   使其与具体表现框架解耦：
    //   - Unity 真实实现：object 桶位存 GameObject/MonoBehaviour 引用。
    //   - FairyGUI 真实实现：object 桶位存 GComponent/GObject 引用。
    //   - Null/Test 实现：不使用本注册表（NullBattleViewPort 不创建表现对象）。
    //   表现对象的具体类型由真实实现决定，本注册表只负责 ID→引用映射。
    //
    // 不变量：
    //   1. 只维护 ID→表现对象映射，不持有逻辑实体引用（不引用 Enemy/Unit/Projectile）。
    //   2. 不回写规则状态（design.md:217 "BattlePresenter 把只读状态/事实翻译成视图操作，
    //      不回写规则状态"；本注册表作为 Presenter 的协作者，同样不回写）。
    //   3. 按 EntityCategory 分桶，避免不同类别 ID 碰撞（Enemy/Unit/Projectile 的
    //      RuntimeId 由同一个 RuntimeIdAllocator 分配，全局唯一，但分桶便于按类别
    //      批量清理与诊断）。
    //   4. Clear 幂等，随 BattleRuntimeScope / Presenter 生命周期释放。
    // ============================================================================

    /// <summary>
    /// 表现对象类别枚举，用于 BattleViewRegistry 分桶存储。
    /// </summary>
    /// <remarks>
    /// 对应逻辑层三类需要表现对象的实体：敌人、单位、投射物。
    /// RuntimeId 由 RuntimeIdAllocator 全局递增分配，三类别 ID 不会冲突，
    /// 分桶只为便于按类别批量清理与诊断。
    /// </remarks>
    internal enum ViewObjectCategory
    {
        /// <summary>敌人表现对象（对应逻辑层 EnemyBase/RuntimeId）。</summary>
        Enemy = 0,

        /// <summary>单位表现对象（对应逻辑层 SoldierBase/RuntimeId）。</summary>
        Unit = 1,

        /// <summary>投射物表现对象（对应逻辑层 ProjectileBase/RuntimeId）。</summary>
        Projectile = 2,
    }

    /// <summary>
    /// 运行时 ID 到 Unity/FairyGUI 表现对象的映射注册表。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md:218）：</b>维护运行时 ID 到表现对象的映射，
    /// 供 BattlePresenter/BattleViewSynchronizer 按 ID 查找表现对象做视图操作。</para>
    ///
    /// <para><b>框架解耦（design.md:9）：</b>使用 object 桶位存储表现对象引用，
    /// 与具体表现框架（UnityEngine GameObject / FairyGUI GComponent）解耦。
    /// 真实实现由 task 7.5/7.6 接入后把具体表现对象注册到本表。</para>
    ///
    /// <para><b>不回写规则状态（design.md:217）：</b>本注册表只维护 ID→引用映射，
    /// 不持有逻辑实体引用，不回写 BattleState 或任何 Manager 状态。</para>
    ///
    /// <para><b>生命周期：</b>随 BattlePresenter 创建，随 BattleRuntimeScope 释放。
    /// Clear 幂等。</para>
    ///
    /// <para><b>线程安全：</b>不要求。所有调用在 Unity 主线程执行。</para>
    /// </remarks>
    internal sealed class BattleViewRegistry
    {
        // ====================================================================
        // 分桶存储
        // --------------------------------------------------------------------
        // 每个类别一个 Dictionary<int, object>，key 为 RuntimeId，value 为表现对象引用。
        // 使用 object 桶位使本类型与具体表现框架解耦（design.md:9）。
        // 表现对象的真实类型由 Presenter / 真实 ViewPort 实现决定。
        // ====================================================================

        /// <summary>敌人表现对象桶位：RuntimeId → 表现对象引用。</summary>
        private readonly Dictionary<int, object> _enemyObjects = new Dictionary<int, object>();

        /// <summary>单位表现对象桶位：RuntimeId → 表现对象引用。</summary>
        private readonly Dictionary<int, object> _unitObjects = new Dictionary<int, object>();

        /// <summary>投射物表现对象桶位：RuntimeId → 表现对象引用。</summary>
        private readonly Dictionary<int, object> _projectileObjects = new Dictionary<int, object>();

        /// <summary>
        /// 是否已清空全部映射。Clear 后置位，重复 Clear 幂等。
        /// </summary>
        public bool IsCleared { get; private set; }

        // ====================================================================
        // 登记 / 注销 / 查询
        // ====================================================================

        /// <summary>
        /// 登记一个表现对象到指定类别的 runtimeId 槽位。
        /// </summary>
        /// <param name="category">表现对象类别。</param>
        /// <param name="runtimeId">逻辑层运行时 ID（由 RuntimeIdAllocator 分配）。</param>
        /// <param name="viewObject">表现对象引用（GameObject/GComponent 等；不可为 null）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="viewObject"/> 为 null。</exception>
        /// <remarks>
        /// <para>由 BattlePresenter 在收到 IBattleViewPort.OnEnemySpawned/OnUnitPlaced/
        /// OnProjectileFired 等事实后调用，把创建的表现对象登记到本表。</para>
        /// <para>重复登记同一 runtimeId：覆盖旧引用（防御性，正常流程不应重复登记）。</para>
        /// </remarks>
        internal void Register(ViewObjectCategory category, int runtimeId, object viewObject)
        {
            if (viewObject == null)
            {
                throw new ArgumentNullException(nameof(viewObject));
            }

            Dictionary<int, object> bucket = GetBucket(category);
            bucket[runtimeId] = viewObject;
        }

        /// <summary>
        /// 注销指定类别的 runtimeId 对应的表现对象。
        /// </summary>
        /// <param name="category">表现对象类别。</param>
        /// <param name="runtimeId">逻辑层运行时 ID。</param>
        /// <returns>被移除的表现对象引用；不存在返回 null。</returns>
        /// <remarks>
        /// <para>由 BattlePresenter 在收到 IBattleViewPort.OnEnemyRemoved/OnUnitRemoved/
        /// OnProjectileRemoved 等事实后调用，从本表移除并返回表现对象引用，
        /// 供调用方销毁/归还池。</para>
        /// <para>幂等：注销不存在的 ID 返回 null，不抛异常。</para>
        /// </remarks>
        internal object Unregister(ViewObjectCategory category, int runtimeId)
        {
            Dictionary<int, object> bucket = GetBucket(category);
            if (bucket.TryGetValue(runtimeId, out object viewObject))
            {
                bucket.Remove(runtimeId);
                return viewObject;
            }

            return null;
        }

        /// <summary>
        /// 查询指定类别的 runtimeId 对应的表现对象。
        /// </summary>
        /// <param name="category">表现对象类别。</param>
        /// <param name="runtimeId">逻辑层运行时 ID。</param>
        /// <returns>表现对象引用；不存在返回 null。</returns>
        /// <remarks>
        /// <para>由 BattleViewSynchronizer 在 Unity 帧中按 runtimeId 查找表现对象
        /// 做插值/同步（如更新敌人 GameObject 位置）。</para>
        /// </remarks>
        internal object Find(ViewObjectCategory category, int runtimeId)
        {
            Dictionary<int, object> bucket = GetBucket(category);
            return bucket.TryGetValue(runtimeId, out object viewObject) ? viewObject : null;
        }

        /// <summary>
        /// 尝试查询指定类别的 runtimeId 对应的表现对象。
        /// </summary>
        /// <param name="category">表现对象类别。</param>
        /// <param name="runtimeId">逻辑层运行时 ID。</param>
        /// <param name="viewObject">查到的表现对象引用；不存在为 null。</param>
        /// <returns>找到返回 true；否则 false。</returns>
        internal bool TryFind(ViewObjectCategory category, int runtimeId, out object viewObject)
        {
            Dictionary<int, object> bucket = GetBucket(category);
            return bucket.TryGetValue(runtimeId, out viewObject);
        }

        /// <summary>
        /// 获取指定类别当前登记的表现对象数量（诊断用）。
        /// </summary>
        internal int GetCount(ViewObjectCategory category)
        {
            return GetBucket(category).Count;
        }

        /// <summary>
        /// 将指定类别的当前映射复制到调用方复用的缓冲区。
        /// </summary>
        /// <remarks>
        /// 同步器在 Unity 帧中使用该方法取得稳定快照，随后才调用表现对象更新。
        /// 复制键值对不会创建托管对象；调用方复用缓冲区即可避免每帧 GC。
        /// </remarks>
        internal void CopyEntries(
            ViewObjectCategory category,
            List<KeyValuePair<int, object>> buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.Clear();
            Dictionary<int, object> bucket = GetBucket(category);
            foreach (KeyValuePair<int, object> entry in bucket)
            {
                buffer.Add(entry);
            }
        }

        // ====================================================================
        // 批量清理
        // ====================================================================

        /// <summary>
        /// 清空全部类别的表现对象映射。幂等。
        /// </summary>
        /// <remarks>
        /// <para>由 BattlePresenter 在 Dispose / StopPresentation 时调用，
        /// 或由 BattleRuntimeScope 经 TrackDisposable 登记后在 Settling/Dispose 时调用。</para>
        /// <para>本方法只清空映射，不销毁表现对象本身——表现对象的销毁/归还池由
        /// 真实 ViewPort 实现负责（如 UnityBattleViewPort.Clear 销毁 GameObject）。
        /// 本注册表只负责 ID→引用映射的生命周期。</para>
        /// </remarks>
        internal void Clear()
        {
            if (IsCleared)
            {
                return;
            }

            IsCleared = true;
            _enemyObjects.Clear();
            _unitObjects.Clear();
            _projectileObjects.Clear();
        }

        // ====================================================================
        // 辅助
        // ====================================================================

        /// <summary>
        /// 按类别获取对应的桶位字典。
        /// </summary>
        private Dictionary<int, object> GetBucket(ViewObjectCategory category)
        {
            switch (category)
            {
                case ViewObjectCategory.Enemy:
                    return _enemyObjects;
                case ViewObjectCategory.Unit:
                    return _unitObjects;
                case ViewObjectCategory.Projectile:
                    return _projectileObjects;
                default:
                    // 防御性：未知类别回退到敌人桶，记录错误。
                    Log.Error($"[BattleViewRegistry] 未知 ViewObjectCategory={category}，回退到 Enemy 桶");
                    return _enemyObjects;
            }
        }
    }
}
