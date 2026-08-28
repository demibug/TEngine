using TEngine;

namespace GameCommon.Weapon
{
    /// <summary>武器局外进度的跨程序集只读通知。</summary>
    [EventInterface(EEventGroup.GroupUI)]
    public interface IWeaponPublicEvent
    {
        /// <summary>碎片、装备或待确认提示发生变化。</summary>
        void OnWeaponProgressChanged();

        /// <summary>本次变更新形成完整武器。</summary>
        void OnNewWeaponCompleted(int weaponId, int count);
    }
}
