using System;
using System.Collections.Generic;
using UnityEngine;

namespace TEngine.FairyGUIIntegration
{
    public enum FguiAssetKind
    {
        TextAsset,
        Texture2D,
        AudioClip,
        Font
    }

    [Serializable]
    public sealed class FguiCatalogAsset
    {
        [SerializeField] private string lookupKey;
        [SerializeField] private string address;
        [SerializeField] private FguiAssetKind kind;

        public string LookupKey => lookupKey;
        public string Address => address;
        public FguiAssetKind Kind => kind;

#if UNITY_EDITOR
        public void EditorSet(string newLookupKey, string newAddress, FguiAssetKind newKind)
        {
            lookupKey = newLookupKey;
            address = newAddress;
            kind = newKind;
        }
#endif
    }

    [Serializable]
    public sealed class FguiCatalogPackage
    {
        [SerializeField] private string key;
        [SerializeField] private string packageId;
        [SerializeField] private string packageName;
        [SerializeField] private string yooAssetPackageName = "DefaultPackage";
        [SerializeField] private string descriptionAddress;
        [SerializeField] private string assetNamePrefix;
        [SerializeField] private List<string> dependencies = new List<string>();
        [SerializeField] private List<FguiCatalogAsset> assets = new List<FguiCatalogAsset>();

        public string Key => key;
        public string PackageId => packageId;
        public string PackageName => packageName;
        public string YooAssetPackageName => yooAssetPackageName;
        public string DescriptionAddress => descriptionAddress;
        public string AssetNamePrefix => assetNamePrefix;
        public IReadOnlyList<string> Dependencies => dependencies;
        public IReadOnlyList<FguiCatalogAsset> Assets => assets;

#if UNITY_EDITOR
        public void EditorSet(string newKey, string newPackageId, string newPackageName,
            string newYooAssetPackageName, string newDescriptionAddress, string newAssetNamePrefix,
            List<string> newDependencies, List<FguiCatalogAsset> newAssets)
        {
            key = newKey;
            packageId = newPackageId;
            packageName = newPackageName;
            yooAssetPackageName = newYooAssetPackageName;
            descriptionAddress = newDescriptionAddress;
            assetNamePrefix = newAssetNamePrefix;
            dependencies = newDependencies ?? new List<string>();
            assets = newAssets ?? new List<FguiCatalogAsset>();
        }
#endif
    }

    [Serializable]
    public sealed class FguiExternalAsset
    {
        [SerializeField] private string key;
        [SerializeField] private string address;
        [SerializeField] private string yooAssetPackageName = "DefaultPackage";
        [SerializeField] private FguiAssetKind kind = FguiAssetKind.Texture2D;

        public string Key => key;
        public string Address => address;
        public string YooAssetPackageName => yooAssetPackageName;
        public FguiAssetKind Kind => kind;
    }

    [CreateAssetMenu(fileName = "FguiPackageCatalog", menuName = "TEngine/FairyGUI/Package Catalog")]
    public sealed class FguiPackageCatalog : ScriptableObject
    {
        [SerializeField] private List<FguiCatalogPackage> packages = new List<FguiCatalogPackage>();
        [SerializeField] private List<FguiExternalAsset> externalAssets = new List<FguiExternalAsset>();

        private Dictionary<string, FguiCatalogPackage> _byKey;
        private Dictionary<string, FguiExternalAsset> _externalByKey;

        public IReadOnlyList<FguiCatalogPackage> Packages => packages;
        public IReadOnlyList<FguiExternalAsset> ExternalAssets => externalAssets;

        public FguiCatalogPackage GetPackage(string key)
        {
            EnsureIndex();
            if (!_byKey.TryGetValue(key, out FguiCatalogPackage package))
                throw new KeyNotFoundException($"FairyGUI catalog package '{key}' was not found.");
            return package;
        }

        public bool TryGetExternal(string key, out FguiExternalAsset asset)
        {
            EnsureIndex();
            return _externalByKey.TryGetValue(key, out asset);
        }

