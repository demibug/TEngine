using GameCommon.Battle;

namespace GameWeapon
{
    public interface IWeaponModule
    {
        WeaponProgressSnapshot Snapshot { get; }
        BattleWeaponLoadoutDto CreateBattleWeaponLoadout();
        WeaponMutationResult ChangeFragments(int weaponId, int delta, string reason);
        WeaponMutationResult RecycleAllUnequipped(int weaponId);
        bool TryEquip(WeaponEquipSlot slot, int weaponId, out string error);
        bool AcknowledgeNewWeapons(out string error);
    }
}
