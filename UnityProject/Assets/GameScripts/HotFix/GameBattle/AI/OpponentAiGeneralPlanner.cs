using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>敌方武将两字配对计划。</summary>
    internal sealed class GeneralSynthesisPlan
    {
        internal GeneralConfigSnapshot Definition { get; }
        internal UnitSlot FirstSource { get; }
        internal UnitSlot SecondSource { get; }
        internal UnitSlot FirstTarget { get; }
        internal UnitSlot SecondTarget { get; }

        internal GeneralSynthesisPlan(
            GeneralConfigSnapshot definition,
            UnitSlot firstSource,
            UnitSlot secondSource,
            UnitSlot firstTarget,
            UnitSlot secondTarget)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            FirstSource = firstSource;
            SecondSource = secondSource;
            FirstTarget = firstTarget;
            SecondTarget = secondTarget;
        }

        internal string Reason => "general_recipe_" + Definition.Name;
    }

    /// <summary>
    /// 根据当前快照规划有序武将配方。它只输出动作所需槽位，不改动棋盘。
    /// </summary>
    internal sealed class OpponentAiGeneralPlanner
    {
        private readonly GeneralCatalogSnapshot _catalog;

        internal OpponentAiGeneralPlanner(GeneralCatalogSnapshot catalog)
        {
            _catalog = catalog ?? new GeneralCatalogSnapshot(
                Array.Empty<GeneralConfigSnapshot>());
        }

        internal bool TryBuildPlan(
            OpponentAiBoardSnapshot snapshot,
            out GeneralSynthesisPlan plan)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            IReadOnlyList<UnitSlot> parts = snapshot.FindGeneralParts(includeReserve: true);
            for (int i = 0; i < _catalog.Definitions.Count; i++)
            {
                GeneralConfigSnapshot definition = _catalog.Definitions[i];
                if (definition == null || snapshot.HasGeneral(definition.Index))
                {
                    continue;
                }

                if (TryFindPart(parts, definition.PartWords[0], out UnitSlot first)
                    && TryFindPartAfter(
                        parts,
                        definition.PartWords[1],
                        first.SlotId.Id,
                        out UnitSlot second)
                    && TryChooseTargets(
                        snapshot,
                        first,
                        second,
                        definition,
                        out UnitSlot firstTarget,
                        out UnitSlot secondTarget))
                {
                    plan = new GeneralSynthesisPlan(
                        definition,
                        first,
                        second,
                        firstTarget,
                        secondTarget);
                    return true;
                }
            }

            plan = null;
            return false;
        }

        private static bool TryChooseTargets(
            OpponentAiBoardSnapshot snapshot,
            UnitSlot first,
            UnitSlot second,
            GeneralConfigSnapshot definition,
            out UnitSlot firstTarget,
            out UnitSlot secondTarget)
        {
            firstTarget = default;
            secondTarget = default;

            // 两张字牌在 Reserve：寻找两个横向相邻空战斗格，严格保持左字/右字。
            if (first.SlotId.Zone == SlotZone.Reserve
                && second.SlotId.Zone == SlotZone.Reserve)
            {
                return TryFindEmptyHorizontalPair(
                    snapshot,
                    out firstTarget,
                    out secondTarget);
            }

            // 左字已在场，右字仍在 Reserve：右字只能落在左侧字的右边。
            if (first.SlotId.Zone == SlotZone.Battle
                && second.SlotId.Zone == SlotZone.Reserve)
            {
                GridPosition rightPosition = new GridPosition(
                    first.SlotId.GridPosition.X + 1,
                    first.SlotId.GridPosition.Y);
                if (TryFindEmptySlot(snapshot.BattleSlots, rightPosition, out secondTarget))
                {
                    firstTarget = first;
                    return true;
                }
            }

            // 右字已在场，左字仍在 Reserve：左字只能落在右侧字的左边。
            if (first.SlotId.Zone == SlotZone.Reserve
                && second.SlotId.Zone == SlotZone.Battle)
            {
                GridPosition leftPosition = new GridPosition(
                    second.SlotId.GridPosition.X - 1,
                    second.SlotId.GridPosition.Y);
                if (TryFindEmptySlot(snapshot.BattleSlots, leftPosition, out firstTarget))
                {
                    secondTarget = second;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindEmptyHorizontalPair(
            OpponentAiBoardSnapshot snapshot,
            out UnitSlot left,
            out UnitSlot right)
        {
            for (int i = 0; i < snapshot.BattleSlots.Count; i++)
            {
                UnitSlot candidate = snapshot.BattleSlots[i];
                if (!candidate.IsEmpty)
                {
                    continue;
                }

                GridPosition rightPosition = new GridPosition(
                    candidate.SlotId.GridPosition.X + 1,
                    candidate.SlotId.GridPosition.Y);
                if (TryFindEmptySlot(snapshot.BattleSlots, rightPosition, out right))
                {
                    left = candidate;
                    return true;
                }
            }

            left = default;
            right = default;
            return false;
        }

        private static bool TryFindPart(
            IReadOnlyList<UnitSlot> parts,
            string text,
            out UnitSlot result)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Occupant.HasValue
                    && parts[i].Occupant.Value.GeneralPartText == text)
                {
                    result = parts[i];
                    return true;
                }
            }

            result = default;
            return false;
        }

        private static bool TryFindPartAfter(
            IReadOnlyList<UnitSlot> parts,
            string text,
            int excludedSlotId,
            out UnitSlot result)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].SlotId.Id != excludedSlotId
                    && parts[i].Occupant.HasValue
                    && parts[i].Occupant.Value.GeneralPartText == text)
                {
                    result = parts[i];
                    return true;
                }
            }

            result = default;
            return false;
        }

        private static bool TryFindEmptySlot(
            IReadOnlyList<UnitSlot> slots,
            GridPosition position,
            out UnitSlot result)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].SlotId.GridPosition == position && slots[i].IsEmpty)
                {
                    result = slots[i];
                    return true;
                }
            }

            result = default;
            return false;
        }
    }
}
