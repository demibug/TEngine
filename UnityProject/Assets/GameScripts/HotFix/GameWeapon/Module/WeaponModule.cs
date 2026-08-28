using System;
using GameCommon.Battle;
using GameCommon.Weapon;
using TEngine;

namespace GameWeapon
{
    public sealed class WeaponModule : Module, IWeaponModule
    {
        private readonly IWeaponProgressStore _store;
        private readonly GameEventMgr _eventMgr = new GameEventMgr();
        private WeaponCatalog _catalog;
        private WeaponCollectionState _state;
        private WeaponBattleRewardPolicy _rewardPolicy;
        private BattleLoadoutDto _activeLoadout;
        private int _battleGeneration;
        private int _rewardedGeneration = -1;
        private bool _hasActiveBattle;
        private bool _isShutdown;

        public WeaponProgressSnapshot Snapshot
        {
            get
            {
                EnsureReady();
                return _state.CreateSnapshot();
            }
        }

        public WeaponModule()
            : this(new PlayerPrefsWeaponProgressStore())
        {
        }

        internal WeaponModule(IWeaponProgressStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public override void OnInit()
        {
            _catalog = WeaponCatalog.FromTables(ConfigSystem.Instance.Tables);
            _store.TryLoad(out WeaponProgressSaveData save);
            _state = WeaponCollectionState.Create(_catalog, save);
            _rewardPolicy = new WeaponBattleRewardPolicy(_catalog);

            if (save == null && !_store.TrySave(_state.CreateSaveData(), out string initialSaveError))
            {
                throw new InvalidOperationException(initialSaveError);
            }

            _eventMgr.AddEvent<BattleLoadoutDto>(
                IBattlePublicEvent_Event.OnBattleStarted,
                OnBattleStarted);
            _eventMgr.AddEvent<BattleResultDto>(
                IBattlePublicEvent_Event.OnBattleFinished,
                OnBattleFinished);
            _isShutdown = false;
            Log.Info("[WeaponModule] 初始化完成。");
        }

        public BattleWeaponLoadoutDto CreateBattleWeaponLoadout()
        {
            EnsureReady();
            return _state.CreateBattleLoadout();
        }

        public WeaponMutationResult ChangeFragments(int weaponId, int delta, string reason)
        {
            EnsureReady();
            WeaponCollectionState candidate = _state.Clone();
            WeaponMutationResult result = candidate.ChangeFragments(weaponId, delta);
            if (!result.IsSuccess)
            {
                return result;
            }

            if (!TryCommit(candidate, out string error))
            {
                return WeaponMutationResult.Fail(error);
            }

            PublishProgress(result);
            Log.Info(
                $"[WeaponModule] ChangeFragments weaponId={weaponId} delta={delta} reason='{reason ?? string.Empty}'");
            return result;
        }

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

            PublishProgress(result);
            return result;
        }

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

            GameEvent.Get<IWeaponPublicEvent>()?.OnWeaponProgressChanged();
            return true;
        }

        public bool AcknowledgeNewWeapons(out string error)
        {
            EnsureReady();
            WeaponCollectionState candidate = _state.Clone();
            candidate.AcknowledgeNewWeapons();
            if (!TryCommit(candidate, out error))
            {
                return false;
            }

            GameEvent.Get<IWeaponPublicEvent>()?.OnWeaponProgressChanged();
            return true;
        }

        public override void Shutdown()
        {
            if (_isShutdown)
            {
                return;
            }

            _eventMgr.Clear();
            _catalog = null;
            _state = null;
            _rewardPolicy = null;
            _isShutdown = true;
        }

        private void OnBattleStarted(BattleLoadoutDto loadout)
        {
            _activeLoadout = loadout;
            _battleGeneration++;
            _hasActiveBattle = true;
        }

        private void OnBattleFinished(BattleResultDto result)
        {
            if (!_hasActiveBattle || _rewardedGeneration == _battleGeneration)
            {
                return;
            }

            int seed = unchecked(
                _activeLoadout.RandomSeed * 397
                ^ _activeLoadout.MapId * 7919
                ^ _battleGeneration * 104729);
            WeaponRewardGrant grant = _rewardPolicy.Select(_activeLoadout.MapId, result.IsWin, seed);
            _rewardedGeneration = _battleGeneration;
            _hasActiveBattle = false;
            if (!grant.HasReward)
            {
                return;
            }

            WeaponMutationResult mutation = ChangeFragments(
                grant.WeaponId,
                grant.FragmentCount,
                "BattleFinished");
            if (!mutation.IsSuccess)
            {
                _rewardedGeneration = -1;
                Log.Error($"[WeaponModule] 战斗武器奖励提交失败：{mutation.Error}");
            }
        }

        private bool TryCommit(WeaponCollectionState candidate, out string error)
        {
            WeaponProgressSaveData save = candidate.CreateSaveData();
            if (!_store.TrySave(save, out error))
            {
                return false;
            }

            _state = candidate;
            return true;
        }

        private static void PublishProgress(WeaponMutationResult result)
        {
            IWeaponPublicEvent publisher = GameEvent.Get<IWeaponPublicEvent>();
            publisher?.OnWeaponProgressChanged();
            if (result.NewCompletedCount > 0)
            {
                publisher?.OnNewWeaponCompleted(result.WeaponId, result.NewCompletedCount);
            }
        }

        private void EnsureReady()
        {
            if (_isShutdown || _state == null)
            {
                throw new InvalidOperationException("WeaponModule 尚未初始化或已经关闭");
            }
        }
    }
}
