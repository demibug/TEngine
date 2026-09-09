using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TEngine
{
    [Serializable]
    public struct AssetsRefInfo
    {
#if UNITY_6000_1_OR_NEWER
        public readonly EntityId instanceID;
#else
        public readonly int instanceID;
#endif

        public Object refAsset;

        public AssetsRefInfo(Object refAsset)
        {
            this.refAsset = refAsset;
#if UNITY_6000_1_OR_NEWER
            instanceID = refAsset.GetEntityId();
#else
            instanceID = refAsset.GetInstanceID();
#endif
        }
    }

    /// <summary>
    /// 实例携带的源资产引用。
    /// <remarks>每次绑定代表一份持有：GameObject 绑定对应 sourceGameObject，资产绑定对应一条 AssetsRefInfo。
    /// 两种绑定统一登记原始实例；销毁时由绑定时保存的 owner 模块配对归还。
    /// 清理前先摘除所持记录，保证重复清理不重复归还；克隆（含未激活克隆）不继承原实例持有权。</remarks>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AssetsReference : MonoBehaviour
    {
        [SerializeField]
        private GameObject sourceGameObject;

        [SerializeField]
        private List<AssetsRefInfo> refAssetInfoList;

        /// <summary>
        /// 绑定时解析并保存的 owner 模块；清理时不重新获取或创建模块。
        /// </summary>
        private IResourceModule _ownerModule;

        /// <summary>
        /// 是否已执行过清理，防止重复归还。
        /// </summary>
        private bool _released;

        /// <summary>
        /// 绑定时登记的实例对象。
        /// <remarks>不能只依赖 gameObject 属性：实例销毁后 Unity 对象会变成伪 null，仍需用原引用移除登记。</remarks>
        /// </summary>
        [NonSerialized]
        private GameObject _registeredGameObject;

        /// <summary>
        /// 已登记的原始实例（key 为实例 GameObject）。
        /// <remarks>克隆（含未激活克隆）不在此表中，其销毁绝不触发归还。</remarks>
        /// </summary>
        private static readonly Dictionary<GameObject, AssetsReference> _originalRefs = new();

        /// <summary>
        /// 需要由资源模块轮询的实例。
        /// <remarks>Unity 不保证从未激活过的对象调用 OnDestroy，因此绑定后额外保留一份追踪，
        /// 由 ResourceModule.Update 检测伪 null 并执行一次性归还。</remarks>
        /// </summary>
        private static readonly HashSet<AssetsReference> _trackedReferences = new();

        private static readonly List<AssetsReference> _destroyedReferences = new();

        /// <summary>
        /// 新会话开始时清除旧 Unity 会话留下的静态索引。旧对象后续收到 OnDestroy 时只会
        /// 走克隆/未登记路径，不会向新会话的资源模块归还引用。
        /// </summary>
        internal static void ResetForNewSession()
        {
            _originalRefs.Clear();
            _trackedReferences.Clear();
            _destroyedReferences.Clear();
        }

        /// <summary>
        /// 在资源对象池仍有效时，主动释放某个 owner 的全部原始实例引用。
        /// </summary>
        internal static void ReleaseOwnedReferences(IResourceModule ownerModule)
        {
            if (ownerModule == null || _trackedReferences.Count <= 0)
            {
                return;
            }

            _destroyedReferences.Clear();
            foreach (AssetsReference reference in _trackedReferences)
            {
                if (!ReferenceEquals(reference, null) && ReferenceEquals(reference._ownerModule, ownerModule) && !reference._released)
                {
                    _destroyedReferences.Add(reference);
                }
            }

            foreach (AssetsReference reference in _destroyedReferences)
            {
                // 先摘除集合，再执行 owner 回调；回调重入或 Unity 延迟 OnDestroy 都不能重复归还。
                _trackedReferences.Remove(reference);
                try
                {
                    reference.ReleaseInternal();
                }
                catch (Exception exception)
                {
                    LogErrorSafely($"AssetsReference owner release failed: {exception}");
                }
            }

            _destroyedReferences.Clear();
        }

        private bool IsOriginalInstance()
        {
            GameObject key = _registeredGameObject;
            if (ReferenceEquals(key, null))
            {
                key = gameObject;
            }

            return !ReferenceEquals(key, null) &&
                   _originalRefs.TryGetValue(key, out var originalComponent) &&
                   ReferenceEquals(originalComponent, this);
        }

        private void RegisterOriginalInstance()
        {
            _registeredGameObject = gameObject;
            _originalRefs[_registeredGameObject] = this;
            _trackedReferences.Add(this);
        }

        private void RemoveOriginalRegistration()
        {
            GameObject key = _registeredGameObject;
            if (!ReferenceEquals(key, null) &&
                _originalRefs.TryGetValue(key, out var registered) &&
                ReferenceEquals(registered, this))
            {
                _originalRefs.Remove(key);
            }

            _trackedReferences.Remove(this);
            _registeredGameObject = null;
        }

        /// <summary>
        /// 清理从未激活即被销毁的已交付实例。
        /// <remarks>必须在主线程调用；释放前先摘除登记，避免随后补发 OnDestroy 时重复归还。</remarks>
        /// </summary>
        internal static void ReleaseDestroyedReferences()
        {
            if (_trackedReferences.Count <= 0)
            {
                return;
            }

            _destroyedReferences.Clear();
            foreach (AssetsReference reference in _trackedReferences)
            {
                // ReferenceEquals 区分真正的 managed null；Unity 的 == 负责检测 native 对象已销毁。
                if (!ReferenceEquals(reference, null) && reference == null)
                {
                    _destroyedReferences.Add(reference);
                }
            }

            foreach (AssetsReference reference in _destroyedReferences)
            {
                // 即使 ReleaseInternal 内部记录异常，也不能让同一组件在下一帧重复尝试归还。
                _trackedReferences.Remove(reference);
                try
                {
                    reference.ReleaseInternal();
                }
                catch (Exception ex)
                {
                    // 销毁对象上的 Unity 属性访问也可能抛错；登记已先摘除，不能阻塞其他实例收尾。
                    LogErrorSafely($"AssetsReference destroyed-instance release failed: {ex.Message}");
                }
            }

            _destroyedReferences.Clear();
        }

        private void Awake()
        {
            // If it is a clone, clear the reference records before cloning
            if (!IsOriginalInstance())
            {
                DetachCloneCopiedRecords();
            }
        }

        /// <summary>
        /// 摘除克隆拷贝的引用记录（不归还）。
        /// <remarks>克隆不继承原实例的持有权；拷贝来的字段一律摘除，防止双重归还。</remarks>
        /// </summary>
        private void DetachCloneCopiedRecords()
        {
            sourceGameObject = null;
            refAssetInfoList = null;
            _ownerModule = null;
            _released = true;
            RemoveOriginalRegistration();
        }

        private void OnDestroy()
        {
            // 只有登记在册的原始实例才执行归还；克隆或未绑定实例仅摘除拷贝记录。
            if (!IsOriginalInstance())
            {
                DetachCloneCopiedRecords();
                return;
            }

            ReleaseInternal();
        }

        /// <summary>
        /// 归还所持有的全部引用。
        /// <remarks>先摘除记录再归还：重复清理不再归还；某一条归还异常仍尝试其余条目，并记录异常。</remarks>
        /// </summary>
        private void ReleaseInternal()
        {
            if (_released)
            {
                return;
            }

            _released = true;

            try
            {
                RemoveOriginalRegistration();
            }
            catch (Exception exception)
            {
                // Unity may have invalidated the native object before OnDestroy. The owner
                // release must still continue even if removing the diagnostic index fails.
                LogErrorSafely($"AssetsReference registration cleanup failed: {exception.Message}");
            }

            IResourceModule ownerModule = _ownerModule;
            _ownerModule = null;
            if (ownerModule == null)
            {
                LogWarningSafely("AssetsReference has no owner module; skip releasing references.");
                DetachRecords();
                return;
            }

            // 摘除 sourceGameObject 记录后再归还，防止重入路径重复归还。
            GameObject source = sourceGameObject;
            sourceGameObject = null;
            if (!ReferenceEquals(source, null))
            {
                try
                {
                    ownerModule.UnloadAsset(source);
                }
                catch (Exception ex)
                {
                    LogErrorSafely($"AssetsReference release source failed: {ex.Message}");
                }
            }

            // 摘除列表后再逐条归还；每条合法登记代表一份持有，不按对象去重。
            List<AssetsRefInfo> list = refAssetInfoList;
            refAssetInfoList = null;
            if (list != null)
            {
                foreach (var refInfo in list)
                {
                    Object asset = refInfo.refAsset;
                    if (ReferenceEquals(asset, null))
                    {
                        continue;
                    }

                    try
                    {
                        ownerModule.UnloadAsset(asset);
                    }
                    catch (Exception ex)
                    {
                        LogErrorSafely($"AssetsReference release '{asset.GetType().FullName}' failed: {ex.Message}");
                    }
                }

                list.Clear();
            }
        }

        private void DetachRecords()
        {
            sourceGameObject = null;
            refAssetInfoList = null;
            _registeredGameObject = null;
            _ownerModule = null;
        }

        /// <summary>
        /// 摘除全部持有记录且不归还。
        /// <remarks>交付前取消等场景的一次性手动补偿前置步骤：调用方随后销毁实例并自行归还 spawn，
        /// OnDestroy 不再重复归还。</remarks>
        /// </summary>
        internal void DetachWithoutRelease()
        {
            _released = true;
            sourceGameObject = null;
            refAssetInfoList = null;
            RemoveOriginalRegistration();
        }

        /// <summary>
        /// 释放 owner 持有并销毁实例。
        /// <remarks>绑定已成功后的失败/取消路径使用此方法；由组件 owner 归还一次，OnDestroy 只会看到已摘除记录。</remarks>
        /// </summary>
        internal void ReleaseAndDestroy()
        {
            GameObject instance = gameObject;
            try
            {
                ReleaseInternal();
            }
            finally
            {
                if (instance != null)
                {
                    Object.Destroy(instance);
                }
            }
        }

        /// <summary>
        /// 绑定源 GameObject 持有。
        /// </summary>
        public AssetsReference Ref(GameObject source, IResourceModule resourceModule = null)
        {
            // 未激活克隆等场景：Awake 可能尚未运行，任何绑定尝试都先摘除复制残留。
            if (!IsOriginalInstance())
            {
                DetachCloneCopiedRecords();
            }

            if (source == null)
            {
                throw new GameFrameworkException($"Source gameObject is null.");
            }

            if (source.scene.name != null)
            {
                throw new GameFrameworkException($"Source gameObject is in scene.");
            }

            // 绑定时解析并保存 owner；清理时不重新获取或创建模块。
            _ownerModule = resourceModule ?? ModuleSystem.GetModule<IResourceModule>();
            if (_ownerModule == null)
            {
                throw new GameFrameworkException($"resourceModule is null.");
            }

            if (!ModuleSystem.IsRunning || (_ownerModule is ResourceModule concreteOwner && !concreteOwner.IsAcceptingRequests))
            {
                _ownerModule = null;
                throw new GameFrameworkException("Resource module is shutting down and cannot accept a new reference.");
            }

            sourceGameObject = source;
            _released = false;

            // 统一登记原始实例（无论 GameObject 绑定还是资产绑定），保证销毁路径可达。
            RegisterOriginalInstance();

            return this;
        }

        /// <summary>
        /// 绑定源资产持有。
        /// </summary>
        public AssetsReference Ref<T>(T source, IResourceModule resourceModule = null) where T : Object
        {
            // 未激活克隆等场景：Awake 可能尚未运行，任何绑定尝试都先摘除复制残留。
            if (!IsOriginalInstance())
            {
                DetachCloneCopiedRecords();
            }

            if (source == null)
            {
                throw new GameFrameworkException($"Source gameObject is null.");
            }

            _ownerModule = resourceModule ?? ModuleSystem.GetModule<IResourceModule>();
            if (_ownerModule == null)
            {
                throw new GameFrameworkException($"resourceModule is null.");
            }

            if (!ModuleSystem.IsRunning || (_ownerModule is ResourceModule concreteOwner && !concreteOwner.IsAcceptingRequests))
            {
                _ownerModule = null;
                throw new GameFrameworkException("Resource module is shutting down and cannot accept a new reference.");
            }

            if (refAssetInfoList == null)
            {
                refAssetInfoList = new List<AssetsRefInfo>();
            }

            _released = false;
            refAssetInfoList.Add(new AssetsRefInfo(source));

            // 统一登记原始实例（无论 GameObject 绑定还是资产绑定），保证销毁路径可达。
            RegisterOriginalInstance();

            return this;
        }

        /// <summary>
        /// 实例化源资产并绑定引用。
        /// </summary>
        internal static AssetsReference Instantiate(GameObject source, Transform parent = null, IResourceModule resourceModule = null)
        {
            if (!ModuleSystem.IsRunning)
            {
                throw new GameFrameworkException("The module system is shutting down and cannot instantiate a resource.");
            }

            if (source == null)
            {
                throw new GameFrameworkException($"Source gameObject is null.");
            }

            if (source.scene.name != null)
            {
                throw new GameFrameworkException($"Source gameObject is in scene.");
            }

            GameObject instance = Object.Instantiate(source, parent);
            try
            {
                AssetsReference component = instance.GetComponent<AssetsReference>();
                if (component == null)
                {
                    component = instance.AddComponent<AssetsReference>();
                }

                return component.Ref(source, resourceModule);
            }
            catch
            {
                // 绑定失败：销毁已创建的未交付实例（组件未绑定成功，不存在 OnDestroy 归还）。
                UnityEngine.Object.Destroy(instance);
                throw;
            }
        }

        public static AssetsReference Ref(GameObject source, GameObject instance, IResourceModule resourceModule = null)
        {
            if (!ModuleSystem.IsRunning)
            {
                throw new GameFrameworkException("The module system is shutting down and cannot register a resource reference.");
            }

            if (source == null)
            {
                throw new GameFrameworkException($"Source gameObject is null.");
            }

            if (source.scene.name != null)
            {
                throw new GameFrameworkException($"Source gameObject is in scene.");
            }

            var comp = instance.GetComponent<AssetsReference>();
            return comp ? comp.Ref(source, resourceModule) : instance.AddComponent<AssetsReference>().Ref(source, resourceModule);
        }

        public static AssetsReference Ref<T>(T source, GameObject instance, IResourceModule resourceModule = null) where T : Object
        {
            if (!ModuleSystem.IsRunning)
            {
                throw new GameFrameworkException("The module system is shutting down and cannot register a resource reference.");
            }

            if (source == null)
            {
                throw new GameFrameworkException($"Source gameObject is null.");
            }

            var comp = instance.GetComponent<AssetsReference>();
            return comp ? comp.Ref(source, resourceModule) : instance.AddComponent<AssetsReference>().Ref(source, resourceModule);
        }

        private static void LogErrorSafely(string message)
        {
            try { Log.Error(message); }
            catch { }
        }

        private static void LogWarningSafely(string message)
        {
            try { Log.Warning(message); }
            catch { }
        }
    }
}
