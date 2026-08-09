using UnityEngine;

namespace GameBattle.Tests.EditMode.Golden
{
    /// <summary>
    /// 黄金输入/输出 fixture（OpenSpec change port-minimal-battle-to-gamebattle task 1.3）。
    /// <para>
    /// 为 C# 对照测试提供与 JS 还原工程一致的五类黄金数据：最简配置、随机序列、外部帧时间序列、
    /// 输入命令序列、关键轨迹黄金输出。数据从还原工程 <c>Origin/reconstructed-project</c> 只读导出，
    /// 禁止修改还原工程来迁就 C# 结果（决策 0.6 / 0.8 / 0.9）。
    /// </para>
    /// <para>
    /// 设计取舍：本类不引入 Newtonsoft.Json 等外部解析依赖（GameBattle.Tests.asmdef 未引用，
    /// 且 task 1.12 产物禁止改动）。黄金数据以强类型 C# 常量/只读集合直接固化，值与同目录
    /// <c>golden-battle-bundle.json</c> 逐字段一致；JSON 文件作为可读 canonical 清单与 hash 凭证保留，
    /// 由 task 8.2 对照工具在需要时独立解析。这样测试消费黄金数据无需 JSON 解析器。
    /// </para>
    /// </summary>
    public static class GoldenBattleFixtures
    {
        // ────────────────────────── Provenance（来源版本与 hash） ──────────────────────────

        /// <summary>还原工程 git commit（TEngine 仓库）。</summary>
        public const string OriginGitCommit = "9b58448e6e0faf09dae19d4349efc31528c92a78";

        /// <summary>还原工程根（相对 TEngine 仓库根）。</summary>
        public const string ReconstructedProjectRoot = "Origin/reconstructed-project";

        /// <summary>黄金 bundle JSON 文件名（与本文件同目录）。</summary>
        public const string BundleFileName = "golden-battle-bundle.json";

        /// <summary>黄金 bundle JSON 内容 SHA-256（由 README manifest 记录的最终值）。</summary>
        public const string BundleSha256 = "9c7063dbfd0a803553d3c38296d9d62ffa4ae925c57fb1c3d6cd583630994f11";

        // 关键来源源文件 SHA-256（只读导出基线，供 hash 校验）
        public static class SourceHashes
        {
            public const string MapsJson = "c3a7056ae9604c8778d74c524c44e9681950fdb19696b65788e7f8ba9241b6db";
            public const string WavesJson = "cda521667ba18edff4ac24dc9a6d78ea7b5f436c6be2f2a3933e79016a0662f3";
            public const string UnitsJson = "4289ab60fdba28f9a2884e6be04085ed83e543307a0d4518ff7e39a03c327a1d";
            public const string EnemiesJson = "051fceb8a1bba384fae7bde56e9538dfa5f39fa8de45edc8b83790242ce932d7";
            public const string DeckPoolJson = "9fe6d16148fb082d903e5b44a32e36ad550643c02eb6d43789bace171ca321e9";
            public const string ProjectilesJson = "89f83e82f03ed5532024ab5d609e12f7174b05ea00df305301b759283dfd2e3c";
            public const string BattleEconomyJson = "009bbd4e88a65569195223651a1f93d4ca833c6958254103f3b92a78ba2d71cf";
            public const string GameLoopJs = "a95cf0388dcccc15e16b7ac1120071e5a4e4f90e78876d4b8e97bc79a7c0a92e";
            public const string MathRandomJs = "6fc2e0c74288dd5e29cac774a046b8910d1ded230a74e3a91121a541efb0343b";
            public const string BattleDataCoreJs = "2f58f32aabe3812d2d14e0a7e29b98b437441e164fb6c57c51e5f219dc8ab9fc";
            public const string BattleStateJs = "b3e546e6cd97ba842073f4e2c69ed76b8757d5c289ad9406f0349f1d7c4013a6";
            public const string BattleEconomyJs = "a30b225054e39e8b8cd639d8cc32be7f43fd86549c97f0a7c4b51ae3aa5dff9d";
            public const string DeckDefinitionsJs = "d5f13c9ae13f43800d47e6352656a9ffbea974ed5bd61532c2117976ca8e2626";
            public const string MinimalBattleBootstrapJs = "d4e1f24c86d7d014824c3097430277abf427cda8e4d0c57a01d0bed0d8036c58";
            public const string BattleInputCommandJs = "d2d29d903533926ce7ce9478cd91b1777711502dad8d19ca1a08f540061278bd";
            public const string BattleInputControllerJs = "94a895e352a91e8f7dd1443daef00be32d63b0d5467ee659b06f027ed24ddcf8";
            public const string MinimalBattleLoopTestJs = "f778994d53188a9b7b42f0a15667a5917d7e9d41194a7bf82c5e74dd9edb0b4b";
            public const string DualClockEvidenceTestJs = "b72d031065485b45f115cac5a8d8b06f18decbcdd9f01ce615ce6f5d8984c41b";
        }

