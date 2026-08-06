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
    //   3. 缺字段（空集合）→ ConfigMissing
    //   4. 非法/空权重 → ConfigInvalid
    //   5. 未知兵种 → ConfigInvalid
    //   6. 非法时间/距离 → ConfigInvalid
    //   7. 地图尺寸非法 → ConfigInvalid
    //   8. 越界路径 → ConfigInvalid
    //   9. 缺失引用 → ConfigInvalid
    //  10. route marker 仅属表现不按游戏路径规则误判 → RouteMarkerMismatch
    //
    //   spec "Spawn weights are invalid"：
    //   波次生成权重为空、为负或无法选择有效索引时，战斗开始失败并报告具体配置位置，
    //   不创建半初始化实体。
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
            string sourceTag = "Test")
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
                sourceTag: sourceTag);
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

            Assert.IsTrue(result.IsValid, "黄金基线快照应校验通过。");
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
        // 缺字段测试（空集合）→ ConfigMissing
        // ====================================================================

        [Test]
        [Description("敌人各波次血量数组为空 → MissingField → ConfigMissing。")]
        public void Validate_EmptyHealthByWave_ReturnsMissingField()
        {
            var enemy = new EnemyConfigSnapshot(
                type: "Mob0",
                mapEnemyTypeIndex: 0,
                speed: 50,
                healthByWave: Array.Empty<int>(),
                earlyRoundHealthMultipliers: new float[] { 0.6f },
                contactDamage: 1);
            BattleConfigSnapshot snapshot = Rebuild(enemy: enemy);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(BattleErrorCode.ConfigMissing, result.ErrorCode,
                "空血量数组应映射到 ConfigMissing。");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingField, "Enemy.HealthByWave"),
                "应包含 Enemy.HealthByWave MissingField 错误。");
        }

        [Test]
        [Description("波次怪物数量数组为空 → MissingField。")]
        public void Validate_EmptyWaveUnitCounts_ReturnsMissingField()
        {
            var wave = new WaveConfigSnapshot(
                waveUnitCounts: Array.Empty<int>(),
                bossWaveNumbers: new int[] { 3 },
                bossSpawnChances: new float[] { 0.1f },
                spawnStrategyWeights: new int[] { 5, 2, 3 },
                spawnStrategies: new IReadOnlyList<float>[]
                {
                    new float[] { 1f },
                    new float[] { 1.1f },
                    new float[] { 1f },
                },
                skipBoss: true,
                delayTimeMs: 10000,
                maxRounds: 20);
            BattleConfigSnapshot snapshot = Rebuild(wave: wave);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.MissingField, "Wave.WaveUnitCounts"));
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
        // 非法/空权重测试 → ConfigInvalid
        // ====================================================================

        [Test]
        [Description("生成策略权重为空 → InvalidSpawnWeight。")]
        public void Validate_EmptySpawnWeights_ReturnsInvalidSpawnWeight()
        {
            var wave = CloneWaveWithWeights(Array.Empty<int>());
            BattleConfigSnapshot snapshot = Rebuild(wave: wave);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(BattleErrorCode.ConfigInvalid, result.ErrorCode,
                "空权重应映射到 ConfigInvalid。");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.InvalidSpawnWeight),
                "应包含 InvalidSpawnWeight 错误。");
        }

        [Test]
        [Description("生成策略权重含负值 → InvalidSpawnWeight。")]
        public void Validate_NegativeSpawnWeight_ReturnsInvalidSpawnWeight()
        {
            var wave = CloneWaveWithWeights(new int[] { 5, -2, 3 });
            BattleConfigSnapshot snapshot = Rebuild(wave: wave);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.InvalidSpawnWeight),
                "负权重应报告 InvalidSpawnWeight。");
        }

        [Test]
        [Description("生成策略权重总和为零 → InvalidSpawnWeight（无法选择有效索引）。")]
        public void Validate_ZeroSumSpawnWeights_ReturnsInvalidSpawnWeight()
        {
            var wave = CloneWaveWithWeights(new int[] { 0, 0, 0 });
            BattleConfigSnapshot snapshot = Rebuild(wave: wave);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.InvalidSpawnWeight),
                "总和为零的权重应报告 InvalidSpawnWeight。");
        }

        [Test]
        [Description("生成策略表行数与权重数不一致 → InvalidSpawnWeight。")]
        public void Validate_StrategyRowCountMismatch_ReturnsInvalidSpawnWeight()
        {
            var basis = BuildValidSnapshot();
            var wave = new WaveConfigSnapshot(
                waveUnitCounts: basis.Wave.WaveUnitCounts,
                bossWaveNumbers: basis.Wave.BossWaveNumbers,
                bossSpawnChances: basis.Wave.BossSpawnChances,
                spawnStrategyWeights: new int[] { 5, 2, 3 },
                spawnStrategies: new IReadOnlyList<float>[]
                {
                    new float[] { 1f }, // 只有 1 行，权重有 3 个
                },
                skipBoss: true,
                delayTimeMs: 10000,
                maxRounds: 20);
            BattleConfigSnapshot snapshot = Rebuild(wave: wave);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.InvalidSpawnWeight),
                "策略表行数与权重数不一致应报告 InvalidSpawnWeight。");
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

        [Test]
        [Description("波间延迟为负 → InvalidTime。")]
        public void Validate_NegativeDelayTime_ReturnsInvalidTime()
        {
            var basis = BuildValidSnapshot();
            var wave = new WaveConfigSnapshot(
                waveUnitCounts: basis.Wave.WaveUnitCounts,
                bossWaveNumbers: basis.Wave.BossWaveNumbers,
                bossSpawnChances: basis.Wave.BossSpawnChances,
                spawnStrategyWeights: basis.Wave.SpawnStrategyWeights,
                spawnStrategies: basis.Wave.SpawnStrategies,
                skipBoss: true,
                delayTimeMs: -100,
                maxRounds: 20);
            BattleConfigSnapshot snapshot = Rebuild(wave: wave);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.InvalidTime, "Wave.DelayTimeMs"),
                "负波间延迟应报告 InvalidTime。");
        }

        [Test]
        [Description("敌人速度为负 → InvalidDistance。")]
        public void Validate_NegativeEnemySpeed_ReturnsInvalidDistance()
        {
            var enemy = new EnemyConfigSnapshot(
                type: "Mob0",
                mapEnemyTypeIndex: 0,
                speed: -10,
                healthByWave: new int[] { 10, 11 },
                earlyRoundHealthMultipliers: new float[] { 0.6f },
                contactDamage: 1);
            BattleConfigSnapshot snapshot = Rebuild(enemy: enemy);

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.InvalidDistance, "Enemy.Speed"),
                "负敌人速度应报告 InvalidDistance。");
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
            // ConfigVersion=0 为占位合法值。Factory.Create 后续会访问 ConfigSystem.Tables，
            // 在 EditMode 未加载配置表时会因配置快照复制失败返回 ConfigInvalid，
            // 但这不是版本校验导致的——版本校验本身应通过。
            // 因此只断言错误码不是 ConfigVersionMismatch，不要求整体成功。
            var loadout = new BattleLoadoutDto(
                mapId: 0,
                round: 0,
                randomSeed: 0,
                configVersion: 0,
                configHash: string.Empty,
                deckPreset: BattleDeckPreset.Normal);

            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(loadout);

            Assert.AreNotEqual(BattleErrorCode.ConfigVersionMismatch, assembly.ErrorCode,
                "ConfigVersion=0 为占位合法值，不应返回 ConfigVersionMismatch。");
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
                enemy: new EnemyConfigSnapshot(
                    type: "Mob0", mapEnemyTypeIndex: 0, speed: 50,
                    healthByWave: Array.Empty<int>(),
                    earlyRoundHealthMultipliers: new float[] { 0.6f },
                    contactDamage: 1));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.AreEqual(BattleErrorCode.ConfigMissing, result.ErrorCode,
                "MissingField 应映射到 ConfigMissing。");
        }

        [Test]
        [Description("校验结果错误码映射：InvalidSpawnWeight → ConfigInvalid。")]
        public void ValidationResult_InvalidSpawnWeight_MapsToConfigInvalid()
        {
            BattleConfigSnapshot snapshot = Rebuild(
                wave: CloneWaveWithWeights(Array.Empty<int>()));

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.AreEqual(BattleErrorCode.ConfigInvalid, result.ErrorCode,
                "InvalidSpawnWeight 应映射到 ConfigInvalid。");
        }

        [Test]
        [Description("多个错误一次性收集，不因首个错误中止。")]
        public void Validate_MultipleErrors_AllCollected()
        {
            // 同时构造多个错误：空权重 + 未知兵种
            var badUnit = new UnitConfigSnapshot(
                index: 99, text: "炮", animationKey: "cannon",
                rangeCells: 1.5f, attackDamage: 3, attackIntervalSeconds: 0.8f,
                damageMode: "单体", targetPolicy: "nearest");

            BattleConfigSnapshot snapshot = Rebuild(
                wave: CloneWaveWithWeights(Array.Empty<int>()),
                units: new UnitConfigSnapshot[] { badUnit });

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(result.IsValid);
            Assert.GreaterOrEqual(result.Errors.Count, 2,
                "应收集多个错误，不因首个错误中止。");
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.InvalidSpawnWeight));
            Assert.IsTrue(HasError(result, BattleConfigErrorCategory.UnknownUnit));
        }

        [Test]
        [Description("DiagnosticMessage 仅用于日志，不作为程序化判断依据。")]
        public void ValidationResult_DiagnosticMessage_ForLogOnly()
        {
            BattleConfigSnapshot snapshot = Rebuild(
                wave: CloneWaveWithWeights(Array.Empty<int>()));
            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);

            Assert.IsFalse(string.IsNullOrEmpty(result.DiagnosticMessage),
                "失败时 DiagnosticMessage 应非空（用于日志）。");
            // 程序化判断应基于 ErrorCode，不解析 DiagnosticMessage
            Assert.AreEqual(BattleErrorCode.ConfigInvalid, result.ErrorCode);
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
        /// 克隆黄金波次配置，替换生成策略权重。
        /// </summary>
        private static WaveConfigSnapshot CloneWaveWithWeights(int[] weights)
        {
            var basis = BuildValidSnapshot();
            int count = weights.Length;
            // 策略表行数与权重数保持一致
            var strategies = new IReadOnlyList<float>[count];
            for (int i = 0; i < count; i++)
            {
                strategies[i] = new float[] { 1f };
            }

            return new WaveConfigSnapshot(
                waveUnitCounts: basis.Wave.WaveUnitCounts,
                bossWaveNumbers: basis.Wave.BossWaveNumbers,
                bossSpawnChances: basis.Wave.BossSpawnChances,
                spawnStrategyWeights: weights,
                spawnStrategies: strategies,
                skipBoss: true,
                delayTimeMs: 10000,
                maxRounds: 20);
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
    }
}
