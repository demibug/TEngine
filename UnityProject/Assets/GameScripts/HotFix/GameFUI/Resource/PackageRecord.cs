using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using FairyGUI;

namespace GameFUI
{
    /// <summary>
    /// FairyGUI 包加载状态。
    /// </summary>
    /// <remarks>
    /// 描述单个 <see cref="PackageRecord"/> 在生命周期中所处的阶段。
    /// FUIContracts 中的 <see cref="FUIWindowState"/> 描述的是窗口状态，
    /// 不直接表达包加载语义，故在此单独定义包级状态枚举。
    /// 设计依据：design.md 决策8。
    /// </remarks>
    public enum PackageLoadState
    {
        /// <summary>尚未加载，记录已创建但未启动任何加载任务。</summary>
        NotLoaded = 0,

        /// <summary>正在加载：描述、外部资源或依赖尚未全部就绪。</summary>
        Loading = 1,

        /// <summary>就绪：UIPackage 已注册，依赖与外部资源全部 Ready，可创建对象。</summary>
        Ready = 2,

        /// <summary>加载失败：本次加载无法进入 Ready，记录等待回滚或重试。</summary>
        Failed = 3,

        /// <summary>卸载中：已进入最终释放流程，停止接受新 Acquire（4.9：design.md 决策9“停止新访问”）。</summary>
        /// <remarks>
        /// 进入此状态后，Acquire 必须拒绝创建新引用，防止释放过程中产生悬空访问。
        /// 该状态只在 UnloadPackage 起始处设置，释放完成后转为 Disposed。
        /// </remarks>
        Unloading = 4,

        /// <summary>已卸载：最终释放完成，UIPackage 已移除、handle 已 Dispose、依赖租约已释放（4.9 终态）。</summary>
        /// <remarks>
        /// 终态。记录仍在注册表中但不再持有任何资源，仅供诊断与防止复用。
        /// </remarks>
        Disposed = 5,
    }

    /// <summary>
    /// 窗口状态查询接口，供包最终释放前置检查判断是否存在存活或缓存窗口持有目标包。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策9“最终卸载必须……没有存活或缓存窗口”。
    /// 实现方为 FUIModule（5.x 任务负责）；4.9 阶段 FUIModule 尚未实现，本接口为预留契约。
    /// 传入 <see cref="PackageLoader.UnloadPackage"/> / <see cref="PackageLoader.UnloadAllForShutdown"/>
    /// 的实例为 null 时，视为无窗口约束（调用方确信无存活/缓存窗口，例如 Shutdown 场景）。
    /// 边界约束：本接口不 using 且不反向依赖 GameLogic/GamePlay/GameBattle 命名空间。
    /// </remarks>
    public interface IFUIWindowStateProvider
    {
        /// <summary>
        /// 查询是否存在存活或缓存窗口持有指定包。
        /// </summary>
        /// <param name="packageName">逻辑包名。</param>
        /// <returns>true 表示存在存活或缓存窗口持有该包，不应卸载；false 表示可卸载。</returns>
        bool HasActiveOrCachedWindow(string packageName);
    }

    /// <summary>
    /// 单个 FairyGUI 包的所有权记录，统一持有包状态、共享加载任务、引用计数、
    /// 依赖租约、外部资源句柄表、待卸载版本与诊断信息。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// - design.md 决策8：每个包对应一个 PackageRecord，包含状态、UIPackage、共享加载任务、
    ///   引用计数、依赖租约、外部资源 AssetHandle 表、待卸载版本和诊断信息。
    /// - design.md 决策9：最终卸载必须同时满足引用为零、没有存活或缓存窗口、没有创建任务、
    ///   没有上层依赖、没有资源操作；本记录只持有数据与引用计数，卸载判定由上层 PackageManager 负责。
    /// - spec fairygui-package-loading：
    ///   * “包和依赖加载任务合并”——同包并发加载通过 <see cref="SharedLoadTask"/> 合并；
    ///   * “资源所有权唯一且释放有序”——外部资源句柄由 <see cref="AssetHandles"/> 统一持有；
    ///   * “包租约控制缓存和卸载”——引用计数由 <see cref="PackageLease"/> 增减。
    ///
    /// 线程安全：引用计数操作使用 <see cref="System.Threading.Interlocked"/> 提供防负数保护；
    /// 其余字段在 PackageManager 单线程驱动下访问，不额外加锁。
    ///
    /// 边界约束：本类型不 using 且不反向依赖 GameLogic/GamePlay/GameBattle 命名空间。
    /// </remarks>
    public sealed class PackageRecord
    {
        /// <summary>
        /// 构造包记录。
        /// </summary>
        /// <param name="packageName">逻辑包名，作为包的唯一身份，不得为 null 或空。</param>
        public PackageRecord(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                throw new System.ArgumentException("逻辑包名不能为空。", nameof(packageName));
            }

