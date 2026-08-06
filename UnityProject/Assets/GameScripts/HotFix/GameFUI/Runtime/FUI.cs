using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;

namespace GameFUI
{
    /// <summary>
    /// GameFUI 静态门面。
    /// <para>业务通过 <see cref="ShowAsync{T}"/>、<see cref="Hide"/>、<see cref="Close"/> 等静态方法访问 FairyGUI 窗口能力，
    /// 转发到已注册的 <see cref="IFUIModule"/> 实例。</para>
    /// <para>设计依据：design.md 决策1，<see cref="Module"/> getter 只返回已经注册的实例，未注册时抛出明确异常，
    /// 不得隐式注册或创建默认实例；第二次注册在进入 <c>ModuleSystem.RegisterModule</c> 前被拒绝。</para>
    /// <para><see cref="RegisterModule"/> 由任务 5.1 实现：在进入 ModuleSystem 前做重复注册保护，
    /// 构造 <see cref="FUIModule"/> 并赋值 <see cref="_module"/>；模块退出由 <see cref="FUIModule.Shutdown"/>
    /// 清空静态缓存。</para>
    /// </summary>
    public static class FUI
    {
        /// <summary>
        /// 已注册的模块实例。未注册时抛 <see cref="FUIException"/>，不隐式注册。
        /// </summary>
        public static IFUIModule Module
        {
            get
            {
                if (_module == null)
                {
                    throw new FUIException("GameFUI 模块尚未注册，请先调用 FUI.RegisterModule。");
                }

                return _module;
            }
        }

        private static IFUIModule _module;

        /// <summary>
        /// 注册 GameFUI 模块。
        /// <para>由装配方（本 change 为 PlayMode 测试 harness，生产组合根由后续 change 接管）显式传入资源能力与选项。
        /// 第二次注册在进入 <c>ModuleSystem.RegisterModule</c> 前被拒绝，避免旧实例残留在更新队列
        /// （design.md 决策1）。</para>
        /// <para>本 change 不修改生产 GameLogic 组合根，因此本方法不实际调用
        /// <c>ModuleSystem.RegisterModule</c>，而是直接构造 <see cref="FUIModule"/> 并赋值 <see cref="_module"/>；
        /// 生产组合根的 ModuleSystem 集成由后续 change 负责。</para>
        /// </summary>
        /// <param name="resourceModule">TEngine 资源管理能力，不得为 null；由模块内部包装为最窄资源 provider。</param>
        /// <param name="options">注册选项，为 null 时使用默认策略（KeepUntilShutdown）。</param>
        /// <exception cref="FUIException">模块已注册（重复注册保护）或 <paramref name="resourceModule"/> 为 null。</exception>
        public static void RegisterModule(IResourceModule resourceModule, FUIOptions options = null)
        {
            // 重复注册保护：在进入 ModuleSystem.RegisterModule 前拒绝第二次注册，
            // 避免旧实例残留在更新队列（design.md 决策1；spec：模块访问不得隐式创建或重复注册模块）。
            if (_module != null)
            {
                throw new FUIException("GameFUI 模块已注册，禁止重复注册。如需重新注册，请先调用模块 Shutdown。");
            }

            if (resourceModule == null)
            {
                throw new FUIException("注册 GameFUI 模块失败：resourceModule 不能为空。");
            }

            // 在内部把公开传入的 IResourceModule 包装成最窄资源 provider（design.md 决策1）。
            YooAssetFUIResourceProvider provider = new YooAssetFUIResourceProvider(resourceModule);

            // 构造 FUIModule 实例并存储。本 change 不调用 ModuleSystem.RegisterModule，
            // 生产组合根的 ModuleSystem 集成由后续 change 接管。
            _module = new FUIModule(provider, options);
        }

        /// <summary>
        /// internal 测试入口：直接注入内存 <see cref="IFUIResourceProvider"/> 注册模块，绕过 IResourceModule 包装。
        /// </summary>
        /// <param name="provider">可控失败/取消的内存资源 provider，不得为 null。</param>
        /// <param name="options">注册选项，为 null 时使用默认策略。</param>
        /// <remarks>
        /// 仅通过 internal 测试入口为测试程序集注入可控失败/取消的内存 provider，不增加第二个公开注册重载
        /// （design.md 决策1）。测试程序集通过 InternalsVisibleTo 访问本方法。
        /// 本方法同样在进入 ModuleSystem 前做重复注册保护。
        /// </remarks>
        /// <exception cref="FUIException">模块已注册（重复注册保护）或 <paramref name="provider"/> 为 null。</exception>
        internal static void RegisterModuleForTesting(IFUIResourceProvider provider, FUIOptions options = null)
        {
            // 重复注册保护：与公开 RegisterModule 一致，在进入 ModuleSystem 前拒绝第二次注册。
            if (_module != null)
            {
                throw new FUIException("GameFUI 模块已注册，禁止重复注册。如需重新注册，请先调用模块 Shutdown。");
            }

            if (provider == null)
            {
                throw new FUIException("注册 GameFUI 模块失败：测试 provider 不能为空。");
            }

            // 直接注入内存 provider，不经过 IResourceModule 包装，使测试可注入可控失败/取消。
            _module = new FUIModule(provider, options);
        }

