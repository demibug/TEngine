using System;
using System.Collections.Generic;
using System.Threading;
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

        /// <summary>
        /// 资源加载接缝。生产默认由 <see cref="GetOrCreateLoader"/> 惰性创建
        /// <see cref="UnityResourceAssetLoader"/>；测试注入替身以驱动成功/失败/取消与句柄释放。
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
        /// 加载本端口的全部必需 Prefab。失败时释放本端口已取得的所有句柄。
        /// </summary>
        /// <param name="enemyResourceAddresses">
        /// 本局所选计划解析后会使用的去重普通敌人资源地址；只为本局实际引用的敌人
        /// 加载 Prefab 并建立表现池，不再固定预加载 Mob0。
        /// </param>
        /// <param name="cancellationToken">取消令牌。</param>
        public async UniTask PreloadAsync(
            IReadOnlyList<string> enemyResourceAddresses,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            requiredPrefabs.Add(new PrefabLoadEntry(
                "projectile_arrow", ArrowAddress, ViewObjectCategory.Projectile));

            try
            {
                for (int index = 0; index < requiredPrefabs.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PrefabLoadEntry entry = requiredPrefabs[index];
                    await LoadRequiredPrefabAsync(
                        loader, entry.AssetKey, entry.Address, entry.Category, cancellationToken);
                }

                await LoadUnitAnimationsAsync(loader, cancellationToken);
                await LoadLevelNumberSpritesAsync(loader, cancellationToken);

                _preloaded = true;
            }
            catch (OperationCanceledException)
            {
                ResetPreload();
                throw;
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
                            $"Boss '{dto.BossKey}' Prefab 缺少可初始化的 SkeletonAnimation"));
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
            if (!_unitAddresses.TryGetValue(soldierType, out string address))
            {
                throw new BattlePresentationLoadException(
                    "instantiate",
                    $"soldierType:{soldierType}",
                    new ArgumentOutOfRangeException(nameof(soldierType)));
            }

            SpawnInstance(
                runtimeId,
                ViewObjectCategory.Unit,
                $"unit_{address}",
                _bindings.UnitCellToWorld(gridX, gridY),
                _bindings.SoldierRoot);

            // 战场单位附加等级数字表现（修复 P0：首次上场显示真实等级，不再固定 1）。
            if (_activeInstances.TryGetValue(runtimeId, out ActiveInstance placed))
            {
                SoldierLevelBadge badge = placed.GameObject.GetComponent<SoldierLevelBadge>();
                if (badge == null)
                {
                    badge = placed.GameObject.AddComponent<SoldierLevelBadge>();
                }

                badge.Configure(_levelNumberSprites);
                badge.SetLevel(level > 0 ? level : 1);
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
            ViewObjectCategory category,
            CancellationToken cancellationToken)
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
                await UniTask.WaitUntil(() => lease.IsDone, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
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
            instance.SetActive(false);
            pool.Push(instance);
        }

        private async UniTask LoadUnitAnimationsAsync(
            IBattleViewAssetLoader loader,
            CancellationToken cancellationToken)
        {
            int[] idleCounts = { 7, 7, 8, 6 };
            int[] attackCounts = { 19, 30, 21, 19 };
            for (int soldierType = 0; soldierType < idleCounts.Length; soldierType++)
            {
                Sprite[] idleFrames = await LoadSpriteFramesAsync(
                    loader, soldierType, "skeleton-zhan", idleCounts[soldierType], cancellationToken);
                Sprite[] attackFrames = await LoadSpriteFramesAsync(
                    loader, soldierType, "skeleton-attack", attackCounts[soldierType], cancellationToken);
                _unitAnimations[soldierType] = new SoldierAnimationFrames(idleFrames, attackFrames);
            }
        }

        private async UniTask<Sprite[]> LoadSpriteFramesAsync(
            IBattleViewAssetLoader loader,
            int soldierType,
            string animationName,
            int frameCount,
            CancellationToken cancellationToken)
        {
            var frames = new Sprite[frameCount];
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    await UniTask.WaitUntil(() => lease.IsDone, cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
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
        private async UniTask LoadLevelNumberSpritesAsync(
            IBattleViewAssetLoader loader,
            CancellationToken cancellationToken)
        {
            var sprites = new Sprite[MaxLevelNumber];
            for (int index = 0; index < MaxLevelNumber; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    await UniTask.WaitUntil(() => lease.IsDone, cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
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

        private void ResetPreload()
        {
            _levelNumberSprites = null;

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
    // EditMode 测试注入替身驱动预加载成功/失败/取消并验证句柄对称释放与可重试。
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
