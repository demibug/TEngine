using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Spine.Unity;
using TEngine;
using UnityEngine;
using YooAsset;

namespace GameBattle
{
    /// <summary>
    /// 战斗表现必需资源加载或实例化失败。
    /// </summary>
    internal sealed class BattlePresentationLoadException : Exception
    {
        internal BattlePresentationLoadException(string operation, string resourceAddress, Exception innerException = null)
            : this(BattleFailureStage.PresentationPreload, operation, resourceAddress, innerException)
        {
        }

        internal BattlePresentationLoadException(
            BattleFailureStage failureStage,
            string operation,
            string resourceAddress,
            Exception innerException = null)
            : base(
                $"战斗表现资源失败 stage={failureStage} operation={operation} address={resourceAddress}",
                innerException)
        {
            FailureStage = failureStage;
            Operation = operation ?? string.Empty;
            ResourceAddress = resourceAddress ?? string.Empty;
        }

        internal BattleFailureStage FailureStage { get; }

        internal string ResourceAddress { get; }

        internal string Operation { get; }
    }

    /// <summary>
    /// 将战斗实体表现绑定到 BattleMap0 动态根节点的 Unity 实现。
    /// </summary>
    internal sealed class UnityBattleViewPort : IBattleViewPort
    {
        private const string ArrowAddress = "Arrow";
        private const int MaxLevelNumber = 8;
        private const string LevelNumberAddressPrefix = "Sprites/LevelBadge/level_number_";

        private readonly BattleMapBindings _bindings;
        private readonly Dictionary<int, string> _unitAddresses = new Dictionary<int, string>
        {
            { 0, "KnifeSoldier" },
            { 1, "BowSoldier" },
            { 2, "SpearSoldier" },
            { 3, "CavalrySoldier" },
        };
        private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
        private readonly Dictionary<int, SoldierAnimationFrames> _unitAnimations =
            new Dictionary<int, SoldierAnimationFrames>();
        private readonly List<IBattleAssetLease> _assetLeases = new List<IBattleAssetLease>();
        private readonly Dictionary<string, Stack<GameObject>> _pools =
            new Dictionary<string, Stack<GameObject>>();
        private readonly Dictionary<int, ActiveInstance> _activeInstances =
            new Dictionary<int, ActiveInstance>();

        private bool _preloaded;
        private BattleViewRegistry _registry;
        private SpriteRenderer[] _playerHealthPoints;
        private SpriteRenderer[] _opponentHealthPoints;
        private Sprite[] _levelNumberSprites;
        private readonly Dictionary<string, Sprite> _generalPartSprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<int, GameObject> _generalPartGlyphs = new Dictionary<int, GameObject>();

        /// <summary>
        /// 资源加载接缝。生产默认由 <see cref="GetOrCreateLoader"/> 惰性创建
        /// <see cref="UnityResourceAssetLoader"/>；测试注入替身以驱动成功、失败与句柄释放。
        /// </summary>
        private IBattleViewAssetLoader _loader;

        internal UnityBattleViewPort(BattleMapBindings bindings)
            : this(bindings, null)
        {
        }

        /// <summary>
        /// 以可注入的资源加载接缝构造端口（供 EditMode 测试驱动预加载与释放语义）。
        /// </summary>
        /// <param name="bindings">地图节点绑定（非 null）。</param>
        /// <param name="loader">资源加载接缝；null 时在预加载时惰性创建生产实现。</param>
        internal UnityBattleViewPort(BattleMapBindings bindings, IBattleViewAssetLoader loader)
        {
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _loader = loader;
        }

        internal void ConfigureRegistry(BattleViewRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// 获取已预加载士兵 Prefab 的默认立绘，供底牌复用同一份美术资源。
        /// </summary>
        internal Sprite GetUnitIcon(int soldierType)
        {
            if (!_unitAddresses.TryGetValue(soldierType, out string address)
                || !_prefabs.TryGetValue($"unit_{address}", out GameObject prefab)
                || prefab == null)
            {
                return null;
            }

            SpriteRenderer renderer = prefab.transform.Find("Body")?.GetComponent<SpriteRenderer>();
            return renderer != null ? renderer.sprite : null;
        }

        /// <summary>
        /// 获取已预加载的武将拆字字形贴图；未预加载或未知拆字返回 null。
        /// </summary>
        /// <remarks>已预加载时同步查表，无 IO。供 BattleHudPanel 卡牌复用，与 GetUnitIcon 同一模式。</remarks>
        internal Sprite GetGeneralPartIcon(string partWord)
        {
            if (string.IsNullOrEmpty(partWord))
            {
                return null;
            }

            _generalPartSprites.TryGetValue(partWord, out Sprite sprite);
            return sprite;
        }

        /// <summary>
        /// 判断 Stage 屏幕点是否命中活动单位可见主体 Renderer 的世界 AABB 屏幕投影。
        /// </summary>
        /// <param name="runtimeId">活动单位运行时 ID（对应 <see cref="SoldierBase.Id"/>）。</param>
        /// <param name="stageX">FairyGUI Stage X 坐标（左上角原点）。</param>
        /// <param name="stageY">FairyGUI Stage Y 坐标（左上角原点）。</param>
        /// <returns>命中返回 true；单位无活动表现、无可见主体 Renderer 或无相机时返回 false。</returns>
        /// <remarks>
        /// <para>供战场起拖源命中使用（统一拖放规则：战场只能从单位 Body 起拖，格子底框不能起拖）。</para>
        /// <para>把 Body SpriteRenderer 世界包围盒的 8 个角投影到屏幕矩形（左下角原点），
        /// 再把 Stage 点（左上角原点，Y 翻转）与之比较。投放目标仍使用完整槽位命中。</para>
        /// <para>普通士兵优先使用名为 Body 的 Renderer；Spine 武将 Prefab 没有 Body，
        /// 回退到 SkeletonAnimation 所在的 MeshRenderer，再回退到首个可见网格 Renderer。</para>
        /// </remarks>
        internal bool TryHitActiveUnitBody(int runtimeId, float stageX, float stageY)
        {
            if (!_activeInstances.TryGetValue(runtimeId, out ActiveInstance active)
                || active.GameObject == null)
            {
                return false;
            }

            Renderer body = ResolveUnitDragRenderer(active.GameObject);
            return HitBodyInStage(body, stageX, stageY);
        }

        /// <summary>
        /// 解析单位用于起拖命中的可见主体 Renderer。
        /// </summary>
        /// <remarks>
        /// 普通士兵沿用 Body 节点；Spine 武将使用 SkeletonAnimation 同节点的 MeshRenderer。
        /// 最后的网格 Renderer 回退兼容不使用 Spine、但同样没有 Body 节点的配置化武将 Prefab。
        /// </remarks>
        internal static Renderer ResolveUnitDragRenderer(GameObject instance)
        {
            if (instance == null)
            {
                return null;
            }

            Renderer body = instance.transform.Find("Body")?.GetComponent<Renderer>();
            if (body != null)
            {
                return body;
            }

            SkeletonAnimation spine = instance.GetComponentInChildren<SkeletonAnimation>(true);
            Renderer spineRenderer = spine?.GetComponent<Renderer>();
            if (spineRenderer != null)
            {
                return spineRenderer;
            }

            SkinnedMeshRenderer skinnedMesh = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skinnedMesh != null)
            {
                return skinnedMesh;
            }

            return instance.GetComponentInChildren<MeshRenderer>(true);
        }

