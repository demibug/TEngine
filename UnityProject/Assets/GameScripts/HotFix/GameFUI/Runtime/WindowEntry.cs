using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFUI
{
    /// <summary>
    /// 单个窗口类型的运行时跟踪条目，维护实例、显式状态、operation version 与每轮打开元数据。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策4（Show 公开契约由显式状态与操作版本驱动）。
    /// <para>
    /// FUIModule 为每个窗口类型保存一个 <see cref="WindowEntry"/>，其中包含：
    /// <list type="bullet">
    /// <item>注册描述（<see cref="Descriptor"/>）：从 <see cref="FUIBindingRegistry"/> 取得的不可变描述，
    ///   含 URL、包名、层级、缓存策略、attach/detach 等。</item>
    /// <item>当前实例（<see cref="Window"/>）：实例终态 <see cref="FUIWindowState.Disposed"/> 后清空，
    ///   下一次 Show 从新的 <see cref="FUIWindowState.Absent"/> 创建新实例。</item>
    /// <item>当前状态（<see cref="State"/>）：显式 8 态之一，由 <see cref="TransitionTo"/> 校验后变更。</item>
    /// <item>operation version（<see cref="OperationVersion"/>）：Close/重开操作递增，Show 只读取快照，
    ///   用于检测过期操作（如加载期间收到 Close 后，旧加载完成只能回滚，不能显示）。</item>
    /// <item>共享创建任务（<see cref="SharedCreateTask"/>）：同类型并发 Show 合并加载与实例创建，
    ///   后续请求 await 同一任务，完成后各自获得刷新机会。</item>
    /// <item>包租约（<see cref="Lease"/>）：实例持有期间引用窗口所属包，最终释放时归还。</item>
    /// <item>打开取消域（<see cref="OpenCts"/>）：每轮打开创建，Close 或 Dispose 时取消，
    ///   供窗口自行启动的业务异步任务使用（<see cref="FUIWindow.OpenCancellationToken"/>）。</item>
    /// <item>刷新请求队列（<see cref="PendingRefreshArgs"/>）：有效 Show 请求按 FIFO 进入队列，
    ///   每个请求在更新 <c>UserDatas/UserData</c> 后各执行一次同步 <c>OnRefresh()</c>。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 显式状态转换校验（spec：窗口状态转换受控）：
    /// <code>
    /// Absent -&gt; Loading           （Show 发起加载）
    /// Loading -&gt; Opening           （包与实例就绪，开始打开生命周期）
    /// Loading -&gt; Absent            （加载失败或被取消，回滚可重试）
    /// Opening -&gt; Open              （OnOpen/OnRefresh 完成）
    /// Opening -&gt; Absent            （打开阶段失败或被取消，回滚并释放实例）
    /// Open -&gt; Hidden               （Hide：只切换显示/输入，不结束打开域）
    /// Hidden -&gt; Open               （再次显示，不重新建立打开域）
    /// Open -&gt; Closing              （Close：取消 Open Token、清事件、OnClose）
    /// Hidden -&gt; Closing            （从 Hidden 直接 Close）
    /// Closing -&gt; Cached            （声明 Cache 时保留实例与租约）
    /// Closing -&gt; Disposed          （默认 None 或最终释放：Dispose 实例并归还租约）
    /// Cached -&gt; Opening            （缓存窗口再次 Show：重新建立打开域与事件域）
    /// Cached -&gt; Disposed           （缓存窗口最终释放）
    /// Disposed -&gt; Absent           （下一次 Show 从新 Absent entry 创建新实例）
    /// </code>
    /// Disposed 是实例终态；类型在下一次 Show 时从新的 Absent entry 创建新实例。
    /// 非法转换抛 <see cref="FUIException"/>，包含窗口类型、URL、包名、当前状态与目标状态。
    /// </para>
    /// <para>
    /// operation version 机制（spec：同类型并发打开串行收敛；加载期间 Close）：
    /// design.md 决策4 明确"Close 在任何非终态都会递增 version，使旧操作完成后只能回滚，不能显示"，
    /// 因此递增责任在 Close/重开，而非每个 Show。Show 只捕获当前 version 快照用于完成后比对，
    /// 保证同类型并发 Show 共享同一快照，loader 完成后版本校验通过，合并等待方拿到同一实例
    /// （spec: 同类型并发打开串行收敛——只创建一个实例）。操作完成后通过 <see cref="IsOperationStale"/>
    /// 检查当前版本是否已变化，若变化（如期间收到 Close 递增了版本）则该操作为过期操作，
    /// 只能回滚（释放本操作取得的资源租约），不得显示或执行过期生命周期回调。
    /// </para>
    /// <para>
    /// 边界约束：本类型不 using 且不反向依赖 GameLogic/GamePlay/GameBattle 命名空间。
    /// </para>
    /// </remarks>
    internal sealed class WindowEntry
    {
        /// <summary>
        /// 合法状态转换表：键为源状态，值为可到达的目标状态集合。
        /// </summary>
        /// <remarks>
        /// 依据 spec“窗口状态转换受控”与 design.md 决策4 的状态机图。
        /// 未列出的转换均为非法，<see cref="TransitionTo"/> 会抛 <see cref="FUIException"/>。
        /// </remarks>
        private static readonly Dictionary<FUIWindowState, HashSet<FUIWindowState>> _legalTransitions =
            BuildLegalTransitions();

        /// <summary>
        /// 本条目对应的窗口类型，同时作为 <see cref="FUIModule"/> 字典键。
        /// </summary>
        public Type WindowType { get; }

        /// <summary>
        /// 注册描述。从 <see cref="FUIBindingRegistry"/> 取得，字段不可变。
        /// </summary>
        public FUIDescriptor Descriptor { get; }

        /// <summary>
        /// 当前窗口实例。实例终态 <see cref="FUIWindowState.Disposed"/> 后清空为 null，
        /// 下一次 Show 从 <see cref="FUIWindowState.Absent"/> 创建新实例。
        /// </summary>
        public FUIWindow Window { get; set; }

        /// <summary>
        /// 当前显式状态。由 <see cref="TransitionTo"/> 校验后变更。
        /// </summary>
        public FUIWindowState State { get; private set; }

        /// <summary>
        /// 当前操作版本号。Close/重开操作递增，Show 只读取快照，用于检测过期操作。
        /// </summary>
        /// <remarks>
        /// 初始为 0。design.md 决策4：Close 在任何非终态递增 version，使旧操作完成后只能回滚；
        /// Show 不递增 version，只捕获当前快照用于完成后比对，保证并发同类型 Show 不互相取消。
        /// 操作完成时通过 <see cref="IsOperationStale"/> 比对快照版本判断是否过期。
        /// 读写底层字段 <see cref="_operationVersionField"/> 使用 <see cref="Interlocked"/>，保证共享加载任务
        /// 在其他线程完成回调时比对版本的可见性。
        /// </remarks>
        public long OperationVersion => Interlocked.Read(ref _operationVersionField);

        /// <summary>
        /// operation version 的实际存储字段，供 <see cref="OperationVersion"/> 读取与
        /// <see cref="IncrementOperationVersion"/> 原子递增。
        /// </summary>
        private long _operationVersionField;

        /// <summary>
        /// 同类型并发 Show 合并的共享创建任务。
        /// </summary>
        /// <remarks>
        /// 首个 Show 请求创建并执行加载与实例构造；后续同类型请求 await 同一任务，
        /// 完成后各自进入 <see cref="PendingRefreshArgs"/> 队列获得刷新机会（design.md 决策4）。
        /// 任务完成后置 null，下一次新的 Show 从 Absent 重新创建。
        /// </remarks>
        public UniTaskCompletionSource<FUIWindow> SharedCreateTask { get; set; }

        /// <summary>
        /// 实例持有的包租约。实例存活或缓存期间持有，最终释放时归还。
        /// </summary>
        public PackageLease Lease { get; set; }

        /// <summary>
        /// 当前打开周期的取消令牌源。每轮打开创建，Close 或 Dispose 时取消。
        /// </summary>
        /// <remarks>
        /// 对外通过 <see cref="FUIWindow.SetOpenCancellationToken"/> 注入为
        /// <see cref="FUIWindow.OpenCancellationToken"/>，供窗口自行启动的业务异步任务使用。
        /// </remarks>
        public CancellationTokenSource OpenCts { get; set; }

        /// <summary>
        /// 实例生命周期是否已完成 attach（AttachContext -> Descriptor.Attach -> AttachWidgetTree -> OnCreate）。
        /// </summary>
        /// <remarks>
        /// 用于区分两条失败回滚路径（任务 5.10）：
        /// <list type="bullet">
        /// <item>false：attach 前失败（如包加载、实例构造或类型转换失败），
        ///   实例未执行任何生命周期回调，回滚只需 Dispose GObject + Release lease，
        ///   不调用 OnDispose / Descriptor.Detach / Dispose Widgets。</item>
        /// <item>true：attach 后失败（如版本过期、Opening 阶段失败）或正常 Close/Shutdown 释放，
        ///   实例已执行完整实例生命周期，回滚需按完整释放顺序：
        ///   Dispose Widgets -> OnDispose -> Descriptor.Detach -> Dispose GObject -> Release lease
        ///   （design.md 决策5）。</item>
        /// </list>
        /// 由 FUIModule.ExecuteLoadAndOpenAsync 在 AttachContext 开始前置 false，
        /// 在 OnCreate 完成后置 true。RollbackLoadedInstance 根据本字段选择回滚路径。
        /// </remarks>
        public bool IsInstanceLifecycleAttached { get; set; }

        /// <summary>
        /// 有效 Show 刷新请求队列（FIFO）。每个请求在更新 UserDatas/UserData 后执行一次同步 OnRefresh。
        /// </summary>
        public Queue<object[]> PendingRefreshArgs { get; } = new Queue<object[]>();

        /// <summary>
        /// 构造窗口条目，初始状态为 <see cref="FUIWindowState.Absent"/>，operation version 为 0。
        /// </summary>
        /// <param name="windowType">窗口类型，不得为 null。</param>
        /// <param name="descriptor">注册描述，不得为 null。</param>
        /// <exception cref="FUIException">参数为 null。</exception>
        public WindowEntry(Type windowType, FUIDescriptor descriptor)
        {
            if (windowType == null)
            {
                throw new FUIException("WindowEntry 构造失败：windowType 不能为空。");
            }

            // descriptor 由 FUIModule 从 Registry 取得，调用方保证非空；这里仍做防御校验。
            if (descriptor.URL == null)
            {
                throw new FUIException("WindowEntry 构造失败：descriptor 不能为空。");
            }

            WindowType = windowType;
            Descriptor = descriptor;
            State = FUIWindowState.Absent;
            // _operationVersionField 默认为 0，无需显式赋值。
        }

        /// <summary>
        /// 递增 operation version 并返回递增后的新版本号。
        /// </summary>
        /// <returns>递增后的新版本号。</returns>
        /// <remarks>
        /// design.md 决策4："Close 在任何非终态都会递增 version，使旧操作完成后只能回滚，不能显示"。
        /// 本方法由 Close/重开（5.6）在"任何非终态"调用，使旧操作完成后通过 <see cref="IsOperationStale"/>
        /// 检测到版本变化，从而只能回滚，不能显示或执行过期回调（spec：加载期间 Close——旧打开操作 SHALL 失效）。
        /// Show（5.5）不调用本方法，只通过 <see cref="OperationVersion"/> 捕获快照，
        /// 保证同类型并发 Show 共享同一快照，不互相取消（spec：同类型并发打开串行收敛）。
        /// </remarks>
        public long IncrementOperationVersion()
        {
            // 使用 Interlocked.Increment 保证多线程下版本号递增原子可见；
            // WindowEntry 本身主要在主线程驱动，但共享加载任务可能在其他线程完成回调时比对版本，
            // 因此版本号读写使用 Interlocked 以保证可见性。
            return Interlocked.Increment(ref _operationVersionField);
        }

        /// <summary>
        /// 判断指定快照版本是否已过期（即当前 <see cref="OperationVersion"/> 与快照不同）。
        /// </summary>
        /// <param name="snapshotVersion">操作开始时通过 <see cref="OperationVersion"/> 捕获的版本快照（Show）或通过 <see cref="IncrementOperationVersion"/> 递增后的版本（Close/重开）。</param>
        /// <returns>当前版本号与快照不同时返回 true，表示该操作为过期操作。</returns>
        public bool IsOperationStale(long snapshotVersion)
        {
            return Interlocked.Read(ref _operationVersionField) != snapshotVersion;
        }

        /// <summary>
        /// 校验并执行状态转换，非法转换抛 <see cref="FUIException"/>。
        /// </summary>
        /// <param name="target">目标状态。</param>
        /// <exception cref="FUIException">源状态到目标状态不是合法转换。</exception>
        /// <remarks>
        /// 合法转换路径见类注释中的状态机图。Disposed 为终态，只能通过新建 entry 回到 Absent。
        /// </remarks>
        public void TransitionTo(FUIWindowState target)
        {
            FUIWindowState source = State;

            if (!_legalTransitions.TryGetValue(source, out HashSet<FUIWindowState> allowed)
                || !allowed.Contains(target))
            {
                throw new FUIException(
                    BuildTransitionErrorMessage(source, target));
            }

            State = target;
        }

        /// <summary>
        /// 尝试校验状态转换，不抛异常。
        /// </summary>
        /// <param name="target">目标状态。</param>
        /// <returns>合法返回 true，非法返回 false。</returns>
        public bool CanTransitionTo(FUIWindowState target)
        {
            if (!_legalTransitions.TryGetValue(State, out HashSet<FUIWindowState> allowed))
            {
                return false;
            }

            return allowed.Contains(target);
        }

        /// <summary>
        /// 构建非法状态转换的错误消息，包含窗口类型、URL、包名、当前状态与目标状态。
        /// </summary>
        /// <param name="source">源状态。</param>
        /// <param name="target">目标状态。</param>
        /// <returns>格式化的错误消息。</returns>
        private string BuildTransitionErrorMessage(FUIWindowState source, FUIWindowState target)
        {
            return $"非法窗口状态转换：{source} -&gt; {target}。" +
                $"窗口类型={WindowType?.FullName}, URL={Descriptor.URL}, " +
                $"包名={Descriptor.PackageName}, 组件名={Descriptor.ComponentName}。" +
                $"合法路径见 WindowEntry 状态机注释。";
        }

        /// <summary>
        /// 构建合法状态转换表。
        /// </summary>
        /// <returns>源状态到可到达目标状态集合的映射。</returns>
        private static Dictionary<FUIWindowState, HashSet<FUIWindowState>> BuildLegalTransitions()
        {
            var map = new Dictionary<FUIWindowState, HashSet<FUIWindowState>>
            {
                // Absent: 发起 Show 进入 Loading。
                [FUIWindowState.Absent] = new HashSet<FUIWindowState>
                {
                    FUIWindowState.Loading,
                },

                // Loading: 加载/创建成功进入 Opening；失败或取消回滚到 Absent（可重试）。
                [FUIWindowState.Loading] = new HashSet<FUIWindowState>
                {
                    FUIWindowState.Opening,
                    FUIWindowState.Absent,
                },

                // Opening: 打开生命周期完成进入 Open；失败或取消回滚到 Absent 并释放实例。
                [FUIWindowState.Opening] = new HashSet<FUIWindowState>
                {
                    FUIWindowState.Open,
                    FUIWindowState.Absent,
                },

                // Open: 可 Hide 到 Hidden；可 Close 到 Closing。
                [FUIWindowState.Open] = new HashSet<FUIWindowState>
                {
                    FUIWindowState.Hidden,
                    FUIWindowState.Closing,
                },

                // Hidden: 可再次显示回到 Open（不重新建立打开域）；可 Close 到 Closing。
                [FUIWindowState.Hidden] = new HashSet<FUIWindowState>
                {
                    FUIWindowState.Open,
                    FUIWindowState.Closing,
                },

                // Closing: OnClose 完成后按缓存策略进入 Cached 或 Disposed。
                [FUIWindowState.Closing] = new HashSet<FUIWindowState>
                {
                    FUIWindowState.Cached,
                    FUIWindowState.Disposed,
                },

                // Cached: 再次 Show 进入 Opening（重新建立打开域）；最终释放进入 Disposed。
                [FUIWindowState.Cached] = new HashSet<FUIWindowState>
                {
                    FUIWindowState.Opening,
                    FUIWindowState.Disposed,
                },

                // Disposed: 实例终态，类型在下一次 Show 时由 FUIModule 重建新 entry 回到 Absent。
                // 本 entry 的状态机到此结束，不在此表内自循环。
                [FUIWindowState.Disposed] = new HashSet<FUIWindowState>(),
            };

            return map;
        }
    }
}
