using System.Threading;

namespace GameFUI
{
    /// <summary>
    /// FairyGUI 受管理 Window 基类。
    /// <para>生命周期回调全部为同步，框架不得在生命周期方法中等待业务自行启动的异步任务。
    /// 业务自行启动的 UniTask 必须使用 <see cref="OpenCancellationToken"/>，在 Close 时被取消，
    /// 但不属于 Show 的等待边界（design.md 决策4）。</para>
    /// <para>实例生命周期与每轮打开生命周期分离（design.md 决策5）：
    /// <list type="bullet">
    /// <item>首次创建：AttachContext -> Descriptor.Attach -> AttachWidgetTree -> <see cref="OnCreate"/>（执行一次）</item>
    /// <item>每轮打开：Create open CTS -> <see cref="RegisterOpenEvents"/> -> <see cref="OnOpen"/> -> <see cref="OnRefresh"/></item>
    /// <item>临时隐藏：<see cref="OnHide"/>，保留 open CTS 与事件域</item>
    /// <item>每轮关闭：Cancel open CTS -> <see cref="ClearOpenEvents"/> -> <see cref="OnClose"/></item>
    /// <item>最终释放：Dispose Widgets -> <see cref="OnDispose"/> -> Descriptor.Detach -> Dispose GObject（执行一次）</item>
    /// </list>
    /// 复用 TEngine <c>UIBase</c> 的同步回调约定（OnCreate/OnRefresh 同步）与 UserDatas/UserData 单参数便捷访问约定。</para>
    /// <para>Stage 事件隔离契约（design.md 决策6；任务 6.3 验证）：
    /// 窗口的 Open/Close/Hide 状态转换完全由 FUIModule 的显式 API（ShowAsync/Hide/Close）驱动，
    /// 不订阅 <c>onAddedToStage</c>/<c>onRemovedFromStage</c> 作为 Open/Close 信号。
    /// <c>AddChild</c>/<c>RemoveChild</c> 到层容器只是显示树操作，不触发状态转换；
    /// Hide 特别采用 <c>visible/touchable=false</c> 而非移出 Stage，避免触发 <c>onRemovedFromStage</c>
    /// 干扰 Widget 生命周期信号（见 FUIModule.HideEntryCore 注释）。</para>
    /// </summary>
    public abstract class FUIWindow : FairyGUI.GComponent
    {
        /// <summary>
        /// 当前打开请求的用户参数数组。每个有效刷新请求执行前由框架更新。
        /// 复用 TEngine <c>UIBase._userDatas</c> 的语义，但使用 protected setter 限制写入来源。
        /// </summary>
        protected object[] UserDatas { get; private set; }

        /// <summary>
        /// 便捷单参数访问，通常为 <see cref="UserDatas"/>[0]，无参数时为 null。
        /// 复用 TEngine <c>UIBase.UserData</c> 的便捷访问约定。
        /// </summary>
        public object UserData
        {
            get
            {
                if (UserDatas != null && UserDatas.Length >= 1)
                {
                    return UserDatas[0];
                }

                return null;
            }
        }

        /// <summary>
        /// 当前打开周期的取消令牌。每轮打开创建新的 CTS，Close 时取消。
        /// <para>供窗口自行启动的业务 UniTask 使用；框架不在生命周期方法中等待这些业务任务，
        /// 但保证它们在 Close 时收到取消（design.md 决策4、决策5）。</para>
        /// <para>在 <see cref="OnCreate"/> 阶段尚未建立打开域，此时访问返回 <see cref="CancellationToken.None"/>。</para>
        /// </summary>
        public CancellationToken OpenCancellationToken { get; private set; }

        /// <summary>
        /// 上下文附加状态。由 attach 阶段注入的运行时上下文，供业务在生命周期中访问依赖。
        /// <para>全局 FairyGUI creator 保持无状态；业务依赖由 FUIModule 通过可清理上下文附加（design.md 决策2、决策5）。
        /// 此处用弱类型占位，待任务 5.7 细化为强类型上下文。</para>
        /// </summary>
        public object Context { get; internal set; }

        /// <summary>
        /// 实例生命周期是否已执行过 OnCreate。用于保证 OnCreate 在单个实例上只执行一次（幂等）。
        /// <para>design.md 决策5：OnCreate 在单个实例上各执行一次。本标记由 <see cref="InvokeOnCreate"/> 设置，
        /// 重复调用时直接跳过，不重复执行业务回调。</para>
        /// </summary>
        private bool _isCreated;

        /// <summary>
        /// 实例生命周期 OnCreate 是否已执行（internal，供测试程序集验证生命周期次数契约）。
        /// </summary>
        internal bool IsCreated => _isCreated;

        /// <summary>
        /// 实例生命周期是否已执行过 OnDispose。用于保证 OnDispose 在单个实例上只执行一次（幂等）。
        /// <para>design.md 决策5：最终释放顺序为 Dispose Widgets -> OnDispose -> Descriptor.Detach ->
        /// Dispose GObject -> Release lease，OnDispose 各执行一次。本标记由 <see cref="InvokeOnDispose"/> 设置，
        /// 重复调用时直接跳过，不重复执行业务回调。</para>
        /// </summary>
        private bool _isDisposed;

