using System;
using FairyGUI;

namespace GameFUI
{
    /// <summary>
    /// FairyGUI 受管理 Window/Widget 的显式运行时描述。
    /// <para>本描述为不可变值类型，创建后字段不可修改。由 owner 在初始化阶段显式注册到
    /// <see cref="FUIBindingRegistry"/>（任务 3.4），是运行时创建受管理对象的唯一描述来源。</para>
    /// <para>设计依据：design.md 决策2，显式 Descriptor Registry 是唯一运行时注册来源。
    /// 全局 <c>UIObjectFactory</c> 的 creator 只捕获 URL 并查询当前活动 Registry，
    /// 不得通过闭包捕获业务 Module 或其他可释放运行时对象。</para>
    /// </summary>
    public readonly struct FUIDescriptor
    {
        /// <summary>
        /// 组件 URL，格式如 <c>ui://UIBattle/BattleStartPanel</c>，来自生成类型常量 <c>URL</c>。
        /// </summary>
        public readonly string URL;

        /// <summary>
        /// FairyGUI 包名，来自生成类型常量 <c>PkgName</c>。
        /// </summary>
        public readonly string PackageName;

        /// <summary>
        /// 组件资源名，来自生成类型常量 <c>ResName</c>。
        /// </summary>
        public readonly string ComponentName;

        /// <summary>
        /// 唯一 owner 类型。未来为所属业务 Module 类型，本 change 使用测试 owner 类型验证相同契约。
        /// 每个 Package 只能有一个 owner，由注册阶段校验。
        /// </summary>
        public readonly Type OwnerType;

        /// <summary>
        /// 最终业务类型，用于创建实例。注册顺序固定为：本包生成 Binder、最终 Widget、最终 Window，
        /// 后注册的最终 creator 覆盖生成类型，使创建结果为最末端业务类型而非生成基类。
        /// </summary>
        public readonly Type TargetType;

        /// <summary>
        /// 窗口所属层级。Widget 通常使用 Normal。
        /// </summary>
        public readonly FUILayer Layer;

        /// <summary>
        /// 是否为全屏窗口。全屏窗口按窗口栈把被遮挡下层窗口的 visible 和 touchable 同时置 false，
        /// 但保留其 Stage 归属。
        /// </summary>
        public readonly bool FullScreen;

        /// <summary>
        /// 关闭后的缓存策略。默认为 None，普通 Close 最终释放实例；显式声明 Cache 时才保留实例和包租约。
        /// </summary>
        public readonly FUICacheMode CacheMode;

        /// <summary>
        /// 安全区容器策略。Full 进入层容器 Full 子容器，Safe 进入 Safe 子容器，
        /// 在分辨率、方向或 safeArea 变化时重算。
        /// </summary>
        public readonly FUISafeAreaMode SafeAreaMode;

        /// <summary>
        /// 无状态 creator 委托。只接收 URL 并创建对象，不得捕获业务 Module、Presenter、资源句柄
        /// 或其他可释放运行时对象。实际运行时由全局 <c>UIObjectFactory</c> creator 只捕获 URL
        /// 并查询当前活动 Registry 完成；本字段供 Registry 在绑定阶段写入全局工厂使用。
        /// </summary>
        public readonly Func<string, GComponent> Creator;

        /// <summary>
        /// 可选 Attach 回调。FUIModule 在对象构造完成后、任何业务生命周期（OnCreate）开始前调用，
        /// 用于通过可清理运行时上下文附加业务依赖。可为 null 表示无附加逻辑。
        /// </summary>
        public readonly Action<GComponent, object> Attach;

        /// <summary>
        /// 可选 Detach 回调。FUIModule 在最终 Dispose 时调用，用于清理 Attach 阶段附加的业务依赖。
        /// 可为 null 表示无清理逻辑。
        /// </summary>
        public readonly Action<GComponent, object> Detach;

        /// <summary>
        /// 初始化描述的全部字段。所有字段均为 readonly，构造后不可修改。
        /// </summary>
        /// <param name="url">组件 URL。</param>
        /// <param name="packageName">包名。</param>
        /// <param name="componentName">组件资源名。</param>
        /// <param name="ownerType">唯一 owner 类型。</param>
        /// <param name="targetType">最终业务类型。</param>
        /// <param name="layer">层级。</param>
        /// <param name="fullScreen">是否全屏。</param>
        /// <param name="cacheMode">缓存策略。</param>
        /// <param name="safeAreaMode">安全区策略。</param>
        /// <param name="creator">无状态 creator，只接收 URL 并创建对象。</param>
        /// <param name="attach">可选 Attach 回调，可为 null。</param>
        /// <param name="detach">可选 Detach 回调，可为 null。</param>
        public FUIDescriptor(
            string url,
            string packageName,
            string componentName,
            Type ownerType,
            Type targetType,
            FUILayer layer,
            bool fullScreen,
            FUICacheMode cacheMode,
            FUISafeAreaMode safeAreaMode,
            Func<string, GComponent> creator,
            Action<GComponent, object> attach = null,
            Action<GComponent, object> detach = null)
        {
            URL = url;
            PackageName = packageName;
            ComponentName = componentName;
            OwnerType = ownerType;
            TargetType = targetType;
            Layer = layer;
            FullScreen = fullScreen;
            CacheMode = cacheMode;
            SafeAreaMode = safeAreaMode;
            Creator = creator;
            Attach = attach;
            Detach = detach;
        }
    }
}
