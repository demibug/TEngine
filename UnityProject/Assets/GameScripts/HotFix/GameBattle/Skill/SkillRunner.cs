using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.2-3.7：SkillRunner —— 最小 skill 生命周期、两节点时间线与取消清理
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 2/3/5 / specs/combat-skill-lifecycle/spec.md）：
    //   owner 注册/注销、(owner,skillKey) 唯一 attached state、Active/Boss 激活的
    //   两阶段原子验证、effect→complete 两节点 scheduler 时间线、取消/清理幂等。
    //   本类型没有 Update、不持有 BattleRuntime/Unity 时间源，时间只读
    //   BattleActionScheduler.FrameNowMs（spec "Cooldown and timeline use the
    //   battle scheduler"）。
    //
    // 关键语义：
    //   1. owner 必须先行 RegisterOwner：以 (runtimeId, generation) 建立当前租期。
    //      同 runtimeId 更小 generation 为过期句柄（StaleOwner）；更大 generation
    //      表示对象池复用，稳定取消旧租期全部运行激活并清除 state 后替换。无效
    //      句柄（RuntimeId<=0 或 Generation<=0）一律视为 StaleOwner。
    //   2. Attach 只允许 Active/Boss；Passive 明确 UnsupportedCategory；handler 未
    //      注册返回 HandlerMissing 且不创建任何 state。
    //   3. Activate 两阶段：先在 owner/definition/category/attached/plan/busy/
    //      cooldown/checked 时间加法全部通过后才提交（runVersion 递增、写冷却、
    //      按 effect→complete 顺序 Schedule）。任一 Schedule 失败取消已登记 handle
    //      并恢复激活前状态，不消耗冷却。
    //   4. 每个 callback 捕获 owner/key/version，执行前重验 current
    //      generation、state 存在、Running、runVersion；迟到 callback no-op。
    //   5. Cancel/Detach/ClearOwner/Clear/Dispose（含 RegisterOwner 新租期替换、
    //      UnregisterOwner）取消未执行 handles；每个运行中激活只调用一次
    //      handler.Cancel(context, effectCommitted)，保留 NextReadyAtMs，不逆转已
    //      提交外部效果。批量清理按 runtimeId、skillKey ordinal 稳定顺序。
    // ============================================================================

    /// <summary>
    /// 技能运行器：一个 owner 的一个技能如何占有 effect/complete 两次定时回调并安全取消。
    /// </summary>
    /// <remarks>
    /// <para><b>唯一时间源（design.md 决策 5）：</b>本类型不持有任何时钟；冷却写入与
    /// 到期判断全部基于 <see cref="BattleActionScheduler.FrameNowMs"/>，effect/complete
    /// 只经调度器的到期动作阶段执行，不新增模拟 phase、协程或独立 Update。</para>
    /// <para><b>owner 租期：</b>RegisterOwner 建立 (runtimeId, generation) 租期；对象池
    /// 复用后以更大 generation 重新注册会取消旧租期全部运行激活并清除 state，旧句柄
    /// 后续操作一律 StaleOwner（spec "Skill ownership is generation safe"）。</para>
    /// <para><b>两节点时间线：</b>成功 Activate 后以 <c>FrameNowMs+EffectDelayMs</c>、
    /// <c>FrameNowMs+CompleteDelayMs</c> 顺序登记两个到期动作；同批 flush 内按注册
    /// 顺序 FIFO，保证 effect 先于 complete、每个节点 exactly-once。</para>
    /// <para><b>不猜测事务回滚：</b>Effect 在 handler 调用开始即视为已提交；handler
    /// 抛异常时 Runner 取消 completion、结束 running 并继续抛出，不自动回滚外部效果。</para>
    /// </remarks>
    internal sealed class SkillRunner : IDisposable
    {
        private readonly SkillCatalogSnapshot _catalog;
        private readonly SkillHandlerRegistry _registry;
        private readonly BattleActionScheduler _scheduler;

        /// <summary>runtimeId → 当前租期（一个 runtimeId 同时至多一个租期）。</summary>
        private readonly Dictionary<int, OwnerLease> _owners =
            new Dictionary<int, OwnerLease>();

        private bool _disposed;

        /// <summary>构造技能运行器。</summary>
        /// <param name="catalog">只读 Skill 目录（必须非 null）。</param>
        /// <param name="registry">handler 注册表（必须非 null）。</param>
        /// <param name="scheduler">唯一时间源与到期动作调度器（必须非 null）。</param>
        /// <exception cref="ArgumentNullException">任一依赖为 null。</exception>
        internal SkillRunner(
            SkillCatalogSnapshot catalog,
            SkillHandlerRegistry registry,
            BattleActionScheduler scheduler)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        /// <summary>注入的调度器（唯一时间源；测试通过它驱动时间与 flush 到期动作）。</summary>
        internal BattleActionScheduler Scheduler => _scheduler;

        /// <summary>当前注册的技能 owner 数量（清理静默性检查）。</summary>
        internal int OwnerCount => _owners.Count;

        /// <summary>当前运行中的技能激活数量（清理静默性检查）。</summary>
        internal int RunningActivationCount
        {
            get
            {
                int count = 0;
                foreach (OwnerLease lease in _owners.Values)
                {
                    foreach (SkillState state in lease.States.Values)
                    {
                        if (state.Running)
                        {
                            count += 1;
                        }
                    }
                }

                return count;
            }
        }

        // ====================================================================
        // owner 注册/注销
        // ====================================================================

        /// <summary>
        /// 注册 owner 当前租期（对象池获取后调用）。
        /// </summary>
        /// <param name="owner">owner 句柄（RuntimeId 与 Generation 必须为正）。</param>
        /// <returns>
        /// Success：新租期建立，或同句柄幂等成功，或更大 generation 替换旧租期；
        /// StaleOwner：无效句柄或同 runtimeId 的更小/过期 generation。
        /// </returns>
        /// <remarks>
        /// <para>更大 generation 替换时先按稳定顺序取消旧租期全部运行激活（每个运行
        /// 中激活调用一次 handler.Cancel）并清除 state，保证旧租期状态不泄漏到新租期。</para>
        /// </remarks>
        internal SkillOperationResult RegisterOwner(SkillOwnerHandle owner)
        {
            if (_disposed)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.Disposed,
                    "SkillRunner 已 Dispose，拒绝注册 owner。");
            }

            if (!IsValidHandle(owner))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.StaleOwner,
                    $"无效 owner 句柄：{owner}（RuntimeId 与 Generation 必须为正）。");
            }

            if (_owners.TryGetValue(owner.RuntimeId, out OwnerLease current))
            {
                if (current.Generation == owner.Generation)
                {
                    // 同句柄重复注册：幂等成功，保留既有租期。
                    return SkillOperationResult.Ok();
                }

                if (current.Generation > owner.Generation)
                {
                    return SkillOperationResult.Fail(SkillOperationStatus.StaleOwner,
                        $"旧 generation 的 owner 不可注册：请求 generation={owner.Generation}，" +
                        $"当前租期 generation={current.Generation}。");
                }

                // 更大 generation：对象池复用，稳定取消并清除旧租期 state 后替换。
                ClearLease(current);
                _owners[owner.RuntimeId] = new OwnerLease(owner.RuntimeId, owner.Generation);
                return SkillOperationResult.Ok();
            }

            _owners.Add(owner.RuntimeId, new OwnerLease(owner.RuntimeId, owner.Generation));
            return SkillOperationResult.Ok();
        }

        /// <summary>
        /// 注销 owner：取消其全部运行激活并清除全部 attached state 后移除租期。
        /// </summary>
        /// <returns>
        /// Success：租期已移除；StaleOwner：owner 未注册或句柄过期。
        /// </returns>
        internal SkillOperationResult UnregisterOwner(SkillOwnerHandle owner)
        {
            if (_disposed)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.Disposed,
                    "SkillRunner 已 Dispose，拒绝注销 owner。");
            }

            if (!TryGetCurrentLease(owner, out OwnerLease lease))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.StaleOwner,
                    $"owner 未注册或句柄过期：{owner}。");
            }

            ClearLease(lease);
            _owners.Remove(owner.RuntimeId);
            return SkillOperationResult.Ok();
        }

        // ====================================================================
        // attach / activate / cancel / detach
        // ====================================================================

        /// <summary>
        /// 为当前租期 owner 附着指定技能（同一 (owner, skillKey) 至多一个 state）。
        /// </summary>
        /// <param name="owner">当前租期 owner 句柄。</param>
        /// <param name="skillKey">目录中的技能 key。</param>
        /// <returns>
        /// Success：state 已创建，或重复 attach 幂等成功保留同一 state；
        /// StaleOwner：owner 未注册/句柄过期；
        /// UnknownSkillKey：目录无该 key；UnsupportedCategory：Passive；
        /// HandlerMissing：handler 未注册（不创建 state）。
        /// </returns>
        internal SkillOperationResult Attach(SkillOwnerHandle owner, int skillId)
        {
            if (_disposed)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.Disposed,
                    "SkillRunner 已 Dispose，拒绝 attach。");
            }

            if (!TryGetCurrentLease(owner, out OwnerLease lease))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.StaleOwner,
                    $"owner 未注册或句柄过期：{owner}。");
            }

            if (skillId < 1)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.UnknownSkillKey,
                    "skillId 必须从 1 开始。");
            }

            if (!_catalog.TryGetById(skillId, out SkillDefinitionSnapshot definition))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.UnknownSkillKey,
                    $"目录中不存在技能 id={skillId}。");
            }

            if (!IsSupportedCategory(definition.Category))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.UnsupportedCategory,
                    $"技能类别不支持 attach：id={skillId}, category={definition.Category}。");
            }

            if (!_registry.TryGet(definition.HandlerKey, out ISkillHandler handler))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.HandlerMissing,
                    $"技能 handler 未注册：id={skillId}, handlerKey='{definition.HandlerKey}'。");
            }

            if (lease.States.ContainsKey(skillId))
            {
                // 重复 attach：幂等成功，保留同一 state（不重复调用 handler）。
                return SkillOperationResult.Ok();
            }

            var state = new SkillState
            {
                Definition = definition,
                Handler = handler,
            };
            lease.States.Add(skillId, state);
            return SkillOperationResult.Ok();
        }

        /// <summary>
        /// 激活指定技能（Active/Boss）：两阶段原子验证通过后提交并登记两节点时间线。
        /// </summary>
        /// <param name="owner">当前租期 owner 句柄。</param>
        /// <param name="skillKey">已 attach 的技能 key。</param>
        /// <param name="plan">激活计划（延迟与 payload）。</param>
        /// <returns>
        /// Success：技能进入 running 并持有唯一 effect/complete 两个到期动作；
        /// StaleOwner：owner 未注册/句柄过期；UnknownSkillKey：目录无该 key；
        /// UnsupportedCategory：Passive；NotAttached：未 attach；
        /// InvalidActivationPlan：不满足 <c>0 &lt;= effectDelayMs &lt; completeDelayMs</c>；
        /// Busy：同技能已有运行中激活；OnCooldown：冷却未结束；
        /// InvalidState：checked 时间加法溢出或调度器拒绝登记（此时零副作用、不消耗冷却）。
        /// </returns>
        /// <remarks>
        /// <para><b>两阶段提交：</b>所有验证通过后才写 runVersion、NextReadyAtMs 与
        /// 两个 scheduler handles；任一 Schedule 失败取消已登记 handle 并恢复激活前
        /// 状态，失败不消耗冷却（spec "Failed activation does not consume cooldown"）。</para>
        /// <para><b>冷却从激活接受时刻开始：</b><c>NextReadyAtMs = FrameNowMs + CooldownMs</c>，
        /// 取消保留该时间戳（spec "Cancelled activation keeps cooldown"）。</para>
        /// </remarks>
        internal SkillOperationResult Activate(
            SkillOwnerHandle owner,
            int skillId,
            SkillActivationPlan plan)
        {
            if (_disposed)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.Disposed,
                    "SkillRunner 已 Dispose，拒绝激活。");
            }

            if (plan == null)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.InvalidActivationPlan,
                    "激活计划不能为 null。");
            }

            if (!TryGetCurrentLease(owner, out OwnerLease lease))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.StaleOwner,
                    $"owner 未注册或句柄过期：{owner}。");
            }

            if (skillId < 1)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.UnknownSkillKey,
                    "skillId 必须从 1 开始。");
            }

            if (!_catalog.TryGetById(skillId, out SkillDefinitionSnapshot definition))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.UnknownSkillKey,
                    $"目录中不存在技能 id={skillId}。");
            }

            if (!IsSupportedCategory(definition.Category))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.UnsupportedCategory,
                    $"技能类别不支持激活：id={skillId}, category={definition.Category}。");
            }

            if (!lease.States.TryGetValue(skillId, out SkillState state))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.NotAttached,
                    $"技能未 attach：id={skillId}。");
            }

            if (plan.EffectDelayMs < 0 || plan.CompleteDelayMs <= plan.EffectDelayMs)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.InvalidActivationPlan,
                    $"非法激活计划：effectDelayMs={plan.EffectDelayMs}, completeDelayMs={plan.CompleteDelayMs}，" +
                    "要求 0 <= effectDelayMs < completeDelayMs。");
            }

            if (state.Running)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.Busy,
                    $"技能已在运行：id={skillId}。");
            }

            if (_scheduler.FrameNowMs < state.NextReadyAtMs)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.OnCooldown,
                    $"技能冷却中：id={skillId}, 当前帧={_scheduler.FrameNowMs}, " +
                    $"就绪于={state.NextReadyAtMs}。");
            }

            long now = _scheduler.FrameNowMs;
            long nextReadyMs;
            long effectDueMs;
            long completeDueMs;
            try
            {
                nextReadyMs = checked(now + definition.CooldownMs);
                effectDueMs = checked(now + plan.EffectDelayMs);
                completeDueMs = checked(now + plan.CompleteDelayMs);
            }
            catch (OverflowException)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.InvalidState,
                    "激活时间戳（checked 加法）溢出，拒绝激活且不消耗冷却。");
            }

            // 捕获 owner/key/version 供 callback 执行前重验。
            long nextVersion = state.RunVersion + 1;

            // 按 effect → complete 顺序登记：同批 flush 内 FIFO 保证 effect 先于 complete。
            ScheduledActionHandle effectHandle = _scheduler.Schedule(
                effectDueMs, () => OnEffectDue(owner, skillId, nextVersion));
            if (effectHandle == null)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.InvalidState,
                    "调度器拒绝登记 effect 动作（冻结或 null 回调），激活未提交。");
            }

            ScheduledActionHandle completeHandle = _scheduler.Schedule(
                completeDueMs, () => OnCompleteDue(owner, skillId, nextVersion));
            if (completeHandle == null)
            {
                _scheduler.Cancel(effectHandle);
                return SkillOperationResult.Fail(SkillOperationStatus.InvalidState,
                    "调度器拒绝登记 complete 动作，已取消 effect 登记，激活未提交。");
            }

            // 两个节点都登记成功后才提交状态（原子：失败路径不消耗冷却、不动 runVersion）。
            state.RunVersion = nextVersion;
            state.Running = true;
            state.EffectCommitted = false;
            state.NextReadyAtMs = nextReadyMs;
            state.EffectHandle = effectHandle;
            state.CompleteHandle = completeHandle;
            return SkillOperationResult.Ok();
        }

        /// <summary>
        /// 取消指定技能的运行中激活（撤销未来回调，保留冷却，不逆转已提交外部效果）。
        /// </summary>
        /// <returns>
        /// Success：已取消；StaleOwner：owner 未注册/句柄过期；
        /// NotAttached：未 attach；NotRunning：无运行中激活（重复取消无额外 handler 调用）。
        /// </returns>
        internal SkillOperationResult Cancel(SkillOwnerHandle owner, int skillId)
        {
            if (_disposed)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.Disposed,
                    "SkillRunner 已 Dispose，拒绝取消。");
            }

            if (!TryGetCurrentLease(owner, out OwnerLease lease))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.StaleOwner,
                    $"owner 未注册或句柄过期：{owner}。");
            }

            if (skillId < 1 || !lease.States.TryGetValue(skillId, out SkillState state))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.NotAttached,
                    $"技能未 attach：id={skillId}。");
            }

            if (!state.Running)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.NotRunning,
                    $"技能未在运行：id={skillId}。");
            }

            CancelRunningState(owner, skillId, state);
            return SkillOperationResult.Ok();
        }

        /// <summary>
        /// 解除指定技能附着（运行中先取消一次，再移除 state）。
        /// </summary>
        /// <returns>
        /// Success：state 已移除；StaleOwner：owner 未注册/句柄过期；
        /// NotAttached：未 attach。
        /// </returns>
        internal SkillOperationResult Detach(SkillOwnerHandle owner, int skillId)
        {
            if (_disposed)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.Disposed,
                    "SkillRunner 已 Dispose，拒绝 detach。");
            }

            if (!TryGetCurrentLease(owner, out OwnerLease lease))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.StaleOwner,
                    $"owner 未注册或句柄过期：{owner}。");
            }

            if (skillId < 1 || !lease.States.TryGetValue(skillId, out SkillState state))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.NotAttached,
                    $"技能未 attach：id={skillId}。");
            }

            CancelRunningState(owner, skillId, state);
            lease.States.Remove(skillId);
            return SkillOperationResult.Ok();
        }

        // ====================================================================
        // 批量清理
        // ====================================================================

        /// <summary>
        /// 清理单个 owner 的全部 attached state（取消每个运行中激活一次），保留租期注册。
        /// </summary>
        /// <returns>
        /// Success：全部 state 已清除（含无 state 的幂等成功）；StaleOwner：owner 未注册/句柄过期。
        /// </returns>
        internal SkillOperationResult ClearOwner(SkillOwnerHandle owner)
        {
            if (_disposed)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.Disposed,
                    "SkillRunner 已 Dispose，拒绝清理 owner。");
            }

            if (!TryGetCurrentLease(owner, out OwnerLease lease))
            {
                return SkillOperationResult.Fail(SkillOperationStatus.StaleOwner,
                    $"owner 未注册或句柄过期：{owner}。");
            }

            ClearLease(lease);
            return SkillOperationResult.Ok();
        }

        /// <summary>
        /// 清理全部 owner 的 attached state 与租期（按 runtimeId、skillKey ordinal 稳定顺序）。
        /// </summary>
        /// <returns>Success（重复清理幂等）。</returns>
        internal SkillOperationResult Clear()
        {
            if (_disposed)
            {
                return SkillOperationResult.Fail(SkillOperationStatus.Disposed,
                    "SkillRunner 已 Dispose，拒绝清理。");
            }

            int[] runtimeIds = SortedRuntimeIds();
            for (int i = 0; i < runtimeIds.Length; i++)
            {
                if (_owners.TryGetValue(runtimeIds[i], out OwnerLease lease))
                {
                    ClearLease(lease);
                }
            }

            _owners.Clear();
            return SkillOperationResult.Ok();
        }

        /// <summary>
        /// 清理全部状态并进入已释放态；幂等（重复调用无副作用），清理后拒绝新操作。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            int[] runtimeIds = SortedRuntimeIds();
            for (int i = 0; i < runtimeIds.Length; i++)
            {
                if (_owners.TryGetValue(runtimeIds[i], out OwnerLease lease))
                {
                    ClearLease(lease);
                }
            }

            _owners.Clear();
            _disposed = true;
        }

        // ====================================================================
        // 最小只读状态查询
        // ====================================================================

        /// <summary>
        /// 查询单个 (owner, skillKey) 的只读状态快照。
        /// </summary>
        /// <param name="owner">owner 句柄。</param>
        /// <param name="skillKey">技能 key。</param>
        /// <param name="state">命中时的状态快照；未命中时为未附着快照。</param>
        /// <returns>该 (owner, skillKey) 已 attach 时返回 true，否则 false。</returns>
        /// <remarks>查询不改变任何状态；owner 已 Dispose、未注册或句柄过期时返回 false。</remarks>
        internal bool TryGetState(SkillOwnerHandle owner, int skillId, out SkillStateSnapshot state)
        {
            if (_disposed || !TryGetCurrentLease(owner, out OwnerLease lease))
            {
                state = NotAttachedSnapshot();
                return false;
            }

            if (skillId < 1 || !lease.States.TryGetValue(skillId, out SkillState inner))
            {
                state = NotAttachedSnapshot();
                return false;
            }

            state = new SkillStateSnapshot(
                isRunning: inner.Running,
                nextReadyAtMs: inner.NextReadyAtMs,
                runVersion: inner.RunVersion,
                effectCommitted: inner.EffectCommitted);
            return true;
        }

        // ====================================================================
        // 内部实现
        // ====================================================================

        /// <summary>owner 句柄是否有效（RuntimeId 与 Generation 必须为正）。</summary>
        private static bool IsValidHandle(SkillOwnerHandle owner)
            => owner.RuntimeId > 0 && owner.Generation > 0;

        /// <summary>本框架只支持 Active 与 Boss；Passive 和未知枚举值均明确拒绝。</summary>
        private static bool IsSupportedCategory(SkillCategory category)
            => category == SkillCategory.Active || category == SkillCategory.Boss;

        /// <summary>按 (runtimeId, generation) 匹配当前租期；未注册或过期句柄返回 false。</summary>
        private bool TryGetCurrentLease(SkillOwnerHandle owner, out OwnerLease lease)
        {
            lease = null;
            if (!IsValidHandle(owner))
            {
                return false;
            }

            if (!_owners.TryGetValue(owner.RuntimeId, out lease))
            {
                return false;
            }

            return lease.Generation == owner.Generation;
        }

        /// <summary>按 skillKey ordinal 升序取消租期内每个运行中激活一次，并清空 state。</summary>
        private void ClearLease(OwnerLease lease)
        {
            var owner = new SkillOwnerHandle(lease.RuntimeId, lease.Generation);
            int[] keys = SortedKeys(lease.States);
            for (int i = 0; i < keys.Length; i++)
            {
                CancelRunningState(owner, keys[i], lease.States[keys[i]]);
            }

            lease.States.Clear();
        }

        /// <summary>
        /// 取消单个运行中激活：每个运行中激活只调用一次 handler.Cancel，并取消未执行
        /// handles、复位 running/effectCommitted；保留 NextReadyAtMs（冷却不退款）。
        /// </summary>
        private void CancelRunningState(SkillOwnerHandle owner, int skillId, SkillState state)
        {
            if (!state.Running)
            {
                return;
            }

            var context = new SkillActivationContext(
                owner, skillId, _scheduler.FrameNowMs);
            bool effectCommitted = state.EffectCommitted;
            try
            {
                state.Handler.Cancel(context, effectCommitted);
            }
            finally
            {
                _scheduler.Cancel(state.EffectHandle);
                _scheduler.Cancel(state.CompleteHandle);
                state.EffectHandle = null;
                state.CompleteHandle = null;
                state.Running = false;
                state.EffectCommitted = false;
            }
        }

        /// <summary>
        /// effect 到期回调：重验 current generation、state 存在、Running、runVersion；
        /// 通过后以 callback 当下 FrameNowMs 新建上下文，handler 调用开始即视为已提交。
        /// </summary>
        private void OnEffectDue(SkillOwnerHandle owner, int skillId, long runVersion)
        {
            if (!TryRevalidateRunning(owner, skillId, runVersion, out SkillState state))
            {
                // 迟到 callback：owner 已换租期、state 被清理或版本过期，no-op。
                return;
            }

            if (state.EffectCommitted)
            {
                // effect 已执行一次（防御性 exactly-once）。
                return;
            }

            state.EffectCommitted = true;
            var context = new SkillActivationContext(
                owner, skillId, _scheduler.FrameNowMs);
            try
            {
                state.Handler.Effect(context);
            }
            catch (Exception)
            {
                // handler 抛异常：取消 completion、结束 running 并继续抛出，不猜测外部事务回滚。
                _scheduler.Cancel(state.CompleteHandle);
                state.CompleteHandle = null;
                state.EffectHandle = null;
                state.Running = false;
                throw;
            }
        }

        /// <summary>
        /// complete 到期回调：重验 current generation、state 存在、Running、runVersion，
        /// 且 effect 已提交后才执行一次并结束 running。
        /// </summary>
        private void OnCompleteDue(SkillOwnerHandle owner, int skillId, long runVersion)
        {
            if (!TryRevalidateRunning(owner, skillId, runVersion, out SkillState state))
            {
                // 迟到 callback no-op。
                return;
            }

            if (!state.EffectCommitted)
            {
                // complete 只在 effect 提交后执行一次（防御性）。
                return;
            }

            var context = new SkillActivationContext(
                owner, skillId, _scheduler.FrameNowMs);
            try
            {
                state.Handler.Complete(context);
            }
            finally
            {
                // 无论 handler 是否抛异常，本次运行都结束。
                state.Running = false;
                state.EffectCommitted = false;
                state.EffectHandle = null;
                state.CompleteHandle = null;
            }
        }

        /// <summary>callback 执行前的统一重验：current generation、state 存在、Running、runVersion。</summary>
        private bool TryRevalidateRunning(
            SkillOwnerHandle owner,
            int skillId,
            long runVersion,
            out SkillState state)
        {
            state = null;
            if (_disposed)
            {
                return false;
            }

            if (!_owners.TryGetValue(owner.RuntimeId, out OwnerLease lease))
            {
                return false;
            }

            if (lease.Generation != owner.Generation)
            {
                return false;
            }

            if (!lease.States.TryGetValue(skillId, out state))
            {
                return false;
            }

            if (!state.Running)
            {
                return false;
            }

            return state.RunVersion == runVersion;
        }

        /// <summary>按 runtimeId 升序返回全部已注册 runtimeId（批量清理稳定顺序）。</summary>
        private int[] SortedRuntimeIds()
        {
            var ids = new int[_owners.Count];
            int index = 0;
            foreach (int id in _owners.Keys)
            {
                ids[index++] = id;
            }

            Array.Sort(ids);
            return ids;
        }

        /// <summary>按 ordinal 升序返回 state 字典的 key（批量清理稳定顺序）。</summary>
        private static int[] SortedKeys(Dictionary<int, SkillState> states)
        {
            var keys = new int[states.Count];
            int index = 0;
            foreach (int key in states.Keys)
            {
                keys[index++] = key;
            }

            Array.Sort(keys);
            return keys;
        }

        /// <summary>未附着时的默认快照。</summary>
        private static SkillStateSnapshot NotAttachedSnapshot()
            => new SkillStateSnapshot(isRunning: false,
                nextReadyAtMs: 0, runVersion: 0, effectCommitted: false);

        /// <summary>单个 owner runtimeId 的当前租期与 (skillKey → state)。</summary>
        private sealed class OwnerLease
        {
            internal readonly int RuntimeId;
            internal readonly long Generation;
            internal readonly Dictionary<int, SkillState> States =
                new Dictionary<int, SkillState>();

            internal OwnerLease(int runtimeId, long generation)
            {
                RuntimeId = runtimeId;
                Generation = generation;
            }
        }

        /// <summary>(owner, skillKey) 的可变附着状态：definition、handler、冷却、运行态与两个调度句柄。</summary>
        private sealed class SkillState
        {
            internal SkillDefinitionSnapshot Definition;
            internal ISkillHandler Handler;
            internal bool Running;
            internal long RunVersion;
            internal long NextReadyAtMs;
            internal bool EffectCommitted;
            internal ScheduledActionHandle EffectHandle;
            internal ScheduledActionHandle CompleteHandle;
        }
    }
}
