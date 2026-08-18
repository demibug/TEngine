using System;
using System.Collections.Generic;
using GameConfig.battle;
using Luban;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Config
{
    // ============================================================================
    // 任务 2.4/2.5：LubanBattleConfigProvider 敌人目录与波次计划适配测试
    // ----------------------------------------------------------------------------
    // 验证内容：
    //   1. 敌人目录：TbEnemy × TbEnemyStats 按 enemyKey 成对关联；缺 stats / typeIndex
    //      冲突 → 结构化 BattleConfigDataException；技能召唤类（无 typeIndex）不进入目录。
    //   2. 波次计划：activePlanId 精确选择 + Order 升序；空键按地图 EnemyTypeIndex 解析；
    //      未知 planId / 未知 typeIndex / 越界 profile → 结构化错误；仅保留显式引用的 profile。
    //
    // 本测试通过手写 Luban ByteBuf 构造受控 bean，避免依赖 EditMode 无法加载的 .bytes
    // 资源（ConfigSystem 需 IResourceModule，测试程序集不引用）。
    // ============================================================================

    [TestFixture]
    internal class LubanBattleConfigProviderTests
    {
        // ====================================================================
        // 敌人目录测试（task 2.4）
        // ====================================================================

        [Test]
        [Description("TbEnemy × TbEnemyStats 成对构建四类普通敌人目录，禁止 DataList[0]/fallback。")]
        public void BuildCatalog_BuildsFourDefinitions_Success()
        {
            TbEnemy tbEnemy = BuildTbEnemy(
                new EnemyRow("Mob0", 0, "Mob0"),
                new EnemyRow("Mob1", 1, "Mob1"),
                new EnemyRow("Mob2", 2, "Mob2"),
                new EnemyRow("Mob3", 3, "Mob3"));
            TbEnemyStats tbStats = BuildTbEnemyStats(
                new StatsRow("Mob0", 50, new[] { 10, 11 }, new[] { 0.6f }, 1, 1),
                new StatsRow("Mob1", 52, new[] { 12, 13 }, new[] { 0.6f }, 1, 1),
                new StatsRow("Mob2", 54, new[] { 14, 15 }, new[] { 0.6f }, 1, 1),
                new StatsRow("Mob3", 56, new[] { 16, 17 }, new[] { 0.6f }, 1, 1));

            EnemyCatalogSnapshot catalog = LubanBattleConfigProvider.BuildCatalogFromLuban(tbEnemy, tbStats);

            Assert.AreEqual(4, catalog.Definitions.Count, "目录应有 4 个定义");
            Assert.AreEqual("Mob0", catalog.NormalKeys[0]);
            Assert.IsTrue(catalog.TryGetByTypeIndex(2, out EnemyDefinitionSnapshot mob2));
            Assert.AreEqual("Mob2", mob2.Key);
            Assert.AreEqual(54, mob2.MoveSpeed, "速度应来自 EnemyStats，而非固定值");
            Assert.AreEqual("Mob2", mob2.ResourceAddress, "资源地址应来自 enemy.xlsx");
        }

        [Test]
        [Description("普通敌人缺少 EnemyStats 行 → EnemyStatsMissing，目录与数值必须成对。")]
        public void BuildCatalog_MissingEnemyStats_ThrowsEnemyStatsMissing()
        {
            TbEnemy tbEnemy = BuildTbEnemy(new EnemyRow("Mob0", 0, "Mob0"));
            TbEnemyStats tbStats = BuildTbEnemyStats(); // 无任何数值行

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildCatalogFromLuban(tbEnemy, tbStats));

            Assert.AreEqual(BattleConfigErrorCategory.EnemyStatsMissing, ex.Category, "缺 stats 应为 EnemyStatsMissing");
            Assert.IsTrue(ex.Path.StartsWith("Enemy.Mob0", StringComparison.Ordinal), "路径应定位到 enemyKey");
        }

        [Test]
        [Description("两个普通敌人 typeIndex 冲突 → EnemyTypeIndexConflict。")]
        public void BuildCatalog_DuplicateTypeIndex_ThrowsEnemyTypeIndexConflict()
        {
            TbEnemy tbEnemy = BuildTbEnemy(
                new EnemyRow("Mob0", 0, "Mob0"),
                new EnemyRow("Mob1", 0, "Mob1")); // typeIndex 重复
            TbEnemyStats tbStats = BuildTbEnemyStats(
                new StatsRow("Mob0", 50, new[] { 10 }, new[] { 0.6f }, 1, 1),
                new StatsRow("Mob1", 50, new[] { 10 }, new[] { 0.6f }, 1, 1));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildCatalogFromLuban(tbEnemy, tbStats));

            Assert.AreEqual(BattleConfigErrorCategory.EnemyTypeIndexConflict, ex.Category, "typeIndex 冲突应为 EnemyTypeIndexConflict");
        }

        [Test]
        [Description("技能召唤类（无 typeIndex）不进入普通波目录，不因缺 stats 报错。")]
        public void BuildCatalog_IgnoresSkillSummonWithoutTypeIndex()
        {
            TbEnemy tbEnemy = BuildTbEnemy(
                new EnemyRow("Mob0", 0, "Mob0"),
                new EnemyRow("Zombie", null, "Zombie"),
                new EnemyRow("Cavalry", null, "Cavalry"));
            TbEnemyStats tbStats = BuildTbEnemyStats(
                new StatsRow("Mob0", 50, new[] { 10 }, new[] { 0.6f }, 1, 1));

            EnemyCatalogSnapshot catalog = LubanBattleConfigProvider.BuildCatalogFromLuban(tbEnemy, tbStats);

            Assert.AreEqual(1, catalog.Definitions.Count, "只有 Mob0 进入普通波目录");
            Assert.IsFalse(catalog.ContainsKey("Zombie"), "技能召唤类不应出现在普通波目录");
        }

        // ====================================================================
        // 波次计划测试（task 2.5）
        // ====================================================================

        [Test]
        [Description("activePlanId 为空 → WavePlanMissing。")]
        public void BuildPlan_MissingActivePlanId_ThrowsWavePlanMissing()
        {
            TbWave tbWave = BuildTbWave(activePlanId: "", StrategyProfile(3, 1f));
            TbWavePlan tbPlan = BuildTbWavePlan(
                new PlanRow("golden", 1, EWaveKind.Normal, "Mob0", 3, 0, ""));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildPlanFromLuban(tbWave, tbPlan, BuildTestCatalog(), mapEnemyTypeIndex: 0));

            Assert.AreEqual(BattleConfigErrorCategory.WavePlanMissing, ex.Category, "空 activePlanId 应为 WavePlanMissing");
        }

        [Test]
        [Description("activePlanId 在 TbWavePlan 中无任何行 → WavePlanMissing。")]
        public void BuildPlan_UnknownPlanId_ThrowsWavePlanMissing()
        {
            TbWave tbWave = BuildTbWave(activePlanId: "missing", StrategyProfile(3, 1f));
            TbWavePlan tbPlan = BuildTbWavePlan(
                new PlanRow("golden", 1, EWaveKind.Normal, "Mob0", 3, 0, ""));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildPlanFromLuban(tbWave, tbPlan, BuildTestCatalog(), mapEnemyTypeIndex: 0));

            Assert.AreEqual(BattleConfigErrorCategory.WavePlanMissing, ex.Category, "无对应行应为 WavePlanMissing");
        }

        [Test]
        [Description("Normal 行空 enemyKey 按地图 EnemyTypeIndex 从目录解析。")]
        public void BuildPlan_ResolvesEmptyEnemyKeyFromMapTypeIndex()
        {
            TbWave tbWave = BuildTbWave(activePlanId: "golden", StrategyProfile(3, 1f));
            TbWavePlan tbPlan = BuildTbWavePlan(
                new PlanRow("golden", 1, EWaveKind.Normal, "", 3, 0, ""));

            OrderedWavePlanSnapshot plan = LubanBattleConfigProvider.BuildPlanFromLuban(
                tbWave, tbPlan, BuildTestCatalog(), mapEnemyTypeIndex: 2);

            Assert.AreEqual(1, plan.Rows.Count, "应选中一行");
            Assert.AreEqual("Mob2", plan.Rows[0].EnemyKey, "空键应按地图索引解析为 Mob2");
        }

        [Test]
        [Description("Normal 行空 enemyKey 且地图索引无法解析 → EnemyTypeIndexUnknown。")]
        public void BuildPlan_UnknownMapTypeIndex_ThrowsEnemyTypeIndexUnknown()
        {
            TbWave tbWave = BuildTbWave(activePlanId: "golden", StrategyProfile(3, 1f));
            TbWavePlan tbPlan = BuildTbWavePlan(
                new PlanRow("golden", 1, EWaveKind.Normal, "", 3, 0, ""));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildPlanFromLuban(
                    tbWave, tbPlan, BuildTestCatalog(), mapEnemyTypeIndex: 99));

            Assert.AreEqual(BattleConfigErrorCategory.EnemyTypeIndexUnknown, ex.Category, "未知索引应为 EnemyTypeIndexUnknown");
        }

        [Test]
        [Description("Boss 行不解析 enemyKey，BossKey 按配置原样保存。")]
        public void BuildPlan_BossRow_KeepsBossKeyAndNoEnemyKey()
        {
            TbWave tbWave = BuildTbWave(activePlanId: "golden", StrategyProfile(3, 1f));
            TbWavePlan tbPlan = BuildTbWavePlan(
                new PlanRow("golden", 1, EWaveKind.Boss, "", 0, 0, "ZhangLiang"),
                new PlanRow("golden", 2, EWaveKind.Normal, "Mob0", 3, 0, ""));

            OrderedWavePlanSnapshot plan = LubanBattleConfigProvider.BuildPlanFromLuban(
                tbWave, tbPlan, BuildTestCatalog(), mapEnemyTypeIndex: 0);

            Assert.AreEqual(2, plan.Rows.Count, "应选中两行");
            Assert.AreEqual(WavePlanKind.Boss, plan.Rows[0].Kind, "EWaveKind.Boss 应映射到 WavePlanKind.Boss");
            Assert.AreEqual("ZhangLiang", plan.Rows[0].BossKey, "BossKey 应原样保存");
            Assert.IsTrue(string.IsNullOrEmpty(plan.Rows[0].EnemyKey), "Boss 行不应占用 enemyKey");
        }

        [Test]
        [Description("越界 strategyProfile 引用 → StrategyProfileInvalid，不静默 fallback。")]
        public void BuildPlan_OutOfRangeProfile_ThrowsStrategyProfileInvalid()
        {
            TbWave tbWave = BuildTbWave(activePlanId: "golden", StrategyProfile(3, 1f));
            TbWavePlan tbPlan = BuildTbWavePlan(
                new PlanRow("golden", 1, EWaveKind.Normal, "Mob0", 3, 0, "", 5));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildPlanFromLuban(tbWave, tbPlan, BuildTestCatalog(), mapEnemyTypeIndex: 0));

            Assert.AreEqual(BattleConfigErrorCategory.StrategyProfileInvalid, ex.Category, "越界 profile 应为 StrategyProfileInvalid");
        }

        [Test]
        [Description("仅保留被所选计划显式引用的策略 profile（源表原始索引）。")]
        public void BuildPlan_RetainsOnlyReferencedProfiles()
        {
            TbWave tbWave = BuildTbWave(
                activePlanId: "golden",
                StrategyProfile(3, 1f), // index 0
                StrategyProfile(3, 2f), // index 1
                StrategyProfile(3, 3f)); // index 2
            TbWavePlan tbPlan = BuildTbWavePlan(
                new PlanRow("golden", 1, EWaveKind.Normal, "Mob0", 3, 0, "", 0),
                new PlanRow("golden", 2, EWaveKind.Normal, "Mob1", 3, 0, "", 2));

            OrderedWavePlanSnapshot plan = LubanBattleConfigProvider.BuildPlanFromLuban(
                tbWave, tbPlan, BuildTestCatalog(), mapEnemyTypeIndex: 0);

            Assert.AreEqual(2, plan.ReferencedProfileIndexes.Count, "应只保留被引用的 profile");
            Assert.AreEqual(0, plan.ReferencedProfileIndexes[0]);
            Assert.AreEqual(2, plan.ReferencedProfileIndexes[1]);
            Assert.IsTrue(plan.TryGetProfile(0, out _));
            Assert.IsFalse(plan.TryGetProfile(1, out _), "未引用的 profile 1 不应保留");
            Assert.IsTrue(plan.TryGetProfile(2, out _));
        }

        [Test]
        [Description("按 activePlanId 精确过滤并按 Order 升序（构造时稳定排序）。")]
        public void BuildPlan_FiltersByPlanIdAndSortsByOrder()
        {
            TbWave tbWave = BuildTbWave(activePlanId: "golden", StrategyProfile(3, 1f));
            TbWavePlan tbPlan = BuildTbWavePlan(
                new PlanRow("other", 1, EWaveKind.Normal, "Mob0", 1, 0, ""),
                new PlanRow("golden", 3, EWaveKind.Normal, "Mob2", 1, 0, ""),
                new PlanRow("golden", 1, EWaveKind.Normal, "Mob0", 1, 0, ""),
                new PlanRow("golden", 2, EWaveKind.Normal, "Mob1", 1, 0, ""));

            OrderedWavePlanSnapshot plan = LubanBattleConfigProvider.BuildPlanFromLuban(
                tbWave, tbPlan, BuildTestCatalog(), mapEnemyTypeIndex: 0);

            Assert.AreEqual("golden", plan.ActivePlanId, "应精确选择 golden 计划");
            Assert.AreEqual(3, plan.Rows.Count, "应排除 other 计划行");
            Assert.AreEqual(1, plan.Rows[0].Order);
            Assert.AreEqual(2, plan.Rows[1].Order);
            Assert.AreEqual(3, plan.Rows[2].Order);
        }

        // ====================================================================
        // 武器目录测试（task 3.2）
        // ====================================================================

        [Test]
        [Description("BuildWeaponCatalogFromLuban 显式消费 TbWeapon 行（Id/Type/AddAttPower/Enabled/HandlerKey），不得按 id 推导或 fallback。")]
        public void BuildWeaponCatalog_ConsumesTbWeaponRows_Directly()
        {
            TbWeapon tbWeapon = BuildTbWeapon(
                WeaponRow(0, 0, "短弓", 1, "Basic", enabled: true),
                WeaponRow(10, 1, "短枪", 1, "Basic", enabled: true),
                WeaponRow(20, 2, "短刀", 1, "Basic", enabled: true),
                WeaponRow(31, 3, "短剑", 1, "Basic", enabled: true),
                WeaponRow(1, 0, "硬弓", 2, null, enabled: false));

            WeaponCatalogSnapshot catalog = LubanBattleConfigProvider.BuildWeaponCatalogFromLuban(tbWeapon);

            Assert.AreEqual(5, catalog.Definitions.Count, "目录应有 5 个定义");

            Assert.IsTrue(catalog.TryGetById(0, out WeaponDefinitionSnapshot bow));
            Assert.AreEqual(WeaponType.Bow, bow.Type, "type=0 应映射到 Bow");
            Assert.AreEqual(1, bow.AddAttackPower, "附加攻击力应来自配置行");
            Assert.IsTrue(bow.Enabled, "enabled 应来自配置行");
            Assert.AreEqual("Basic", bow.HandlerKey, "handlerKey 应来自配置行");

            Assert.IsTrue(catalog.TryGetById(10, out WeaponDefinitionSnapshot spear));
            Assert.AreEqual(WeaponType.Spear, spear.Type, "type=1 应映射到 Spear");
            Assert.IsTrue(catalog.TryGetById(20, out WeaponDefinitionSnapshot knife));
            Assert.AreEqual(WeaponType.Knife, knife.Type, "type=2 应映射到 Knife");
            Assert.IsTrue(catalog.TryGetById(31, out WeaponDefinitionSnapshot sword));
            Assert.AreEqual(WeaponType.Sword, sword.Type, "type=3 应映射到 Sword");

            Assert.IsTrue(catalog.TryGetById(1, out WeaponDefinitionSnapshot disabled));
            Assert.IsFalse(disabled.Enabled, "禁用行 enabled 应来自配置行");
            Assert.AreEqual(2, disabled.AddAttackPower, "禁用行附加攻击力应原样复制");
            Assert.IsTrue(string.IsNullOrEmpty(disabled.HandlerKey),
                "禁用行 null handlerKey 应规范化为空串，不得推导 Basic");
        }

        [Test]
        [Description("未知 Weapon Type → Provider 抛 WeaponTypeUnknown，路径含 id 与 Type 字段。")]
        public void BuildWeaponCatalog_UnknownType_ThrowsWeaponTypeUnknown()
        {
            TbWeapon tbWeapon = BuildTbWeapon(
                WeaponRow(0, 0, "短弓", 1, "Basic", enabled: true),
                WeaponRow(99, 99, "未知", 0, null, enabled: false));

            BattleConfigDataException ex = Assert.Throws<BattleConfigDataException>(
                () => LubanBattleConfigProvider.BuildWeaponCatalogFromLuban(tbWeapon));

            Assert.AreEqual(BattleConfigErrorCategory.WeaponTypeUnknown, ex.Category,
                "未知 Type 应为 WeaponTypeUnknown");
            Assert.IsTrue(ex.Path.StartsWith("Weapon.99.Type", StringComparison.Ordinal),
                "路径应定位到武器 id 与 Type 字段。");
        }

        // ====================================================================
        // 测试辅助：构造受控 Luban bean（手写 ByteBuf，字段顺序与生成代码一致）
        // ====================================================================

        private readonly struct EnemyRow
        {
            public readonly string Key;
            public readonly int? TypeIndex;
            public readonly string ResourceAddress;
            public EnemyRow(string key, int? typeIndex, string resourceAddress)
            {
                Key = key;
                TypeIndex = typeIndex;
                ResourceAddress = resourceAddress;
            }
        }

        private readonly struct StatsRow
        {
            public readonly string Key;
            public readonly int MoveSpeed;
            public readonly int[] HealthByWave;
            public readonly float[] EarlyMultipliers;
            public readonly int ContactDamage;
            public readonly int RewardGold;
            public StatsRow(string key, int moveSpeed, int[] healthByWave, float[] earlyMultipliers, int contactDamage, int rewardGold)
            {
                Key = key;
                MoveSpeed = moveSpeed;
                HealthByWave = healthByWave;
                EarlyMultipliers = earlyMultipliers;
                ContactDamage = contactDamage;
                RewardGold = rewardGold;
            }
        }

        private readonly struct PlanRow
        {
            public readonly string PlanId;
            public readonly int Order;
            public readonly EWaveKind Kind;
            public readonly string EnemyKey;
            public readonly int NormalCount;
            public readonly int DifficultyIndex;
            public readonly string BossKey;
            public readonly int StrategyProfile;
            public readonly long PreDelayMs;
            public readonly long SpawnIntervalMs;
            public readonly long PostDelayMs;
            public readonly bool PlayerLane;
            public readonly bool OpponentLane;

            public PlanRow(
                string planId,
                int order,
                EWaveKind kind,
                string enemyKey,
                int normalCount,
                int difficultyIndex,
                string bossKey,
                int strategyProfile = 0,
                long preDelayMs = 1000,
                long spawnIntervalMs = 500,
                long postDelayMs = 500,
                bool playerLane = true,
                bool opponentLane = true)
            {
                PlanId = planId;
                Order = order;
                Kind = kind;
                EnemyKey = enemyKey;
                NormalCount = normalCount;
                DifficultyIndex = difficultyIndex;
                BossKey = bossKey;
                StrategyProfile = strategyProfile;
                PreDelayMs = preDelayMs;
                SpawnIntervalMs = spawnIntervalMs;
                PostDelayMs = postDelayMs;
                PlayerLane = playerLane;
                OpponentLane = opponentLane;
            }
        }

        /// <summary>武器测试行（只填 Provider 消费的字段）。</summary>
        private readonly struct WeaponRow
        {
            public readonly int Id;
            public readonly int Type;
            public readonly string Txt;
            public readonly int AddAttPower;
            public readonly bool Enabled;
            public readonly string HandlerKey;

            public WeaponRow(int id, int type, string txt, int addAttPower, string handlerKey, bool enabled)
            {
                Id = id;
                Type = type;
                Txt = txt;
                AddAttPower = addAttPower;
                Enabled = enabled;
                HandlerKey = handlerKey;
            }
        }

        private static WeaponRow WeaponRow(int id, int type, string txt, int addAttPower, string handlerKey, bool enabled)
            => new WeaponRow(id, type, txt, addAttPower, handlerKey, enabled);

        private static float[] StrategyProfile(int length, float value)
        {
            var arr = new float[length];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = value;
            }

            return arr;
        }

        /// <summary>
        /// 构造一个含 Mob0～Mob3（typeIndex 0～3）的敌人目录。
        /// </summary>
        private static EnemyCatalogSnapshot BuildTestCatalog()
        {
            var defs = new List<EnemyDefinitionSnapshot>(4);
            for (int i = 0; i < 4; i++)
            {
                defs.Add(new EnemyDefinitionSnapshot(
                    typeIndex: i,
                    key: $"Mob{i}",
                    resourceAddress: $"Mob{i}",
                    moveSpeed: 50,
                    healthByWave: new[] { 10, 11, 12, 13, 14 },
                    earlyRoundHealthMultipliers: new[] { 0.6f },
                    contactDamage: 1,
                    rewardGold: 1));
            }

            return new EnemyCatalogSnapshot(defs);
        }

        private static TbEnemy BuildTbEnemy(params EnemyRow[] rows)
        {
            var buf = new ByteBuf();
            buf.WriteSize(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                WriteEnemyRow(buf, rows[i]);
            }

            return new TbEnemy(buf);
        }

        private static void WriteEnemyRow(ByteBuf buf, EnemyRow row)
        {
            buf.WriteString(row.Key);
            buf.WriteString(row.Key + "_symbol");
            buf.WriteBool(false); // Status = null
            buf.WriteBool(false); // Resource = null
            buf.WriteBool(false); // Deferred = null
            buf.WriteSize(0);     // LevelMultipliers 空
            buf.WriteBool(row.TypeIndex.HasValue);
            if (row.TypeIndex.HasValue)
            {
                buf.WriteInt(row.TypeIndex.Value);
            }

            buf.WriteBool(row.ResourceAddress != null);
            if (row.ResourceAddress != null)
            {
                buf.WriteString(row.ResourceAddress);
            }
        }

        private static TbEnemyStats BuildTbEnemyStats(params StatsRow[] rows)
        {
            var buf = new ByteBuf();
            buf.WriteSize(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                WriteStatsRow(buf, rows[i]);
            }

            return new TbEnemyStats(buf);
        }

        private static void WriteStatsRow(ByteBuf buf, StatsRow row)
        {
            buf.WriteString(row.Key);
            buf.WriteInt(row.MoveSpeed);
            buf.WriteSize(row.HealthByWave.Length);
            for (int i = 0; i < row.HealthByWave.Length; i++)
            {
                buf.WriteInt(row.HealthByWave[i]);
            }

            buf.WriteSize(row.EarlyMultipliers.Length);
            for (int i = 0; i < row.EarlyMultipliers.Length; i++)
            {
                buf.WriteFloat(row.EarlyMultipliers[i]);
            }

            buf.WriteInt(row.ContactDamage);
            buf.WriteInt(row.RewardGold);
        }

        private static TbWavePlan BuildTbWavePlan(params PlanRow[] rows)
        {
            var buf = new ByteBuf();
            buf.WriteSize(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                WritePlanRow(buf, rows[i]);
            }

            return new TbWavePlan(buf);
        }

        private static void WritePlanRow(ByteBuf buf, PlanRow row)
        {
            buf.WriteString(row.PlanId);
            buf.WriteInt(row.Order);
            buf.WriteInt((int)row.Kind);
            buf.WriteString(row.EnemyKey ?? string.Empty);
            buf.WriteInt(row.NormalCount);
            buf.WriteInt(row.DifficultyIndex);
            buf.WriteString(row.BossKey ?? string.Empty);
            buf.WriteLong(row.PreDelayMs);
            buf.WriteLong(row.SpawnIntervalMs);
            buf.WriteLong(row.PostDelayMs);
            buf.WriteBool(row.PlayerLane);
            buf.WriteBool(row.OpponentLane);
            buf.WriteInt(row.StrategyProfile);
        }

        private static TbWave BuildTbWave(string activePlanId, params float[][] strategies)
        {
            var buf = new ByteBuf();
            buf.WriteSize(1); // mode=one 必须恰有 1 行
            buf.WriteSize(0); // WaveUnitCounts（deprecated，空）
            buf.WriteSize(0); // BossWaveNumbers（deprecated，空）
            buf.WriteSize(0); // BossSpawnChances（deprecated，空）
            buf.WriteSize(0); // SpawnStrategyWeights（deprecated，空）
            buf.WriteSize(strategies.Length);
            for (int i = 0; i < strategies.Length; i++)
            {
                float[] strategy = strategies[i];
                buf.WriteSize(strategy.Length);
                for (int j = 0; j < strategy.Length; j++)
                {
                    buf.WriteFloat(strategy[j]);
                }
            }

            buf.WriteBool(true); // SkipBoss（deprecated）
            buf.WriteString(activePlanId);
            return new TbWave(buf);
        }

        private static TbWeapon BuildTbWeapon(params WeaponRow[] rows)
        {
            var buf = new ByteBuf();
            buf.WriteSize(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                WriteWeaponRow(buf, rows[i]);
            }

            return new TbWeapon(buf);
        }

        /// <summary>
        /// 按生成 Weapon.cs 反序列化字段顺序写入：Id/Type/Txt/Rarity/(RareTxt?)/
        /// AddAttPower/(Exclusive?)/Scale/AnchorY/Intro/FragmentNum/Enabled/HandlerKey。
        /// </summary>
        private static void WriteWeaponRow(ByteBuf buf, WeaponRow row)
        {
            buf.WriteInt(row.Id);
            buf.WriteInt(row.Type);
            buf.WriteString(row.Txt ?? string.Empty);
            buf.WriteInt(0); // Rarity
            buf.WriteBool(false); // RareTxt = null
            buf.WriteInt(row.AddAttPower);
            buf.WriteBool(false); // Exclusive = null
            buf.WriteFloat(1f); // Scale
            buf.WriteFloat(0.5f); // AnchorY
            buf.WriteString(string.Empty); // Intro
            buf.WriteInt(1); // FragmentNum
            buf.WriteBool(row.Enabled);
            bool hasHandler = row.HandlerKey != null;
            buf.WriteString(hasHandler ? row.HandlerKey : string.Empty);
        }
    }
}
