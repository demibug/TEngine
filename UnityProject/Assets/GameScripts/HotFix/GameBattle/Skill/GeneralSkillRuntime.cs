using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // Wave 3：GeneralSkillRuntime —— 武将主动技能的普通攻击计数与生命周期绑定
    // ----------------------------------------------------------------------------
    // 职责：
    //   1. 按 BattleUnit.UnitId 保存武将的 SkillKey 与普通攻击累计计数 AttackCount；
    //      上下场保留累计，Clear/Dispose 清局。
    //   2. 当前活动租期按 soldier.Id + LifecycleGeneration 绑定到 SkillRunner：
    //      Bind 做 RegisterOwner+Attach（任一步失败原子回滚并抛清晰错误），
    //      Unbind 只清当前租期 owner/runtime 映射（保留 AttackCount）。
    //   3. TryActivateInsteadOfAttack：当前租期匹配且 AttackCount>=TriggerAttackCount 时
    //      用 SkillActivationPlan(0,1) 激活；仅 Success 清零并返回 true，
    //      任何失败不清零返回 false。
    //   4. OnBasicAttack：仅给已绑定武将累计。
    //
    // 设计原则（AGENTS.md：最小、直接设计）：
    //   不引入接口/DSL/事件/UI；不持有 BattleRuntime；不接具体 Handler；
    //   纯逻辑，构造注入 SkillRunner + SkillCatalogSnapshot。
    //
    // 关键语义：
    //   - 持久状态（SkillKey/AttackCount）按 UnitId 保存，跨上下场保留；
    //   - 租期（SkillOwnerHandle）按 soldier.Id+LifecycleGeneration 绑定，随实例变化；
    //   - 同一活动实例重复 Bind 幂等（同 handle + 同 skillKey）；禁止双 attach 到不同 skillKey；
    //   - Unbind 只解除租期映射，不动持久状态；
    //   - Clear/Dispose 清全部 owner 与累计，幂等。
    // ============================================================================

    /// <summary>
    /// 武将主动技能运行时：管理普通攻击计数、SkillRunner owner 租期绑定与上下场生命周期。
    /// </summary>
    /// <remarks>
    /// <para><b>持久状态按 UnitId：</b>每个武将的 SkillKey 与 AttackCount 按
    /// <see cref="BattleUnit.UnitId"/> 保存，武将下场再上场时保留累计。</para>
    /// <para><b>租期按 soldier.Id+LifecycleGeneration：</b>当前活动战斗实例的技能租期
    /// 以 <see cref="SkillOwnerHandle"/>(soldier.Id, soldier.LifecycleGeneration) 绑定到
    /// <see cref="SkillRunner"/>。武将下场解除租期，上场重新绑定。</para>
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部 UnitRegistry 装配
    /// 与 AttackScheduler 调用，不对外暴露。</para>
    /// </remarks>
    internal sealed class GeneralSkillRuntime
    {
        // ====================================================================
        // 注入依赖（不可变）
        // ====================================================================

        private readonly SkillRunner _runner;
        private readonly SkillCatalogSnapshot _catalog;

        // ====================================================================
        // 持久状态（按 UnitId，跨上下场保留）
        // ====================================================================

        /// <summary>
        /// 按 UnitId 保存的武将技能持久状态：SkillKey 与普通攻击累计计数。
        /// </summary>
        private readonly Dictionary<int, GeneralSkillState> _states =
            new Dictionary<int, GeneralSkillState>();

        // ====================================================================
        // 活动租期映射（UnitId → 当前绑定的 SkillOwnerHandle）
        // ====================================================================

        /// <summary>
        /// 当前已绑定到 SkillRunner 的武将租期映射。
        /// <para>键为 UnitId，值为当前活动租期的 <see cref="SkillOwnerHandle"/>。
        /// Unbind 移除此映射但保留 <see cref="_states"/> 中的累计。</para>
        /// </summary>
        private readonly Dictionary<int, SkillOwnerHandle> _activeLeases =
            new Dictionary<int, SkillOwnerHandle>();

        private bool _disposed;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造武将技能运行时。
        /// </summary>
        /// <param name="runner">技能运行器（非 null）。</param>
        /// <param name="catalog">技能目录快照（非 null）。</param>
        /// <exception cref="ArgumentNullException">任一依赖为 null。</exception>
        internal GeneralSkillRuntime(SkillRunner runner, SkillCatalogSnapshot catalog)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        // ====================================================================
        // 诊断属性（测试与清理静默性检查）
        // ====================================================================

        /// <summary>当前已绑定活动租期的武将数量。</summary>
        internal int ActiveLeaseCount => _activeLeases.Count;

        /// <summary>当前已保存持久状态的武将数量（含已下场未清局的）。</summary>
        internal int StateCount => _states.Count;

        // ====================================================================
        // Bind —— 绑定武将当前活动实例的技能租期
        // ====================================================================

        /// <summary>
        /// 绑定武将当前活动战斗实例到 SkillRunner 租期。
        /// </summary>
        /// <param name="unitId">局内单位权威 ID（BattleUnit.UnitId）。</param>
        /// <param name="soldier">当前活动战斗实例（非 null）。</param>
        /// <param name="skillKey">武将技能 key（非空）。</param>
        /// <remarks>
        /// <para><b>原子绑定：</b>先 RegisterOwner 再 Attach；任一步失败原子回滚
        /// （RegisterOwner 成功但 Attach 失败时 UnregisterOwner 恢复）并抛
        /// <see cref="InvalidOperationException"/>。</para>
        /// <para><b>重复绑定幂等：</b>同一活动实例（同 soldier.Id+LifecycleGeneration）
        /// 且同一 skillKey 重复 Bind 时幂等成功，不重复 RegisterOwner/Attach。</para>
        /// <para><b>禁止双 attach：</b>同一活动实例已绑定到不同 skillKey 时明确拒绝。</para>
        /// <para><b>持久状态：</b>RegisterOwner+Attach 全部成功后才按 UnitId 记录 SkillKey
        /// 与 AttackCount=0；上下场重进同 UnitId 时保留已有 AttackCount。失败不新增/改写
        /// 持久 state，不满足原子语义前不会写入。</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="soldier"/> 为 null。</exception>
        /// <exception cref="ArgumentException"><paramref name="skillKey"/> 为 null 或空。</exception>
        /// <exception cref="InvalidOperationException">RegisterOwner 或 Attach 失败。</exception>
        internal void Bind(int unitId, SoldierBase soldier, string skillKey)
        {
            ThrowIfDisposed();

            if (soldier == null)
            {
                throw new ArgumentNullException(nameof(soldier));
            }

            if (string.IsNullOrEmpty(skillKey))
            {
                throw new ArgumentException("skillKey 不能为 null 或空。", nameof(skillKey));
            }

            var handle = new SkillOwnerHandle(soldier.Id, soldier.LifecycleGeneration);

            // 同一活动实例重复绑定：幂等或拒绝。
            if (_activeLeases.TryGetValue(unitId, out SkillOwnerHandle existing))
            {
                if (existing.Equals(handle))
                {
                    // 同 handle：检查 skillKey 是否一致。
                    GeneralSkillState state = GetOrCreateState(unitId);
                    if (state.SkillKey == skillKey)
                    {
                        // 幂等成功。
                        return;
                    }

                    // 同活动实例已绑定到不同 skillKey：明确拒绝。
                    throw new InvalidOperationException(
                        $"UnitId={unitId} 已绑定到 skillKey='{state.SkillKey}'，" +
                        $"不可重复绑定到 skillKey='{skillKey}'。");
                }

                // 不同 handle（新实例）：旧租期应已 Unbind，此处防御性拒绝。
                throw new InvalidOperationException(
                    $"UnitId={unitId} 已有活动租期 {existing}，需先 Unbind 再绑定新租期 {handle}。");
            }

            // RegisterOwner + Attach，原子回滚。
            SkillOperationResult registerResult = _runner.RegisterOwner(handle);
            if (!registerResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"RegisterOwner 失败：{registerResult.Status} {registerResult.DiagnosticMessage}。" +
                    $"UnitId={unitId}, handle={handle}。");
            }

            SkillOperationResult attachResult = _runner.Attach(handle, skillKey);
            if (!attachResult.IsSuccess)
            {
                // 原子回滚：UnregisterOwner 恢复。
                _runner.UnregisterOwner(handle);
                throw new InvalidOperationException(
                    $"Attach 失败：{attachResult.Status} {attachResult.DiagnosticMessage}。" +
                    $"UnitId={unitId}, skillKey='{skillKey}'，已回滚 RegisterOwner。");
            }

            // 持久状态：RegisterOwner+Attach 全部成功后才创建/更新，保留已有 AttackCount。
            // 失败不得新增/改写持久 state，满足原子语义。
            GeneralSkillState persistent = GetOrCreateState(unitId);
            persistent.SkillKey = skillKey;

            _activeLeases[unitId] = handle;
        }

        // ====================================================================
        // Unbind —— 解除当前租期映射（保留 AttackCount）
        // ====================================================================

        /// <summary>
        /// 解除武将当前活动租期的 owner/runtime 映射，保留 AttackCount。
        /// </summary>
        /// <param name="unitId">局内单位权威 ID。</param>
        /// <param name="soldier">当前活动战斗实例（非 null）。</param>
        /// <remarks>
        /// <para>只清当前租期 owner/runtime 映射：<see cref="SkillRunner.UnregisterOwner"/>
        /// 取消运行激活并移除租期，但 <see cref="_states"/> 中的 AttackCount 保留。</para>
        /// <para><b>租期匹配守卫：</b>若当前活动租期与传入 soldier 不匹配（不同实例或已解除），
        /// 静默返回，不误清其他实例的租期。</para>
        /// </remarks>
        internal void Unbind(int unitId, SoldierBase soldier)
        {
            ThrowIfDisposed();

            if (soldier == null)
            {
                return;
            }

            if (!_activeLeases.TryGetValue(unitId, out SkillOwnerHandle current))
            {
                return;
            }

            var handle = new SkillOwnerHandle(soldier.Id, soldier.LifecycleGeneration);
            if (!current.Equals(handle))
            {
                // 传入的 soldier 不是当前租期的实例，静默返回。
                return;
            }

            _runner.UnregisterOwner(handle);
            _activeLeases.Remove(unitId);
        }

        // ====================================================================
        // TryActivateInsteadOfAttack —— 攻击槽替换为技能激活
        // ====================================================================

        /// <summary>
        /// 尝试用武将主动技能替换当前攻击槽。
        /// </summary>
        /// <param name="soldier">当前活动战斗实例。</param>
        /// <returns>
        /// true=技能已成功激活（AttackCount 清零）；false=未激活（未绑定、租期不匹配、
        /// 未达阈值、Busy、OnCooldown 等，AttackCount 不清零）。
        /// </returns>
        /// <remarks>
        /// <para><b>激活条件：</b>当前租期匹配、AttackCount&gt;=definition.TriggerAttackCount 时
        /// 用 <see cref="SkillActivationPlan"/>(0, 1) 激活。</para>
        /// <para><b>清零语义：</b>仅 <see cref="SkillOperationStatus.Success"/> 清零 AttackCount
        /// 并返回 true；任何失败不清零返回 false。</para>
        /// </remarks>
        internal bool TryActivateInsteadOfAttack(SoldierBase soldier)
        {
            if (_disposed || soldier == null)
            {
                return false;
            }

            int unitId = FindUnitIdBySoldier(soldier);
            if (unitId <= 0)
            {
                return false;
            }

            if (!_activeLeases.TryGetValue(unitId, out SkillOwnerHandle current))
            {
                return false;
            }

            var handle = new SkillOwnerHandle(soldier.Id, soldier.LifecycleGeneration);
            if (!current.Equals(handle))
            {
                // 租期不匹配（stale generation）。
                return false;
            }

            GeneralSkillState state = GetOrCreateState(unitId);
            if (string.IsNullOrEmpty(state.SkillKey))
            {
                return false;
            }

            if (!_catalog.TryGetByKey(state.SkillKey, out SkillDefinitionSnapshot definition))
            {
                return false;
            }

            int trigger = definition.TriggerAttackCount ?? 0;
            if (trigger <= 0 || state.AttackCount < trigger)
            {
                return false;
            }

            var plan = new SkillActivationPlan(0, 1);
            SkillOperationResult result = _runner.Activate(handle, state.SkillKey, plan);
            if (result.IsSuccess)
            {
                state.AttackCount = 0;
                return true;
            }

            return false;
        }

        // ====================================================================
        // OnBasicAttack —— 普通攻击后累计
        // ====================================================================

        /// <summary>
        /// 普通攻击完成后累计武将的攻击计数。
        /// </summary>
        /// <param name="soldier">完成普通攻击的战斗实例。</param>
        /// <remarks>
        /// 仅给已绑定武将累计（未绑定或租期不匹配时静默跳过）。
        /// </remarks>
        internal void OnBasicAttack(SoldierBase soldier)
        {
            if (_disposed || soldier == null)
            {
                return;
            }

            int unitId = FindUnitIdBySoldier(soldier);
            if (unitId <= 0)
            {
                return;
            }

            if (!_activeLeases.TryGetValue(unitId, out SkillOwnerHandle current))
            {
                return;
            }

            var handle = new SkillOwnerHandle(soldier.Id, soldier.LifecycleGeneration);
            if (!current.Equals(handle))
            {
                return;
            }

            GeneralSkillState state = GetOrCreateState(unitId);
            state.AttackCount += 1;
        }

        // ====================================================================
        // Clear / Dispose —— 清局
        // ====================================================================

        /// <summary>
        /// 清理全部 owner 租期与所有累计计数（幂等）。
        /// </summary>
        internal void Clear()
        {
            if (_disposed)
            {
                return;
            }

            ClearActiveLeases();
            _states.Clear();
        }

        /// <summary>
        /// 清理全部状态并进入已释放态；幂等，清理后拒绝新操作。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            ClearActiveLeases();
            _states.Clear();
            _disposed = true;
        }

        // ====================================================================
        // 内部实现
        // ====================================================================

        /// <summary>清理全部活动租期（UnregisterOwner），不清持久状态。</summary>
        private void ClearActiveLeases()
        {
            if (_activeLeases.Count == 0)
            {
                return;
            }

            // 收集 handle 列表，避免遍历中修改。
            var handles = new List<SkillOwnerHandle>(_activeLeases.Values);
            _activeLeases.Clear();
            for (int i = 0; i < handles.Count; i++)
            {
                _runner.UnregisterOwner(handles[i]);
            }
        }

        /// <summary>获取或创建 UnitId 的持久状态（保留已有 AttackCount）。</summary>
        private GeneralSkillState GetOrCreateState(int unitId)
        {
            if (!_states.TryGetValue(unitId, out GeneralSkillState state))
            {
                state = new GeneralSkillState();
                _states[unitId] = state;
            }

            return state;
        }

        /// <summary>
        /// 按 soldier 实例查找已绑定的 UnitId；未找到返回 0。
        /// </summary>
        private int FindUnitIdBySoldier(SoldierBase soldier)
        {
            var handle = new SkillOwnerHandle(soldier.Id, soldier.LifecycleGeneration);
            foreach (var pair in _activeLeases)
            {
                if (pair.Value.Equals(handle))
                {
                    return pair.Key;
                }
            }

            return 0;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GeneralSkillRuntime));
            }
        }

        /// <summary>武将技能持久状态：SkillKey 与普通攻击累计计数。</summary>
        private sealed class GeneralSkillState
        {
            internal string SkillKey;
            internal int AttackCount;
        }
    }
}