        /// <summary>返回黄金 bundle JSON 的绝对路径（供 task 8.2 对照工具独立解析）。</summary>
        public static string GetBundleAbsolutePath()
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            return dataPath + "/GameScripts/HotFix/GameBattle.Tests/EditMode/Golden/" + BundleFileName;
        }

        // ────────────────────────── 1. 最简配置 ──────────────────────────

        public static class MinimalConfig
        {
            public const int MapIndex = 0;
            public const int MapWidth = 8;
            public const int MapHeight = 10;
            public const string GridLayout = "列优先 grid[x][y]（x=列 0-7，y=行 0-9）";

            // 玩家/对手起止点
            public const int PlayerStartX = 0, PlayerStartY = 8;
            public const int PlayerEndX = 7, PlayerEndY = 9;
            public const int OpponentStartX = 7, OpponentStartY = 1;
            public const int OpponentEndX = 0, OpponentEndY = 0;

            // 玩家可建造格（map0 '1_1'）
            public static readonly int[][] PlayerBuildableCells =
            {
                new[] { 3, 1 }, new[] { 3, 2 }, new[] { 4, 1 }, new[] { 4, 2 }, new[] { 5, 1 }, new[] { 5, 2 },
            };
            // 对手可建造格（map0 '1_0'）
            public static readonly int[][] OpponentBuildableCells =
            {
                new[] { 2, 7 }, new[] { 2, 8 }, new[] { 3, 7 }, new[] { 3, 8 }, new[] { 4, 7 }, new[] { 4, 8 },
            };

            public const int PlayerPathLength = 17;
            public const int OpponentPathLength = 17;
        }

        /// <summary>Mob0 敌人数值（BattleDataCore 硬编码，enemies.json 无 HP/速度）。</summary>
        public static class EnemyConfig
        {
            public const string Type = "Mob0";
            public const int MapEnemyTypeIndex = 0;
            public const int Speed = 50;
            public const int PlayerMaxHealth = 3;
            public const int OpponentMaxHealth = 3;
            public const int ContactDamage = 1;
            public const float Wave1Health = 6.0f; // 10 * 1 * 0.6

            public static readonly int[] HealthByWave =
            {
                10, 11, 57, 44, 39, 92, 138, 200, 291, 421,
                611, 886, 1285, 1863, 2701, 3917, 5680, 8235, 11941, 17315,
            };
            public static readonly float[] EarlyRoundHealthMultipliers =
            {
                0.6f, 0.6f, 0.6f, 0.6f, 0.7f, 0.7f, 0.7f, 0.8f, 0.8f, 0.8f,
            };
        }

        /// <summary>波次数值（waves.json 与 BattleDataCore 一致）。</summary>
        public static class WaveConfig
        {
            public static readonly int[] WaveUnitCounts =
            {
                10, 11, 12, 13, 15, 16, 18, 19, 21, 24,
                26, 29, 31, 35, 38, 42, 46, 51, 56, 61,
            };
            public static readonly int[] BossWaveNumbers = { 3, 6, 9, 12, 15, 20 };
            public static readonly float[] BossSpawnChances = { 0.1f, 0.2f, 0.3f, 0.5f, 0.9f, 1f };
            public static readonly int[] SpawnStrategyWeights = { 5, 2, 3 };
            public const int MinimalWave1UnitCount = 10;
            public const bool SkipBoss = true;
            public const int DelayTimeMs = 10000;
            public const int MaxRounds = 20;
        }

