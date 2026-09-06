using System;
using System.Collections.Generic;

namespace GameBattle.Weapon
{
    /// <summary>一次战斗结算产生的武器奖励。</summary>
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

    /// <summary>按武器奖励表选择单次战斗奖励。</summary>
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

                if (!_catalog.TryGet(rule.WeaponId, out WeaponDefinition weapon)
                    || !weapon.Enabled
                    || !weapon.Obtainable)
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
