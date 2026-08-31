using System;
using System.Collections.Generic;

namespace GameBattle.Weapon
{
    /// <summary>供武器 UI 使用的不可变进度条目。</summary>
    public readonly struct WeaponProgressEntry
    {
        public readonly int WeaponId;
        public readonly int TotalFragments;
        public readonly int CompletedCount;
        public readonly int Remainder;
        public readonly int PendingNewCount;
        public readonly bool Equipped;

        internal WeaponProgressEntry(
            int weaponId,
            int totalFragments,
            int completedCount,
            int remainder,
            int pendingNewCount,
            bool equipped)
        {
            WeaponId = weaponId;
            TotalFragments = totalFragments;
            CompletedCount = completedCount;
            Remainder = remainder;
            PendingNewCount = pendingNewCount;
            Equipped = equipped;
        }
    }

    /// <summary>武器 UI 读取的不可变进度快照。</summary>
    public sealed class WeaponProgressSnapshot
    {
        public IReadOnlyList<WeaponProgressEntry> Entries { get; }
        public int BowWeaponId { get; }
        public int SpearWeaponId { get; }
        public int KnifeWeaponId { get; }
        public int SwordWeaponId { get; }

        internal WeaponProgressSnapshot(
            WeaponProgressEntry[] entries,
            int bowWeaponId,
            int spearWeaponId,
            int knifeWeaponId,
            int swordWeaponId)
        {
            Entries = Array.AsReadOnly(entries ?? Array.Empty<WeaponProgressEntry>());
            BowWeaponId = bowWeaponId;
            SpearWeaponId = spearWeaponId;
            KnifeWeaponId = knifeWeaponId;
            SwordWeaponId = swordWeaponId;
        }
    }

    /// <summary>一次武器状态变更的结果。</summary>
    public readonly struct WeaponMutationResult
    {
        public readonly bool IsSuccess;
        public readonly string Error;
        public readonly int WeaponId;
        public readonly int TotalFragments;
        public readonly int NewCompletedCount;
        public readonly int RecycleGold;

        private WeaponMutationResult(
            bool isSuccess,
            string error,
            int weaponId,
            int totalFragments,
            int newCompletedCount,
            int recycleGold)
        {
            IsSuccess = isSuccess;
            Error = error ?? string.Empty;
            WeaponId = weaponId;
            TotalFragments = totalFragments;
            NewCompletedCount = newCompletedCount;
            RecycleGold = recycleGold;
        }

        internal static WeaponMutationResult Ok(
            int weaponId,
            int totalFragments,
            int newCompletedCount,
            int recycleGold = 0)
        {
            return new WeaponMutationResult(
                true, string.Empty, weaponId, totalFragments, newCompletedCount, recycleGold);
        }

        internal static WeaponMutationResult Fail(string error)
        {
            return new WeaponMutationResult(false, error, 0, 0, 0, 0);
        }
    }
}