        /// <summary>四兵数值（units.json）。</summary>
        public sealed class UnitConfig
        {
            public int Index { get; set; }
            public string Text { get; set; }
            public string AnimationKey { get; set; }
            public float RangeCells { get; set; }
            public int AttackDamage { get; set; }
            public float AttackIntervalSeconds { get; set; }
            public string DamageMode { get; set; }
            public string TargetPolicy { get; set; }
            public string AttackEffect { get; set; }
        }

        public static readonly UnitConfig[] Units =
        {
            new UnitConfig { Index = 0, Text = "刀", AnimationKey = "knife",   RangeCells = 1.5f, AttackDamage = 3, AttackIntervalSeconds = 0.8f, DamageMode = "单体",   TargetPolicy = "nearest",     AttackEffect = "KnifeAttackEffect 500ms 延迟命中" },
            new UnitConfig { Index = 1, Text = "弓", AnimationKey = "bow",     RangeCells = 3.5f, AttackDamage = 2, AttackIntervalSeconds = 0.8f, DamageMode = "单体",   TargetPolicy = "closest_end", AttackEffect = "ProjectileAttackEffect -> SimpleDynamicArrow" },
            new UnitConfig { Index = 2, Text = "枪", AnimationKey = "pike",    RangeCells = 2.5f, AttackDamage = 2, AttackIntervalSeconds = 0.8f, DamageMode = "近战枪击", TargetPolicy = "nearest",     AttackEffect = "PikeAttackEffect 360ms 命中" },
            new UnitConfig { Index = 3, Text = "骑", AnimationKey = "cavalry", RangeCells = 2.0f, AttackDamage = 2, AttackIntervalSeconds = 0.8f, DamageMode = "范围",   TargetPolicy = "nearest",     AttackEffect = "CavalrySweepEffect 150ms 双段" },
        };

        /// <summary>最简牌组（RecruitDefinitions.BASE_POOL）。</summary>
        public static class DeckConfig
        {
            public const bool MinimalMode = true;
            public static readonly string[] BaseSoldierTexts = { "刀", "弓", "枪", "骑" };
            public const int HandSize = 5;
            public const int DefaultLevel = 1;
            public const int BaseUnitCost = 1;
        }

        public static class ProjectileConfig
        {
            public const string Type = "SimpleDynamicArrow";
            public const bool IsOnlyRegisteredType = true;
            public const string MovementStrategy = "TargetEnemyBezierMovement";
            public const string HitStrategy = "HitEnemyStrategy";
        }

        public static class EconomyConfig
        {
            public const int InitialGold = 20;
            public const int RefreshCostStart = 10;
            public const int RefreshCostIncrement = 2;
            public const int UnitBaseCost = 1;
            public const int HandSize = 5;
            public const int PlayerMaxHealth = 3;
            public const int OpponentMaxHealth = 3;
        }

        // ────────────────────────── 2. 随机序列 ──────────────────────────

        public static class RandomSequence
        {
            /// <summary>函数式常量随机源（非 PRNG 种子）：每次调用恒返回 0.5。</summary>
            public const float ConstantValue = 0.5f;

            /// <summary>前 N 次产出。</summary>
            public static readonly float[] FirstNValues = { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
            public const int N = 10;

            /// <summary>weightedIndex 首次调用：weights=[5,2,3] target=0.5*10=5.0 → index 0。</summary>
            public const int WeightedIndexFirstCall = 0;

            /// <summary>drawText 首次调用（minimalMode）：floor(0.5*4)=2 → '枪'。测试用 setHand 覆盖。</summary>
            public const string DrawTextFirstCall = "枪";
        }

        // ────────────────────────── 3. 外部帧时间序列 ──────────────────────────

