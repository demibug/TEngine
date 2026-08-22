using System;
using System.Collections.Generic;
using System.Linq;
using GameCommon.Battle;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Config
{
    // ============================================================================
    // 任务 3.5：BattleConfigValidator 单元测试
    // ----------------------------------------------------------------------------
    // 验证内容（specs/battle-config-snapshot/spec.md
    //   "Invalid configuration blocks battle entry"）：
    //   1. 合法快照校验通过
    //   2. 缺表（null 子节）→ ConfigMissing（防御性，构造函数已拒 null）
    //   3. 新权威缺字段（EnemyCatalog/OrderedWavePlan 等）→ ConfigMissing
    //   4. legacy Enemy/Wave 字段即使冲突或非法也不参与启动门禁
    //   5. 未知兵种、非法单位时间/距离、地图、路径与引用错误 → ConfigInvalid
    //
    // 结构化结果约束（决策 0.7）：
    //   校验结果为结构化（BattleConfigValidationResult），不依赖异常文本。
    //   调用方基于 ErrorCode 做程序化判断。
    // ============================================================================

    /// <summary>
    /// BattleConfigValidator 单元测试（task 3.5）。
    /// </summary>
    /// <remarks>
    /// <para>本测试使用 <see cref="JsonBattleConfigProvider"/> 产生合法黄金基线快照，
    /// 并在其基础上构造各种非法变体，验证 Validator 能检出并返回结构化错误。</para>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class BattleConfigValidatorTests
    {
        // ====================================================================
        // 合法快照构造辅助
        // ====================================================================

        /// <summary>
        /// 获取黄金基线合法快照。
        /// </summary>
        private static BattleConfigSnapshot BuildValidSnapshot()
        {
            var provider = new JsonBattleConfigProvider();
            return provider.GetSnapshot();
        }

        /// <summary>
        /// 构造一个与黄金基线等价的快照，但替换其中某子节。
        /// 用于快速构造非法变体。
        /// </summary>
        private static BattleConfigSnapshot Rebuild(
            MapData map = null,
            EnemyConfigSnapshot enemy = null,
            WaveConfigSnapshot wave = null,
            IReadOnlyList<UnitConfigSnapshot> units = null,
            UnitLevelConfigSnapshot unitLevel = null,
            EconomyConfigSnapshot economy = null,
            DeckConfigSnapshot deck = null,
            ProjectileConfigSnapshot projectile = null,
            IReadOnlyList<string> missingNotes = null,
            string sourceTag = "Test",
            EnemyCatalogSnapshot enemyCatalog = null,
            OrderedWavePlanSnapshot orderedWavePlan = null,
            BuffCatalogSnapshot buffCatalog = null,
            SkillCatalogSnapshot skillCatalog = null,
            BossCatalogSnapshot bossCatalog = null,
            WeaponCatalogSnapshot weaponCatalog = null,
            GeneralCatalogSnapshot generalCatalog = null)
        {
            var basis = new JsonBattleConfigProvider().GetSnapshot();
            return new BattleConfigSnapshot(
                map: map ?? basis.Map,
                enemy: enemy ?? basis.Enemy,
                wave: wave ?? basis.Wave,
                units: units ?? basis.Units,
                unitLevel: unitLevel ?? basis.UnitLevel,
                economy: economy ?? basis.Economy,
                deck: deck ?? basis.Deck,
                projectile: projectile ?? basis.Projectile,
                missingFieldNotes: missingNotes ?? Array.Empty<string>(),
                sourceTag: sourceTag,
                enemyCatalog: enemyCatalog ?? basis.EnemyCatalog,
                orderedWavePlan: orderedWavePlan ?? basis.OrderedWavePlan,
                buffCatalog: buffCatalog ?? basis.BuffCatalog,
                skillCatalog: skillCatalog ?? basis.SkillCatalog,
                bossCatalog: bossCatalog ?? basis.BossCatalog,
                weaponCatalog: weaponCatalog,
                generalCatalog: generalCatalog ?? basis.GeneralCatalog);
        }

        // ====================================================================
        // 合法快照测试
        // ====================================================================

        [Test]
        [Description("黄金基线合法快照校验通过，返回 IsValid=true 与 ErrorCode=None。")]
        public void Validate_GoldenSnapshot_IsValid()
        {
            BattleConfigSnapshot snapshot = BuildValidSnapshot();
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsTrue(result.IsValid, $"黄金基线快照应校验通过。{result.DiagnosticMessage}");
            Assert.AreEqual(BattleErrorCode.None, result.ErrorCode, "合法快照 ErrorCode 应为 None。");
            Assert.AreEqual(0, result.Errors.Count, "合法快照不应有错误项。");
        }

        [Test]
        [Description("校验只读不修改快照：校验后快照字段不变。")]
        public void Validate_DoesNotMutateSnapshot()
        {
            BattleConfigSnapshot snapshot = BuildValidSnapshot();
            int originalUnitCount = snapshot.Units.Count;
            int originalWaveCount = snapshot.Wave.WaveUnitCounts.Count;

            BattleConfigValidator.Validate(snapshot);

            Assert.AreEqual(originalUnitCount, snapshot.Units.Count, "Units 数量不应被修改。");
            Assert.AreEqual(originalWaveCount, snapshot.Wave.WaveUnitCounts.Count, "WaveUnitCounts 数量不应被修改。");
        }

        [Test]
        public void Validate_GeneralCatalog_RejectsMalformedRecipeAndInvalidBowProjectile()
        {
            var invalid = new GeneralConfigSnapshot(
                4, "黄忠", "黄", new[] { "黄", "黄" }, GeneralCombatArchetype.Bow,
                3.5f, 13, 0.8f, "单体", "nearest", "BowSoldier", "default",
                "MissingArrow", 0, 0);
            BattleConfigSnapshot snapshot = Rebuild(
                generalCatalog: new GeneralCatalogSnapshot(new[] { invalid }));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(BattleErrorCode.ConfigInvalid, result.ErrorCode);
            Assert.IsTrue(result.Errors.Any(e => e.Category == BattleConfigErrorCategory.GeneralConfigInvalid));
        }

        [Test]
        [Description("null 快照返回 MissingSection 错误（防御性）。")]
        public void Validate_NullSnapshot_ReturnsMissingSection()
        {
            BattleConfigValidationResult result = BattleConfigValidator.Validate(null);

            Assert.IsFalse(result.IsValid, "null 快照不应通过。");
            Assert.AreEqual(BattleErrorCode.ConfigMissing, result.ErrorCode,
                "null 快照应映射到 ConfigMissing。");
            Assert.AreEqual(BattleConfigErrorCategory.MissingSection, result.Errors[0].Category,
                "首条错误类别应为 MissingSection。");
        }

        // ====================================================================
        // 新权威缺字段与 legacy 忽略测试
        // ====================================================================

        [Test]
        [Description("冲突/非法 legacy Enemy/Wave 值不得改变结构化校验结果；新 EnemyCatalog + OrderedWavePlan 仍为唯一权威。")]
        public void Validate_ConflictingLegacyEnemyAndWaveValues_AreIgnored()
        {
            BattleConfigValidationResult baselineResult =
                BattleConfigValidator.Validate(BuildValidSnapshot());
            var legacyEnemy = new EnemyConfigSnapshot(
                type: string.Empty,
                mapEnemyTypeIndex: 99,
                speed: -10,
                healthByWave: Array.Empty<int>(),
                earlyRoundHealthMultipliers: Array.Empty<float>(),
                contactDamage: -1);
            var legacyWave = new WaveConfigSnapshot(
                waveUnitCounts: new[] { 999 },
                bossWaveNumbers: new[] { 1, 2, 3 },
                bossSpawnChances: new[] { -1f, 2f },
                spawnStrategyWeights: new[] { -5, 0 },
                spawnStrategies: Array.Empty<IReadOnlyList<float>>(),
                skipBoss: false,
                delayTimeMs: -100,
                maxRounds: -7);
            BattleConfigSnapshot snapshot = Rebuild(enemy: legacyEnemy, wave: legacyWave);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.AreEqual(baselineResult.IsValid, result.IsValid,
                "仅修改 legacy Enemy/Wave 不得改变校验通过状态。");
            Assert.AreEqual(baselineResult.ErrorCode, result.ErrorCode,
                "仅修改 legacy Enemy/Wave 不得改变结构化错误码。");
            CollectionAssert.AreEqual(
                baselineResult.Errors.Select(error => error.ToString()).ToArray(),
                result.Errors.Select(error => error.ToString()).ToArray(),
                "仅修改 legacy Enemy/Wave 不得新增、删除或改变任何校验错误。");
            Assert.AreEqual(999, snapshot.Wave.WaveUnitCounts[0]);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, snapshot.Wave.BossWaveNumbers);
            CollectionAssert.AreEqual(new[] { -1f, 2f }, snapshot.Wave.BossSpawnChances);
            CollectionAssert.AreEqual(new[] { -5, 0 }, snapshot.Wave.SpawnStrategyWeights);
            Assert.AreEqual(0, snapshot.Wave.SpawnStrategies.Count);
            Assert.IsFalse(snapshot.Wave.SkipBoss);
            Assert.AreEqual(-100, snapshot.Wave.DelayTimeMs);
            Assert.AreEqual(-7, snapshot.Wave.MaxRounds);
            Assert.AreEqual(string.Empty, snapshot.Enemy.Type);
            Assert.AreEqual(99, snapshot.Enemy.MapEnemyTypeIndex);
            Assert.AreEqual(-10, snapshot.Enemy.Speed);
            Assert.AreEqual(0, snapshot.Enemy.HealthByWave.Count);
            Assert.AreEqual(0, snapshot.Enemy.EarlyRoundHealthMultipliers.Count);
            Assert.AreEqual(-1, snapshot.Enemy.ContactDamage);
        }

        [Test]
        [Description("单位配置列表为空 → MissingField。")]
        public void Validate_EmptyUnits_ReturnsMissingField()
        {
            BattleConfigSnapshot snapshot = Rebuild(units: Array.Empty<UnitConfigSnapshot>());

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingField, "Units"));
        }

        // ====================================================================
        // 未知兵种测试 → ConfigInvalid
        // ====================================================================

        [Test]
        [Description("单位索引超出 0..3 → UnknownUnit。")]
        public void Validate_UnitIndexOutOfRange_ReturnsUnknownUnit()
        {
            var basis = BuildValidSnapshot();
            var units = new List<UnitConfigSnapshot>(basis.Units)
            {
                new UnitConfigSnapshot(
                    index: 9,
                    text: "炮",
                    animationKey: "cannon",
                    rangeCells: 3.0f,
                    attackDamage: 5,
                    attackIntervalSeconds: 1.0f,
                    damageMode: "单体",
                    targetPolicy: "nearest"),
            };
            BattleConfigSnapshot snapshot = Rebuild(units: units);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(BattleErrorCode.ConfigInvalid, result.ErrorCode);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.UnknownUnit),
                "索引 9 不在本期合法集合，应报告 UnknownUnit。");
        }

        [Test]
        [Description("单位文本不在四兵集合 → UnknownUnit。")]
        public void Validate_UnitTextNotInFourSoldiers_ReturnsUnknownUnit()
        {
            var badUnit = new UnitConfigSnapshot(
                index: 0,
                text: "炮",
                animationKey: "cannon",
                rangeCells: 3.0f,
                attackDamage: 5,
                attackIntervalSeconds: 1.0f,
                damageMode: "单体",
                targetPolicy: "nearest");
            BattleConfigSnapshot snapshot = Rebuild(units: new UnitConfigSnapshot[] { badUnit });

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.UnknownUnit),
                "文本 '炮' 不在四兵集合，应报告 UnknownUnit。");
        }

        // ====================================================================
        // 非法时间/距离测试 → ConfigInvalid
        // ====================================================================

        [Test]
        [Description("单位攻击间隔为负 → InvalidTime。")]
        public void Validate_NegativeAttackInterval_ReturnsInvalidTime()
        {
            var badUnit = new UnitConfigSnapshot(
                index: 0,
                text: "刀",
                animationKey: "knife",
                rangeCells: 1.5f,
                attackDamage: 3,
                attackIntervalSeconds: -0.5f,
                damageMode: "单体",
                targetPolicy: "nearest");
            BattleConfigSnapshot snapshot = Rebuild(units: new UnitConfigSnapshot[] { badUnit });

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.InvalidTime),
                "负攻击间隔应报告 InvalidTime。");
        }

        [Test]
        [Description("单位攻击距离为零 → InvalidDistance。")]
        public void Validate_ZeroRange_ReturnsInvalidDistance()
        {
            var badUnit = new UnitConfigSnapshot(
                index: 0,
                text: "刀",
                animationKey: "knife",
                rangeCells: 0f,
                attackDamage: 3,
                attackIntervalSeconds: 0.8f,
                damageMode: "单体",
                targetPolicy: "nearest");
            BattleConfigSnapshot snapshot = Rebuild(units: new UnitConfigSnapshot[] { badUnit });

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.InvalidDistance),
                "零攻击距离应报告 InvalidDistance。");
        }

        // ====================================================================
        // 地图尺寸测试
        // ====================================================================

        [Test]
        [Description("黄金基线地图尺寸 8×10 合法，校验通过。")]
        public void Validate_GoldenMapSize_IsValid()
        {
            BattleConfigSnapshot snapshot = BuildValidSnapshot();
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsTrue(result.IsValid, "黄金基线地图尺寸应合法。");
        }

        // 注意：MapData 构造函数已拒绝 Width/Height <= 0，因此 InvalidMapSize 防御性校验
        // 在正常路径下不会触发。此处不构造非法 MapData，因构造函数会抛异常。
        // Validator 的 InvalidMapSize 校验为防御性复查，覆盖非标准路径构造的快照。

        // ====================================================================
        // 越界路径测试 → PathOutOfBounds
        // ====================================================================

        [Test]
        [Description("路径点越界 → PathOutOfBounds。")]
        public void Validate_PathPointOutOfBounds_ReturnsPathOutOfBounds()
        {
            // 构造一个路径点越界的地图：路径包含 (100, 100) 越界点。
            // 使用 JsonBattleConfigProvider 的黄金 grid，但替换 playerPath 包含越界点。
            // MapData.FromColumnMajorGrid 接受任意路径点序列（只校验起终点），故可注入越界路径点。
            MapData map = BuildMapWithCustomPlayerPath(new GridPosition[]
            {
                new GridPosition(0, 8),
                new GridPosition(100, 100), // 越界
            });

            BattleConfigSnapshot snapshot = Rebuild(map: map);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.PathOutOfBounds),
                "越界路径点应报告 PathOutOfBounds。");
        }

        [Test]
        [Description("对手路径点越界 → PathOutOfBounds。")]
        public void Validate_OpponentPathOutOfBounds_ReturnsPathOutOfBounds()
        {
            MapData map = BuildMapWithCustomOpponentPath(new GridPosition[]
            {
                new GridPosition(7, 1),
                new GridPosition(-1, 0), // 越界
            });

            BattleConfigSnapshot snapshot = Rebuild(map: map);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.PathOutOfBounds),
                "对手越界路径点应报告 PathOutOfBounds。");
        }

        // ====================================================================
        // route marker 表现边界测试 → RouteMarkerMismatch
        // ====================================================================

        [Test]
        [Description("route marker 若仅属表现不得按游戏路径规则误判：" +
                     "路径点落在阻挡格上 → RouteMarkerMismatch，不报告 PathOutOfBounds。")]
        public void Validate_PathOnBlockedCell_ReturnsRouteMarkerMismatch()
        {
            // 构造一个路径点落在阻挡格(Kind=Blocked)上的地图。
            // 黄金 grid 中 (1,0) = "2_1" 是阻挡格。
            // 若路径点落在阻挡格上，表明表现层 route marker 被错误地作为游戏路径点传入。
            MapData map = BuildMapWithCustomPlayerPath(new GridPosition[]
            {
                new GridPosition(0, 8),
                new GridPosition(1, 0), // 阻挡格 "2_1"
            });

            BattleConfigSnapshot snapshot = Rebuild(map: map);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.RouteMarkerMismatch),
                "路径点落在阻挡格上应报告 RouteMarkerMismatch。");
            Assert.IsFalse(HasError(result, BattleConfigErrorCategory.PathOutOfBounds),
                "route marker 误判不应报告为 PathOutOfBounds。");
        }

        [Test]
        [Description("路径点落在可建造格上 → RouteMarkerMismatch（游戏路径只应经过通道格）。")]
        public void Validate_PathOnBuildableCell_ReturnsRouteMarkerMismatch()
        {
            // 黄金 grid 中 (3,1) = "1_1" 是玩家可建造格。
            MapData map = BuildMapWithCustomPlayerPath(new GridPosition[]
            {
                new GridPosition(0, 8),
                new GridPosition(3, 1), // 可建造格
            });

            BattleConfigSnapshot snapshot = Rebuild(map: map);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.RouteMarkerMismatch),
                "路径点落在可建造格上应报告 RouteMarkerMismatch。");
        }

        [Test]
        [Description("黄金基线路径全部落在通道格上 → 无 RouteMarkerMismatch 错误。")]
        public void Validate_GoldenPathOnPassage_NoRouteMarkerMismatch()
        {
            BattleConfigSnapshot snapshot = BuildValidSnapshot();
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsTrue(result.IsValid, "黄金基线路径应全部在通道格上。");
            Assert.IsFalse(HasError(result, BattleConfigErrorCategory.RouteMarkerMismatch),
                "黄金基线不应有 RouteMarkerMismatch 错误。");
        }

        // ====================================================================
        // 缺失引用测试 → MissingReference
        // ====================================================================

        [Test]
        [Description("牌组引用未定义兵种文本 → MissingReference。")]
        public void Validate_DeckReferencesUnknownSoldier_ReturnsMissingReference()
        {
            var deck = new DeckConfigSnapshot(
                minimalMode: true,
                baseSoldierTexts: new string[] { "刀", "弓", "枪", "骑", "炮" }, // "炮" 未定义
                handSize: 5,
                defaultLevel: 1,
                baseUnitCost: 1);
            BattleConfigSnapshot snapshot = Rebuild(deck: deck);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            // "炮" 同时触发 MissingReference（未在 Units 定义）和 UnknownUnit（不在四兵集合）
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingReference),
                "牌组引用未定义兵种应报告 MissingReference。");
        }

        [Test]
        [Description("投射物主要类型不在 Types 列表中 → MissingReference。")]
        public void Validate_ProjectilePrimaryTypeNotInTypes_ReturnsMissingReference()
        {
            var projectile = new ProjectileConfigSnapshot(
                types: new string[] { "Arrow" },
                primaryType: "Cannon", // 不在 Types 中
                movementStrategy: "TargetEnemyBezierMovement",
                hitStrategy: "HitEnemyStrategy");
            BattleConfigSnapshot snapshot = Rebuild(projectile: projectile);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingReference, "Projectile.PrimaryType"),
                "主要投射物类型不在 Types 列表应报告 MissingReference。");
        }

        // ====================================================================
        // 配置版本校验测试（task 3.5 "覆盖配置版本"）
        // ====================================================================
        // 版本字段位置：BattleLoadoutDto.ConfigVersion（loadout 层），权威校验在
        // BattleRuntimeFactory.TryValidateLoadout（拒绝负值 → ConfigVersionMismatch）。
        // BattleConfigValidator.ValidateVersion 在快照层做防御性 SourceTag 复查：
        // 空 SourceTag → InvalidVersion（配置来源未知，无法确定版本基线）。
        // 以下测试覆盖两条路径：快照层防御性复查与 loadout 层权威校验。

        [Test]
        [Description("黄金基线快照 SourceTag=\"Json\" 通过版本校验，无 InvalidVersion 错误。")]
        public void Validate_GoldenSourceTag_PassesVersionCheck()
        {
            BattleConfigSnapshot snapshot = BuildValidSnapshot();
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsTrue(result.IsValid, "黄金基线应通过版本校验。");
            Assert.IsFalse(HasError(result, BattleConfigErrorCategory.InvalidVersion),
                "黄金基线 SourceTag 非空，不应有 InvalidVersion 错误。");
        }

        [Test]
        [Description("空 SourceTag → InvalidVersion（快照层防御性复查：配置来源未知，无法确定版本基线）。")]
        public void Validate_EmptySourceTag_ReturnsInvalidVersion()
        {
            // 构造与黄金基线等价的快照，但 SourceTag 为空串。
            BattleConfigSnapshot snapshot = Rebuild(sourceTag: string.Empty);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid, "空 SourceTag 不应通过。");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.InvalidVersion, "SourceTag"),
                "空 SourceTag 应报告 InvalidVersion。");
        }

        [Test]
        [Description("InvalidVersion 错误映射到 ConfigVersionMismatch（决策 0.7 错误码映射）。")]
        public void Validate_InvalidVersion_MapsToConfigVersionMismatch()
        {
            BattleConfigSnapshot snapshot = Rebuild(sourceTag: string.Empty);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.AreEqual(BattleErrorCode.ConfigVersionMismatch, result.ErrorCode,
                "InvalidVersion 应映射到 ConfigVersionMismatch。");
        }

        // ====================================================================
        // loadout 层配置版本权威校验测试（BattleRuntimeFactory.TryValidateLoadout）
        // ====================================================================
        // 版本字段 ConfigVersion 在 BattleLoadoutDto 中，权威校验在 Factory loadout 步骤。
        // Factory.Create 步骤 4（loadout 校验）在步骤 5（ConfigSystem 访问）之前，
        // 负值版本在触及 ConfigSystem 前即返回 ConfigVersionMismatch，因此无需真实配置表。

        [Test]
        [Description("loadout ConfigVersion 为负 → Factory.Create 返回 ConfigVersionMismatch" +
                     "（版本字段权威校验在 loadout 层，task 3.5 覆盖配置版本）。")]
        public void Factory_NegativeConfigVersion_ReturnsConfigVersionMismatch()
        {
            // 构造负值版本的 loadout，其余字段使用最简默认。
            var loadout = new BattleLoadoutDto(
                mapId: 0,
                round: 0,
                randomSeed: 0,
                configVersion: -1,
                configHash: string.Empty,
                deckPreset: BattleDeckPreset.Normal);

            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(loadout);

            Assert.IsFalse(assembly.IsSuccess, "负值版本号不应组装成功。");
            Assert.AreEqual(BattleErrorCode.ConfigVersionMismatch, assembly.ErrorCode,
                "负值 ConfigVersion 应返回 ConfigVersionMismatch。");
        }

        [Test]
        [Description("loadout ConfigVersion=0（占位合法值）→ Factory.Create 不因版本返回失败" +
                     "（占位零值合法，本期未启用版本协商）。")]
        public void Factory_ZeroConfigVersion_NotRejectedByVersion()
        {
            // ConfigVersion=0 为占位合法值，直接验证装载参数，避免穿透到配置和资源模块。
            var loadout = new BattleLoadoutDto(
                mapId: 0,
                round: 0,
                randomSeed: 0,
                configVersion: 0,
                configHash: string.Empty,
                deckPreset: BattleDeckPreset.Normal);

            bool valid = BattleRuntimeFactory.TryValidateLoadout(
                loadout,
                out BattleErrorCode errorCode,
                out string diagnosticMessage);

            Assert.IsTrue(valid, diagnosticMessage);
            Assert.AreEqual(BattleErrorCode.None, errorCode,
                "ConfigVersion=0 为占位合法值，装载参数校验应通过。");
        }

        // ====================================================================
        // 结构化结果约束测试（决策 0.7）
        // ====================================================================

        [Test]
        [Description("校验结果为结构化：IsValid 等价于 Errors 为空。")]
        public void ValidationResult_IsValid_EquivalentToEmptyErrors()
        {
            BattleConfigSnapshot valid = BuildValidSnapshot();
            BattleConfigValidationResult validResult = BattleConfigValidator.Validate(valid);
            Assert.IsTrue(validResult.IsValid);
            Assert.AreEqual(0, validResult.Errors.Count);

            var badUnit = new UnitConfigSnapshot(
                index: 99, text: "刀", animationKey: "knife",
                rangeCells: 1.5f, attackDamage: 3, attackIntervalSeconds: 0.8f,
                damageMode: "单体", targetPolicy: "nearest");
            BattleConfigSnapshot invalid = Rebuild(units: new UnitConfigSnapshot[] { badUnit });
            BattleConfigValidationResult invalidResult = BattleConfigValidator.Validate(invalid);
            Assert.IsFalse(invalidResult.IsValid);
            Assert.Greater(invalidResult.Errors.Count, 0);
        }

        [Test]
        [Description("校验结果错误码映射：MissingField → ConfigMissing。")]
        public void ValidationResult_MissingField_MapsToConfigMissing()
        {
            BattleConfigSnapshot snapshot = Rebuild(
                enemyCatalog: new EnemyCatalogSnapshot(Array.Empty<EnemyDefinitionSnapshot>()));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.AreEqual(BattleErrorCode.ConfigMissing, result.ErrorCode,
                "MissingField 应映射到 ConfigMissing。");
        }

        [Test]
        [Description("校验结果错误码映射：InvalidSpawnWeight → ConfigInvalid。")]
        public void ValidationResult_InvalidSpawnWeight_MapsToConfigInvalid()
        {
            var result = new BattleConfigValidationResult(new[]
            {
                new BattleConfigValidationError(
                    BattleConfigErrorCategory.InvalidSpawnWeight,
                    "兼容错误类别映射测试",
                    "Legacy.Test"),
            });

            Assert.AreEqual(BattleErrorCode.ConfigInvalid, result.ErrorCode,
                "InvalidSpawnWeight 应映射到 ConfigInvalid。");
        }

        [Test]
        [Description("多个错误一次性收集，不因首个错误中止。")]
        public void Validate_MultipleErrors_AllCollected()
        {
            // 同时构造多个新权威错误：空敌人目录 + 未知兵种。
            var badUnit = new UnitConfigSnapshot(
                index: 99, text: "炮", animationKey: "cannon",
                rangeCells: 1.5f, attackDamage: 3, attackIntervalSeconds: 0.8f,
                damageMode: "单体", targetPolicy: "nearest");

            BattleConfigSnapshot snapshot = Rebuild(
                enemyCatalog: new EnemyCatalogSnapshot(Array.Empty<EnemyDefinitionSnapshot>()),
                units: new UnitConfigSnapshot[] { badUnit });

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.GreaterOrEqual(result.Errors.Count, 2,
                "应收集多个错误，不因首个错误中止。");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingField));
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.UnknownUnit));
        }

        [Test]
        [Description("DiagnosticMessage 仅用于日志，不作为程序化判断依据。")]
        public void ValidationResult_DiagnosticMessage_ForLogOnly()
        {
            var badUnit = new UnitConfigSnapshot(
                index: 99, text: "炮", animationKey: "cannon",
                rangeCells: 1.5f, attackDamage: 3, attackIntervalSeconds: 0.8f,
                damageMode: "单体", targetPolicy: "nearest");
            BattleConfigSnapshot snapshot = Rebuild(units: new[] { badUnit });
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(string.IsNullOrEmpty(result.DiagnosticMessage),
                "失败时 DiagnosticMessage 应非空（用于日志）。");
            // 程序化判断应基于 ErrorCode，不解析 DiagnosticMessage
            Assert.AreEqual(BattleErrorCode.ConfigInvalid, result.ErrorCode);
        }

        // ====================================================================
        // 敌人目录校验测试（task 2.7/2.9 / configured-enemy-spawning spec）
        // ====================================================================

        [Test]
        [Description("敌人目录为空 → MissingField。")]
        public void Validate_EmptyEnemyCatalog_ReturnsMissingField()
        {
            BattleConfigSnapshot snapshot = Rebuild(
                enemyCatalog: new EnemyCatalogSnapshot(Array.Empty<EnemyDefinitionSnapshot>()));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingField, "EnemyCatalog.Definitions"),
                "空目录应报告 MissingField。");
        }

        [Test]
        [Description("目录条目数值非法（空资源地址、非正速度、非正血量、负接触伤害/奖励）→ EnemyCatalogInvalid。")]
        public void Validate_EnemyDefinitionInvalidValues_ReturnsEnemyCatalogInvalid()
        {
            var badDef = new EnemyDefinitionSnapshot(
                typeIndex: 0,
                key: "Mob0",
                resourceAddress: "",
                moveSpeed: 0,
                healthByWave: new int[] { 0, -1 },
                earlyRoundHealthMultipliers: new float[] { 1f },
                contactDamage: -1,
                rewardGold: -1);
            BattleConfigSnapshot snapshot = Rebuild(
                enemyCatalog: new EnemyCatalogSnapshot(new[] { badDef }));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.EnemyCatalogInvalid, "EnemyCatalog.Definitions[0]"),
                "数值越界应报告 EnemyCatalogInvalid。");
        }

        [Test]
        [Description("敌人目录类型索引冲突在目录构造时即被拒绝（无法构建双索引目录）。")]
        public void EnemyCatalog_DuplicateTypeIndex_ThrowsAtConstruction()
        {
            var defs = new List<EnemyDefinitionSnapshot>
            {
                BuildDefinition(0, "Mob0"),
                BuildDefinition(0, "Mob1"), // typeIndex 重复
            };

            Assert.Throws<ArgumentException>(() => new EnemyCatalogSnapshot(defs),
                "重复 typeIndex 无法构建合法双索引目录，应抛异常。");
        }

        // ====================================================================
        // 有序波次计划校验测试（task 2.7/2.9 / ordered-wave-plan spec）
        // ====================================================================

        [Test]
        [Description("order 重复 → WaveOrderDuplicate。")]
        public void Validate_DuplicateOrder_ReturnsWaveOrderDuplicate()
        {
            OrderedWavePlanSnapshot plan = BuildPlan(NormalRow(1), NormalRow(1));
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: plan);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WaveOrderDuplicate, "WavePlan.1"),
                "重复 order 应报告 WaveOrderDuplicate。");
        }

        [Test]
        [Description("order 缺号/不连续 → WaveOrderGap。")]
        public void Validate_DiscontinuousOrder_ReturnsWaveOrderGap()
        {
            OrderedWavePlanSnapshot plan = BuildPlan(NormalRow(1), NormalRow(3));
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: plan);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WaveOrderGap, "WavePlan.3"),
                "缺号 order 应报告 WaveOrderGap。");
        }

        [Test]
        [Description("负数前置延迟 → WaveTimingInvalid。")]
        public void Validate_NegativeTiming_ReturnsWaveTimingInvalid()
        {
            var row = new WavePlanEntry(
                "golden", 1, WavePlanKind.Normal, "Mob0", 3, 0, "",
                -100, 500, 500, true, true, 0);
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: BuildPlan(row));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WaveTimingInvalid, "WavePlan.1.PreDelayMs"),
                "负前置延迟应报告 WaveTimingInvalid。");
        }

        [Test]
        [Description("两个车道都关闭 → WaveLaneInvalid。")]
        public void Validate_BothLanesOff_ReturnsWaveLaneInvalid()
        {
            var row = new WavePlanEntry(
                "golden", 1, WavePlanKind.Normal, "Mob0", 3, 0, "",
                1000, 500, 500, false, false, 0);
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: BuildPlan(row));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WaveLaneInvalid, "WavePlan.1.Lane"),
                "双车道关闭应报告 WaveLaneInvalid。");
        }

        [Test]
        [Description("Normal 行数量非正 → WaveCountInvalid。")]
        public void Validate_NonPositiveNormalCount_ReturnsWaveCountInvalid()
        {
            var row = NormalRow(1, count: 0);
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: BuildPlan(row));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WaveCountInvalid, "WavePlan.1.NormalCount"),
                "数量非正应报告 WaveCountInvalid。");
        }

        [Test]
        [Description("Boss 行缺少 bossKey → WaveBossKeyMissing。")]
        public void Validate_BossRowMissingBossKey_ReturnsWaveBossKeyMissing()
        {
            var row = BossRow(1, bossKey: "");
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: BuildPlan(row));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WaveBossKeyMissing, "WavePlan.1.BossKey"),
                "Boss 行缺 bossKey 应报告 WaveBossKeyMissing。");
        }

        [Test]
        [Description("Normal 行引用未知 enemyKey → EnemyKeyUnknown。")]
        public void Validate_UnknownRowEnemyKey_ReturnsEnemyKeyUnknown()
        {
            var row = NormalRow(1, enemyKey: "Zombie");
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: BuildPlan(row));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.EnemyKeyUnknown, "WavePlan.1.EnemyKey"),
                "未知 enemyKey 应报告 EnemyKeyUnknown。");
        }

        [Test]
        [Description("Normal 行空 enemyKey 且地图默认索引无法解析 → EnemyTypeIndexUnknown。")]
        public void Validate_UnknownMapDefaultTypeIndex_ReturnsEnemyTypeIndexUnknown()
        {
            var row = new WavePlanEntry(
                "golden", 1, WavePlanKind.Normal, "", 3, 0, "",
                1000, 500, 500, true, true, 0);
            MapData map = BuildMapWithEnemyTypeIndex(99);
            BattleConfigSnapshot snapshot = Rebuild(map: map, orderedWavePlan: BuildPlan(row));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.EnemyTypeIndexUnknown, "Map.EnemyTypeIndex"),
                "未知地图默认索引应报告 EnemyTypeIndexUnknown。");
        }

        [Test]
        [Description("难度索引越界（超出血量曲线/profile 长度）→ DifficultyIndexInvalid。")]
        public void Validate_OutOfRangeDifficulty_ReturnsDifficultyIndexInvalid()
        {
            var row = NormalRow(1, difficulty: 50);
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: BuildPlan(row));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.DifficultyIndexInvalid, "WavePlan.1.DifficultyIndex"),
                "越界难度应报告 DifficultyIndexInvalid。");
        }

        [Test]
        [Description("strategyProfile 引用无效（未被计划保留）→ StrategyProfileInvalid。")]
        public void Validate_UnknownStrategyProfile_ReturnsStrategyProfileInvalid()
        {
            var row = NormalRow(1, profile: 99);
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: BuildPlan(row));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.StrategyProfileInvalid, "WavePlan.1.StrategyProfile"),
                "未知 profile 应报告 StrategyProfileInvalid。");
        }

        [Test]
        [Description("activePlanId 为空 → WavePlanMissing。")]
        public void Validate_EmptyActivePlanId_ReturnsWavePlanMissing()
        {
            var plan = new OrderedWavePlanSnapshot(
                "", new[] { NormalRow(1) }, BuildDefaultProfiles());
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: plan);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WavePlanMissing, "OrderedWavePlan.ActivePlanId"),
                "空 activePlanId 应报告 WavePlanMissing。");
        }

        [Test]
        [Description("计划没有任何行 → WavePlanMissing。")]
        public void Validate_EmptyPlanRows_ReturnsWavePlanMissing()
        {
            var plan = new OrderedWavePlanSnapshot("golden", Array.Empty<WavePlanEntry>(), BuildDefaultProfiles());
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: plan);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WavePlanMissing, "OrderedWavePlan.Rows"),
                "无行计划应报告 WavePlanMissing。");
        }

        [Test]
        [Description("构造不可变计划：行按 order 升序稳定排序，profile 深拷贝只保留显式引用。")]
        public void OrderedWavePlanSnapshot_SortsRowsAndRetainsReferencedProfiles()
        {
            var profiles = new Dictionary<int, IReadOnlyList<float>>
            {
                [0] = MultiplierProfile(2, 1f),
                [1] = MultiplierProfile(3, 2f),
                [2] = MultiplierProfile(4, 3f),
            };
            var plan = new OrderedWavePlanSnapshot(
                "golden",
                new[] { NormalRow(3, profile: 1), NormalRow(1, profile: 0), NormalRow(2, profile: 0) },
                profiles);

            // 行按 order 升序稳定排序。
            Assert.AreEqual(1, plan.Rows[0].Order);
            Assert.AreEqual(2, plan.Rows[1].Order);
            Assert.AreEqual(3, plan.Rows[2].Order);

            // 仅保留被显式引用的 profile（0 与 1），profile 2 不被保留。
            Assert.AreEqual(2, plan.ReferencedProfileIndexes.Count);
            Assert.IsTrue(plan.TryGetProfile(0, out _));
            Assert.IsTrue(plan.TryGetProfile(1, out _));
            Assert.IsFalse(plan.TryGetProfile(2, out _));
        }

        // ====================================================================
        // 运行时能力校验（Boss 波 gate）测试（task 2.8）
        // ====================================================================

        [Test]
        [Description("所选计划含 Boss 行而默认能力不支持 → BossCapabilityUnsupported，不静默跳过。")]
        public void Validate_BossRowWithDefaultCapability_ReturnsBossCapabilityUnsupported()
        {
            OrderedWavePlanSnapshot plan = BuildPlan(BossRow(1), NormalRow(2));
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: plan);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BossCapabilityUnsupported, "WavePlan.1.Kind"),
                "默认能力不支持 Boss 波应报告 BossCapabilityUnsupported。");
        }

        [Test]
        [Description("注入支持对应 bossKey 的能力后，Boss 行计划校验通过。")]
        public void Validate_BossRowWithSupportedCapability_Passes()
        {
            OrderedWavePlanSnapshot plan = BuildPlan(BossRow(1, "ZhangLiang"), NormalRow(2));
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: plan);

            var capabilities = new BattleRuntimeCapabilities(true, new[] { "ZhangLiang" });
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot, capabilities);

            Assert.IsTrue(result.IsValid, "支持对应 bossKey 时不应拒绝 Boss 行。");
            Assert.IsFalse(HasError(result, BattleConfigErrorCategory.BossCapabilityUnsupported));
        }

        [Test]
        [Description("所选 Boss 未启用 → BossDisabled。")]
        public void Validate_SelectedDisabledBoss_ReturnsBossDisabled()
        {
            BattleConfigSnapshot snapshot = Rebuild(
                orderedWavePlan: BuildPlan(BossRow(1, "ZhangBao")));
            var capabilities = new BattleRuntimeCapabilities(true, new[] { "ZhangBao" });

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot, capabilities);

            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BossDisabled, "WavePlan.1.BossKey"));
        }

        [Test]
        [Description("所选 Boss 不在目录 → BossKeyUnknown。")]
        public void Validate_SelectedUnknownBoss_ReturnsBossKeyUnknown()
        {
            BattleConfigSnapshot snapshot = Rebuild(
                orderedWavePlan: BuildPlan(BossRow(1, "UnknownBoss")));
            var capabilities = new BattleRuntimeCapabilities(true, new[] { "UnknownBoss" });

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot, capabilities);

            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BossKeyUnknown, "WavePlan.1.BossKey"));
        }

        [Test]
        [Description("所选张梁缺少必填 skillKey → BossSkillKeyMissing。")]
        public void Validate_ZhangLiangMissingSkillKey_ReturnsBossSkillKeyMissing()
        {
            var invalidBoss = new BossDefinitionSnapshot(
                "ZhangLiang", "张梁", string.Empty, "boss0", string.Empty,
                "attackliang", "goliang", new BossTimelineSnapshot(500, 1400),
                true, 7f, 10f, 1, 10, 84.33f, 101.25f);
            BattleConfigSnapshot snapshot = Rebuild(
                orderedWavePlan: BuildPlan(BossRow(1)),
                bossCatalog: new BossCatalogSnapshot(new[] { invalidBoss }));

            BattleConfigValidationResult result =
                BattleConfigValidator.Validate(snapshot, BattleRuntimeCapabilities.Production);

            Assert.IsTrue(HasError(result,
                BattleConfigErrorCategory.BossSkillKeyMissing,
                "Boss.ZhangLiang.SkillKey"));
        }

        [Test]
        [Description("张梁依赖的 SoulCapture 缺失 → BossSkillDefinitionMissing。")]
        public void Validate_ZhangLiangMissingSkill_ReturnsBossSkillDefinitionMissing()
        {
            BattleConfigSnapshot basis = BuildValidSnapshot();
            var withoutSoulCapture = new SkillCatalogSnapshot(
                basis.SkillCatalog.Definitions
                    .Where(definition => definition.Key != "SoulCapture")
                    .ToArray());
            BattleConfigSnapshot snapshot = Rebuild(
                orderedWavePlan: BuildPlan(BossRow(1)),
                skillCatalog: withoutSoulCapture);

            BattleConfigValidationResult result =
                BattleConfigValidator.Validate(snapshot, BattleRuntimeCapabilities.Production);

            Assert.IsTrue(HasError(result,
                BattleConfigErrorCategory.BossSkillDefinitionMissing,
                "Boss.ZhangLiang.SkillKey"));
        }

        [Test]
        [Description("SoulCapture handlerKey 不是生产实现 → BossSkillHandlerMissing。")]
        public void Validate_ZhangLiangWrongHandler_ReturnsBossSkillHandlerMissing()
        {
            var invalidSkill = new SkillDefinitionSnapshot(
                "SoulCapture", SkillCategory.Boss, 8000, "UnknownHandler", 13, 2000, 2f);
            BattleConfigSnapshot snapshot = Rebuild(
                orderedWavePlan: BuildPlan(BossRow(1)),
                skillCatalog: ReplaceSoulCapture(invalidSkill));

            BattleConfigValidationResult result =
                BattleConfigValidator.Validate(snapshot, BattleRuntimeCapabilities.Production);

            Assert.IsTrue(HasError(result,
                BattleConfigErrorCategory.BossSkillHandlerMissing,
                "Skill.SoulCapture.HandlerKey"));
        }

        [Test]
        [Description("SoulCapture 引用的 Buff13 缺失 → BossEffectBuffMissing。")]
        public void Validate_ZhangLiangMissingBuff13_ReturnsBossEffectBuffMissing()
        {
            BattleConfigSnapshot basis = BuildValidSnapshot();
            var withoutBuff13 = new BuffCatalogSnapshot(
                basis.BuffCatalog.Definitions
                    .Where(definition => definition.Type != 13)
                    .ToArray());
            BattleConfigSnapshot snapshot = Rebuild(
                orderedWavePlan: BuildPlan(BossRow(1)),
                buffCatalog: withoutBuff13);

            BattleConfigValidationResult result =
                BattleConfigValidator.Validate(snapshot, BattleRuntimeCapabilities.Production);

            Assert.IsTrue(HasError(result,
                BattleConfigErrorCategory.BossEffectBuffMissing,
                "Skill.SoulCapture.EffectBuffType"));
        }

        [Test]
        [Description("张梁 timeline 不是 500/1400 → BossTimelineInvalid。")]
        public void Validate_ZhangLiangWrongTimeline_ReturnsBossTimelineInvalid()
        {
            var invalidBoss = new BossDefinitionSnapshot(
                "ZhangLiang", "张梁", "SoulCapture", "boss0", string.Empty,
                "attackliang", "goliang", new BossTimelineSnapshot(501, 1400),
                true, 7f, 10f, 1, 10, 84.33f, 101.25f);
            BattleConfigSnapshot snapshot = Rebuild(
                orderedWavePlan: BuildPlan(BossRow(1)),
                bossCatalog: new BossCatalogSnapshot(new[] { invalidBoss }));

            BattleConfigValidationResult result =
                BattleConfigValidator.Validate(snapshot, BattleRuntimeCapabilities.Production);

            Assert.IsTrue(HasError(result,
                BattleConfigErrorCategory.BossTimelineInvalid,
                "Boss.ZhangLiang.Timeline"));
        }

        [Test]
        [Description("能力可用但 bossKey 未注册 → BossCapabilityUnsupported（指向 bossKey）。")]
        public void Validate_BossRowWithUnregisteredKey_ReturnsBossCapabilityUnsupported()
        {
            OrderedWavePlanSnapshot plan = BuildPlan(BossRow(1, "ZhangLiang"), NormalRow(2));
            BattleConfigSnapshot snapshot = Rebuild(orderedWavePlan: plan);

            var capabilities = new BattleRuntimeCapabilities(true, new[] { "SomeOtherBoss" });
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot, capabilities);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.BossCapabilityUnsupported, "WavePlan.1.BossKey"),
                "未注册 bossKey 应报告 BossCapabilityUnsupported。");
        }

        [Test]
        [Description("纯 Normal 计划在显式无 Boss 能力下校验通过。")]
        public void Validate_NormalOnlyPlanWithDefaultCapability_Passes()
        {
            BattleConfigSnapshot snapshot = BuildValidSnapshot();
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsTrue(result.IsValid, "纯 Normal 计划在默认能力下应校验通过。");
            Assert.IsFalse(HasError(result, BattleConfigErrorCategory.BossCapabilityUnsupported));
        }

        // ====================================================================
        // 9e. 武器目录校验（spec "Exactly four basic weapon definitions are enabled"）
        // ====================================================================

        [Test]
        [Description("武器目录为 null 不进入启动门禁（非必选节，旧兼容快照合法）。")]
        public void Validate_WeaponCatalog_Null_IsNotAnError()
        {
            BattleConfigSnapshot snapshot = Rebuild(weaponCatalog: null);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsTrue(result.IsValid, "null WeaponCatalog 不应产生武器校验错误");
            Assert.IsFalse(HasError(result, BattleConfigErrorCategory.MissingField, "WeaponCatalog"));
            Assert.IsFalse(HasError(result, BattleConfigErrorCategory.WeaponEnabledSetInvalid));
        }

        [Test]
        [Description("合法武器目录（四 Basic +1 启用、其余 disabled）校验通过。")]
        public void Validate_WeaponCatalog_ValidEnabledSet_Passes()
        {
            BattleConfigSnapshot snapshot = Rebuild(weaponCatalog: WeaponCatalog(
                WeaponDef(0, WeaponType.Bow, 1, true, "Basic"),
                WeaponDef(1, WeaponType.Bow, 2, false, null),
                WeaponDef(10, WeaponType.Spear, 1, true, "Basic"),
                WeaponDef(20, WeaponType.Knife, 1, true, "Basic"),
                WeaponDef(31, WeaponType.Sword, 1, true, "Basic")));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsTrue(result.IsValid, $"合法武器目录应通过。{result.DiagnosticMessage}");
            Assert.IsFalse(HasError(result, BattleConfigErrorCategory.WeaponEnabledSetInvalid));
        }

        [Test]
        [Description("空武器目录报告 MissingField（WeaponCatalog.Definitions）。")]
        public void Validate_WeaponCatalog_Empty_ReportsMissingField()
        {
            BattleConfigSnapshot snapshot = Rebuild(
                weaponCatalog: new WeaponCatalogSnapshot(Array.Empty<WeaponDefinitionSnapshot>()));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid, "空武器目录应校验失败");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingField, "WeaponCatalog"),
                "空目录应报告 MissingField");
        }

        [Test]
        [Description("未知武器类型（非 0/1/2/3）报告 WeaponTypeUnknown，路径含 Weapon.{id}.Type。")]
        public void Validate_WeaponCatalog_UnknownType_ReportsWeaponTypeUnknown()
        {
            BattleConfigSnapshot snapshot = Rebuild(weaponCatalog: WeaponCatalog(
                WeaponDef(0, WeaponType.Bow, 1, true, "Basic"),
                WeaponDef(99, (WeaponType)99, 0, false, null),
                WeaponDef(10, WeaponType.Spear, 1, true, "Basic"),
                WeaponDef(20, WeaponType.Knife, 1, true, "Basic"),
                WeaponDef(31, WeaponType.Sword, 1, true, "Basic")));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid, "未知武器类型应校验失败");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WeaponTypeUnknown, "Weapon.99.Type"),
                "未知类型应报告 WeaponTypeUnknown 且路径定位到 Weapon.99.Type");
        }

        [Test]
        [Description("负附加攻击力报告 WeaponConfigInvalid，路径含 Weapon.{id}.AddAttackPower。")]
        public void Validate_WeaponCatalog_NegativeAttackPower_ReportsWeaponConfigInvalid()
        {
            BattleConfigSnapshot snapshot = Rebuild(weaponCatalog: WeaponCatalog(
                WeaponDef(0, WeaponType.Bow, 1, true, "Basic"),
                WeaponDef(5, WeaponType.Bow, -1, false, null),
                WeaponDef(10, WeaponType.Spear, 1, true, "Basic"),
                WeaponDef(20, WeaponType.Knife, 1, true, "Basic"),
                WeaponDef(31, WeaponType.Sword, 1, true, "Basic")));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid, "负附加攻击力应校验失败");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WeaponConfigInvalid, "Weapon.5.AddAttackPower"),
                "负附加攻击力应报告 WeaponConfigInvalid 且路径定位到 Weapon.5.AddAttackPower");
        }

        [Test]
        [Description("启用集合缺少 id（如 0 改为 disabled）报告 WeaponEnabledSetInvalid（缺类别/缺行）。")]
        public void Validate_WeaponCatalog_MissingEnabledId_ReportsWeaponEnabledSetInvalid()
        {
            BattleConfigSnapshot snapshot = Rebuild(weaponCatalog: WeaponCatalog(
                WeaponDef(0, WeaponType.Bow, 1, false, null),
                WeaponDef(10, WeaponType.Spear, 1, true, "Basic"),
                WeaponDef(20, WeaponType.Knife, 1, true, "Basic"),
                WeaponDef(31, WeaponType.Sword, 1, true, "Basic")));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid, "缺少启用行应校验失败");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WeaponEnabledSetInvalid, "Weapon.0.Enabled"),
                "缺少 id=0 启用行应报告 WeaponEnabledSetInvalid");
        }

        [Test]
        [Description("额外启用非基础行（如 id=1）报告 WeaponEnabledSetInvalid（误启用特殊行）。")]
        public void Validate_WeaponCatalog_ExtraEnabledId_ReportsWeaponEnabledSetInvalid()
        {
            BattleConfigSnapshot snapshot = Rebuild(weaponCatalog: WeaponCatalog(
                WeaponDef(0, WeaponType.Bow, 1, true, "Basic"),
                WeaponDef(1, WeaponType.Bow, 2, true, null),
                WeaponDef(10, WeaponType.Spear, 1, true, "Basic"),
                WeaponDef(20, WeaponType.Knife, 1, true, "Basic"),
                WeaponDef(31, WeaponType.Sword, 1, true, "Basic")));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid, "误启用特殊行应校验失败");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WeaponEnabledSetInvalid, "Weapon.1.Enabled"),
                "额外启用 id=1 应报告 WeaponEnabledSetInvalid");
        }

        [Test]
        [Description("启用行 handlerKey 缺失/非 Basic 报告 WeaponEnabledSetInvalid（缺字段，不 fallback）。")]
        public void Validate_WeaponCatalog_EnabledRow_EmptyHandlerKey_ReportsWeaponEnabledSetInvalid()
        {
            BattleConfigSnapshot snapshot = Rebuild(weaponCatalog: WeaponCatalog(
                WeaponDef(0, WeaponType.Bow, 1, true, string.Empty),
                WeaponDef(10, WeaponType.Spear, 1, true, "Basic"),
                WeaponDef(20, WeaponType.Knife, 1, true, "Basic"),
                WeaponDef(31, WeaponType.Sword, 1, true, "Basic")));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid, "启用行 handlerKey 为空应校验失败");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WeaponEnabledSetInvalid, "Weapon.0.HandlerKey"),
                "空 handlerKey 应报告 WeaponEnabledSetInvalid 且路径定位到 Weapon.0.HandlerKey");
        }

        [Test]
        [Description("启用行类别与 id 期望不符（id=0 填 Knife）报告 WeaponEnabledSetInvalid（缺类别映射）。")]
        public void Validate_WeaponCatalog_EnabledRow_WrongTypeForId_ReportsWeaponEnabledSetInvalid()
        {
            BattleConfigSnapshot snapshot = Rebuild(weaponCatalog: WeaponCatalog(
                WeaponDef(0, WeaponType.Knife, 1, true, "Basic"),
                WeaponDef(10, WeaponType.Spear, 1, true, "Basic"),
                WeaponDef(20, WeaponType.Knife, 1, true, "Basic"),
                WeaponDef(31, WeaponType.Sword, 1, true, "Basic")));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid, "启用行类别错误应校验失败");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WeaponEnabledSetInvalid, "Weapon.0.Type"),
                "id=0 填 Knife 应报告 WeaponEnabledSetInvalid 且路径定位到 Weapon.0.Type");
        }

        [Test]
        [Description("启用行附加攻击力非 1 报告 WeaponEnabledSetInvalid（Basic +1 契约）。")]
        public void Validate_WeaponCatalog_EnabledRow_WrongAttackPower_ReportsWeaponEnabledSetInvalid()
        {
            BattleConfigSnapshot snapshot = Rebuild(weaponCatalog: WeaponCatalog(
                WeaponDef(0, WeaponType.Bow, 2, true, "Basic"),
                WeaponDef(10, WeaponType.Spear, 1, true, "Basic"),
                WeaponDef(20, WeaponType.Knife, 1, true, "Basic"),
                WeaponDef(31, WeaponType.Sword, 1, true, "Basic")));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid, "启用行附加攻击力非 1 应校验失败");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WeaponEnabledSetInvalid, "Weapon.0.AddAttackPower"),
                "id=0 附加攻击力=2 应报告 WeaponEnabledSetInvalid 且路径定位到 Weapon.0.AddAttackPower");
        }

        [Test]
        [Description("多个武器问题一次性收集（未知类型 + 缺启用行 + 空目录之外的多类错误）。")]
        public void Validate_WeaponCatalog_MultipleIssues_CollectedTogether()
        {
            BattleConfigSnapshot snapshot = Rebuild(weaponCatalog: WeaponCatalog(
                WeaponDef(0, WeaponType.Bow, 1, false, null),
                WeaponDef(5, (WeaponType)99, -2, false, null),
                WeaponDef(10, WeaponType.Spear, 1, true, "Basic"),
                WeaponDef(20, WeaponType.Knife, 1, true, "Basic"),
                WeaponDef(31, WeaponType.Sword, 1, true, "Basic")));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid, "含多类武器问题应校验失败");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WeaponTypeUnknown, "Weapon.5.Type"),
                "未知类型错误应被收集");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WeaponConfigInvalid, "Weapon.5.AddAttackPower"),
                "负附加攻击力错误应被收集");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.WeaponEnabledSetInvalid, "Weapon.0.Enabled"),
                "缺少启用行错误应被收集");
        }

        // ====================================================================
        // 辅助方法
        // ====================================================================

        /// <summary>
        /// 判断结果中是否包含指定类别与可选路径的错误项。
        /// </summary>
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

        /// <summary>
        /// 使用黄金 grid 构造地图，替换玩家路径。
        /// </summary>
        private static MapData BuildMapWithCustomPlayerPath(IReadOnlyList<GridPosition> playerPath)
        {
            // 复用 JsonBattleConfigProvider 的黄金 grid
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot basis = provider.GetSnapshot();
            MapData basisMap = basis.Map;

            // 重新构造 MapData，使用相同 grid 但替换路径
            // MapData 构造为 internal，测试程序集经 InternalsVisibleTo 可访问
            var grid = new IReadOnlyList<string>[]
            {
                new string[] { "0_1", "0_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0" },
                new string[] { "2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0" },
                new string[] { "2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "1_0", "1_0", "2_0" },
                new string[] { "2_1", "1_1", "1_1", "0_1", "0_1", "0_1", "0_0", "1_0", "1_0", "2_0" },
                new string[] { "2_1", "1_1", "1_1", "0_1", "0_0", "0_0", "0_0", "1_0", "1_0", "2_0" },
                new string[] { "2_1", "1_1", "1_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0" },
                new string[] { "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0" },
                new string[] { "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "0_0", "0_0" },
            };

            return BattleConfigNormalizer.NormalizeMap(
                grid,
                mapIndex: 0,
                playerStart: new GridPosition(0, 8),
                playerEnd: new GridPosition(7, 9),
                opponentStart: new GridPosition(7, 1),
                opponentEnd: new GridPosition(0, 0),
                playerPath: playerPath,
                opponentPath: basisMap.GetOpponentPath());
        }

        /// <summary>
        /// 使用黄金 grid 构造地图，替换对手路径。
        /// </summary>
        private static MapData BuildMapWithCustomOpponentPath(IReadOnlyList<GridPosition> opponentPath)
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot basis = provider.GetSnapshot();
            MapData basisMap = basis.Map;

            var grid = new IReadOnlyList<string>[]
            {
                new string[] { "0_1", "0_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0" },
                new string[] { "2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0" },
                new string[] { "2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "1_0", "1_0", "2_0" },
                new string[] { "2_1", "1_1", "1_1", "0_1", "0_1", "0_1", "0_0", "1_0", "1_0", "2_0" },
                new string[] { "2_1", "1_1", "1_1", "0_1", "0_0", "0_0", "0_0", "1_0", "1_0", "2_0" },
                new string[] { "2_1", "1_1", "1_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0" },
                new string[] { "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0" },
                new string[] { "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "0_0", "0_0" },
            };

            return BattleConfigNormalizer.NormalizeMap(
                grid,
                mapIndex: 0,
                playerStart: new GridPosition(0, 8),
                playerEnd: new GridPosition(7, 9),
                opponentStart: new GridPosition(7, 1),
                opponentEnd: new GridPosition(0, 0),
                playerPath: basisMap.GetPlayerPath(),
                opponentPath: opponentPath);
        }

        /// <summary>
        /// 使用黄金 grid 与路径构造地图，替换敌人类型索引。
        /// </summary>
        private static MapData BuildMapWithEnemyTypeIndex(int enemyTypeIndex)
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot basis = provider.GetSnapshot();
            MapData basisMap = basis.Map;

            var grid = new IReadOnlyList<string>[]
            {
                new string[] { "0_1", "0_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0" },
                new string[] { "2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0" },
                new string[] { "2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "1_0", "1_0", "2_0" },
                new string[] { "2_1", "1_1", "1_1", "0_1", "0_1", "0_1", "0_0", "1_0", "1_0", "2_0" },
                new string[] { "2_1", "1_1", "1_1", "0_1", "0_0", "0_0", "0_0", "1_0", "1_0", "2_0" },
                new string[] { "2_1", "1_1", "1_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0" },
                new string[] { "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0" },
                new string[] { "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "0_0", "0_0" },
            };

            return BattleConfigNormalizer.NormalizeMap(
                grid,
                mapIndex: 0,
                playerStart: new GridPosition(0, 8),
                playerEnd: new GridPosition(7, 9),
                opponentStart: new GridPosition(7, 1),
                opponentEnd: new GridPosition(0, 0),
                playerPath: basisMap.GetPlayerPath(),
                opponentPath: basisMap.GetOpponentPath(),
                name: basisMap.Name,
                resourceAddress: basisMap.ResourceAddress,
                cellWidth: basisMap.CellWidth,
                cellHeight: basisMap.CellHeight,
                playerEntry: basisMap.PlayerEntry,
                opponentEntry: basisMap.OpponentEntry,
                routeMarkers: basisMap.RouteMarkers,
                enemyTypeIndex: enemyTypeIndex);
        }

        // ====================================================================
        // 敌人目录 / 有序波次计划测试辅助
        // ====================================================================

        /// <summary>
        /// 构造一个合法敌人定义（typeIndex 0～3 对应 Mob0～Mob3）。
        /// </summary>
        private static EnemyDefinitionSnapshot BuildDefinition(int typeIndex, string key)
        {
            return new EnemyDefinitionSnapshot(
                typeIndex: typeIndex,
                key: key,
                resourceAddress: key,
                moveSpeed: 50,
                healthByWave: new int[] { 10, 11, 12, 13, 14 },
                earlyRoundHealthMultipliers: new float[] { 0.6f },
                contactDamage: 1,
                rewardGold: 1);
        }

        /// <summary>
        /// 构造默认 profile 字典：仅保留 profile 0（20 项乘数 1.0）。
        /// </summary>
        private static Dictionary<int, IReadOnlyList<float>> BuildDefaultProfiles()
        {
            return new Dictionary<int, IReadOnlyList<float>>
            {
                [0] = MultiplierProfile(20, 1f),
            };
        }

        /// <summary>
        /// 构造指定长度与取值的策略乘数数组。
        /// </summary>
        private static float[] MultiplierProfile(int length, float value)
        {
            var arr = new float[length];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = value;
            }

            return arr;
        }

        /// <summary>
        /// 用给定行构造有序波次计划（默认保留 profile 0）。
        /// </summary>
        private static OrderedWavePlanSnapshot BuildPlan(params WavePlanEntry[] rows)
        {
            return new OrderedWavePlanSnapshot("golden", rows, BuildDefaultProfiles());
        }

        /// <summary>
        /// 构造一条合法的 Normal 行。
        /// </summary>
        private static WavePlanEntry NormalRow(
            int order,
            string enemyKey = "Mob0",
            int count = 3,
            int difficulty = 0,
            int profile = 0,
            bool player = true,
            bool opponent = true,
            long preDelayMs = 1000,
            long spawnIntervalMs = 500,
            long postDelayMs = 500)
        {
            return new WavePlanEntry(
                "golden", order, WavePlanKind.Normal, enemyKey, count, difficulty, "",
                preDelayMs, spawnIntervalMs, postDelayMs, player, opponent, profile);
        }

        /// <summary>
        /// 构造一条 Boss 行。
        /// </summary>
        private static WavePlanEntry BossRow(int order, string bossKey = "ZhangLiang")
        {
            return new WavePlanEntry(
                "golden", order, WavePlanKind.Boss, "", 0, 0, bossKey,
                1000, 500, 500, true, false, 0);
        }

        /// <summary>以指定定义替换 golden SoulCapture，保留其余 Skill 行。</summary>
        private static SkillCatalogSnapshot ReplaceSoulCapture(SkillDefinitionSnapshot replacement)
        {
            BattleConfigSnapshot basis = BuildValidSnapshot();
            return new SkillCatalogSnapshot(
                basis.SkillCatalog.Definitions
                    .Where(definition => definition.Key != "SoulCapture")
                    .Concat(new[] { replacement })
                    .ToArray());
        }

        /// <summary>构造武器目录。</summary>
        private static WeaponCatalogSnapshot WeaponCatalog(params WeaponDefinitionSnapshot[] definitions)
        {
            return new WeaponCatalogSnapshot(definitions);
        }

        /// <summary>构造单条武器定义。</summary>
        private static WeaponDefinitionSnapshot WeaponDef(
            int id, WeaponType type, int addAttackPower, bool enabled, string handlerKey)
        {
            return new WeaponDefinitionSnapshot(id, type, addAttackPower, enabled, handlerKey);
        }
    }
}
