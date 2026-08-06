namespace GameFUI
{
    /// <summary>
    /// FairyGUI 受管理 Widget 基类。
    /// <para>Widget 的生命周期较 Window 简单：实例创建时执行一次 <see cref="OnCreate"/>，最终释放时执行一次 <see cref="OnDispose"/>。
    /// 生命周期回调全部为同步，框架不得在生命周期方法中等待业务自行启动的异步任务（design.md 决策5）。</para>
    /// <para>每个受管理 Widget 在其业务生命周期（<see cref="OnCreate"/>）开始前由 FUIModule 通过幂等 Attach 获得
    /// <see cref="OwnerWindow"/> 与运行时上下文（design.md 决策6、spec: Widget 在生命周期前获得所属窗口）。
    /// 业务不得在构造函数或 <c>ConstructFromXML</c> 中依赖 <see cref="OwnerWindow"/>。</para>
    /// <para>Stage 事件隔离契约（design.md 决策6；任务 6.3 验证）：
    /// Widget 的 <see cref="OnCreate"/>/<see cref="OnDispose"/> 由 FUIModule 的受控入口（AttachWidgetTree 初始树、
    /// CreateDynamicWidget 动态创建、AttachDynamicWidget 池化复用）驱动，不订阅
    /// <c>onAddedToStage</c>/<c>onRemovedFromStage</c> 作为生命周期信号。动态 Attach 时 Widget 被添加到
    /// 显示树不会误触发窗口或自身的 Open/Close（design.md 决策6：onAddedToStage/onRemovedFromStage
    /// 不作为唯一 Open/Close 信号）。</para>
    /// </summary>
    public class FUIWidget : FairyGUI.GComponent
    {
        /// <summary>
        /// 所属窗口。在 OnCreate 前由 FUIModule 通过幂等 <see cref="AttachContext"/> 设置（任务 6.1）。
        /// <para>任务 6.1 建立显式幂等 Attach 状态（<see cref="IsAttached"/>），保证初始 Widget 树在 OnCreate 前
        /// 已获得正确的 OwnerWindow。任务 6.2 补充 owner 变更诊断（<see cref="LastAttachOwnerChanged"/>）与
        /// 池化复用重置入口（<see cref="ResetForReuse"/>），用于动态创建与池化复用 Widget 的受控 Attach。
        /// 在未 Attach 前为 null。</para>
        /// </summary>
        public FUIWindow OwnerWindow { get; internal set; }

        /// <summary>
        /// 上下文附加状态。由 attach 阶段注入的运行时上下文，供业务在生命周期中访问依赖。
        /// <para>与 <see cref="FUIWindow.Context"/> 同语义，用弱类型占位，待后续任务细化为强类型上下文。</para>
        /// </summary>
        public object Context { get; internal set; }

        /// <summary>
        /// 实例是否已完成幂等 Attach（由 <see cref="AttachContext"/> 设置为 true）。
        /// <para>任务 6.1：用于显式表达初始 Widget 树的幂等 Attach 状态，使“重复调用安全”契约可观测、可验证。
        /// 任务 6.2：owner 变更诊断与池化复用入口已补充，本标记可由 <see cref="ResetForReuse"/> 回退为 false，
        /// 以支持池化复用场景下的干净重新 Attach（design.md 决策6、风险项“Widget 池化和动态重挂导致
        /// OwnerWindow 陈旧 → Attach 幂等但检测 owner 变更，复用入口在交付业务前重新 Attach”）。</para>
        /// <para>重复以相同 owner 调用 <see cref="AttachContext"/> 安全：只重新设置字段与本标记，不重复触发 OnCreate
        /// （由 <see cref="InvokeOnCreate"/> 的 <see cref="_isCreated"/> 保护）。</para>
        /// <para>已 Dispose 的实例不应再被复用：<see cref="InvokeOnDispose"/> 执行后实例进入终态，
        /// <see cref="ResetForReuse"/> 不会重置 <see cref="IsDisposed"/>，避免复用已释放实例。</para>
        /// </summary>
        internal bool IsAttached { get; private set; }

        /// <summary>
        /// 最近一次 <see cref="AttachContext"/> 调用是否检测到 owner 变更（诊断标记，任务 6.2）。
        /// </summary>
        /// <remarks>
        /// design.md 决策6 风险项：“Widget 池化和动态重挂导致 OwnerWindow 陈旧 → Attach 幂等但检测 owner 变更，
        /// 复用入口在交付业务前重新 Attach”。
        /// <para>
        /// 本标记在每次 <see cref="AttachContext"/> 调用时更新：
        /// <list type="bullet">
        /// <item>首次 Attach（<see cref="IsAttached"/> 为 false）：置 false，无 owner 可比较。</item>
        /// <item>重复 Attach 且 owner 未变：置 false，幂等无副作用。</item>
        /// <item>重复 Attach 且 owner 变化（旧 owner 非 null 且与新 owner 不是同一实例）：置 true，
        ///   同时更新 <see cref="OwnerWindow"/> 为新 owner，使“复用入口在交付业务前重新 Attach”生效。
        ///   这使池化复用跨 owner 场景仍能更新 OwnerWindow，但通过本标记暴露变更事实供诊断与测试。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 本标记为诊断性质，不阻止 owner 更新：design.md 明确“复用入口在交付业务前重新 Attach”，
        /// 即 owner 变更后应更新 OwnerWindow 而非拒绝；本标记供 <see cref="FUIModule"/> 受控入口与
        /// 测试程序集观测变更事件。调用 <see cref="ResetForReuse"/> 会将本标记重置为 false。
        /// </para>
        /// </remarks>
        internal bool LastAttachOwnerChanged { get; private set; }

        /// <summary>
        /// 实例生命周期是否已执行过 OnCreate。用于保证 OnCreate 在单个实例上只执行一次（幂等）。
        /// <para>design.md 决策5、决策6：Widget 的 OnCreate 各执行一次。本标记由 <see cref="InvokeOnCreate"/> 设置，
        /// 重复调用时直接跳过，不重复执行业务回调。幂等 Attach 不得重复执行创建生命周期
        /// （spec: 动态列表 Widget——不得因重复 Attach 重复执行创建生命周期）。</para>
        /// </summary>
        private bool _isCreated;

        /// <summary>
        /// 实例生命周期 OnCreate 是否已执行（internal，供测试程序集验证 Widget 生命周期次数契约）。
        /// </summary>
        internal bool IsCreated => _isCreated;

        /// <summary>
        /// 实例生命周期是否已执行过 OnDispose。用于保证 OnDispose 在单个实例上只执行一次（幂等）。
        /// <para>design.md 决策5：最终释放顺序为 Dispose Widgets -> OnDispose（窗口）-> Descriptor.Detach ->
        /// Dispose GObject -> Release lease。Widget 的 OnDispose 在"Dispose Widgets"阶段执行，
        /// 位于窗口 OnDispose 之前。本标记由 <see cref="InvokeOnDispose"/> 设置，
        /// 重复调用时直接跳过，不重复执行业务回调。</para>
        /// </summary>
        private bool _isDisposed;

        /// <summary>
        /// 实例生命周期 OnDispose 是否已执行（internal，供测试程序集验证 Widget 生命周期次数契约与最终释放顺序）。
        /// </summary>
        internal bool IsDisposed => _isDisposed;

        /// <summary>
        /// 实例生命周期：创建回调，在幂等 Attach 设置 <see cref="OwnerWindow"/> 与 <see cref="Context"/> 之后执行，仅执行一次。
        /// 同步回调，不得在此启动需要被框架等待的异步任务。
        /// </summary>
        protected virtual void OnCreate()
        {
        }

        /// <summary>
        /// 实例生命周期：最终释放回调，仅执行一次。
        /// 同步回调。
        /// </summary>
        protected virtual void OnDispose()
        {
        }

        /// <summary>
        /// 框架内部入口：在 OnCreate 之前注入所属窗口与运行时上下文（幂等 Attach，任务 6.1）。
        /// 由 FUIModule 在 <see cref="FUIModule.AttachWidgetTree"/> 的 Collect 阶段对初始 Widget 树调用，
        /// 也可由动态受控入口 <see cref="FUIModule.CreateDynamicWidget{TWidget}"/> /
        /// <see cref="FUIModule.AttachDynamicWidget"/> 调用。业务不应直接调用。
        /// <para>
        /// 幂等语义（任务 6.1 + 6.2）：
        /// <list type="bullet">
        /// <item>重复以相同 owner 调用安全：只重新设置 <see cref="OwnerWindow"/>、<see cref="Context"/> 与
        ///   <see cref="IsAttached"/>，不抛异常，不重复触发 OnCreate（由 <see cref="InvokeOnCreate"/> 的
        ///   <see cref="_isCreated"/> 保护，spec: 动态列表 Widget——不得因重复 Attach 重复执行创建生命周期）。</item>
        /// <item>owner 变更诊断（任务 6.2）：当本实例已 Attach 且旧 owner 与新 owner 不是同一实例时，
        ///   设置 <see cref="LastAttachOwnerChanged"/> = true 作为诊断标记，并更新 <see cref="OwnerWindow"/> 为新 owner。
        ///   这使池化复用跨 owner 场景仍能“在交付业务前重新 Attach”（design.md 决策6 风险项），
        ///   同时通过标记暴露变更事实供受控入口与测试观测。owner 变更不抛异常：design.md 明确复用入口应重新
        ///   Attach 而非拒绝；如需拒绝跨 owner 复用，应由上层受控入口在诊断后决定。</item>
        /// <item><see cref="ResetForReuse"/> 可在池化复用前显式回退 Attach 状态，使后续 AttachContext 视为首次 Attach，
        ///   不触发 owner 变更诊断（用于干净的池化复用路径）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 调用时机保证（spec: Widget 在生命周期前获得所属窗口——初始嵌套 Widget SHALL 在执行自身 OnCreate 前获得正确的 OwnerWindow）：
        /// FUIModule.AttachWidgetTree 先对全部初始 Widget 完成 AttachContext，再统一 InvokeOnCreate，
        /// 因此 Widget.OnCreate 执行时 <see cref="OwnerWindow"/> 已可用。
        /// 动态受控入口同样在 InvokeOnCreate 前完成 AttachContext。
        /// </para>
        /// </summary>
        /// <param name="ownerWindow">所属窗口。</param>
        /// <param name="context">运行时上下文，可为 null。</param>
        internal void AttachContext(FUIWindow ownerWindow, object context)
        {
            // owner 变更诊断（任务 6.2）：已 Attach 且旧 owner 非 null 且与新 owner 不是同一实例时，
            // 标记诊断并更新 OwnerWindow。design.md 决策6 风险项要求“检测 owner 变更”且“复用入口在交付业务前重新 Attach”：
            // 此处更新 OwnerWindow 完成重新 Attach，LastAttachOwnerChanged 标记暴露变更供受控入口与测试观测。
            // 初始 Widget 树（任务 6.1 范围）owner 唯一确定，重复 Attach 相同 owner，不触发本标记。
            bool ownerChanged = IsAttached
                && OwnerWindow != null
                && !ReferenceEquals(OwnerWindow, ownerWindow);
            LastAttachOwnerChanged = ownerChanged;

            // 幂等 Attach：设置字段与标记，不抛异常，不重复触发 OnCreate（任务 6.1）。
            // owner 变更时更新 OwnerWindow 为新 owner，使后续业务访问得到正确窗口（design.md：复用入口重新 Attach）。
            OwnerWindow = ownerWindow;
            Context = context;
            IsAttached = true;
        }

        /// <summary>
        /// 池化复用前的状态重置入口（任务 6.2）。
        /// <para>回退 Attach 相关状态（<see cref="IsAttached"/>、<see cref="OwnerWindow"/>、<see cref="Context"/>、
        /// <see cref="LastAttachOwnerChanged"/>），使后续 <see cref="AttachContext"/> 视为首次 Attach，
        /// 不触发 owner 变更诊断。用于池化复用场景的干净重新 Attach
        /// （design.md 决策6：复用入口在交付业务前重新 Attach）。</para>
        /// <para>
        /// 本方法不重置实例生命周期标记（<see cref="IsCreated"/>、<see cref="IsDisposed"/>）：
        /// <list type="bullet">
        /// <item><see cref="IsCreated"/> 保持 true，保证 <see cref="InvokeOnCreate"/> 幂等不重复执行
        ///   （spec: 动态列表 Widget——不得因重复 Attach 重复执行创建生命周期）。</item>
        /// <item><see cref="IsDisposed"/> 保持 true：已 Dispose 的实例进入终态，不应被复用。
        ///   调用方在池化回收前应判断 <see cref="IsDisposed"/>，避免复用已释放实例。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 典型池化复用流程：
        /// <code>
        /// widget.ResetForReuse();          // 回退 Attach 状态（不重置生命周期）
        /// module.AttachDynamicWidget(widget, newOwner);  // 受控重新 Attach + 包校验
        /// // InvokeOnCreate 幂等跳过（IsCreated 保持 true），不重复执行 OnCreate
        /// </code>
        /// </para>
        /// </summary>
        internal void ResetForReuse()
        {
            // 只回退 Attach 相关状态，保留生命周期幂等标记。
            // IsCreated 保持 true：保证 InvokeOnCreate 幂等，池化复用不重复执行 OnCreate。
            // IsDisposed 保持 true：已 Dispose 的实例不应被复用；调用方应判断 IsDisposed 避免复用已释放实例。
            IsAttached = false;
            OwnerWindow = null;
            Context = null;
            LastAttachOwnerChanged = false;
        }

        /// <summary>
        /// 框架内部入口：触发实例生命周期 <see cref="OnCreate"/>（幂等，仅执行一次）。
        /// <para>由 FUIModule 在 <c>AttachWidgetTree</c> 为本 Widget 完成幂等 Attach（<see cref="AttachContext"/>，
        /// 任务 6.1）之后统一调用，保证 OnCreate 在单个实例上只执行一次（design.md 决策5/6）。
        /// 重复调用安全跳过，不重复执行业务回调（spec: 动态列表 Widget——不得因重复 Attach 重复执行创建生命周期）。</para>
        /// <para>任务 6.1 保证：本入口被调用时 <see cref="OwnerWindow"/> 已由 <see cref="AttachContext"/> 设置可用
        /// （spec: 初始嵌套 Widget SHALL 在执行自身 OnCreate 前获得正确的 OwnerWindow）。</para>
        /// <para>OnCreate 为 protected virtual，业务通过 override OnCreate 参与生命周期；
        /// 本入口负责幂等保护，不改变 OnCreate 的 protected 可见性。</para>
        /// </summary>
        internal void InvokeOnCreate()
        {
            // 幂等保护：已执行过 OnCreate 的实例不再重复执行（design.md 决策5/6）。
            if (_isCreated)
            {
                return;
            }

            _isCreated = true;
            OnCreate();
        }

        /// <summary>
        /// 框架内部入口：触发实例生命周期 <see cref="OnDispose"/>（幂等，仅执行一次）。
        /// <para>由 FUIModule 在最终释放阶段调用，位于窗口 OnDispose 之前
        /// （design.md 决策5：最终释放顺序 Dispose Widgets -> OnDispose（窗口）-> Descriptor.Detach ->
        /// Dispose GObject -> Release lease；"Dispose Widgets"即遍历 Widget 树并调用本入口）。
        /// 重复调用安全跳过，不重复执行业务回调。</para>
        /// <para>OnDispose 为 protected virtual，业务通过 override OnDispose 参与生命周期；
        /// 本入口负责幂等保护，不改变 OnDispose 的 protected 可见性。</para>
        /// <para>注意：本入口只触发 OnDispose 回调，不 Dispose Widget 自身的 GObject。
        /// Widget GObject 的 Dispose 由窗口 GObject 的 Dispose 顺带完成
        /// （FairyGUI GComponent.Dispose 会递归 Dispose 子对象），无需框架单独处理。</para>
        /// </summary>
        internal void InvokeOnDispose()
        {
            // 幂等保护：已执行过 OnDispose 的实例不再重复执行（design.md 决策5：OnDispose 各执行一次）。
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            OnDispose();
        }
    }
}
