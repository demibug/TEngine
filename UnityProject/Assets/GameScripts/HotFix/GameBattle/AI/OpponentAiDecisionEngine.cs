using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>
    /// 纯 AI 规划层：输入不可变快照，输出按优先级排列的动作队列。
    /// </summary>
    internal sealed class OpponentAiDecisionEngine
    {
        private readonly OpponentAiPlacementPlanner _placementPlanner;
        private readonly OpponentAiGeneralPlanner _generalPlanner;
        private readonly OpponentAiTemplateResolver _templateResolver;
        private readonly UnitLevelService _levelService;
        private readonly OpponentAiValueEvaluator _valueEvaluator;

        internal OpponentAiDecisionEngine(
            MapData map,
            IRandomSource strategyRandom,
            UnitLevelService levelService,
            GeneralCatalogSnapshot generalCatalog = null)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            _levelService = levelService ?? throw new ArgumentNullException(nameof(levelService));
            _placementPlanner = new OpponentAiPlacementPlanner(
                map,
                strategyRandom ?? throw new ArgumentNullException(nameof(strategyRandom)),
                levelService);
            _generalPlanner = new OpponentAiGeneralPlanner(generalCatalog);
            _valueEvaluator = new OpponentAiValueEvaluator();
            _templateResolver = new OpponentAiTemplateResolver(
                map,
                strategyRandom,
                _valueEvaluator);
        }

        internal OpponentAiValueEvaluator ValueEvaluator => _valueEvaluator;

        internal IReadOnlyList<OpponentAiAction> BuildPlan(
            OpponentAiBoardSnapshot snapshot,
            OpponentAiProfileSnapshot profile)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var actions = new List<OpponentAiAction>();

            if (profile.AllowActiveMerge)
            {
                AddMergeActions(actions, snapshot);
                if (actions.Count > 0)
                {
                    return actions.AsReadOnly();
                }
            }

            if (profile.AllowGeneralParts
                && _generalPlanner.TryBuildPlan(snapshot, out GeneralSynthesisPlan generalPlan))
            {
                // 只为仍在 Reserve 的字部件生成动作。若左字已经在场，
                // 再把它拖到自己所在的战斗槽会得到 SameSlot；若右字已经在场，
                // 只需把左字放到其左侧即可触发棋盘层的原子合成。
                if (generalPlan.FirstSource.SlotId.Zone == SlotZone.Reserve)
                {
                    actions.Add(new OpponentAiAction(
                        generalPlan.SecondSource.SlotId.Zone == SlotZone.Battle
                            ? OpponentAiActionType.SynthesizeGeneral
                            : OpponentAiActionType.Deploy,
                        generalPlan.FirstSource.SlotId.Id,
                        generalPlan.FirstTarget.SlotId.Id,
                        secondarySourceSlotId: generalPlan.SecondSource.SlotId.Id,
                        reason: generalPlan.Reason,
                        expectedUnitId: generalPlan.FirstSource.OccupantUnitId));
                }

                if (generalPlan.SecondSource.SlotId.Zone == SlotZone.Reserve)
                {
                    actions.Add(new OpponentAiAction(
                        OpponentAiActionType.SynthesizeGeneral,
                        generalPlan.SecondSource.SlotId.Id,
                        generalPlan.SecondTarget.SlotId.Id,
                        reason: generalPlan.Reason,
                        expectedUnitId: generalPlan.SecondSource.OccupantUnitId));
                }

                if (actions.Count == 0)
                {
                    return new[] { OpponentAiAction.Wait("general_parts_already_in_battle") };
                }

                return actions.AsReadOnly();
            }

            AddDeployActions(actions, snapshot, profile);
            if (actions.Count == 0 && profile.EnableReclaim)
            {
                IReadOnlyList<OpponentAiAction> reclaimActions =
                    BuildReclaimPlan(snapshot, profile);
                for (int i = 0; i < reclaimActions.Count; i++)
                {
                    actions.Add(reclaimActions[i]);
                }
            }

            if (actions.Count == 0)
            {
                actions.Add(OpponentAiAction.Wait());
            }

            return actions.AsReadOnly();
        }

        /// <summary>
        /// 构造 raw UG/FastDeploy 的候选部署计划；调用方负责概率、金币和 one-shot guard。
        /// </summary>
        internal IReadOnlyList<OpponentAiAction> BuildFastDeployPlan(
            OpponentAiBoardSnapshot snapshot,
            OpponentAiProfileSnapshot profile)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (profile == null || !profile.AllowFastDeploy || profile.FastDeployMaxUnits <= 0)
            {
                return Array.Empty<OpponentAiAction>();
            }

            var actions = new List<OpponentAiAction>();
            AddDeployActions(
                actions,
                snapshot,
                profile,
                OpponentAiActionType.FastDeploy,
                profile.FastDeployMaxUnits);
            return actions.AsReadOnly();
        }

        /// <summary>
        /// 构造 raw QX/Reclaim 的稳定回收候选；只选择唯一的对手战斗实体。
        /// </summary>
        internal IReadOnlyList<OpponentAiAction> BuildReclaimPlan(
            OpponentAiBoardSnapshot snapshot,
            OpponentAiProfileSnapshot profile)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (profile == null || !profile.EnableReclaim)
            {
                return Array.Empty<OpponentAiAction>();
            }

            UnitSlot selected = default;
            bool found = false;
            for (int i = 0; i < snapshot.BattleSlots.Count; i++)
            {
                UnitSlot candidate = snapshot.BattleSlots[i];
                if (!candidate.Occupant.HasValue
                    || candidate.Occupant.Value.Side
                    || (candidate.Occupant.Value.Kind == UnitKind.General
                        && !candidate.Occupant.Value.IsGeneralPrimaryCell)
                    || (candidate.Occupant.Value.Kind != UnitKind.Soldier
                        && candidate.Occupant.Value.Kind != UnitKind.General))
                {
                    continue;
                }

                if (!found
                    || candidate.Occupant.Value.Level < selected.Occupant.Value.Level
                    || (candidate.Occupant.Value.Level == selected.Occupant.Value.Level
                        && candidate.SlotId.Id < selected.SlotId.Id))
                {
                    selected = candidate;
                    found = true;
                }
            }

            if (!found)
            {
                return Array.Empty<OpponentAiAction>();
            }

            return new[]
            {
                new OpponentAiAction(
                    OpponentAiActionType.Replace,
                    sourceSlotId: selected.SlotId.Id,
                    reason: "reclaim_lowest_level_battle_unit",
                    expectedUnitId: selected.Occupant.Value.UnitId),
            };
        }

        private void AddMergeActions(
            List<OpponentAiAction> actions,
            OpponentAiBoardSnapshot snapshot)
        {
            var consumed = new HashSet<int>();
            for (int i = 0; i < snapshot.BattleSlots.Count; i++)
            {
                UnitSlot sourceSlot = snapshot.BattleSlots[i];
                if (!sourceSlot.Occupant.HasValue
                    || consumed.Contains(sourceSlot.OccupantUnitId))
                {
                    continue;
                }

                BattleUnit source = sourceSlot.Occupant.Value;
                IReadOnlyList<UnitSlot> targets = snapshot.FindMergeTargets(source);
                for (int j = 0; j < targets.Count; j++)
                {
                    UnitSlot target = targets[j];
                    if (consumed.Contains(target.OccupantUnitId))
                    {
                        continue;
                    }

                    actions.Add(new OpponentAiAction(
                        OpponentAiActionType.Merge,
                        sourceSlot.SlotId.Id,
                        target.SlotId.Id,
                        reason: "merge_same_type_same_level",
                        expectedUnitId: source.UnitId));
                    consumed.Add(source.UnitId);
                    consumed.Add(target.OccupantUnitId);
                    break;
                }
            }
        }

        private void AddDeployActions(
            List<OpponentAiAction> actions,
            OpponentAiBoardSnapshot snapshot,
            OpponentAiProfileSnapshot profile,
            OpponentAiActionType actionType = OpponentAiActionType.Deploy,
            int maxActions = int.MaxValue)
        {
            var reservedTargets = new HashSet<int>();
            for (int i = 0; i < snapshot.ReserveSlots.Count; i++)
            {
                UnitSlot sourceSlot = snapshot.ReserveSlots[i];
                if (!sourceSlot.Occupant.HasValue
                    || !CanDeployToBattle(sourceSlot.Occupant.Value))
                {
                    continue;
                }

                IReadOnlyList<UnitSlot> candidates = GetAvailableBattleSlots(
                    snapshot.BattleSlots,
                    reservedTargets);
                string reason = string.Empty;
                UnitSlotId target;
                bool hasTarget = profile.AllowTemplatePlacement
                    ? _templateResolver.TryChooseTarget(
                        sourceSlot.Occupant.Value,
                        snapshot,
                        candidates,
                        profile,
                        out target,
                        out reason)
                    : _placementPlanner.TryChooseTarget(
                        sourceSlot.Occupant.Value,
                        candidates,
                        profile,
                        out target);
                if (!hasTarget)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(reason))
                {
                    reason = sourceSlot.Occupant.Value.Kind == UnitKind.GeneralPart
                        ? "general_part_route_candidate"
                        : profile.EnableValueEvaluation
                            ? "value_evaluated_route_candidate"
                            : "route_candidate";
                }
                actions.Add(new OpponentAiAction(
                    actionType,
                    sourceSlot.SlotId.Id,
                    target.Id,
                    reason: reason,
                    expectedUnitId: sourceSlot.Occupant.Value.UnitId));
                reservedTargets.Add(target.Id);

                // Preserve one-action-at-a-time ordinary behavior for low difficulty;
                // high difficulty can safely queue the whole visible hand projection.
                if (actionType == OpponentAiActionType.Deploy
                    && !profile.AllowActiveMerge
                    && !profile.AllowTemplatePlacement
                    && actions.Count > 0)
                {
                    break;
                }

                if (actions.Count >= maxActions)
                {
                    break;
                }
            }
        }

        private static bool CanDeployToBattle(BattleUnit unit)
            => unit.Kind == UnitKind.Soldier || unit.Kind == UnitKind.GeneralPart;

        private static IReadOnlyList<UnitSlot> GetAvailableBattleSlots(
            IReadOnlyList<UnitSlot> slots,
            HashSet<int> reservedTargets)
        {
            var available = new List<UnitSlot>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsEmpty && !reservedTargets.Contains(slots[i].SlotId.Id))
                {
                    available.Add(slots[i]);
                }
            }

            return available.AsReadOnly();
        }

    }
}