            PackageName = packageName;
            State = PackageLoadState.NotLoaded;
            Package = null;
            SharedLoadTask = null;
            _referenceCount = 0;
            AssetHandles = new Dictionary<string, IFUIAssetHandle>();
            DependencyLeases = new List<PackageLease>();
            PendingUnloadVersion = 0;
            LoadDurationMs = 0;
            FailureContext = null;
        }

        /// <summary>
        /// 逻辑包名，作为包的唯一身份。
        /// </summary>
        /// <remarks>
        /// 对应 spec 中的“包名作为逻辑身份”，与 <c>{PackageName}_fui</c> 描述资源 location 一致。
        /// </remarks>
        public string PackageName { get; }

        /// <summary>
        /// 当前加载状态。
        /// </summary>
        public PackageLoadState State { get; set; }

        /// <summary>
        /// 已注册的 FairyGUI <see cref="UIPackage"/>；加载完成后设置，未完成或失败时为 null。
        /// </summary>
        public UIPackage Package { get; set; }

        /// <summary>
        /// 共享加载任务，用于合并同一包的并发加载请求。
        /// </summary>
        /// <remarks>
        /// 首个 Acquire 创建该任务，后续同包 Acquire 直接等待同一任务，
        /// 完成后向每个调用方返回独立 <see cref="PackageLease"/>（spec：包和依赖加载任务合并）。
        /// null 表示当前没有进行中的加载任务。
        /// </remarks>
        public UniTaskCompletionSource<PackageRecord> SharedLoadTask { get; set; }

        /// <summary>
        /// 引用计数，由 <see cref="PackageLease"/> 持有时递增、释放时递减。
        /// </summary>
        /// <remarks>
        /// 通过 <see cref="AddReference"/> 与 <see cref="TryReleaseReference"/> 以
        /// <see cref="System.Threading.Interlocked"/> 操作，提供防负数保护。
        /// 每个存活或缓存窗口、正在创建的对象以及上层依赖均持有对应包租约（spec：包租约控制缓存和卸载）。
        /// </remarks>
        private int _referenceCount;

        /// <summary>
        /// 获取当前引用计数快照。
        /// </summary>
        public int ReferenceCount => _referenceCount;

        /// <summary>
        /// 递增引用计数，返回递增后的值。
        /// </summary>
        /// <returns>递增后的引用计数。</returns>
        public int AddReference()
        {
            return System.Threading.Interlocked.Increment(ref _referenceCount);
        }

        /// <summary>
        /// 递减引用计数，提供防负数保护。
        /// </summary>
        /// <returns>递减成功返回 true 并输出递减后的值；当引用计数已为零时拒绝递减并返回 false。</returns>
        /// <remarks>
        /// spec“包租约控制缓存和卸载”：重复释放租约 SHALL 被拒绝，不得使引用计数变为负数。
        /// 本方法在引用计数为零时直接拒绝递减，配合 <see cref="PackageLease"/> 的
        /// <see cref="PackageLease.IsReleased"/> 标志共同保证幂等语义。
        ///
        /// 6.4 Delayed 卸载触发：当递减后引用计数归零时，通过
        /// <see cref="OnReferenceCountReachedZero"/> 静态回调通知 <see cref="PackageLoader"/>，
        /// 由其根据当前卸载策略决定是否启动延迟卸载任务（design.md 决策8：延迟卸载前重新 Acquire
        /// 通过递增待卸载版本取消旧卸载任务）。回调在引用计数原子递减成功后同步触发，
        /// 不持有任何锁，避免在回调用户路径中产生死锁；回调用户不得在回调中再次递增/递减同一记录的引用计数。
        /// </remarks>
        public bool TryReleaseReference(out int newCount)
        {
            int spinIndex = 0;
            while (true)
            {
                int current = _referenceCount;
                if (current <= 0)
                {
                    // 引用计数已为零，拒绝递减，防止变为负数。
                    newCount = 0;
                    return false;
                }

                int next = current - 1;
                if (System.Threading.Interlocked.CompareExchange(ref _referenceCount, next, current) == current)
                {
                    newCount = next;
                    // 6.4：引用计数归零时通知 PackageLoader 评估延迟卸载。
                    // 仅在成功递减到 0 时触发，保证每个“从有引用到无引用”的边界只通知一次。
                    // 回调为 null 时（如测试场景未配置）安全跳过，不影响引用计数语义。
                    if (next == 0)
                    {
                        try
                        {
                            OnReferenceCountReachedZero?.Invoke(this);
                        }
                        catch
                        {
                            // 回调异常不得影响引用计数递减的成功语义；诊断由回调实现方处理。
                        }
                    }
                    return true;
                }

                // 竞态失败则重试，避免长自旋。
                if (++spinIndex > 64)
                {
                    System.Threading.Thread.SpinWait(spinIndex);
                }
            }
        }

