using System.Collections.Generic;
using GameBattle.Weapon;
using GameCommon.Battle;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Weapon
{
    [TestFixture]
    internal sealed class WeaponBattleRewardPolicyTests
    {
        private static WeaponCatalog CreateCatalog(
            bool winOnly,
            bool rewardEnabled = true)
        {
            var definitions = new List<WeaponDefinition>
            {
                new WeaponDefinition(1, WeaponEquipSlot.Bow, 1),
                new WeaponDefinition(11, WeaponEquipSlot.Spear, 1),
                new WeaponDefinition(21, WeaponEquipSlot.Knife, 1),
                new WeaponDefinition(32, WeaponEquipSlot.Sword, 1),
                new WeaponDefinition(
                    2,
                    WeaponEquipSlot.Bow,
                    3,
                    enabled: rewardEnabled,
                    obtainable: true),
            };
            var rewards = new List<WeaponRewardDefinition>
            {
                new WeaponRewardDefinition(
                    id: 1,
                    mapId: 0,
                    weaponId: 2,
                    winOnly: winOnly,
                    weight: 1,
                    minFragments: 3,
                    maxFragments: 3),
            };
            return WeaponCatalog.CreateForTests(definitions, rewards);
        }

        [Test]
        public void Victory_UsesRewardTableAndAddsFragments()
        {
            WeaponCatalog catalog = CreateCatalog(winOnly: false);
            WeaponBattleRewardPolicy policy = new WeaponBattleRewardPolicy(catalog);
            WeaponRewardGrant grant = policy.Select(mapId: 0, playerWon: true, seed: 123);

            Assert.That(grant.HasReward, Is.True);
            Assert.That(grant.WeaponId, Is.EqualTo(2));
            Assert.That(grant.FragmentCount, Is.EqualTo(3));

            WeaponCollectionState state = WeaponCollectionState.CreateDefault(catalog);
            WeaponMutationResult mutation = state.ChangeFragments(
                grant.WeaponId,
                grant.FragmentCount);

            Assert.That(mutation.IsSuccess, Is.True);
            Assert.That(mutation.NewCompletedCount, Is.EqualTo(1));
            WeaponProgressEntry rewardEntry = default;
            foreach (WeaponProgressEntry entry in state.CreateSnapshot().Entries)
            {
                if (entry.WeaponId == grant.WeaponId)
                {
                    rewardEntry = entry;
                    break;
                }
            }

            Assert.That(rewardEntry.WeaponId, Is.EqualTo(2));
            Assert.That(rewardEntry.TotalFragments, Is.EqualTo(3));
        }

        [Test]
        public void Defeat_DoesNotUseWinOnlyReward()
        {
            WeaponBattleRewardPolicy policy =
                new WeaponBattleRewardPolicy(CreateCatalog(winOnly: true));

            WeaponRewardGrant grant = policy.Select(mapId: 0, playerWon: false, seed: 123);

            Assert.That(grant.HasReward, Is.False);
        }

        [Test]
        public void DisabledWeapon_IsExcludedFromRewardPool()
        {
            WeaponBattleRewardPolicy policy =
                new WeaponBattleRewardPolicy(CreateCatalog(
                    winOnly: false,
                    rewardEnabled: false));

            WeaponRewardGrant grant = policy.Select(
                mapId: 0,
                playerWon: true,
                seed: 123);

            Assert.That(grant.HasReward, Is.False);
        }
    }
}
