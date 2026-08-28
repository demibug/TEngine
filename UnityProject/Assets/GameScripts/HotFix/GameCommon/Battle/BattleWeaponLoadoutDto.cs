namespace GameCommon.Battle
{
    /// <summary>
    /// 不可变战斗武器装载 DTO（跨程序集公共契约，四槽）。
    /// </summary>
    /// <remarks>
    /// 归属：GameCommon 是跨程序集公共契约的唯一归属（见 specs/battle-event-boundary）。
    /// 四个槽位分别对应弓/枪/刀/剑的武器配置 id；值为 0 表示该槽位未配置。
    /// 本 DTO 只承载 id 数据，不实现存在性、类型、enabled 校验——校验由 GameBattle
    /// 在进入战斗时负责。构造后不可修改；默认值 <see cref="default"/>（全零）表示
    /// “未指定”，由调用方决定如何解释。
    /// </remarks>
    public readonly struct BattleWeaponLoadoutDto
    {
        /// <summary>弓（Bow）槽位武器配置 id。0 表示未配置。</summary>
        public readonly int BowWeaponId;

        /// <summary>枪（Spear）槽位武器配置 id。0 表示未配置。</summary>
        public readonly int SpearWeaponId;

        /// <summary>刀（Knife）槽位武器配置 id。0 表示未配置。</summary>
        public readonly int KnifeWeaponId;

        /// <summary>剑（Sword）槽位武器配置 id。0 表示未配置。</summary>
        public readonly int SwordWeaponId;

        /// <summary>
        /// 构造四槽武器装载。任一槽位为 0 表示该槽位未配置，原样保留，不做隐式修复。
        /// </summary>
        /// <param name="bowWeaponId">弓槽位武器配置 id。</param>
        /// <param name="spearWeaponId">枪槽位武器配置 id。</param>
        /// <param name="knifeWeaponId">刀槽位武器配置 id。</param>
        /// <param name="swordWeaponId">剑槽位武器配置 id。</param>
        public BattleWeaponLoadoutDto(
            int bowWeaponId,
            int spearWeaponId,
            int knifeWeaponId,
            int swordWeaponId)
        {
            BowWeaponId = bowWeaponId;
            SpearWeaponId = spearWeaponId;
            KnifeWeaponId = knifeWeaponId;
            SwordWeaponId = swordWeaponId;
        }

        /// <summary>
        /// 四槽均为 0 时返回 true（表示未指定任何武器）。
        /// </summary>
        public bool IsEmpty
            => BowWeaponId == 0
               && SpearWeaponId == 0
               && KnifeWeaponId == 0
               && SwordWeaponId == 0;

        /// <summary>
        /// 基础四兵默认武器装载：弓 1、枪 11、刀 21、剑 32。
        /// </summary>
        public static BattleWeaponLoadoutDto CreateBasicDefault()
            => new BattleWeaponLoadoutDto(
                bowWeaponId: 1,
                spearWeaponId: 11,
                knifeWeaponId: 21,
                swordWeaponId: 32);
    }
}
