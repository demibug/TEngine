namespace GameFUI
{
    /// <summary>
    /// FairyGUI 窗口层级。
    /// </summary>
    public enum FUILayer
    {
        Background = 0,
        Normal = 1,
        Popup = 2,
        Guide = 3,
        Tips = 4,
        System = 5,
    }

    /// <summary>
    /// FairyGUI 窗口运行状态。
    /// </summary>
    public enum FUIWindowState
    {
        Absent = 0,
        Loading = 1,
        Opening = 2,
        Open = 3,
        Hidden = 4,
        Closing = 5,
        Cached = 6,
        Disposed = 7,
    }

    /// <summary>
    /// 窗口关闭后的缓存策略。
    /// </summary>
    public enum FUICacheMode
    {
        None = 0,
        Cache = 1,
    }

    /// <summary>
    /// FairyGUI 包卸载策略。
    /// </summary>
    public enum FUIPackageUnloadPolicy
    {
        KeepUntilShutdown = 0,
        Delayed = 1,
    }

    /// <summary>
    /// 窗口使用的安全区容器。
    /// </summary>
    public enum FUISafeAreaMode
    {
        Full = 0,
        Safe = 1,
    }
}
