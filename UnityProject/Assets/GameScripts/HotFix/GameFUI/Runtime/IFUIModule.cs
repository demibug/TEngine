using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFUI
{
    /// <summary>
    /// GameFUI 公开模块接口。
    /// <para>定义 FairyGUI 窗口的 Show、Hide、Close、查询、Registry 冻结与模块退出清理的公开契约。
    /// 本接口由 <see cref="FUIModule"/>（任务 5.1）实现，<see cref="FUI"/> 静态门面转发到此接口；
    /// 本任务只固定接口形态，不提供实现。</para>
    /// <para>设计依据：design.md 决策1（公开模块注册形态）、决策4（Show 公开契约由显式状态与操作版本驱动）。
    /// 公开等待接口返回非空业务窗口；调用取消使用 <see cref="OperationCanceledException"/>，
    /// 其余失败包装为包含窗口类型、URL、包名和状态的 <see cref="FUIException"/>。</para>
    /// </summary>
    public interface IFUIModule
    {
        /// <summary>
        /// 打开窗口（无调用方取消令牌）。
        /// <para>使用模块 lifetime token 驱动框架加载与创建工作；成功表示包和依赖资源已就绪、
        /// 最终类型已构造、上下文与 Widget 已 Attach、同步 OnCreate/OnOpen/OnRefresh 已完成且窗口处于 Open。
        /// Show 不等待窗口自行启动的业务异步任务（spec: Show 公开接口和完成边界稳定）。</para>
        /// </summary>
        /// <param name="args">零个或多个用户参数，由框架在每个有效刷新请求执行前更新到窗口的 UserDatas/UserData。</param>
        /// <typeparam name="T">最终业务窗口类型，必须为 <see cref="FUIWindow"/> 子类且已注册。</typeparam>
        /// <returns>已处于 Open 状态的非空业务窗口实例。</returns>
        /// <exception cref="OperationCanceledException">模块 lifetime 被取消（如 Shutdown）。</exception>
        /// <exception cref="FUIException">注册缺失、包资源、对象构造或生命周期失败。</exception>
        UniTask<T> ShowAsync<T>(params object[] args) where T : FUIWindow;

        /// <summary>
        /// 打开窗口（带调用方取消令牌）。
        /// <para>令牌只取消该调用方的等待，不得取消其他调用方共享的包加载或对象创建
        /// （spec: Show 操作可等待且错误明确；design.md 决策4）。</para>
        /// </summary>
        /// <param name="cancellationToken">调用方取消令牌，只影响本次等待。</param>
        /// <param name="args">零个或多个用户参数。</param>
        /// <typeparam name="T">最终业务窗口类型，必须为 <see cref="FUIWindow"/> 子类且已注册。</typeparam>
        /// <returns>已处于 Open 状态的非空业务窗口实例。</returns>
        /// <exception cref="OperationCanceledException">调用方或模块 lifetime 被取消。</exception>
        /// <exception cref="FUIException">注册缺失、包资源、对象构造或生命周期失败。</exception>
        UniTask<T> ShowAsync<T>(CancellationToken cancellationToken, params object[] args) where T : FUIWindow;

        /// <summary>
        /// 隐藏指定类型的窗口。
        /// <para>仅改变显示与输入状态（同步 OnHide），不结束本轮打开域：不取消 Open Token、不清事件、不释放实例
        /// （spec: 窗口生命周期次数确定；design.md 决策5）。</para>
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        void Hide<T>() where T : FUIWindow;

        /// <summary>
        /// 隐藏指定窗口实例。
        /// </summary>
        /// <param name="window">要隐藏的窗口实例。</param>
        void Hide(FUIWindow window);

        /// <summary>
        /// 关闭指定类型的窗口。
        /// <para>递增 operation version 使旧操作完成后只能回滚；按缓存策略决定是否最终释放实例：
        /// 默认 None 执行 OnClose 后最终释放，显式 Cache 保留实例与包租约（spec: 默认关闭即释放且缓存显式启用）。</para>
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        void Close<T>() where T : FUIWindow;

        /// <summary>
        /// 关闭指定窗口实例。
        /// </summary>
        /// <param name="window">要关闭的窗口实例。</param>
        void Close(FUIWindow window);

        /// <summary>
        /// 查询指定类型窗口的当前实例。
        /// <para>返回当前存活或缓存的实例，未创建时返回 null。仅查询，不触发创建或显示。</para>
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>当前实例，未创建时为 null。</returns>
        T GetWindow<T>() where T : FUIWindow;

        /// <summary>
        /// 查询指定类型窗口是否存在当前实例（存活或缓存）。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>存在返回 true，否则 false。</returns>
        bool HasWindow<T>() where T : FUIWindow;

        /// <summary>
        /// 冻结绑定注册表。
        /// <para>所有 owner 完成显式注册后由装配方调用；冻结后新增或冲突注册直接报错
        /// （design.md 决策2：显式 Descriptor Registry 是唯一运行时注册来源）。
        /// 首次创建任何受管理对象前必须完成冻结。</para>
        /// </summary>
        void FreezeBindings();

        /// <summary>
        /// 模块退出清理。
        /// <para>取消所有进行中的打开操作，按反向顺序关闭并释放窗口，执行 detach，清理本地描述、owner、
        /// 活动 Registry 和静态模块缓存，并把所持包租约交还资源管理能力。不得调用会影响其他 FairyGUI 扩展的
        /// 全局 <c>UIObjectFactory.Clear()</c>（spec: 模块退出完整清理）。</para>
        /// </summary>
        void Shutdown();
    }
}