        public List<string> ValidateCatalog()
        {
            var errors = new List<string>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var byKey = new Dictionary<string, FguiCatalogPackage>(StringComparer.Ordinal);

            foreach (FguiCatalogPackage package in packages)
            {
                if (package == null || string.IsNullOrWhiteSpace(package.Key))
                {
                    errors.Add("A package has an empty catalog key.");
                    continue;
                }

                if (!keys.Add(package.Key))
                    errors.Add($"Duplicate package key '{package.Key}'.");
                else
                    byKey.Add(package.Key, package);

                if (string.IsNullOrWhiteSpace(package.PackageId) || !ids.Add(package.PackageId))
                    errors.Add($"Package '{package.Key}' has an empty or duplicate FairyGUI id '{package.PackageId}'.");
                if (string.IsNullOrWhiteSpace(package.PackageName) || !names.Add(package.PackageName))
                    errors.Add($"Package '{package.Key}' has an empty or duplicate FairyGUI name '{package.PackageName}'.");
                if (string.IsNullOrWhiteSpace(package.DescriptionAddress))
                    errors.Add($"Package '{package.Key}' has no description address.");
                if (string.IsNullOrWhiteSpace(package.AssetNamePrefix))
                    errors.Add($"Package '{package.Key}' has no published asset name prefix.");
                if (string.IsNullOrWhiteSpace(package.YooAssetPackageName))
                    errors.Add($"Package '{package.Key}' has no YooAsset package name.");

                var lookupKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (FguiCatalogAsset asset in package.Assets)
                {
                    if (asset == null || string.IsNullOrWhiteSpace(asset.LookupKey) ||
                        string.IsNullOrWhiteSpace(asset.Address))
                        errors.Add($"Package '{package.Key}' contains an incomplete asset mapping.");
                    else if (!lookupKeys.Add(asset.LookupKey))
                        errors.Add($"Package '{package.Key}' has duplicate asset lookup key '{asset.LookupKey}'.");
                }
            }

            foreach (FguiCatalogPackage package in packages)
            {
                if (package == null || string.IsNullOrWhiteSpace(package.Key))
                    continue;
                foreach (string dependency in package.Dependencies)
                {
                    if (!byKey.ContainsKey(dependency))
                        errors.Add($"Package '{package.Key}' depends on missing package '{dependency}'.");
                }
            }

            var externalKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (FguiExternalAsset asset in externalAssets)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.Key) ||
                    string.IsNullOrWhiteSpace(asset.Address) || string.IsNullOrWhiteSpace(asset.YooAssetPackageName))
                {
                    errors.Add("The external FairyGUI asset catalog contains an incomplete entry.");
                    continue;
                }

                if (!externalKeys.Add(asset.Key))
                    errors.Add($"Duplicate external FairyGUI asset key '{asset.Key}'.");
            }

            DetectCycles(byKey, errors);
            return errors;
        }

        private static void DetectCycles(Dictionary<string, FguiCatalogPackage> byKey, List<string> errors)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var path = new List<string>();
            foreach (string key in byKey.Keys)
                Visit(key, byKey, visiting, visited, path, errors);
        }

        private static void Visit(string key, Dictionary<string, FguiCatalogPackage> byKey,
            HashSet<string> visiting, HashSet<string> visited, List<string> path, List<string> errors)
        {
            if (visited.Contains(key))
                return;
            if (!visiting.Add(key))
            {
                int start = path.IndexOf(key);
                var cycle = start >= 0 ? path.GetRange(start, path.Count - start) : new List<string> { key };
                cycle.Add(key);
                errors.Add("FairyGUI package dependency cycle: " + string.Join(" -> ", cycle));
                return;
            }

            path.Add(key);
            if (byKey.TryGetValue(key, out FguiCatalogPackage package))
            {
                foreach (string dependency in package.Dependencies)
                {
                    if (byKey.ContainsKey(dependency))
                        Visit(dependency, byKey, visiting, visited, path, errors);
                }
            }
            path.RemoveAt(path.Count - 1);
            visiting.Remove(key);
            visited.Add(key);
        }

        private void EnsureIndex()
        {
            if (_byKey != null)
                return;

            List<string> errors = ValidateCatalog();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));

            _byKey = new Dictionary<string, FguiCatalogPackage>(StringComparer.Ordinal);
            foreach (FguiCatalogPackage package in packages)
                _byKey.Add(package.Key, package);

            _externalByKey = new Dictionary<string, FguiExternalAsset>(StringComparer.Ordinal);
            foreach (FguiExternalAsset asset in externalAssets)
            {
                if (asset != null && !string.IsNullOrWhiteSpace(asset.Key))
                    _externalByKey.Add(asset.Key, asset);
            }
        }

        private void OnValidate()
        {
            _byKey = null;
            _externalByKey = null;
        }

#if UNITY_EDITOR
        public void EditorReplacePackages(List<FguiCatalogPackage> value)
        {
            packages = value ?? new List<FguiCatalogPackage>();
            _byKey = null;
        }
#endif
    }
}