        public static class FrameTime
        {
            public const int MaxFrameDeltaMs = 500;
            public const int LogicStepMs = 80;

            /// <summary>MinimalBattleLoop.test.js tick(80) 每帧。</summary>
            public const int TickMs = 80;
            public const int TickCountToWave1 = 160;
            public const int TickCountToMovement = 220;
            public const int TickCountBattlePhase3 = 400;
        }

        /// <summary>标准外部帧序列（覆盖 16/80/550ms 与暂停），供 BattleSimulation 双时钟测试直接消费。</summary>
        public sealed class CanonicalFrame
        {
            public int DeltaMs { get; set; }
            public int ExpectedLogicAdvanceMs { get; set; }
            public int ExpectedSubstepCount { get; set; }
            public bool Paused { get; set; }
            public int[] ExpectedSubsteps { get; set; }
        }

        public static readonly CanonicalFrame[] CanonicalFrames =
        {
            new CanonicalFrame { DeltaMs = 16,  ExpectedLogicAdvanceMs = 16,  ExpectedSubstepCount = 1, Paused = false, ExpectedSubsteps = new[] { 16 } },
            new CanonicalFrame { DeltaMs = 80,  ExpectedLogicAdvanceMs = 80,  ExpectedSubstepCount = 1, Paused = false, ExpectedSubsteps = new[] { 80 } },
            new CanonicalFrame { DeltaMs = 81,  ExpectedLogicAdvanceMs = 81,  ExpectedSubstepCount = 2, Paused = false, ExpectedSubsteps = new[] { 80, 1 } },
            new CanonicalFrame { DeltaMs = 500, ExpectedLogicAdvanceMs = 500, ExpectedSubstepCount = 7, Paused = false, ExpectedSubsteps = new[] { 80, 80, 80, 80, 80, 80, 20 } },
            new CanonicalFrame { DeltaMs = 550, ExpectedLogicAdvanceMs = 500, ExpectedSubstepCount = 7, Paused = false, ExpectedSubsteps = new[] { 80, 80, 80, 80, 80, 80, 20 } },
            new CanonicalFrame { DeltaMs = 0,   ExpectedLogicAdvanceMs = 0,   ExpectedSubstepCount = 0, Paused = true,  ExpectedSubsteps = System.Array.Empty<int>() },
        };

        // ────────────────────────── 4. 输入命令序列 ──────────────────────────

        public static class InputCommandType
        {
            public const string PurchaseAndPlace = "PurchaseAndPlace";
            public const string Refresh = "Refresh";
        }

        public sealed class GoldenInputCommand
        {
            public int Step { get; set; }
            public string CommandType { get; set; }
            public bool Side { get; set; }
            public int Slot { get; set; }
            public int GridX { get; set; }
            public int GridY { get; set; }
            public string CardText { get; set; }
            /// <summary>C# 端按 step 分配的单局 CommandId（决策 0.8，JS 源无此字段）。</summary>
            public int CommandId => Step;
        }

        /// <summary>黄金购买放置命令序列（MinimalBattleLoop.test.js placeUnits 5.3）。</summary>
        public static readonly GoldenInputCommand[] GoldenInputCommands =
        {
            new GoldenInputCommand { Step = 1, CommandType = InputCommandType.PurchaseAndPlace, Side = true, Slot = 0, GridX = 4, GridY = 2, CardText = "弓" },
            new GoldenInputCommand { Step = 2, CommandType = InputCommandType.PurchaseAndPlace, Side = true, Slot = 1, GridX = 3, GridY = 2, CardText = "刀" },
            new GoldenInputCommand { Step = 3, CommandType = InputCommandType.PurchaseAndPlace, Side = true, Slot = 2, GridX = 5, GridY = 2, CardText = "枪" },
            new GoldenInputCommand { Step = 4, CommandType = InputCommandType.PurchaseAndPlace, Side = true, Slot = 3, GridX = 3, GridY = 1, CardText = "骑" },
        };

        public static class RefreshCommand
        {
            public const string CommandType = InputCommandType.Refresh;
            public const bool Side = true;
            public const int Cost = 10;
            public const int NextCost = 12;
        }

