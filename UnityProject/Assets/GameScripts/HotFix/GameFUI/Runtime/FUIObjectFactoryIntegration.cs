using System.Collections.Generic;
using FairyGUI;

namespace GameFUI
{
    /// <summary>
    /// 将 <see cref="FUIBindingRegistry"/> 中已注册的组件 URL 安装到全局
    /// <c>FairyGUI.UIObjectFactory</c>，并提供只捕获 URL 的无状态 creator。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策2 与任务 3.5。
    /// <para>
    /// 全局 <c>UIObjectFactory</c> 是进程级静态注册表（见
    /// <c>Assets/ThirdParty/FairyGUI/Scripts/UI/UIObjectFactory.cs</c>），其
    /// <c>SetPackageItemExtension(string url, GComponentCreator creator)</c>
    /// 接收的 creator 委托签名为 <c>delegate GComponent GComponentCreator()</c>（无参），
    /// 因此 URL 只能通过闭包捕获。本类型保证写入全局工厂的 creator <b>只捕获 URL 字符串</b>，
    /// 不捕获 <see cref="FUIBindingRegistry"/> 实例、业务 Module、资源句柄或其他可释放运行时对象；
    /// creator 在被调用时再通过 <see cref="ActiveRegistry"/> 静态访问器查询当前活动 Registry。
    /// </para>
    /// <para>
    /// 这样做的目的：模块 <c>Shutdown</c> 清空本地 Registry 后，全局 creator 仍然存在
    /// （<c>UIObjectFactory</c> 没有按 URL 注销接口），但因其只依赖 URL 与活动 Registry 静态点，
    /// 迟到的 creator 调用 SHALL 因 Registry 非活动而抛 <see cref="FUIException"/>，明确失败，
    /// 不得创建持有旧业务依赖的窗口（spec fairygui-window-runtime：Shutdown 后全局 creator 被调用）。
    /// </para>
    /// <para>
    /// 边界约束：本类型不调用全局 <c>UIObjectFactory.Clear()</c>，以免清除其他 FairyGUI 扩展
    /// （design.md Risks；spec：模块退出完整清理——模块不得调用会清除其他 FairyGUI 扩展的全局
    /// <c>UIObjectFactory.Clear()</c>）。退出时仅清理本类型持有的活动 Registry 静态引用。
    /// </para>
    /// <para>
    /// 边界约束：本类型不 using 且不反向依赖 GameLogic/GamePlay/GameBattle 命名空间。
    /// </para>
    /// </remarks>
    public static class FUIObjectFactoryIntegration
    {
        /// <summary>
        /// 当前活动 <see cref="FUIBindingRegistry"/> 的静态引用。
        /// <para>全局 creator 不通过闭包捕获 Registry 实例，而在被调用时通过本静态访问器查询，
        /// 从而避免捕获可释放运行时对象（任务 3.5：creator 只捕获 URL，不捕获 Registry 实例）。</para>
        /// <para>装配方在冻结 Registry 后通过 <see cref="InstallPackageItemExtensions"/> 设置本引用；
        /// 模块 <c>Shutdown</c> 通过 <see cref="ClearActiveRegistry"/> 清空本引用。
        /// 清空后迟到的全局 creator 调用将因无活动 Registry 而明确失败。</para>
        /// </summary>
        private static FUIBindingRegistry _activeRegistry;

        /// <summary>
        /// 获取当前活动 <see cref="FUIBindingRegistry"/>，用于全局 creator 在创建时查询描述。
        /// </summary>
        /// <remarks>
        /// 返回值可能为 null（尚未安装或已被 <see cref="ClearActiveRegistry"/> 清空）。
        /// 调用方（全局 creator）必须同时校验返回值非空且 <see cref="FUIBindingRegistry.IsActive"/>
        /// 为 true，否则视为非活动并抛 <see cref="FUIException"/>。
        /// </remarks>
        internal static FUIBindingRegistry ActiveRegistry => _activeRegistry;