        /// <summary>
        /// 判断 Stage 屏幕点是否命中武将字部件字形对象的 SpriteRenderer 世界 AABB 屏幕投影。
        /// </summary>
        /// <param name="slotId">战场槽位固定标识（对应 <see cref="OnBattleGeneralPartGlyphChanged"/> 的 slotId）。</param>
        /// <param name="stageX">FairyGUI Stage X 坐标（左上角原点）。</param>
        /// <param name="stageY">FairyGUI Stage Y 坐标（左上角原点）。</param>
        /// <returns>命中返回 true；该槽无字形、无 SpriteRenderer 或无相机时返回 false。</returns>
        /// <remarks>
        /// 供战场起拖源命中使用：未合成武将字在战场以单格字形显示并可再次起拖。
        /// 复用 <see cref="HitBodyInStage"/> 的 SpriteRenderer bounds 屏幕投影，与活动单位 Body 命中一致。
        /// </remarks>
        internal bool TryHitGeneralPartGlyph(int slotId, float stageX, float stageY)
        {
            if (!_generalPartGlyphs.TryGetValue(slotId, out GameObject glyph) || glyph == null)
            {
                return false;
            }

            SpriteRenderer body = glyph.GetComponent<SpriteRenderer>();
            return HitBodyInStage(body, stageX, stageY);
        }

        /// <summary>
        /// 把 SpriteRenderer 世界包围盒的 8 个角投影到屏幕矩形（左下角原点），
        /// 再把 Stage 点（左上角原点，Y 翻转）与之比较。
        /// </summary>
        private static bool HitBodyInStage(Renderer body, float stageX, float stageY)
        {
            if (body == null || !body.enabled
                || body is SpriteRenderer spriteRenderer && spriteRenderer.sprite == null)
            {
                return false;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return false;
            }

            Bounds bounds = body.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            Vector3[] corners =
            {
                center + new Vector3(-extents.x, -extents.y, -extents.z),
                center + new Vector3(extents.x, -extents.y, -extents.z),
                center + new Vector3(extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, extents.y, extents.z),
                center + new Vector3(-extents.x, extents.y, extents.z),
            };

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 screen = camera.WorldToScreenPoint(corners[i]);
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxY = Mathf.Max(maxY, screen.y);
            }

            // Stage 坐标为左上角原点；转换到 Unity 左下角原点后比较（与 UnityCoordinateConverter 一致）。
            float unityScreenY = Screen.height - stageY;
            return stageX >= minX && stageX <= maxX
                && unityScreenY >= minY && unityScreenY <= maxY;
        }

        /// <summary>
        /// 显示或移除某个战场槽位的武将字部件字形。
        /// </summary>
        /// <param name="slotId">固定槽位标识（Battle 槽）。</param>
        /// <param name="isPlayerSide">是否玩家方（当前仅玩家侧会有武将字，保留供通用）。</param>
        /// <param name="gridX">战场格子列。</param>
        /// <param name="gridY">战场格子行。</param>
        /// <param name="partWord">武将字；null 或空时移除该槽字形。</param>
        /// <remarks>
        /// <para>由 <see cref="BattlePresenter"/> 从权威槽位事实驱动。字形使用已预加载的
        /// <see cref="_generalPartSprites"/>（<see cref="LoadGeneralPartSpritesAsync"/> 填充），
        /// 以 SpriteRenderer 呈现，并跟踪 GameObject 供 <see cref="TryHitGeneralPartGlyph"/>
        /// 与 <see cref="Clear"/> 清理。字形不进入 UnitRegistry/攻击调度，仅作可起拖表现。</para>
        /// </remarks>
        public void OnBattleGeneralPartGlyphChanged(int slotId, bool isPlayerSide, int gridX, int gridY, string partWord)
        {
            if (string.IsNullOrEmpty(partWord))
            {
                RemoveGeneralPartGlyph(slotId);
                return;
            }

            if (!_generalPartSprites.TryGetValue(partWord, out Sprite sprite) || sprite == null)
            {
                // 未预加载该字形：保持无字形状态（移除旧字形，避免残留错误字形）。
                RemoveGeneralPartGlyph(slotId);
                return;
            }

            Vector3 world = _bindings.CellToWorld(gridX, gridY);
            if (_generalPartGlyphs.TryGetValue(slotId, out GameObject existing) && existing != null)
            {
                existing.transform.position = world;
                SpriteRenderer existingRenderer = existing.GetComponent<SpriteRenderer>();
                if (existingRenderer != null)
                {
                    existingRenderer.sprite = sprite;
                    existingRenderer.sortingOrder = 12;
                }

                return;
            }

            var glyph = new GameObject($"GeneralPartGlyph_{slotId}");
            glyph.transform.SetParent(_bindings.SoldierRoot, false);
            glyph.transform.position = world;
            SpriteRenderer renderer = glyph.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            // 玩家战场槽底框使用 sortingOrder=8；字形必须位于其上方才能稳定显示和命中。
            renderer.sortingOrder = 12;
            _generalPartGlyphs[slotId] = glyph;
        }

