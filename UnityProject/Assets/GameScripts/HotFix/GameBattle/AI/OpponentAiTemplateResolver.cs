using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>
    /// 难度 3 的模板落点解析器。它只读取棋盘快照并输出候选槽位，不修改棋盘。
    /// </summary>
    internal sealed class OpponentAiTemplateResolver
    {
        private readonly MapData _map;
        private readonly IRandomSource _randomSource;
        private readonly OpponentAiValueEvaluator _valueEvaluator;

        internal OpponentAiTemplateResolver(
            MapData map,
            IRandomSource randomSource,
            OpponentAiValueEvaluator valueEvaluator)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _randomSource = randomSource ?? throw new ArgumentNullException(nameof(randomSource));
            _valueEvaluator = valueEvaluator ?? throw new ArgumentNullException(nameof(valueEvaluator));
        }

        /// <summary>返回与原始 AG/FG 缓存语义等价的本局模板键。</summary>
        internal string GetTemplateKey(OpponentAiProfileSnapshot profile)
        {
            string suffix = profile != null && profile.AllowTemplatePlacement ? "_f" : "_s";
            return _map.MapIndex + suffix;
        }

        /// <summary>
        /// 对空战场槽按模板、路线、邻域和单位价值排序，并从前 N 个候选中取一个。
        /// </summary>
        internal bool TryChooseTarget(
            BattleUnit source,
            OpponentAiBoardSnapshot snapshot,
            IReadOnlyList<UnitSlot> candidates,
            OpponentAiProfileSnapshot profile,
            out UnitSlotId target,
            out string reason)
        {
            target = UnitSlotId.Invalid;
            reason = string.Empty;
            if (snapshot == null || candidates == null || candidates.Count == 0)
            {
                return false;
            }

            var scored = new List<ScoredSlot>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                UnitSlot candidate = candidates[i];
                if (!candidate.IsEmpty || candidate.SlotId.Zone != SlotZone.Battle)
                {
                    continue;
                }

                int routeScore = snapshot.GetRouteScore(candidate.SlotId.GridPosition);
                int score = _valueEvaluator.EvaluatePlacement(
                    source,
                    candidate,
                    snapshot,
                    templatePlacement: true);

                if (routeScore != int.MaxValue)
                {
                    score += Math.Max(0, 30 - routeScore * 5);
                    if (routeScore == 1)
                    {
                        score += 20;
                    }
                }

                if (source.Kind == UnitKind.GeneralPart
                    && HasMatchingPartNear(source, candidate, snapshot))
                {
                    score += 30;
                }

                // The original template has a dedicated general/farmer branch. Keep the
                // key distinction explicit even when the current map has no such entry.
                if (source.Kind == UnitKind.GeneralPart)
                {
                    score += 10;
                }

                scored.Add(new ScoredSlot(candidate, score));
            }

            if (scored.Count == 0)
            {
                return false;
            }

            scored.Sort((left, right) =>
            {
                int scoreComparison = right.Score.CompareTo(left.Score);
                return scoreComparison != 0
                    ? scoreComparison
                    : left.Slot.SlotId.Id.CompareTo(right.Slot.SlotId.Id);
            });

            int topCount = profile != null && profile.CandidateTopN > 0
                ? Math.Min(profile.CandidateTopN, scored.Count)
                : 1;
            int selected = SelectIndex(topCount);
            target = scored[selected].Slot.SlotId;
            reason = source.Kind == UnitKind.GeneralPart
                ? "template_general_pair"
                : "template_route_neighbour_value";
            return true;
        }

        private static bool HasMatchingPartNear(
            BattleUnit source,
            UnitSlot candidate,
            OpponentAiBoardSnapshot snapshot)
        {
            IReadOnlyList<GridPosition> adjacent =
                snapshot.GetAdjacentCells(candidate.SlotId.GridPosition);
            IReadOnlyList<UnitSlot> parts = snapshot.FindGeneralParts(includeReserve: false);
            for (int i = 0; i < adjacent.Count; i++)
            {
                for (int j = 0; j < parts.Count; j++)
                {
                    if (parts[j].SlotId.GridPosition == adjacent[i]
                        && parts[j].Occupant.HasValue
                        && parts[j].Occupant.Value.Side == source.Side)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private int SelectIndex(int count)
        {
            int selected = (int)Math.Floor(_randomSource.NextUnit() * count);
            return Math.Max(0, Math.Min(selected, count - 1));
        }

        private readonly struct ScoredSlot
        {
            internal readonly UnitSlot Slot;
            internal readonly int Score;

            internal ScoredSlot(UnitSlot slot, int score)
            {
                Slot = slot;
                Score = score;
            }
        }
    }
}
