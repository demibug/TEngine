namespace Game.Combat.Input
{
    public enum BattleInputCommandType
    {
        PurchaseAndPlace,
        BeginDrag,
        MoveDrag,
        CommitPlacement,
        CancelDrag,
        MoveUnit,
        MergeUnits,
        Refresh
    }

    public readonly record struct GridPosition(int X, int Y);
    public sealed record BattleInputCommand(BattleInputCommandType Type, object Payload);
    public sealed record PurchaseAndPlacePayload(bool PlayerSide, int Slot, GridPosition Position);
    public sealed record MergeUnitsPayload(int SourceId, int TargetId);
    public sealed record MoveUnitPayload(int UnitId, GridPosition Position);
}