        // ────────────────────────── 5. 关键轨迹黄金输出 ──────────────────────────

        /// <summary>更新阶段顺序（每个子步，design.md 决策 2）。</summary>
        public static readonly string[] SubstepPhaseOrder =
        {
            "EnemyManager.update",
            "ProjectileManager.update",
            "DevelopmentAnimationDriver.update",
            "BattleManager.update (_updateSpawnState / _updateUnitAttacks / AttackEffectManager.update)",
            "后续动态注册回调 (UnitBase.update no-op)",
        };

        public sealed class TrajectoryFact
        {
            public string Id { get; set; }
            public string Test { get; set; }
            public string Assertion { get; set; }
            public string GoldenValue { get; set; }
            public string SourceLine { get; set; }
        }

        /// <summary>关键轨迹事实（MinimalBattleLoop.test.js 断言级黄金输出）。</summary>
        public static readonly TrajectoryFact[] TrajectoryFacts =
        {
            new TrajectoryFact { Id = "T1-wave1-spawn",          Test = "5.1+5.2", Assertion = "tickN(160)后 enemyManager.count>0", GoldenValue = "count>0", SourceLine = "MinimalBattleLoop.test.js:58-59" },
            new TrajectoryFact { Id = "T2-enemy-movement",       Test = "5.1+5.2", Assertion = "存在敌人 currentPathIndex>0 且 path.length>0", GoldenValue = "moved.length>0", SourceLine = "MinimalBattleLoop.test.js:62-67" },
            new TrajectoryFact { Id = "T3-adou-health-drop",     Test = "5.1+5.2", Assertion = "playerHealth+opponentHealth 下降", GoldenValue = "healthDropped==true", SourceLine = "MinimalBattleLoop.test.js:70-78" },
            new TrajectoryFact { Id = "T4-place-4-soldiers",     Test = "5.3",     Assertion = "4兵放置成功，unitManager.count==4", GoldenValue = "success=[true,true,true,true]; count==4", SourceLine = "MinimalBattleLoop.test.js:89-92" },
            new TrajectoryFact { Id = "T5-soldier-attack-kill",  Test = "5.3",     Assertion = "存在 UnitAttack 士兵；killCount>0；ENEMY_KILLED_BY 触发", GoldenValue = "attacking>0; killCount>0; killEvents>0", SourceLine = "MinimalBattleLoop.test.js:101-110" },
            new TrajectoryFact { Id = "T6-enemy-death-pool",     Test = "5.3",     Assertion = "resetForPool 与 recoverByKey('mob') 回收", GoldenValue = "after>before", SourceLine = "MinimalBattleLoop.test.js:113-116" },
            new TrajectoryFact { Id = "T7-loss-player-adou",     Test = "5.4a",    Assertion = "isGameOver; lastBattleResult=false; playerHealth==0; opponentHealth>0", GoldenValue = "判负（首信号胜出）", SourceLine = "MinimalBattleLoop.test.js:122-139" },
            new TrajectoryFact { Id = "T8-win-opponent-adou",    Test = "5.4b",    Assertion = "isGameOver; lastBattleResult=true; opponentHealth==0; playerHealth>0", GoldenValue = "判胜（首信号胜出）", SourceLine = "MinimalBattleLoop.test.js:142-160" },
            new TrajectoryFact { Id = "T9-cleanup-pool",         Test = "5.5",     Assertion = "gameOver后 enemy/unit/projectile/effect 全部归零", GoldenValue = "全部 count==0", SourceLine = "MinimalBattleLoop.test.js:172-180" },
            new TrajectoryFact { Id = "T10-restart-next",        Test = "5.5",     Assertion = "重置后新局可启动并出兵", GoldenValue = "isGameOver==false; 出兵正常", SourceLine = "MinimalBattleLoop.test.js:186-195" },
        };

        public static class ResultFreeze
        {
            public const string Rule = "首个 BATTLE_FINISHED 事实胜出（幂等）；TryFreeze 不在伤害调用栈内重入销毁 Manager";
        }
    }
}