        /// <summary>
        /// 引用计数归零静态回调，由 <see cref="PackageLoader"/> 在模块装配阶段设置。
        /// </summary>
        /// <remarks>
        /// 6.4 Delayed 卸载策略触发点：当任意 <see cref="PackageLease.Release"/> 使引用计数归零时，
        /// <see cref="TryReleaseReference"/> 同步触发本回调。回调实现（PackageLoader）根据当前
        /// <see cref="PackageLoader.UnloadPolicy"/> 决定是否启动延迟卸载任务：
        /// - <see cref="FUIPackageUnloadPolicy.KeepUntilShutdown"/>：不启动卸载，包保留到模块 Shutdown；
        /// - <see cref="FUIPackageUnloadPolicy.Delayed"/>：递增 <see cref="PendingUnloadVersion"/>，
        ///   调度延迟卸载任务，到期后重新检查引用计数与版本，决定是否执行最终释放。
        ///
        /// 使用静态委托而非实例事件，原因：
        /// 1. <see cref="PackageLoader"/> 是全局唯一策略持有者，所有记录共用同一处理逻辑；
        /// 2. 避免每个 <see cref="PackageRecord"/> 持有事件订阅者导致的内存泄漏风险；
        /// 3. 回调只做“评估是否启动卸载”，不持有记录强引用以外的状态，无悬空引用问题。
        ///
        /// 线程安全：回调字段在 <see cref="PackageLoader"/> 静态构造阶段一次性设置，之后不修改；
        /// .NET 静态初始化保证写入对所有线程可见，读取侧无需加锁或 <see cref="System.Threading.Volatile"/>。
        /// 回调为 null 时（未配置，如独立测试 <see cref="PackageRecord"/> 不经过 <see cref="PackageLoader"/>）
        /// <see cref="TryReleaseReference"/> 安全跳过。
        /// </remarks>
        internal static System.Action<PackageRecord> OnReferenceCountReachedZero;

        /// <summary>
        /// 外部资源 AssetHandle 表，按资源 location（即 <c>Path.GetFileNameWithoutExtension(item.file)</c>）索引。
        /// </summary>
        /// <remarks>
        /// spec“资源所有权唯一且释放有序”：由 YooAsset 加载的贴图、音频及其他外部资源
        /// SHALL 由包记录统一持有其 <see cref="IFUIAssetHandle"/>。最终释放时先移除 FairyGUI 包，
        /// 再 Dispose 本表全部 handle，最后释放依赖租约。
        /// 注意：描述文件 handle 不入本表，由 <see cref="DescHandle"/> 单独持有，
        /// 因为描述 location（<c>{PackageName}_fui</c>）不参与 FairyGUI resolver 的 location 映射，
        /// 放入本表会与 resolver 的 key 语义混淆。
        /// </remarks>
        public Dictionary<string, IFUIAssetHandle> AssetHandles { get; }

        /// <summary>
        /// 描述文件资源 handle，对应 location <c>{PackageName}_fui</c>。
        /// </summary>
        /// <remarks>
        /// spec“资源所有权唯一且释放有序”：描述文件同样由 YooAsset 加载，
        /// SHALL 由包记录统一持有其 <see cref="IFUIAssetHandle"/>。
        /// 4.5 实现的 <see cref="PackageLoader.LoadPackageAsync"/> 在成功路径未将描述 handle 写入记录，
        /// 存在泄漏风险；4.6 在写入 <see cref="Package"/> 后将描述 handle 纳入本字段，
        /// 由最终释放阶段与 <see cref="AssetHandles"/> 一并 Dispose。
        /// 加载失败（AddPackage 抛出或返回 null）时由加载器自行 Dispose 描述 handle，
        /// 本字段保持 null，避免记录持有已释放句柄。
        /// </remarks>
        public IFUIAssetHandle DescHandle { get; set; }

        /// <summary>
        /// 依赖租约列表，持有本包递归 Acquire 得到的依赖包租约。
        /// </summary>
        /// <remarks>
        /// spec“包和依赖加载任务合并”：多个上层包可共享同一依赖，任一上层包持有依赖时依赖保持可用。
        /// 本列表在最终释放时按反向顺序释放（design.md 决策9）。
        /// </remarks>
        public List<PackageLease> DependencyLeases { get; }

