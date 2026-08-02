using System.Collections.Generic;

namespace Game.Combat.Config
{
    public sealed class UnitConfigDto
    {
        public int Index;
        public string Text;
        public string AnimationKey;
        public float RangeCells;
        public float AttackDamage;
        public float AttackIntervalSeconds;
        public string DamageMode;
        public string TargetPolicy;
    }

    public sealed class WaveConfigDto
    {
        public List<int> WaveUnitCounts = new();
        public List<int> BossWaveNumbers = new();
        public List<float> BossSpawnChances = new();
        public List<float> SpawnStrategyWeights = new();
        public List<List<float>> SpawnStrategies = new();
    }

    public sealed class BattleEconomyConfigDto
    {
        public int InitialGold;
        public int RefreshCostStart;
        public int RefreshCostIncrement;
        public int UnitBaseCost;
        public int HandSize;
    }
}
