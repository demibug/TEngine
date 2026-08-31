using System;
using System.Collections.Generic;
using GameBattle.Weapon;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Config
{
    // ============================================================================
    // 任务 3.1/3.3/3.4/3.5：Weapon 配置领域、目录与默认 Resolver 测试
    // ----------------------------------------------------------------------------
    // 验证内容（design.md 决策 1/2 / specs/player-weapon-runtime/spec.md
    //   "Exactly four basic weapon definitions are enabled" /
    //   "Weapon category mapping is explicit"）：
    //   1. 黄金目录（id 1～44）可加载：恰好四条 Basic +1 启用（1/11/21/32），
    //      其余 40 行 disabled 不产生运行时状态。
    //   2. 目录对外不可变：按 id 升序稳定排序、查询只读。
    //   3. 重复 id 在目录构造即被拒绝（Provider 转 WeaponIdDuplicate）。
    //   4. BasicWeaponResolver 显式映射 Knife→21、Bow→1、Spear→11、Cavalry→32，
    //      不比较 Weapon 原始 type 数值与 Soldier 枚举数值相等。
    //   5. Resolver 拒绝缺启用行 / 多余启用行 / 错误类别 / 错误 handlerKey /
    //      错误附加攻击力，禁止隐式 fallback。
    // ============================================================================

    [TestFixture]
    internal class WeaponConfigTests
    {
        // ====================================================================
        // 黄金目录测试
        // ====================================================================

        [Test]
        [Description("黄金武器目录（id 1～44）可加载：恰好四条 Basic +1 启用（1/11/21/32）。")]
        public void GoldenCatalog_ExactlyFourBasicWeaponsEnabled()
        {
            WeaponCatalogSnapshot catalog = GoldenCatalog();

            Assert.IsNotNull(catalog, "WeaponCatalog 不应为 null");
            Assert.AreEqual(44, catalog.Definitions.Count, "黄金目录应有 44 个定义");

            int enabledCount = 0;
            foreach (WeaponDefinitionSnapshot def in catalog.Definitions)
            {
                if (!def.Enabled)
                {
                    continue;
                }

                enabledCount++;
                Assert.AreEqual(1, def.AddAttackPower, "启用武器附加攻击力应为 1");
                Assert.AreEqual("Basic", def.HandlerKey, "启用武器 handlerKey 应为 Basic");
            }

            Assert.AreEqual(4, enabledCount, "应恰好 4 条启用武器");
            Assert.IsTrue(catalog.ContainsId(1), "短弓 id=1 应存在");
            Assert.IsTrue(catalog.ContainsId(11), "短枪 id=11 应存在");
            Assert.IsTrue(catalog.ContainsId(21), "短刀 id=21 应存在");
            Assert.IsTrue(catalog.ContainsId(32), "短剑 id=32 应存在");

            Assert.IsTrue(catalog.TryGetById(1, out WeaponDefinitionSnapshot bow));
            Assert.AreEqual(WeaponType.Bow, bow.Type, "id=1 应为 Bow");
            Assert.IsTrue(catalog.TryGetById(32, out WeaponDefinitionSnapshot sword));
            Assert.AreEqual(WeaponType.Sword, sword.Type, "id=32 应为 Sword");
        }

        [Test]
        [Description("禁用行不产生运行时状态：其余 40 行 disabled 且 handlerKey 为空。")]
        public void GoldenCatalog_DisabledRows_HaveNoRuntimeState()
        {
            WeaponCatalogSnapshot catalog = GoldenCatalog();

            int disabledCount = 0;
            foreach (WeaponDefinitionSnapshot def in catalog.Definitions)
            {
                if (def.Enabled)
                {
                    continue;
                }

                disabledCount++;
                Assert.IsTrue(string.IsNullOrEmpty(def.HandlerKey),
                    $"禁用行 id={def.Id} handlerKey 应为空（不做 Basic fallback）");
            }

            Assert.AreEqual(40, disabledCount, "应有 40 条禁用武器");
        }

        // ====================================================================
        // 不可变性测试
        // ====================================================================

        [Test]
        [Description("目录不可变：Definitions 按 id 升序稳定排序，查询只读。")]
        public void WeaponCatalogSnapshot_ImmutableAndSortedById()
        {
            var catalog = new WeaponCatalogSnapshot(new[]
            {
                Def(32, WeaponType.Sword, 1, true, "Basic"),
                Def(1, WeaponType.Bow, 1, true, "Basic"),
                Def(11, WeaponType.Spear, 1, true, "Basic"),
                Def(21, WeaponType.Knife, 1, true, "Basic"),
            });

            Assert.AreEqual(4, catalog.Definitions.Count);
            Assert.AreEqual(1, catalog.Definitions[0].Id, "定义应按 id 升序");
            Assert.AreEqual(11, catalog.Definitions[1].Id);
            Assert.AreEqual(21, catalog.Definitions[2].Id);
            Assert.AreEqual(32, catalog.Definitions[3].Id);

            Assert.IsTrue(catalog.Definitions is IReadOnlyList<WeaponDefinitionSnapshot>,
                "Definitions 应为只读列表");
            Assert.Throws<NotSupportedException>(
                () => ((IList<WeaponDefinitionSnapshot>)catalog.Definitions)[0] =
                    Def(99, WeaponType.Sword, 0, false, null),
                "Definitions 不应允许通过 IList 写回");

            Assert.IsTrue(catalog.TryGetById(21, out WeaponDefinitionSnapshot knife));
            Assert.AreEqual(WeaponType.Knife, knife.Type);
            Assert.IsTrue(catalog.ContainsId(1));
            Assert.IsFalse(catalog.TryGetById(99, out _), "未知 id 不应命中");
        }

        [Test]
        [Description("重复 id 无法构建按 id 索引目录（构造抛 ArgumentException）。")]
        public void WeaponCatalogSnapshot_DuplicateId_ThrowsAtConstruction()
        {
            Assert.Throws<ArgumentException>(() => new WeaponCatalogSnapshot(new[]
            {
                Def(1, WeaponType.Bow, 1, true, "Basic"),
                Def(1, WeaponType.Bow, 2, false, null),
            }), "重复 id 应抛 ArgumentException");
        }

        [Test]
        [Description("定义不可变：五个字段构造后只读。")]
        public void WeaponDefinitionSnapshot_IsReadOnly()
        {
            WeaponDefinitionSnapshot def = Def(21, WeaponType.Knife, 1, true, "Basic");

            Assert.AreEqual(21, def.Id);
            Assert.AreEqual(WeaponType.Knife, def.Type);
            Assert.AreEqual(1, def.AddAttackPower);
            Assert.IsTrue(def.Enabled);
            Assert.AreEqual("Basic", def.HandlerKey);
        }

        // ====================================================================
        // BasicWeaponResolver 显式映射测试
        // ====================================================================

        [Test]
        [Description("四类玩家兵种各解析到指定 ID 且 +1（Knife→21、Bow→1、Spear→11、Cavalry→32）。")]
        public void Resolver_ResolvesAllFourCategories()
        {
            BasicWeaponResolver resolver = new BasicWeaponResolver(GoldenCatalog());

            AssertResolves(resolver, SoldierType.Knife, 21, WeaponType.Knife);
            AssertResolves(resolver, SoldierType.Bow, 1, WeaponType.Bow);
            AssertResolves(resolver, SoldierType.Spear, 11, WeaponType.Spear);
            AssertResolves(resolver, SoldierType.Cavalry, 32, WeaponType.Sword);
        }

        [Test]
        [Description("显式映射不按枚举原始数值相等推断：Bow 解析到 id 1，Knife 解析到 id 21。")]
        public void Resolver_ExplicitMapping_NotByNumericEquality()
        {
            // WeaponType.Bow=0 与 SoldierType.Knife=0 数值相同、WeaponType.Knife=2
            // 与 SoldierType.Spear=2 数值相同；若按数值相等推断会错误交叉映射。
            BasicWeaponResolver resolver = new BasicWeaponResolver(GoldenCatalog());

            Assert.IsTrue(resolver.TryResolve(SoldierType.Bow, out WeaponDefinitionSnapshot bow));
            Assert.AreEqual(1, bow.Id, "Bow 必须解析到 id 1（短弓）");
            Assert.AreEqual(WeaponType.Bow, bow.Type, "Bow 武器类别必须为 Bow");

            Assert.IsTrue(resolver.TryResolve(SoldierType.Knife, out WeaponDefinitionSnapshot knife));
            Assert.AreEqual(21, knife.Id, "Knife 必须解析到 id 21（短刀）");

            Assert.IsTrue(resolver.TryResolve(SoldierType.Spear, out WeaponDefinitionSnapshot spear));
            Assert.AreEqual(11, spear.Id, "Spear 必须解析到 id 11（短枪）");

            Assert.IsTrue(resolver.TryResolve(SoldierType.Cavalry, out WeaponDefinitionSnapshot cavalry));
            Assert.AreEqual(32, cavalry.Id, "Cavalry 必须解析到 id 32（短剑）");
            Assert.AreEqual(WeaponType.Sword, cavalry.Type, "Cavalry 武器类别必须为 Sword");
        }

        // ====================================================================
        // Resolver 非法目录拒绝测试（禁止隐式 fallback）
        // ====================================================================

        [Test]
        [Description("缺少任一启用行（如 id=1 未启用）→ 构造抛 InvalidOperationException。")]
        public void Resolver_MissingEnabledRow_Throws()
        {
            // id=1 改为 disabled。
            var catalog = new WeaponCatalogSnapshot(new[]
            {
                Def(1, WeaponType.Bow, 1, false, null),
                Def(11, WeaponType.Spear, 1, true, "Basic"),
                Def(21, WeaponType.Knife, 1, true, "Basic"),
                Def(32, WeaponType.Sword, 1, true, "Basic"),
            });

            Assert.Throws<InvalidOperationException>(
                () => new BasicWeaponResolver(catalog), "缺少启用行必须抛错，不 fallback");
        }

        [Test]
        [Description("多余启用行（id=1 也启用）→ 构造抛 InvalidOperationException。")]
        public void Resolver_ExtraEnabledRow_Throws()
        {
            var catalog = new WeaponCatalogSnapshot(new[]
            {
                Def(1, WeaponType.Bow, 1, true, "Basic"),
                Def(2, WeaponType.Bow, 2, true, null),
                Def(11, WeaponType.Spear, 1, true, "Basic"),
                Def(21, WeaponType.Knife, 1, true, "Basic"),
                Def(32, WeaponType.Sword, 1, true, "Basic"),
            });

            Assert.Throws<InvalidOperationException>(
                () => new BasicWeaponResolver(catalog), "多余启用行必须抛错");
        }

        [Test]
        [Description("启用行 handlerKey 非 Basic → 构造抛 InvalidOperationException。")]
        public void Resolver_WrongHandlerKey_Throws()
        {
            var catalog = new WeaponCatalogSnapshot(new[]
            {
                Def(1, WeaponType.Bow, 1, true, "Special"),
                Def(11, WeaponType.Spear, 1, true, "Basic"),
                Def(21, WeaponType.Knife, 1, true, "Basic"),
                Def(32, WeaponType.Sword, 1, true, "Basic"),
            });

            Assert.Throws<InvalidOperationException>(
                () => new BasicWeaponResolver(catalog), "handlerKey 非 Basic 必须抛错");
        }

        [Test]
        [Description("启用行附加攻击力非 1 → 构造抛 InvalidOperationException。")]
        public void Resolver_WrongAttackPower_Throws()
        {
            var catalog = new WeaponCatalogSnapshot(new[]
            {
                Def(1, WeaponType.Bow, 2, true, "Basic"),
                Def(11, WeaponType.Spear, 1, true, "Basic"),
                Def(21, WeaponType.Knife, 1, true, "Basic"),
                Def(32, WeaponType.Sword, 1, true, "Basic"),
            });

            Assert.Throws<InvalidOperationException>(
                () => new BasicWeaponResolver(catalog), "附加攻击力非 1 必须抛错");
        }

        [Test]
        [Description("启用行类别与 id 期望不符（id=1 填 Knife）→ 构造抛 InvalidOperationException。")]
        public void Resolver_WrongWeaponTypeForId_Throws()
        {
            var catalog = new WeaponCatalogSnapshot(new[]
            {
                Def(1, WeaponType.Knife, 1, true, "Basic"),
                Def(11, WeaponType.Spear, 1, true, "Basic"),
                Def(21, WeaponType.Knife, 1, true, "Basic"),
                Def(32, WeaponType.Sword, 1, true, "Basic"),
            });

            Assert.Throws<InvalidOperationException>(
                () => new BasicWeaponResolver(catalog), "类别与 id 期望不符必须抛错");
        }

        // ====================================================================
        // 测试辅助
        // ====================================================================

        /// <summary>获取黄金武器目录（id 1～44，与 weapon.xlsx 全量行等价）。</summary>
        private static WeaponCatalogSnapshot GoldenCatalog()
        {
            return new JsonBattleConfigProvider().GetSnapshot().WeaponCatalog;
        }

        /// <summary>构造武器定义。</summary>
        private static WeaponDefinitionSnapshot Def(
            int id, WeaponType type, int addAttackPower, bool enabled, string handlerKey)
        {
            return new WeaponDefinitionSnapshot(id, type, addAttackPower, enabled, handlerKey);
        }

        /// <summary>断言兵种解析到指定 id/类别且附加攻击力为 1。</summary>
        private static void AssertResolves(
            BasicWeaponResolver resolver,
            SoldierType soldierType,
            int expectedId,
            WeaponType expectedType)
        {
            Assert.IsTrue(resolver.TryResolve(soldierType, out WeaponDefinitionSnapshot definition),
                $"{soldierType} 应可解析");
            Assert.AreEqual(expectedId, definition.Id, $"{soldierType} 应解析到 id={expectedId}");
            Assert.AreEqual(expectedType, definition.Type, $"{soldierType} 武器类别应匹配");
            Assert.AreEqual(1, definition.AddAttackPower, $"{soldierType} 附加攻击力应为 1");
            Assert.AreEqual("Basic", definition.HandlerKey, $"{soldierType} handlerKey 应为 Basic");
        }
    }
}
