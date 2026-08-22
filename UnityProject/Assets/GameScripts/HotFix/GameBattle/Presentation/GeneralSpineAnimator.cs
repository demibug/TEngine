using Spine.Unity;
using UnityEngine;

namespace GameBattle
{
    /// <summary>
    /// 驱动武将 Spine 待机与攻击动画。
    /// 待机循环播放 idleKey；攻击播放 attackKey 一次后队列接续待机循环。
    /// 与 <see cref="SoldierSpriteAnimator"/> 并存：士兵走 SpriteRenderer 序列帧，
    /// 武将走 Spine；二者互不干扰（各自 Prefab 只挂其中一种）。
    /// </summary>
    internal sealed class GeneralSpineAnimator : MonoBehaviour
    {
        // "default" 是士兵占位动画键，武将 Spine 无此动画，回退到 zhan1。
        private const string FallbackIdleKey = "zhan1";
        private const string AttackKey = "attack1";
        private const string ZhangFeiSkeletonDataName = "zhangfei_SkeletonData";
        private const string ZhangFeiSecondaryIdleKey = "zhan2";
        private const string ZhangFeiSecondaryAttackKey = "attack2";

        private SkeletonAnimation _spine;
        private string _idleKey;
        private bool _useZhangFeiSecondaryTrack;
        private bool _configured;

        /// <summary>绑定 Spine 组件并以 idleKey 进入待机。spine 为空时成为空操作。</summary>
        internal void Configure(SkeletonAnimation spine, string idleKey)
        {
            _spine = spine;
            _idleKey = ResolveIdleKey(idleKey);
            _configured = spine != null && spine.AnimationState != null;
            _useZhangFeiSecondaryTrack = _configured && HasZhangFeiSecondaryAnimations(spine);
            ResetToIdle();
        }

        /// <summary>播放一次攻击动画，结束后自动回待机。</summary>
        internal void PlayAttack()
        {
            if (!_configured)
            {
                return;
            }

            _spine.AnimationState.ClearTracks();
            _spine.AnimationState.SetAnimation(0, AttackKey, false);
            _spine.AnimationState.AddAnimation(0, _idleKey, true, 0f);

            if (_useZhangFeiSecondaryTrack)
            {
                _spine.AnimationState.SetAnimation(1, ZhangFeiSecondaryAttackKey, false);
                _spine.AnimationState.AddAnimation(1, ZhangFeiSecondaryIdleKey, true, 0f);
            }
        }

        /// <summary>立即回到待机循环（池回收/复用前复位）。</summary>
        internal void ResetToIdle()
        {
            if (!_configured)
            {
                return;
            }

            _spine.AnimationState.ClearTracks();
            _spine.AnimationState.SetAnimation(0, _idleKey, true);

            if (_useZhangFeiSecondaryTrack)
            {
                _spine.AnimationState.SetAnimation(1, ZhangFeiSecondaryIdleKey, true);
            }
        }

        private static bool HasZhangFeiSecondaryAnimations(SkeletonAnimation spine)
        {
            if (spine.SkeletonDataAsset == null ||
                spine.SkeletonDataAsset.name != ZhangFeiSkeletonDataName ||
                spine.Skeleton == null)
            {
                return false;
            }

            return spine.Skeleton.Data.FindAnimation(ZhangFeiSecondaryIdleKey) != null &&
                   spine.Skeleton.Data.FindAnimation(ZhangFeiSecondaryAttackKey) != null;
        }

        private static string ResolveIdleKey(string idleKey)
        {
            if (string.IsNullOrEmpty(idleKey) || idleKey == "default")
            {
                return FallbackIdleKey;
            }

            return idleKey;
        }
    }
}
