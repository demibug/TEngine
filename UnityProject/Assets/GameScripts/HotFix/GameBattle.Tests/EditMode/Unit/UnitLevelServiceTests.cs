using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Unit
{
    // ============================================================================
    // UnitLevelService 测试（最终方案 Unit/UnitLevelService）
    // ----------------------------------------------------------------------------
    // 验证内容：
    //   1. 校验最大等级（合并上限）。
    //   2. 从 UnitLevelConfigSnapshot 解析伤害及攻速倍率。
    //   3. 统一应用等级数值：damage = base × DamageMultiplier[level-1]，
    //      interval = base ÷ AttackSpeedMultiplier[level-1]。
    //   4. C# 整数伤害统一采用四舍五入。
    //   5. 配置缺失或数组越界时回退默认倍率 1.0。
    // ============================================================================

    /// <summary>
    /// UnitLevelService 单元测试（最终方案：等级倍率解析与应用）。
    /// </summary>
    [TestFixture]
    internal class UnitLevelServiceTests
    {
        private static UnitLevelConfigSnapshot MakeConfig(
            int maxLevel = 3,
            float[] damageMultipliers = null,
            float[] speedMultipliers = null)
        {
            return new UnitLevelConfigSnapshot(
                maxLevel,
                damageMultipliers ?? new float[] { 1f, 1.2f, 1.5f },
                speedMultipliers ?? new float[] { 1f, 1.1f, 1.2f });
        }

        [Test]
        [Description("MaxLevel 来自配置；配置缺失时回退 RecruitDefinitions.MaxLevel。")]
        public void MaxLevel_FromConfig_OrFallback()
        {
            Assert.AreEqual(3, new UnitLevelService(MakeConfig()).MaxLevel, "MaxLevel 来自配置");
            Assert.AreEqual(RecruitDefinitions.MaxLevel, new UnitLevelService(null).MaxLevel,
                "配置缺失回退 RecruitDefinitions.MaxLevel");
        }

        [Test]
        [Description("IsMaxLevel 判断当前等级是否达到最大等级。")]
        public void IsMaxLevel_ChecksLimit()
        {
            var service = new UnitLevelService(MakeConfig(maxLevel: 3));
            Assert.IsFalse(service.IsMaxLevel(2), "2 级未满");
            Assert.IsTrue(service.IsMaxLevel(3), "3 级满级");
            Assert.IsTrue(service.IsMaxLevel(4), "超过最大等级视为满级");
        }

        [Test]
        [Description("ResolveDamage：damage = base × DamageMultiplier[level-1]。")]
        public void ResolveDamage_MultipliesBaseByLevelMultiplier()
        {
            var service = new UnitLevelService(MakeConfig());
            Assert.AreEqual(10, service.ResolveDamage(10, 1), "Lv1 倍率 1.0 → 10");
            Assert.AreEqual(12, service.ResolveDamage(10, 2), "Lv2 倍率 1.2 → 12");
            Assert.AreEqual(15, service.ResolveDamage(10, 3), "Lv3 倍率 1.5 → 15");
        }

        [Test]
        [Description("ResolveDamage 整数伤害四舍五入（AwayFromZero）。")]
        public void ResolveDamage_RoundsToNearest()
        {
            // 10 × 1.15 = 11.5 → 四舍五入到 12。
            var service = new UnitLevelService(MakeConfig(
                maxLevel: 3,
                damageMultipliers: new float[] { 1f, 1.15f, 1.5f }));
            Assert.AreEqual(12, service.ResolveDamage(10, 2), "10×1.15=11.5 四舍五入到 12");
        }

        [Test]
        [Description("ResolveAttackInterval：interval = base ÷ AttackSpeedMultiplier[level-1]。")]
        public void ResolveAttackInterval_DividesBaseBySpeedMultiplier()
        {
            var service = new UnitLevelService(MakeConfig());
            Assert.AreEqual(1.0f, service.ResolveAttackInterval(1.0f, 1), "Lv1 倍率 1.0 → 1.0");
            Assert.AreEqual(1.0f / 1.1f, service.ResolveAttackInterval(1.0f, 2), "Lv2 倍率 1.1 → 1/1.1");
            Assert.AreEqual(1.0f / 1.2f, service.ResolveAttackInterval(1.0f, 3), "Lv3 倍率 1.2 → 1/1.2");
        }

        [Test]
        [Description("倍率越界/缺失时回退 1.0。")]
        public void Multipliers_OutOfRange_FallbackToOne()
        {
            var service = new UnitLevelService(MakeConfig(maxLevel: 3));
            Assert.AreEqual(1f, service.GetDamageMultiplier(5), "越界伤害倍率回退 1.0");
            Assert.AreEqual(1f, service.GetAttackSpeedMultiplier(5), "越界攻速倍率回退 1.0");
        }

        [Test]
        [Description("配置为 null 时倍率回退 1.0，伤害等于基础值。")]
        public void NullConfig_FallbackToBase()
        {
            var service = new UnitLevelService(null);
            Assert.AreEqual(1f, service.GetDamageMultiplier(2), "配置缺失伤害倍率 1.0");
            Assert.AreEqual(1f, service.GetAttackSpeedMultiplier(2), "配置缺失攻速倍率 1.0");
            Assert.AreEqual(7, service.ResolveDamage(7, 2), "配置缺失伤害等于基础值");
            Assert.AreEqual(0.9f, service.ResolveAttackInterval(0.9f, 2), "配置缺失间隔等于基础值");
        }
    }
}
