using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>对手侧单次部署候选选择器。</summary>
    internal sealed class OpponentAiPlacementPlanner
    {
        private readonly MapData _map;
        private readonly IRandomSource _randomSource;
        private readonly UnitLevelService _levelService;

        internal OpponentAiPlacementPlanner(
            MapData map,
            IRandomSource randomSource,
            UnitLevelService levelService)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _randomSource = randomSource ?? throw new ArgumentNullException(nameof(randomSource));
            _levelService = levelService ?? throw new ArgumentNullException(nameof(levelService));
        }

        internal bool TryChooseTarget(
            BattleUnit source,
            IReadOnlyList<UnitSlot> battleSlots,
            OpponentAiProfileSnapshot profile,
            out UnitSlotId target)
        {
            target = UnitSlotId.Invalid;

            if (profile.PlacementPolicy == OpponentAiPlacementPolicy.RouteAware
                && source.Kind == UnitKind.Soldier
                && !_levelService.IsMaxLevel(source.Level))
            {
                for (int i = 0; i < battleSlots.Count; i++)
                {
                    UnitSlot slot = battleSlots[i];
                    if (!slot.Occupant.HasValue)
                    {
                        continue;
                    }

                    BattleUnit occupant = slot.Occupant.Value;
                    if (occupant.Kind == UnitKind.Soldier
                        && occupant.SoldierType == source.SoldierType
                        && occupant.Level == source.Level)
                    {
                        target = slot.SlotId;
                        return true;
                    }
                }
            }

            var emptyCandidates = new List<UnitSlot>();
            for (int i = 0; i < battleSlots.Count; i++)
            {
                if (battleSlots[i].IsEmpty)
                {
                    emptyCandidates.Add(battleSlots[i]);
                }
            }

            if (emptyCandidates.Count == 0)
            {
                return false;
            }

            if (profile.PlacementPolicy == OpponentAiPlacementPolicy.RouteAware)
            {
                emptyCandidates.Sort(CompareRouteScore);
                int window = profile.CandidateTopN > 0
                    ? Math.Min(profile.CandidateTopN, emptyCandidates.Count)
                    : 1;
                int selected = SelectIndex(window);
                target = emptyCandidates[selected].SlotId;
                return true;
            }

            target = emptyCandidates[SelectIndex(emptyCandidates.Count)].SlotId;
            return true;
        }

        private int CompareRouteScore(UnitSlot left, UnitSlot right)
        {
            GetRouteScore(left.SlotId.GridPosition, out int leftAdjacency, out int leftDistance);
            GetRouteScore(right.SlotId.GridPosition, out int rightAdjacency, out int rightDistance);

            int adjacencyComparison = rightAdjacency.CompareTo(leftAdjacency);
            if (adjacencyComparison != 0)
            {
                return adjacencyComparison;
            }

            int distanceComparison = leftDistance.CompareTo(rightDistance);
            return distanceComparison != 0
                ? distanceComparison
                : left.SlotId.Id.CompareTo(right.SlotId.Id);
        }

        private void GetRouteScore(GridPosition candidate, out int adjacency, out int distance)
        {
            IReadOnlyList<GridPosition> path = _map.GetPathForSide(playerSide: false);
            adjacency = 0;
            distance = int.MaxValue;
            if (path == null || path.Count == 0)
            {
                return;
            }

            int start = (int)Math.Floor(path.Count * 0.15f);
            int end = Math.Max(start, (int)Math.Ceiling(path.Count * 0.85f) - 1);
            end = Math.Min(end, path.Count - 1);

            for (int i = 0; i < path.Count; i++)
            {
                GridPosition route = path[i];
                int manhattan = Math.Abs(candidate.X - route.X) + Math.Abs(candidate.Y - route.Y);
                if (manhattan == 1)
                {
                    adjacency++;
                }

                if (i >= start && i <= end && manhattan < distance)
                {
                    distance = manhattan;
                }
            }
        }

        private int SelectIndex(int count)
        {
            float value = _randomSource.NextUnit();
            int selected = (int)Math.Floor(value * count);
            return Math.Max(0, Math.Min(selected, count - 1));
        }
    }
}
