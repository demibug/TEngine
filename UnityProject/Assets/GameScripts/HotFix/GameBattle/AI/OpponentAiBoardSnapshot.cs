using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>
    /// 对手 AI 使用的不可变棋盘快照。快照复制槽位列表，规划期间不持有可变 Board 引用。
    /// </summary>
    internal sealed class OpponentAiBoardSnapshot
    {
        private readonly MapData _map;
        private readonly int _maxLevel;
        private readonly List<UnitSlot> _battleSlots;
        private readonly List<UnitSlot> _reserveSlots;

        internal OpponentAiBoardSnapshot(
            UnitSlotSnapshot boardSnapshot,
            MapData map,
            int maxLevel)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            _map = map;
            _maxLevel = maxLevel > 0 ? maxLevel : RecruitDefinitions.MaxLevel;
            _battleSlots = CopySlots(boardSnapshot.GetSlots(false, SlotZone.Battle));
            _reserveSlots = CopySlots(boardSnapshot.GetSlots(false, SlotZone.Reserve));
        }

        internal MapData Map => _map;
        internal int MaxLevel => _maxLevel;
        internal IReadOnlyList<UnitSlot> BattleSlots => _battleSlots;
        internal IReadOnlyList<UnitSlot> ReserveSlots => _reserveSlots;

        internal IReadOnlyList<BattleUnit> GetOpponentBattleUnits()
        {
            var units = new List<BattleUnit>();
            for (int i = 0; i < _battleSlots.Count; i++)
            {
                if (_battleSlots[i].Occupant.HasValue)
                {
                    units.Add(_battleSlots[i].Occupant.Value);
                }
            }

            return units.AsReadOnly();
        }

        internal IReadOnlyList<BattleUnit> GetOpponentReserveUnits()
        {
            var units = new List<BattleUnit>();
            for (int i = 0; i < _reserveSlots.Count; i++)
            {
                if (_reserveSlots[i].Occupant.HasValue)
                {
                    units.Add(_reserveSlots[i].Occupant.Value);
                }
            }

            return units.AsReadOnly();
        }

        internal IReadOnlyList<UnitSlot> GetEmptyBattleCells()
        {
            var cells = new List<UnitSlot>();
            for (int i = 0; i < _battleSlots.Count; i++)
            {
                if (_battleSlots[i].IsEmpty)
                {
                    cells.Add(_battleSlots[i]);
                }
            }

            return cells.AsReadOnly();
        }

        internal IReadOnlyList<UnitSlot> FindMergeTargets(BattleUnit source)
        {
            var targets = new List<UnitSlot>();
            for (int i = 0; i < _battleSlots.Count; i++)
            {
                UnitSlot slot = _battleSlots[i];
                if (!slot.Occupant.HasValue)
                {
                    continue;
                }

                BattleUnit target = slot.Occupant.Value;
                if (target.UnitId == source.UnitId
                    || source.Side != target.Side
                    || source.Kind != UnitKind.Soldier
                    || target.Kind != UnitKind.Soldier
                    || source.SoldierType != target.SoldierType
                    || source.Level != target.Level
                    || source.Level >= _maxLevel)
                {
                    continue;
                }

                targets.Add(slot);
            }

            return targets.AsReadOnly();
        }

        internal IReadOnlyList<UnitSlot> FindGeneralParts(bool includeReserve)
        {
            var parts = new List<UnitSlot>();
            AddGeneralParts(parts, _battleSlots);
            if (includeReserve)
            {
                AddGeneralParts(parts, _reserveSlots);
            }

            return parts.AsReadOnly();
        }

        internal bool HasGeneral(int generalIndex)
        {
            for (int i = 0; i < _battleSlots.Count; i++)
            {
                if (_battleSlots[i].Occupant.HasValue
                    && _battleSlots[i].Occupant.Value.Kind == UnitKind.General
                    && _battleSlots[i].Occupant.Value.GeneralIndex == generalIndex)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>返回该格距对手路线的 Manhattan 距离，越小越靠近路线。</summary>
        internal int GetRouteScore(GridPosition position)
        {
            IReadOnlyList<GridPosition> path = _map.GetOpponentPath();
            if (path == null || path.Count == 0)
            {
                return int.MaxValue;
            }

            int best = int.MaxValue;
            for (int i = 0; i < path.Count; i++)
            {
                int distance = Math.Abs(path[i].X - position.X) + Math.Abs(path[i].Y - position.Y);
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        internal bool IsCultivable(GridPosition position)
            => _map.IsInside(position) && _map.GetCell(position).IsCultivable;

        internal IReadOnlyList<GridPosition> GetAdjacentCells(GridPosition position)
        {
            var result = new List<GridPosition>(4);
            AddIfInside(result, new GridPosition(position.X - 1, position.Y));
            AddIfInside(result, new GridPosition(position.X + 1, position.Y));
            AddIfInside(result, new GridPosition(position.X, position.Y - 1));
            AddIfInside(result, new GridPosition(position.X, position.Y + 1));
            return result.AsReadOnly();
        }

        internal bool TryFindUnitSlot(int unitId, out UnitSlot slot)
        {
            for (int i = 0; i < _battleSlots.Count; i++)
            {
                if (_battleSlots[i].OccupantUnitId == unitId)
                {
                    slot = _battleSlots[i];
                    return true;
                }
            }

            for (int i = 0; i < _reserveSlots.Count; i++)
            {
                if (_reserveSlots[i].OccupantUnitId == unitId)
                {
                    slot = _reserveSlots[i];
                    return true;
                }
            }

            slot = default;
            return false;
        }

        private static List<UnitSlot> CopySlots(IReadOnlyList<UnitSlot> slots)
        {
            return slots == null
                ? new List<UnitSlot>()
                : new List<UnitSlot>(slots);
        }

        private static void AddGeneralParts(List<UnitSlot> target, IReadOnlyList<UnitSlot> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i].Occupant.HasValue
                    && source[i].Occupant.Value.Kind == UnitKind.GeneralPart)
                {
                    target.Add(source[i]);
                }
            }
        }

        private void AddIfInside(List<GridPosition> target, GridPosition position)
        {
            if (_map.IsInside(position))
            {
                target.Add(position);
            }
        }
    }
}
