using System;
using System.Collections.Generic;
using GameCommon.Battle;
using GameConfig.battle;
using Luban;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Config
{
    // ============================================================================
    // 任务 3.1/3.3/3.5/3.6：Buff 配置领域、Provider 接入与 Validator 校验测试
    // ----------------------------------------------------------------------------
    // 验证内容（design.md 决策 1 / specs/combat-buff-lifecycle/spec.md
    //   "Buff definitions form a validated immutable catalog"）：
    //   1. 黄金全目录（type 1～20）可加载且通过校验；type 13/15 的 state channel 5/4
    //      保持合法（原表既有行，4/5 的一次性语义在目标申请阶段处理）。
    //   2. 目录对外不可变：Channels 深拷贝、按 type 升序稳定排序、集合只读。
    //   3. 重复 type 在目录构造即被拒绝（Provider 转 BuffTypeDuplicate）。
    //   4. Validator 拒绝未知 kind/policy/channel、Numeric/State 缺 channel、非正
    //      maxStacks；Custom 允许空 channel；诊断包含 Buff type 与字段名。
    //   5. Luban Provider 显式消费 TbBuff 行（Type/Name/Label/Kind/Channels/
    //      StackPolicy/MaxStacks/ConflictKey），不按 type 推导或 fallback。
    // ============================================================================

    [TestFixture]
    internal class BuffConfigTests
    {
        // ====================================================================
        // 黄金全目录测试
        // ====================================================================

        [Test]
        [Description("黄金全目录（type 1～20）可加载且通过启动校验，type 13/15 的原表通道保持合法。")]
        public void GoldenCatalog_ValidFullCatalog_PassesValidation()
        {
            BattleConfigSnapshot snapshot = GoldenSnapshot();
            BattleConfigSnapshot basis = snapshot;

            Assert.IsNotNull(snapshot.BuffCatalog, "BuffCatalog 不应为 null");
            Assert.AreEqual(20, snapshot.BuffCatalog.Definitions.Count, "黄金目录应有 20 个定义");
            Assert.AreEqual(20, snapshot.BuffCatalog.Types.Count, "Types 应有 20 个");

            // 按 type 升序稳定排序。
            for (int i = 0; i < snapshot.BuffCatalog.Types.Count; i++)
            {
                Assert.AreEqual(i + 1, snapshot.BuffCatalog.Types[i], "Types 应按 type 升序");
            }

            // 关键类型抽查。
            Assert.IsTrue(snapshot.BuffCatalog.TryGetByType(8, out BuffDefinitionSnapshot custom));
            Assert.AreEqual(BuffKind.Custom, custom.Kind, "type 8 应为 Custom");
            Assert.AreEqual(0, custom.Channels.Count, "Custom 允许空通道");

            // type 13/15 来自原表：state channel 5/4 是合法目录通道（申请阶段处理 4/5 语义）。
            Assert.IsTrue(snapshot.BuffCatalog.TryGetByType(13, out BuffDefinitionSnapshot knockback));
            Assert.AreEqual(BuffKind.State, knockback.Kind);
            Assert.IsTrue(knockback.HasChannel((int)BuffStateChannel.KnockbackImpulse),
                "type 13 knockback 应保留原表 state channel 5");
            Assert.IsTrue(snapshot.BuffCatalog.TryGetByType(15, out BuffDefinitionSnapshot burnStatic));
            Assert.IsTrue(burnStatic.HasChannel((int)BuffStateChannel.DamageImpulse),
                "type 15 burnStatic 应保留原表 state channel 4");

            // 本测试只验证 Buff 子域。黄金 JSON 的地图/波次基线可能被其他 change
            // 独立调整，不应让非 Buff 错误遮蔽合法 Buff 目录。
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);
            Assert.IsFalse(HasBuffError(result), "黄金全目录不应产生任何 Buff 校验错误。");
        }

        [Test]
        [Description("黄金目录与生产 TbBuff 等价：type 9/19 的中文标签被保留。")]
        public void GoldenCatalog_PreservesLabelsAndStackFields()
        {
            BattleConfigSnapshot snapshot = GoldenSnapshot();

            Assert.IsTrue(snapshot.BuffCatalog.TryGetByType(9, out BuffDefinitionSnapshot stun));
            Assert.AreEqual("stun", stun.Name);
            Assert.AreEqual("晕眩", stun.Label, "type 9 标签应为 晕眩");

            Assert.IsTrue(snapshot.BuffCatalog.TryGetByType(19, out BuffDefinitionSnapshot suppression));
            Assert.AreEqual("压制", suppression.Label, "type 19 标签应为 压制");

            // 新字段显式写入：全部 Add / maxStacks=1 / conflictKey 空。
            foreach (BuffDefinitionSnapshot def in snapshot.BuffCatalog.Definitions)
            {
                Assert.AreEqual(BuffStackPolicy.Add, def.StackPolicy, "type 显式 stackPolicy 应为 Add");
                Assert.AreEqual(1, def.MaxStacks, "type 显式 maxStacks 应为 1");
                Assert.IsTrue(string.IsNullOrEmpty(def.ConflictKey), "conflictKey 应为空");
            }
        }

        // ====================================================================
        // 不可变性测试
        // ====================================================================

        [Test]
        [Description("定义构造深拷贝通道：源列表后续修改不影响快照。")]
        public void BuffDefinitionSnapshot_DeepCopiesChannels()
        {
            var sourceChannels = new List<int> { 1, 0 };
            var def = new BuffDefinitionSnapshot(
                8, "stun", "晕眩", BuffKind.State, sourceChannels,
                BuffStackPolicy.Add, 1, string.Empty);

            sourceChannels.Add(99);
            sourceChannels[0] = 42;

            Assert.AreEqual(2, def.Channels.Count, "通道应为构造时的深拷贝");
            Assert.IsTrue(def.HasChannel(0) && def.HasChannel(1), "通道应保持原值");
            Assert.IsFalse(def.HasChannel(99), "深拷贝后追加的通道不应可见");
        }

        [Test]
        [Description("目录不可变：Definitions 按 type 升序稳定排序，Types 只读，查询命中一致。")]
        public void BuffCatalogSnapshot_ImmutableAndSorted()
        {
            var catalog = new BuffCatalogSnapshot(new[]
            {
                Def(3, "moveSpeed", BuffKind.Numeric, new[] { 3 }),
                Def(0, "attPower", BuffKind.Numeric, new[] { 0 }),
                Def(1, "attSpeed", BuffKind.Numeric, new[] { 1 }),
            });

            Assert.AreEqual(3, catalog.Definitions.Count);
            Assert.AreEqual(0, catalog.Definitions[0].Type, "定义应按 type 升序");
            Assert.AreEqual(1, catalog.Definitions[1].Type);
            Assert.AreEqual(3, catalog.Definitions[2].Type);

            Assert.IsTrue(catalog.Types is IReadOnlyList<int>, "Types 应为只读列表");
            Assert.IsTrue(catalog.Definitions is IReadOnlyList<BuffDefinitionSnapshot>,
                "Definitions 应为只读列表");
            Assert.Throws<NotSupportedException>(
                () => ((IList<int>)catalog.Types)[0] = 99,
                "Types 不应允许通过 IList 写回");
            Assert.Throws<NotSupportedException>(
                () => ((IList<BuffDefinitionSnapshot>)catalog.Definitions)[0] =
                    Def(9, "illegal", BuffKind.Numeric, new[] { 0 }),
                "Definitions 不应允许通过 IList 写回");
            Assert.Throws<NotSupportedException>(
                () => ((IList<int>)catalog.Definitions[0].Channels)[0] = 99,
                "Channels 不应允许通过 IList 写回");
            Assert.IsTrue(catalog.TryGetByType(3, out BuffDefinitionSnapshot found));
            Assert.AreEqual("moveSpeed", found.Name);
            Assert.IsTrue(catalog.ContainsType(0));
            Assert.IsFalse(catalog.TryGetByType(9, out _), "未知 type 不应命中");
        }

        [Test]
        [Description("重复 type 无法构建按 type 索引目录（构造抛 ArgumentException）。")]
        public void BuffCatalogSnapshot_DuplicateType_ThrowsAtConstruction()
        {
            Assert.Throws<ArgumentException>(() => new BuffCatalogSnapshot(new[]
            {
                Def(0, "attPower", BuffKind.Numeric, new[] { 0 }),
                Def(0, "attPowerDup", BuffKind.Numeric, new[] { 0 }),
            }), "重复 type 应抛 ArgumentException");
        }

        // ====================================================================
        // Validator：缺失/未知/非法字段测试
        // ====================================================================

        [Test]
        [Description("BuffCatalog 为 null → MissingSection（启动门禁拒绝，不静默降级）。")]
        public void Validate_NullBuffCatalog_ReturnsMissingSection()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(null);
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingSection, "BuffCatalog"),
                "null 目录应报告 MissingSection（路径含 BuffCatalog）。");
        }

        [Test]
        [Description("Buff 目录为空 → MissingField。")]
        public void Validate_EmptyBuffCatalog_ReturnsMissingField()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(
                new BuffCatalogSnapshot(Array.Empty<BuffDefinitionSnapshot>()));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingField, "BuffCatalog.Definitions"),
                "空目录应报告 MissingField。");
        }

        [Test]
        [Description("未知 Kind → BuffKindUnknown，诊断包含 Buff type 与字段名。")]
        public void Validate_UnknownKind_ReturnsBuffKindUnknown()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new BuffCatalogSnapshot(new[]
            {
                Def(0, "attPower", BuffKind.Numeric, new[] { 0 }),
                BadDef(1, (BuffKind)99, BuffStackPolicy.Add, 1, new[] { 0 }),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BuffKindUnknown, "Buff.1.Kind"),
                "未知 Kind 应报告 BuffKindUnknown，路径含 Buff type 与 Kind 字段。");
        }

        [Test]
        [Description("未知 StackPolicy → BuffStackPolicyUnknown，诊断含 Buff type 与字段名。")]
        public void Validate_UnknownStackPolicy_ReturnsBuffStackPolicyUnknown()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new BuffCatalogSnapshot(new[]
            {
                Def(0, "attPower", BuffKind.Numeric, new[] { 0 }),
                BadDef(1, BuffKind.Numeric, (BuffStackPolicy)99, 1, new[] { 0 }),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BuffStackPolicyUnknown, "Buff.1.StackPolicy"),
                "未知 StackPolicy 应报告 BuffStackPolicyUnknown。");
        }

        [Test]
        [Description("未知通道值 → BuffChannelInvalid，诊断含 Buff type 与通道索引。")]
        public void Validate_UnknownChannel_ReturnsBuffChannelInvalid()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new BuffCatalogSnapshot(new[]
            {
                Def(0, "attPower", BuffKind.Numeric, new[] { 0 }),
                Def(1, "weird", BuffKind.Numeric, new[] { 42 }),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BuffChannelInvalid, "Buff.1.Channels"),
                "未知通道应报告 BuffChannelInvalid，路径含 Buff type 与 Channels。");
        }

        [Test]
        [Description("Numeric 定义缺通道 → BuffChannelMissing。")]
        public void Validate_NumericMissingChannel_ReturnsBuffChannelMissing()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new BuffCatalogSnapshot(new[]
            {
                Def(0, "attPower", BuffKind.Numeric, new[] { 0 }),
                Def(1, "emptyNumeric", BuffKind.Numeric, Array.Empty<int>()),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BuffChannelMissing, "Buff.1.Channels"),
                "Numeric 缺通道应报告 BuffChannelMissing。");
        }

        [Test]
        [Description("State 定义缺通道 → BuffChannelMissing。")]
        public void Validate_StateMissingChannel_ReturnsBuffChannelMissing()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new BuffCatalogSnapshot(new[]
            {
                Def(0, "attPower", BuffKind.Numeric, new[] { 0 }),
                Def(1, "emptyState", BuffKind.State, Array.Empty<int>()),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BuffChannelMissing, "Buff.1.Channels"),
                "State 缺通道应报告 BuffChannelMissing。");
        }

        [Test]
        [Description("Custom 允许空通道（type 7 配置为空通道，目录应保持可加载）。")]
        public void Validate_CustomEmptyChannel_Allowed()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new BuffCatalogSnapshot(new[]
            {
                Def(0, "attPower", BuffKind.Numeric, new[] { 0 }),
                Def(7, "custom", BuffKind.Custom, Array.Empty<int>()),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(HasError(result, BattleConfigErrorCategory.BuffChannelMissing),
                "Custom 空通道不应报告 BuffChannelMissing。");
        }

        [Test]
        [Description("非正 maxStacks → BuffMaxStacksInvalid，诊断含 Buff type 与字段名。")]
        public void Validate_NonPositiveMaxStacks_ReturnsBuffMaxStacksInvalid()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new BuffCatalogSnapshot(new[]
            {
                Def(0, "attPower", BuffKind.Numeric, new[] { 0 }),
                BadDef(1, BuffKind.Numeric, BuffStackPolicy.Add, 0, new[] { 0 }),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BuffMaxStacksInvalid, "Buff.1.MaxStacks"),
                "非正 maxStacks 应报告 BuffMaxStacksInvalid。");
        }

        [Test]
        [Description("多个非法 Buff 字段一次性收集，不因首个错误中止。")]
        public void Validate_MultipleBuffErrors_AllCollected()
        {
            BattleConfigSnapshot snapshot = RebuildWithCatalog(new BuffCatalogSnapshot(new[]
            {
                Def(0, "attPower", BuffKind.Numeric, new[] { 0 }),
                // 未知 Kind + 未知通道 + 非正 maxStacks 同一行全部检出。
                BadDef(1, (BuffKind)99, (BuffStackPolicy)99, -1, new[] { 42 }),
            }));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BuffKindUnknown, "Buff.1"),
                "未知 Kind 应被收集。");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BuffStackPolicyUnknown, "Buff.1"),
                "未知 StackPolicy 应被收集。");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BuffChannelInvalid, "Buff.1"),
                "未知通道应被收集。");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BuffMaxStacksInvalid, "Buff.1"),
                "非正 maxStacks 应被收集。");
        }

        // ====================================================================
        // Luban Provider 显式消费测试（task 3.3）
        // ====================================================================

        [Test]
        [Description("BuildBuffCatalogFromLuban 显式消费 TbBuff 行（Type/Name/Label/Kind/Channels/StackPolicy/MaxStacks/ConflictKey），不得按 type 推导。")]
        public void Provider_ConsumesTbBuffRows_Directly()
        {
            TbBuff tbBuff = BuildTbBuff(
                BuffRow(0, "attPower", null, 0, new[] { 0 }, "Add", 1, null),
                BuffRow(8, "stun", "晕眩", 1, new[] { 1, 0 }, "Refresh", 3, "ctrl"),
                BuffRow(7, "custom", null, 2, Array.Empty<int>(), "Replace", 2, null));

            BuffCatalogSnapshot catalog = LubanBattleConfigProvider.BuildBuffCatalogFromLuban(tbBuff);

            Assert.AreEqual(3, catalog.Definitions.Count);

            Assert.IsTrue(catalog.TryGetByType(0, out BuffDefinitionSnapshot attPower));
            Assert.AreEqual(BuffKind.Numeric, attPower.Kind);
            Assert.IsTrue(attPower.HasChannel(0));
            Assert.AreEqual("", attPower.Label, "null Label 应规范化为空串");
            Assert.AreEqual(BuffStackPolicy.Add, attPower.StackPolicy);
            Assert.AreEqual(1, attPower.MaxStacks);
            Assert.AreEqual("", attPower.ConflictKey, "null ConflictKey 应规范化为空串");

            Assert.IsTrue(catalog.TryGetByType(8, out BuffDefinitionSnapshot stun));
            Assert.AreEqual(BuffKind.State, stun.Kind);
            Assert.AreEqual("晕眩", stun.Label, "Label 应来自配置行");
            Assert.IsTrue(stun.HasChannel(1) && stun.HasChannel(0), "channels 应来自配置行");
            Assert.AreEqual(BuffStackPolicy.Refresh, stun.StackPolicy, "StackPolicy 应来自配置行");
            Assert.AreEqual(3, stun.MaxStacks, "MaxStacks 应来自配置行");
            Assert.AreEqual("ctrl", stun.ConflictKey, "ConflictKey 应来自配置行");

            Assert.IsTrue(catalog.TryGetByType(7, out BuffDefinitionSnapshot custom));
            Assert.AreEqual(BuffKind.Custom, custom.Kind);
            Assert.AreEqual(0, custom.Channels.Count);
        }

        [Test]
        [Description("未知 Kind 无法映射领域枚举 → Provider 抛 BuffKindUnknown，路径含 type。")]
        public void Provider_UnknownKind_ThrowsBuffKindUnknown()
        {
            TbBuff tbBuff = BuildTbBuff(
                BuffRow(0, "attPower", null, 0, new[] { 0 }, "Add", 1, null),
                BuffRow(1, "weird", null, 99, new[] { 0 }, "Add", 1, null));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildBuffCatalogFromLuban(tbBuff));

            Assert.AreEqual(BattleConfigErrorCategory.BuffKindUnknown, ex.Category);
            Assert.IsTrue(ex.Path.StartsWith("Buff.1", StringComparison.Ordinal),
                "路径应定位到 Buff type 与 Kind 字段。");
        }

        [Test]
        [Description("未知/空 StackPolicy → Provider 抛 BuffStackPolicyUnknown（不得在代码中推导默认策略）。")]
        public void Provider_UnknownStackPolicy_ThrowsBuffStackPolicyUnknown()
        {
            TbBuff tbBuff = BuildTbBuff(
                BuffRow(0, "attPower", null, 0, new[] { 0 }, "Add", 1, null),
                BuffRow(1, "weird", null, 0, new[] { 0 }, "Mystery", 1, null));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildBuffCatalogFromLuban(tbBuff));

            Assert.AreEqual(BattleConfigErrorCategory.BuffStackPolicyUnknown, ex.Category);
            Assert.IsTrue(ex.Path.StartsWith("Buff.1.StackPolicy", StringComparison.Ordinal),
                "路径应定位到 Buff type 与 StackPolicy 字段。");
        }

        [Test]
        [Description("生成 TbBuff 的主键字典在 Provider 前拒绝重复 type；领域目录重复校验由独立测试覆盖。")]
        public void GeneratedTbBuff_DuplicateType_IsRejectedBeforeProvider()
        {
            Assert.Throws<ArgumentException>(() => BuildTbBuff(
                BuffRow(0, "attPower", null, 0, new[] { 0 }, "Add", 1, null),
                BuffRow(0, "attPowerDup", null, 0, new[] { 0 }, "Add", 1, null)));
        }

        // ====================================================================
        // 测试辅助
        // ====================================================================

        /// <summary>获取黄金基线快照（含完整 Buff 目录）。</summary>
        private static BattleConfigSnapshot GoldenSnapshot()
        {
            return new JsonBattleConfigProvider().GetSnapshot();
        }

        /// <summary>用自定义 Buff 目录重建快照（其余子节沿用黄金基线）。</summary>
        private static BattleConfigSnapshot RebuildWithCatalog(BuffCatalogSnapshot buffCatalog)
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
                buffCatalog: buffCatalog);
        }

        /// <summary>构造合法 Buff 定义（默认 Add/1/null 冲突键）。</summary>
        private static BuffDefinitionSnapshot Def(
            int type, string name, BuffKind kind, IReadOnlyList<int> channels)
        {
            return new BuffDefinitionSnapshot(type, name, "", kind, channels,
                BuffStackPolicy.Add, 1, string.Empty);
        }

        /// <summary>构造任意字段值的 Buff 定义（用于非法变体）。</summary>
        private static BuffDefinitionSnapshot BadDef(
            int type, BuffKind kind, BuffStackPolicy stackPolicy, int maxStacks, IReadOnlyList<int> channels)
        {
            return new BuffDefinitionSnapshot(type, $"t{type}", "", kind, channels,
                stackPolicy, maxStacks, string.Empty);
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

        /// <summary>是否含 Buff 子域错误；忽略由其他 change 独立拥有的地图/波次错误。</summary>
        private static bool HasBuffError(BattleConfigValidationResult result)
        {
            for (int i = 0; i < result.Errors.Count; i++)
            {
                BattleConfigErrorCategory category = result.Errors[i].Category;
                if (category >= BattleConfigErrorCategory.BuffKindUnknown
                    && category <= BattleConfigErrorCategory.BuffMaxStacksInvalid)
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================
        // Luban Bean 构造（手写 ByteBuf，字段顺序与生成代码一致）
        // ====================================================================

        private readonly struct BuffRowData
        {
            public readonly int Type;
            public readonly string Name;
            public readonly string Label;
            public readonly int Kind;
            public readonly int[] Channels;
            public readonly string StackPolicy;
            public readonly int MaxStacks;
            public readonly string ConflictKey;

            public BuffRowData(
                int type, string name, string label, int kind, int[] channels,
                string stackPolicy, int maxStacks, string conflictKey)
            {
                Type = type;
                Name = name;
                Label = label;
                Kind = kind;
                Channels = channels;
                StackPolicy = stackPolicy;
                MaxStacks = maxStacks;
                ConflictKey = conflictKey;
            }
        }

        private static BuffRowData BuffRow(
            int type, string name, string label, int kind, int[] channels,
            string stackPolicy, int maxStacks, string conflictKey)
        {
            return new BuffRowData(type, name, label, kind, channels, stackPolicy, maxStacks, conflictKey);
        }

        private static TbBuff BuildTbBuff(params BuffRowData[] rows)
        {
            var buf = new ByteBuf();
            buf.WriteSize(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                WriteBuffRow(buf, rows[i]);
            }

            return new TbBuff(buf);
        }

        private static void WriteBuffRow(ByteBuf buf, BuffRowData row)
        {
            buf.WriteInt(row.Type);
            buf.WriteString(row.Name);
            bool hasLabel = row.Label != null;
            buf.WriteBool(hasLabel);
            if (hasLabel)
            {
                buf.WriteString(row.Label);
            }

            buf.WriteInt(row.Kind);
            buf.WriteSize(row.Channels.Length);
            for (int i = 0; i < row.Channels.Length; i++)
            {
                buf.WriteInt(row.Channels[i]);
            }

            buf.WriteString(row.StackPolicy);
            buf.WriteInt(row.MaxStacks);
            bool hasConflict = row.ConflictKey != null;
            buf.WriteBool(hasConflict);
            if (hasConflict)
            {
                buf.WriteString(row.ConflictKey);
            }
        }
    }
}
