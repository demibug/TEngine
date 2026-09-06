using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>
    /// 对手道具决策器。只选择铲子/农民扩地动作，真正写入仍由输入控制器完成。
    /// </summary>
    internal sealed class OpponentAiItemController
    {
        private readonly BattleMapState _mapState;
        private readonly MapData _map;
        private long _elapsedMs;

        internal OpponentAiItemController(BattleMapState mapState, MapData map)
        {
            _mapState = mapState ?? throw new ArgumentNullException(nameof(mapState));
            _map = map ?? throw new ArgumentNullException(nameof(map));
        }

        internal long CooldownElapsedMs => _elapsedMs;

        internal void StartGame()
        {
            _elapsedMs = 0;
        }

        internal void Stop()
        {
            _elapsedMs = 0;
        }

        internal void Update(long stepMs)
        {
            if (stepMs <= 0)
            {
                return;
            }

            _elapsedMs = Math.Min(long.MaxValue - stepMs, _elapsedMs + stepMs);
        }

        internal bool IsReady(OpponentAiProfileSnapshot profile)
        {
            int cooldown = profile == null ? 0 : Math.Max(0, profile.ItemCooldownMs);
            return _elapsedMs >= cooldown;
        }

        internal void MarkUsed()
        {
            _elapsedMs = 0;
        }

        internal bool TryBuildAction(
            OpponentAiBoardSnapshot snapshot,
            OpponentAiProfileSnapshot profile,
            out OpponentAiAction action)
        {
            action = null;
            if (snapshot == null || profile == null || !IsReady(profile))
            {
                return false;
            }

            if (profile.AllowFarmer
                && TryBuildExpansionAction(
                    snapshot,
                    profile,
                    UnitKind.Farmer,
                    OpponentAiActionType.UseFarmer,
                    out action))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 仅为显式 OnPlayerDanger 事件构造危险响应道具动作。
        /// </summary>
        internal bool TryBuildDangerAction(
            OpponentAiBoardSnapshot snapshot,
            OpponentAiProfileSnapshot profile,
            out OpponentAiAction action)
        {
            action = null;
            if (snapshot == null
                || profile == null
                || !profile.AllowDangerResponse
                || !IsReady(profile))
            {
                return false;
            }

            if (TryBuildExpansionAction(
                    snapshot,
                    profile,
                    UnitKind.Prop,
                    OpponentAiActionType.UseShovel,
                    out action))
            {
                return true;
            }

            // 原工程危险槽位承载的是“危险响应类道具”。在当前规则层农民也是
            // 一次性输入道具，铲子缺失时允许同一显式事件使用农民兜底；
            // 普通决策 tick 仍不会因此自动消耗铲子。
            return profile.AllowFarmer
                && TryBuildExpansionAction(
                    snapshot,
                    profile,
                    UnitKind.Farmer,
                    OpponentAiActionType.UseFarmer,
                    out action);
        }

        private bool TryBuildExpansionAction(
            OpponentAiBoardSnapshot snapshot,
            OpponentAiProfileSnapshot profile,
            UnitKind kind,
            OpponentAiActionType actionType,
            out OpponentAiAction action)
        {
            action = null;
            UnitSlot source = default;
            bool foundSource = false;
            IReadOnlyList<UnitSlot> reserves = snapshot.ReserveSlots;
            for (int i = 0; i < reserves.Count; i++)
            {
                UnitSlot candidate = reserves[i];
                if (!candidate.Occupant.HasValue
                    || candidate.Occupant.Value.Kind != kind
                    || (kind == UnitKind.Prop && !candidate.Occupant.Value.IsShovel))
                {
                    continue;
                }

                source = candidate;
                foundSource = true;
                break;
            }

            if (!foundSource || !TryChooseTarget(snapshot, profile, kind, out GridPosition target, out string reason))
            {
                return false;
            }

            action = new OpponentAiAction(
                actionType,
                sourceSlotId: source.SlotId.Id,
                targetPosition: target,
                reason: reason,
                expectedUnitId: source.Occupant.Value.UnitId);
            return true;
        }

        private bool TryChooseTarget(
            OpponentAiBoardSnapshot snapshot,
            OpponentAiProfileSnapshot profile,
            UnitKind kind,
            out GridPosition target,
            out string reason)
        {
            target = default;
            reason = string.Empty;
            var candidates = new List<ExpansionCandidate>();
            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    GridPosition position = new GridPosition(x, y);
                    if (_mapState.CanOpenTile(playerSide: false, position)
                        != OpenTileRejectReason.None)
                    {
                        continue;
                    }

                    int routeScore = snapshot.GetRouteScore(position);
                    int score = routeScore == int.MaxValue
                        ? 0
                        : Math.Max(0, 80 - routeScore * 8);
                    if (routeScore == 1)
                    {
                        score += profile.AllowDangerResponse ? 80 : 20;
                    }

                    if (kind == UnitKind.Farmer && position == new GridPosition(2, 1))
                    {
                        score += 1000;
                    }

                    if (profile.AllowTemplatePlacement && HasAdjacentRoute(position))
                    {
                        score += 35;
                    }

                    candidates.Add(new ExpansionCandidate(position, score));
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            candidates.Sort((left, right) =>
            {
                int scoreComparison = right.Score.CompareTo(left.Score);
                return scoreComparison != 0
                    ? scoreComparison
                    : ComparePosition(left.Position, right.Position);
            });

            target = candidates[0].Position;
            reason = kind == UnitKind.Farmer
                ? target == new GridPosition(2, 1)
                    ? "farmer_priority_2_1"
                    : "farmer_route_expansion"
                : profile.AllowDangerResponse
                    ? "shovel_danger_response"
                    : "shovel_expansion";
            return true;
        }

        private bool HasAdjacentRoute(GridPosition position)
        {
            IReadOnlyList<GridPosition> path = _map.GetPathForSide(playerSide: false);
            if (path == null)
            {
                return false;
            }

            for (int i = 0; i < path.Count; i++)
            {
                int distance = Math.Abs(path[i].X - position.X) + Math.Abs(path[i].Y - position.Y);
                if (distance == 1)
                {
                    return true;
                }
            }

            return false;
        }

        private static int ComparePosition(GridPosition left, GridPosition right)
        {
            int xComparison = left.X.CompareTo(right.X);
            return xComparison != 0 ? xComparison : left.Y.CompareTo(right.Y);
        }

        private readonly struct ExpansionCandidate
        {
            internal readonly GridPosition Position;
            internal readonly int Score;

            internal ExpansionCandidate(GridPosition position, int score)
            {
                Position = position;
                Score = score;
            }
        }
    }
}