        /// <summary>
        /// 待卸载版本，用于 Delayed 卸载策略取消（6.4 实现）。
        /// </summary>
        /// <remarks>
        /// design.md 决策8：延迟卸载前重新 Acquire 通过递增待卸载版本取消旧卸载任务。
        /// 卸载任务执行前比对自身捕获的版本与当前 <see cref="PendingUnloadVersion"/>，
        /// 不一致则放弃卸载，避免卸载与重载抖动（spec：延迟卸载期间重新 Acquire）。
        ///
        /// 6.4 版本递增时机：
        /// 1. <see cref="PackageLoader"/> 在 Delayed 策略下检测到引用计数归零并调度延迟卸载任务时，
        ///    递增版本并捕获本次任务版本号；
        /// 2. 延迟期内有新 <see cref="PackageLoader.AcquireAsync"/> 命中已 Ready 记录时，
        ///    递增版本使正在等待的旧卸载任务版本号过期；
        /// 3. 模块 <see cref="PackageLoader.UnloadAllForShutdown"/> 强制回收时，
        ///    各记录版本递增使进行中的延迟卸载任务安全中止（被 CanUnload 的状态检查拦截）。
        ///
        /// 版本号只增不减，不回绕处理（int.MaxValue 溢出在实际游戏生命周期内不会触及）。
        /// </remarks>
        public int PendingUnloadVersion { get; set; }

        /// <summary>
        /// 本次加载耗时（毫秒），用于诊断。0 表示未记录。
        /// </summary>
        public long LoadDurationMs { get; set; }

        /// <summary>
        /// 加载失败上下文，用于诊断。加载成功或未加载时为 null。
        /// </summary>
        public string FailureContext { get; set; }

        /// <summary>
        /// 获取简短诊断字符串，便于日志输出。
        /// </summary>
        public override string ToString()
        {
            return $"[PackageRecord] name={PackageName}, state={State}, refCount={_referenceCount}, handles={AssetHandles.Count}, hasDesc={DescHandle != null}, depLeases={DependencyLeases.Count}, pendingUnloadVer={PendingUnloadVersion}";
        }

        /// <summary>
        /// 获取含加载耗时与失败上下文的完整诊断字符串，供任务 6.7 诊断日志使用。
        /// </summary>
        /// <remarks>
        /// 任务 6.7 要求覆盖加载耗时与失败上下文诊断点。本方法在 <see cref="ToString"/> 基础上
        /// 追加 <see cref="LoadDurationMs"/> 与 <see cref="FailureContext"/>，使一次日志输出即可
        /// 包含全部诊断字段，避免调用方重复拼接。
        /// </remarks>
        public string ToDiagnosticString()
        {
            string failure = FailureContext != null
                ? $"failure={FailureContext}"
                : "failure=<无>";
            return $"[PackageRecord] name={PackageName}, state={State}, refCount={_referenceCount}, " +
                $"handles={AssetHandles.Count}, depLeases={DependencyLeases.Count}, " +
                $"loadMs={LoadDurationMs}, pendingUnloadVer={PendingUnloadVersion}, {failure}";
        }

        /// <summary>
        /// 构建依赖链诊断文本：枚举 <see cref="DependencyLeases"/> 中每个依赖租约的包名，
        /// 拼接为 "A, B, C" 形式，供任务 6.7 依赖链诊断日志使用。
        /// </summary>
        /// <returns>依赖包名逗号分隔字符串；无依赖时返回 "&lt;无&gt;"。</returns>
        /// <remarks>
        /// 依赖来源为 <see cref="PackageLease.Record"/> 的 <see cref="PackageName"/>。
        /// 本方法只读遍历，不修改任何状态；用于在 Acquire 成功、FinalUnload 等关键节点
        /// 输出当前包的直接依赖集合，便于排查依赖关系与卸载顺序问题
        /// （spec：包和依赖加载任务合并；design.md 决策8/9）。
        /// </remarks>
        public string BuildDependencyChainText()
        {
            if (DependencyLeases == null || DependencyLeases.Count == 0)
            {
                return "<无>";
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int count = DependencyLeases.Count;
            for (int i = 0; i < count; i++)
            {
                PackageLease lease = DependencyLeases[i];
                if (lease == null || lease.Record == null)
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(lease.Record.PackageName);
            }

            return sb.Length > 0 ? sb.ToString() : "<无>";
        }
    }
}