        private void RemoveGeneralPartGlyph(int slotId)
        {
            if (!_generalPartGlyphs.TryGetValue(slotId, out GameObject glyph) || glyph == null)
            {
                return;
            }

            _generalPartGlyphs.Remove(slotId);
            Destroy(glyph);
        }

        /// <summary>
        /// 加载本端口的全部必需 Prefab。失败时释放本端口已取得的所有句柄。
        /// </summary>
        /// <param name="enemyResourceAddresses">
        /// 本局所选计划解析后会使用的去重普通敌人资源地址；只为本局实际引用的敌人
        /// 加载 Prefab 并建立表现池，不再固定预加载 Mob0。
        /// </param>
        /// <param name="generalResourceAddresses">配置驱动的 General Prefab 地址。</param>
        /// <param name="generalPartWords">需预加载的武将拆字列表，用于字形贴图加载。</param>
        public async UniTask PreloadAsync(
            IReadOnlyList<string> enemyResourceAddresses,
            IReadOnlyList<string> generalResourceAddresses,
            IReadOnlyList<string> generalPartWords)
        {
            if (_preloaded)
            {
                return;
            }

            IBattleViewAssetLoader loader = GetOrCreateLoader();

            var requiredPrefabs = new List<PrefabLoadEntry>();
            if (enemyResourceAddresses != null)
            {
                foreach (string address in enemyResourceAddresses)
                {
                    // 敌人以 resourceAddress 为预加载池键（可内部加前缀但映射唯一）。
                    requiredPrefabs.Add(new PrefabLoadEntry(
                        address, address, ViewObjectCategory.Enemy));
                }
            }

            requiredPrefabs.Add(new PrefabLoadEntry(
                "unit_KnifeSoldier", "KnifeSoldier", ViewObjectCategory.Unit));
            requiredPrefabs.Add(new PrefabLoadEntry(
                "unit_BowSoldier", "BowSoldier", ViewObjectCategory.Unit));
            requiredPrefabs.Add(new PrefabLoadEntry(
                "unit_SpearSoldier", "SpearSoldier", ViewObjectCategory.Unit));
            requiredPrefabs.Add(new PrefabLoadEntry(
                "unit_CavalrySoldier", "CavalrySoldier", ViewObjectCategory.Unit));
            if (generalResourceAddresses != null)
            {
                var seenGeneralAddresses = new HashSet<string>(StringComparer.Ordinal)
                {
                    "KnifeSoldier",
                    "BowSoldier",
                    "SpearSoldier",
                    "CavalrySoldier",
                };
                foreach (string address in generalResourceAddresses)
                {
                    if (seenGeneralAddresses.Add(address))
                    {
                        requiredPrefabs.Add(new PrefabLoadEntry(
                            $"unit_{address}", address, ViewObjectCategory.Unit));
                    }
                }
            }
            requiredPrefabs.Add(new PrefabLoadEntry(
                "projectile_arrow", ArrowAddress, ViewObjectCategory.Projectile));

            try
            {
                for (int index = 0; index < requiredPrefabs.Count; index++)
                {
                    PrefabLoadEntry entry = requiredPrefabs[index];
                    await LoadRequiredPrefabAsync(
                        loader, entry.AssetKey, entry.Address, entry.Category);
                }

                await LoadUnitAnimationsAsync(loader);
                await LoadLevelNumberSpritesAsync(loader);
                await LoadGeneralPartSpritesAsync(loader, generalPartWords);

                _preloaded = true;
            }
            catch (BattlePresentationLoadException)
            {
                ResetPreload();
                throw;
            }
            catch (Exception ex)
            {
                ResetPreload();
                throw new BattlePresentationLoadException("preload", "<unknown>", ex);
            }
        }

        public void OnBattleStarted(int maxRounds, int playerMaxHealth, int opponentMaxHealth)
        {
            RefreshHealth(GetOrCachePlayerHealthPoints(), playerMaxHealth);
            RefreshHealth(GetOrCacheOpponentHealthPoints(), opponentMaxHealth);
        }

        public void OnBattleFinished(bool playerWin, int resultStar) { }

