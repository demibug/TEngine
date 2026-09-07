using System;
using System.Collections.Generic;
using System.IO;
using FairyGUI;
using UnityEditor;
using UnityEngine;

namespace TEngine.FairyGUIIntegration.Editor
{
    public static class FguiCatalogTools
    {
        private const string CatalogPath = "Assets/AssetRaw/FGUI/FguiPackageCatalog.asset";
        private const string SettingsPath = "Assets/AssetRaw/FGUI/FguiSettings.asset";
        private const string PackagesPath = "Assets/AssetRaw/FGUI/Packages";
        private const string AssetRawPrefix = "Assets/AssetRaw/";

        [MenuItem("TEngine/FairyGUI/Rebuild Package Catalog")]
        public static void RebuildCatalog()
        {
            FguiPackageCatalog catalog = AssetDatabase.LoadAssetAtPath<FguiPackageCatalog>(CatalogPath);
            if (catalog == null)
                throw new InvalidOperationException($"Catalog asset not found at '{CatalogPath}'.");

            var preservedDependencies = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var preservedKeysById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (FguiCatalogPackage existing in catalog.Packages)
            {
                preservedDependencies[existing.Key] = new List<string>(existing.Dependencies);
                preservedKeysById[existing.PackageId] = existing.Key;
            }

            string[] descriptionGuids = AssetDatabase.FindAssets("t:TextAsset", new[] { PackagesPath });
            var descriptions = new List<string>();
            foreach (string guid in descriptionGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("_fui.bytes", StringComparison.OrdinalIgnoreCase))
                    descriptions.Add(path);
            }
            descriptions.Sort(StringComparer.Ordinal);

            var results = new List<FguiCatalogPackage>();
            var identityToKey = new Dictionary<string, string>(StringComparer.Ordinal);
            var descriptorDependencies = new Dictionary<string, Dictionary<string, string>[]>(StringComparer.Ordinal);

            foreach (string descriptionPath in descriptions)
            {
                string directory = Path.GetDirectoryName(descriptionPath)?.Replace('\\', '/');
                string prefix = Path.GetFileNameWithoutExtension(descriptionPath);
                prefix = prefix.Substring(0, prefix.Length - "_fui".Length);
                TextAsset description = AssetDatabase.LoadAssetAtPath<TextAsset>(descriptionPath);
                UIPackage package = null;
                try
                {
                    package = UIPackage.AddPackage(description.bytes, prefix,
                        (string name, string extension, Type type, out DestroyMethod destroyMethod) =>
                        {
                            destroyMethod = DestroyMethod.None;
                            return AssetDatabase.LoadAssetAtPath(directory + "/" + name + extension, type);
                        });
                    if (package == null)
                        throw new InvalidOperationException($"Could not parse '{descriptionPath}'.");

                    string key = preservedKeysById.TryGetValue(package.id, out string preservedKey)
                        ? preservedKey
                        : prefix;
                    identityToKey[package.id] = key;
                    identityToKey[package.name] = key;
                    descriptorDependencies[key] = package.dependencies;

                    var assets = new List<FguiCatalogAsset>();
                    foreach (string file in Directory.GetFiles(directory))
                    {
                        string assetPath = file.Replace('\\', '/');
                        if (assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
                            assetPath == descriptionPath)
                            continue;
                        FguiAssetKind kind = DetermineKind(assetPath);
                        var asset = new FguiCatalogAsset();
                        asset.EditorSet(Path.GetFileName(assetPath), assetPath.Substring(AssetRawPrefix.Length), kind);
                        assets.Add(asset);
                    }

                    preservedDependencies.TryGetValue(key, out List<string> dependencies);
                    var entry = new FguiCatalogPackage();
                    entry.EditorSet(key, package.id, package.name, "DefaultPackage",
                        descriptionPath.Substring(AssetRawPrefix.Length), prefix,
                        dependencies ?? new List<string>(), assets);
                    results.Add(entry);
                }
                finally
                {
                    if (package != null)
                        UIPackage.RemovePackage(package.id);
                }
            }

