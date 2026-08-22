using System;
using System.IO;
using System.Linq;
using Spine;
using UnityEditor;
using UnityEngine;
using Spine.Unity;
using Spine.Unity.Editor;

/// <summary>
/// 将 SourceAssets 中已转换为 Spine 4.3 的 ZhangLiang 素材接入生产资源：
/// 复制到 AssetRaw/Battle 收集目录（atlas 重命名 .atlas.txt 触发 spine-unity
/// 自动生成 SkeletonDataAsset/SpineAtlasAsset），构建含血条结构的 ZhangLiang
/// Prefab，并输出应回填 boss.xlsx 的 YooAsset 地址。
/// </summary>
public static class ZhangLiangBossPresentationBuilder
{
    private const string SourceDir = "Assets/SourceAssets/SpineConversion/Boss/ZhangLiang/boss0";
    private const string TargetDir = "Assets/AssetRaw/Battle/Prefabs/Enemies/ZhangLiang";
    private const string PrefabPath = "Assets/AssetRaw/Battle/Prefabs/Enemies/ZhangLiang.prefab";
    private const string Mob0PrefabPath = "Assets/AssetRaw/Battle/Prefabs/Enemies/Mob0.prefab";
    private const string HealthBarParentName = "hpBgImg";
    private const string ProductionSpineName = "zhangliang";

    /// <summary>生成 boss Prefab 并打印回填地址。</summary>
    [MenuItem("Tools/GameBattle/Generate ZhangLiang Boss Prefab")]
    public static void Generate()
    {
        try
        {
            RemoveLegacySpineAssets();
            CopySpineSourcesToCollectDir();
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(TargetDir, ImportAssetOptions.ForceUpdate);

            SkeletonDataAsset skeletonData = LoadGeneratedSkeletonData();
            if (skeletonData == null)
            {
                throw new InvalidOperationException("spine-unity 未生成 zhangliang_SkeletonData.asset，请检查 json/atlas.txt 兼容性");
            }

            GameObject prefab = BuildPrefab(skeletonData);
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            UnityEngine.Object.DestroyImmediate(prefab);

            string address = Path.GetFileNameWithoutExtension(PrefabPath);
            Debug.Log($"[ZhangLiangBossPresentationBuilder] 生成完成: {PrefabPath}\n" +
                      $"boss.xlsx 的 ZhangLiang.resourcePath 应回填为: {address}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ZhangLiangBossPresentationBuilder] 生成失败: {ex}");
        }
    }

    /// <summary>把 json/atlas/png 复制到收集目录，atlas 重命名为 .atlas.txt。</summary>
    private static void CopySpineSourcesToCollectDir()
    {
        if (!AssetDatabase.IsValidFolder(TargetDir))
        {
            EnsureFolders(TargetDir);
        }

        CopyAssetFile("skeleton.json", ProductionSpineName + ".json");
        CopyAtlasFile("skeleton.atlas", ProductionSpineName + ".atlas.txt");
        CopyAssetFile("skeleton.png", ProductionSpineName + ".png");
    }

    private static void RemoveLegacySpineAssets()
    {
        string[] legacyFiles =
        {
            "skeleton.json",
            "skeleton.atlas.txt",
            "skeleton.png",
            "skeleton_Atlas.asset",
            "skeleton_Material.mat",
            "skeleton_SkeletonData.asset",
        };

        foreach (string file in legacyFiles)
        {
            AssetDatabase.DeleteAsset(TargetDir + "/" + file);
        }
    }

    private static void CopyAssetFile(string sourceFile, string targetFile)
    {
        string sourcePath = Path.Combine(SourceDir, sourceFile);
        string targetPath = Path.Combine(TargetDir, targetFile);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"源素材缺失: {sourcePath}");
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static void CopyAtlasFile(string sourceFile, string targetFile)
    {
        string sourcePath = Path.Combine(SourceDir, sourceFile);
        string targetPath = Path.Combine(TargetDir, targetFile);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"源素材缺失: {sourcePath}");
        }

        string atlas = File.ReadAllText(sourcePath);
        atlas = atlas.Replace("skeleton.png", ProductionSpineName + ".png", StringComparison.Ordinal);
        File.WriteAllText(targetPath, atlas);
    }

    /// <summary>读取 spine-unity 自动生成的 SkeletonDataAsset。</summary>
    private static SkeletonDataAsset LoadGeneratedSkeletonData()
    {
        string assetPath = TargetDir + "/" + ProductionSpineName + "_SkeletonData.asset";
        return AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(assetPath);
    }

    /// <summary>
    /// 以 Mob0 的血条结构为基础构建 boss Prefab：root 挂 SkeletonAnimation，
    /// 保留 VisualRoot/hpBgImg 血条，移除非血条子节点。
    /// </summary>
    private static GameObject BuildPrefab(SkeletonDataAsset skeletonData)
    {
        GameObject mob0 = AssetDatabase.LoadAssetAtPath<GameObject>(Mob0PrefabPath);
        if (mob0 == null)
        {
            throw new InvalidOperationException($"缺少血条模板: {Mob0PrefabPath}");
        }

        GameObject root = UnityEngine.Object.Instantiate(mob0);
        root.name = "ZhangLiang";

        Transform visualRoot = root.transform.Find("VisualRoot");
        if (visualRoot == null)
        {
            throw new InvalidOperationException("Mob0.prefab 缺少 VisualRoot 节点");
        }

        foreach (Transform child in visualRoot.Cast<Transform>().ToArray())
        {
            if (child.name != HealthBarParentName)
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        // Use Spine's editor factory. It creates SkeletonRenderer and
        // SkeletonAnimation together, assigns the data asset first, and only
        // then initializes the animation component.
        SkeletonAnimation animation = EditorInstantiation.InstantiateSkeletonAnimation(
            skeletonData,
            destroyInvalid: true,
            useObjectFactory: true);
        if (animation == null)
        {
            throw new InvalidOperationException("Spine Editor 工厂未能创建 SkeletonAnimation");
        }

        GameObject spineGo = animation.gameObject;
        spineGo.name = "Skeleton";
        spineGo.transform.SetParent(root.transform, false);

        SkeletonData data = skeletonData.GetSkeletonData(true);
        if (data != null)
        {
            string[] required = { "attackliang", "goliang" };
            string[] missing = required.Where(anim => !data.Animations.Any(a => a.Name == anim)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException($"SkeletonData 缺少动画: {string.Join(", ", missing)}");
            }
        }

        return root;
    }

    private static void EnsureFolders(string targetDir)
    {
        string current = "Assets";
        foreach (string segment in targetDir.Split('/').Skip(1))
        {
            string next = current + "/" + segment;
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segment);
            }

            current = next;
        }
    }
}
