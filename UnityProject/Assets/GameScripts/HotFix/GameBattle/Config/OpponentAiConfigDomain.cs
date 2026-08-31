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
        internal int HandSize { get; }
        internal int RefreshBaseCost { get; }
        internal int RefreshCostIncrement { get; }
        internal int ItemCooldownMs { get; }
        internal bool AllowGeneralParts { get; }
        internal bool AllowFarmer { get; }
        internal bool AllowActiveMerge { get; }
        internal bool AllowTemplatePlacement { get; }
        internal bool AllowDangerResponse { get; }
        internal bool AllowFastDeploy { get; }
        internal bool EnableValueEvaluation { get; }
        internal bool EnableReclaim { get; }

        internal OpponentAiProfileSnapshot(
            int id,
            int decisionIntervalMs,
            int initialBonusGold,
            IReadOnlyList<int> incomeWaveOrders,
            IReadOnlyList<int> incomeGoldValues,
            OpponentAiPlacementPolicy placementPolicy,
            int candidateTopN)
            : this(
                id,
                decisionIntervalMs,
                initialBonusGold,
                incomeWaveOrders,
                incomeGoldValues,
                placementPolicy,
                candidateTopN,
                handSize: 5,
                refreshBaseCost: 10,
                refreshCostIncrement: 2,
                itemCooldownMs: 5000,
                allowGeneralParts: false,
                allowFarmer: false,
                allowActiveMerge: false,
                allowTemplatePlacement: false,
                allowDangerResponse: false,
                allowFastDeploy: false,
                enableValueEvaluation: false,
                enableReclaim: false)
        {
        }

        internal OpponentAiProfileSnapshot(
            int id,
            int decisionIntervalMs,
            int initialBonusGold,
            IReadOnlyList<int> incomeWaveOrders,
            IReadOnlyList<int> incomeGoldValues,
            OpponentAiPlacementPolicy placementPolicy,
            int candidateTopN,
            int handSize,
            int refreshBaseCost,
            int refreshCostIncrement,
            int itemCooldownMs,
            bool allowGeneralParts,
            bool allowFarmer,
            bool allowActiveMerge,
            bool allowTemplatePlacement,
            bool allowDangerResponse,
            bool allowFastDeploy,
            bool enableValueEvaluation,
            bool enableReclaim)
        {
            Id = id;
            DecisionIntervalMs = decisionIntervalMs;
            InitialBonusGold = initialBonusGold;
            PlacementPolicy = placementPolicy;
            CandidateTopN = candidateTopN;
            HandSize = handSize;
            RefreshBaseCost = refreshBaseCost;
            RefreshCostIncrement = refreshCostIncrement;
            ItemCooldownMs = itemCooldownMs;
            AllowGeneralParts = allowGeneralParts;
            AllowFarmer = allowFarmer;
            AllowActiveMerge = allowActiveMerge;
            AllowTemplatePlacement = allowTemplatePlacement;
            AllowDangerResponse = allowDangerResponse;
            AllowFastDeploy = allowFastDeploy;
            EnableValueEvaluation = enableValueEvaluation;
            EnableReclaim = enableReclaim;
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