            foreach (FguiCatalogPackage package in results)
            {
                var dependencies = new List<string>(package.Dependencies);
                if (descriptorDependencies.TryGetValue(package.Key, out Dictionary<string, string>[] declared) &&
                    declared != null)
                {
                    foreach (Dictionary<string, string> dependency in declared)
                    {
                        string identity = dependency.TryGetValue("id", out string id) ? id : dependency["name"];
                        if (!identityToKey.TryGetValue(identity, out string dependencyKey))
                            throw new InvalidOperationException(
                                $"Package '{package.Key}' depends on unpublished descriptor '{identity}'.");
                        if (!dependencies.Contains(dependencyKey))
                            dependencies.Add(dependencyKey);
                    }
                }
                package.EditorSet(package.Key, package.PackageId, package.PackageName, package.YooAssetPackageName,
                    package.DescriptionAddress, package.AssetNamePrefix, dependencies,
                    new List<FguiCatalogAsset>(package.Assets));
            }

            catalog.EditorReplacePackages(results);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            ValidateCatalog();
        }

        [MenuItem("TEngine/FairyGUI/Validate Package Catalog")]
        public static void ValidateCatalog()
        {
            FguiPackageCatalog catalog = AssetDatabase.LoadAssetAtPath<FguiPackageCatalog>(CatalogPath);
            if (catalog == null)
                throw new InvalidOperationException($"Catalog asset not found at '{CatalogPath}'.");
            List<string> errors = catalog.ValidateCatalog();
            FguiSettings settings = AssetDatabase.LoadAssetAtPath<FguiSettings>(SettingsPath);
            if (settings == null)
                errors.Add($"FairyGUI settings asset not found at '{SettingsPath}'.");
            else
            {
                if (settings.Catalog != catalog)
                    errors.Add("FairyGUI settings does not reference the validated package catalog.");
                if (LayerMask.NameToLayer(settings.RenderLayerName) < 0)
                    errors.Add($"FairyGUI render layer '{settings.RenderLayerName}' is missing from TagManager.");
                if (string.Equals(settings.RenderLayerName, "UI", StringComparison.Ordinal))
                    errors.Add("FairyGUI must not share the existing UGUI 'UI' render layer.");
            }
            foreach (FguiCatalogPackage package in catalog.Packages)
            {
                CheckAsset(package.DescriptionAddress, typeof(TextAsset), errors);
                foreach (FguiCatalogAsset asset in package.Assets)
                    CheckAsset(asset.Address, KindToType(asset.Kind), errors);
            }
            foreach (FguiExternalAsset asset in catalog.ExternalAssets)
                CheckAsset(asset.Address, KindToType(asset.Kind), errors);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            Debug.Log($"FairyGUI catalog is valid: {catalog.Packages.Count} packages, " +
                      $"{catalog.ExternalAssets.Count} external assets.");
        }

        private static void CheckAsset(string address, Type type, List<string> errors)
        {
            string path = AssetRawPrefix + address;
            if (AssetDatabase.LoadAssetAtPath(path, type) == null)
                errors.Add($"Catalog address '{address}' does not resolve to {type.Name} at '{path}'.");
        }

        private static FguiAssetKind DetermineKind(string path)
        {
            Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (typeof(Texture).IsAssignableFrom(type)) return FguiAssetKind.Texture2D;
            if (typeof(AudioClip).IsAssignableFrom(type)) return FguiAssetKind.AudioClip;
            if (typeof(Font).IsAssignableFrom(type)) return FguiAssetKind.Font;
            return FguiAssetKind.TextAsset;
        }

        private static Type KindToType(FguiAssetKind kind)
        {
            switch (kind)
            {
                case FguiAssetKind.Texture2D: return typeof(Texture2D);
                case FguiAssetKind.AudioClip: return typeof(AudioClip);
                case FguiAssetKind.Font: return typeof(Font);
                default: return typeof(TextAsset);
            }
        }
    }
}
