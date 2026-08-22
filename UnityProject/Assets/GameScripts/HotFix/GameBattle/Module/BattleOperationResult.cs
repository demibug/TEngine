using System;

namespace GameBattle
{
    /// <summary>
    /// 战斗操作失败发生的稳定阶段。
    /// </summary>
    public enum BattleFailureStage
    {
        None = 0,
        WorldPreparation = 1,
        MapLoad = 2,
        MapBinding = 3,
        CameraSetup = 4,
        PresentationPreload = 5,
        RuntimeCreate = 6,
        ServiceStart = 7,
        HudOpen = 8,
        Rollback = 9,
        Exit = 10,
    }

    /// <summary>
    /// 战斗模块公共操作的结构化结果（task 2.4）。
    /// </summary>
    /// <remarks>
    /// <para><b>设计依据（specs/battle-runtime-lifecycle/spec.md + 决策 0.7）：</b></para>
    /// <para>预期状态、配置、资源错误 MUST 返回结构化结果而非异常，调用方不得依赖异常文本判断失败原因。
    /// 本结构为公共异步 API（<see cref="IBattleModule.StartAsync"/> / <see cref="IBattleModule.RestartAsync"/>
    /// / <see cref="IBattleModule.ExitAsync"/>）的统一返回类型，携带稳定错误码与结构化只读数据。</para>
    ///
    /// <para><b>稳定性约束：</b></para>
    /// <list type="bullet">
    /// <item><see cref="ErrorCode"/> 为稳定枚举，调用方以此做程序化判断。</item>
    /// <item><see cref="DiagnosticMessage"/> 仅用于日志/诊断，调用方 MUST NOT 解析其文本判断失败原因。</item>
    /// <item><see cref="CurrentState"/> 为操作完成后的模块状态快照，供调用方做后续决策。</item>
    /// </list>
    ///
    /// <para><b>与异常的关系（决策 0.7）：</b></para>
    /// <list type="bullet">
    /// <item>预期失败（状态、配置、资源）返回 <c>Failed</c> 结果 + 错误码，不抛异常。</item>
    /// <item>非预期异常由实现层捕获并包装为 <see cref="BattleErrorCode.Unknown"/> 错误码。</item>
    /// </list>
    ///
    /// <para><b>不可变性：</b>本结构为 readonly struct，全部字段为 readonly，构造后不可修改。</para>
    /// </remarks>
    public readonly struct BattleOperationResult
    {
        /// <summary>
        /// 操作是否成功。等价于 <see cref="ErrorCode"/> == <see cref="BattleErrorCode.None"/>。
        /// 调用方应优先使用本属性判断成功，而非比较枚举值。
        /// </summary>
        public bool IsSuccess => ErrorCode == BattleErrorCode.None;

        /// <summary>
        /// 稳定错误码。成功时为 <see cref="BattleErrorCode.None"/>。
        /// 调用方以此做程序化判断，不得依赖 <see cref="DiagnosticMessage"/> 文本。
        /// </summary>
        public readonly BattleErrorCode ErrorCode;

        /// <summary>
        /// 操作完成后的模块状态快照。供调用方了解操作执行后模块处于哪个状态，
        /// 例如 Start 成功后为 <see cref="BattleModuleState.Running"/>，
        /// Exit 完成后为 <see cref="BattleModuleState.Idle"/>。
        /// </summary>
        public readonly BattleModuleState CurrentState;

        /// <summary>
        /// 诊断信息（仅用于日志/诊断）。调用方 MUST NOT 解析此文本判断失败原因，
        /// 必须使用 <see cref="ErrorCode"/> 做程序化判断。成功时为空串。
        /// </summary>
        public readonly string DiagnosticMessage;

        /// <summary>失败阶段；成功时为 <see cref="BattleFailureStage.None"/>。</summary>
        public readonly BattleFailureStage FailureStage;

        /// <summary>失败涉及的资源地址；不涉及资源时为空串。</summary>
        public readonly string ResourceAddress;

        /// <summary>失败涉及的地图节点路径；不涉及节点时为空串。</summary>
        public readonly string NodePath;

        /// <summary>底层异常类型；没有底层异常时为空串。</summary>
        public readonly string ExceptionType;

        /// <summary>底层异常信息；没有底层异常时为空串。</summary>
        public readonly string ExceptionMessage;

        /// <summary>
        /// 构造结构化操作结果。
        /// </summary>
        /// <param name="errorCode">稳定错误码；成功时为 <see cref="BattleErrorCode.None"/>。</param>
        /// <param name="currentState">操作完成后的模块状态快照。</param>
        /// <param name="diagnosticMessage">
        /// 诊断信息（仅用于日志）。为 null 时规范化为空串；调用方不得解析文本判断失败原因。
        /// </param>
        public BattleOperationResult(
            BattleErrorCode errorCode,
            BattleModuleState currentState,
            string diagnosticMessage = null,
            BattleFailureStage failureStage = BattleFailureStage.None,
            string resourceAddress = null,
            string nodePath = null,
            Exception exception = null)
        {
            ErrorCode = errorCode;
            CurrentState = currentState;
            // 明确拒绝 null：诊断信息用空串而非 null，避免接收方判空歧义。
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
            FailureStage = failureStage;
            ResourceAddress = resourceAddress ?? string.Empty;
            NodePath = nodePath ?? string.Empty;
            ExceptionType = exception?.GetType().FullName ?? string.Empty;
            ExceptionMessage = exception?.Message ?? string.Empty;
        }

        private BattleOperationResult(
            BattleErrorCode errorCode,
            BattleModuleState currentState,
            string diagnosticMessage,
            BattleFailureStage failureStage,
            string resourceAddress,
            string nodePath,
            string exceptionType,
            string exceptionMessage)
        {
            ErrorCode = errorCode;
            CurrentState = currentState;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
            FailureStage = failureStage;
            ResourceAddress = resourceAddress ?? string.Empty;
            NodePath = nodePath ?? string.Empty;
            ExceptionType = exceptionType ?? string.Empty;
            ExceptionMessage = exceptionMessage ?? string.Empty;
        }

        /// <summary>保留失败上下文，仅更新模块状态快照。</summary>
        internal BattleOperationResult WithState(BattleModuleState currentState)
            => new BattleOperationResult(
                ErrorCode,
                currentState,
                DiagnosticMessage,
                FailureStage,
                ResourceAddress,
                NodePath,
                ExceptionType,
                ExceptionMessage);

        /// <summary>
        /// 构造成功结果。
        /// </summary>
        /// <param name="currentState">操作完成后的模块状态。</param>
        /// <returns>成功结果，错误码为 None。</returns>
        public static BattleOperationResult Ok(BattleModuleState currentState)
            => new BattleOperationResult(BattleErrorCode.None, currentState);

        /// <summary>
        /// 构造失败结果。
        /// </summary>
        /// <param name="errorCode">稳定错误码（非 None）。</param>
        /// <param name="currentState">操作完成后的模块状态快照。</param>
        /// <param name="diagnosticMessage">诊断信息（仅用于日志），为 null 时规范化为空串。</param>
        /// <returns>失败结果。</returns>
        public static BattleOperationResult Fail(
            BattleErrorCode errorCode,
            BattleModuleState currentState,
            string diagnosticMessage = null,
            BattleFailureStage failureStage = BattleFailureStage.None,
            string resourceAddress = null,
            string nodePath = null,
            Exception exception = null)
            => new BattleOperationResult(
                errorCode,
                currentState,
                diagnosticMessage,
                failureStage,
                resourceAddress,
                nodePath,
                exception);

        /// <summary>
        /// 返回结果的字符串表示（仅用于日志/诊断）。调用方不得解析此字符串判断失败原因。
        /// </summary>
        public override string ToString()
            => IsSuccess
                ? $"[BattleOperationResult] Success, State={CurrentState}"
                : $"[BattleOperationResult] Failed, Code={ErrorCode}, State={CurrentState}, " +
                  $"Stage={FailureStage}, Resource={ResourceAddress}, Node={NodePath}, " +
                  $"Exception={ExceptionType}: {ExceptionMessage}, Msg={DiagnosticMessage}";
    }
}
