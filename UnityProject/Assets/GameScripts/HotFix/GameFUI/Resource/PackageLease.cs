using System.Threading;

namespace GameFUI
{
    /// <summary>
    /// 包租约，代表一个存活窗口、缓存窗口、正在创建的对象或上层依赖对包的引用所有权。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// - spec fairygui-package-loading“包租约控制缓存和卸载”：每个存活或缓存窗口、
    ///   正在创建的对象以及上层依赖 SHALL 持有相应包租约；只有不存在存活或缓存对象、
    ///   创建任务、上层依赖和待完成资源操作时，包才可在延迟窗口结束后卸载。
    /// - spec“资源所有权唯一且释放有序”——重复释放租约 SHALL 被拒绝，不得使引用计数变为负数。
    /// - design.md 决策8：引用计数由 lease 持有时递增、释放时递减。
    /// - design.md 决策9：最终释放顺序固定为停止新访问、Dispose 包对象、RemovePackage、
    ///   Dispose 本包 handles、释放依赖 leases。
    ///
    /// 幂等语义：<see cref="Release"/> 只能成功一次。首次成功时递减
    /// <see cref="PackageRecord"/> 引用计数并标记 <see cref="IsReleased"/>；
    /// 再次调用 <see cref="Release"/> 直接返回 false，不再次递减，保证引用计数不为负。
    ///
    /// 线程安全：<see cref="IsReleased"/> 标志与引用计数递减通过
    /// <see cref="Interlocked.CompareExchange"/> 原子完成，保证多线程重复释放只有一个成功。
    ///
    /// 边界约束：本类型不 using 且不反向依赖 GameLogic/GamePlay/GameBattle 命名空间。
    /// </remarks>
    public sealed class PackageLease
    {
        /// <summary>
        /// 本租约关联的包记录，用于递减引用计数。
        /// </summary>
        private readonly PackageRecord _record;

        /// <summary>
        /// 释放状态：0 表示未释放，1 表示已释放。使用 <see cref="Interlocked"/> 原子操作。
        /// </summary>
        private int _released;

        /// <summary>
        /// 构造包租约，并立即递增关联 <see cref="PackageRecord"/> 的引用计数。
        /// </summary>
        /// <param name="record">关联的包记录，不得为 null。</param>
        /// <remarks>
        /// Acquire 成功后由 PackageManager 创建租约并交付调用方；调用方在不再持有包时调用
        /// <see cref="Release"/> 归还所有权。依赖租约同样由本类型表示，存放在
        /// <see cref="PackageRecord.DependencyLeases"/> 中。
        /// </remarks>
        public PackageLease(PackageRecord record)
        {
            _record = record ?? throw new System.ArgumentNullException(nameof(record));
            _released = 0;
            // 持有租约即递增引用计数（design.md 决策8）。
            _record.AddReference();
        }

        /// <summary>
        /// 获取租约关联的包记录。
        /// </summary>
        public PackageRecord Record => _record;

        /// <summary>
        /// 获取包名（便捷访问，等价于 <c>Record.PackageName</c>）。
        /// </summary>
        public string PackageName => _record.PackageName;

        /// <summary>
        /// 是否已释放。释放后再次调用 <see cref="Release"/> 将被拒绝。
        /// </summary>
        public bool IsReleased => Volatile.Read(ref _released) != 0;

        /// <summary>
        /// 释放租约，递减 <see cref="PackageRecord"/> 引用计数。
        /// </summary>
        /// <returns>首次释放返回 true；重复释放返回 false，不改变引用计数。</returns>
        /// <remarks>
        /// 幂等保护：使用 <see cref="Interlocked.CompareExchange"/> 将 <see cref="_released"/>
        /// 从 0 原子置为 1，只有一个调用方能进入递减路径。重复释放直接返回 false，
        /// 满足 spec“重复释放租约 SHALL 被拒绝，不得使引用计数变为负数”。
        ///
        /// 防负数保护：引用计数递减委托给 <see cref="PackageRecord.TryReleaseReference"/>,
        /// 该方法在引用计数已为零时拒绝递减。正常路径下构造时已递增，此处不会触发拒绝；
        /// 该委托作为防御性保护，确保任何异常情况下引用计数都不会变为负数。
        /// </remarks>
        public bool Release()
        {
            // 原子标记为已释放：只有一个调用方能成功把 0 置为 1。
            if (Interlocked.CompareExchange(ref _released, 1, 0) != 0)
            {
                // 已释放过，拒绝重复释放。
                return false;
            }

            // 递减引用计数；防负数保护由 PackageRecord.TryReleaseReference 提供。
            _record.TryReleaseReference(out _);
            return true;
        }
    }
}
