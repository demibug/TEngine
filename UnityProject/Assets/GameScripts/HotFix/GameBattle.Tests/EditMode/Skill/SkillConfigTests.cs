using System;
using System.Collections.Generic;
using GameConfig.battle;
using Luban;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Skill
{
    // ============================================================================
    // 任务 2.1/2.2/2.3/2.5：Skill 配置领域、Provider 接入与 Validator 校验测试
    // ----------------------------------------------------------------------------
    // 验证内容（design.md 决策 1/3 / specs/combat-skill-lifecycle/spec.md
    //   "Skill definitions are validated before use"）：
    //   1. 黄金全目录（skill.xlsx 19 行）可加载且通过校验；SoulCapture 的
    //      effectBuffType=13/effectDurationMs=2000 只读透传。
    //   2. 目录对外不可变：定义深拷贝、按 key 升序稳定排序、集合只读。
    //   3. 重复 key 在目录构造即被拒绝（Provider 转 SkillKeyDuplicate）。
    //   4. Validator 拒绝空 key、未知 category、负 cooldown、空 handlerKey；
    //      诊断包含 Skill key 与字段名。
    //   5. 未实现 handler 的技能行可留在目录（spec "Catalog rows MAY exist without
    //      a registered handler, but such rows MUST NOT be attached"）。
    //   6. Luban Provider 显式消费 TbSkill 行（Key/Category/CooldownSeconds/
    //      HandlerKey/EffectBuffType/EffectDurationMs），严格拒绝
    //      null/负 cooldown 与未知 category，不做 fallback。
    // ============================================================================

    [TestFixture]
    internal class SkillConfigTests
    {
        // ====================================================================
        // 黄金全目录测试
        // ====================================================================

        [Test]
        [Description("黄金全目录（skill.xlsx 19 行）可加载且通过启动校验。")]
        public void GoldenCatalog_ValidFullCatalog_PassesValidation()
        {
            BattleConfigSnapshot snapshot = GoldenSnapshot();

            Assert.IsNotNull(snapshot.SkillCatalog, "SkillCatalog 不应为 null");
            Assert.AreEqual(19, snapshot.SkillCatalog.Definitions.Count, "黄金目录应有 19 个定义");

            // 类别分布：6 active + 1 passive + 12 boss（与 skill.xlsx 一致）。
            int active = 0, passive = 0, boss = 0;
            foreach (SkillDefinitionSnapshot def in snapshot.SkillCatalog.Definitions)
            {
                switch (def.Category)
                {
                    case SkillCategory.Active: active++; break;
                    case SkillCategory.Passive: passive++; break;
                    case SkillCategory.Boss: boss++; break;
                }
            }

            Assert.AreEqual(6, active, "应有 6 个 active 技能");
            Assert.AreEqual(1, passive, "应有 1 个 passive 技能（StunPassive）");
            Assert.AreEqual(12, boss, "应有 12 个 boss 技能");

            // 冷却以 checked 转成毫秒：SoulCapture cooldownSeconds=8 → 8000ms。
            Assert.IsTrue(snapshot.SkillCatalog.TryGetByKey("SoulCapture", out SkillDefinitionSnapshot soulCapture));
            Assert.AreEqual(SkillCategory.Boss, soulCapture.Category);
            Assert.AreEqual(8000L, soulCapture.CooldownMs, "SoulCapture 冷却应为 8000ms");

            // EffectBuffType/EffectDurationMs 只读透传：仅 SoulCapture 携带。
            Assert.AreEqual(13, soulCapture.EffectBuffType, "SoulCapture effectBuffType 应为 13");
            Assert.AreEqual(2000, soulCapture.EffectDurationMs, "SoulCapture effectDurationMs 应为 2000");

            // 本测试只验证 Skill 子域。黄金 JSON 的其他子节错误不应遮蔽合法 Skill 目录。
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);
            Assert.IsFalse(HasSkillError(result), "黄金全目录不应产生任何 Skill 校验错误。");
        }

        [Test]
        [Description("黄金目录使用配置中显式 handlerKey，不在运行时推导 fallback。")]
        public void GoldenCatalog_PreservesExplicitHandlerKeys()
        {
            BattleConfigSnapshot snapshot = GoldenSnapshot();

            // handlerKey 与 key 一致（skill.xlsx 现状）。
            foreach (SkillDefinitionSnapshot def in snapshot.SkillCatalog.Definitions)
            {
                Assert.AreEqual(def.Key, def.HandlerKey, "黄金目录 handlerKey 应与 key 相同");
            }
        }

        // ====================================================================
        // 不可变性测试
        // ====================================================================

        [Test]
        [Description("目录不可变：Definitions 按 key 升序稳定排序，查询命中一致。")]
        public void SkillCatalogSnapshot_ImmutableAndSorted()
        {
            var catalog = new SkillCatalogSnapshot(new[]
            {
                Def("SoulCapture", SkillCategory.Boss, 8000, "SoulCapture"),
                Def("ArrowRain", SkillCategory.Active, 0, "ArrowRain"),
                Def("StunPassive", SkillCategory.Passive, 0, "StunPassive"),
            });

            Assert.AreEqual(3, catalog.Definitions.Count);
            Assert.AreEqual("ArrowRain", catalog.Definitions[0].Key, "定义应按 key 升序");
            Assert.AreEqual("SoulCapture", catalog.Definitions[1].Key);
            Assert.AreEqual("StunPassive", catalog.Definitions[2].Key);

            Assert.IsTrue(catalog.Definitions is IReadOnlyList<SkillDefinitionSnapshot>,
                "Definitions 应为只读列表");
            Assert.Throws<NotSupportedException>(
                () => ((IList<SkillDefinitionSnapshot>)catalog.Definitions)[0] =
                    Def("LeapSlash", SkillCategory.Active, 0, "LeapSlash"),
                "Definitions 不应允许通过 IList 写回");
            Assert.IsTrue(catalog.TryGetByKey("SoulCapture", out SkillDefinitionSnapshot found));
            Assert.AreEqual(8000L, found.CooldownMs);
            Assert.IsFalse(catalog.TryGetByKey("UnknownSkill", out _), "未知 key 不应命中");
        }

        [Test]
        [Description("重复 key 无法构建按 key 索引目录（构造抛 ArgumentException）。")]
        public void SkillCatalogSnapshot_DuplicateKey_ThrowsAtConstruction()
        {
            Assert.Throws<ArgumentException>(() => new SkillCatalogSnapshot(new[]
            {
                Def("SoulCapture", SkillCategory.Boss, 8000, "SoulCapture"),
                Def("SoulCapture", SkillCategory.Boss, 8000, "SoulCaptureDup"),
            }), "重复 key 应抛 ArgumentException");
        }

        // ====================================================================
        // Validator：缺失/未知/非法字段测试
        // ====================================================================

        [Test]
        [Description("SkillCatalog 为 null → MissingSection（启动门禁拒绝，不静默降级）。")]
        public void Validate_NullSkillCatalog_ReturnsMissingSection()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(null);
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingSection, "SkillCatalog"),
                "null 目录应报告 MissingSection（路径含 SkillCatalog）。");
        }

        [Test]
        [Description("Skill 目录为空 → MissingField。")]
        public void Validate_EmptySkillCatalog_ReturnsMissingField()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(
                new SkillCatalogSnapshot(Array.Empty<SkillDefinitionSnapshot>()));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingField, "SkillCatalog.Definitions"),
                "空目录应报告 MissingField。");
        }

        [Test]
        [Description("空 key 可进入目录但被 Validator 拒绝 → SkillKeyInvalid（缺字段在缺配置时检出）。")]
        public void Validate_EmptyKey_ReturnsSkillKeyInvalid()
        {
            // 目录允许空 key（构造不抛），保证 Validator 的 SkillKeyInvalid 检查可达。
            var catalog = new SkillCatalogSnapshot(new[]
            {
                Def("", SkillCategory.Active, 0, "EmptyKeyHandler"),
            });
            BattleConfigSnapshot snapshot = RebuildWithCatalog(catalog);
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.SkillKeyInvalid, "SkillCatalog.Definitions"),
                "空 key 应报告 SkillKeyInvalid。");
        }

        [Test]
        [Description("未知 Category → SkillCategoryUnknown，诊断含 Skill key 与字段名。")]
        public void Validate_UnknownCategory_ReturnsSkillCategoryUnknown()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new SkillCatalogSnapshot(new[]
            {
                Def("ArrowRain", SkillCategory.Active, 0, "ArrowRain"),
                BadDef("SoulCapture", (SkillCategory)99, 8000, "SoulCapture"),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.SkillCategoryUnknown, "Skill.SoulCapture.Category"),
                "未知 Category 应报告 SkillCategoryUnknown，路径含 Skill key 与 Category 字段。");
        }

        [Test]
        [Description("负冷却毫秒 → SkillCooldownInvalid，诊断含 Skill key 与字段名。")]
        public void Validate_NegativeCooldown_ReturnsSkillCooldownInvalid()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new SkillCatalogSnapshot(new[]
            {
                Def("ArrowRain", SkillCategory.Active, 0, "ArrowRain"),
                BadDef("BrokenCooldown", SkillCategory.Active, -1, "BrokenCooldown"),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.SkillCooldownInvalid, "Skill.BrokenCooldown.CooldownMs"),
                "负冷却应报告 SkillCooldownInvalid。");
        }

        [Test]
        [Description("空 handlerKey → SkillHandlerKeyMissing，诊断含 Skill key 与字段名。")]
        public void Validate_EmptyHandlerKey_ReturnsSkillHandlerKeyMissing()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new SkillCatalogSnapshot(new[]
            {
                Def("ArrowRain", SkillCategory.Active, 0, "ArrowRain"),
                Def("NoHandler", SkillCategory.Active, 0, ""),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.SkillHandlerKeyMissing, "Skill.NoHandler.HandlerKey"),
                "空 handlerKey 应报告 SkillHandlerKeyMissing。");
        }

        [Test]
        [Description("未实现 handler 的技能行可留在目录并保持合法（不得 attach，但目录/校验不拒绝）。")]
        public void Validate_UnimplementedHandlerKey_StaysInCatalog()
        {
            // handlerKey 非空但未在 registry 注册：目录/校验器不检查注册，保持合法。
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new SkillCatalogSnapshot(new[]
            {
                Def("ArrowRain", SkillCategory.Active, 0, "ArrowRain"),
                Def("NotImplemented", SkillCategory.Boss, 8000, "NotRegisteredHandler"),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(HasSkillError(result),
                "未实现 handler 的技能行不应产生任何 Skill 校验错误（attach 时由 Runner 拒绝）。");
        }

        [Test]
        [Description("多个非法 Skill 字段一次性收集，不因首个错误中止。")]
        public void Validate_MultipleSkillErrors_AllCollected()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new SkillCatalogSnapshot(new[]
            {
                Def("ArrowRain", SkillCategory.Active, 0, "ArrowRain"),
                // 未知 Category + 负冷却 + 空 handlerKey 同一行全部检出。
                BadDef("Broken", (SkillCategory)99, -1, ""),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.SkillCategoryUnknown, "Skill.Broken"),
                "未知 Category 应被收集。");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.SkillCooldownInvalid, "Skill.Broken"),
                "负冷却应被收集。");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.SkillHandlerKeyMissing, "Skill.Broken"),
                "空 handlerKey 应被收集。");
        }

        // ====================================================================
        // Luban Provider 显式消费测试（task 2.2）
        // ====================================================================

        [Test]
        [Description("BuildSkillCatalogFromLuban 显式消费 TbSkill 行，不得按 key 推导或 fallback。")]
        public void Provider_ConsumesTbSkillRows_Directly()
        {
            TbSkill tbSkill = BuildTbSkill(
                SkillRow("ArrowRain", "active", 0, "46141", null, "ArrowRain", null, null),
                SkillRow("SoulCapture", "boss", 8, "12054", null, "SoulCapture", 13, 2000),
                SkillRow("StunPassive", "passive", 0, "46279", null, "StunPassive", null, null),
                SkillRow("Inspire", "boss", 10, "12070", "INFERRED_HEALTH_MULTIPLIER", "Inspire", null, null));

            SkillCatalogSnapshot catalog = LubanBattleConfigProvider.BuildSkillCatalogFromLuban(tbSkill);

            Assert.AreEqual(4, catalog.Definitions.Count);

            Assert.IsTrue(catalog.TryGetByKey("ArrowRain", out SkillDefinitionSnapshot arrowRain));
            Assert.AreEqual(SkillCategory.Active, arrowRain.Category);
            Assert.AreEqual(0L, arrowRain.CooldownMs, "cooldownSeconds=0 应转 0ms");
            Assert.AreEqual("ArrowRain", arrowRain.HandlerKey);
            Assert.IsNull(arrowRain.EffectBuffType);
            Assert.IsNull(arrowRain.EffectDurationMs);

            Assert.IsTrue(catalog.TryGetByKey("SoulCapture", out SkillDefinitionSnapshot soulCapture));
            Assert.AreEqual(SkillCategory.Boss, soulCapture.Category);
            Assert.AreEqual(8000L, soulCapture.CooldownMs, "cooldownSeconds=8 应转 8000ms");
            Assert.AreEqual(13, soulCapture.EffectBuffType, "EffectBuffType 应来自配置行");
            Assert.AreEqual(2000, soulCapture.EffectDurationMs, "EffectDurationMs 应来自配置行");

            Assert.IsTrue(catalog.TryGetByKey("StunPassive", out SkillDefinitionSnapshot stunPassive));
            Assert.AreEqual(SkillCategory.Passive, stunPassive.Category, "passive 应严格映射 Passive");

            Assert.IsTrue(catalog.TryGetByKey("Inspire", out _));
        }

        [Test]
        [Description("null CooldownSeconds 无法转换 → Provider 抛 SkillCooldownInvalid，路径含 key。")]
        public void Provider_NullCooldown_ThrowsSkillCooldownInvalid()
        {
            TbSkill tbSkill = BuildTbSkill(
                SkillRow("ArrowRain", "active", 0, "46141", null, "ArrowRain", null, null),
                SkillRow("NoCooldown", "active", null, "99999", null, "NoCooldown", null, null));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildSkillCatalogFromLuban(tbSkill));

            Assert.AreEqual(BattleConfigErrorCategory.SkillCooldownInvalid, ex.Category);
            Assert.IsTrue(ex.Path.StartsWith("Skill.NoCooldown.CooldownSeconds", StringComparison.Ordinal),
                "路径应定位到 Skill key 与 CooldownSeconds 字段。");
        }

        [Test]
        [Description("负 CooldownSeconds → Provider 抛 SkillCooldownInvalid。")]
        public void Provider_NegativeCooldown_ThrowsSkillCooldownInvalid()
        {
            TbSkill tbSkill = BuildTbSkill(
                SkillRow("ArrowRain", "active", 0, "46141", null, "ArrowRain", null, null),
                SkillRow("NegativeCd", "active", -1, "99998", null, "NegativeCd", null, null));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildSkillCatalogFromLuban(tbSkill));

            Assert.AreEqual(BattleConfigErrorCategory.SkillCooldownInvalid, ex.Category);
            Assert.IsTrue(ex.Path.StartsWith("Skill.NegativeCd.CooldownSeconds", StringComparison.Ordinal),
                "路径应定位到 Skill key 与 CooldownSeconds 字段。");
        }

        [Test]
        [Description("空 handlerKey → Provider 抛 SkillHandlerKeyMissing（必填，不做 fallback）。")]
        public void Provider_EmptyHandlerKey_ThrowsSkillHandlerKeyMissing()
        {
            TbSkill tbSkill = BuildTbSkill(
                SkillRow("ArrowRain", "active", 0, "46141", null, "ArrowRain", null, null),
                SkillRow("NoHandler", "active", 0, "99995", null, "", null, null));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildSkillCatalogFromLuban(tbSkill));

            Assert.AreEqual(BattleConfigErrorCategory.SkillHandlerKeyMissing, ex.Category);
            Assert.IsTrue(ex.Path.StartsWith("Skill.NoHandler.HandlerKey", StringComparison.Ordinal),
                "路径应定位到 Skill key 与 HandlerKey 字段。");
        }

        [Test]
        [Description("未知 Category → Provider 抛 SkillCategoryUnknown（严格映射，不做 fallback）。")]
        public void Provider_UnknownCategory_ThrowsSkillCategoryUnknown()
        {
            TbSkill tbSkill = BuildTbSkill(
                SkillRow("ArrowRain", "active", 0, "46141", null, "ArrowRain", null, null),
                SkillRow("WeirdCategory", "ultra", 0, "99997", null, "WeirdCategory", null, null));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildSkillCatalogFromLuban(tbSkill));

            Assert.AreEqual(BattleConfigErrorCategory.SkillCategoryUnknown, ex.Category);
            Assert.IsTrue(ex.Path.StartsWith("Skill.WeirdCategory.Category", StringComparison.Ordinal),
                "路径应定位到 Skill key 与 Category 字段。");
        }

        [Test]
        [Description("空 Category → Provider 抛 SkillCategoryUnknown（大小写严格，空值不做默认映射）。")]
        public void Provider_EmptyCategory_ThrowsSkillCategoryUnknown()
        {
            TbSkill tbSkill = BuildTbSkill(
                SkillRow("ArrowRain", "active", 0, "46141", null, "ArrowRain", null, null),
                SkillRow("NoCategory", "", 0, "99996", null, "NoCategory", null, null));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildSkillCatalogFromLuban(tbSkill));

            Assert.AreEqual(BattleConfigErrorCategory.SkillCategoryUnknown, ex.Category);
        }

        [Test]
        [Description("大小写严格：'Active'/'Boss'（非小写）不得被误映射为合法类别。")]
        public void Provider_CategoryIsCaseSensitive_NoFallback()
        {
            TbSkill tbSkill = BuildTbSkill(
                SkillRow("ArrowRain", "Active", 0, "46141", null, "ArrowRain", null, null));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildSkillCatalogFromLuban(tbSkill));

            Assert.AreEqual(BattleConfigErrorCategory.SkillCategoryUnknown, ex.Category,
                "'Active'（大写）严格按实际 xlsx 小写映射，不应被静默接受。");
        }

        // ====================================================================
        // 测试辅助
        // ====================================================================

        /// <summary>获取黄金基线快照（含完整 Skill 目录）。</summary>
        private static BattleConfigSnapshot GoldenSnapshot()
        {
            return new JsonBattleConfigProvider().GetSnapshot();
        }

        /// <summary>用自定义 Skill 目录重建快照（其余子节沿用黄金基线）。</summary>
        private static BattleConfigSnapshot RebuildWithCatalog(SkillCatalogSnapshot skillCatalog)
        {
            BattleConfigSnapshot basis = GoldenSnapshot();
            return new BattleConfigSnapshot(
                map: basis.Map,
                enemy: basis.Enemy,
                wave: basis.Wave,
                units: basis.Units,
                unitLevel: basis.UnitLevel,
                economy: basis.Economy,
                deck: basis.Deck,
                projectile: basis.Projectile,
                missingFieldNotes: basis.MissingFieldNotes,
                sourceTag: "Test",
                enemyCatalog: basis.EnemyCatalog,
                orderedWavePlan: basis.OrderedWavePlan,
                buffCatalog: basis.BuffCatalog,
                skillCatalog: skillCatalog);
        }

        /// <summary>构造合法 Skill 定义（默认空 effect 字段）。</summary>
        private static SkillDefinitionSnapshot Def(
            string key, SkillCategory category, long cooldownMs, string handlerKey)
        {
            return new SkillDefinitionSnapshot(key, category, cooldownMs, handlerKey, null, null);
        }

        /// <summary>构造任意字段值的 Skill 定义（用于非法变体）。</summary>
        private static SkillDefinitionSnapshot BadDef(
            string key, SkillCategory category, long cooldownMs, string handlerKey)
        {
            return new SkillDefinitionSnapshot(key, category, cooldownMs, handlerKey, null, null);
        }

        /// <summary>判断结果中是否包含指定类别与可选路径前缀的错误项。</summary>
        private static bool HasError(
            BattleConfigValidationResult result,
            BattleConfigErrorCategory category,
            string pathPrefix = null)
        {
            for (int i = 0; i < result.Errors.Count; i++)
            {
                BattleConfigValidationError e = result.Errors[i];
                if (e.Category != category)
                {
                    continue;
                }

                if (pathPrefix == null || e.Path.StartsWith(pathPrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>是否含 Skill 子域错误；忽略由其他 change 独立拥有的子节错误。</summary>
        private static bool HasSkillError(BattleConfigValidationResult result)
        {
            for (int i = 0; i < result.Errors.Count; i++)
            {
                BattleConfigErrorCategory category = result.Errors[i].Category;
                if (category >= BattleConfigErrorCategory.SkillKeyInvalid
                    && category <= BattleConfigErrorCategory.SkillHandlerKeyMissing)
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================
        // Luban Bean 构造（手写 ByteBuf，字段顺序与生成代码一致）
        // ====================================================================

        private readonly struct SkillRowData
        {
            public readonly string Key;
            public readonly string Category;
            public readonly int? CooldownSeconds;
            public readonly string Source;
            public readonly string Confidence;
            public readonly string HandlerKey;
            public readonly int? EffectBuffType;
            public readonly int? EffectDurationMs;

            public SkillRowData(
                string key, string category, int? cooldownSeconds, string source,
                string confidence, string handlerKey, int? effectBuffType, int? effectDurationMs)
            {
                Key = key;
                Category = category;
                CooldownSeconds = cooldownSeconds;
                Source = source;
                Confidence = confidence;
                HandlerKey = handlerKey;
                EffectBuffType = effectBuffType;
                EffectDurationMs = effectDurationMs;
            }
        }

        private static SkillRowData SkillRow(
            string key, string category, int? cooldownSeconds, string source,
            string confidence, string handlerKey, int? effectBuffType, int? effectDurationMs)
        {
            return new SkillRowData(key, category, cooldownSeconds, source, confidence,
                handlerKey, effectBuffType, effectDurationMs);
        }

        private static TbSkill BuildTbSkill(params SkillRowData[] rows)
        {
            var buf = new ByteBuf();
            buf.WriteSize(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                WriteSkillRow(buf, rows[i]);
            }

            return new TbSkill(buf);
        }

        private static void WriteSkillRow(ByteBuf buf, SkillRowData row)
        {
            buf.WriteString(row.Key);       // Key
            buf.WriteString("");            // Name
            buf.WriteString(row.Category);  // Category
            buf.WriteString("");            // Description
            buf.WriteBool(false);           // HealthMultiplier: null
            buf.WriteBool(false);           // Speed: null
            buf.WriteBool(false);           // RangeTiles: null
            WriteNullableInt(buf, row.CooldownSeconds); // CooldownSeconds
            buf.WriteString(row.Source);    // Source
            WriteNullableString(buf, row.Confidence);   // Confidence
            buf.WriteString(row.HandlerKey); // HandlerKey
            WriteNullableInt(buf, row.EffectBuffType);  // EffectBuffType
            WriteNullableInt(buf, row.EffectDurationMs); // EffectDurationMs
        }

        private static void WriteNullableInt(ByteBuf buf, int? value)
        {
            if (value.HasValue)
            {
                buf.WriteBool(true);
                buf.WriteInt(value.Value);
            }
            else
            {
                buf.WriteBool(false);
            }
        }

        private static void WriteNullableString(ByteBuf buf, string value)
        {
            if (value != null)
            {
                buf.WriteBool(true);
                buf.WriteString(value);
            }
            else
            {
                buf.WriteBool(false);
            }
        }
    }
}