        /// <summary>
        /// 为已注册的全部组件 URL 安装全局无状态 creator，并登记当前活动 Registry。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 本方法遍历 <paramref name="urls"/> 中的每个 URL，为每个 URL 调用
        /// <c>UIObjectFactory.SetPackageItemExtension(url, GComponentCreator)</c>，
        /// 安装只捕获该 URL 的 creator。creator 在被 FairyGUI 调用时，通过
        /// <see cref="ActiveRegistry"/> 查询当前活动 Registry 的描述，再用描述的
        /// <see cref="FUIDescriptor.Creator"/> 创建对象。
        /// </para>
        /// <para>
        /// 由于 <c>FUIBindingRegistry</c> 不向本类型公开 URL 枚举接口，且任务 3.5 禁止修改
        /// <c>FUIBindingRegistry.cs</c>，由装配方（本 change 为 PlayMode 测试 harness，
        /// 生产组合根由后续 change 接管）显式传入已注册 URL 集合。装配方应在所有 owner 完成注册、
        /// Registry 冻结后调用本方法，且必须在首次创建任何受管理对象前完成安装
        /// （spec：首次创建任何受管理 FairyGUI 对象前完成显式注册）。
        /// </para>
        /// <para>
        /// 本方法会将 <paramref name="registry"/> 登记为活动 Registry。若已有活动 Registry 且与
        /// 传入实例不同，将抛 <see cref="FUIException"/>，避免多个 Registry 交叉安装造成 creator
        /// 查询到错误的描述表。重复以同一实例安装是幂等的。
        /// </para>
        /// <para>
        /// 注意：本方法不调用全局 <c>UIObjectFactory.Clear()</c>。
        /// </para>
        /// </remarks>
        /// <param name="registry">已冻结的活动 Registry，creator 将通过静态访问器查询它。</param>
        /// <param name="urls">已注册的组件 URL 集合，每个 URL 形如 <c>ui://UIBattle/BattleStartPanel</c>。</param>
        /// <exception cref="FUIException"><paramref name="registry"/> 为 null、已存在不同的活动 Registry、
        /// 或 <paramref name="urls"/> 为 null。</exception>
        public static void InstallPackageItemExtensions(FUIBindingRegistry registry, IEnumerable<string> urls)
        {
            if (registry == null)
            {
                throw new FUIException("安装全局 UIObjectFactory 扩展失败：活动 Registry 不能为空。");
            }

            if (urls == null)
            {
                throw new FUIException("安装全局 UIObjectFactory 扩展失败：URL 集合不能为空。");
            }

            // 登记活动 Registry。若已有不同实例，拒绝交叉安装，避免 creator 查询到错误描述表。
            if (_activeRegistry != null && !ReferenceEquals(_activeRegistry, registry))
            {
                throw new FUIException(
                    "安装全局 UIObjectFactory 扩展失败：已存在不同的活动 Registry，" +
                    "不得在未清理前一个 Registry 的情况下安装新的 Registry。请先调用 ClearActiveRegistry。");
            }

            _activeRegistry = registry;

            foreach (string url in urls)
            {
                if (string.IsNullOrEmpty(url))
                {
                    // 跳过空 URL，不安装。空 URL 不应出现在已注册集合中，此处防御性处理。
                    continue;
                }

                // 为每个 URL 安装只捕获该 URL 的无状态 creator。
                // CreateUrlOnlyCreator 返回的闭包仅捕获 url 字符串，不捕获 registry 实例，
                // 在被调用时通过 ActiveRegistry 静态访问器查询当前活动 Registry。
                UIObjectFactory.SetPackageItemExtension(url, CreateUrlOnlyCreator(url));
            }
        }

        /// <summary>
        /// 清空本类型持有的活动 Registry 静态引用，使迟到的全局 creator 调用明确失败。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 模块 <c>Shutdown</c> 时调用：先由 <see cref="FUIBindingRegistry.Shutdown"/> 清空本地描述表，
        /// 再调用本方法清空活动 Registry 静态引用。此后全局 creator 仍存在于 FairyGUI 静态字典中，
        /// 但其被调用时将因 <see cref="ActiveRegistry"/> 为 null 而抛 <see cref="FUIException"/>，
        /// 不得创建持有旧业务依赖的窗口。
        /// </para>
        /// <para>
        /// 本方法 <b>不</b> 调用全局 <c>UIObjectFactory.Clear()</c>，以免清除其他 FairyGUI 扩展
        /// （design.md Risks；spec：模块退出完整清理）。
        /// </para>
        /// <para>
        /// 重复调用是幂等的。
        /// </para>
        /// </remarks>
        public static void ClearActiveRegistry()
        {
            _activeRegistry = null;
        }

        /// <summary>
        /// 构造只捕获指定 URL 的无状态 <see cref="UIObjectFactory.GComponentCreator"/>。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 返回的 creator 闭包<b>仅捕获 <paramref name="url"/> 字符串</b>，不捕获
        /// <see cref="FUIBindingRegistry"/> 实例、Module 或其他可释放运行时对象
        /// （任务 3.5：creator 只捕获 URL，不捕获 Registry 实例、Module 或其他可释放对象）。
        /// </para>
        /// <para>
        /// creator 被 FairyGUI 调用时，通过 <see cref="ActiveRegistry"/> 查询当前活动 Registry：
        /// <list type="bullet">
        /// <item>Registry 为 null（未安装或已清空）或 <see cref="FUIBindingRegistry.IsActive"/> 为 false
        /// （未冻结或已 Shutdown）→ 抛 <see cref="FUIException"/>，明确失败，不创建对象。</item>
        /// <item>Registry 活动但 <see cref="FUIBindingRegistry.TryGetDescriptor(string, out FUIDescriptor)"/>
        /// 未找到 URL 对应描述 → 抛 <see cref="FUIException"/>，给出 URL 诊断。</item>
        /// <item>查到描述 → 使用 <see cref="FUIDescriptor.Creator"/> 创建对象并返回。</item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <param name="url">组件 URL，闭包仅捕获此字符串。</param>
        /// <returns>只捕获 URL 的无状态 creator 委托。</returns>
        private static UIObjectFactory.GComponentCreator CreateUrlOnlyCreator(string url)
        {
            // 闭包只捕获 url（字符串，不可释放），不捕获 registry 实例。
            return () =>
            {
                FUIBindingRegistry registry = _activeRegistry;

                // Registry 非活动（未安装、未冻结或已 Shutdown）→ 明确失败，不创建对象。
                if (registry == null || !registry.IsActive)
                {
                    throw new FUIException(
                        $"全局 UIObjectFactory creator 被调用时 Registry 非活动：url='{url}'。" +
                        "可能原因：模块尚未注册/冻结，或已 Shutdown 清空本地 Registry。" +
                        "不得创建持有旧业务依赖的窗口。");
                }

                // 通过 URL 查询当前活动 Registry 的描述。
                if (!registry.TryGetDescriptor(url, out FUIDescriptor descriptor))
                {
                    throw new FUIException(
                        $"全局 UIObjectFactory creator 被调用时未在活动 Registry 中找到 URL 描述：url='{url}'。" +
                        "可能原因：URL 未注册或已被 Shutdown 清空。");
                }

                // 使用描述的无状态 creator 创建对象。
                // descriptor.Creator 为 Func<string, GComponent>，接收 URL 并创建最终业务类型实例。
                return descriptor.Creator(url);
            };
        }
    }
}
