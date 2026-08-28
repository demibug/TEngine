using System.Collections.Generic;
using NUnit.Framework;

namespace GameWeapon.Tests
{
    public sealed class WeaponCollectionStateTests
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

        [Test]
        public void ChangeFragments_CrossesMultipleThresholds_ProducesPendingCopies()
        {
            WeaponCollectionState state = WeaponCollectionState.Create(CreateCatalog(), null);

            WeaponMutationResult result = state.ChangeFragments(2, 7);
            WeaponProgressEntry entry = state.CreateSnapshot().Entries[1];

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.NewCompletedCount, Is.EqualTo(2));
            Assert.That(entry.CompletedCount, Is.EqualTo(2));
            Assert.That(entry.Remainder, Is.EqualTo(1));
            Assert.That(entry.PendingNewCount, Is.EqualTo(2));
        }

        [Test]
        public void EquippedWeapon_CannotRecycleReservedCopy()
        {
            WeaponCollectionState state = WeaponCollectionState.Create(CreateCatalog(), null);

            WeaponMutationResult result = state.ChangeFragments(1, -1);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(state.CreateBattleLoadout().BowWeaponId, Is.EqualTo(1));
        }

        [Test]
        public void SaveRoundTrip_PreservesFragmentsAndEquipment()
        {
            WeaponCatalog catalog = CreateCatalog();
            WeaponCollectionState state = WeaponCollectionState.Create(catalog, null);
            state.ChangeFragments(2, 3);
            Assert.That(state.TryEquip(WeaponEquipSlot.Bow, 2, out _), Is.True);

            WeaponCollectionState restored = WeaponCollectionState.Create(catalog, state.CreateSaveData());

            Assert.That(restored.CreateBattleLoadout().BowWeaponId, Is.EqualTo(2));
            Assert.That(restored.CreateSnapshot().Entries[1].CompletedCount, Is.EqualTo(1));
        }
    }
}
