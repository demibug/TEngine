using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
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
        private const string Mob0Address = "Mob0";
        private const string ArrowAddress = "Arrow";

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
        private readonly List<AssetHandle> _assetHandles = new List<AssetHandle>();
        private readonly Dictionary<string, Stack<GameObject>> _pools =
            new Dictionary<string, Stack<GameObject>>();
        private readonly Dictionary<int, ActiveInstance> _activeInstances =
            new Dictionary<int, ActiveInstance>();

        private bool _preloaded;
        private BattleViewRegistry _registry;
        private SpriteRenderer[] _playerHealthPoints;
        private SpriteRenderer[] _opponentHealthPoints;

        internal UnityBattleViewPort(BattleMapBindings bindings)
        {
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
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
        public async UniTask PreloadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_preloaded)
            {
                return;
            }

            var requiredPrefabs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("enemy_mob0", Mob0Address),
                new KeyValuePair<string, string>("unit_KnifeSoldier", "KnifeSoldier"),
                new KeyValuePair<string, string>("unit_BowSoldier", "BowSoldier"),
                new KeyValuePair<string, string>("unit_SpearSoldier", "SpearSoldier"),
                new KeyValuePair<string, string>("unit_CavalrySoldier", "CavalrySoldier"),
                new KeyValuePair<string, string>("projectile_arrow", ArrowAddress),
            };

            try
            {
                IResourceModule resource = GetRequiredResourceModule();
                for (int index = 0; index < requiredPrefabs.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    KeyValuePair<string, string> requiredPrefab = requiredPrefabs[index];
                    await LoadRequiredPrefabAsync(resource, requiredPrefab.Key, requiredPrefab.Value, cancellationToken);
                }

                await LoadUnitAnimationsAsync(resource, cancellationToken);

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

        public void OnEnemySpawned(int runtimeId, bool isPlayerLane, float logicX, float logicY)
        {
            GameObject instance = SpawnInstance(
                runtimeId,
                ViewObjectCategory.Enemy,
                "enemy_mob0",
                _bindings.LogicToWorld(logicX, logicY),
                _bindings.EnemyRoot);

            // 出生时血条保持隐藏、复位到满血（池复用不残留旧显示状态）。
            EnemyHealthBarView healthBar = instance.GetComponent<EnemyHealthBarView>();
            if (healthBar == null)
            {
                healthBar = instance.AddComponent<EnemyHealthBarView>();
                Transform bg = instance.transform.Find("VisualRoot/hpBgImg");
                Transform fill = instance.transform.Find("VisualRoot/hpBgImg/hpImg1");
                if (bg != null && fill != null)
                {
                    healthBar.Bind(
                        bg.GetComponent<SpriteRenderer>(),
                        fill.GetComponent<SpriteRenderer>());
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

        private async UniTask LoadRequiredPrefabAsync(
            IResourceModule resource,
            string assetKey,
            string address,
            CancellationToken cancellationToken)
        {
            AssetHandle handle;
            try
            {
                handle = resource.LoadAssetAsyncHandle<GameObject>(address);
                if (handle == null)
                {
                    throw new InvalidOperationException("资源模块返回空 AssetHandle");
                }

                _assetHandles.Add(handle);
                await UniTask.WaitUntil(() => handle.IsDone, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BattlePresentationLoadException("load", address, ex);
            }

            if (!handle.IsValid)
            {
                throw new BattlePresentationLoadException(
                    "validate-handle",
                    address,
                    new InvalidOperationException("AssetHandle 无效"));
            }

            if (!(handle.AssetObject is GameObject prefab) || prefab == null)
            {
                throw new BattlePresentationLoadException(
                    "validate-asset",
                    address,
                    new InvalidCastException("AssetObject 不是 GameObject"));
            }

            ValidateInstantiation(assetKey, address, prefab);

            _prefabs.Add(assetKey, prefab);
        }

        private void ValidateInstantiation(string assetKey, string address, GameObject prefab)
        {
            GameObject probe = null;
            try
            {
                Transform parent = assetKey == "enemy_mob0"
                    ? _bindings.EnemyRoot
                    : assetKey == "projectile_arrow"
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

            // 恢复士兵根节点默认朝向（弓兵攻击可能翻转 localScale.x），避免复用残留翻转。
            Vector3 instanceScale = instance.transform.localScale;
            instanceScale.x = Mathf.Abs(instanceScale.x);
            instance.transform.localScale = instanceScale;
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
            IResourceModule resource,
            CancellationToken cancellationToken)
        {
            int[] idleCounts = { 7, 7, 8, 6 };
            int[] attackCounts = { 19, 30, 21, 19 };
            for (int soldierType = 0; soldierType < idleCounts.Length; soldierType++)
            {
                Sprite[] idleFrames = await LoadSpriteFramesAsync(
                    resource, soldierType, "skeleton-zhan", idleCounts[soldierType], cancellationToken);
                Sprite[] attackFrames = await LoadSpriteFramesAsync(
                    resource, soldierType, "skeleton-attack", attackCounts[soldierType], cancellationToken);
                _unitAnimations[soldierType] = new SoldierAnimationFrames(idleFrames, attackFrames);
            }
        }

        private async UniTask<Sprite[]> LoadSpriteFramesAsync(
            IResourceModule resource,
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
                AssetHandle handle = resource.LoadAssetAsyncHandle<Sprite>(address);
                if (handle == null)
                {
                    throw new BattlePresentationLoadException(
                        "load-animation", address, new InvalidOperationException("资源模块返回空 AssetHandle"));
                }

                _assetHandles.Add(handle);
                await UniTask.WaitUntil(() => handle.IsDone, cancellationToken: cancellationToken);
                if (!handle.IsValid || !(handle.AssetObject is Sprite sprite) || sprite == null)
                {
                    throw new BattlePresentationLoadException(
                        "validate-animation", address, new InvalidOperationException("序列帧 Sprite 无效"));
                }

                frames[frameIndex] = sprite;
            }

            return frames;
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
            for (int index = 0; index < _assetHandles.Count; index++)
            {
                AssetHandle handle = _assetHandles[index];
                if (handle != null && handle.IsValid)
                {
                    handle.Release();
                }
            }

            _assetHandles.Clear();
            _prefabs.Clear();
            _unitAnimations.Clear();
            _preloaded = false;
        }

        private string GetAddress(string assetKey)
        {
            if (assetKey == "enemy_mob0") return Mob0Address;
            if (assetKey == "projectile_arrow") return ArrowAddress;
            return assetKey.StartsWith("unit_", StringComparison.Ordinal)
                ? assetKey.Substring("unit_".Length)
                : assetKey;
        }

        private static void Destroy(GameObject gameObject)
        {
            if (gameObject != null)
            {
                UnityEngine.Object.Destroy(gameObject);
            }
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
}
