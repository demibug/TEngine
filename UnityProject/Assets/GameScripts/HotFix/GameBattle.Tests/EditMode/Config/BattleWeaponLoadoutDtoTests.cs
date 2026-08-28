using GameCommon.Battle;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Config
{
    /// <summary>
    /// 战斗武器装载共享契约（BattleWeaponLoadoutDto）聚焦测试。
    /// 覆盖：默认值空判定、Basic 默认四槽 id、旧构造调用兼容、
    /// 部分零值原样保留、两个默认工厂携带 Basic 武器且保留 AI 语义。
    /// </summary>
    [TestFixture]
    internal class BattleWeaponLoadoutDtoTests
    {
        [Test]
        [Description("default(BattleWeaponLoadoutDto).IsEmpty 为 true。")]
        public void DefaultInstance_IsEmpty()
        {
            BattleWeaponLoadoutDto empty = default;

            Assert.IsTrue(empty.IsEmpty, "default 实例四槽全 0，IsEmpty 应为 true");
            Assert.AreEqual(0, empty.BowWeaponId);
            Assert.AreEqual(0, empty.SpearWeaponId);
            Assert.AreEqual(0, empty.KnifeWeaponId);
            Assert.AreEqual(0, empty.SwordWeaponId);
        }

        [Test]
        [Description("CreateBasicDefault 返回 1/11/21/32 且非空。")]
        public void BasicDefault_ReturnsOneElevenTwentyOneThirtyTwo()
        {
            BattleWeaponLoadoutDto basic = BattleWeaponLoadoutDto.CreateBasicDefault();

            Assert.IsFalse(basic.IsEmpty, "Basic 默认不应为空");
            Assert.AreEqual(1, basic.BowWeaponId, "弓槽应为 1");
            Assert.AreEqual(11, basic.SpearWeaponId, "枪槽应为 11");
            Assert.AreEqual(21, basic.KnifeWeaponId, "刀槽应为 21");
            Assert.AreEqual(32, basic.SwordWeaponId, "剑槽应为 32");
        }

        [Test]
        [Description("旧构造调用（未传 weapons）得到 Basic 默认武器装载。")]
        public void LegacyConstructorCall_WithoutWeapons_GetsBasicDefault()
        {
            var loadout = new BattleLoadoutDto(
                mapId: 0,
                round: 0,
                randomSeed: 123,
                configVersion: 0,
                configHash: string.Empty);

            Assert.AreEqual(BattleDeckPreset.Normal, loadout.DeckPreset, "牌组预设应保持默认");
            AssertWeaponsAreBasicDefault(loadout.Weapons);
        }

        [Test]
        [Description("显式传入的部分零值武器装载原样保留，不做静默修复。")]
        public void PartiallyZeroWeapons_ArePreservedAsIs()
        {
            var weapons = new BattleWeaponLoadoutDto(
                bowWeaponId: 5,
                spearWeaponId: 0,
                knifeWeaponId: 0,
                swordWeaponId: 42);

            var loadout = new BattleLoadoutDto(
                mapId: 0,
                round: 0,
                randomSeed: 0,
                configVersion: 0,
                configHash: string.Empty,
                weapons: weapons);

            Assert.AreEqual(5, loadout.Weapons.BowWeaponId, "弓槽部分零值应原样保留");
            Assert.AreEqual(0, loadout.Weapons.SpearWeaponId, "枪槽 0 不得被静默修复");
            Assert.AreEqual(0, loadout.Weapons.KnifeWeaponId, "刀槽 0 不得被静默修复");
            Assert.AreEqual(42, loadout.Weapons.SwordWeaponId, "剑槽部分零值应原样保留");
        }

        [Test]
        [Description("CreateMinimalDefault 携带 Basic 武器且保持对手模式关闭。")]
        public void MinimalDefault_HasBasicWeapons_AndKeepsOpponentModeNone()
        {
            BattleLoadoutDto minimal = BattleLoadoutDto.CreateMinimalDefault();

            Assert.AreEqual(BattleOpponentMode.None, minimal.OpponentMode, "最简默认对手模式应为 None");
            Assert.AreEqual(OpponentAiDifficulty.Easy, minimal.OpponentAiDifficulty);
            AssertWeaponsAreBasicDefault(minimal.Weapons);
        }

        [Test]
        [Description("CreateLocalAiDefault 携带 Basic 武器且保留 LocalAI 模式与难度参数。")]
        public void LocalAiDefault_HasBasicWeapons_AndKeepsAiSemantics()
        {
            BattleLoadoutDto localAi = BattleLoadoutDto.CreateLocalAiDefault(
                OpponentAiDifficulty.Expert);

            Assert.AreEqual(BattleOpponentMode.LocalAI, localAi.OpponentMode, "本地 AI 默认应为 LocalAI 模式");
            Assert.AreEqual(OpponentAiDifficulty.Expert, localAi.OpponentAiDifficulty, "难度参数应保留");
            AssertWeaponsAreBasicDefault(localAi.Weapons);
        }

        private static void AssertWeaponsAreBasicDefault(BattleWeaponLoadoutDto weapons)
        {
            Assert.IsFalse(weapons.IsEmpty, "武器装载不应为空");
            Assert.AreEqual(1, weapons.BowWeaponId);
            Assert.AreEqual(11, weapons.SpearWeaponId);
            Assert.AreEqual(21, weapons.KnifeWeaponId);
            Assert.AreEqual(32, weapons.SwordWeaponId);
        }
    }
}
