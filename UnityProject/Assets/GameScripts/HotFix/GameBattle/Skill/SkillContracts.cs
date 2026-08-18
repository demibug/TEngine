using System;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.1：Skill 公共契约 —— owner 句柄、激活计划/上下文、操作结果与只读状态
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 2/4/5 / specs/combat-skill-lifecycle/spec.md）：
    //   定义 SkillRunner 与其 handler 之间交换的稳定数据类型。本 wave 只交付契约
    //   与 registry，SkillRunner 本体由下一波实现。
    //
    // 关键语义（与 tasks/design 对齐）：
    //   1. SkillOwnerHandle(RuntimeId, Generation) 是值类型：以 runtime id 与对象池
    //      generation 标识 owner，保证池复用与迟到回调安全（spec "Old callback
    //      observes a reused owner"）。
    //   2. SkillActivationPlan 是不可变 DTO，但允许承载非法延迟：非法
    //      `0 <= effectDelayMs < completeDelayMs` 由后续 Activate 返回结构化失败，
    //      本构造器不得因此抛异常。
    //   3. SkillActivationContext 不可变：只包含 handler 真正消费的 owner、skillKey
    //      与 battleNowMs；内部 runVersion 不泄漏给具体效果实现。
    //   4. 本契约不创建通用表现协议、效果 DSL、服务定位器或独立 Update。
    // ============================================================================

    /// <summary>
    /// 技能持有者句柄（值类型）：以运行时标识与生命周期 generation 唯一标识 owner。
    /// </summary>
    /// <remarks>
    /// <para>spec "Skill ownership is generation safe and minimal"：同一
    /// (RuntimeId, Generation) 表示对象池当前租期内的一个稳定身份；对象被回收并
    /// 以新 generation 复用后，旧句柄不得再操作新租期。</para>
    /// <para>值相等基于两个字段，不持有任何 Unity 对象引用。</para>
    /// </remarks>
    internal readonly struct SkillOwnerHandle : IEquatable<SkillOwnerHandle>
    {
        /// <summary>owner 运行时标识（对应对象池实例标识）。</summary>
        public readonly int RuntimeId;

        /// <summary>owner 生命周期 generation（对象池租期递增）。</summary>
        public readonly long Generation;

        /// <summary>构造技能持有者句柄。</summary>
        /// <param name="runtimeId">owner 运行时标识。</param>
        /// <param name="generation">owner 生命周期 generation。</param>
        public SkillOwnerHandle(int runtimeId, long generation)
        {
            RuntimeId = runtimeId;
            Generation = generation;
        }

        /// <inheritdoc/>
        public bool Equals(SkillOwnerHandle other)
        {
            return RuntimeId == other.RuntimeId && Generation == other.Generation;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is SkillOwnerHandle other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (RuntimeId * 397) ^ Generation.GetHashCode();
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"SkillOwnerHandle(runtimeId={RuntimeId}, generation={Generation})";
        }

        /// <summary>两个句柄是否相等。</summary>
        public static bool operator ==(SkillOwnerHandle left, SkillOwnerHandle right)
            => left.Equals(right);

        /// <summary>两个句柄是否不相等。</summary>
        public static bool operator !=(SkillOwnerHandle left, SkillOwnerHandle right)
            => !left.Equals(right);
    }

    /// <summary>
    /// 激活计划（不可变 DTO）：只包含 effect/complete 偏移。
    /// </summary>
    /// <remarks>
    /// <para>spec "Active and Boss activation is atomic"：合法计划要求
    /// <c>0 &lt;= EffectDelayMs &lt; CompleteDelayMs</c>。但本 DTO 允许承载非法延迟，
    /// 非法值由后续 Activate 以 <see cref="SkillOperationStatus.InvalidActivationPlan"/>
    /// 结构化失败返回，构造器不抛异常——计划只是请求数据，不是校验点。</para>
    /// </remarks>
    internal sealed class SkillActivationPlan
    {
        /// <summary>effect 回调相对激活时刻的偏移（毫秒，允许非法值）。</summary>
        public long EffectDelayMs { get; }

        /// <summary>complete 回调相对激活时刻的偏移（毫秒，允许非法值）。</summary>
        public long CompleteDelayMs { get; }

        /// <summary>构造激活计划。</summary>
        /// <param name="effectDelayMs">effect 回调偏移（毫秒）。</param>
        /// <param name="completeDelayMs">complete 回调偏移（毫秒）。</param>
        public SkillActivationPlan(long effectDelayMs, long completeDelayMs)
        {
            EffectDelayMs = effectDelayMs;
            CompleteDelayMs = completeDelayMs;
        }
    }

    /// <summary>
    /// 激活上下文（不可变）：handler 收到的全部激活信息。
    /// </summary>
    /// <remarks>
    /// <para>spec "Concrete effects are explicit handlers"：handler 接收 owner、
    /// skill key 与战斗时间戳；依赖（Buff/Unit/Enemy/表现端口）经构造
    /// 注入，不从上下文访问全局 Runtime。</para>
    /// <para>本类型不持有 Unity 对象或 <c>BattleRuntime</c> 引用；时间戳
    /// <see cref="BattleNowMs"/> 是调度器的战斗帧毫秒（外部帧时间戳），不是真实时间。</para>
    /// </remarks>
    internal sealed class SkillActivationContext
    {
        /// <summary>激活所属的 owner 句柄。</summary>
        public SkillOwnerHandle Owner { get; }

        /// <summary>激活的技能 key（与目录定义一致）。</summary>
        public string SkillKey { get; }

        /// <summary>激活时的战斗帧时间戳（毫秒）。</summary>
        public long BattleNowMs { get; }

        /// <summary>构造激活上下文。</summary>
        /// <param name="owner">激活所属的 owner 句柄。</param>
        /// <param name="skillKey">激活的技能 key。</param>
        /// <param name="battleNowMs">战斗帧时间戳（毫秒）。</param>
        public SkillActivationContext(
            SkillOwnerHandle owner,
            string skillKey,
            long battleNowMs)
        {
            Owner = owner;
            SkillKey = skillKey ?? string.Empty;
            BattleNowMs = battleNowMs;
        }
    }

    /// <summary>
    /// 技能操作结果状态（稳定枚举，调用方以此做程序化判断）。
    /// </summary>
    /// <remarks>
    /// <para>对应 spec 的 attach/activate/cancel/cleanup 失败语义；新增状态只能追加
    /// 到末尾，不得重排已有值。调用方不解析 <see cref="SkillOperationResult.DiagnosticMessage"/>
    /// 文本。</para>
    /// </remarks>
    internal enum SkillOperationStatus
    {
        /// <summary>操作成功。</summary>
        Success = 0,

        /// <summary>目录中不存在该 skill key。</summary>
        UnknownSkillKey = 1,

        /// <summary>类别不受本框架支持（如 Passive 明确 unsupported）。</summary>
        UnsupportedCategory = 2,

        /// <summary>owner 未 attach 该技能（无法激活/取消）。</summary>
        NotAttached = 3,

        /// <summary>owner 句柄过期（generation 不匹配或已被清理）。</summary>
        StaleOwner = 4,

        /// <summary>定义引用的 handler 键未在 registry 注册（可留目录但不可 attach）。</summary>
        HandlerMissing = 5,

        /// <summary>激活计划非法（effect/complete 偏移不满足 0 &lt;= effect &lt; complete）。</summary>
        InvalidActivationPlan = 6,

        /// <summary>同一技能已有正在运行的激活（busy）。</summary>
        Busy = 7,

        /// <summary>冷却未结束（尚未就绪）。</summary>
        OnCooldown = 8,

        /// <summary>技能当前未在运行（取消/完成时无进行中激活）。</summary>
        NotRunning = 9,

        /// <summary>内部状态非法（防御性，正常路径不可达）。</summary>
        InvalidState = 10,

        /// <summary>Runner 已 Dispose：清理完成后拒绝任何新操作（幂等，重复清理不抛异常）。</summary>
        Disposed = 11,
    }

    /// <summary>
    /// 技能操作结果（不可变值类型）：状态码 + 诊断信息。
    /// </summary>
    /// <remarks>
    /// <para>调用方以 <see cref="Status"/> 做程序化判断；<see cref="DiagnosticMessage"/>
    /// 仅用于日志。成功操作的 <see cref="DiagnosticMessage"/> 为空串。</para>
    /// </remarks>
    internal readonly struct SkillOperationResult
    {
        /// <summary>稳定状态码。</summary>
        public SkillOperationStatus Status { get; }

        /// <summary>诊断信息（仅用于日志，不可作程序化判断依据）。</summary>
        public string DiagnosticMessage { get; }

        /// <summary>是否成功（等价于 <see cref="Status"/> 为 Success）。</summary>
        public bool IsSuccess => Status == SkillOperationStatus.Success;

        /// <summary>构造操作结果。</summary>
        /// <param name="status">稳定状态码。</param>
        /// <param name="diagnosticMessage">诊断信息（可为空）。</param>
        public SkillOperationResult(SkillOperationStatus status, string diagnosticMessage = "")
        {
            Status = status;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
        }

        /// <summary>成功结果的便捷工厂。</summary>
        public static SkillOperationResult Ok()
            => new SkillOperationResult(SkillOperationStatus.Success);

        /// <summary>失败结果的便捷工厂。</summary>
        public static SkillOperationResult Fail(SkillOperationStatus status, string diagnosticMessage = "")
            => new SkillOperationResult(status, diagnosticMessage);

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.IsNullOrEmpty(DiagnosticMessage)
                ? $"[{Status}]"
                : $"[{Status}] {DiagnosticMessage}";
        }
    }

    /// <summary>
    /// 单个 (owner, skillKey) 附着状态的只读快照（供最小状态查询断言，不新增全局实例列表）。
    /// </summary>
    /// <remarks>
    /// <para>design.md 决策 2：Runner 以 (owner, skillKey) 保存可变 SkillState；本快照
    /// 是它的不可变视图，测试与诊断可据此断言单个 key，不引入全局实例查询系统。</para>
    /// <para>本类型由 <see cref="SkillRunner.TryGetState"/> 产出，供最小状态查询断言。</para>
    /// </remarks>
    internal sealed class SkillStateSnapshot
    {
        /// <summary>当前是否有正在运行的激活。</summary>
        public bool IsRunning { get; }

        /// <summary>下一次允许激活的时间戳（毫秒；由调度器战斗时钟驱动）。</summary>
        public long NextReadyAtMs { get; }

        /// <summary>当前激活的 run version（只在成功激活时递增；0 表示从未激活）。</summary>
        public long RunVersion { get; }

        /// <summary>当前激活的 effect 是否已提交（effect 回调已执行）。</summary>
        public bool EffectCommitted { get; }

        /// <summary>构造只读状态快照。</summary>
        /// <param name="isRunning">是否有正在运行的激活。</param>
        /// <param name="nextReadyAtMs">下一次允许激活的时间戳（毫秒）。</param>
        /// <param name="runVersion">当前激活的 run version。</param>
        /// <param name="effectCommitted">当前激活的 effect 是否已提交。</param>
        public SkillStateSnapshot(
            bool isRunning,
            long nextReadyAtMs,
            long runVersion,
            bool effectCommitted)
        {
            IsRunning = isRunning;
            NextReadyAtMs = nextReadyAtMs;
            RunVersion = runVersion;
            EffectCommitted = effectCommitted;
        }
    }
}
