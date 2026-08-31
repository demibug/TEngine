using System;

namespace GameBattle
{
    internal enum OpponentAiActionType
    {
        Deploy = 0,
        Merge = 1,
        SynthesizeGeneral = 2,
        Recruit = 3,
        UseShovel = 4,
        UseFarmer = 8,
        FastDeploy = 5,
        Replace = 6,
        Wait = 7,
    }

    /// <summary>纯决策层输出的动作；执行前必须重新以当前棋盘校验槽位和单位 ID。</summary>
    internal sealed class OpponentAiAction
    {
        internal OpponentAiActionType Type { get; }
        internal int SourceSlotId { get; }
        internal int TargetSlotId { get; }
        internal int SecondarySourceSlotId { get; }
        internal GridPosition TargetPosition { get; }
        internal string Reason { get; }
        internal int ExpectedUnitId { get; }

        internal OpponentAiAction(
            OpponentAiActionType type,
            int sourceSlotId = UnitSlot.InvalidUnitId,
            int targetSlotId = UnitSlot.InvalidUnitId,
            int secondarySourceSlotId = UnitSlot.InvalidUnitId,
            GridPosition targetPosition = default,
            string reason = "",
            int expectedUnitId = UnitSlot.InvalidUnitId)
        {
            Type = type;
            SourceSlotId = sourceSlotId;
            TargetSlotId = targetSlotId;
            SecondarySourceSlotId = secondarySourceSlotId;
            TargetPosition = targetPosition;
            Reason = reason ?? string.Empty;
            ExpectedUnitId = expectedUnitId;
        }

        internal static OpponentAiAction Wait(string reason = "no_legal_action")
            => new OpponentAiAction(OpponentAiActionType.Wait, reason: reason);

        public override string ToString()
            => $"OpponentAiAction(Type={Type}, Source={SourceSlotId}, Target={TargetSlotId}, " +
               $"TargetPosition={TargetPosition}, Reason={Reason})";
    }
}
