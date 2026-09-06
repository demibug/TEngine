using System;
using System.Collections.Generic;
using GameBattle.Weapon;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Combat
{
    // ============================================================================
    // 任务 5.3：Basic 武器不改变攻击路径回归测试
    // ----------------------------------------------------------------------------
    // 验证内容（specs/player-weapon-runtime/spec.md
    //   "Basic weapons do not create a second attack path"）：
    //   1. 四兵种装备基础武器后，target/release/hit/cooldown trace 与不装备完全一致，
    //      只改变预期 damage（按各兵种既有伤害公式计算）。
    //   2. 对手（无武器）trace 与不装备武器的玩家 trace 完全一致（含 damage）。
    //   3. 攻击冷却行为（AttackScheduler 触发/冷却窗口/再次触发）不被武器改变。
    //   4. 不新增武器专用 Prefab/资源/handler：EditMode 只验证纯逻辑路径。
    // ============================================================================

    /// <summary>
    /// Basic 武器攻击路径回归测试：四兵种装备武器后仅伤害变化，攻击链其余部分不变。
    /// </summary>
    [TestFixture]
    internal class BasicWeaponAttackPathRegressionTests
    {
        private const float UnitWidth = 40f;
        private const float UnitHeight = 40f;
        private const float CellSize = 80f;
        private const int GridSize = 80;
        private const int RuntimeId = 10;

        /// <summary>刀/枪/弓基础攻击力（偶数，武器 +1 使攻击力/箭伤可见地变为 base+1）。</summary>
        private const int BaseAttack = 20;

        /// <summary>骑兵基础攻击力（奇数，双段 0.5 倍截断后武器 +1 使总伤害可见地 +2）。</summary>
        private const int CavalryBaseAttack = 21;

        // ====================================================================
        // 测试敌人（与 SoldierTypeTests 一致的最小 IEnemyEntity 桩）
        // ====================================================================

        private sealed class TestEnemy : IEnemyEntity
        {
            internal int IdValue;
            internal float XValue;
            internal float YValue;
            internal int HealthValue;
            internal int StateValue;
            internal bool TargetableValue = true;
            internal int HitCount;
            internal int TotalHitDamage;

            public int Id => IdValue;
            public bool IsPlayerLane => false;
            public int CurrentState => StateValue;
            public float X => XValue;
            public float Y => YValue;
            public float Width => 40f;
            public float Height => 40f;
            public float ProjectileAimOffsetX => 0f;
            public float ProjectileAimOffsetY => 0f;
            public float RemainingPathDistance => 100f;
            public int CurrentPathIndex => 0;
            public int Health => HealthValue;
            public int MaxHealth => HealthValue;

            public void Update(long deltaMs) { }

            public bool Hit(int damage, int attackerId)
            {
                if (HealthValue <= 0 || damage <= 0)
                {
                    return false;
                }
                HealthValue = Math.Max(0, HealthValue - damage);
                TotalHitDamage += damage;
                HitCount++;
                if (HealthValue <= 0)
                {
                    StateValue = 4;
                    TargetableValue = false;
                }
                return true;
            }

            public bool GameOver()
            {
                StateValue = 4;
                TargetableValue = false;
                return true;
            }

            public bool IsTargetableBy(bool playerSide) =>
                TargetableValue && StateValue != 0 && StateValue != 4;
        }

        // ====================================================================
        // 攻击 trace
        // ====================================================================

        /// <summary>
        /// 单次攻击的可比较 trace：效果创建序列、命中事件、弓兵箭矢属性。
        /// </summary>
        private sealed class AttackTrace
        {
            public string[] EffectTypes = Array.Empty<string>();
            public List<(int TimeMs, int Damage)> Hits = new List<(int, int)>();
            public int ArrowCount;
            public int ArrowDamage = -1;
            public int ArrowTargetId = -1;
            public int ArrowAttackerId = -1;

            /// <summary>命中总伤害：弓兵取箭矢伤害（单次），其余兵种为各次命中之和。</summary>
            public int TotalDamage
            {
                get
                {
                    if (ArrowDamage >= 0)
                    {
                        return ArrowDamage;
                    }

                    int total = 0;
                    for (int i = 0; i < Hits.Count; i++)
                    {
                        total += Hits[i].Damage;
                    }

                    return total;
                }
            }

            public string Describe()
            {
                string hits = string.Join(";", Hits.ConvertAll(h => $"{h.TimeMs}ms/{h.Damage}"));
                return $"effects=[{string.Join(",", EffectTypes)}] hits=[{hits}] " +
                       $"arrow=({ArrowCount},{ArrowDamage},t{ArrowTargetId},a{ArrowAttackerId})";
            }
        }

        private static readonly SoldierType[] AllTypes =
        {
            SoldierType.Knife,
            SoldierType.Bow,
            SoldierType.Spear,
            SoldierType.Cavalry,
        };

        /// <summary>各兵种显式武器 id（与 resolver mapping 一致，测试直接写入以隔离 resolver）。</summary>
        private static int WeaponIdFor(SoldierType type)
        {
            switch (type)
            {
                case SoldierType.Knife: return BasicWeaponResolver.KnifeWeaponId;
                case SoldierType.Bow: return BasicWeaponResolver.BowWeaponId;
                case SoldierType.Spear: return BasicWeaponResolver.SpearWeaponId;
                case SoldierType.Cavalry: return BasicWeaponResolver.CavalryWeaponId;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary>各兵种基础攻击力（骑用奇数使双段 0.5 截断下武器可见）。</summary>
        private static int BaseAttackFor(SoldierType type)
            => type == SoldierType.Cavalry ? CavalryBaseAttack : BaseAttack;

        /// <summary>既有攻击链伤害公式：给定有效攻击力返回实际命中总伤害。</summary>
        private static int ExpectedDealtDamage(SoldierType type, int attackDamage)
        {
            switch (type)
            {
                case SoldierType.Knife:
                case SoldierType.Spear:
                case SoldierType.Bow:
                    return attackDamage;
                case SoldierType.Cavalry:
                    // 双段横扫各 (int)(damage * 0.5)，合计两段（既有公式，未因武器改变）。
                    return 2 * (int)(attackDamage * 0.5d);
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        // ====================================================================
        // 士兵初始化辅助（与 SoldierTypeTests 一致）
        // ====================================================================

        private static UnitConfigSnapshot MakeConfig(SoldierType type, int damage)
        {
            int index;
            string text;
            string anim;
            switch (type)
            {
                case SoldierType.Knife: index = 0; text = "刀"; anim = "knife"; break;
                case SoldierType.Bow: index = 1; text = "弓"; anim = "bow"; break;
                case SoldierType.Spear: index = 2; text = "枪"; anim = "pike"; break;
                case SoldierType.Cavalry: index = 3; text = "骑"; anim = "cavalry"; break;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }

            return new UnitConfigSnapshot(
                index, text, anim, rangeCells: 2f, attackDamage: damage,
                attackIntervalSeconds: 0.8f, damageMode: "单体", targetPolicy: "nearest");
        }

        private static SoldierBase SetupSoldier(
            SoldierType type,
            bool side,
            EnemyManager enemyManager,
            AttackResolver resolver,
            AttackEffectManager effectManager,
            ProjectileFactory factory,
            ProjectileManager projManager,
            out int baseAttack)
        {
            baseAttack = BaseAttackFor(type);
            UnitConfigSnapshot config = MakeConfig(type, baseAttack);

            switch (type)
            {
                case SoldierType.Knife:
                    var knife = new KnifeSoldier();
                    knife.Configure(enemyManager, resolver, effectManager, CellSize, 1);
                    knife.AssignRuntimeIdForTest(RuntimeId);
                    knife.InitForTest("刀", side, UnitWidth, UnitHeight);
                    knife.InitStats(config);
                    knife.ActivateAt(400f, 300f);
                    return knife;
                case SoldierType.Bow:
                    var bow = new BowSoldier();
                    bow.Configure(enemyManager, resolver, effectManager, factory, projManager, CellSize, 1);
                    bow.AssignRuntimeIdForTest(RuntimeId);
                    bow.InitForTest("弓", side, UnitWidth, UnitHeight);
                    bow.InitStats(config);
                    bow.ActivateAt(400f, 300f);
                    return bow;
                case SoldierType.Spear:
                    var spear = new SpearSoldier();
                    spear.Configure(enemyManager, resolver, effectManager, CellSize, 1);
                    spear.AssignRuntimeIdForTest(RuntimeId);
                    spear.InitForTest("枪", side, UnitWidth, UnitHeight);
                    spear.InitStats(config);
                    spear.ActivateAt(400f, 300f);
                    return spear;
                case SoldierType.Cavalry:
                    var cavalry = new CavalrySoldier();
                    cavalry.Configure(enemyManager, resolver, effectManager, CellSize, 1);
                    cavalry.AssignRuntimeIdForTest(RuntimeId);
                    cavalry.InitForTest("骑", side, UnitWidth, UnitHeight);
                    cavalry.InitStats(config);
                    cavalry.ActivateAt(400f, 300f);
                    return cavalry;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static EnemyTargetDto InitialTarget(TestEnemy enemy)
            => new EnemyTargetDto(enemy.IdValue, enemy.XValue, enemy.YValue, float.PositiveInfinity);

        // ====================================================================
        // 场景运行
        // ====================================================================

        private static AttackTrace RunScenario(
            SoldierType type, bool withWeapon, bool side, out TestEnemy enemy)
        {
            var enemyManager = new EnemyManager(GridSize);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();

            var idAllocator = new RuntimeIdAllocator();
            var arrowPool = new BattleObjectPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
            var factory = new ProjectileFactory(idAllocator, arrowPool, enemyManager, CellSize);
            var projManager = new ProjectileManager(factory);

            SoldierBase soldier = SetupSoldier(
                type, side, enemyManager, resolver, effectManager, factory, projManager, out int baseAttack);

            // 模拟 UnitRegistry 的默认武器应用（withWeapon=true 时写入 +1；对手侧永不装备）。
            if (withWeapon && side)
            {
                soldier.ApplyBasicWeapon(WeaponIdFor(type), 1);
            }

            enemy = new TestEnemy
            {
                IdValue = 1,
                XValue = type == SoldierType.Bow ? 600f : 420f,
                YValue = 300f,
                HealthValue = 10000,
                StateValue = 1,
                TargetableValue = true,
            };
            enemyManager.Register(enemy);

            var trace = new AttackTrace();

            soldier.Attack(InitialTarget(enemy));

            IReadOnlyList<IAttackEffect> snapshot = effectManager.GetEffectsSnapshot();
            trace.EffectTypes = new string[snapshot.Count];
            for (int i = 0; i < snapshot.Count; i++)
            {
                trace.EffectTypes[i] = snapshot[i].GetType().Name;
            }

            if (type == SoldierType.Bow)
            {
                // 释放延迟 = ceil(0.8s × 17/30) = 454ms；到达释放点应创建恰好一支箭。
                effectManager.Update(454);
                trace.ArrowCount = projManager.ActiveCount;
                IReadOnlyList<ProjectileBase> arrows = projManager.GetProjectilesSnapshot();
                if (arrows.Count > 0)
                {
                    trace.ArrowDamage = arrows[0].Damage;
                    trace.ArrowTargetId = arrows[0].TargetId;
                    trace.ArrowAttackerId = arrows[0].AttackerId;
                }
            }
            else
            {
                trace.Hits = SettleHits(effectManager, enemy, maxMs: 1200);
            }

            return trace;
        }

        /// <summary>
        /// 逐毫秒推进效果管理器，记录每次命中事件（时间毫秒、单次伤害）。
        /// </summary>
        private static List<(int TimeMs, int Damage)> SettleHits(
            AttackEffectManager effects, TestEnemy enemy, int maxMs)
        {
            var hits = new List<(int, int)>();
            int last = enemy.TotalHitDamage;
            for (int t = 1; t <= maxMs; t++)
            {
                effects.Update(1);
                if (enemy.TotalHitDamage != last)
                {
                    hits.Add((t, enemy.TotalHitDamage - last));
                    last = enemy.TotalHitDamage;
                }
            }

            return hits;
        }

        // ====================================================================
        // 四兵种：武器只改变预期 damage，其余 trace 不变
        // ====================================================================

        [Test]
        [Description("四兵种装备基础武器后 target/release/hit trace 不变，只改变预期 damage。")]
        public void AllFourSoldiers_WeaponChangesOnlyDamage_NotAttackPath()
        {
            for (int i = 0; i < AllTypes.Length; i++)
            {
                SoldierType type = AllTypes[i];
                AttackTrace without = RunScenario(type, withWeapon: false, side: true, out _);
                AttackTrace with = RunScenario(type, withWeapon: true, side: true, out _);

                int baseAttack = BaseAttackFor(type);
                int expectedWithout = ExpectedDealtDamage(type, baseAttack);
                int expectedWith = ExpectedDealtDamage(type, baseAttack + 1);

                AssertTracePathUnchanged(type, without, with);
                AssertDamageOnly(type, without, with, expectedWithout, expectedWith);
            }
        }

        [Test]
        [Description("对手（无武器）trace 与不装备武器的玩家 trace 完全一致（含 damage）。")]
        public void Opponent_Trace_IdenticalToUnarmedPlayer()
        {
            for (int i = 0; i < AllTypes.Length; i++)
            {
                SoldierType type = AllTypes[i];
                AttackTrace player = RunScenario(type, withWeapon: false, side: true, out _);
                AttackTrace opponent = RunScenario(type, withWeapon: true, side: false, out _);

                Assert.AreEqual(player.Describe(), opponent.Describe(),
                    $"{type}：对手无武器 trace 应完全等于不装备武器玩家的 trace");

                int baseAttack = BaseAttackFor(type);
                Assert.AreEqual(ExpectedDealtDamage(type, baseAttack), player.TotalDamage,
                    $"{type}：不装备武器玩家命中总伤害=基础公式值");
                Assert.AreEqual(ExpectedDealtDamage(type, baseAttack), opponent.TotalDamage,
                    $"{type}：对手命中总伤害与玩家不装备完全一致");
            }
        }

        [Test]
        [Description("装备武器的玩家命中的总伤害按既有公式只含 +1 武器贡献，且各次命中时机不变。")]
        public void Weapon_Trace_HitTimingsUnchanged_DamageAdjustedPerFormula()
        {
            for (int i = 0; i < AllTypes.Length; i++)
            {
                SoldierType type = AllTypes[i];
                AttackTrace without = RunScenario(type, withWeapon: false, side: true, out _);
                AttackTrace with = RunScenario(type, withWeapon: true, side: true, out _);

                Assert.AreEqual(without.Hits.Count, with.Hits.Count,
                    $"{type}：命中次数不变");
                for (int h = 0; h < without.Hits.Count; h++)
                {
                    Assert.AreEqual(without.Hits[h].TimeMs, with.Hits[h].TimeMs,
                        $"{type}：第 {h} 次命中时机不变");
                }
            }
        }

        private static void AssertTracePathUnchanged(
            SoldierType type, AttackTrace without, AttackTrace with)
        {
            Assert.AreEqual(string.Join(",", without.EffectTypes), string.Join(",", with.EffectTypes),
                $"{type}：效果创建序列不变（无第二条攻击路径）");
            Assert.AreEqual(without.Hits.Count, with.Hits.Count,
                $"{type}：命中次数不变");
            for (int h = 0; h < without.Hits.Count; h++)
            {
                Assert.AreEqual(without.Hits[h].TimeMs, with.Hits[h].TimeMs,
                    $"{type}：第 {h} 次命中时机不变");
            }

            if (type == SoldierType.Bow)
            {
                Assert.AreEqual(without.ArrowCount, with.ArrowCount,
                    "{type}：箭矢数量不变（固定目标 Arrow 生命周期仍只跑一次）");
                Assert.AreEqual(without.ArrowTargetId, with.ArrowTargetId,
                    "{type}：箭矢目标 ID 不变（target 选择不变）");
                Assert.AreEqual(without.ArrowAttackerId, with.ArrowAttackerId,
                    "{type}：箭矢攻击者 ID 不变");
            }
        }

        private static void AssertDamageOnly(
            SoldierType type, AttackTrace without, AttackTrace with,
            int expectedWithout, int expectedWith)
        {
            Assert.AreEqual(expectedWithout, without.TotalDamage,
                $"{type}：不装备武器命中总伤害 = 既有公式值 {expectedWithout}");
            Assert.AreEqual(expectedWith, with.TotalDamage,
                $"{type}：装备武器命中总伤害 = 既有公式值(基础+1) = {expectedWith}");
            Assert.AreNotEqual(expectedWithout, expectedWith,
                $"{type}：测试前提——武器应使预期伤害可见地变化");
        }

        // ====================================================================
        // 冷却行为（task 5.3 cooldown trace）
        // ====================================================================

        [Test]
        [Description("AttackScheduler 冷却触发/冷却窗口/再次触发与武器无关（cooldown trace 不变）。")]
        public void AttackScheduler_CooldownBehavior_UnchangedByWeapon()
        {
            int[] countsWithout = RunSchedulerCounts(withWeapon: false);
            int[] countsWith = RunSchedulerCounts(withWeapon: true);

            Assert.AreEqual(string.Join(",", countsWithout), string.Join(",", countsWith),
                "武器不得改变 AttackScheduler 的攻击触发次数序列（冷却行为不变）");
            Assert.AreEqual(1, countsWithout[0], "第 1 帧冷却就绪应触发 1 次");
            Assert.AreEqual(0, countsWithout[1], "冷却窗口内不得再次触发");
            Assert.AreEqual(1, countsWithout[2], "冷却结束后应再次触发 1 次");
        }

        private static int[] RunSchedulerCounts(bool withWeapon)
        {
            var enemyManager = new EnemyManager(GridSize);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();

            var idAllocator = new RuntimeIdAllocator();
            var arrowPool = new BattleObjectPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
            var factory = new ProjectileFactory(idAllocator, arrowPool, enemyManager, CellSize);
            var projManager = new ProjectileManager(factory);

            SoldierBase soldier = SetupSoldier(
                SoldierType.Knife, side: true, enemyManager, resolver, effectManager,
                factory, projManager, out _);
            if (withWeapon)
            {
                soldier.ApplyBasicWeapon(
                    BasicWeaponResolver.KnifeWeaponId,
                    BasicWeaponResolver.BasicAttackPower);
            }

            enemyManager.Register(new TestEnemy
            {
                IdValue = 1,
                XValue = 420f,
                YValue = 300f,
                HealthValue = 10000,
                StateValue = 1,
                TargetableValue = true,
            });

            var actionScheduler = new BattleActionScheduler();
            var scheduler = new AttackScheduler(actionScheduler, resolver, CellSize, CellSize);
            var units = new List<IAttackUnit> { soldier };

            // 每帧调用两次 Update 模拟生产的多个子步（FrameNowMs 同帧不变），
            // 取两子步触发次数之和作为该帧的攻击次数：
            //   帧1 (1000)：子步1 从 Idle 切到 Attack（0），子步2 冷却就绪触发攻击（1）→ 1。
            //   帧2 (1100)：1000-1000=0 < 800，冷却窗口内两子步都不攻击 → 0。
            //   帧3 (1900)：1900-1000=900 >= 800，子步1 再次触发攻击（1），子步2 冷却中（0）→ 1。
            actionScheduler.BeginFrame(1000);
            int first = scheduler.Update(units, enemyManager) + scheduler.Update(units, enemyManager);

            actionScheduler.BeginFrame(1100);
            int withinCooldown = scheduler.Update(units, enemyManager) + scheduler.Update(units, enemyManager);

            actionScheduler.BeginFrame(1900);
            int afterCooldown = scheduler.Update(units, enemyManager) + scheduler.Update(units, enemyManager);

            return new[] { first, withinCooldown, afterCooldown };
        }
    }
}
