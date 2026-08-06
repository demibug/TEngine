namespace GameFUI
{
    /// <summary>
    /// GameFUI 模块注册选项。
    /// <para>由 <see cref="FUI.RegisterModule"/> 在装配阶段显式传入，描述包卸载策略与延迟时间等运行期配置。
    /// 本类为可空配置对象：<c>null</c> 表示使用默认策略（KeepUntilShutdown），不强制要求调用方构造。</para>
    /// <para>设计依据：design.md 决策1，公开模块注册形态固定为
    /// <c>FUI.RegisterModule(IResourceModule resourceModule, FUIOptions options = null)</c>；
    /// 决策8，首版允许 <see cref="FUIPackageUnloadPolicy.KeepUntilShutdown"/>，稳定后再启用 Delayed。</para>
    /// </summary>
    public sealed class FUIOptions
    {
        /// <summary>
        /// 包卸载策略。默认为 <see cref="FUIPackageUnloadPolicy.KeepUntilShutdown"/>，
        /// 即包存活到模块 Shutdown；选择 <see cref="FUIPackageUnloadPolicy.Delayed"/> 时
        /// 使用 <see cref="UnloadDelaySeconds"/> 控制零引用后的延迟卸载时间。
        /// </summary>
        public FUIPackageUnloadPolicy UnloadPolicy { get; set; } = FUIPackageUnloadPolicy.KeepUntilShutdown;

        /// <summary>
        /// Delayed 策略下零引用后的延迟卸载时间（秒）。仅在
        /// <see cref="UnloadPolicy"/> 为 <see cref="FUIPackageUnloadPolicy.Delayed"/> 时生效。
        /// 延迟期内重新 Acquire 通过递增待卸载版本取消旧卸载任务（design.md 决策8）。
        /// </summary>
        public float UnloadDelaySeconds { get; set; } = 5f;
    }
}
