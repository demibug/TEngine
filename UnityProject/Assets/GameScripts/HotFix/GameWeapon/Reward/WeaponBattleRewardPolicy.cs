using System;
using System.Collections.Generic;

namespace GameWeapon
{
    internal readonly struct WeaponRewardGrant
    {
        internal readonly bool HasReward;
        internal readonly int WeaponId;
        internal readonly int FragmentCount;

        internal WeaponRewardGrant(bool hasReward, int weaponId, int fragmentCount)
        {
            HasReward = hasReward;
            WeaponId = weaponId;
            FragmentCount = fragmentCount;
        }
    }

    internal sealed class WeaponBattleRewardPolicy
    {
        private readonly WeaponCatalog _catalog;

        internal WeaponBattleRewardPolicy(WeaponCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        internal WeaponRewardGrant Select(int mapId, bool playerWon, int seed)
        {
            var candidates = new List<WeaponRewardDefinition>();
            int totalWeight = 0;
            foreach (WeaponRewardDefinition rule in _catalog.Rewards)
            {
                if (rule.MapId != mapId || (rule.WinOnly && !playerWon))
                {
                    continue;
                }

                candidates.Add(rule);
                totalWeight = checked(totalWeight + rule.Weight);
            }

            if (candidates.Count == 0)
            {
                return default;
            }

            var random = new Random(seed);
            int selectedWeight = random.Next(totalWeight);
            WeaponRewardDefinition selected = candidates[0];
            for (int i = 0; i < candidates.Count; i++)
            {
                selectedWeight -= candidates[i].Weight;
                if (selectedWeight < 0)
                {
                    selected = candidates[i];
                    break;
                }
            }

            int count = selected.MinFragments == selected.MaxFragments
                ? selected.MinFragments
                : random.Next(selected.MinFragments, selected.MaxFragments + 1);
            return new WeaponRewardGrant(true, selected.WeaponId, count);
        }
    }
}
