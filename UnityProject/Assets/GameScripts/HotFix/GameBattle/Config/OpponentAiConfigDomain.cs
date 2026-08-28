using System;
using System.Collections.Generic;

namespace GameBattle
{
    internal enum OpponentAiPlacementPolicy
    {
        Random = 0,
        RouteAware = 1,
    }

    /// <summary>单档对手 AI 的不可变本局配置。</summary>
    internal sealed class OpponentAiProfileSnapshot
    {
        private readonly Dictionary<int, int> _incomeByWave;

        internal int Id { get; }
        internal int DecisionIntervalMs { get; }
        internal int InitialBonusGold { get; }
        internal OpponentAiPlacementPolicy PlacementPolicy { get; }
        internal int CandidateTopN { get; }

        internal OpponentAiProfileSnapshot(
            int id,
            int decisionIntervalMs,
            int initialBonusGold,
            IReadOnlyList<int> incomeWaveOrders,
            IReadOnlyList<int> incomeGoldValues,
            OpponentAiPlacementPolicy placementPolicy,
            int candidateTopN)
        {
            Id = id;
            DecisionIntervalMs = decisionIntervalMs;
            InitialBonusGold = initialBonusGold;
            PlacementPolicy = placementPolicy;
            CandidateTopN = candidateTopN;
            _incomeByWave = new Dictionary<int, int>();

            int count = incomeWaveOrders?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                _incomeByWave.Add(incomeWaveOrders[i], incomeGoldValues[i]);
            }
        }

        internal int GetIncomeForWave(int waveOrder)
        {
            return _incomeByWave.TryGetValue(waveOrder, out int amount) ? amount : 0;
        }
    }

    /// <summary>四档难度的不可变目录。</summary>
    public sealed class OpponentAiProfileCatalogSnapshot
    {
        private readonly Dictionary<int, OpponentAiProfileSnapshot> _profiles;

        internal OpponentAiProfileCatalogSnapshot(
            IReadOnlyList<OpponentAiProfileSnapshot> profiles)
        {
            _profiles = new Dictionary<int, OpponentAiProfileSnapshot>();
            if (profiles == null)
            {
                return;
            }

            for (int i = 0; i < profiles.Count; i++)
            {
                OpponentAiProfileSnapshot profile = profiles[i];
                if (profile != null)
                {
                    _profiles.Add(profile.Id, profile);
                }
            }
        }

        internal bool TryGet(int difficulty, out OpponentAiProfileSnapshot profile)
        {
            return _profiles.TryGetValue(difficulty, out profile);
        }
    }
}
