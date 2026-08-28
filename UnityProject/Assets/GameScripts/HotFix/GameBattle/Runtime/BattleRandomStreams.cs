namespace GameBattle
{
    /// <summary>从单局根种子派生互不污染的确定性随机流。</summary>
    internal sealed class BattleRandomStreams
    {
        internal IRandomSource PlayerRecruit { get; }
        internal IRandomSource OpponentRecruit { get; }
        internal IRandomSource CombatAndSkills { get; }
        internal IRandomSource OpponentStrategy { get; }

        internal BattleRandomStreams(int rootSeed)
        {
            PlayerRecruit = new SeededRandomSource(Derive(rootSeed, 0x13579BDFu));
            OpponentRecruit = new SeededRandomSource(Derive(rootSeed, 0x2468ACE1u));
            CombatAndSkills = new SeededRandomSource(Derive(rootSeed, 0x9E3779B9u));
            OpponentStrategy = new SeededRandomSource(Derive(rootSeed, 0x7F4A7C15u));
        }

        private static int Derive(int rootSeed, uint streamTag)
        {
            unchecked
            {
                uint value = (uint)rootSeed ^ streamTag;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (int)value;
            }
        }
    }
}