        public void OnEnemySpawned(EnemySpawnViewData dto)
        {
            if (dto == null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            // 按 DTO.resourceAddress 选择已预加载 Prefab 与对应表现池，不再固定 Mob0。
            // 空地址（测试替身/非普通实体）显式失败，禁止静默 fallback。
            string address = dto.ResourceAddress;
            if (string.IsNullOrEmpty(address))
            {
                throw new BattlePresentationLoadException(
                    "instantiate",
                    "<empty-enemy-address>",
                    new InvalidOperationException(
                        "敌人出生 DTO 缺少 resourceAddress（非生产普通敌人不得伪装为普通敌人）"));
            }

            GameObject instance = SpawnInstance(
                dto.RuntimeId,
                ViewObjectCategory.Enemy,
                address,
                _bindings.LogicToWorld(dto.LogicX, dto.LogicY),
                _bindings.EnemyRoot);

            if (dto.Kind == EnemyPresentationKind.Boss)
            {
                SkeletonAnimation spine = instance.GetComponentInChildren<SkeletonAnimation>(true);
                if (spine == null || spine.AnimationState == null)
                {
                    DespawnInstance(dto.RuntimeId);
                    throw new BattlePresentationLoadException(
                        "bind-boss-spine",
                        address,
                        new InvalidOperationException(
                            $"Boss '{dto.BossResName}' Prefab 缺少可初始化的 SkeletonAnimation"));
                }

                spine.AnimationState.SetAnimation(0, dto.IdleAnimationKey, true);
            }

            // 出生时血条保持隐藏、复位到满血（池复用不残留旧显示状态）。
            EnemyHealthBarView healthBar = instance.GetComponent<EnemyHealthBarView>();
            if (healthBar == null)
            {
                healthBar = instance.AddComponent<EnemyHealthBarView>();
                Transform bg = instance.transform.Find("VisualRoot/hpBgImg");
                Transform fill = instance.transform.Find("VisualRoot/hpBgImg/hpImg2");
                Transform standbyFill = instance.transform.Find("VisualRoot/hpBgImg/hpImg1");
                if (bg != null && fill != null)
                {
                    healthBar.Bind(
                        bg.GetComponent<SpriteRenderer>(),
                        fill.GetComponent<SpriteRenderer>(),
                        standbyFill == null ? null : standbyFill.GetComponent<SpriteRenderer>());
                }
            }
            else
            {
                healthBar.ResetAndHide();
            }
        }

        public void OnEnemyRemoved(int runtimeId, bool playDeathEffect)
        {
            DespawnInstance(runtimeId);
        }

        public void OnBossSkillIntent(int runtimeId, string animationKey, bool active)
        {
            if (string.IsNullOrEmpty(animationKey)
                || !_activeInstances.TryGetValue(runtimeId, out ActiveInstance activeInstance))
            {
                return;
            }

            SkeletonAnimation spine =
                activeInstance.GameObject.GetComponentInChildren<SkeletonAnimation>(true);
            if (spine == null || spine.AnimationState == null)
            {
                throw new BattlePresentationLoadException(
                    "boss-skill-animation",
                    activeInstance.AssetKey,
                    new InvalidOperationException("Boss 表现缺少 SkeletonAnimation"));
            }

            spine.AnimationState.SetAnimation(0, animationKey, loop: !active);
        }

        public void OnUnitPlaced(int runtimeId, bool isPlayerSide, int soldierType, int gridX, int gridY, int level)
        {
            OnConfiguredUnitPlaced(new UnitSpawnViewData(
                runtimeId, isPlayerSide, 0, -1, string.Empty, soldierType,
                string.Empty, string.Empty, gridX, gridY, level));
        }

        public void OnConfiguredUnitPlaced(UnitSpawnViewData dto)
        {
            string address = dto.PrefabAddress;
            if (string.IsNullOrEmpty(address)
                && !_unitAddresses.TryGetValue(dto.SoldierType, out address))
            {
                throw new BattlePresentationLoadException(
                    "instantiate",
                    $"soldierType:{dto.SoldierType}",
                    new ArgumentOutOfRangeException(nameof(dto.SoldierType)));
            }

            SpawnInstance(
                dto.RuntimeId,
                ViewObjectCategory.Unit,
                $"unit_{address}",
                _bindings.UnitCellToWorld(dto.GridX, dto.GridY),
                _bindings.SoldierRoot);

            // 战场单位附加等级数字表现（修复 P0：首次上场显示真实等级，不再固定 1）。
            if (_activeInstances.TryGetValue(dto.RuntimeId, out ActiveInstance placed))
            {
                SoldierLevelBadge badge = placed.GameObject.GetComponent<SoldierLevelBadge>();
                if (badge == null)
                {
                    badge = placed.GameObject.AddComponent<SoldierLevelBadge>();
                }

                badge.Configure(_levelNumberSprites);
                badge.SetLevel(dto.Level > 0 ? dto.Level : 1);
                ConfigureGeneralSpine(placed.GameObject, dto.AnimationKey);
            }
        }

        public void OnUnitRemoved(int runtimeId)
        {
            DespawnInstance(runtimeId);
        }

        public void OnUnitMoved(int runtimeId, int gridX, int gridY)
        {
            if (!_activeInstances.TryGetValue(runtimeId, out ActiveInstance active))
            {
                return;
            }

            active.GameObject.transform.position = _bindings.UnitCellToWorld(gridX, gridY);
        }

        public void OnUnitLevelChanged(int runtimeId, int newLevel)
        {
            if (!_activeInstances.TryGetValue(runtimeId, out ActiveInstance active))
            {
                return;
            }

            active.GameObject.GetComponent<SoldierLevelBadge>()?.SetLevel(newLevel);
        }

        public void OnProjectileFired(int runtimeId, float fromX, float fromY, bool isPlayerSide)
        {
            SpawnInstance(
                runtimeId,
                ViewObjectCategory.Projectile,
                "projectile_arrow",
                _bindings.LogicToWorld(fromX, fromY),
                _bindings.ProjectileRoot);
        }

        public void OnProjectileRemoved(int runtimeId)
        {
            DespawnInstance(runtimeId);
        }

        public void OnHealthChanged(bool isPlayerSide, int currentHealth, int maxHealth, int delta)
        {
            SpriteRenderer[] healthPoints = isPlayerSide
                ? GetOrCachePlayerHealthPoints()
                : GetOrCacheOpponentHealthPoints();
            RefreshHealth(healthPoints, currentHealth);
        }

        public void OnGoldChanged(bool isPlayerSide, int currentGold, int delta) { }

        public void OnRoundChanged(int currentRound, int maxRounds) { }

        public void OnHandUpdated(bool isPlayerSide, int handSlotCount) { }

        public void Clear()
        {
            foreach (ActiveInstance active in _activeInstances.Values)
            {
                Destroy(active.GameObject);
            }
            _activeInstances.Clear();

            // 清理战场武将字字形对象（在 ResetPreload 清空字形贴图字典之前）。
            foreach (GameObject glyph in _generalPartGlyphs.Values)
            {
                Destroy(glyph);
            }
            _generalPartGlyphs.Clear();

            foreach (Stack<GameObject> pool in _pools.Values)
            {
                while (pool.Count > 0)
                {
                    Destroy(pool.Pop());
                }
            }
            _pools.Clear();
            _registry = null;
            _playerHealthPoints = null;
            _opponentHealthPoints = null;
            ResetPreload();
        }

        /// <summary>
        /// 绑定我方阿斗头顶的三颗心（PlayerEnd/ADou/HealthRoot）。
        /// </summary>
        private SpriteRenderer[] GetOrCachePlayerHealthPoints()
        {
            if (_playerHealthPoints == null)
            {
                _playerHealthPoints = CacheHealthPoints(_bindings.PlayerEnd);
            }

            return _playerHealthPoints;
        }

        /// <summary>
        /// 绑定敌方终点阿斗头顶的三颗心（OpponentEnd/ADou/HealthRoot）。
        /// </summary>
        private SpriteRenderer[] GetOrCacheOpponentHealthPoints()
        {
            if (_opponentHealthPoints == null)
            {
                _opponentHealthPoints = CacheHealthPoints(_bindings.OpponentEnd);
            }

            return _opponentHealthPoints;
        }

        /// <summary>
        /// 缓存指定终点阿斗（PlayerEnd/OpponentEnd）头顶的三颗心。
        /// 阿斗本体后续替换为 Spine 表现时，保持该节点路径即可。
        /// </summary>
        private SpriteRenderer[] CacheHealthPoints(Transform endPoint)
        {
            Transform healthRoot = endPoint.Find("ADou/HealthRoot");
            if (healthRoot == null)
            {
                throw new BattlePresentationLoadException(
                    "bind-health",
                    $"{endPoint.name}/ADou/HealthRoot",
                    new InvalidOperationException("阿斗血量节点不存在"));
            }

            var healthPoints = new SpriteRenderer[3];
            for (int index = 0; index < healthPoints.Length; index++)
            {
                Transform healthPoint = healthRoot.Find($"HealthPoint{index + 1}");
                SpriteRenderer renderer = healthPoint == null
                    ? null
                    : healthPoint.GetComponent<SpriteRenderer>();
                if (renderer == null)
                {
                    throw new BattlePresentationLoadException(
                        "bind-health",
                        $"{endPoint.name}/ADou/HealthRoot/HealthPoint{index + 1}",
                        new InvalidOperationException("阿斗血量图标不存在或缺少 SpriteRenderer"));
                }

                healthPoints[index] = renderer;
            }

            return healthPoints;
        }

        /// <summary>
        /// 按当前生命数显示连续的心形血量；生命降为 2、1、0 时从右向左隐藏。
        /// </summary>
        private void RefreshHealth(SpriteRenderer[] healthPoints, int currentHealth)
        {
            int visibleCount = Mathf.Clamp(currentHealth, 0, healthPoints.Length);
            for (int index = 0; index < healthPoints.Length; index++)
            {
                healthPoints[index].enabled = index < visibleCount;
            }
        }

        private static IResourceModule GetRequiredResourceModule()
        {
            try
            {
                IResourceModule resource = ModuleSystem.GetModule<IResourceModule>();
                if (resource != null)
                {
                    return resource;
                }
            }
            catch (Exception ex)
            {
                throw new BattlePresentationLoadException("resource-module", "<resource-module>", ex);
            }

            throw new BattlePresentationLoadException(
                "resource-module",
                "<resource-module>",
                new InvalidOperationException("IResourceModule 未初始化"));
        }

        /// <summary>
        /// 取得资源加载接缝：测试注入的替身优先，否则惰性创建生产实现。
        /// </summary>
        private IBattleViewAssetLoader GetOrCreateLoader()
        {
            if (_loader != null)
            {
                return _loader;
            }

            _loader = new UnityResourceAssetLoader(GetRequiredResourceModule());
            return _loader;
        }

        private async UniTask LoadRequiredPrefabAsync(
            IBattleViewAssetLoader loader,
            string assetKey,
            string address,
            ViewObjectCategory category)
        {
            IBattleAssetLease lease;
            try
            {
                lease = loader.LoadAsync<GameObject>(address);
                if (lease == null)
                {
                    throw new InvalidOperationException("资源加载接缝返回空租约");
                }

                _assetLeases.Add(lease);
                await UniTask.WaitUntil(() => lease.IsDone);
            }
            catch (BattlePresentationLoadException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BattlePresentationLoadException("load", address, ex);
            }

            if (!lease.IsValid)
            {
                throw new BattlePresentationLoadException(
                    "validate-handle",
                    address,
                    new InvalidOperationException("AssetHandle 无效"));
            }

            if (!(lease.AssetObject is GameObject prefab) || prefab == null)
            {
                throw new BattlePresentationLoadException(
                    "validate-asset",
                    address,
                    new InvalidCastException("AssetObject 不是 GameObject"));
            }

            ValidateInstantiation(address, category, prefab);

            _prefabs.Add(assetKey, prefab);
        }

        /// <summary>
        /// 实例化校验探针。按类别选择父节点，支持 Mob0..3 且不误分类 unit/projectile。
        /// </summary>
        private void ValidateInstantiation(string address, ViewObjectCategory category, GameObject prefab)
        {
            GameObject probe = null;
            try
            {
                Transform parent = category == ViewObjectCategory.Enemy
                    ? _bindings.EnemyRoot
                    : category == ViewObjectCategory.Projectile
                        ? _bindings.ProjectileRoot
                        : _bindings.SoldierRoot;
                probe = UnityEngine.Object.Instantiate(prefab, parent);
                if (probe == null)
                {
                    throw new InvalidOperationException("Instantiate 返回空对象");
                }

                probe.SetActive(false);
            }
            catch (Exception ex)
            {
                throw new BattlePresentationLoadException("instantiate-validate", address, ex);
            }
            finally
            {
                Destroy(probe);
            }
        }

        private GameObject SpawnInstance(
            int runtimeId,
            ViewObjectCategory category,
            string assetKey,
            Vector3 worldPosition,
            Transform parent)
        {
            if (!_prefabs.TryGetValue(assetKey, out GameObject prefab) || prefab == null)
            {
                throw new BattlePresentationLoadException(
                    "instantiate",
                    GetAddress(assetKey),
                    new InvalidOperationException("必需 Prefab 未预加载"));
            }

            if (_activeInstances.TryGetValue(runtimeId, out ActiveInstance existing))
            {
                ReturnToPool(existing.GameObject, existing.AssetKey, existing.Parent);
                _activeInstances.Remove(runtimeId);
            }

            try
            {
                GameObject instance = AcquireFromPool(assetKey, prefab, parent);
                ConfigureUnitAnimation(instance, assetKey);
                instance.transform.position = worldPosition;
                _activeInstances.Add(
                    runtimeId,
                    new ActiveInstance(instance, category, assetKey, parent));
                _registry?.Register(category, runtimeId, instance);
                return instance;
            }
            catch (BattlePresentationLoadException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BattlePresentationLoadException("instantiate", GetAddress(assetKey), ex);
            }
        }

        private void DespawnInstance(int runtimeId)
        {
            if (!_activeInstances.TryGetValue(runtimeId, out ActiveInstance active))
            {
                return;
            }

            // 敌人回池前复位血条（隐藏、满血），保证复用不残留旧显示状态。
            active.GameObject.GetComponent<EnemyHealthBarView>()?.ResetAndHide();

            _activeInstances.Remove(runtimeId);
            _registry?.Unregister(active.Category, runtimeId);
            ReturnToPool(active.GameObject, active.AssetKey, active.Parent);
        }

        private GameObject AcquireFromPool(string assetKey, GameObject prefab, Transform parent)
        {
            if (!_pools.TryGetValue(assetKey, out Stack<GameObject> pool))
            {
                pool = new Stack<GameObject>();
                _pools.Add(assetKey, pool);
            }

            GameObject instance = null;
            while (pool.Count > 0 && instance == null)
            {
                instance = pool.Pop();
            }

            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(prefab, parent);
            }
            else
            {
                instance.transform.SetParent(parent, false);
            }

            instance.SetActive(true);
            instance.GetComponent<SoldierSpriteAnimator>()?.ResetToIdle();
            instance.GetComponent<SpearWeaponView>()?.ResetView();
            instance.GetComponent<GeneralSpineAnimator>()?.ResetToIdle();

            return instance;
        }

