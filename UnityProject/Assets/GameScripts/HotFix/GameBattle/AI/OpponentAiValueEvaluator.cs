using System;

namespace GameBattle
{
    /// <summary>
    /// 对手单位/落点价值评估。它只读取快照，供难度 2/3 的排序使用。
    /// </summary>
    internal sealed class OpponentAiValueEvaluator
    {
        internal int EvaluateUnit(BattleUnit unit)
        {
            switch (unit.Kind)
            {
                case UnitKind.General:
                    return 100 + unit.Level * 12;
                case UnitKind.GeneralPart:
                    return 35;
                case UnitKind.Farmer:
                    return 22;
                case UnitKind.Prop:
                    return unit.IsShovel ? 30 : 10;
                default:
                    return 10 + Math.Max(1, unit.Level) * 10;
            }
        }

        internal int EvaluatePlacement(
            BattleUnit unit,
            UnitSlot target,
            OpponentAiBoardSnapshot snapshot,
            bool templatePlacement)
        {
            if (target.SlotId.Zone != SlotZone.Battle)
            {
                return int.MinValue;
            }

            int score = EvaluateUnit(unit);
            int routeScore = snapshot.GetRouteScore(target.SlotId.GridPosition);
            if (routeScore != int.MaxValue)
            {
                score += Math.Max(0, 10 - routeScore * 2);
            }

            if (templatePlacement && unit.Kind == UnitKind.Soldier)
            {
                score += EvaluateNeighbourSupport(unit, target, snapshot);
            }

            if (unit.Kind == UnitKind.GeneralPart)
            {
                score += EvaluateGeneralPartPair(unit, target, snapshot);
            }

            return score;
        }

        internal bool IsLowValue(BattleUnit unit)
            => unit.Kind == UnitKind.Soldier && unit.Level <= 1;

        private int EvaluateNeighbourSupport(
            BattleUnit unit,
            UnitSlot target,
            OpponentAiBoardSnapshot snapshot)
        {
            int support = 0;
            var adjacent = snapshot.GetAdjacentCells(target.SlotId.GridPosition);
            for (int i = 0; i < adjacent.Count; i++)
            {
                for (int j = 0; j < snapshot.BattleSlots.Count; j++)
                {
                    UnitSlot neighbour = snapshot.BattleSlots[j];
                    if (neighbour.SlotId.GridPosition != adjacent[i]
                        || !neighbour.Occupant.HasValue)
                    {
                        continue;
                    }

                    BattleUnit neighbourUnit = neighbour.Occupant.Value;
                    if (neighbourUnit.Kind == UnitKind.Soldier
                        && neighbourUnit.SoldierType == unit.SoldierType)
                    {
                        support += 8;
                    }
                }
            }

            return support;
        }

        private int EvaluateGeneralPartPair(
            BattleUnit unit,
            UnitSlot target,
            OpponentAiBoardSnapshot snapshot)
        {
            int score = 0;
            var adjacent = snapshot.GetAdjacentCells(target.SlotId.GridPosition);
            for (int i = 0; i < adjacent.Count; i++)
            {
                for (int j = 0; j < snapshot.BattleSlots.Count; j++)
                {
                    UnitSlot neighbour = snapshot.BattleSlots[j];
                    if (!neighbour.Occupant.HasValue
                        || neighbour.SlotId.GridPosition != adjacent[i]
                        || neighbour.Occupant.Value.Kind != UnitKind.GeneralPart)
                    {
                        continue;
                    }

                    score += 20;
                }
            }

            return score;
        }
    }
}
