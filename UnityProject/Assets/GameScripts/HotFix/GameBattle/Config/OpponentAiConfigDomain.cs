using System;
using System.Collections.Generic;

namespace GameBattle
{
    internal enum OpponentAiPlacementPolicy
    {
        Random = 0,
        RouteAware = 1,
    }

    /// <summary>Raw bundle constants not represented by the generated difficulty row.</summary>
    internal static class OpponentAiPolicyDefaults
    {
        internal const float GeneralPartCopyProbability = 0.5f;
        internal const int FastDeployMaxUnits = 2;
        internal const int FastDeployMaxUses = 1;
        internal const int DangerResponseMaxUses = 1;

        internal static float GetFastDeployProbability(int difficulty)
        {
            return 0.001f;
        }

        internal static float GetDangerResponseProbability(int difficulty)
        {
            switch (difficulty)
            {
                case 0:
                    return 0.1f;
                case 1:
                    return 0.2f;
                case 2:
                    return 0.5f;
                case 3:
                    return 0.8f;
                default:
                    return 0.1f;
            }
        }
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
        internal float FastDeployProbability { get; }
        internal float DangerResponseProbability { get; }
        internal float GeneralPartCopyProbability { get; }
        internal int FastDeployMaxUnits { get; }
        internal int FastDeployMaxUses { get; }
        internal int DangerResponseMaxUses { get; }

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
                enableReclaim: false,
                fastDeployProbability: OpponentAiPolicyDefaults.GetFastDeployProbability(id),
                dangerResponseProbability: OpponentAiPolicyDefaults.GetDangerResponseProbability(id),
                generalPartCopyProbability: OpponentAiPolicyDefaults.GeneralPartCopyProbability,
                fastDeployMaxUnits: OpponentAiPolicyDefaults.FastDeployMaxUnits,
                fastDeployMaxUses: OpponentAiPolicyDefaults.FastDeployMaxUses,
                dangerResponseMaxUses: OpponentAiPolicyDefaults.DangerResponseMaxUses)
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
            bool enableReclaim,
            float fastDeployProbability = 0.001f,
            float dangerResponseProbability = 0.1f,
            float generalPartCopyProbability = OpponentAiPolicyDefaults.GeneralPartCopyProbability,
            int fastDeployMaxUnits = OpponentAiPolicyDefaults.FastDeployMaxUnits,
            int fastDeployMaxUses = OpponentAiPolicyDefaults.FastDeployMaxUses,
            int dangerResponseMaxUses = OpponentAiPolicyDefaults.DangerResponseMaxUses)
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
            FastDeployProbability = ClampProbability(fastDeployProbability);
            DangerResponseProbability = ClampProbability(dangerResponseProbability);
            GeneralPartCopyProbability = ClampProbability(generalPartCopyProbability);
            FastDeployMaxUnits = Math.Max(0, fastDeployMaxUnits);
            FastDeployMaxUses = Math.Max(0, fastDeployMaxUses);
            DangerResponseMaxUses = Math.Max(0, dangerResponseMaxUses);
            _incomeByWave = new Dictionary<int, int>();

            int count = incomeWaveOrders?.Count ?? 0;
            int valueCount = incomeGoldValues?.Count ?? 0;
            if (valueCount != count)
            {
                throw new ArgumentException(
                    "incomeWaveOrders and incomeGoldValues must have the same length.");
            }

            for (int i = 0; i < count; i++)
            {
                _incomeByWave.Add(incomeWaveOrders[i], incomeGoldValues[i]);
            }
        }

        private static float ClampProbability(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
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