        private void ReturnToPool(GameObject instance, string assetKey, Transform parent)
        {
            if (instance == null)
            {
                return;
            }

            if (!_pools.TryGetValue(assetKey, out Stack<GameObject> pool))
            {
                pool = new Stack<GameObject>();
                _pools.Add(assetKey, pool);
            }

            instance.transform.SetParent(parent, false);
            instance.GetComponent<SoldierSpriteAnimator>()?.ResetToIdle();
            instance.GetComponent<SpearWeaponView>()?.ResetView();
            instance.GetComponent<GeneralSpineAnimator>()?.ResetToIdle();
            instance.SetActive(false);
            pool.Push(instance);
        }

        private async UniTask LoadUnitAnimationsAsync(IBattleViewAssetLoader loader)
        {
            int[] idleCounts = { 7, 7, 8, 6 };
            int[] attackCounts = { 19, 30, 21, 19 };
            for (int soldierType = 0; soldierType < idleCounts.Length; soldierType++)
            {
                Sprite[] idleFrames = await LoadSpriteFramesAsync(
                    loader, soldierType, "skeleton-zhan", idleCounts[soldierType]);
                Sprite[] attackFrames = await LoadSpriteFramesAsync(
                    loader, soldierType, "skeleton-attack", attackCounts[soldierType]);
                _unitAnimations[soldierType] = new SoldierAnimationFrames(idleFrames, attackFrames);
            }
        }

