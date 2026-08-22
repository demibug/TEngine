using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 槽位领域：SlotDropPlan —— 换槽/合并/互换事务计划
    // ----------------------------------------------------------------------------
    // 职责（修复阶段 P0：规则事务与唯一真值）：
    //   保存一次拖放事务的完整信息，只表达"计划"，不直接修改状态。
    //   BattleInputController 按"Plan → Prepare Runtime → Commit Board → Commit Runtime"
    //   顺序执行：先经 UnitSlotBoard.TryPlanDrop 生成计划（只读校验，不修改任何状态），
    //   再准备战斗实例（配置/对象池/等级），全部就绪后才 Commit 提交槽位状态。
    //   任何一步失败都返回失败，槽位不变化（原子事务）。
    //
    // 操作类型（统一拖放规则）：
    //   Move —— 空目标：源单位移动到目标槽，源槽变空。
    //   Merge —— 目标占用且满足合并条件（同兵种/同等级/未满级）：目标升一级，源消失。
    //   Swap —— 目标占用但不满足合并条件（不同兵种/不同等级/满级）：两单位互换位置。
    //   四种操作都以 SourceBefore/TargetBefore → SourceAfter/TargetAfter 表达事务前后快照。
    //
    // 不变量：
    //   1. 本类型只描述计划，不触发任何状态修改。
    //   2. IsMerge 为 true 时，源单位被消耗（消失），结果保留目标 UnitId 与目标 SlotId。
    //   3. IsSwap 为 true 时，源/目标单位互换槽位，双方属性（含冷却）随单位迁移。
    //   4. 提交时所有字段快照自 TryPlanDrop 生成那一刻起不可变。
    // ============================================================================

    /// <summary>
    /// 一次拖放事务的操作类型（统一拖放规则：Move / Merge / Swap）。
    /// </summary>
    /// <remarks>
    /// <para>由 <see cref="UnitSlotBoard.TryPlanDrop"/> 按目标槽占用与合并条件决定：
    /// 空目标走 Move；同兵种（Kind/SoldierType 权威）且同等级、未满级走 Merge；
    /// 其余占用目标（不同兵种/不同等级/满级）走 Swap。SameSlot/CrossSide 仍拒绝。</para>
    /// </remarks>
    internal enum SlotDropOperationType
    {
        /// <summary>空目标：源单位移动到目标槽，源槽变空。</summary>
        Move = 0,

        /// <summary>目标占用且满足合并条件：目标升一级，源单位消失。</summary>
        Merge = 1,

        /// <summary>目标占用但不满足合并条件：两单位互换位置。</summary>
        Swap = 2,

        /// <summary>两个待上场武将字按左+右顺序命中有序配方，合成为目标 ID 的武将。</summary>
        Synthesize = 3,
    }

    /// <summary>一次拖放计划中的单槽不可变变更。</summary>
    internal readonly struct SlotDropMutation
    {
        public readonly UnitSlotId SlotId;
        public readonly BattleUnit? Before;
        public readonly BattleUnit? After;

        internal SlotDropMutation(UnitSlotId slotId, BattleUnit? before, BattleUnit? after)
        {
            SlotId = slotId;
            Before = before;
            After = after;
        }
    }

    /// <summary>
    /// 一次换槽/合并/互换事务计划（只表达计划，不直接修改状态）。
    /// </summary>
    /// <remarks>
    /// <para>由 <see cref="UnitSlotBoard.TryPlanDrop"/> 生成：执行全部只读校验
    /// （槽位合法、源非空、同阵营、操作类型判定），成功后把源/目标槽修改前后的
    /// 快照与操作类型写入本计划。计划不修改任何状态。</para>
    /// <para><see cref="BattleInputController"/> 据计划准备战斗实例，全部就绪后
    /// 调用 <see cref="UnitSlotBoard.CommitDrop"/> 一次性提交槽位状态。</para>
    /// </remarks>
    internal readonly struct SlotDropPlan
    {
        /// <summary>源槽位标识。</summary>
        public readonly UnitSlotId SourceSlotId;

        /// <summary>目标槽位标识。</summary>
        public readonly UnitSlotId TargetSlotId;

        /// <summary>操作类型（Move / Merge / Swap）。</summary>
        public readonly SlotDropOperationType OperationType;

        /// <summary>源槽事务后的占用单位（空槽为 null；Swap 时为原目标单位）。</summary>
        public readonly BattleUnit? SourceAfter;

        /// <summary>目标槽事务后的占用单位（空槽为 null；Merge 时为升级后的目标单位）。</summary>
        public readonly BattleUnit? TargetAfter;

        /// <summary>源槽修改前的占用（提交后回滚/表现用）。</summary>
        public readonly BattleUnit? SourceBefore;

        /// <summary>目标槽修改前的占用（提交后回滚/表现用）。</summary>
        public readonly BattleUnit? TargetBefore;

        /// <summary>槽位面板修订号（提交时校验，防止并发版本冲突）。</summary>
        public readonly int BoardRevision;

        private readonly SlotDropMutation[] _mutations;

        /// <summary>本事务涉及的全部槽位变更；普通单位为两槽，双格 General 最多四槽。</summary>
        public IReadOnlyList<SlotDropMutation> Mutations =>
            _mutations ?? Array.Empty<SlotDropMutation>();

        /// <summary>是否发生合并（true=目标升一级，源消失；false=换槽/互换）。</summary>
        public bool IsMerge => OperationType == SlotDropOperationType.Merge;

        /// <summary>是否发生互换（true=源/目标单位互换位置，不消耗任何单位）。</summary>
        public bool IsSwap => OperationType == SlotDropOperationType.Swap;

        public bool IsSynthesize => OperationType == SlotDropOperationType.Synthesize;

        /// <summary>事务后目标槽占用单位（便捷属性委托 TargetAfter）。</summary>
        public BattleUnit? ResultUnit => TargetAfter;

        /// <summary>被消耗的源单位（合并时存在；Move/Swap 时为 null）。</summary>
        public BattleUnit? ConsumedSourceUnit => IsMerge || IsSynthesize ? SourceBefore : null;

        /// <summary>构造事务计划。</summary>
        internal SlotDropPlan(
            UnitSlotId sourceSlotId,
            UnitSlotId targetSlotId,
            SlotDropOperationType operationType,
            BattleUnit? sourceAfter,
            BattleUnit? targetAfter,
            BattleUnit? sourceBefore,
            BattleUnit? targetBefore,
            int boardRevision)
        {
            SourceSlotId = sourceSlotId;
            TargetSlotId = targetSlotId;
            OperationType = operationType;
            SourceAfter = sourceAfter;
            TargetAfter = targetAfter;
            SourceBefore = sourceBefore;
            TargetBefore = targetBefore;
            BoardRevision = boardRevision;
            _mutations = new[]
            {
                new SlotDropMutation(sourceSlotId, sourceBefore, sourceAfter),
                new SlotDropMutation(targetSlotId, targetBefore, targetAfter),
            };
        }

        internal SlotDropPlan(
            UnitSlotId sourceSlotId,
            UnitSlotId targetSlotId,
            SlotDropOperationType operationType,
            IReadOnlyList<SlotDropMutation> mutations,
            int boardRevision)
        {
            if (mutations == null || mutations.Count == 0)
            {
                throw new ArgumentException("槽位事务至少需要一个变更", nameof(mutations));
            }

            SourceSlotId = sourceSlotId;
            TargetSlotId = targetSlotId;
            OperationType = operationType;
            BoardRevision = boardRevision;
            _mutations = new SlotDropMutation[mutations.Count];
            SourceBefore = null;
            SourceAfter = null;
            TargetBefore = null;
            TargetAfter = null;
            for (int index = 0; index < mutations.Count; index++)
            {
                SlotDropMutation mutation = mutations[index];
                _mutations[index] = mutation;
                if (mutation.SlotId.Id == sourceSlotId.Id)
                {
                    SourceBefore = mutation.Before;
                    SourceAfter = mutation.After;
                }
                if (mutation.SlotId.Id == targetSlotId.Id)
                {
                    TargetBefore = mutation.Before;
                    TargetAfter = mutation.After;
                }
            }
        }

        /// <inheritdoc/>
        public override string ToString()
            => $"[SlotDropPlan {SourceSlotId} -> {TargetSlotId} Op={OperationType} " +
               $"Source={SourceAfter?.ToString() ?? "Empty"} " +
               $"Target={TargetAfter?.ToString() ?? "Empty"}]";
    }
}
