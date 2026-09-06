using System.Collections.Generic;
using GameCommon.Battle;
using GameBattle.Weapon;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Weapon
{
    [TestFixture]
    internal sealed class WeaponCollectionStateTests
    {
        private static WeaponCatalog CreateCatalog()
        {
            return WeaponCatalog.CreateForTests(new List<WeaponDefinition>
            {
                new WeaponDefinition(1, WeaponEquipSlot.Bow, 1),
                new WeaponDefinition(11, WeaponEquipSlot.Spear, 1),
                new WeaponDefinition(21, WeaponEquipSlot.Knife, 1),
                new WeaponDefinition(32, WeaponEquipSlot.Sword, 1),
                new WeaponDefinition(2, WeaponEquipSlot.Bow, 3),
            });
        }

        private static WeaponCollectionState CreateState()
        {
            return WeaponCollectionState.CreateDefault(CreateCatalog());
        }

        private static WeaponProgressEntry GetEntry(
            WeaponCollectionState state,
            int weaponId)
        {
            foreach (WeaponProgressEntry entry in state.CreateSnapshot().Entries)
            {
                if (entry.WeaponId == weaponId)
                {
                    return entry;
                }
            }

            Assert.Fail($"未找到 weaponId={weaponId} 的进度条目");
            return default;
        }

        [Test]
        public void FirstState_AutoOwnsAndEquipsFourBasicWeapons()
        {
            WeaponCollectionState state = CreateState();
            BattleWeaponLoadoutDto loadout = state.CreateBattleLoadout();

            Assert.That(loadout.BowWeaponId, Is.EqualTo(1));
            Assert.That(loadout.SpearWeaponId, Is.EqualTo(11));
            Assert.That(loadout.KnifeWeaponId, Is.EqualTo(21));
            Assert.That(loadout.SwordWeaponId, Is.EqualTo(32));
            Assert.That(GetEntry(state, 1).CompletedCount, Is.EqualTo(1));
            Assert.That(GetEntry(state, 11).CompletedCount, Is.EqualTo(1));
            Assert.That(GetEntry(state, 21).CompletedCount, Is.EqualTo(1));
            Assert.That(GetEntry(state, 32).CompletedCount, Is.EqualTo(1));
        }

        [Test]
        public void FragmentsBelowThreshold_DoNotCreateCompleteWeapon()
        {
            WeaponCollectionState state = CreateState();

            WeaponMutationResult result = state.ChangeFragments(2, 2);
            WeaponProgressEntry entry = GetEntry(state, 2);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.NewCompletedCount, Is.EqualTo(0));
            Assert.That(entry.CompletedCount, Is.EqualTo(0));
            Assert.That(entry.Remainder, Is.EqualTo(2));
        }

        [Test]
        public void AddFragments_CrossesMultipleThresholds_ProducesPendingCopies()
        {
            WeaponCollectionState state = CreateState();

            WeaponMutationResult result = state.ChangeFragments(2, 7);
            WeaponProgressEntry entry = GetEntry(state, 2);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.NewCompletedCount, Is.EqualTo(2));
            Assert.That(entry.CompletedCount, Is.EqualTo(2));
            Assert.That(entry.Remainder, Is.EqualTo(1));
            Assert.That(entry.PendingNewCount, Is.EqualTo(2));
        }

        [Test]
        public void NegativeChange_CannotMakeFragmentsNegative()
        {
            WeaponCollectionState state = CreateState();

            WeaponMutationResult result = state.ChangeFragments(2, -1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(GetEntry(state, 2).TotalFragments, Is.EqualTo(0));
        }

        [Test]
        public void EquippedWeapon_CannotRecycleReservedCopy()
        {
            WeaponCollectionState state = CreateState();

            WeaponMutationResult result = state.ChangeFragments(1, -1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(state.CreateBattleLoadout().BowWeaponId, Is.EqualTo(1));
        }

        [Test]
        public void RecycleAllUnequipped_LeavesEquippedCopy()
        {
            WeaponCollectionState state = CreateState();
            Assert.That(state.ChangeFragments(1, 4).IsSuccess, Is.True);

            WeaponMutationResult result = state.RecycleAllUnequipped(1);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.TotalFragments, Is.EqualTo(1));
            Assert.That(GetEntry(state, 1).CompletedCount, Is.EqualTo(1));
        }

        [Test]
        public void IncompleteWeapon_CannotEquip()
        {
            WeaponCollectionState state = CreateState();

            Assert.That(state.TryEquip(WeaponEquipSlot.Bow, 2, out _), Is.False);
        }

        [Test]
        public void SlotMismatch_CannotEquip()
        {
            WeaponCollectionState state = CreateState();

            Assert.That(state.TryEquip(WeaponEquipSlot.Spear, 1, out _), Is.False);
        }

        [Test]
        public void SaveRoundTrip_PreservesFragmentsAndEquipment()
        {
            WeaponCatalog catalog = CreateCatalog();
            WeaponCollectionState state = WeaponCollectionState.CreateDefault(catalog);
            Assert.That(state.ChangeFragments(2, 3).IsSuccess, Is.True);
            Assert.That(state.TryEquip(WeaponEquipSlot.Bow, 2, out _), Is.True);

            WeaponCollectionState restored =
                WeaponCollectionState.Create(catalog, state.CreateSaveData());

            Assert.That(restored.CreateBattleLoadout().BowWeaponId, Is.EqualTo(2));
            Assert.That(GetEntry(restored, 2).CompletedCount, Is.EqualTo(1));
        }

        [Test]
        public void BattleLoadout_IsSnapshotAfterStateChanges()
        {
            WeaponCollectionState state = CreateState();
            BattleWeaponLoadoutDto loadout = state.CreateBattleLoadout();

            Assert.That(state.ChangeFragments(2, 3).IsSuccess, Is.True);
            Assert.That(state.TryEquip(WeaponEquipSlot.Bow, 2, out _), Is.True);

            Assert.That(loadout.BowWeaponId, Is.EqualTo(1));
            Assert.That(loadout.SpearWeaponId, Is.EqualTo(11));
            Assert.That(loadout.KnifeWeaponId, Is.EqualTo(21));
            Assert.That(loadout.SwordWeaponId, Is.EqualTo(32));
            Assert.That(state.CreateBattleLoadout().BowWeaponId, Is.EqualTo(2));
        }

        [Test]
        public void AcknowledgeNewWeapons_ClearsPendingWithoutChangingFragments()
        {
            WeaponCollectionState state = CreateState();
            Assert.That(state.ChangeFragments(2, 3).IsSuccess, Is.True);
            int total = GetEntry(state, 2).TotalFragments;

            state.AcknowledgeNewWeapons();

            Assert.That(GetEntry(state, 2).PendingNewCount, Is.EqualTo(0));
            Assert.That(GetEntry(state, 2).TotalFragments, Is.EqualTo(total));
        }
    }
}