        private async UniTask<Sprite[]> LoadSpriteFramesAsync(
            IBattleViewAssetLoader loader,
            int soldierType,
            string animationName,
            int frameCount)
        {
            var frames = new Sprite[frameCount];
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                string frameName = animationName == "skeleton-attack"
                    ? $"{animationName}_{frameIndex:D2}"
                    : $"{animationName}_{frameIndex}";
                string address =
                    $"Sprites/Extracted/GameObject/soldier/anim/soldier_{soldierType}/" +
                    frameName;

                IBattleAssetLease lease;
                try
                {
                    lease = loader.LoadAsync<Sprite>(address);
                    if (lease == null)
                    {
                        throw new InvalidOperationException("资源加载接缝返回空租约");
                    }

                    _assetLeases.Add(lease);
                    await UniTask.WaitUntil(() => lease.IsDone);
                }
                catch (BattlePresentationLoadException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new BattlePresentationLoadException("load-animation", address, ex);
                }

                if (!lease.IsValid || !(lease.AssetObject is Sprite sprite) || sprite == null)
                {
                    throw new BattlePresentationLoadException(
                        "validate-animation", address, new InvalidOperationException("序列帧 Sprite 无效"));
                }

                frames[frameIndex] = sprite;
            }

            return frames;
        }

        /// <summary>
        /// 预加载 1 至 8 的等级数字 Sprite（index = level - 1），每个数字独立单图。
        /// </summary>
        private async UniTask LoadLevelNumberSpritesAsync(IBattleViewAssetLoader loader)
        {
            var sprites = new Sprite[MaxLevelNumber];
            for (int index = 0; index < MaxLevelNumber; index++)
            {
                string address = LevelNumberAddressPrefix + (index + 1);

                IBattleAssetLease lease;
                try
                {
                    lease = loader.LoadAsync<Sprite>(address);
                    if (lease == null)
                    {
                        throw new InvalidOperationException("资源加载接缝返回空租约");
                    }

                    _assetLeases.Add(lease);
                    await UniTask.WaitUntil(() => lease.IsDone);
                }
                catch (BattlePresentationLoadException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new BattlePresentationLoadException("load-level-number", address, ex);
                }

                if (!lease.IsValid || !(lease.AssetObject is Sprite sprite) || sprite == null)
                {
                    throw new BattlePresentationLoadException(
                        "validate-level-number", address,
                        new InvalidOperationException("等级数字 Sprite 无效"));
                }

                sprites[index] = sprite;
            }

            _levelNumberSprites = sprites;
        }

        /// <summary>
        /// 预加载武将拆字字形贴图，按 partWord 建立查表字典。
        /// </summary>
        /// <remarks>复用 LoadLevelNumberSpritesAsync 的 lease 模式：LoadAsync 登记 _assetLeases，
        /// 对称释放由 ResetPreload 统一 Dispose。未知拆字（GetSpriteAddress 返回 null）跳过。</remarks>
        private async UniTask LoadGeneralPartSpritesAsync(
            IBattleViewAssetLoader loader,
            IReadOnlyList<string> partWords)
        {
            if (partWords == null)
            {
                return;
            }

            for (int index = 0; index < partWords.Count; index++)
            {
                string word = partWords[index];
                string address = GeneralPartGlyphMap.GetSpriteAddress(word);
                if (address == null)
                {
                    continue;
                }

                IBattleAssetLease lease;
                try
                {
                    lease = loader.LoadAsync<Sprite>(address);
                    if (lease == null)
                    {
                        throw new InvalidOperationException("资源加载接缝返回空租约");
                    }

                    _assetLeases.Add(lease);
                    await UniTask.WaitUntil(() => lease.IsDone);
                }
                catch (BattlePresentationLoadException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new BattlePresentationLoadException("load-general-part", address, ex);
                }

                if (!lease.IsValid || !(lease.AssetObject is Sprite sprite) || sprite == null)
                {
                    throw new BattlePresentationLoadException(
                        "validate-general-part", address,
                        new InvalidOperationException("武将字形 Sprite 无效"));
                }

                _generalPartSprites[word] = sprite;
            }
        }

        private void ConfigureUnitAnimation(GameObject instance, string assetKey)
        {
            int soldierType = assetKey == "unit_KnifeSoldier" ? 0
                : assetKey == "unit_BowSoldier" ? 1
                : assetKey == "unit_SpearSoldier" ? 2
                : assetKey == "unit_CavalrySoldier" ? 3
                : -1;
            if (soldierType < 0
                || !_unitAnimations.TryGetValue(soldierType, out SoldierAnimationFrames frames))
            {
                return;
            }

            SpriteRenderer renderer = instance.transform.Find("Body")?.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                throw new BattlePresentationLoadException(
                    "bind-animation", assetKey, new InvalidOperationException("士兵 Prefab 缺少 Body/SpriteRenderer"));
            }

            SoldierSpriteAnimator animator = instance.GetComponent<SoldierSpriteAnimator>();
            if (animator == null)
            {
                animator = instance.AddComponent<SoldierSpriteAnimator>();
            }

            animator.Configure(renderer, frames.IdleFrames, frames.AttackFrames);
        }

        // 武将单位挂 SkeletonAnimation 时绑定 Spine 待机/攻击动画；士兵 Prefab 无此组件则空操作。
        private void ConfigureGeneralSpine(GameObject instance, string idleKey)
        {
            SkeletonAnimation spine = instance.GetComponentInChildren<SkeletonAnimation>(true);
            if (spine == null)
            {
                return;
            }

            GeneralSpineAnimator animator = instance.GetComponent<GeneralSpineAnimator>();
            if (animator == null)
            {
                animator = instance.AddComponent<GeneralSpineAnimator>();
            }

            animator.Configure(spine, idleKey);
        }

        private void ResetPreload()
        {
            _levelNumberSprites = null;
            _generalPartSprites.Clear();

            // 对称释放至今取得的所有资源租约（生产租约内部 Release 对应 AssetHandle）。
            for (int index = 0; index < _assetLeases.Count; index++)
            {
                IBattleAssetLease lease = _assetLeases[index];
                if (lease != null)
                {
                    lease.Dispose();
                }
            }

            _assetLeases.Clear();
            _prefabs.Clear();
            _unitAnimations.Clear();
            _preloaded = false;
        }

        private string GetAddress(string assetKey)
        {
            if (assetKey == "projectile_arrow") return ArrowAddress;
            return assetKey.StartsWith("unit_", StringComparison.Ordinal)
                ? assetKey.Substring("unit_".Length)
                : assetKey;
        }

        private static void Destroy(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(gameObject);
                return;
            }

            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        /// <summary>预加载 Prefab 条目：池键 + YooAsset 地址 + 表现类别。</summary>
        private readonly struct PrefabLoadEntry
        {
            internal PrefabLoadEntry(string assetKey, string address, ViewObjectCategory category)
            {
                AssetKey = assetKey;
                Address = address;
                Category = category;
            }

            internal string AssetKey { get; }
            internal string Address { get; }
            internal ViewObjectCategory Category { get; }
        }

        private sealed class ActiveInstance
        {
            internal ActiveInstance(
                GameObject gameObject,
                ViewObjectCategory category,
                string assetKey,
                Transform parent)
            {
                GameObject = gameObject;
                Category = category;
                AssetKey = assetKey;
                Parent = parent;
            }

            internal GameObject GameObject { get; }
            internal ViewObjectCategory Category { get; }
            internal string AssetKey { get; }
            internal Transform Parent { get; }
        }

        private readonly struct SoldierAnimationFrames
        {
            internal SoldierAnimationFrames(Sprite[] idleFrames, Sprite[] attackFrames)
            {
                IdleFrames = idleFrames;
                AttackFrames = attackFrames;
            }

            internal Sprite[] IdleFrames { get; }
            internal Sprite[] AttackFrames { get; }
        }
    }

    /// <summary>
    /// 使用 SpriteRenderer 播放士兵待机循环与单次攻击序列。
    /// </summary>
    internal sealed class SoldierSpriteAnimator : MonoBehaviour
    {
        /// <summary>待机动画基准帧时长（秒，12fps）。</summary>
        private const float IdleFrameDuration = 1f / 12f;

        private SpriteRenderer _renderer;
        private Sprite[] _idleFrames;
        private Sprite[] _attackFrames;
        private float _elapsed;
        private int _frameIndex;
        private bool _isAttacking;
        private float _attackIntervalSeconds = 1f;
        private float _attackFrameDuration = IdleFrameDuration;

        internal void Configure(
            SpriteRenderer renderer,
            Sprite[] idleFrames,
            Sprite[] attackFrames)
        {
            _renderer = renderer;
            _idleFrames = idleFrames;
            _attackFrames = attackFrames;
            _attackIntervalSeconds = 1f;
            _attackFrameDuration = IdleFrameDuration;
            ResetToIdle();
        }

        /// <summary>
        /// 设置当前有效攻击间隔。下一次攻击会按攻击总帧数等分整个攻击周期。
        /// </summary>
        /// <param name="intervalSeconds">有效攻击间隔（秒）。</param>
        internal void SetAttackIntervalSeconds(float intervalSeconds)
        {
            _attackIntervalSeconds = intervalSeconds > 0f ? intervalSeconds : 1f;
        }

        internal void PlayAttack()
        {
            if (_renderer == null || _attackFrames == null || _attackFrames.Length == 0)
            {
                return;
            }

            _isAttacking = true;
            _elapsed = 0f;
            _frameIndex = 0;
            _attackFrameDuration = CalculateAttackFrameDuration(
                _attackIntervalSeconds, _attackFrames.Length);
            _renderer.sprite = _attackFrames[0];
        }

        internal void ResetToIdle()
        {
            _isAttacking = false;
            _elapsed = 0f;
            _frameIndex = 0;
            if (_renderer != null && _idleFrames != null && _idleFrames.Length > 0)
            {
                _renderer.sprite = _idleFrames[0];
            }
        }

        private void Update()
        {
            Sprite[] frames = _isAttacking ? _attackFrames : _idleFrames;
            if (_renderer == null || frames == null || frames.Length == 0)
            {
                return;
            }

            // 待机保持 12fps；攻击动画将一个有效攻击间隔均分给全部攻击帧。
            // 攻击开始时已缓存本轮帧时长，运行时攻速变化从下一次攻击立即生效。
            float frameDuration = _isAttacking ? _attackFrameDuration : IdleFrameDuration;
            _elapsed += Time.deltaTime;
            while (_elapsed >= frameDuration)
            {
                _elapsed -= frameDuration;
                _frameIndex++;
                if (_frameIndex >= frames.Length)
                {
                    if (_isAttacking)
                    {
                        ResetToIdle();
                        return;
                    }

                    _frameIndex = 0;
                }

                _renderer.sprite = frames[_frameIndex];
            }
        }

        /// <summary>按完整攻击周期计算攻击动画逐帧时长。</summary>
        internal static float CalculateAttackFrameDuration(float intervalSeconds, int frameCount)
        {
            float effectiveInterval = intervalSeconds > 0f ? intervalSeconds : 1f;
            int effectiveFrameCount = frameCount > 0 ? frameCount : 1;
            return effectiveInterval / effectiveFrameCount;
        }
    }

    // ============================================================================
    // 资源加载接缝（task 5.4/5.5）：把 YooAsset AssetHandle 生命周期抽象为可注入租约
    // ----------------------------------------------------------------------------
    // 生产实现（UnityResourceAssetLoader + YooAssetBattleAssetLease）沿用现有
    // IResourceModule.LoadAssetAsyncHandle<T> / AssetHandle.Release 语义；
    // EditMode 测试注入替身驱动预加载成功/失败并验证句柄对称释放与可重试。
    // 不修改 GameModule.Resource 实现（contract 7）。
    // ============================================================================

    /// <summary>
    /// 资源加载租约：屏蔽 YooAsset <see cref="AssetHandle"/> 细节的只读资源租约。
    /// </summary>
    /// <remarks>
    /// <para>由 <see cref="IBattleViewAssetLoader"/> 创建，端口在预加载时登记并统一
    /// 于 <see cref="IDisposable.Dispose"/> 释放（生产租约内部调用
    /// <c>AssetHandle.Release()</c>）。测试可注入替身验证对称释放。</para>
    /// </remarks>
    internal interface IBattleAssetLease : IDisposable
    {
        /// <summary>是否加载完毕。</summary>
        bool IsDone { get; }

        /// <summary>句柄是否有效。</summary>
        bool IsValid { get; }

        /// <summary>加载到的资源对象（类型由调用方断言）。</summary>
        object AssetObject { get; }
    }

    /// <summary>
    /// 战斗表现资源加载接缝：按地址创建异步加载租约。
    /// </summary>
    /// <remarks>
    /// <para>生产实现 <see cref="UnityResourceAssetLoader"/> 经
    /// <c>IResourceModule.LoadAssetAsyncHandle&lt;T&gt;</c> 创建
    /// <see cref="YooAssetBattleAssetLease"/>；测试注入替身即可控制预加载结果。</para>
    /// </remarks>
    internal interface IBattleViewAssetLoader
    {
        /// <summary>按地址创建异步加载租约（不等待完成）。</summary>
        /// <typeparam name="T">资源类型（GameObject/Sprite）。</typeparam>
        /// <param name="location">YooAsset location。</param>
        /// <returns>资源租约；加载器内部失败时抛出
        /// <see cref="BattlePresentationLoadException"/> 或返回 null（由端口显式失败）。</returns>
        IBattleAssetLease LoadAsync<T>(string location) where T : UnityEngine.Object;
    }

    /// <summary>
    /// 生产资源加载器：封装 <see cref="IResourceModule"/> 的异步句柄创建。
    /// </summary>
    internal sealed class UnityResourceAssetLoader : IBattleViewAssetLoader
    {
        private readonly IResourceModule _resource;

        internal UnityResourceAssetLoader(IResourceModule resource)
        {
            _resource = resource ?? throw new ArgumentNullException(nameof(resource));
        }

        /// <inheritdoc/>
        public IBattleAssetLease LoadAsync<T>(string location) where T : UnityEngine.Object
        {
            AssetHandle handle = _resource.LoadAssetAsyncHandle<T>(location);
            if (handle == null)
            {
                throw new BattlePresentationLoadException(
                    "load", location, new InvalidOperationException("资源模块返回空 AssetHandle"));
            }

            return new YooAssetBattleAssetLease(handle);
        }
    }

    /// <summary>
    /// YooAsset <see cref="AssetHandle"/> 的生产租约实现：Dispose 时对称 Release。
    /// </summary>
    internal sealed class YooAssetBattleAssetLease : IBattleAssetLease
    {
        private AssetHandle _handle;

        internal YooAssetBattleAssetLease(AssetHandle handle)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        /// <inheritdoc/>
        public bool IsDone => _handle == null || _handle.IsDone;

        /// <inheritdoc/>
        public bool IsValid => _handle != null && _handle.IsValid;

        /// <inheritdoc/>
        public object AssetObject => _handle?.AssetObject;

        /// <inheritdoc/>
        /// <remarks>释放底层 AssetHandle（若仍有效）；幂等。</remarks>
        public void Dispose()
        {
            if (_handle != null && _handle.IsValid)
            {
                _handle.Release();
            }

            _handle = null;
        }
    }
}
