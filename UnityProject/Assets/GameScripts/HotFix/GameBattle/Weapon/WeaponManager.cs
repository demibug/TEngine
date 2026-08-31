using System;
using GameCommon.Battle;
using GameConfig;
using TEngine;
using UnityEngine;

namespace GameBattle.Weapon
{
    /// <summary>
    /// BattleModule 持有的局外武器业务对象。
    /// </summary>
    /// <remarks>
    /// 该类型不是 TEngine Module，也不注册到 ModuleSystem。它与 BattleModule
    /// 同生命周期，保存局外武器进度，并在开战前生成不可变的四槽装载。
    /// </remarks>
    public sealed class WeaponManager
    {
        internal const string SaveKey = "PLAYER_WEAPON_PROGRESS_V1";

        private WeaponCatalog _catalog;
        private WeaponCollectionState _state;
        private WeaponBattleRewardPolicy _rewardPolicy;
        private bool _isDirty;
        private bool _isShutdown;

        internal WeaponManager(Tables tables)
        {
            _catalog = WeaponCatalog.FromTables(tables);
            _rewardPolicy = new WeaponBattleRewardPolicy(_catalog);
            Load();
        }

        /// <summary>当前局外武器进度快照。</summary>
        public WeaponProgressSnapshot Snapshot
        {
            get
            {
                EnsureReady();
                return _state.CreateSnapshot();
            }
        }

        /// <summary>冻结当前四个装备槽，作为下一局战斗输入。</summary>
        public BattleWeaponLoadoutDto CreateBattleLoadout()
        {
            EnsureReady();
            return _state.CreateBattleLoadout();
        }

        /// <summary>
        /// 增加或扣除指定武器的累计碎片。所有奖励、调试和后续商店变更均从此入口进入。
        /// </summary>
        public WeaponMutationResult AddFragments(int weaponId, int count, string reason)
        {
            EnsureReady();

            WeaponCollectionState candidate = _state.Clone();
            WeaponMutationResult result = candidate.ChangeFragments(weaponId, count);
            if (!result.IsSuccess)
            {
                return result;
            }

            if (!TryCommit(candidate, out string error))
            {
                return WeaponMutationResult.Fail(error);
            }

            Log.Info(
                $"[WeaponManager] AddFragments weaponId={weaponId} count={count} " +
                $"reason='{reason ?? string.Empty}'");
            return result;
        }

        /// <summary>回收指定武器的全部未装备碎片。</summary>
        public WeaponMutationResult RecycleAllUnequipped(int weaponId)
        {
            EnsureReady();

            WeaponCollectionState candidate = _state.Clone();
            WeaponMutationResult result = candidate.RecycleAllUnequipped(weaponId);
            if (!result.IsSuccess)
            {
                return result;
            }

            if (!TryCommit(candidate, out string error))
            {
                return WeaponMutationResult.Fail(error);
            }

            return result;
        }

        /// <summary>装备一个已完整且类型匹配的武器。</summary>
        public bool TryEquip(WeaponEquipSlot slot, int weaponId, out string error)
        {
            EnsureReady();

            WeaponCollectionState candidate = _state.Clone();
            if (!candidate.TryEquip(slot, weaponId, out error))
            {
                return false;
            }

            if (!TryCommit(candidate, out error))
            {
                return false;
            }

            return true;
        }

        /// <summary>清除已读的新武器提示。</summary>
        public void AcknowledgeNewWeapons()
        {
            EnsureReady();

            WeaponCollectionState candidate = _state.Clone();
            candidate.AcknowledgeNewWeapons();
            if (!TryCommit(candidate, out string error))
            {
                Log.Error($"[WeaponManager] 清除新武器提示失败：{error}");
            }
        }

        /// <summary>
        /// 在唯一战斗结算点按本局装载和结果提交一次武器奖励。
        /// </summary>
        internal WeaponRewardGrant ApplyBattleResult(
            BattleLoadoutDto loadout,
            BattleResultDto result)
        {
            EnsureReady();

            int seed = unchecked(
                loadout.RandomSeed * 397
                ^ loadout.MapId * 7919
                ^ (result.IsWin ? 1 : 0) * 104729);
            WeaponRewardGrant grant = _rewardPolicy.Select(loadout.MapId, result.IsWin, seed);
            if (!grant.HasReward)
            {
                return default;
            }

            WeaponCollectionState candidate = _state.Clone();
            WeaponMutationResult mutation = candidate.ChangeFragments(
                grant.WeaponId,
                grant.FragmentCount);
            if (!mutation.IsSuccess)
            {
                Log.Error($"[WeaponManager] 战斗武器奖励无效：{mutation.Error}");
                return default;
            }

            if (!TryCommit(candidate, out string error))
            {
                // 结算继续完成，但未保存的奖励不提交到内存，也不返回给结算 UI。
                Log.Error($"[WeaponManager] 战斗武器奖励保存失败：{error}");
                return default;
            }

            Log.Info(
                $"[WeaponManager] BattleFinished weaponId={grant.WeaponId} " +
                $"fragmentCount={grant.FragmentCount}");
            return grant;
        }

        /// <summary>关闭 BattleModule 前保存仍未落盘的状态。</summary>
        internal void SaveIfDirty()
        {
            if (_state == null || !_isDirty)
            {
                return;
            }

            if (!TrySave(_state, out string error))
            {
                Log.Error($"[WeaponManager] 关闭时保存武器进度失败：{error}");
                return;
            }

            _isDirty = false;
        }

        internal void Shutdown()
        {
            if (_isShutdown)
            {
                return;
            }

            SaveIfDirty();
            _catalog = null;
            _state = null;
            _rewardPolicy = null;
            _isShutdown = true;
        }

        private void Load()
        {
            string json = Utility.PlayerPrefs.GetString(SaveKey, string.Empty);
            WeaponProgressSaveData data = null;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    data = JsonUtility.FromJson<WeaponProgressSaveData>(json);
                    if (data == null || data.schemaVersion != 1)
                    {
                        Log.Warning("[WeaponManager] 武器存档版本无效，使用默认进度。");
                        data = null;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[WeaponManager] 读取武器存档失败：{ex.Message}");
                    data = null;
                }
            }

            _state = data == null
                ? WeaponCollectionState.CreateDefault(_catalog)
                : WeaponCollectionState.Create(_catalog, data);
            _isDirty = data == null;

            if (_isDirty && !TrySave(_state, out string error))
            {
                Log.Error($"[WeaponManager] 初始化默认武器存档失败：{error}");
            }
            else
            {
                _isDirty = false;
            }
        }

        private bool TryCommit(WeaponCollectionState candidate, out string error)
        {
            if (!TrySave(candidate, out error))
            {
                return false;
            }

            _state = candidate;
            _isDirty = false;
            return true;
        }

        private bool TrySave(WeaponCollectionState state, out string error)
        {
            try
            {
                string json = JsonUtility.ToJson(state.CreateSaveData());
                Utility.PlayerPrefs.SetString(SaveKey, json);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void EnsureReady()
        {
            if (_isShutdown || _state == null)
            {
                throw new InvalidOperationException(
                    "WeaponManager 尚未初始化或已经关闭");
            }
        }
    }
}