        /// <summary>
        /// 实例生命周期 OnDispose 是否已执行（internal，供测试程序集验证生命周期次数契约与最终释放顺序）。
        /// </summary>
        internal bool IsDisposed => _isDisposed;

        /// <summary>
        /// 实例生命周期：首次创建回调，在 AttachContext -> Descriptor.Attach -> AttachWidgetTree 之后执行，仅执行一次。
        /// 同步回调，不得在此启动需要被 Show 等待的异步任务；业务异步任务应使用 <see cref="OpenCancellationToken"/>。
        /// </summary>
        protected virtual void OnCreate()
        {
        }

        /// <summary>
        /// 每轮打开生命周期：打开回调。每轮打开执行一次，在 <see cref="RegisterOpenEvents"/> 之后、<see cref="OnRefresh"/> 之前。
        /// 同步回调。
        /// </summary>
        protected virtual void OnOpen()
        {
        }

        /// <summary>
        /// 每轮打开生命周期：刷新回调。每个有效刷新请求执行前更新 <see cref="UserDatas"/> 与 <see cref="UserData"/> 后执行。
        /// 同步回调。
        /// </summary>
        protected virtual void OnRefresh()
        {
        }

        /// <summary>
        /// 临时隐藏回调。仅改变显示与输入状态，不结束本轮打开域（不取消 Open Token、不清事件）。
        /// 同步回调。
        /// </summary>
        protected virtual void OnHide()
        {
        }

        /// <summary>
        /// 每轮关闭生命周期：关闭回调。在取消 Open Token、清理打开事件之后执行。
        /// 同步回调。
        /// </summary>
        protected virtual void OnClose()
        {
        }

        /// <summary>
        /// 实例生命周期：最终释放回调，在 Dispose Widgets 之后、Descriptor.Detach 之前执行，仅执行一次。
        /// 同步回调。
        /// </summary>
        protected virtual void OnDispose()
        {
        }

        /// <summary>
        /// 每轮打开生命周期：注册本轮打开事件。每轮打开执行一次，在 OnOpen 之前。
        /// 复用 TEngine <c>UIBase.RegisterEvent</c> 的事件域语义，但与打开周期绑定，Close 时由 <see cref="ClearOpenEvents"/> 清理。
        /// 同步回调。
        /// </summary>
        protected virtual void RegisterOpenEvents()
        {
        }

        /// <summary>
        /// 每轮关闭生命周期：清理本轮打开事件。每轮关闭执行一次，与 RegisterOpenEvents 配对。
        /// 同步回调。
        /// </summary>
        protected virtual void ClearOpenEvents()
        {
        }

        /// <summary>
        /// 框架内部入口：在构造完成、OnCreate 之前注入运行时上下文。
        /// 由 FUIModule 在 attach 阶段调用，业务不应直接调用。
        /// </summary>
        /// <param name="context">运行时上下文，可为 null。</param>
        internal void AttachContext(object context)
        {
            Context = context;
        }

        /// <summary>
        /// 框架内部入口：触发实例生命周期 <see cref="OnCreate"/>（幂等，仅执行一次）。
        /// <para>由 FUIModule 在 <c>AttachContext -> Descriptor.Attach -> AttachWidgetTree</c> 之后调用,
        /// 保证 OnCreate 在单个实例上只执行一次（design.md 决策5：OnCreate 各执行一次）。
        /// 重复调用安全跳过，不重复执行业务回调。</para>
        /// <para>OnCreate 为 protected virtual，业务不应直接重写本入口；业务通过 override OnCreate 参与生命周期。
        /// 本入口负责幂等保护与调用顺序，不改变 OnCreate 的 protected 可见性。</para>
        /// </summary>
        internal void InvokeOnCreate()
        {
            // 幂等保护：已执行过 OnCreate 的实例不再重复执行（design.md 决策5：OnCreate 各执行一次）。
            if (_isCreated)
            {
                return;
            }

            _isCreated = true;
            OnCreate();
        }

        /// <summary>
        /// 框架内部入口：触发实例生命周期 <see cref="OnDispose"/>（幂等，仅执行一次）。
        /// <para>由 FUIModule 在最终释放阶段调用，位于 Dispose Widgets 之后、Descriptor.Detach 之前
        /// （design.md 决策5：最终释放顺序 Dispose Widgets -> OnDispose -> Descriptor.Detach ->
        /// Dispose GObject -> Release lease）。重复调用安全跳过，不重复执行业务回调。</para>
        /// <para>OnDispose 为 protected virtual，业务不应直接重写本入口；业务通过 override OnDispose 参与生命周期。
        /// 本入口负责幂等保护与调用顺序，不改变 OnDispose 的 protected 可见性。</para>
        /// <para>注意：本入口只触发 OnDispose 回调，不执行 Dispose GObject 与 Release lease。
        /// GObject Dispose 与租约释放由 FUIModule 在本入口返回后统一处理，保证释放顺序固定。</para>
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