        /// <summary>
        /// 清空静态模块缓存，供 <see cref="FUIModule.Shutdown"/> 在模块退出时调用。
        /// </summary>
        /// <remarks>
        /// Shutdown 后 <see cref="Module"/> getter 将因 <see cref="_module"/> 为 null 而抛“尚未注册”异常，
        /// 避免残留实例被访问（design.md 决策1：Shutdown 清空静态缓存）。
        /// 本方法为 internal，仅供同程序集的 <see cref="FUIModule.Shutdown"/> 调用，不对外公开。
        /// </remarks>
        internal static void ClearModuleForShutdown()
        {
            _module = null;
        }

        /// <summary>
        /// 打开窗口（无调用方取消令牌）。转发到 <see cref="Module.ShowAsync{T}"/>。
        /// <para>使用模块 lifetime token 驱动框架工作；成功返回非空 Open 窗口。</para>
        /// </summary>
        /// <param name="args">零个或多个用户参数。</param>
        /// <typeparam name="T">最终业务窗口类型。</typeparam>
        /// <returns>已处于 Open 状态的非空业务窗口实例。</returns>
        public static UniTask<T> ShowAsync<T>(params object[] args) where T : FUIWindow
        {
            return Module.ShowAsync<T>(args);
        }

        /// <summary>
        /// 打开窗口（带调用方取消令牌）。转发到 <see cref="Module.ShowAsync{T}"/>。
        /// <para>令牌只取消该调用方的等待，不影响其他调用方共享的包加载。</para>
        /// </summary>
        /// <param name="cancellationToken">调用方取消令牌。</param>
        /// <param name="args">零个或多个用户参数。</param>
        /// <typeparam name="T">最终业务窗口类型。</typeparam>
        /// <returns>已处于 Open 状态的非空业务窗口实例。</returns>
        public static UniTask<T> ShowAsync<T>(CancellationToken cancellationToken, params object[] args) where T : FUIWindow
        {
            return Module.ShowAsync<T>(cancellationToken, args);
        }

        /// <summary>
        /// 隐藏指定类型的窗口。转发到 <see cref="Module.Hide{T}"/>。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        public static void Hide<T>() where T : FUIWindow
        {
            Module.Hide<T>();
        }

        /// <summary>
        /// 隐藏指定窗口实例。转发到 <see cref="Module.Hide(FUIWindow)"/>。
        /// </summary>
        /// <param name="window">要隐藏的窗口实例。</param>
        public static void Hide(FUIWindow window)
        {
            Module.Hide(window);
        }

        /// <summary>
        /// 关闭指定类型的窗口。转发到 <see cref="Module.Close{T}"/>。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        public static void Close<T>() where T : FUIWindow
        {
            Module.Close<T>();
        }

        /// <summary>
        /// 关闭指定窗口实例。转发到 <see cref="Module.Close(FUIWindow)"/>。
        /// </summary>
        /// <param name="window">要关闭的窗口实例。</param>
        public static void Close(FUIWindow window)
        {
            Module.Close(window);
        }

        /// <summary>
        /// 查询指定类型窗口的当前实例。仅转发到已注册模块，不触发模块注册、窗口创建或显示。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>当前存活或缓存的实例；未创建、仍在加载或已释放时返回 null。</returns>
        public static T GetWindow<T>() where T : FUIWindow
        {
            return Module.GetWindow<T>();
        }

        /// <summary>
        /// 查询指定类型窗口是否存在活动条目。仅转发到已注册模块，不触发模块注册、窗口创建或显示。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>Loading、Opening、Open、Hidden、Closing 或 Cached 状态返回 true，否则返回 false。</returns>
        public static bool HasWindow<T>() where T : FUIWindow
        {
            return Module.HasWindow<T>();
        }
    }
}
