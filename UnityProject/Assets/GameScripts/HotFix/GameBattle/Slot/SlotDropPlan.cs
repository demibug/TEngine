namespace GameBattle
{
    // ============================================================================
    // 槽位领域：SlotDropPlan —— 换槽/合并事务计划
    // ----------------------------------------------------------------------------
    // 职责（修复阶段 P0：规则事务与唯一真值）：
    //   保存一次换槽/合并事务的完整信息，只表达"计划"，不直接修改状态。
    //   BattleInputController 按"Plan → Prepare Runtime → Commit Board → Commit Runtime"
    //   顺序执行：先经 UnitSlotBoard.TryPlanDrop 生成计划（只读校验，不修改任何状态），
    //   再准备战斗实例（配置/对象池/等级），全部就绪后才 Commit 提交槽位状态。
    //   任何一步失败都返回失败，槽位不变化（原子事务）。
    //
    // 不变量：
    //   1. 本类型只描述计划，不触发任何状态修改。
    //   2. IsMerge 为 true 时，源单位被消耗（消失），结果保留目标 UnitId 与目标 SlotId。
    //   3. IsMerge 为 false 时，源单位移动到目标槽，源槽变空。
    //   4. 提交时所有字段快照自 TryPlanDrop 生成那一刻起不可变。
    // ============================================================================

    /// <summary>
    /// 一次换槽/合并事务计划（只表达计划，不直接修改状态）。
    /// </summary>
    /// <remarks>
    /// <para>由 <see cref="UnitSlotBoard.TryPlanDrop"/> 生成：执行全部只读校验
    /// （槽位合法、源非空、同阵营、同兵种/等级/未满级等），成功后把源/目标槽
    /// 修改前后的快照、结果单位、是否合并写入本计划。计划不修改任何状态。</para>
    /// <para><see cref="BattleInputController"/> 据计划准备战斗实例，全部就绪后
    /// 调用 <see cref="UnitSlotBoard.CommitDrop"/> 一次性提交槽位状态。</para>
    /// </remarks>
    internal readonly struct SlotDropPlan
    {
        /// <summary>源槽位标识。</summary>
        public readonly UnitSlotId SourceSlotId;

        /// <summary>目标槽位标识。</summary>
        public readonly UnitSlotId TargetSlotId;

        /// <summary>是否发生合并（true=目标升一级，源消失；false=换槽）。</summary>
        public readonly bool IsMerge;

        /// <summary>换槽/合并后的目标槽占用单位（提交后有效）。</summary>
        public readonly BattleUnit? ResultUnit;

        /// <summary>被消耗的源单位（合并时存在；换槽时为 null，源单位移动到目标槽）。</summary>
        public readonly BattleUnit? ConsumedSourceUnit;

        /// <summary>源槽修改前的占用（提交后回滚/表现用）。</summary>
        public readonly BattleUnit? SourceBefore;

        /// <summary>目标槽修改前的占用（提交后回滚/表现用）。</summary>
        public readonly BattleUnit? TargetBefore;

        /// <summary>槽位面板修订号（提交时校验，防止并发版本冲突）。</summary>
        public readonly int BoardRevision;

        /// <summary>构造事务计划。</summary>
        internal SlotDropPlan(
            UnitSlotId sourceSlotId,
            UnitSlotId targetSlotId,
            bool isMerge,
            BattleUnit? resultUnit,
            BattleUnit? consumedSourceUnit,
            BattleUnit? sourceBefore,
            BattleUnit? targetBefore,
            int boardRevision)
        {
            SourceSlotId = sourceSlotId;
            TargetSlotId = targetSlotId;
            IsMerge = isMerge;
            ResultUnit = resultUnit;
            ConsumedSourceUnit = consumedSourceUnit;
            SourceBefore = sourceBefore;
            TargetBefore = targetBefore;
            BoardRevision = boardRevision;
        }

        /// <inheritdoc/>
        public override string ToString()
            => $"[SlotDropPlan {SourceSlotId} -> {TargetSlotId} IsMerge={IsMerge} " +
               $"Result={(ResultUnit.HasValue ? ResultUnit.Value.ToString() : "Empty")}]";
    }
}