        /// <summary>
        /// 框架内部入口：触发每轮打开生命周期 <see cref="RegisterOpenEvents"/>（每轮打开执行一次，在 OnOpen 之前）。
        /// <para>由 FUIModule 在创建 OpenCts 之后、<see cref="InvokeOnOpen"/> 之前调用
        /// （design.md 决策5：每轮打开 Create open CTS -> RegisterOpenEvents -> OnOpen -> OnRefresh）。
        /// 业务不应直接调用本入口，通过 override <see cref="RegisterOpenEvents"/> 参与生命周期。</para>
        /// </summary>
        internal void InvokeRegisterOpenEvents()
        {
            RegisterOpenEvents();
        }

        /// <summary>
        /// 框架内部入口：触发每轮打开生命周期 <see cref="OnOpen"/>（每轮打开执行一次，在 RegisterOpenEvents 之后、OnRefresh 之前）。
        /// <para>由 FUIModule 在 <see cref="InvokeRegisterOpenEvents"/> 之后、TransitionTo(Open) 之前调用
        /// （design.md 决策5）。业务不应直接调用本入口，通过 override <see cref="OnOpen"/> 参与生命周期。</para>
        /// </summary>
        internal void InvokeOnOpen()
        {
            OnOpen();
        }

        /// <summary>
        /// 框架内部入口：触发每轮打开生命周期 <see cref="OnRefresh"/>（每个有效刷新请求执行一次，在 SetUserDatas 之后）。
        /// <para>由 FUIModule 在 <see cref="ProcessRefreshQueue"/> 中 SetUserDatas 之后调用
        /// （spec: 每个刷新请求执行前 SHALL 更新 UserDatas 和 UserData，更新后执行同步刷新）。
        /// 业务不应直接调用本入口，通过 override <see cref="OnRefresh"/> 参与生命周期。</para>
        /// </summary>
        internal void InvokeOnRefresh()
        {
            OnRefresh();
        }

        /// <summary>
        /// 框架内部入口：触发临时隐藏生命周期 <see cref="OnHide"/>（仅改变显示与输入状态，不结束本轮打开域）。
        /// <para>由 FUIModule 在 <see cref="HideEntryCore"/> 中切换 visible/touchable=false 之后、
        /// TransitionTo(Hidden) 之前调用
        /// （design.md 决策5：临时隐藏 OnHide -> visible/touchable false，保留 open CTS 与事件域；
        /// spec "Hide 不结束打开域"：系统 SHALL 执行同步 OnHide 并改变显示与输入状态）。
        /// 业务不应直接调用本入口，通过 override <see cref="OnHide"/> 参与生命周期。</para>
        /// </summary>
        internal void InvokeOnHide()
        {
            OnHide();
        }

        /// <summary>
        /// 框架内部入口：触发每轮关闭生命周期 <see cref="ClearOpenEvents"/>（每轮关闭执行一次，与 RegisterOpenEvents 配对）。
        /// <para>由 FUIModule 在 Close 流程中 CancelOpenCts 之后、<see cref="InvokeOnClose"/> 之前调用
        /// （design.md 决策5：每轮关闭 Cancel open CTS -> ClearOpenEvents -> OnClose）。
        /// 业务不应直接调用本入口，通过 override <see cref="ClearOpenEvents"/> 参与生命周期。</para>
        /// </summary>
        internal void InvokeClearOpenEvents()
        {
            ClearOpenEvents();
        }

        /// <summary>
        /// 框架内部入口：触发每轮关闭生命周期 <see cref="OnClose"/>（每轮关闭执行一次，在 ClearOpenEvents 之后）。
        /// <para>由 FUIModule 在 Close 流程中 <see cref="InvokeClearOpenEvents"/> 之后调用
        /// （design.md 决策5）。业务不应直接调用本入口，通过 override <see cref="OnClose"/> 参与生命周期。</para>
        /// </summary>
        internal void InvokeOnClose()
        {
            OnClose();
        }

        /// <summary>
        /// 框架内部入口：在每轮打开创建打开 CTS 后设置当前 OpenCancellationToken。
        /// 由 FUIModule 调用，业务不应直接调用。
        /// </summary>
        /// <param name="token">本轮打开的取消令牌。</param>
        internal void SetOpenCancellationToken(CancellationToken token)
        {
            OpenCancellationToken = token;
        }

        /// <summary>
        /// 框架内部入口：在每个有效刷新请求执行前更新用户参数。
        /// 复用 TEngine <c>UIWindow</c> 的 <c>base._userDatas = userDatas</c> 更新语义。
        /// </summary>
        /// <param name="userDatas">用户参数数组，可为 null。</param>
        internal void SetUserDatas(object[] userDatas)
        {
            UserDatas = userDatas;
        }
    }
}
