namespace GameBattle
{
    /// <summary>
    /// 战斗模块公共操作稳定错误码（task 2.4）。
    /// </summary>
    /// <remarks>
    /// <para><b>设计依据（specs/battle-runtime-lifecycle/spec.md + 决策 0.7）：</b></para>
    /// <para>预期状态、配置、资源错误 MUST 返回结构化结果而非异常，调用方不得依赖异常文本判断失败原因
    /// （spec：“预期失败 MUST 返回结构化结果而非异常”）。本枚举提供稳定、可编程判断的错误码，
    /// 覆盖三类预期失败：</para>
    /// <list type="bullet">
    /// <item><b>预期状态错误</b>：Start/Restart/Exit 在不合法状态下调用（AlreadyActive、NotSettling、Faulted 等）。</item>
    /// <item><b>配置错误</b>：配置缺失、校验失败、版本/hash 不匹配（task 3.5 BattleConfigValidator 产生）。</item>
    /// <item><b>资源错误</b>：Scene、UI、Prefab 或其他战斗资源加载失败或缺失（task 7.6 资源接入）。</item>
    /// </list>
    /// <para><b>稳定性约束：</b></para>
    /// <list type="bullet">
    /// <item>已发布枚举值名称与数值不得变更，调用方以此做程序化判断。</item>
    /// <item>新增错误码只能追加到末尾，不得插入或重排已有值。</item>
    /// <item><see cref="None"/> 固定为 0，表示成功；调用方应优先判断 <c>IsSuccess</c> 而非枚举值大小。</item>
    /// </list>
    /// <para><b>与异常的关系：</b></para>
    /// <list type="bullet">
    /// <item>预期失败使用本错误码 + <see cref="BattleOperationResult"/> 返回，不抛异常。</item>
    /// <item>调用方取消抛出 <see cref="System.OperationCanceledException"/>，不走错误码路径（决策 0.7）。</item>
    /// <item>非预期异常（如 NullReferenceException）由实现层捕获并包装为 <see cref="Unknown"/> 错误码，
    /// 但不得用异常文本作为调用方判断依据。</item>
    /// </list>
    /// </remarks>
    public enum BattleErrorCode
    {
        /// <summary>
        /// 操作成功（无错误）。固定为 0。
        /// </summary>
        None = 0,

        // ====================================================================
        // 预期状态错误（spec: "Battle module exposes one authoritative lifecycle"）
        // ====================================================================

        /// <summary>
        /// Start 在非 Idle 状态调用。对应 spec 场景 "Duplicate start returns AlreadyActive"：
        /// 战斗正在加载或运行时再次收到开始请求，不创建第二个活动运行时，不改变当前运行时状态。
        /// </summary>
        AlreadyActive = 10,

        /// <summary>
        /// Restart 在非 Settling 状态调用。对应 spec 场景 "Restart rejected outside settling"：
        /// 在 Running 或 Entering 等非 Settling 状态请求再来一局，不销毁当前运行时，不创建新运行时。
        /// </summary>
        NotSettling = 11,

        /// <summary>
        /// 模块处于 Faulted 状态，需先清理（Faulted → Idle）才能接受新命令。
        /// 对应决策 0.7：Faulted 必须先清理才能回到 Idle。
        /// </summary>
        Faulted = 12,

        /// <summary>
        /// 模块正在退出（Exiting），拒绝新的 Start/Restart 请求。
        /// Exit 本身幂等，但 Start/Restart 在退出过程中不应被接受。
        /// </summary>
        Exiting = 13,

        // ====================================================================
        // 配置错误（task 3.5 BattleConfigValidator 产生）
        // ====================================================================

        /// <summary>
        /// 必需配置表缺失（如地图、波次、敌人、单位或牌组表不存在）。
        /// 对应 task 3.5：缺表在进入战斗前失败。
        /// </summary>
        ConfigMissing = 20,

        /// <summary>
        /// 配置校验失败（非法权重、未知兵种、非法时间/距离、地图尺寸错误、越界路径或缺失引用）。
        /// 对应 task 3.5：任何未知兵种、非法权重、越界路径或缺失引用在进入战斗前失败。
        /// </summary>
        ConfigInvalid = 21,

        /// <summary>
        /// 配置版本或 hash 不匹配，本局配置与预期基线不一致。
        /// 对应 task 3.2/3.5：配置版本/hash 校验。
        /// </summary>
        ConfigVersionMismatch = 22,

        // ====================================================================
        // 资源错误（task 7.6 资源接入）
        // ====================================================================

        /// <summary>
        /// 战斗 Scene 加载失败。对应 spec 场景 "Resource loading fails during entry"。
        /// </summary>
        SceneLoadFailed = 30,

        /// <summary>
        /// 战斗 UI（FUI Package/Window）加载失败。
        /// </summary>
        UILoadFailed = 31,

        /// <summary>
        /// 战斗 Prefab 或其他必需资源加载失败。
        /// </summary>
        AssetLoadFailed = 32,

        /// <summary>
        /// 必需资源不存在（寻址无效或资源未收集到 YooAsset 包）。
        /// 对应 task 3.5：资源缺失在进入战斗前失败。
        /// </summary>
        AssetMissing = 33,

        // ====================================================================
        // 初始化与清理错误
        // ====================================================================

        /// <summary>
        /// 部分初始化失败，已执行反向回滚。对应 spec "Partial initialization is recoverable"：
        /// 系统不会进入运行状态，不留下半初始化运行时。
        /// </summary>
        PartialInitializationFailed = 40,

        /// <summary>
        /// 未知/非预期错误。实现层捕获非预期异常后包装为此错误码；
        /// 调用方不得依赖 <see cref="BattleOperationResult.DiagnosticMessage"/> 文本做程序化判断。
        /// </summary>
        Unknown = 99,
    }
}
