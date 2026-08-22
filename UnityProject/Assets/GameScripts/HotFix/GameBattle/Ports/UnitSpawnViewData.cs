namespace GameBattle
{
    /// <summary>单位首次上场的不可变表现事实。</summary>
    public readonly struct UnitSpawnViewData
    {
        public int RuntimeId { get; }
        public bool IsPlayerSide { get; }
        public int IdentityKind { get; }
        public int GeneralIndex { get; }
        public string DisplayName { get; }
        public int SoldierType { get; }
        public string PrefabAddress { get; }
        public string AnimationKey { get; }
        public int GridX { get; }
        public int GridY { get; }
        public int Level { get; }

        public UnitSpawnViewData(
            int runtimeId,
            bool isPlayerSide,
            int identityKind,
            int generalIndex,
            string displayName,
            int soldierType,
            string prefabAddress,
            string animationKey,
            int gridX,
            int gridY,
            int level)
        {
            RuntimeId = runtimeId;
            IsPlayerSide = isPlayerSide;
            IdentityKind = identityKind;
            GeneralIndex = generalIndex;
            DisplayName = displayName ?? string.Empty;
            SoldierType = soldierType;
            PrefabAddress = prefabAddress ?? string.Empty;
            AnimationKey = animationKey ?? string.Empty;
            GridX = gridX;
            GridY = gridY;
            Level = level;
        }
    }
}
